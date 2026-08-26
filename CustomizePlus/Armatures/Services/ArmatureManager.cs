// Copyright (c) Customize+.
// Licensed under the MIT license.

using CustomizePlus.Armatures.Data;
using CustomizePlus.Armatures.Events;
using CustomizePlus.Core.Data;
using CustomizePlus.Core.Extensions;
using CustomizePlus.Game.Services;
using CustomizePlus.Game.Services.GPose;
using CustomizePlus.GameData.Extensions;
using CustomizePlus.Profiles;
using CustomizePlus.Profiles.Data;
using CustomizePlus.Profiles.Events;
using CustomizePlus.Templates.Events;
using CustomizePlus.Configuration.Data;
using CustomizePlus.Interop.Ipc;
using Dalamud.Plugin.Services;
using OtterGui.Classes;
using OtterGui.Log;
using Penumbra.GameData.Actors;
using Penumbra.GameData.Enums;
using Penumbra.GameData.Interop;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using CustomizePlus.Core.Services;

namespace CustomizePlus.Armatures.Services;

public unsafe sealed class ArmatureManager : IDisposable
{
    private readonly ProfileManager _profileManager;
    private readonly IObjectTable _objectTable;
    private readonly GameObjectService _gameObjectService;
    private readonly TemplateChanged _templateChangedEvent;
    private readonly ProfileChanged _profileChangedEvent;
    private readonly Logger _logger;
    private readonly PluginConfiguration _configuration;
    private readonly FrameworkManager _framework;
    private readonly ActorObjectManager _objectManager;
    private readonly ActorManager _actorManager;
    private readonly GPoseService _gposeService;
    private readonly ArmatureChanged _event;
    private readonly EmoteService _emoteService;
    private readonly AdvancedBodyScalingBoneImportanceService _boneImportanceService;
    private readonly GlamourerIpcHandler _glamourerIpcHandler;
    private const float NearbyFullBoneImportanceDistance = 12f;
    private const float NearbyFullBoneImportanceDistanceSquared = NearbyFullBoneImportanceDistance * NearbyFullBoneImportanceDistance;
    private const float ActiveBoneImportanceBlendEpsilon = 0.0001f;
    private const int SelfProbeIntervalMs = 450;
    private const int ProfiledProbeIntervalMs = 700;
    private const int TargetProbeIntervalMs = 500;
    private const int NearbyProbeIntervalMs = 1200;
    private const int OtherProbeIntervalMs = 2200;
    private const int SelfResolveIntervalMs = 850;
    private const int ProfiledResolveIntervalMs = 1300;
    private const int TargetResolveIntervalMs = 1400;
    private const int NearbyResolveIntervalMs = 2600;
    private const int OtherResolveIntervalMs = 4200;
    private const int BoneImportanceVisibleStateDebounceMs = 900;
    private const int BoneImportanceVisibleLowActivityDebounceMs = 1200;
    private const int SelfSignatureChangeDebounceMs = 950;
    private const int ProfiledSignatureChangeDebounceMs = 1250;
    private const int StableSignatureConfirmationProbeCount = 2;
    private const int TransitionalSlotBaselineDebounceMs = 6500;
    private const int TransitionalSlotBaselineProbeCount = 6;
    private const int TransitionalSlotBaselineSettleHoldMs = 1750;

    /// <summary>
    /// This is a movement flag for every object. Used to prevent calls to ApplyRootTranslation from both movement and render hooks.
    /// Sized dynamically because object table indices are not a stable contract.
    /// </summary>
    private bool[] _objectMovementFlagsArr = new bool[1024];
    private DateTime _lastRenderAtUtc;
    private readonly Dictionary<ActorIdentifier, ActorFailureState> _actorFailureStates = new();
    private readonly ArmatureLifecycleTrace _selfLifecycleTrace = new();
    private long _debugLifecycleFrame;

    private sealed class ActorFailureState
    {
        public Dictionary<string, long> LastLoggedByStage { get; } = new(StringComparer.Ordinal);
        public HashSet<string> FailingStages { get; } = new(StringComparer.Ordinal);
    }

    private sealed class BoneImportanceFrameBudgetState
    {
        private int _profiledFullRemaining;
        private int _profiledProbeRemaining;
        private int _targetFullRemaining;
        private int _nearbyFullRemaining;
        private int _targetReducedRemaining;
        private int _nearbyReducedRemaining;

        public BoneImportanceFrameBudgetState(int crowdActorCount)
        {
            CrowdActorCount = Math.Max(crowdActorCount, 1);
            HighCrowdPressure = CrowdActorCount >= 8;
            ExtremeCrowdPressure = CrowdActorCount >= 14;

            _profiledFullRemaining = ExtremeCrowdPressure ? 1 : HighCrowdPressure ? 2 : 3;
            // Stable signature probes can still resolve four slot paths. Spread profiled probes
            // across frames in a crowd so a synchronized interval does not create a hitch.
            _profiledProbeRemaining = HighCrowdPressure ? 1 : 2;
            _targetFullRemaining = 1;
            _nearbyFullRemaining = CrowdActorCount >= 8 ? 0 : 1;
            _targetReducedRemaining = 1;
            _nearbyReducedRemaining = HighCrowdPressure ? 0 : 1;
        }

        public int CrowdActorCount { get; }
        public bool HighCrowdPressure { get; }
        public bool ExtremeCrowdPressure { get; }

        public bool TryConsumeFull(AdvancedBodyScalingBoneImportanceActorTier tier)
            => tier switch
            {
                AdvancedBodyScalingBoneImportanceActorTier.Self => true,
                AdvancedBodyScalingBoneImportanceActorTier.ProfiledActor => Consume(ref _profiledFullRemaining),
                AdvancedBodyScalingBoneImportanceActorTier.TargetOrFocus => Consume(ref _targetFullRemaining),
                AdvancedBodyScalingBoneImportanceActorTier.NearbyNonProfiled => Consume(ref _nearbyFullRemaining),
                _ => false,
            };

        public bool TryConsumeProbe(AdvancedBodyScalingBoneImportanceActorTier tier)
            => tier switch
            {
                AdvancedBodyScalingBoneImportanceActorTier.Self => true,
                AdvancedBodyScalingBoneImportanceActorTier.ProfiledActor => Consume(ref _profiledProbeRemaining),
                _ => false,
            };

        public bool TryConsumeReduced(AdvancedBodyScalingBoneImportanceActorTier tier)
            => tier switch
            {
                AdvancedBodyScalingBoneImportanceActorTier.Self => true,
                AdvancedBodyScalingBoneImportanceActorTier.ProfiledActor => true,
                AdvancedBodyScalingBoneImportanceActorTier.TargetOrFocus => Consume(ref _targetReducedRemaining),
                AdvancedBodyScalingBoneImportanceActorTier.NearbyNonProfiled => Consume(ref _nearbyReducedRemaining),
                _ => false,
            };

        private static bool Consume(ref int remaining)
        {
            if (remaining <= 0)
                return false;

            remaining--;
            return true;
        }
    }

    private readonly record struct BoneImportanceVisibleRuntimeState(
        string RuntimeModeLabel,
        string ActorTierLabel,
        bool FullQualityEligible,
        bool CrowdSafeDowngraded,
        bool StableThrottled,
        string RuntimeSummary)
    {
        public string Key
            => $"{RuntimeModeLabel}|{ActorTierLabel}|{FullQualityEligible}|{CrowdSafeDowngraded}|{StableThrottled}";
    }

    public Dictionary<ActorIdentifier, Armature> Armatures { get; private set; } = new();

    public ArmatureManager(
        ProfileManager profileManager,
        IObjectTable objectTable,
        GameObjectService gameObjectService,
        TemplateChanged templateChangedEvent,
        ProfileChanged profileChangedEvent,
        Logger logger,
        PluginConfiguration configuration,
        FrameworkManager framework,
        ActorObjectManager objectManager,
        ActorManager actorManager,
        GPoseService gposeService,
        ArmatureChanged @event,
        EmoteService emoteService,
        AdvancedBodyScalingBoneImportanceService boneImportanceService,
        GlamourerIpcHandler glamourerIpcHandler)
    {
        _profileManager = profileManager;
        _objectTable = objectTable;
        _gameObjectService = gameObjectService;
        _templateChangedEvent = templateChangedEvent;
        _profileChangedEvent = profileChangedEvent;
        _logger = logger;
        _configuration = configuration;
        _framework = framework;
        _objectManager = objectManager;
        _actorManager = actorManager;
        _gposeService = gposeService;
        _event = @event;
        _emoteService = emoteService;
        _boneImportanceService = boneImportanceService;
        _glamourerIpcHandler = glamourerIpcHandler;

        _templateChangedEvent.Subscribe(OnTemplateChange, TemplateChanged.Priority.ArmatureManager);
        _profileChangedEvent.Subscribe(OnProfileChange, ProfileChanged.Priority.ArmatureManager);
    }

    public void Dispose()
    {
        _templateChangedEvent.Unsubscribe(OnTemplateChange);
        _profileChangedEvent.Unsubscribe(OnProfileChange);
    }

    /// <summary>
    /// Main rendering function, called from rendering hook
    /// </summary>
    public void OnRender()
    {
        try
        {
#if DEBUG
            if (_configuration.DebuggingModeEnabled)
                _debugLifecycleFrame++;
#endif
            var now = DateTime.UtcNow;
            var deltaSeconds = _lastRenderAtUtc == default
                ? Constants.MaxTransitionDeltaSeconds
                : (float)Math.Min((now - _lastRenderAtUtc).TotalSeconds, Constants.MaxTransitionDeltaSeconds);
            _lastRenderAtUtc = now;

            RefreshArmatures();
            ApplyArmatureTransforms(deltaSeconds);
        }
        catch (Exception ex)
        {
            _logger.Error($"Exception while rendering armatures:\n\t{ex}");
        }
    }

    /// <summary>
    /// Function called when game object movement is detected
    /// </summary>
    public void OnGameObjectMove(Actor actor)
    {
        if (!actor.Identifier(_actorManager, out var identifier))
            return;

        try
        {
            if (Armatures.TryGetValue(identifier, out var armature)
                && armature.IsBuilt
                && armature.IsSkeletonBindingCurrent
                && armature.IsVisible)
            {
                EnsureObjectMovementFlagCapacity(actor.AsObject->ObjectIndex);
                _objectMovementFlagsArr[actor.AsObject->ObjectIndex] = true;
                ApplyRootTranslation(armature, actor);
            }
            MarkActorStageHealthy(identifier, "movement");
        }
        catch (Exception ex)
        {
            RecordActorFailure(identifier, "movement", ex);
        }
    }

    /// <summary>
    /// Reasserts the root draw scale after the game has completed its own render-manager update.
    /// The regular transform pass remains the authority for every bone; this only closes the
    /// frame-order gap where the game resets the character draw object's root scale to one.
    /// </summary>
    public void OnPostRender()
    {
        foreach (var armature in Armatures.Values)
        {
            if (!armature.IsBuilt
                || !armature.IsSkeletonBindingCurrent
                || !armature.IsVisible
                || !_objectManager.TryGetValue(armature.ActorIdentifier, out var actorData)
                || actorData.Objects.Count == 0)
                continue;

            try
            {
                var rootBone = armature.MainRootBone;
                var transform = rootBone.AppliedTransform;
                if (transform == null || !rootBone.IsModifiedScale() || !TransformSafety.IsFinite(transform.Scaling))
                    continue;

                foreach (var actor in actorData.Objects)
                {
                    if (!actor || !_gameObjectService.IsActorHasScalableRoot(actor))
                        continue;

                    var cBase = actor.Model.AsCharacterBase;
                    if (cBase == null)
                        continue;

                    var observedBefore = cBase->DrawObject.Object.Scale;
                    cBase->DrawObject.Object.Scale = transform.Scaling;
                    cBase->DrawObject.Object.IsTransformChanged = true;

                    armature.RecordRootScaleApplication(
                        rootScaleModified: true,
                        actorEligible: true,
                        observedBefore: new Vector3(observedBefore.X, observedBefore.Y, observedBefore.Z),
                        requested: transform.Scaling,
                        observedAfter: transform.Scaling,
                        applied: true);
                }
            }
            catch (Exception ex)
            {
                // A late root correction is optional. Preserve the normal transform path if it fails.
                RecordActorFailure(armature.ActorIdentifier, "late root scale", ex);
            }
        }
    }

    /// <summary>
    /// Force profile rebind for all armatures
    /// </summary>
    public void RebindAllArmatures()
    {
        foreach (var kvPair in Armatures)
            kvPair.Value.IsPendingProfileRebind = true;
    }

    internal IReadOnlyList<ArmatureLifecycleTraceEntry> GetDebugSelfLifecycleTrace()
        => _selfLifecycleTrace.Snapshot();

    internal string GetDebugSelfLifecycleTraceClipboardText()
        => _selfLifecycleTrace.BuildClipboardText();

    internal void ClearDebugSelfLifecycleTrace()
        => _selfLifecycleTrace.Clear();

#if DEBUG
    internal bool TryStartDebugPoseCorrectiveValidation(out string reason)
    {
        foreach (var armature in Armatures.Values)
        {
            if (!_objectManager.TryGetValue(armature.ActorIdentifier, out var actorData)
                || actorData.Objects.Count == 0
                || !AreSameActor(actorData.Objects[0], _objectManager.Player))
            {
                continue;
            }

            return armature.TryStartDebugPoseCorrectiveValidation(out reason);
        }

        reason = "No current-player armature is available for a bounded RBF validation fixture.";
        return false;
    }
#endif

    internal void CaptureDebugSelfLifecycleSnapshot()
    {
#if DEBUG
        if (!_configuration.DebuggingModeEnabled)
            return;

        foreach (var armature in Armatures.Values)
        {
            if (!_objectManager.TryGetValue(armature.ActorIdentifier, out var actorData) || actorData.Objects.Count == 0)
                continue;

            var actor = actorData.Objects[0];
            if (AreSameActor(actor, _objectManager.Player))
                RecordSelfLifecycleTrace(armature, actor, "manual capture", force: true, TryGetGlamourerAppearanceTransition(actor));
        }
#endif
    }

    private AdvancedBodyScalingSettings ResolveAdvancedBodyScaling(Profile profile, Actor actor)
    {
        var baseline = _configuration.AdvancedBodyScalingSettings;
        var race = TryGetActorRace(actor, out var resolvedRace)
            ? resolvedRace
            : Race.Unknown;

        return profile.AdvancedBodyScalingOverrides.Resolve(baseline, race);
    }

    private static bool TryGetActorRace(Actor actor, out Race race)
    {
        race = Race.Unknown;

        if (!actor || !actor.IsCharacter)
            return false;

        var customize = actor.Customize;
        if (customize == null)
            return false;

        race = customize->Race;
        return race != Race.Unknown;
    }

    private static unsafe bool TryGetActorAppearanceContext(Actor actor, out int race, out int clan, out int gender)
    {
        race = 0;
        clan = 0;
        gender = 0;

        if (!actor || !actor.IsCharacter)
            return false;

        var customize = actor.Customize;
        if (customize == null || customize->Race == Race.Unknown)
            return false;

        race = (int)customize->Race;
        clan = (int)customize->Clan;
        gender = (int)customize->Gender;
        return true;
    }

    /// <summary>
    /// Deletes armatures which no longer have actor associated with them and creates armatures for new actors
    /// </summary>
    private void RefreshArmatures()
    {
        var currentTime = DateTime.UtcNow;
        var armatureExpirationDateTime = currentTime.AddSeconds(-30);
        foreach (var kvPair in Armatures.ToList())
        {
            var armature = kvPair.Value;
            try
            {
                //Only remove armatures which haven't been seen for a while
                //But remove armatures of special actors (like examine screen) right away
                if (!_objectManager.ContainsKey(kvPair.Value.ActorIdentifier))
                {
                    // Keep the persistent profile/armature association during the short zoning grace
                    // period, but invalidate the old native ModelBone lifetime immediately. This
                    // forces a validated publication when the actor returns even if addresses/topology
                    // happen to be reused.
                    armature.MarkNativeBindingUnavailable("actor was absent from the object manager");

                    if (armature.LastSeen <= armatureExpirationDateTime || armature.ActorIdentifier.Type == IdentifierType.Special)
                    {
                        _logger.Debug($"Removing armature {armature} because {kvPair.Key.IncognitoDebug()} is gone");
                        RemoveArmature(armature, ArmatureChanged.DeletionReason.Gone);
                    }

                    continue;
                }

                //armature is considered visible if 1 or less seconds passed since last time we've seen the actor
                armature.IsVisible = armature.LastSeen.AddSeconds(1) >= currentTime;
                MarkActorStageHealthy(armature.ActorIdentifier, "cleanup");
            }
            catch (Exception ex)
            {
                RecordActorFailure(armature.ActorIdentifier, "cleanup", ex);
            }
        }

        var renderableEntries = _objectManager
            .Where(obj => obj.Value.Objects != null
                && obj.Value.Objects.Count > 0
                && obj.Value.Objects.Any(x => x.IsRenderedByGame()))
            .ToList();
        var boneImportanceBudget = new BoneImportanceFrameBudgetState(renderableEntries.Count);

        foreach (var obj in renderableEntries)
        {
            var actorIdentifier = obj.Key.CreatePermanent();
            try
            {
                var objects = obj.Value.Objects;
                if (objects == null || objects.Count == 0)
                    continue;

                if (!Armatures.ContainsKey(actorIdentifier))
                {
                    var activeProfile = _profileManager.GetEnabledProfilesByActor(actorIdentifier).FirstOrDefault();
                    if (activeProfile == null)
                        continue;

                    var newArm = new Armature(actorIdentifier, activeProfile);
                    TryLinkSkeleton(newArm, boneImportanceBudget);
                    Armatures.Add(actorIdentifier, newArm);
                    _logger.Debug($"Added '{newArm}' for {actorIdentifier.IncognitoDebug()} to cache");
                    _event.Invoke(ArmatureChanged.Type.Created, newArm, activeProfile);

                    MarkActorStageHealthy(actorIdentifier, "refresh");
                    continue;
                }

                var armature = Armatures[actorIdentifier];

                armature.UpdateLastSeen(currentTime);

                if (armature.IsPendingProfileRebind)
                {
                _logger.Debug($"Armature {armature} is pending profile/bone rebind, rebinding...");
                armature.IsPendingProfileRebind = false;

                var activeProfile = _profileManager.GetEnabledProfilesByActor(actorIdentifier).FirstOrDefault();
                Profile? oldProfile = armature.Profile;
                bool profileChange = activeProfile != armature.Profile;
                bool oldHadRoot = oldProfile.Templates.Any(x => x.Bones.ContainsKey("n_root"));
                bool newHasRoot = activeProfile?.Templates.Any(x => x.Bones.ContainsKey("n_root")) ?? false;

                if (profileChange)
                {
                    if (activeProfile == null)
                    {
                        _logger.Debug($"Removing armature {armature} because it doesn't have any active profiles");
                        RemoveArmature(armature, ArmatureChanged.DeletionReason.NoActiveProfiles);

                        if (oldHadRoot && obj.Value.Objects != null)
                        {
                            //Reset root translation
                            foreach (var actor in obj.Value.Objects)
                                ApplyRootTranslation(armature, actor, true);
                        }

                        continue;
                    }

                    armature.Profile.Armatures.Remove(armature);
                    armature.Profile = activeProfile!;
                    activeProfile.Armatures.Add(armature);
                }

                var actorForSettings = objects[0];
                var advancedBodyScaling = ResolveAdvancedBodyScaling(armature.Profile, actorForSettings);
                var actorSkeletonUpdated = actorForSettings && armature.IsSkeletonUpdated(actorForSettings.Model.AsCharacterBase);
                var boneImportance = actorForSettings
                    ? EvaluateBoneImportanceForArmature(
                        armature,
                        actorForSettings,
                        advancedBodyScaling,
                        boneImportanceBudget,
                        actorSkeletonUpdated,
                        TryGetGlamourerAppearanceTransition(actorForSettings),
                        forceRefresh: true)
                    : AdvancedBodyScalingBoneImportanceResult.CreateFallback(
                        "No live actor was available during profile rebind.",
                        enabled: advancedBodyScaling.ModelDerivedBoneImportanceEnabled,
                        preferSkinWeights: advancedBodyScaling.PreferTrueSkinWeightImportance,
                        heuristicBlend: advancedBodyScaling.BoneImportanceHeuristicBlend);
                armature.RebuildBoneTemplateBinding(
                    _configuration.RuntimeSafetySettings.SoftScaleLimitsEnabled,
                    _configuration.RuntimeSafetySettings.AutomaticChildScaleCompensationEnabled,
                    advancedBodyScaling,
                    boneImportance,
                    "profile rebind");

                //warn: might be a bit of a performance hit on profiles with a lot of templates/bones
                //warn: this must be done after RebuildBoneTemplateBinding or it will not work
                if (oldHadRoot && (!profileChange || !newHasRoot))
                {
                    _logger.Debug($"Resetting root transform for {armature} because new profile doesn't have root edits");

                    if (obj.Value.Objects != null)
                    {
                        foreach (var actor in obj.Value.Objects)
                        {
                            if (_emoteService.IsSitting(actor))
                            {
                                _logger.Debug($"Skipping root reset for sitting actor {actor.Utf8Name}");
                                continue;
                            }

                            _logger.Debug($"Resetting root for {actor.Utf8Name}");
                            ApplyRootTranslation(armature, actor, true);
                        }
                    }
                }

                _event.Invoke(ArmatureChanged.Type.Updated, armature, (activeProfile, oldProfile));
                RecordSelfLifecycleTrace(armature, actorForSettings, "profile rebind", force: true, TryGetGlamourerAppearanceTransition(actorForSettings));
                }

                //Needed because:
                //* Skeleton sometimes appears to be not ready when armature is created
                //* We want to keep armature up to date with any character skeleton changes
                TryLinkSkeleton(armature, boneImportanceBudget);
                MarkActorStageHealthy(actorIdentifier, "refresh");
            }
            catch (Exception ex)
            {
                RecordActorFailure(actorIdentifier, "refresh", ex);
            }
        }
    }

    private unsafe void ApplyArmatureTransforms(float deltaSeconds)
    {
        var transitionSharpness = _configuration.RuntimeBehaviorSettings.TransformTransitionSharpness;

        foreach (var kvPair in Armatures)
        {
            var armature = kvPair.Value;
            try
            {
                var applyFailed = false;
                armature.UpdateRuntimeTransforms(deltaSeconds, transitionSharpness);

                if (armature.IsBuilt
                    && armature.IsSkeletonBindingCurrent
                    && armature.IsVisible
                    && _objectManager.TryGetValue(armature.ActorIdentifier, out var actorData))
                {
                    if (actorData.Objects.Count > 0)
                    {
                        var motionActor = actorData.Objects[0];
                        if (_emoteService.IsSitting(motionActor))
                        {
                            armature.ResetMotionWarpingContext("Motion warping is suppressed while the actor is sitting.");
                        }
                        else
                        {
                            armature.UpdateMotionWarpingContext(
                                new Vector3(
                                    motionActor.AsObject->Position.X,
                                    motionActor.AsObject->Position.Y,
                                    motionActor.AsObject->Position.Z),
                                motionActor.AsObject->Rotation,
                                deltaSeconds);
                        }
                    }
                    else
                    {
                        armature.ResetMotionWarpingContext();
                    }

                    foreach (var actor in actorData.Objects)
                    {
                        try
                        {
                            EnsureObjectMovementFlagCapacity(actor.AsObject->ObjectIndex);
                            ApplyPiecewiseTransformation(armature, actor, armature.ActorIdentifier, deltaSeconds);

                            if (!_objectMovementFlagsArr[actor.AsObject->ObjectIndex])
                            {
                                //todo: ApplyRootTranslation causes character flashing in gpose
                                //research if this can be fixed without breaking this functionality
                                if (_gposeService.IsInGPose)
                                    continue;

                                ApplyRootTranslation(armature, actor);
                            }
                            else
                                _objectMovementFlagsArr[actor.AsObject->ObjectIndex] = false;
                        }
                        catch (Exception ex)
                        {
                            applyFailed = true;
                            RecordActorFailure(armature.ActorIdentifier, "apply", ex);
                        }
                    }
                }

                if (!applyFailed)
                    MarkActorStageHealthy(armature.ActorIdentifier, "apply");
            }
            catch (Exception ex)
            {
                RecordActorFailure(armature.ActorIdentifier, "apply", ex);
            }
        }
    }

    private void EnsureObjectMovementFlagCapacity(ushort objectIndex)
    {
        if (objectIndex < _objectMovementFlagsArr.Length)
            return;

        var newSize = _objectMovementFlagsArr.Length;
        while (newSize <= objectIndex)
            newSize *= 2;

        Array.Resize(ref _objectMovementFlagsArr, newSize);
    }

    private void RecordActorFailure(ActorIdentifier actorIdentifier, string stage, Exception exception)
    {
        const long logIntervalMs = 5000;
        var now = Environment.TickCount64;
        if (!_actorFailureStates.TryGetValue(actorIdentifier, out var state))
        {
            state = new ActorFailureState();
            _actorFailureStates[actorIdentifier] = state;
        }

        state.FailingStages.Add(stage);
        if (state.LastLoggedByStage.TryGetValue(stage, out var lastLogged)
            && now - lastLogged < logIntervalMs)
            return;

        state.LastLoggedByStage[stage] = now;
        _logger.Warning($"Skipped {stage} processing for {actorIdentifier.IncognitoDebug()} after {exception.GetType().Name}; other actors will continue.");
    }

    private void MarkActorStageHealthy(ActorIdentifier actorIdentifier, string stage)
    {
        if (!_actorFailureStates.TryGetValue(actorIdentifier, out var state))
            return;

        state.FailingStages.Remove(stage);
        state.LastLoggedByStage.Remove(stage);
        if (state.FailingStages.Count == 0)
            _actorFailureStates.Remove(actorIdentifier);
    }

    private AdvancedBodyScalingBoneImportanceResult EvaluateBoneImportanceForArmature(
        Armature armature,
        Actor actor,
        AdvancedBodyScalingSettings settings,
        BoneImportanceFrameBudgetState budget,
        bool skeletonUpdated,
        GlamourerAppearanceTransitionSnapshot glamourerAppearanceTransition,
        bool forceRefresh = false)
    {
        var boneImportanceStarted = armature.PerformanceMetrics.Start();
        try
        {
        var tier = ClassifyBoneImportanceTier(armature, actor);
        var fullEligible = IsFullBoneImportanceEligible(settings, tier);
        var activelyManaged = ShouldActivelyManageBoneImportance(settings, tier, budget);
        var runtimeState = armature.BoneImportanceRuntimeState;
        var activeResult = armature.ActiveBoneImportanceResult;
        var hasCachedModelResult = activeResult.ModelDerivedActive;
        var now = Environment.TickCount64;
        if (string.IsNullOrWhiteSpace(runtimeState.LastConfirmedModelSignature) &&
            !string.IsNullOrWhiteSpace(activeResult.ModelSignature))
        {
            runtimeState.LastConfirmedModelSignature = activeResult.ModelSignature;
        }

        var stableSignature = GetStableBoneImportanceSignature(runtimeState, activeResult);

        if (!settings.ModelDerivedBoneImportanceEnabled)
        {
            ResetPendingBoneImportanceSignature(runtimeState);
            runtimeState.LastMode = AdvancedBodyScalingBoneImportanceRuntimeMode.Skipped;
            return ApplyRuntimePolicy(
                AdvancedBodyScalingBoneImportanceResult.CreateFallback(
                    "Model-derived bone importance is disabled for this evaluation.",
                    enabled: false,
                    preferSkinWeights: settings.PreferTrueSkinWeightImportance,
                    heuristicBlend: settings.BoneImportanceHeuristicBlend,
                    modelSignature: stableSignature),
                settings,
                runtimeState,
                now,
                AdvancedBodyScalingBoneImportanceRuntimeMode.Skipped,
                tier,
                fullEligible,
                crowdSafeDowngraded: false,
                stableThrottled: false,
                runtimeSummary: "BIW was skipped because model-derived weighting is disabled.");
        }

        if (!activelyManaged)
        {
            if (!string.IsNullOrWhiteSpace(activeResult.ModelSignature))
            {
                runtimeState.LastProbedModelSignature = activeResult.ModelSignature;
                runtimeState.LastConfirmedModelSignature = activeResult.ModelSignature;
            }

            ResetPendingBoneImportanceSignature(runtimeState);
            runtimeState.StableProbeCount = 0;
            var refreshStatus = BuildHardSkipRefreshStatus(tier, settings, budget, hasCachedModelResult);
            return ApplyRuntimePolicy(
                AdvancedBodyScalingBoneImportanceResult.CreateFallback(
                    BuildHardSkipFallbackReason(tier, settings, budget, hasCachedModelResult),
                    enabled: true,
                    preferSkinWeights: settings.PreferTrueSkinWeightImportance,
                    heuristicBlend: settings.BoneImportanceHeuristicBlend,
                    modelSignature: runtimeState.LastProbedModelSignature,
                    refreshStatus: refreshStatus),
                settings,
                runtimeState,
                now,
                AdvancedBodyScalingBoneImportanceRuntimeMode.Skipped,
                tier,
                fullEligible,
                crowdSafeDowngraded: true,
                stableThrottled: true,
                runtimeSummary: refreshStatus);
        }

        var probeStability = glamourerAppearanceTransition.Active ? 0 : runtimeState.StableProbeCount;
        var probeInterval = GetBoneImportanceProbeIntervalMs(tier, probeStability);
        var resolveInterval = GetBoneImportanceResolveIntervalMs(tier, probeStability);
        var priorityRefresh = forceRefresh
            || runtimeState.LastMode == AdvancedBodyScalingBoneImportanceRuntimeMode.Skipped
            || !hasCachedModelResult;
        var probeDue = priorityRefresh
            || runtimeState.LastProbeAtMs == 0
            || now - runtimeState.LastProbeAtMs >= probeInterval
            || string.IsNullOrWhiteSpace(stableSignature);

        if (!probeDue)
        {
            if (hasCachedModelResult)
            {
                var cachedSummary = glamourerAppearanceTransition.Active
                    ? glamourerAppearanceTransition.Summary
                    : "Cached BIW was reused while the actor stayed within the current stable-check window.";
                return ApplyRuntimePolicy(
                    activeResult,
                    settings,
                    runtimeState,
                    now,
                    AdvancedBodyScalingBoneImportanceRuntimeMode.Cached,
                    tier,
                    fullEligible,
                    crowdSafeDowngraded: !fullEligible,
                    stableThrottled: true,
                    runtimeSummary: cachedSummary);
            }

            runtimeState.LastMode = AdvancedBodyScalingBoneImportanceRuntimeMode.Skipped;
            return ApplyRuntimePolicy(
                AdvancedBodyScalingBoneImportanceResult.CreateFallback(
                    "Crowd-safe BIW skipped this actor until the next scheduled model-signature probe.",
                    enabled: true,
                    preferSkinWeights: settings.PreferTrueSkinWeightImportance,
                    heuristicBlend: settings.BoneImportanceHeuristicBlend,
                    modelSignature: runtimeState.LastProbedModelSignature),
                settings,
                runtimeState,
                now,
                AdvancedBodyScalingBoneImportanceRuntimeMode.Skipped,
                tier,
                fullEligible,
                crowdSafeDowngraded: true,
                stableThrottled: true,
                runtimeSummary: "BIW was skipped until the next scheduled probe because this actor is currently low-priority.");
        }

        // A forced refresh, an uncached result, or an active appearance transition must keep its
        // normal priority. Only defer a stable cached profiled actor whose scheduled probe would
        // otherwise coincide with other crowd actors this frame.
        if (!priorityRefresh &&
            hasCachedModelResult &&
            !glamourerAppearanceTransition.Active &&
            !budget.TryConsumeProbe(tier))
        {
            return ApplyRuntimePolicy(
                activeResult,
                settings,
                runtimeState,
                now,
                AdvancedBodyScalingBoneImportanceRuntimeMode.Cached,
                tier,
                fullEligible,
                crowdSafeDowngraded: !fullEligible,
                stableThrottled: true,
                runtimeSummary: "Crowd-safe BIW reused the cached result because this stable actor's model-signature probe was deferred to a later frame.");
        }

        var probe = _boneImportanceService.ProbeActorModelSignature(actor, settings, stableSignature);
        runtimeState.LastProbeAtMs = now;
        runtimeState.LastProbedModelSignature = probe.ModelSignature;
        var signatureChanged = EvaluateBoneImportanceSignatureChange(
            runtimeState,
            tier,
            now,
            forceRefresh,
            hasCachedModelResult,
            stableSignature,
            probe,
            glamourerAppearanceTransition,
            skeletonUpdated,
            out var confirmedAfterDebounce,
            out var pendingSignatureSummary);

        var resolveDue = priorityRefresh
            || !hasCachedModelResult
            || signatureChanged
            || runtimeState.LastResolveAtMs == 0
            || now - runtimeState.LastResolveAtMs >= resolveInterval;

        // A stable signature already has a complete applied importance map. Do not let the
        // per-frame full/reduced budget swap its source mode and force a template rebind just
        // because this actor reached a periodic resolve interval. Signature changes and explicit
        // appearance refreshes still take the normal rebuild path below.
        if (probe.HasResolvedModelSet &&
            hasCachedModelResult &&
            !forceRefresh &&
            !signatureChanged &&
            string.Equals(probe.ModelSignature, stableSignature, StringComparison.OrdinalIgnoreCase))
        {
            return ApplyRuntimePolicy(
                activeResult,
                settings,
                runtimeState,
                now,
                AdvancedBodyScalingBoneImportanceRuntimeMode.Cached,
                tier,
                fullEligible,
                crowdSafeDowngraded: !fullEligible,
                stableThrottled: true,
                runtimeSummary: "Resolved model signature was unchanged, so the existing BIW map stayed applied without rebuilding template bindings.");
        }

        if (probe.HasResolvedModelSet && !resolveDue && hasCachedModelResult)
        {
            var cachedSummary = !string.IsNullOrWhiteSpace(pendingSignatureSummary)
                ? pendingSignatureSummary
                : glamourerAppearanceTransition.Active
                    ? glamourerAppearanceTransition.Summary
                    : "Resolved model signature was unchanged, so cached BIW stayed active and the expensive rebuild was deferred.";
            return ApplyRuntimePolicy(
                activeResult,
                settings,
                runtimeState,
                now,
                AdvancedBodyScalingBoneImportanceRuntimeMode.Cached,
                tier,
                fullEligible,
                crowdSafeDowngraded: !fullEligible,
                stableThrottled: true,
                runtimeSummary: cachedSummary);
        }

        if (probe.HasResolvedModelSet && fullEligible && budget.TryConsumeFull(tier))
        {
            var resolved = _boneImportanceService.ResolveForActor(actor, settings, stableSignature);
            runtimeState.LastResolveAtMs = now;
            runtimeState.LastProbedModelSignature = resolved.ModelSignature;
            runtimeState.LastConfirmedModelSignature = resolved.ModelSignature;
            ResetPendingBoneImportanceSignature(runtimeState);
            return ApplyRuntimePolicy(
                resolved,
                settings,
                runtimeState,
                now,
                AdvancedBodyScalingBoneImportanceRuntimeMode.Full,
                tier,
                fullEligible,
                crowdSafeDowngraded: false,
                stableThrottled: false,
                runtimeSummary: signatureChanged
                    ? confirmedAfterDebounce
                        ? "Full BIW was refreshed because a new resolved model signature persisted long enough to confirm a real high-priority actor change."
                        : "Full BIW was refreshed because the actor’s resolved model signature changed."
                    : "Full BIW was refreshed on schedule for a high-priority actor.");
        }

        if (probe.HasResolvedModelSet &&
            ShouldUseReducedBoneImportance(tier, fullEligible) &&
            budget.TryConsumeReduced(tier))
        {
            var reduced = _boneImportanceService.ResolveForActor(actor, CreateReducedBoneImportanceSettings(settings), stableSignature);
            runtimeState.LastResolveAtMs = now;
            runtimeState.LastProbedModelSignature = reduced.ModelSignature;
            runtimeState.LastConfirmedModelSignature = reduced.ModelSignature;
            ResetPendingBoneImportanceSignature(runtimeState);
            return ApplyRuntimePolicy(
                reduced,
                settings,
                runtimeState,
                now,
                AdvancedBodyScalingBoneImportanceRuntimeMode.Reduced,
                tier,
                fullEligible,
                crowdSafeDowngraded: true,
                stableThrottled: false,
                runtimeSummary: signatureChanged && confirmedAfterDebounce
                    ? "Crowd-safe BIW applied a reduced/coarse refresh after a new resolved model signature persisted long enough to confirm a real change."
                    : "Crowd-safe BIW applied a reduced/coarse refresh because full-quality budget was not available for this actor.");
        }

        if (hasCachedModelResult)
        {
            var cachedSummary = !string.IsNullOrWhiteSpace(pendingSignatureSummary)
                ? pendingSignatureSummary
                : glamourerAppearanceTransition.Active
                    ? glamourerAppearanceTransition.Summary
                    : probe.HasResolvedModelSet
                        ? "Crowd-safe BIW reused the cached result because the actor was deprioritized under the current frame budget."
                        : "Crowd-safe BIW reused the cached result because the current model probe did not return a usable slot set.";
            return ApplyRuntimePolicy(
                activeResult,
                settings,
                runtimeState,
                now,
                AdvancedBodyScalingBoneImportanceRuntimeMode.Cached,
                tier,
                fullEligible,
                crowdSafeDowngraded: true,
                stableThrottled: !signatureChanged,
                runtimeSummary: cachedSummary);
        }

        runtimeState.LastMode = AdvancedBodyScalingBoneImportanceRuntimeMode.Skipped;
        return ApplyRuntimePolicy(
            AdvancedBodyScalingBoneImportanceResult.CreateFallback(
                probe.HasResolvedModelSet
                    ? "Crowd-safe BIW skipped this actor because the current frame budget was reserved for higher-priority actors."
                    : probe.Summary,
                enabled: true,
                preferSkinWeights: settings.PreferTrueSkinWeightImportance,
                heuristicBlend: settings.BoneImportanceHeuristicBlend,
                modelSignature: probe.ModelSignature,
                modelSignatureChanged: signatureChanged,
                refreshStatus: !string.IsNullOrWhiteSpace(pendingSignatureSummary) ? pendingSignatureSummary : probe.Summary),
            settings,
            runtimeState,
            now,
            AdvancedBodyScalingBoneImportanceRuntimeMode.Skipped,
            tier,
            fullEligible,
            crowdSafeDowngraded: true,
            stableThrottled: false,
            runtimeSummary: probe.HasResolvedModelSet
                ? "BIW was skipped for this actor because the internal crowd-safe budget prioritized higher-value actors this frame."
                : "BIW was skipped because the actor did not expose a usable resolved whole-body model set during the current probe.");
        }
        finally
        {
            armature.PerformanceMetrics.Record("bone-importance-refresh", boneImportanceStarted);
        }
    }

    private static AdvancedBodyScalingSettings CreateReducedBoneImportanceSettings(AdvancedBodyScalingSettings settings)
        => new()
        {
            ModelDerivedBoneImportanceEnabled = settings.ModelDerivedBoneImportanceEnabled,
            PreferTrueSkinWeightImportance = false,
            BoneImportanceHeuristicBlend = settings.BoneImportanceHeuristicBlend,
        };

    private static string GetStableBoneImportanceSignature(
        AdvancedBodyScalingBoneImportanceRuntimeState runtimeState,
        AdvancedBodyScalingBoneImportanceResult activeResult)
    {
        if (!string.IsNullOrWhiteSpace(runtimeState.LastConfirmedModelSignature))
            return runtimeState.LastConfirmedModelSignature;

        if (!string.IsNullOrWhiteSpace(activeResult.ModelSignature))
            return activeResult.ModelSignature;

        return runtimeState.LastProbedModelSignature;
    }

    private static void ResetPendingBoneImportanceSignature(AdvancedBodyScalingBoneImportanceRuntimeState runtimeState)
    {
        runtimeState.PendingModelSignature = string.Empty;
        runtimeState.PendingModelSignatureAtMs = 0;
        runtimeState.PendingModelSignatureProbeCount = 0;
        runtimeState.PendingModelSignatureSettleHoldUntilMs = 0;
    }

    private static bool ShouldDebounceBoneImportanceSignatureChange(
        AdvancedBodyScalingBoneImportanceActorTier tier,
        bool forceRefresh,
        bool hasCachedModelResult)
        => !forceRefresh
           && hasCachedModelResult
           && (tier == AdvancedBodyScalingBoneImportanceActorTier.Self
               || tier == AdvancedBodyScalingBoneImportanceActorTier.ProfiledActor);

    private static int GetBoneImportanceSignatureChangeDebounceMs(AdvancedBodyScalingBoneImportanceActorTier tier)
        => tier switch
        {
            AdvancedBodyScalingBoneImportanceActorTier.Self => SelfSignatureChangeDebounceMs,
            AdvancedBodyScalingBoneImportanceActorTier.ProfiledActor => ProfiledSignatureChangeDebounceMs,
            _ => 0,
        };

    private static int GetBoneImportanceSignatureConfirmationProbeCount(AdvancedBodyScalingBoneImportanceActorTier tier)
        => tier switch
        {
            AdvancedBodyScalingBoneImportanceActorTier.Self => StableSignatureConfirmationProbeCount,
            AdvancedBodyScalingBoneImportanceActorTier.ProfiledActor => StableSignatureConfirmationProbeCount,
            _ => 1,
        };

    private static bool EvaluateBoneImportanceSignatureChange(
        AdvancedBodyScalingBoneImportanceRuntimeState runtimeState,
        AdvancedBodyScalingBoneImportanceActorTier tier,
        long now,
        bool forceRefresh,
        bool hasCachedModelResult,
        string stableSignature,
        AdvancedBodyScalingBoneImportanceProbeResult probe,
        GlamourerAppearanceTransitionSnapshot glamourerAppearanceTransition,
        bool skeletonUpdated,
        out bool confirmedAfterDebounce,
        out string pendingSummary)
    {
        confirmedAfterDebounce = false;
        pendingSummary = string.Empty;

        if (!probe.HasResolvedModelSet)
        {
            runtimeState.StableProbeCount = 0;
            ResetPendingBoneImportanceSignature(runtimeState);
            return false;
        }

        if (string.IsNullOrWhiteSpace(probe.ModelSignature) ||
            !probe.ModelSignatureChanged ||
            string.Equals(probe.ModelSignature, stableSignature, StringComparison.OrdinalIgnoreCase))
        {
            runtimeState.StableProbeCount = Math.Min(runtimeState.StableProbeCount + 1, 24);
            runtimeState.LastConfirmedModelSignature = probe.ModelSignature;
            ResetPendingBoneImportanceSignature(runtimeState);
            return false;
        }

        if (!ShouldDebounceBoneImportanceSignatureChange(tier, forceRefresh, hasCachedModelResult))
        {
            runtimeState.StableProbeCount = 0;
            ResetPendingBoneImportanceSignature(runtimeState);
            return true;
        }

        if (!string.Equals(runtimeState.PendingModelSignature, probe.ModelSignature, StringComparison.OrdinalIgnoreCase))
        {
            runtimeState.PendingModelSignature = probe.ModelSignature;
            runtimeState.PendingModelSignatureAtMs = now;
            runtimeState.PendingModelSignatureProbeCount = 1;
            runtimeState.PendingModelSignatureSettleHoldUntilMs = 0;
        }
        else
        {
            runtimeState.PendingModelSignatureProbeCount++;
        }

        runtimeState.StableProbeCount = Math.Min(runtimeState.StableProbeCount + 1, 24);

        var transitionalSlotBaseline = IsTransitionalSlotBaselineSignatureChange(stableSignature, probe.ModelSignature);
        if (glamourerAppearanceTransition.AwaitingFinalization)
        {
            pendingSummary = glamourerAppearanceTransition.Summary;
            return false;
        }

        if (transitionalSlotBaseline && glamourerAppearanceTransition.FinalizationSettling)
        {
            pendingSummary = glamourerAppearanceTransition.Summary;
            return false;
        }

        if (transitionalSlotBaseline && skeletonUpdated)
        {
            pendingSummary = "A transitional all-slot e0000 baseline was observed while the actor skeleton was rebuilding, so BIW kept the previous settled slot signature until the final slot winners finished loading.";
            return false;
        }

        var debounceMs = transitionalSlotBaseline
            ? TransitionalSlotBaselineDebounceMs
            : GetBoneImportanceSignatureChangeDebounceMs(tier);
        var requiredProbeCount = transitionalSlotBaseline
            ? TransitionalSlotBaselineProbeCount
            : GetBoneImportanceSignatureConfirmationProbeCount(tier);
        var pendingDurationMs = now - runtimeState.PendingModelSignatureAtMs;
        var confirmed = transitionalSlotBaseline
            ? pendingDurationMs >= debounceMs && runtimeState.PendingModelSignatureProbeCount >= requiredProbeCount
            : pendingDurationMs >= debounceMs || runtimeState.PendingModelSignatureProbeCount >= requiredProbeCount;
        if (confirmed)
        {
            if (transitionalSlotBaseline)
            {
                if (runtimeState.PendingModelSignatureSettleHoldUntilMs == 0)
                {
                    runtimeState.PendingModelSignatureSettleHoldUntilMs = now + TransitionalSlotBaselineSettleHoldMs;
                    pendingSummary = $"A transitional all-slot e0000 baseline persisted long enough to look real, but BIW is holding one final settle window for the actual slot winners to arrive before refreshing (~{TransitionalSlotBaselineSettleHoldMs} ms hold).";
                    return false;
                }

                if (now < runtimeState.PendingModelSignatureSettleHoldUntilMs)
                {
                    var settleRemainingMs = runtimeState.PendingModelSignatureSettleHoldUntilMs - now;
                    pendingSummary = $"A transitional all-slot e0000 baseline is in a final settle hold so BIW can prefer the real post-swap slot winners if they appear (~{settleRemainingMs} ms remaining).";
                    return false;
                }
            }

            runtimeState.StableProbeCount = 0;
            confirmedAfterDebounce = true;
            pendingSummary = transitionalSlotBaseline
                ? "A transitional all-slot e0000 baseline persisted through the final settle hold, so BIW treated it as the actor's settled slot set."
                : "A new resolved model signature persisted long enough to confirm a real BIW refresh for this high-priority actor.";
            return true;
        }

        var remainingMs = Math.Max(0L, debounceMs - pendingDurationMs);
        pendingSummary = transitionalSlotBaseline
            ? $"A transitional all-slot e0000 baseline was observed during a likely outfit/model swap, so BIW is waiting for the final slot winners to settle before refreshing ({runtimeState.PendingModelSignatureProbeCount}/{requiredProbeCount} probe{(requiredProbeCount == 1 ? string.Empty : "s")}, ~{remainingMs} ms remaining)."
            : $"A new resolved model signature was observed, but BIW is waiting for it to persist before refreshing this high-priority actor ({runtimeState.PendingModelSignatureProbeCount}/{requiredProbeCount} probe{(requiredProbeCount == 1 ? string.Empty : "s")}, ~{remainingMs} ms remaining).";
        return false;
    }

    private AdvancedBodyScalingBoneImportanceResult ApplyRuntimePolicy(
        AdvancedBodyScalingBoneImportanceResult result,
        AdvancedBodyScalingSettings settings,
        AdvancedBodyScalingBoneImportanceRuntimeState runtimeState,
        long now,
        AdvancedBodyScalingBoneImportanceRuntimeMode mode,
        AdvancedBodyScalingBoneImportanceActorTier tier,
        bool fullEligible,
        bool crowdSafeDowngraded,
        bool stableThrottled,
        string runtimeSummary)
    {
        result.RuntimeMode = result.Source == AdvancedBodyScalingBoneImportanceSource.HeuristicFallback
            ? AdvancedBodyScalingBoneImportanceRuntimeMode.Skipped
            : mode;
        result.ActorTier = tier;
        result.FullQualityEligible = fullEligible;
        result.CrowdSafeDowngraded = crowdSafeDowngraded;
        result.StableThrottled = stableThrottled;
        result.RuntimeSummary = runtimeSummary;
        runtimeState.LastMode = result.RuntimeMode;
        ApplyVisibleRuntimeState(result, settings, runtimeState, now);
        return result;
    }

    private static void ApplyVisibleRuntimeState(
        AdvancedBodyScalingBoneImportanceResult result,
        AdvancedBodyScalingSettings settings,
        AdvancedBodyScalingBoneImportanceRuntimeState runtimeState,
        long now)
    {
        var candidate = BuildVisibleRuntimeState(result, settings);
        if (!runtimeState.HasVisibleRuntimeState)
        {
            CommitVisibleRuntimeState(result, runtimeState, candidate);
            return;
        }

        if (string.Equals(runtimeState.VisibleStateKey, candidate.Key, StringComparison.Ordinal))
        {
            ApplyVisibleRuntimeStateToResult(result, runtimeState);
            runtimeState.PendingVisibleStateKey = string.Empty;
            runtimeState.PendingVisibleStateAtMs = 0;
            return;
        }

        if (ShouldApplyVisibleRuntimeStateImmediately(runtimeState, candidate))
        {
            CommitVisibleRuntimeState(result, runtimeState, candidate);
            runtimeState.PendingVisibleStateKey = string.Empty;
            runtimeState.PendingVisibleStateAtMs = 0;
            return;
        }

        if (!string.Equals(runtimeState.PendingVisibleStateKey, candidate.Key, StringComparison.Ordinal))
        {
            runtimeState.PendingVisibleStateKey = candidate.Key;
            runtimeState.PendingVisibleStateAtMs = now;
        }
        else if (now - runtimeState.PendingVisibleStateAtMs >= GetVisibleRuntimeStateDebounceMs(runtimeState.VisibleRuntimeModeLabel, candidate.RuntimeModeLabel))
        {
            CommitVisibleRuntimeState(result, runtimeState, candidate);
            runtimeState.PendingVisibleStateKey = string.Empty;
            runtimeState.PendingVisibleStateAtMs = 0;
            return;
        }

        ApplyVisibleRuntimeStateToResult(result, runtimeState);
    }

    private static BoneImportanceVisibleRuntimeState BuildVisibleRuntimeState(
        AdvancedBodyScalingBoneImportanceResult result,
        AdvancedBodyScalingSettings settings)
    {
        if (result.ActorTier == AdvancedBodyScalingBoneImportanceActorTier.Self &&
            settings.FullBoneImportanceOnSelf &&
            result.ModelDerivedActive)
        {
            var summary = result.RuntimeMode switch
            {
                AdvancedBodyScalingBoneImportanceRuntimeMode.Full => "Self BIW is pinned to full-priority mode.",
                AdvancedBodyScalingBoneImportanceRuntimeMode.Cached => "Self BIW is pinned to full-priority mode; cached model data was reused internally instead of rebuilding.",
                _ => "Self BIW is pinned to full-priority mode while internal crowd-safe bookkeeping reuses the current model-derived state."
            };

            return new BoneImportanceVisibleRuntimeState(
                "full",
                "self",
                true,
                false,
                false,
                summary);
        }

        return new BoneImportanceVisibleRuntimeState(
            result.RuntimeModeLabel,
            result.ActorTierLabel,
            result.FullQualityEligible,
            result.CrowdSafeDowngraded,
            result.StableThrottled,
            result.RuntimeSummary);
    }

    private static bool ShouldApplyVisibleRuntimeStateImmediately(
        AdvancedBodyScalingBoneImportanceRuntimeState runtimeState,
        BoneImportanceVisibleRuntimeState candidate)
    {
        if (!string.Equals(runtimeState.VisibleActorTierLabel, candidate.ActorTierLabel, StringComparison.Ordinal))
            return true;

        if (IsHighSignalVisibleMode(runtimeState.VisibleRuntimeModeLabel) || IsHighSignalVisibleMode(candidate.RuntimeModeLabel))
            return true;

        return false;
    }

    private static bool IsHighSignalVisibleMode(string modeLabel)
        => string.Equals(modeLabel, "full", StringComparison.Ordinal)
           || string.Equals(modeLabel, "reduced/coarse", StringComparison.Ordinal)
           || string.Equals(modeLabel, "heuristic fallback", StringComparison.Ordinal);

    private static int GetVisibleRuntimeStateDebounceMs(string currentModeLabel, string candidateModeLabel)
    {
        var currentLowActivity = IsLowActivityVisibleMode(currentModeLabel);
        var candidateLowActivity = IsLowActivityVisibleMode(candidateModeLabel);
        return currentLowActivity && candidateLowActivity
            ? BoneImportanceVisibleLowActivityDebounceMs
            : BoneImportanceVisibleStateDebounceMs;
    }

    private static bool IsLowActivityVisibleMode(string modeLabel)
        => string.Equals(modeLabel, "cached", StringComparison.Ordinal)
           || string.Equals(modeLabel, "cached-frozen", StringComparison.Ordinal)
           || string.Equals(modeLabel, "skipped", StringComparison.Ordinal)
           || string.Equals(modeLabel, "hard-skipped", StringComparison.Ordinal);

    private static void CommitVisibleRuntimeState(
        AdvancedBodyScalingBoneImportanceResult result,
        AdvancedBodyScalingBoneImportanceRuntimeState runtimeState,
        BoneImportanceVisibleRuntimeState state)
    {
        runtimeState.HasVisibleRuntimeState = true;
        runtimeState.VisibleStateKey = state.Key;
        runtimeState.VisibleRuntimeModeLabel = state.RuntimeModeLabel;
        runtimeState.VisibleActorTierLabel = state.ActorTierLabel;
        runtimeState.VisibleFullQualityEligible = state.FullQualityEligible;
        runtimeState.VisibleCrowdSafeDowngraded = state.CrowdSafeDowngraded;
        runtimeState.VisibleStableThrottled = state.StableThrottled;
        runtimeState.VisibleRuntimeSummary = state.RuntimeSummary;
        ApplyVisibleRuntimeStateToResult(result, runtimeState);
    }

    private static void ApplyVisibleRuntimeStateToResult(
        AdvancedBodyScalingBoneImportanceResult result,
        AdvancedBodyScalingBoneImportanceRuntimeState runtimeState)
    {
        result.UseVisibleRuntimeState = runtimeState.HasVisibleRuntimeState;
        result.DisplayRuntimeModeLabel = runtimeState.VisibleRuntimeModeLabel;
        result.DisplayActorTierLabel = runtimeState.VisibleActorTierLabel;
        result.DisplayFullQualityEligible = runtimeState.VisibleFullQualityEligible;
        result.DisplayCrowdSafeDowngraded = runtimeState.VisibleCrowdSafeDowngraded;
        result.DisplayStableThrottled = runtimeState.VisibleStableThrottled;
        result.DisplayRuntimeSummary = runtimeState.VisibleRuntimeSummary;
    }

    private bool ShouldActivelyManageBoneImportance(
        AdvancedBodyScalingSettings settings,
        AdvancedBodyScalingBoneImportanceActorTier tier,
        BoneImportanceFrameBudgetState budget)
        => tier switch
        {
            AdvancedBodyScalingBoneImportanceActorTier.Self => true,
            AdvancedBodyScalingBoneImportanceActorTier.ProfiledActor => true,
            _ => false,
        };

    private static bool ShouldUseReducedBoneImportance(
        AdvancedBodyScalingBoneImportanceActorTier tier,
        bool fullEligible)
        => tier switch
        {
            AdvancedBodyScalingBoneImportanceActorTier.Self => !fullEligible,
            AdvancedBodyScalingBoneImportanceActorTier.ProfiledActor => true,
            _ => false,
        };

    private static string BuildHardSkipFallbackReason(
        AdvancedBodyScalingBoneImportanceActorTier tier,
        AdvancedBodyScalingSettings settings,
        BoneImportanceFrameBudgetState budget,
        bool hadCachedModelResult)
        => tier switch
        {
            AdvancedBodyScalingBoneImportanceActorTier.TargetOrFocus
                => "Target/focus actors only receive active BIW when they are self or explicitly assigned to a profile, so this actor was returned to heuristic fallback.",
            AdvancedBodyScalingBoneImportanceActorTier.NearbyNonProfiled
                => "Nearby non-profiled actors are outside the active BIW set, so this actor was returned to heuristic fallback.",
            AdvancedBodyScalingBoneImportanceActorTier.Other when hadCachedModelResult
                => "This actor is outside the active BIW priority set, so its cached model-derived result was detached and heuristic fallback resumed.",
            _ => "This actor is outside the active BIW priority set, so crowd-safe BIW fell back to heuristics until relevance changes.",
        };

    private static string BuildHardSkipRefreshStatus(
        AdvancedBodyScalingBoneImportanceActorTier tier,
        AdvancedBodyScalingSettings settings,
        BoneImportanceFrameBudgetState budget,
        bool hadCachedModelResult)
        => tier switch
        {
            AdvancedBodyScalingBoneImportanceActorTier.TargetOrFocus
                => "Target/focus actors are outside the active BIW set unless they are self or explicitly profiled, so no live model-signature probe was scheduled.",
            AdvancedBodyScalingBoneImportanceActorTier.NearbyNonProfiled
                => "Nearby non-profiled actors are outside the active BIW set, so no live model-signature probe was scheduled.",
            AdvancedBodyScalingBoneImportanceActorTier.Other when hadCachedModelResult
                => "This non-important actor kept no active BIW work; its previous model-derived result was frozen out and no probe was scheduled until relevance changes.",
            _ => "This actor is outside the active BIW set, so no live model-signature probe was scheduled until relevance changes.",
        };

    private int GetBoneImportanceProbeIntervalMs(AdvancedBodyScalingBoneImportanceActorTier tier, int stableProbeCount)
    {
        var baseInterval = tier switch
        {
            AdvancedBodyScalingBoneImportanceActorTier.Self => SelfProbeIntervalMs,
            AdvancedBodyScalingBoneImportanceActorTier.ProfiledActor => ProfiledProbeIntervalMs,
            AdvancedBodyScalingBoneImportanceActorTier.TargetOrFocus => TargetProbeIntervalMs,
            AdvancedBodyScalingBoneImportanceActorTier.NearbyNonProfiled => NearbyProbeIntervalMs,
            _ => OtherProbeIntervalMs,
        };

        return ApplyStableProbeMultiplier(baseInterval, stableProbeCount);
    }

    private int GetBoneImportanceResolveIntervalMs(AdvancedBodyScalingBoneImportanceActorTier tier, int stableProbeCount)
    {
        var baseInterval = tier switch
        {
            AdvancedBodyScalingBoneImportanceActorTier.Self => SelfResolveIntervalMs,
            AdvancedBodyScalingBoneImportanceActorTier.ProfiledActor => ProfiledResolveIntervalMs,
            AdvancedBodyScalingBoneImportanceActorTier.TargetOrFocus => TargetResolveIntervalMs,
            AdvancedBodyScalingBoneImportanceActorTier.NearbyNonProfiled => NearbyResolveIntervalMs,
            _ => OtherResolveIntervalMs,
        };

        return ApplyStableProbeMultiplier(baseInterval, stableProbeCount);
    }

    private static int ApplyStableProbeMultiplier(int baseInterval, int stableProbeCount)
    {
        var multiplier = stableProbeCount switch
        {
            >= 6 => 2.5f,
            >= 3 => 1.6f,
            _ => 1f,
        };

        return (int)MathF.Round(baseInterval * multiplier);
    }

    private GlamourerAppearanceTransitionSnapshot TryGetGlamourerAppearanceTransition(Actor actor)
    {
        if (actor.AsObject == null)
            return GlamourerAppearanceTransitionSnapshot.None;

        return _glamourerIpcHandler.TryGetAppearanceTransitionSnapshot(actor.AsObject->ObjectIndex, (nint)actor.AsObject, out var snapshot)
            ? snapshot
            : GlamourerAppearanceTransitionSnapshot.None;
    }

    private AdvancedBodyScalingBoneImportanceActorTier ClassifyBoneImportanceTier(Armature armature, Actor actor)
    {
        if (IsLocalPlayerArmature(armature, actor))
            return AdvancedBodyScalingBoneImportanceActorTier.Self;

        if (IsExplicitlyProfiledActor(armature))
            return AdvancedBodyScalingBoneImportanceActorTier.ProfiledActor;

        if (IsTargetOrFocusActor(actor))
            return AdvancedBodyScalingBoneImportanceActorTier.TargetOrFocus;

        if (IsNearbyNonProfiledActor(actor))
            return AdvancedBodyScalingBoneImportanceActorTier.NearbyNonProfiled;

        return AdvancedBodyScalingBoneImportanceActorTier.Other;
    }

    private bool IsFullBoneImportanceEligible(
        AdvancedBodyScalingSettings settings,
        AdvancedBodyScalingBoneImportanceActorTier tier)
        => tier switch
        {
            AdvancedBodyScalingBoneImportanceActorTier.Self => settings.FullBoneImportanceOnSelf,
            AdvancedBodyScalingBoneImportanceActorTier.ProfiledActor => settings.FullBoneImportanceOnProfiledActors,
            _ => false,
        };

    private bool IsExplicitlyProfiledActor(Armature armature)
        => armature.Profile != _profileManager.DefaultProfile
           && armature.Profile != _profileManager.DefaultLocalPlayerProfile;

    private bool IsTargetOrFocusActor(Actor actor)
        => AreSameActor(actor, _objectManager.Target) || AreSameActor(actor, _objectManager.Focus);

    private bool IsNearbyNonProfiledActor(Actor actor)
    {
        var player = _objectManager.Player;
        if (!actor || !player || actor.AsObject == null || player.AsObject == null || AreSameActor(actor, player))
            return false;

        var dx = actor.AsObject->Position.X - player.AsObject->Position.X;
        var dy = actor.AsObject->Position.Y - player.AsObject->Position.Y;
        var dz = actor.AsObject->Position.Z - player.AsObject->Position.Z;
        var distanceSquared = (dx * dx) + (dy * dy) + (dz * dz);
        return distanceSquared <= NearbyFullBoneImportanceDistanceSquared;
    }

    private static bool AreSameActor(Actor left, Actor right)
        => left
           && right
           && left.AsObject != null
           && right.AsObject != null
           && left.AsObject->ObjectIndex == right.AsObject->ObjectIndex;

    private bool IsLocalPlayerArmature(Armature armature, Actor actor)
    {
        if (AreSameActor(actor, _objectManager.Player))
            return true;

        // During redraw, CharacterBase copies can have a different object index from the
        // current object-manager player instance. The armature identity remains stable.
        var localPlayerIdentifier = _objectManager.PlayerData.Identifier;
        return localPlayerIdentifier.IsValid && armature.ActorIdentifier.Equals(localPlayerIdentifier);
    }

    private static string BuildAppliedBoneImportanceBindingIdentity(
        AdvancedBodyScalingSettings? settings,
        AdvancedBodyScalingBoneImportanceResult? result)
    {
        if (settings == null ||
            !settings.Enabled ||
            settings.Mode == AdvancedBodyScalingMode.Manual ||
            !settings.ModelDerivedBoneImportanceEnabled ||
            settings.BoneImportanceHeuristicBlend <= ActiveBoneImportanceBlendEpsilon ||
            result == null ||
            !result.ModelDerivedActive ||
            result.Scores.Count == 0)
        {
            return "inactive";
        }

        var signature = string.IsNullOrWhiteSpace(result.ModelSignature)
            ? "nosignature"
            : result.ModelSignature;

        return $"{(int)result.Source}|{signature}|{settings.BoneImportanceHeuristicBlend:0.000}";
    }

    private static bool ShouldRetainHigherQualityBoneImportance(
        AdvancedBodyScalingBoneImportanceResult active,
        AdvancedBodyScalingBoneImportanceResult candidate)
        => active.ModelDerivedActive
           && candidate.ModelDerivedActive
           && string.Equals(active.ModelSignature, candidate.ModelSignature, StringComparison.OrdinalIgnoreCase)
           && active.Source is AdvancedBodyScalingBoneImportanceSource.ModelWeights or AdvancedBodyScalingBoneImportanceSource.MixedAggregate
           && candidate.Source == AdvancedBodyScalingBoneImportanceSource.CoarseParticipation;

    private static string BuildBoneImportanceBindingRefreshReason(
        string previousBindingIdentity,
        AdvancedBodyScalingBoneImportanceResult previousResult,
        string currentBindingIdentity,
        AdvancedBodyScalingBoneImportanceResult currentResult)
    {
        if (string.IsNullOrWhiteSpace(previousBindingIdentity))
            return "initial binding state";

        if (previousBindingIdentity == "inactive" && currentBindingIdentity != "inactive")
            return "model-derived BIW became active";

        if (previousBindingIdentity != "inactive" && currentBindingIdentity == "inactive")
            return "model-derived BIW became inactive";

        if (!string.Equals(previousResult.ModelSignature, currentResult.ModelSignature, StringComparison.OrdinalIgnoreCase))
            return "resolved model signature changed";

        if (previousResult.Source != currentResult.Source)
            return $"BIW source changed to {currentResult.SourceLabel}";

        return "effective BIW binding identity changed";
    }

    private static string BuildBoneImportanceSignatureChangeDetail(
        string previousModelSignature,
        string currentModelSignature)
    {
        if (string.IsNullOrWhiteSpace(previousModelSignature) || string.IsNullOrWhiteSpace(currentModelSignature))
            return string.Empty;

        if (string.Equals(previousModelSignature, currentModelSignature, StringComparison.OrdinalIgnoreCase))
            return string.Empty;

        var previousParts = ParseBoneImportanceSignature(previousModelSignature);
        var currentParts = ParseBoneImportanceSignature(currentModelSignature);
        if (previousParts.Count == 0 || currentParts.Count == 0)
            return string.Empty;

        var changedParts = previousParts.Keys
            .Concat(currentParts.Keys)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(key =>
            {
                previousParts.TryGetValue(key, out var previousPart);
                currentParts.TryGetValue(key, out var currentPart);
                if (string.Equals(previousPart.RawSegment, currentPart.RawSegment, StringComparison.OrdinalIgnoreCase))
                    return string.Empty;

                return $"{key}: {previousPart.DisplayLabel} -> {currentPart.DisplayLabel}";
            })
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Take(4)
            .ToList();

        return changedParts.Count == 0
            ? string.Empty
            : string.Join(" | ", changedParts);
    }

    private static bool IsTransitionalSlotBaselineSignatureChange(
        string previousModelSignature,
        string currentModelSignature)
    {
        var previousParts = ParseBoneImportanceSignature(previousModelSignature);
        var currentParts = ParseBoneImportanceSignature(currentModelSignature);
        if (previousParts.Count == 0 || currentParts.Count == 0)
            return false;

        var currentResolvedParts = currentParts.Values
            .Where(static part => !part.IsMissing)
            .ToList();
        if (currentResolvedParts.Count < 3)
            return false;

        if (!currentResolvedParts.All(static part => part.IsE0000SlotModel))
            return false;

        var previousResolvedParts = previousParts.Values
            .Where(static part => !part.IsMissing)
            .ToList();
        if (previousResolvedParts.Count == 0)
            return false;

        return previousResolvedParts.Any(static part => !part.IsE0000SlotModel);
    }

    private static Dictionary<string, BoneImportanceSignaturePart> ParseBoneImportanceSignature(string signature)
    {
        var parts = new Dictionary<string, BoneImportanceSignaturePart>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(signature))
            return parts;

        foreach (var segment in signature.Split("||", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parsed = ParseBoneImportanceSignaturePart(segment);
            parts[parsed.PartKey] = parsed;
        }

        return parts;
    }

    private static BoneImportanceSignaturePart ParseBoneImportanceSignaturePart(string segment)
    {
        if (string.IsNullOrWhiteSpace(segment))
            return BoneImportanceSignaturePart.Missing("unknown");

        var firstColon = segment.IndexOf(':');
        if (firstColon < 0)
            return new BoneImportanceSignaturePart(segment, segment, segment, false, IsE0000SlotModelSignatureDetail(segment));

        var partKey = segment[..firstColon];
        var remainder = segment[(firstColon + 1)..];
        if (string.Equals(remainder, "missing", StringComparison.OrdinalIgnoreCase))
            return BoneImportanceSignaturePart.Missing(partKey);

        var secondColonOffset = remainder.IndexOf(':');
        if (secondColonOffset < 0)
            return new BoneImportanceSignaturePart(partKey, segment, remainder, false, IsE0000SlotModelSignatureDetail(remainder));

        var source = remainder[..secondColonOffset];
        var detail = remainder[(secondColonOffset + 1)..];
        var displayDetail = detail;

        var stage2Index = detail.LastIndexOf(':');
        if (stage2Index > 0)
        {
            var maybeStage2 = detail[(stage2Index + 1)..];
            var stage1Slice = detail[..stage2Index];
            var stage1Index = stage1Slice.LastIndexOf(':');
            if (stage1Index > 0)
            {
                var maybeStage1 = stage1Slice[(stage1Index + 1)..];
                if (bool.TryParse(maybeStage1, out _) && bool.TryParse(maybeStage2, out _))
                    displayDetail = stage1Slice[..stage1Index];
            }
        }

        displayDetail = SummarizeBoneImportanceSignatureValue(displayDetail);
        return new BoneImportanceSignaturePart(
            partKey,
            segment,
            $"{source}/{displayDetail}",
            false,
            IsE0000SlotModelSignatureDetail(displayDetail));
    }

    private static string SummarizeBoneImportanceSignatureValue(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "none";

        var normalized = value.Replace('\\', '/');
        var segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (segments.Length == 0)
            return value;

        if (segments.Length == 1)
            return segments[0];

        return $"{segments[^2]}/{segments[^1]}";
    }

    private static bool IsE0000SlotModelSignatureDetail(string value)
        => !string.IsNullOrWhiteSpace(value)
           && (value.Contains("e0000_", StringComparison.OrdinalIgnoreCase)
               || value.Contains("/e0000/", StringComparison.OrdinalIgnoreCase));

    private readonly record struct BoneImportanceSignaturePart(
        string PartKey,
        string RawSegment,
        string DisplayLabel,
        bool IsMissing,
        bool IsE0000SlotModel)
    {
        public static BoneImportanceSignaturePart Missing(string partKey)
            => new(partKey, $"{partKey}:missing", "missing", true, false);
    }

    /// <summary>
    /// Returns whether or not a link can be established between the armature and an in-game object.
    /// If unbuilt, the armature will be rebuilded.
    /// </summary>
    private bool TryLinkSkeleton(Armature armature, BoneImportanceFrameBudgetState boneImportanceBudget)
    {
        var updateStarted = armature.PerformanceMetrics.Start();
        try
        {
        if (!_objectManager.TryGetValue(armature.ActorIdentifier, out var actorData) ||
            actorData.Objects == null ||
            actorData.Objects.Count == 0)
            return false;

        //we assume that all other objects are a copy of object #0
        var actor = actorData.Objects[0];

        var glamourerAppearanceTransition = TryGetGlamourerAppearanceTransition(actor);
        var appearanceTransitionState = glamourerAppearanceTransition.Phase switch
        {
            GlamourerAppearanceTransitionPhase.AwaitingFinalization => "awaiting finalization",
            GlamourerAppearanceTransitionPhase.Settling => "settling",
            _ => "idle",
        };
        var appearanceLifecycleEvent = armature.ObserveGlamourerAppearanceEpoch(
            glamourerAppearanceTransition.AppearanceEpoch,
            glamourerAppearanceTransition.Active,
            appearanceTransitionState,
            glamourerAppearanceTransition.OperationType,
            glamourerAppearanceTransition.FinalizedAtMs);
        if (!string.IsNullOrWhiteSpace(appearanceLifecycleEvent))
        {
            _logger.Debug($"{appearanceLifecycleEvent} for actor #{actor.AsObject->ObjectIndex}.");
            RecordSelfLifecycleTrace(armature, actor, appearanceLifecycleEvent, force: true, glamourerAppearanceTransition);
        }

        var hasAppearanceContext = TryGetActorAppearanceContext(actor, out var appearanceRace, out var appearanceClan, out var appearanceGender);
        var appearanceContextRefreshReason = string.Empty;
        var fallbackAppearanceContextRefresh = hasAppearanceContext && armature.ShouldRefreshForAppearanceContextFallback(
            appearanceRace,
            appearanceClan,
            appearanceGender,
            glamourerAppearanceTransition.Active,
            out appearanceContextRefreshReason);
        var stableAppearanceEpochRefresh = armature.TryGetPendingStableAppearanceRefresh(
            glamourerAppearanceTransition.Active,
            out var stableAppearanceEpoch,
            out var stableAppearanceRefreshReason);
        var appearanceContextRefresh = stableAppearanceEpochRefresh || fallbackAppearanceContextRefresh;
        var appearanceRefreshReason = stableAppearanceEpochRefresh
            ? stableAppearanceRefreshReason
            : appearanceContextRefreshReason;

        var advancedBodyScaling = ResolveAdvancedBodyScaling(armature.Profile, actor);
        var skeletonUpdated = armature.IsSkeletonUpdated(actor.Model.AsCharacterBase);
        // IsSkeletonUpdated intentionally returns false for an incomplete redraw: it cannot prove a
        // topology change, but the existing native links are still unsafe. Retry a validated
        // candidate publication at a bounded interval so a settled replacement can recover without
        // requiring a profile toggle.
        var bindingRecoveryDue = armature.IsBuilt
            && !armature.IsSkeletonBindingCurrent
            && armature.ShouldAttemptInvalidBindingRecovery();
        var bindingUnsafe = !armature.IsBuilt || skeletonUpdated || !armature.IsSkeletonBindingCurrent;
        var needsValidatedPublication = !armature.IsBuilt || skeletonUpdated || bindingRecoveryDue;
        // Do not carry model-derived evidence across a pending skeleton publication. A successful
        // publication below performs one forced, bounded refresh against the new live model.
        var boneImportance = bindingUnsafe
            ? AdvancedBodyScalingBoneImportanceResult.CreateFallback(
                "Waiting for the current validated armature publication before refreshing model-derived weighting.",
                enabled: advancedBodyScaling.ModelDerivedBoneImportanceEnabled,
                preferSkinWeights: advancedBodyScaling.PreferTrueSkinWeightImportance,
                heuristicBlend: advancedBodyScaling.BoneImportanceHeuristicBlend)
            : EvaluateBoneImportanceForArmature(
                armature,
                actor,
                advancedBodyScaling,
                boneImportanceBudget,
                skeletonUpdated,
                glamourerAppearanceTransition,
                forceRefresh: appearanceContextRefresh);

        // A crowd budget may make a profiled actor eligible for a reduced resolve after it has
        // already obtained a stable skin-weight map. Do not replace that stronger map with coarse
        // participation when the resolved model identity is unchanged; doing so only rebound the
        // same template and made the solver oscillate between equivalent crowd-budget passes.
        if (!bindingUnsafe &&
            !appearanceContextRefresh &&
            ShouldRetainHigherQualityBoneImportance(armature.ActiveBoneImportanceResult, boneImportance))
        {
            boneImportance = armature.ActiveBoneImportanceResult;
        }

        var previousBindingIdentity = BuildAppliedBoneImportanceBindingIdentity(armature.ActiveAdvancedBodyScalingSettings, armature.ActiveBoneImportanceResult);
        var currentBindingIdentity = BuildAppliedBoneImportanceBindingIdentity(advancedBodyScaling, boneImportance);
        var boneImportanceBindingChanged = !string.Equals(previousBindingIdentity, currentBindingIdentity, StringComparison.Ordinal);
        if (needsValidatedPublication || (!bindingUnsafe && (boneImportanceBindingChanged || appearanceContextRefresh)))
        {
            if (needsValidatedPublication)
            {
                var previousRevision = armature.SkeletonRevision;
                var reacquisitionPublication = armature.IsAwaitingActorReacquisitionPublication;
                var published = armature.RebuildSkeleton(
                    actor.Model.AsCharacterBase,
                    _configuration.RuntimeSafetySettings.SoftScaleLimitsEnabled,
                    _configuration.RuntimeSafetySettings.AutomaticChildScaleCompensationEnabled,
                    advancedBodyScaling,
                    boneImportance);
                if (published && armature.SkeletonRevision != previousRevision)
                {
                    // The profile remains owned by the armature, but its ModelBone links and
                    // model-derived weighting must be rebuilt for this exact native generation.
                    var refreshedBoneImportance = EvaluateBoneImportanceForArmature(
                        armature,
                        actor,
                        advancedBodyScaling,
                        boneImportanceBudget,
                        skeletonUpdated: true,
                        glamourerAppearanceTransition: glamourerAppearanceTransition,
                        forceRefresh: true);
                    armature.RebuildBoneTemplateBinding(
                        _configuration.RuntimeSafetySettings.SoftScaleLimitsEnabled,
                        _configuration.RuntimeSafetySettings.AutomaticChildScaleCompensationEnabled,
                        advancedBodyScaling,
                        refreshedBoneImportance,
                        "post-publication model refresh");
                    if (hasAppearanceContext)
                        armature.MarkAppearanceContextBindingApplied(appearanceRace, appearanceClan, appearanceGender);
                    if (glamourerAppearanceTransition.Active)
                    {
                        _logger.Debug($"IntermediateArmaturePublishedDuringAppearance epoch {armature.CurrentAppearanceEpoch} for actor #{actor.AsObject->ObjectIndex}; stable appearance work remains pending.");
                        RecordSelfLifecycleTrace(armature, actor, "IntermediateArmaturePublishedDuringAppearance", force: true, glamourerAppearanceTransition);
                    }
                    else if (stableAppearanceEpochRefresh)
                    {
                        armature.MarkStableAppearanceEpochApplied(stableAppearanceEpoch, "validated armature publication");
                        _logger.Debug($"StableAppearanceRebindCompleted epoch {stableAppearanceEpoch} for actor #{actor.AsObject->ObjectIndex} after validated armature publication.");
                        RecordSelfLifecycleTrace(armature, actor, "StableAppearanceRebindCompleted", force: true, glamourerAppearanceTransition);
                    }
                    _logger.Debug($"Published armature revision {armature.SkeletonRevision} (native binding generation {armature.NativeBindingGeneration}) for {armature}; rebuilt current profile bindings and model-derived state.");
                    RecordSelfLifecycleTrace(armature, actor,
                        reacquisitionPublication ? "actor reacquisition publication" : "armature publication",
                        force: true,
                        glamourerAppearanceTransition);
                }
            }
            else
            {
                var refreshReason = appearanceContextRefresh
                    ? appearanceRefreshReason
                    : BuildBoneImportanceBindingRefreshReason(
                        previousBindingIdentity,
                        armature.ActiveBoneImportanceResult,
                        currentBindingIdentity,
                        boneImportance);
                var signatureChangeDetail = string.Equals(refreshReason, "resolved model signature changed", StringComparison.Ordinal)
                    ? BuildBoneImportanceSignatureChangeDetail(
                        armature.ActiveBoneImportanceResult.ModelSignature,
                        boneImportance.ModelSignature)
                    : string.Empty;
                _logger.Debug($"Refreshing bone-importance bindings for actor #{actor.AsObject->ObjectIndex} tied to \"{armature}\" because {refreshReason}{(string.IsNullOrWhiteSpace(signatureChangeDetail) ? string.Empty : $" [{signatureChangeDetail}]")} ({boneImportance.VisibleRuntimeModeLabel}, {boneImportance.VisibleActorTierLabel}).");
                armature.RebuildBoneTemplateBinding(
                    _configuration.RuntimeSafetySettings.SoftScaleLimitsEnabled,
                    _configuration.RuntimeSafetySettings.AutomaticChildScaleCompensationEnabled,
                    advancedBodyScaling,
                    boneImportance,
                    $"binding identity: {refreshReason}");
                if (hasAppearanceContext && !glamourerAppearanceTransition.Active)
                    armature.MarkAppearanceContextBindingApplied(appearanceRace, appearanceClan, appearanceGender);
                if (stableAppearanceEpochRefresh)
                {
                    armature.MarkStableAppearanceEpochApplied(stableAppearanceEpoch, "stable appearance binding refresh");
                    _logger.Debug($"StableAppearanceRebindCompleted epoch {stableAppearanceEpoch} for actor #{actor.AsObject->ObjectIndex}.");
                }
                RecordSelfLifecycleTrace(armature, actor,
                    stableAppearanceEpochRefresh ? "StableAppearanceRebindCompleted"
                    : fallbackAppearanceContextRefresh ? "appearance-context/template binding refresh"
                    : "BIW/template binding refresh",
                    force: true,
                    glamourerAppearanceTransition);
            }
        }
        RecordSelfLifecycleTrace(armature, actor, "lifecycle state", force: false, glamourerAppearanceTransition);
        return true;
        }
        finally
        {
            armature.PerformanceMetrics.Record("armature-update-check", updateStarted);
        }
    }

    private void RecordSelfLifecycleTrace(
        Armature armature,
        Actor actor,
        string reason,
        bool force,
        GlamourerAppearanceTransitionSnapshot glamourerTransition)
    {
#if !DEBUG
        return;
#else
        if (!_configuration.DebuggingModeEnabled || !AreSameActor(actor, _objectManager.Player) || actor.AsObject == null)
            return;

        unsafe
        {
            var cBase = actor.Model.AsCharacterBase;
            var skeleton = cBase == null ? null : cBase->Skeleton;
            var partials = new List<string>();
            if (skeleton != null)
            {
                for (var partialIndex = 0; partialIndex < Math.Min((int)skeleton->PartialSkeletonCount, 4); partialIndex++)
                {
                    var pose = skeleton->PartialSkeletons[partialIndex].GetHavokPose(Constants.TruePoseIndex);
                    var poseSkeleton = pose == null ? 0 : (nuint)pose->Skeleton;
                    partials.Add($"p{partialIndex}=0x{(nuint)pose:X}/sk=0x{poseSkeleton:X}");
                }
            }

            var customize = actor.Customize;
            var modelIdentity = customize == null
                ? "customize unavailable"
                : $"race={customize->Race}; clan={customize->Clan}; gender={customize->Gender}";
            var nativeIdentity = $"actor=0x{(nuint)actor.AsObject:X}; model/draw=0x{(nuint)actor.Model.Address:X}; cbase=0x{(nuint)cBase:X}; skeleton=0x{(nuint)skeleton:X}; {string.Join(", ", partials)}";
            var manifest = armature.GetCapabilityManifestSnapshot();
            var profile = armature.Profile;
            var templateIds = profile.Templates
                .Take(6)
                .Select(template => $"{template.UniqueId.ToString()[..8]}:{(!profile.DisabledTemplates.Contains(template.UniqueId) ? "on" : "off")}");
            var glamourer = glamourerTransition.Active ? glamourerTransition.Summary : "none";
            // Summary contains a settling countdown. Only the categorical phase belongs in the deduplication key.
            var glamourerPhase = glamourerTransition.Active
                ? glamourerTransition.AwaitingFinalization ? "awaiting-finalization"
                    : glamourerTransition.FinalizationSettling ? "finalization-settling"
                    : "appearance-transition"
                : "none";
            var stateKey = string.Join("|", new[]
            {
                modelIdentity,
                nativeIdentity,
                manifest.StructuralFingerprint ?? string.Empty,
                armature.SkeletonRevision.ToString(),
                armature.NativeBindingGeneration.ToString(),
                armature.IsBuilt.ToString(),
                armature.IsSkeletonBindingCurrent.ToString(),
                armature.PendingPublicationIdentity ?? string.Empty,
                armature.PendingPublicationObservations.ToString(),
                profile.UniqueId.ToString(),
                profile.Enabled.ToString(),
                armature.TemplateBindingRevision.ToString(),
                armature.ResolvedBoneTransforms.Count.ToString(),
                armature.BoundModelBoneCount.ToString(),
                armature.ActiveBones.Count.ToString(),
                armature.IsPendingProfileRebind.ToString(),
                armature.ActiveBoneImportanceResult.ModelSignature ?? string.Empty,
                glamourerPhase,
                armature.CurrentAppearanceEpoch.ToString(),
                armature.AppearanceEpochState,
                armature.LatestPendingStableAppearanceEpoch.ToString(),
                armature.LastAppliedStableAppearanceEpoch.ToString(),
            });

            var entry = new ArmatureLifecycleTraceEntry(
                Sequence: 0,
                TimestampMs: Environment.TickCount64,
                Frame: _debugLifecycleFrame,
                Reason: reason,
                ActorIdentity: $"index={actor.AsObject->ObjectIndex}; address=0x{(nuint)actor.AsObject:X}",
                ModelIdentity: modelIdentity,
                NativeIdentity: nativeIdentity,
                StructuralFingerprint: string.IsNullOrWhiteSpace(manifest.StructuralFingerprint) ? "unavailable" : manifest.StructuralFingerprint,
                SkeletonRevision: armature.SkeletonRevision,
                NativeBindingGeneration: armature.NativeBindingGeneration,
                IsBuilt: armature.IsBuilt,
                BindingCurrent: armature.IsSkeletonBindingCurrent,
                PendingPublication: armature.PendingPublicationIdentity ?? "none",
                PendingObservations: armature.PendingPublicationObservations,
                ProfileIdentity: $"{profile.UniqueId.ToString()[..8]}; enabled={profile.Enabled}",
                TemplateSummary: $"assigned={profile.Templates.Count}; {string.Join(", ", templateIds)}",
                ResolvedTransformCount: armature.ResolvedBoneTransforms.Count,
                BoundModelBoneCount: armature.BoundModelBoneCount,
                ActiveModelBoneCount: armature.ActiveBones.Count,
                TemplateBindingRevision: armature.TemplateBindingRevision,
                PendingProfileRebind: armature.IsPendingProfileRebind,
                BoneImportanceSignature: string.IsNullOrWhiteSpace(armature.ActiveBoneImportanceResult.ModelSignature) ? "unavailable" : armature.ActiveBoneImportanceResult.ModelSignature,
                GlamourerTransition: glamourer,
                NativeWrites: armature.GetDebugNativeWriteDiagnostics(),
                StateKey: stateKey);
            _selfLifecycleTrace.Record(entry, force);
        }
#endif
    }

    /// <summary>
    /// Iterate through the skeleton of the given character base, and apply any transformations
    /// for which this armature contains corresponding model bones. This method of application
    /// is safer but more computationally costly
    /// </summary>
    private void ApplyPiecewiseTransformation(Armature armature, Actor actor, ActorIdentifier actorIdentifier, float deltaSeconds)
    {
        var nativeApplicationStarted = armature.PerformanceMetrics.Start();
        try
        {
        var cBase = actor.Model.AsCharacterBase;
        if (_configuration.DebuggingModeEnabled && AreSameActor(actor, _objectManager.Player))
        {
            armature.SetDebugNativeWriteDiagnosticsEnabled(true);
            armature.BeginDebugNativeWriteFrame(Math.Max(armature.ActiveBones.Count - 1, 0));
        }

        var isMount = actorIdentifier.Type == IdentifierType.Owned &&
            actorIdentifier.Kind == Dalamud.Game.ClientState.Objects.Enums.ObjectKind.Mount;

        Actor? mountOwner = null;
        Armature? mountOwnerArmature = null;
        if (isMount)
        {
            (var ident, mountOwner) = _gameObjectService.FindActorsByName(actorIdentifier.PlayerName.ToString()).FirstOrDefault();
            Armatures.TryGetValue(ident, out mountOwnerArmature);
        }

        if (cBase != null)
        {
            var drawObject = cBase->DrawObject.Object;

            // Final runtime order:
            // 1. Base/profile/template transforms
            // 2. Advanced body scaling output
            // 3. Runtime safeguards
            // 4. RBF pose-space corrective support
            // 5. Full IK retargeting adaptation layer
            // 6. Motion warping locomotion layer
            // 7. Full-body IK final pose solve
            armature.EvaluatePoseCorrectives(cBase);

            var modelBoneApplicationStarted = armature.PerformanceMetrics.Start();
            try
            {
                foreach (var mb in armature.ActiveBones)
                {
                    if (mb == armature.MainRootBone)
                    {
                        var appliedTransform = mb.AppliedTransform;
                        var requestedScale = appliedTransform?.Scaling ?? Vector3.One;
                        var rootScaleModified = appliedTransform != null
                            && TransformSafety.IsFinite(requestedScale)
                            && mb.IsModifiedScale();
                        var actorEligibleForRootScale = _gameObjectService.IsActorHasScalableRoot(actor);
                        var observedBefore = Vector3.One;
                        var drawScale = cBase->DrawObject.Object.Scale;
                        observedBefore = new Vector3(drawScale.X, drawScale.Y, drawScale.Z);

                        var rootScaleApplied = false;
                        if (actorEligibleForRootScale && rootScaleModified)
                        {
                            // Root scaling belongs to the character draw object. This is the same
                            // transform boundary equipment and weapon attachments inherit.
                            cBase->DrawObject.Object.Scale = requestedScale;
                            cBase->DrawObject.Object.IsTransformChanged = true;
                            rootScaleApplied = true;
                        }

                        if (actorEligibleForRootScale)
                        {
                            //Fix mount owner's scale if needed
                            //todo: always keep owner's scale proper instead of scaling with mount if no armature found
                            if (isMount && mountOwner != null && mountOwnerArmature != null)
                            {
                                var ownerDrawObject = drawObject.ChildObject;

                                //limit to only modified scales because that is just easier to handle
                                //because we don't need to hook into dismount code to reset character scale
                                //todo: hook into dismount
                                //https://github.com/Cytraen/SeatedSidekickSpectator/blob/main/SetModeHook.cs?
                                if (drawObject.ChildObject == mountOwner.Value.Model &&
                                    ownerDrawObject != null &&
                                    mountOwnerArmature.MainRootBone.IsModifiedScale() &&
                                    mountOwnerArmature.MainRootBone.AppliedTransform != null)
                                {
                                    var baseScale = mountOwnerArmature.MainRootBone.AppliedTransform!.Scaling;
                                    var mountScale = rootScaleApplied ? requestedScale : observedBefore;
                                    if (TransformSafety.TryDivide(
                                            baseScale,
                                            new Vector3(mountScale.X, mountScale.Y, mountScale.Z),
                                            out var correctedOwnerScale))
                                    {
                                        ownerDrawObject->Scale = new Vector3(
                                            MathF.Abs(correctedOwnerScale.X),
                                            MathF.Abs(correctedOwnerScale.Y),
                                            MathF.Abs(correctedOwnerScale.Z));
                                        ownerDrawObject->IsTransformChanged = true;
                                    }
                                }
                            }
                        }

                        armature.RecordRootScaleApplication(
                            rootScaleModified,
                            actorEligibleForRootScale,
                            observedBefore,
                            requestedScale,
                            rootScaleApplied ? requestedScale : observedBefore,
                            rootScaleApplied);
                    }
                    else
                    {
                        mb.ApplyModelTransform(cBase);
                    }
                }

#if DEBUG
                foreach (var diagnosticBone in armature.GetDebugPoseCorrectiveValidationBones())
                    diagnosticBone.ApplyDebugPoseCorrectiveValidationTransform(cBase);
                armature.CompleteDebugPoseCorrectiveValidationFrame();
#endif

            }
            finally
            {
                armature.PerformanceMetrics.Record("modelbone-application", modelBoneApplicationStarted);
            }

            var optionalLayersStarted = armature.PerformanceMetrics.Start();
            try
            {
            armature.EvaluateAndApplyFullIkRetargeting(cBase, deltaSeconds);
            armature.EvaluateAndApplyMotionWarping(cBase, deltaSeconds);
            // Keep the player's corrective solve fully responsive. Profiled non-self actors
            // reuse their latest smoothed Full-Body IK output between 30 Hz solves.
            armature.EvaluateAndApplyFullBodyIk(cBase, deltaSeconds, useReducedCadence: !IsLocalPlayerArmature(armature, actor));
            }
            finally
            {
                armature.PerformanceMetrics.Record("optional-corrective-layers", optionalLayersStarted);
            }
        }
        }
        finally
        {
            armature.PerformanceMetrics.Record("native-transform-application", nativeApplicationStarted);
        }
    }

    /// <summary>
    /// Apply root bone translation. If reset = true then this will forcibly reset translation to in-game value.
    /// </summary>
    private void ApplyRootTranslation(Armature arm, Actor actor, bool reset = false)
    {
        //I'm honestly not sure if we should or even can check if cBase->DrawObject or cBase->DrawObject.Object is a valid object
        //So for now let's assume we don't need to check for that

        //2024/11/21: we no longer check cBase->DrawObject.IsVisible here so we can set object position in render hook.

        var cBase = actor.Model.AsCharacterBase;
        if (cBase != null && actor.AsObject != null)
        {
            var drawObject = cBase->DrawObject.Object;
            var actorObject = actor.AsObject;
            var actorPosition = actorObject->Position;
            if (!TransformSafety.IsFinite(actorPosition.X)
                || !TransformSafety.IsFinite(actorPosition.Y)
                || !TransformSafety.IsFinite(actorPosition.Z))
                return;

            if (reset)
            {
                drawObject.Position = actorPosition;
                return;
            }

            //warn: hotpath for characters with n_root edits. IsApproximately might have some performance hit.
            var rootBoneTransform = arm.GetAppliedBoneTransform("n_root");
            if (rootBoneTransform == null ||
                !TransformSafety.IsFinite(rootBoneTransform.Translation) ||
                rootBoneTransform.Translation.IsApproximately(Vector3.Zero, 0.00001f))
                return;

            if (rootBoneTransform.Translation.X == 0 &&
                rootBoneTransform.Translation.Y == 0 &&
                rootBoneTransform.Translation.Z == 0)
                return;

            //Reset position so we don't fly away
            drawObject.Position = actorPosition;

            var newPosition = new FFXIVClientStructs.FFXIV.Common.Math.Vector3
            {
                X = drawObject.Position.X + rootBoneTransform.Translation.X,
                Y = drawObject.Position.Y + rootBoneTransform.Translation.Y,
                Z = drawObject.Position.Z + rootBoneTransform.Translation.Z
            };

            if (TransformSafety.IsFinite(newPosition.X)
                && TransformSafety.IsFinite(newPosition.Y)
                && TransformSafety.IsFinite(newPosition.Z))
            {
                drawObject.Position = newPosition;
            }
        }
    }

    private void RemoveArmature(Armature armature, ArmatureChanged.DeletionReason reason)
    {
        armature.Profile.Armatures.Remove(armature);
        Armatures.Remove(armature.ActorIdentifier);
        _actorFailureStates.Remove(armature.ActorIdentifier);
        _logger.Debug($"Armature {armature} removed from cache");

        _event.Invoke(ArmatureChanged.Type.Deleted, armature, reason);
    }

    private void OnTemplateChange(TemplateChanged.Type type, Templates.Data.Template? template, object? arg3)
    {
        if (type is not TemplateChanged.Type.NewBone &&
            type is not TemplateChanged.Type.UpdatedBone &&
            type is not TemplateChanged.Type.DeletedBone &&
            type is not TemplateChanged.Type.EditorCharacterChanged &&
            type is not TemplateChanged.Type.EditorContextChanged &&
            type is not TemplateChanged.Type.EditorEnabled &&
            type is not TemplateChanged.Type.EditorDisabled)
            return;

        if (type == TemplateChanged.Type.NewBone ||
            type == TemplateChanged.Type.UpdatedBone ||
            type == TemplateChanged.Type.DeletedBone) //type == TemplateChanged.Type.EditorCharacterChanged?
        {
            if (template == null)
                return;

            //In case a lot of events are triggered at the same time for the same template this should limit the amount of times bindings are unneccessary rebuilt
            _framework.RegisterImportant($"TemplateRebuild @ {template.UniqueId}", () =>
            {
                foreach (var profile in _profileManager.GetProfilesUsingTemplate(template))
                {
                    _logger.Debug($"ArmatureManager.OnTemplateChange New/Deleted bone or character changed: {type}, template: {template.Name.Text.Incognify()}, profile: {profile.Name.Text.Incognify()}->{profile.Enabled}->{profile.Armatures.Count} armatures");
                    if (!profile.Enabled || profile.Armatures.Count == 0)
                        continue;

                    profile.Armatures.ForEach(x => x.IsPendingProfileRebind = true);
                }
            });

            return;
        }

        if (type == TemplateChanged.Type.EditorCharacterChanged)
        {
            if (arg3 is not ValueTuple<ActorIdentifier, Profile> payload)
                return;

            var (character, profile) = payload;

            foreach (var armature in GetArmaturesForCharacter(character))
            {
                armature.IsPendingProfileRebind = true;
                _logger.Debug($"ArmatureManager.OnTemplateChange Editor profile character name changed, armature rebind scheduled: {type}, {armature}");
            }

            if (profile.Armatures.Count == 0)
                return;

            //Rebuild armatures for previous character
            foreach (var armature in profile.Armatures)
                armature.IsPendingProfileRebind = true;

            _logger.Debug($"ArmatureManager.OnTemplateChange Editor profile character name changed, armature rebind scheduled: {type}, profile: {profile.Name.Text.Incognify()}->{profile.Enabled}, new name: {character.Incognito(null)}");

            return;
        }

        if (type == TemplateChanged.Type.EditorContextChanged)
        {
            if (arg3 is not ValueTuple<ActorIdentifier, Profile> payload)
                return;

            var (character, profile) = payload;

            foreach (var armature in GetArmaturesForCharacter(character))
            {
                armature.IsPendingProfileRebind = true;
                _logger.Debug($"ArmatureManager.OnTemplateChange editor context changed, armature rebind scheduled: {type}, {armature}");
            }

            if (profile.Armatures.Count == 0)
                return;

            foreach (var armature in profile.Armatures)
                armature.IsPendingProfileRebind = true;

            _logger.Debug($"ArmatureManager.OnTemplateChange editor context changed, armature rebind scheduled: {type}, profile: {profile.Name.Text.Incognify()}->{profile.Enabled}");

            return;
        }

        if (type == TemplateChanged.Type.EditorEnabled ||
            type == TemplateChanged.Type.EditorDisabled)
        {
            ActorIdentifier actor;
            bool hasChanges;

            if (type == TemplateChanged.Type.EditorEnabled)
            {
                if (arg3 is not ActorIdentifier enabledActor)
                    return;

                actor = enabledActor;
            }
            else
            {
                if (arg3 is not ValueTuple<ActorIdentifier, bool> editorPayload)
                    return;

                (actor, hasChanges) = editorPayload;
            }

            foreach (var armature in GetArmaturesForCharacter(actor))
            {
                armature.IsPendingProfileRebind = true;
                _logger.Debug($"ArmatureManager.OnTemplateChange template editor enabled/disabled: {type}, pending profile set for {armature}");
            }

            return;
        }
    }

    private void OnProfileChange(ProfileChanged.Type type, Profile? profile, object? arg3)
    {
        if (type is not ProfileChanged.Type.AddedTemplate &&
            type is not ProfileChanged.Type.RemovedTemplate &&
            type is not ProfileChanged.Type.EnabledTemplate &&
            type is not ProfileChanged.Type.DisabledTemplate &&
            type is not ProfileChanged.Type.MovedTemplate &&
            type is not ProfileChanged.Type.ChangedTemplate &&
            type is not ProfileChanged.Type.TemplateWeightChanged &&
            type is not ProfileChanged.Type.TemplateCompatibilityChanged &&
            type is not ProfileChanged.Type.AdvancedBodyScalingSettingsChanged &&
            type is not ProfileChanged.Type.Toggled &&
            type is not ProfileChanged.Type.Deleted &&
            type is not ProfileChanged.Type.TemporaryProfileAdded &&
            type is not ProfileChanged.Type.TemporaryProfileDeleted &&
            type is not ProfileChanged.Type.AddedCharacter &&
            type is not ProfileChanged.Type.RemovedCharacter &&
            type is not ProfileChanged.Type.PriorityChanged &&
            type is not ProfileChanged.Type.ChangedDefaultProfile &&
            type is not ProfileChanged.Type.ChangedDefaultLocalPlayerProfile)
            return;

        if (type == ProfileChanged.Type.ChangedDefaultProfile || type == ProfileChanged.Type.ChangedDefaultLocalPlayerProfile)
        {
            var oldProfile = (Profile?)arg3;

            if (oldProfile == null || oldProfile.Armatures.Count == 0)
                return;

            foreach (var armature in oldProfile.Armatures)
                armature.IsPendingProfileRebind = true;

            _logger.Debug($"ArmatureManager.OnProfileChange Profile no longer default/default for local player, armatures rebind scheduled: {type}, old profile: {oldProfile.Name.Text.Incognify()}->{oldProfile.Enabled}");

            return;
        }

        if (profile == null)
        {
            _logger.Error($"ArmatureManager.OnProfileChange Invalid input for event: {type}, profile is null.");
            return;
        }

        if(type == ProfileChanged.Type.PriorityChanged)
        {
            if (!profile.Enabled)
                return;

            foreach (var character in profile.Characters)
            {
                if (!character.IsValid)
                    continue;

                foreach (var armature in GetArmaturesForCharacter(character))
                {
                    armature.IsPendingProfileRebind = true;
                    _logger.Debug($"ArmatureManager.OnProfileChange profile {profile} priority changed, planning rebind for armature {armature}");
                }
            }

            return;
        }

        if (type == ProfileChanged.Type.Toggled)
        {
            if (!profile.Enabled && profile.Armatures.Count == 0)
                return;

            if (profile == _profileManager.DefaultProfile ||
                profile == _profileManager.DefaultLocalPlayerProfile)
            {
                foreach (var kvPair in Armatures)
                {
                    var armature = kvPair.Value;
                    if (armature.Profile == _profileManager.DefaultProfile || //not the best solution but w/e
                        armature.Profile == _profileManager.DefaultLocalPlayerProfile)
                        armature.IsPendingProfileRebind = true;

                    _logger.Debug($"ArmatureManager.OnProfileChange default/default local player profile toggled, planning rebind for armature {armature}");
                }

                return;
            }

            foreach(var character in profile.Characters)
            {
                if (!character.IsValid)
                    continue;

                foreach (var armature in GetArmaturesForCharacter(character))
                {
                    armature.IsPendingProfileRebind = true;
                    _logger.Debug($"ArmatureManager.OnProfileChange profile {profile} toggled, planning rebind for armature {armature}");
                }
            }

            return;
        }

        if (type == ProfileChanged.Type.TemporaryProfileAdded)
        {
            foreach(var character in profile.Characters)
            {
                if (!character.IsValid || !Armatures.ContainsKey(character))
                    continue;

                var armature = Armatures[character];

                if (armature.Profile == profile)
                    return;

                armature.UpdateLastSeen();

                armature.IsPendingProfileRebind = true;
            }

            _logger.Debug($"ArmatureManager.OnProfileChange TemporaryProfileAdded, calling rebind for existing armature: {type}, data payload: {arg3?.ToString()}, profile: {profile.Name.Text.Incognify()}->{profile.Enabled}");

            return;
        }

        if (type == ProfileChanged.Type.AddedCharacter ||
            type == ProfileChanged.Type.RemovedCharacter)
        {
            if (arg3 == null)
                throw new InvalidOperationException("AddedCharacter/RemovedCharacter must supply actor identifier as an argument");

            ActorIdentifier actorIdentifier = (ActorIdentifier)arg3;
            if (!actorIdentifier.IsValid)
                return;

            foreach (var armature in GetArmaturesForCharacter(actorIdentifier))
                armature.IsPendingProfileRebind = true;

            _logger.Debug($"ArmatureManager.OnProfileChange AC/RC, armature rebind scheduled: {type}, data payload: {arg3?.ToString()?.Incognify()}, profile: {profile.Name.Text.Incognify()}->{profile.Enabled}");
            
            return;
        }

        if (type == ProfileChanged.Type.Deleted ||
            type == ProfileChanged.Type.TemporaryProfileDeleted)
        {
            if (profile.Armatures.Count == 0)
                return;

            foreach (var armature in profile.Armatures)
            {
                if (type == ProfileChanged.Type.TemporaryProfileDeleted)
                    armature.UpdateLastSeen(); //just to be safe

                armature.IsPendingProfileRebind = true;
            }

            _logger.Debug($"ArmatureManager.OnProfileChange DEL/TPD, armature rebind scheduled: {type}, data payload: {arg3?.ToString()?.Incognify()}, profile: {profile.Name.Text.Incognify()}->{profile.Enabled}");

            return;
        }

        //todo: shouldn't happen, but happens sometimes? I think?
        if (profile.Armatures.Count == 0)
            return;

        _logger.Debug($"ArmatureManager.OnProfileChange Added/Deleted/Moved/Changed template: {type}, data payload: {arg3?.ToString()}, profile: {profile.Name}->{profile.Enabled}->{profile.Armatures.Count} armatures");

        profile!.Armatures.ForEach(x => x.IsPendingProfileRebind = true);
    }

    /// <summary>
    /// Warn: should not be used for temporary profiles as this limits search for Type = Owned to things owned by local player.
    /// </summary>
    private IEnumerable<Armature> GetArmaturesForCharacter(ActorIdentifier actorIdentifier)
    {
        foreach (var kvPair in Armatures)
        {
            (var armatureActorIdentifier, _) = _gameObjectService.GetTrueActorForSpecialTypeActor(kvPair.Key);

            if (actorIdentifier.IsValid && armatureActorIdentifier.MatchesIgnoringOwnership(actorIdentifier) &&
                (armatureActorIdentifier.Type != IdentifierType.Owned || armatureActorIdentifier.IsOwnedByLocalPlayer()))
                yield return kvPair.Value;
        }
    }
}
