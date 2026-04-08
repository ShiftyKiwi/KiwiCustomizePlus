// Copyright (c) Customize+.
// Licensed under the MIT license.

using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using Dalamud.Plugin.Services;
using OtterGui.Log;
using System;
using System.Collections.Generic;

namespace CustomizePlus.Interop.Ipc;

internal enum GlamourerStateChangeType
{
    Model = 0,
    EntireCustomize = 1,
    Customize = 2,
    Equip = 3,
    Weapon = 4,
    Stains = 5,
    Crest = 6,
    Parameter = 7,
    MaterialValue = 8,
    Design = 9,
    Reset = 10,
    Other = 11,
    Reapply = 12,
    BonusItem = 13,
}

internal enum GlamourerStateFinalizationType
{
    ModelChange = 0,
    DesignApplied = 1,
    Revert = 2,
    RevertCustomize = 3,
    RevertEquipment = 4,
    RevertAdvanced = 5,
    RevertAutomation = 6,
    Reapply = 7,
    ReapplyAutomation = 8,
    Gearset = 9,
}

internal readonly record struct GlamourerAppearanceTransitionSnapshot(
    bool Active,
    bool AwaitingFinalization,
    bool FinalizationSettling,
    string Summary)
{
    public static GlamourerAppearanceTransitionSnapshot None
        => new(false, false, false, string.Empty);
}

public sealed class GlamourerIpcHandler : IIpcSubscriber
{
    private const string ApiVersionLabel = "Glamourer.ApiVersion.V2";
    private const string InitializedLabel = "Glamourer.Initialized";
    private const string DisposedLabel = "Glamourer.Disposed";
    private const string StateChangedWithTypeLabel = "Penumbra.StateChangedWithType";
    private const string StateFinalizedLabel = "Penumbra.StateFinalized";

    private const int AppearanceChangeWindowMs = 3200;
    private const int DesignOrModelChangeWindowMs = 4800;
    private const int FinalizationSettleWindowMs = 1800;
    private const int FinalizationModelSettleWindowMs = 2600;
    private const int TransitionCleanupGraceMs = 12000;

    private readonly Logger _log;
    private readonly IObjectTable _objectTable;
    private readonly ICallGateSubscriber<(int Major, int Minor)>? _apiVersion;
    private readonly ICallGateSubscriber<object?>? _initialized;
    private readonly ICallGateSubscriber<object?>? _disposed;
    private readonly ICallGateSubscriber<nint, int, object?>? _stateChangedWithType;
    private readonly ICallGateSubscriber<nint, int, object?>? _stateFinalized;
    private readonly object _transitionLock = new();
    private readonly Dictionary<GlamourerActorKey, ActorAppearanceTransitionState> _actorTransitions = new();

    private bool _available;
    private bool _stateSubscriptionsEnabled;
    private int _currentMajor;
    private int _currentMinor;

    private readonly record struct GlamourerActorKey(ushort? ObjectIndex, nint Address)
    {
        public static GlamourerActorKey FromObjectIndex(ushort objectIndex)
            => new(objectIndex, nint.Zero);

        public static GlamourerActorKey FromAddress(nint address)
            => new(null, address);
    }

    private sealed class ActorAppearanceTransitionState
    {
        public int ChangeSequence { get; set; }
        public int FinalizedSequence { get; set; }
        public long LastChangeAtMs { get; set; }
        public long LastFinalizedAtMs { get; set; }
        public long PendingUntilMs { get; set; }
        public long FinalizationSettleUntilMs { get; set; }
        public GlamourerStateChangeType LastChangeType { get; set; }
        public GlamourerStateFinalizationType LastFinalizationType { get; set; }
    }

    public GlamourerIpcHandler(IDalamudPluginInterface pi, IObjectTable objectTable, Logger log)
    {
        _objectTable = objectTable;
        _log = log;
        _apiVersion = TryCreate(label => pi.GetIpcSubscriber<(int Major, int Minor)>(label), ApiVersionLabel);
        _initialized = TryCreate(label => pi.GetIpcSubscriber<object?>(label), InitializedLabel);
        _disposed = TryCreate(label => pi.GetIpcSubscriber<object?>(label), DisposedLabel);
        _stateChangedWithType = TryCreate(label => pi.GetIpcSubscriber<nint, int, object?>(label), StateChangedWithTypeLabel);
        _stateFinalized = TryCreate(label => pi.GetIpcSubscriber<nint, int, object?>(label), StateFinalizedLabel);

        try
        {
            _initialized?.Subscribe(OnInitialized);
            _disposed?.Subscribe(OnDisposed);
        }
        catch (Exception ex)
        {
            _log.Verbose($"Unable to subscribe to Glamourer lifecycle IPC.\n\t{ex}");
        }

        Initialize();
    }

    public bool Available => _available;

    public bool CheckApiVersion()
    {
        if (_apiVersion == null)
            return false;

        try
        {
            var (major, minor) = _apiVersion.InvokeFunc();
            _currentMajor = major;
            _currentMinor = minor;
            return major >= 0;
        }
        catch
        {
            _currentMajor = 0;
            _currentMinor = 0;
            return false;
        }
    }

    public void Initialize()
    {
        Disable();

        if (!CheckApiVersion())
            return;

        try
        {
            _stateChangedWithType?.Subscribe(OnStateChangedWithType);
            _stateFinalized?.Subscribe(OnStateFinalized);
            _stateSubscriptionsEnabled = true;
            _available = true;
            _log.Verbose($"Glamourer IPC initialized. Version {_currentMajor}.{_currentMinor}.");
        }
        catch (Exception ex)
        {
            _stateSubscriptionsEnabled = false;
            _available = false;
            _log.Verbose($"Unable to subscribe to Glamourer state IPC.\n\t{ex}");
        }
    }

    public void Disable()
    {
        if (_stateSubscriptionsEnabled)
        {
            try
            {
                _stateChangedWithType?.Unsubscribe(OnStateChangedWithType);
                _stateFinalized?.Unsubscribe(OnStateFinalized);
            }
            catch (Exception ex)
            {
                _log.Verbose($"Unable to unsubscribe from Glamourer state IPC cleanly.\n\t{ex}");
            }
        }

        _stateSubscriptionsEnabled = false;
        _available = false;

        lock (_transitionLock)
        {
            _actorTransitions.Clear();
        }
    }

    public void Dispose()
    {
        Disable();

        try
        {
            _initialized?.Unsubscribe(OnInitialized);
            _disposed?.Unsubscribe(OnDisposed);
        }
        catch (Exception ex)
        {
            _log.Verbose($"Unable to unsubscribe from Glamourer lifecycle IPC cleanly.\n\t{ex}");
        }
    }

    internal bool TryGetAppearanceTransitionSnapshot(ushort objectIndex, nint actorAddress, out GlamourerAppearanceTransitionSnapshot snapshot)
    {
        snapshot = GlamourerAppearanceTransitionSnapshot.None;
        if (!_available || (objectIndex == ushort.MaxValue && actorAddress == nint.Zero))
            return false;

        var now = Environment.TickCount64;
        lock (_transitionLock)
        {
            CleanupExpiredTransitions(now);

            if (!TryGetTransitionState(objectIndex, actorAddress, out var state, out var awaitingFinalization, out var finalizationSettling))
                return false;

            snapshot = awaitingFinalization
                ? new GlamourerAppearanceTransitionSnapshot(
                    true,
                    true,
                    false,
                    BuildAwaitingFinalizationSummary(state, now))
                : new GlamourerAppearanceTransitionSnapshot(
                    true,
                    false,
                    true,
                    BuildFinalizationSettleSummary(state, now));

            return true;
        }
    }

    private void OnInitialized()
        => Initialize();

    private void OnDisposed()
        => Disable();

    private void OnStateChangedWithType(nint actorAddress, int rawType)
    {
        if (!_available || actorAddress == nint.Zero)
            return;

        var type = (GlamourerStateChangeType)rawType;
        if (!ShouldTrackChangeType(type))
            return;

        var now = Environment.TickCount64;
        lock (_transitionLock)
        {
            CleanupExpiredTransitions(now);

            var actorKey = ResolveActorKey(actorAddress);
            var state = TryTakeTransitionState(actorKey, actorAddress) ?? new ActorAppearanceTransitionState();

            state.ChangeSequence++;
            state.FinalizedSequence = Math.Min(state.FinalizedSequence, state.ChangeSequence - 1);
            state.LastChangeAtMs = now;
            state.PendingUntilMs = now + GetAppearanceChangeWindowMs(type);
            state.FinalizationSettleUntilMs = 0;
            state.LastChangeType = type;

            _actorTransitions[actorKey] = state;
        }
    }

    private void OnStateFinalized(nint actorAddress, int rawType)
    {
        if (!_available || actorAddress == nint.Zero)
            return;

        var type = (GlamourerStateFinalizationType)rawType;
        if (!ShouldTrackFinalizationType(type))
            return;

        var now = Environment.TickCount64;
        lock (_transitionLock)
        {
            CleanupExpiredTransitions(now);

            var actorKey = ResolveActorKey(actorAddress);
            var state = TryTakeTransitionState(actorKey, actorAddress) ?? new ActorAppearanceTransitionState
            {
                ChangeSequence = 1,
            };

            state.FinalizedSequence = Math.Max(state.ChangeSequence, 1);
            state.LastFinalizedAtMs = now;
            state.LastFinalizationType = type;
            state.PendingUntilMs = Math.Max(state.PendingUntilMs, now + 250);
            state.FinalizationSettleUntilMs = now + GetFinalizationSettleWindowMs(type);

            _actorTransitions[actorKey] = state;
        }
    }

    private static bool ShouldTrackChangeType(GlamourerStateChangeType type)
        => type switch
        {
            GlamourerStateChangeType.Model => true,
            GlamourerStateChangeType.EntireCustomize => true,
            GlamourerStateChangeType.Customize => true,
            GlamourerStateChangeType.Equip => true,
            GlamourerStateChangeType.Weapon => true,
            GlamourerStateChangeType.Design => true,
            GlamourerStateChangeType.Reset => true,
            GlamourerStateChangeType.Reapply => true,
            GlamourerStateChangeType.BonusItem => true,
            _ => false,
        };

    private static bool ShouldTrackFinalizationType(GlamourerStateFinalizationType type)
        => type switch
        {
            GlamourerStateFinalizationType.ModelChange => true,
            GlamourerStateFinalizationType.DesignApplied => true,
            GlamourerStateFinalizationType.Revert => true,
            GlamourerStateFinalizationType.RevertCustomize => true,
            GlamourerStateFinalizationType.RevertEquipment => true,
            GlamourerStateFinalizationType.RevertAdvanced => true,
            GlamourerStateFinalizationType.RevertAutomation => true,
            GlamourerStateFinalizationType.Reapply => true,
            GlamourerStateFinalizationType.ReapplyAutomation => true,
            GlamourerStateFinalizationType.Gearset => true,
            _ => false,
        };

    private static int GetAppearanceChangeWindowMs(GlamourerStateChangeType type)
        => type switch
        {
            GlamourerStateChangeType.Model => DesignOrModelChangeWindowMs,
            GlamourerStateChangeType.Design => DesignOrModelChangeWindowMs,
            GlamourerStateChangeType.Reset => DesignOrModelChangeWindowMs,
            GlamourerStateChangeType.Reapply => DesignOrModelChangeWindowMs,
            _ => AppearanceChangeWindowMs,
        };

    private static int GetFinalizationSettleWindowMs(GlamourerStateFinalizationType type)
        => type switch
        {
            GlamourerStateFinalizationType.ModelChange => FinalizationModelSettleWindowMs,
            GlamourerStateFinalizationType.DesignApplied => FinalizationModelSettleWindowMs,
            GlamourerStateFinalizationType.Gearset => FinalizationModelSettleWindowMs,
            _ => FinalizationSettleWindowMs,
        };

    private void CleanupExpiredTransitions(long now)
    {
        if (_actorTransitions.Count == 0)
            return;

        var expiredActors = new List<GlamourerActorKey>();
        foreach (var (actorKey, state) in _actorTransitions)
        {
            var expiry = Math.Max(state.PendingUntilMs, state.FinalizationSettleUntilMs) + TransitionCleanupGraceMs;
            if (now >= expiry)
                expiredActors.Add(actorKey);
        }

        foreach (var actorKey in expiredActors)
            _actorTransitions.Remove(actorKey);
    }

    private bool TryGetTransitionState(
        ushort objectIndex,
        nint actorAddress,
        out ActorAppearanceTransitionState state,
        out bool awaitingFinalization,
        out bool finalizationSettling)
    {
        state = null!;
        awaitingFinalization = false;
        finalizationSettling = false;

        if (objectIndex != ushort.MaxValue)
        {
            var objectIndexKey = GlamourerActorKey.FromObjectIndex(objectIndex);
            if (_actorTransitions.TryGetValue(objectIndexKey, out var objectIndexState) &&
                TryValidateTransitionState(objectIndexKey, objectIndexState, out awaitingFinalization, out finalizationSettling))
            {
                state = objectIndexState;
                return true;
            }
        }

        if (actorAddress == nint.Zero)
            return false;

        var addressKey = GlamourerActorKey.FromAddress(actorAddress);
        if (!_actorTransitions.TryGetValue(addressKey, out var addressState) ||
            !TryValidateTransitionState(addressKey, addressState, out awaitingFinalization, out finalizationSettling))
        {
            return false;
        }

        state = addressState;

        if (objectIndex != ushort.MaxValue)
        {
            _actorTransitions[GlamourerActorKey.FromObjectIndex(objectIndex)] = state;
            _actorTransitions.Remove(addressKey);
        }
        return true;
    }

    private bool TryValidateTransitionState(
        GlamourerActorKey actorKey,
        ActorAppearanceTransitionState state,
        out bool awaitingFinalization,
        out bool finalizationSettling)
    {
        var now = Environment.TickCount64;
        awaitingFinalization = state.FinalizedSequence < state.ChangeSequence && now < state.PendingUntilMs;
        finalizationSettling = state.FinalizedSequence == state.ChangeSequence && state.ChangeSequence > 0 && now < state.FinalizationSettleUntilMs;
        if (awaitingFinalization || finalizationSettling)
            return true;

        _actorTransitions.Remove(actorKey);
        return false;
    }

    private ActorAppearanceTransitionState? TryTakeTransitionState(GlamourerActorKey actorKey, nint actorAddress)
    {
        if (_actorTransitions.Remove(actorKey, out var existing))
            return existing;

        if (actorKey.ObjectIndex.HasValue && actorAddress != nint.Zero)
        {
            var addressKey = GlamourerActorKey.FromAddress(actorAddress);
            if (_actorTransitions.Remove(addressKey, out existing))
                return existing;
        }

        return null;
    }

    private GlamourerActorKey ResolveActorKey(nint actorAddress)
    {
        if (TryResolveObjectIndex(actorAddress, out var objectIndex))
            return GlamourerActorKey.FromObjectIndex(objectIndex);

        return GlamourerActorKey.FromAddress(actorAddress);
    }

    private bool TryResolveObjectIndex(nint actorAddress, out ushort objectIndex)
    {
        objectIndex = ushort.MaxValue;
        if (actorAddress == nint.Zero)
            return false;

        try
        {
            var reference = _objectTable.CreateObjectReference(actorAddress);
            if (reference != null && reference.Address == actorAddress)
            {
                objectIndex = (ushort)reference.ObjectIndex;
                return true;
            }
        }
        catch
        {
            // Fall back to a direct table scan if the transient object reference could not be built.
        }

        for (var i = 0; i < _objectTable.Length; ++i)
        {
            if (_objectTable.GetObjectAddress(i) != actorAddress)
                continue;

            objectIndex = (ushort)i;
            return true;
        }

        return false;
    }

    private static string BuildAwaitingFinalizationSummary(ActorAppearanceTransitionState state, long now)
    {
        var remainingMs = Math.Max(0L, state.PendingUntilMs - now);
        return $"Glamourer is still finalizing a {GetChangeTypeLabel(state.LastChangeType)} appearance update for this actor, so Kiwi is holding BIW signature promotion until the post-apply slot winners settle (~{remainingMs} ms remaining).";
    }

    private static string BuildFinalizationSettleSummary(ActorAppearanceTransitionState state, long now)
    {
        var remainingMs = Math.Max(0L, state.FinalizationSettleUntilMs - now);
        return $"Glamourer finalized a {GetFinalizationTypeLabel(state.LastFinalizationType)} appearance update for this actor, so Kiwi is giving the final resolved model winners a short settle window before refreshing BIW (~{remainingMs} ms remaining).";
    }

    private static string GetChangeTypeLabel(GlamourerStateChangeType type)
        => type switch
        {
            GlamourerStateChangeType.Model => "model",
            GlamourerStateChangeType.EntireCustomize => "full customize",
            GlamourerStateChangeType.Customize => "customize",
            GlamourerStateChangeType.Equip => "equipment",
            GlamourerStateChangeType.Weapon => "weapon",
            GlamourerStateChangeType.Design => "design",
            GlamourerStateChangeType.Reset => "reset",
            GlamourerStateChangeType.Reapply => "reapply",
            GlamourerStateChangeType.BonusItem => "bonus item",
            _ => "appearance",
        };

    private static string GetFinalizationTypeLabel(GlamourerStateFinalizationType type)
        => type switch
        {
            GlamourerStateFinalizationType.ModelChange => "model change",
            GlamourerStateFinalizationType.DesignApplied => "design application",
            GlamourerStateFinalizationType.Revert => "revert",
            GlamourerStateFinalizationType.RevertCustomize => "customize revert",
            GlamourerStateFinalizationType.RevertEquipment => "equipment revert",
            GlamourerStateFinalizationType.RevertAdvanced => "advanced revert",
            GlamourerStateFinalizationType.RevertAutomation => "automation revert",
            GlamourerStateFinalizationType.Reapply => "reapply",
            GlamourerStateFinalizationType.ReapplyAutomation => "automation reapply",
            GlamourerStateFinalizationType.Gearset => "gearset update",
            _ => "appearance finalization",
        };

    private T? TryCreate<T>(Func<string, T> creator, string label)
        where T : class
    {
        try
        {
            return creator(label);
        }
        catch (Exception ex)
        {
            _log.Verbose($"Unable to create Glamourer IPC subscriber for {label}.\n\t{ex}");
            return null;
        }
    }
}
