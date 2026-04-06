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
    private const float NearbyFullBoneImportanceDistance = 12f;
    private const float NearbyFullBoneImportanceDistanceSquared = NearbyFullBoneImportanceDistance * NearbyFullBoneImportanceDistance;
    private const float ActiveBoneImportanceBlendEpsilon = 0.0001f;
    private const int SelfProbeIntervalMs = 250;
    private const int ProfiledProbeIntervalMs = 500;
    private const int TargetProbeIntervalMs = 500;
    private const int NearbyProbeIntervalMs = 1200;
    private const int OtherProbeIntervalMs = 2200;
    private const int SelfResolveIntervalMs = 500;
    private const int ProfiledResolveIntervalMs = 1100;
    private const int TargetResolveIntervalMs = 1400;
    private const int NearbyResolveIntervalMs = 2600;
    private const int OtherResolveIntervalMs = 4200;
    private const int BoneImportanceVisibleStateDebounceMs = 900;
    private const int BoneImportanceVisibleLowActivityDebounceMs = 1200;

    /// <summary>
    /// This is a movement flag for every object. Used to prevent calls to ApplyRootTranslation from both movement and render hooks.
    /// Sized dynamically because object table indices are not a stable contract.
    /// </summary>
    private bool[] _objectMovementFlagsArr = new bool[1024];
    private DateTime _lastRenderAtUtc;

    private sealed class BoneImportanceFrameBudgetState
    {
        private int _profiledFullRemaining;
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
        AdvancedBodyScalingBoneImportanceService boneImportanceService)
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

        if (Armatures.TryGetValue(identifier, out var armature) && armature.IsBuilt && armature.IsVisible)
        {
            EnsureObjectMovementFlagCapacity(actor.AsObject->ObjectIndex);
            _objectMovementFlagsArr[actor.AsObject->ObjectIndex] = true;
            ApplyRootTranslation(armature, actor);
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

    private AdvancedBodyScalingSettings ResolveAdvancedBodyScaling(Profile profile, Actor actor)
    {
        var baseline = _configuration.AdvancedBodyScalingSettings;
        if (TryGetActorRace(actor, out var race))
            baseline = baseline.ApplyRaceNeckPreset(race);

        return profile.AdvancedBodyScalingOverrides.Resolve(baseline);
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
            //Only remove armatures which haven't been seen for a while
            //But remove armatures of special actors (like examine screen) right away
            if (!_objectManager.ContainsKey(kvPair.Value.ActorIdentifier) &&
                (armature.LastSeen <= armatureExpirationDateTime || armature.ActorIdentifier.Type == IdentifierType.Special))
            {
                _logger.Debug($"Removing armature {armature} because {kvPair.Key.IncognitoDebug()} is gone");
                RemoveArmature(armature, ArmatureChanged.DeletionReason.Gone);

                continue;
            }

            //armature is considered visible if 1 or less seconds passed since last time we've seen the actor
            armature.IsVisible = armature.LastSeen.AddSeconds(1) >= currentTime;
        }

        var renderableEntries = _objectManager
            .Where(obj => obj.Value.Objects != null
                && obj.Value.Objects.Count > 0
                && obj.Value.Objects.Any(x => x.IsRenderedByGame()))
            .ToList();
        var boneImportanceBudget = new BoneImportanceFrameBudgetState(renderableEntries.Count);

        foreach (var obj in renderableEntries)
        {
            var objects = obj.Value.Objects;
            if (objects == null || objects.Count == 0)
                continue;

            var actorIdentifier = obj.Key.CreatePermanent();

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
                var boneImportance = actorForSettings
                    ? EvaluateBoneImportanceForArmature(armature, actorForSettings, advancedBodyScaling, boneImportanceBudget, forceRefresh: true)
                    : AdvancedBodyScalingBoneImportanceResult.CreateFallback(
                        "No live actor was available during profile rebind.",
                        enabled: advancedBodyScaling.ModelDerivedBoneImportanceEnabled,
                        preferSkinWeights: advancedBodyScaling.PreferTrueSkinWeightImportance,
                        heuristicBlend: advancedBodyScaling.BoneImportanceHeuristicBlend);
                armature.RebuildBoneTemplateBinding(
                    _configuration.RuntimeSafetySettings.SoftScaleLimitsEnabled,
                    _configuration.RuntimeSafetySettings.AutomaticChildScaleCompensationEnabled,
                    advancedBodyScaling,
                    boneImportance);

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
            }

            //Needed because:
            //* Skeleton sometimes appears to be not ready when armature is created
            //* We want to keep armature up to date with any character skeleton changes
            TryLinkSkeleton(armature, boneImportanceBudget);
        }
    }

    private unsafe void ApplyArmatureTransforms(float deltaSeconds)
    {
        var transitionSharpness = _configuration.RuntimeBehaviorSettings.TransformTransitionSharpness;

        foreach (var kvPair in Armatures)
        {
            var armature = kvPair.Value;
            armature.UpdateRuntimeTransforms(deltaSeconds, transitionSharpness);

            if (armature.IsBuilt && armature.IsVisible && _objectManager.TryGetValue(armature.ActorIdentifier, out var actorData))
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

    private AdvancedBodyScalingBoneImportanceResult EvaluateBoneImportanceForArmature(
        Armature armature,
        Actor actor,
        AdvancedBodyScalingSettings settings,
        BoneImportanceFrameBudgetState budget,
        bool forceRefresh = false)
    {
        var tier = ClassifyBoneImportanceTier(armature, actor);
        var fullEligible = IsFullBoneImportanceEligible(settings, tier);
        var activelyManaged = ShouldActivelyManageBoneImportance(settings, tier, budget);
        var runtimeState = armature.BoneImportanceRuntimeState;
        var activeResult = armature.ActiveBoneImportanceResult;
        var hasCachedModelResult = activeResult.ModelDerivedActive;
        var now = Environment.TickCount64;

        if (!settings.ModelDerivedBoneImportanceEnabled)
        {
            runtimeState.LastMode = AdvancedBodyScalingBoneImportanceRuntimeMode.Skipped;
            return ApplyRuntimePolicy(
                AdvancedBodyScalingBoneImportanceResult.CreateFallback(
                    "Model-derived bone importance is disabled for this evaluation.",
                    enabled: false,
                    preferSkinWeights: settings.PreferTrueSkinWeightImportance,
                    heuristicBlend: settings.BoneImportanceHeuristicBlend,
                    modelSignature: activeResult.ModelSignature),
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
                runtimeState.LastProbedModelSignature = activeResult.ModelSignature;

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

        var probeInterval = GetBoneImportanceProbeIntervalMs(tier, runtimeState.StableProbeCount);
        var resolveInterval = GetBoneImportanceResolveIntervalMs(tier, runtimeState.StableProbeCount);
        var priorityRefresh = forceRefresh
            || runtimeState.LastMode == AdvancedBodyScalingBoneImportanceRuntimeMode.Skipped
            || !hasCachedModelResult;
        var probeDue = priorityRefresh
            || runtimeState.LastProbeAtMs == 0
            || now - runtimeState.LastProbeAtMs >= probeInterval
            || string.IsNullOrWhiteSpace(runtimeState.LastProbedModelSignature);

        if (!probeDue)
        {
            if (hasCachedModelResult)
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
                    runtimeSummary: "Cached BIW was reused while the actor stayed within the current stable-check window.");
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

        var probe = _boneImportanceService.ProbeActorModelSignature(actor, settings, runtimeState.LastProbedModelSignature);
        runtimeState.LastProbeAtMs = now;
        if (probe.HasResolvedModelSet)
        {
            runtimeState.StableProbeCount = probe.ModelSignatureChanged
                ? 0
                : Math.Min(runtimeState.StableProbeCount + 1, 24);
            runtimeState.LastProbedModelSignature = probe.ModelSignature;
        }
        else
        {
            runtimeState.StableProbeCount = 0;
            runtimeState.LastProbedModelSignature = probe.ModelSignature;
        }

        var resolveDue = priorityRefresh
            || !hasCachedModelResult
            || probe.ModelSignatureChanged
            || runtimeState.LastResolveAtMs == 0
            || now - runtimeState.LastResolveAtMs >= resolveInterval;

        if (probe.HasResolvedModelSet && !resolveDue && hasCachedModelResult)
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
                runtimeSummary: "Resolved model signature was unchanged, so cached BIW stayed active and the expensive rebuild was deferred.");
        }

        if (probe.HasResolvedModelSet && fullEligible && budget.TryConsumeFull(tier))
        {
            var resolved = _boneImportanceService.ResolveForActor(actor, settings, activeResult.ModelSignature);
            runtimeState.LastResolveAtMs = now;
            runtimeState.LastProbedModelSignature = resolved.ModelSignature;
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
                runtimeSummary: probe.ModelSignatureChanged
                    ? "Full BIW was refreshed because the actor’s resolved model signature changed."
                    : "Full BIW was refreshed on schedule for a high-priority actor.");
        }

        if (probe.HasResolvedModelSet &&
            ShouldUseReducedBoneImportance(tier, fullEligible) &&
            budget.TryConsumeReduced(tier))
        {
            var reduced = _boneImportanceService.ResolveForActor(actor, CreateReducedBoneImportanceSettings(settings), activeResult.ModelSignature);
            runtimeState.LastResolveAtMs = now;
            runtimeState.LastProbedModelSignature = reduced.ModelSignature;
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
                runtimeSummary: "Crowd-safe BIW applied a reduced/coarse refresh because full-quality budget was not available for this actor.");
        }

        if (hasCachedModelResult)
        {
            return ApplyRuntimePolicy(
                activeResult,
                settings,
                runtimeState,
                now,
                AdvancedBodyScalingBoneImportanceRuntimeMode.Cached,
                tier,
                fullEligible,
                crowdSafeDowngraded: true,
                stableThrottled: !probe.ModelSignatureChanged,
                runtimeSummary: probe.HasResolvedModelSet
                    ? "Crowd-safe BIW reused the cached result because the actor was deprioritized under the current frame budget."
                    : "Crowd-safe BIW reused the cached result because the current model probe did not return a usable slot set.");
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
                modelSignatureChanged: probe.ModelSignatureChanged,
                refreshStatus: probe.Summary),
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

    private static AdvancedBodyScalingSettings CreateReducedBoneImportanceSettings(AdvancedBodyScalingSettings settings)
        => new()
        {
            ModelDerivedBoneImportanceEnabled = settings.ModelDerivedBoneImportanceEnabled,
            PreferTrueSkinWeightImportance = false,
            BoneImportanceHeuristicBlend = settings.BoneImportanceHeuristicBlend,
        };

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
            AdvancedBodyScalingBoneImportanceActorTier.TargetOrFocus => settings.FullBoneImportanceOnTargetOrFocus,
            AdvancedBodyScalingBoneImportanceActorTier.NearbyNonProfiled => settings.FullBoneImportanceOnNearbyNonProfiledActors && !budget.HighCrowdPressure,
            _ => false,
        };

    private static bool ShouldUseReducedBoneImportance(
        AdvancedBodyScalingBoneImportanceActorTier tier,
        bool fullEligible)
        => tier switch
        {
            AdvancedBodyScalingBoneImportanceActorTier.Self => !fullEligible,
            AdvancedBodyScalingBoneImportanceActorTier.ProfiledActor => true,
            AdvancedBodyScalingBoneImportanceActorTier.TargetOrFocus => true,
            AdvancedBodyScalingBoneImportanceActorTier.NearbyNonProfiled => true,
            _ => false,
        };

    private static string BuildHardSkipFallbackReason(
        AdvancedBodyScalingBoneImportanceActorTier tier,
        AdvancedBodyScalingSettings settings,
        BoneImportanceFrameBudgetState budget,
        bool hadCachedModelResult)
        => tier switch
        {
            AdvancedBodyScalingBoneImportanceActorTier.TargetOrFocus when !settings.FullBoneImportanceOnTargetOrFocus
                => "Target/focus BIW full-quality processing is disabled, so this actor was returned to heuristic fallback.",
            AdvancedBodyScalingBoneImportanceActorTier.NearbyNonProfiled when !settings.FullBoneImportanceOnNearbyNonProfiledActors
                => "Nearby non-profiled actors are not allowed to receive active BIW, so this actor was returned to heuristic fallback.",
            AdvancedBodyScalingBoneImportanceActorTier.NearbyNonProfiled when budget.HighCrowdPressure
                => "Crowd pressure is high, so nearby non-profiled actors were hard-skipped back to heuristic fallback.",
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
            AdvancedBodyScalingBoneImportanceActorTier.TargetOrFocus when !settings.FullBoneImportanceOnTargetOrFocus
                => "Target/focus BIW is disabled here, so no live model-signature probe was scheduled.",
            AdvancedBodyScalingBoneImportanceActorTier.NearbyNonProfiled when !settings.FullBoneImportanceOnNearbyNonProfiledActors
                => "Nearby non-profiled actors are outside the active BIW set, so no live model-signature probe was scheduled.",
            AdvancedBodyScalingBoneImportanceActorTier.NearbyNonProfiled when budget.HighCrowdPressure
                => "Crowd pressure is high, so nearby non-profiled actors were hard-skipped and left on heuristic fallback until relevance changes.",
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

    private AdvancedBodyScalingBoneImportanceActorTier ClassifyBoneImportanceTier(Armature armature, Actor actor)
    {
        if (AreSameActor(actor, _objectManager.Player))
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
            AdvancedBodyScalingBoneImportanceActorTier.TargetOrFocus => settings.FullBoneImportanceOnTargetOrFocus,
            AdvancedBodyScalingBoneImportanceActorTier.NearbyNonProfiled => settings.FullBoneImportanceOnNearbyNonProfiledActors,
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

    /// <summary>
    /// Returns whether or not a link can be established between the armature and an in-game object.
    /// If unbuilt, the armature will be rebuilded.
    /// </summary>
    private bool TryLinkSkeleton(Armature armature, BoneImportanceFrameBudgetState boneImportanceBudget)
    {
        if (!_objectManager.TryGetValue(armature.ActorIdentifier, out var actorData) ||
            actorData.Objects == null ||
            actorData.Objects.Count == 0)
            return false;

        //we assume that all other objects are a copy of object #0
        var actor = actorData.Objects[0];

        var advancedBodyScaling = ResolveAdvancedBodyScaling(armature.Profile, actor);
        var boneImportance = EvaluateBoneImportanceForArmature(armature, actor, advancedBodyScaling, boneImportanceBudget, forceRefresh: !armature.IsBuilt);
        var skeletonUpdated = armature.IsSkeletonUpdated(actor.Model.AsCharacterBase);
        var previousBindingIdentity = BuildAppliedBoneImportanceBindingIdentity(armature.ActiveAdvancedBodyScalingSettings, armature.ActiveBoneImportanceResult);
        var currentBindingIdentity = BuildAppliedBoneImportanceBindingIdentity(advancedBodyScaling, boneImportance);
        var boneImportanceBindingChanged = !string.Equals(previousBindingIdentity, currentBindingIdentity, StringComparison.Ordinal);
        if (!armature.IsBuilt || skeletonUpdated || boneImportanceBindingChanged)
        {
            if (!armature.IsBuilt || skeletonUpdated)
            {
                _logger.Debug($"Skeleton for actor #{actor.AsObject->ObjectIndex} tied to \"{armature}\" has changed");
                armature.RebuildSkeleton(
                    actor.Model.AsCharacterBase,
                    _configuration.RuntimeSafetySettings.SoftScaleLimitsEnabled,
                    _configuration.RuntimeSafetySettings.AutomaticChildScaleCompensationEnabled,
                    advancedBodyScaling,
                    boneImportance);
            }
            else
            {
                var refreshReason = BuildBoneImportanceBindingRefreshReason(
                    previousBindingIdentity,
                    armature.ActiveBoneImportanceResult,
                    currentBindingIdentity,
                    boneImportance);
                _logger.Debug($"Refreshing bone-importance bindings for actor #{actor.AsObject->ObjectIndex} tied to \"{armature}\" because {refreshReason} ({boneImportance.VisibleRuntimeModeLabel}, {boneImportance.VisibleActorTierLabel}).");
                armature.RebuildBoneTemplateBinding(
                    _configuration.RuntimeSafetySettings.SoftScaleLimitsEnabled,
                    _configuration.RuntimeSafetySettings.AutomaticChildScaleCompensationEnabled,
                    advancedBodyScaling,
                    boneImportance);
            }
        }
        return true;
    }

    /// <summary>
    /// Iterate through the skeleton of the given character base, and apply any transformations
    /// for which this armature contains corresponding model bones. This method of application
    /// is safer but more computationally costly
    /// </summary>
    private void ApplyPiecewiseTransformation(Armature armature, Actor actor, ActorIdentifier actorIdentifier, float deltaSeconds)
    {
        var cBase = actor.Model.AsCharacterBase;

        var isMount = actorIdentifier.Type == IdentifierType.Owned &&
            actorIdentifier.Kind == Dalamud.Game.ClientState.Objects.Enums.ObjectKind.MountType;

        Actor? mountOwner = null;
        Armature? mountOwnerArmature = null;
        if (isMount)
        {
            (var ident, mountOwner) = _gameObjectService.FindActorsByName(actorIdentifier.PlayerName.ToString()).FirstOrDefault();
            Armatures.TryGetValue(ident, out mountOwnerArmature);
        }

        if (cBase != null)
        {
            // Final runtime order:
            // 1. Base/profile/template transforms
            // 2. Advanced body scaling output
            // 3. Runtime safeguards
            // 4. RBF pose-space corrective support
            // 5. Full IK retargeting adaptation layer
            // 6. Motion warping locomotion layer
            // 7. Full-body IK final pose solve
            armature.EvaluatePoseCorrectives(cBase);

            foreach (var mb in armature.ActiveBones)
            {
                if (mb == armature.MainRootBone)
                {
                    var appliedTransform = mb.AppliedTransform;
                    if (_gameObjectService.IsActorHasScalableRoot(actor) && appliedTransform != null && mb.IsModifiedScale())
                    {
                        cBase->DrawObject.Object.Scale = appliedTransform.Scaling;

                        //Fix mount owner's scale if needed
                        //todo: always keep owner's scale proper instead of scaling with mount if no armature found
                        if (isMount && mountOwner != null && mountOwnerArmature != null)
                        {
                            var ownerDrawObject = cBase->DrawObject.Object.ChildObject;

                            //limit to only modified scales because that is just easier to handle
                            //because we don't need to hook into dismount code to reset character scale
                            //todo: hook into dismount
                            //https://github.com/Cytraen/SeatedSidekickSpectator/blob/main/SetModeHook.cs?
                            if (cBase->DrawObject.Object.ChildObject == mountOwner.Value.Model &&
                                mountOwnerArmature.MainRootBone.IsModifiedScale() &&
                                mountOwnerArmature.MainRootBone.AppliedTransform != null)
                            {
                                var baseScale = mountOwnerArmature.MainRootBone.AppliedTransform!.Scaling;

                                ownerDrawObject->Scale = new Vector3(Math.Abs(baseScale.X / cBase->DrawObject.Object.Scale.X),
                                        Math.Abs(baseScale.Y / cBase->DrawObject.Object.Scale.Y),
                                        Math.Abs(baseScale.Z / cBase->DrawObject.Object.Scale.Z));
                            }
                        }
                    }
                }
                else
                {
                    mb.ApplyModelTransform(cBase);
                }
            }

            armature.EvaluateAndApplyFullIkRetargeting(cBase, deltaSeconds);
            armature.EvaluateAndApplyMotionWarping(cBase, deltaSeconds);
            armature.EvaluateAndApplyFullBodyIk(cBase, deltaSeconds);
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
        if (cBase != null)
        {
            if (reset)
            {
                cBase->DrawObject.Object.Position = actor.AsObject->Position;
                return;
            }

            //warn: hotpath for characters with n_root edits. IsApproximately might have some performance hit.
            var rootBoneTransform = arm.GetAppliedBoneTransform("n_root");
            if (rootBoneTransform == null ||
                rootBoneTransform.Translation.IsApproximately(Vector3.Zero, 0.00001f))
                return;

            if (rootBoneTransform.Translation.X == 0 &&
                rootBoneTransform.Translation.Y == 0 &&
                rootBoneTransform.Translation.Z == 0)
                return;

            //Reset position so we don't fly away
            cBase->DrawObject.Object.Position = actor.AsObject->Position;

            var newPosition = new FFXIVClientStructs.FFXIV.Common.Math.Vector3
            {
                X = cBase->DrawObject.Object.Position.X + rootBoneTransform.Translation.X,
                Y = cBase->DrawObject.Object.Position.Y + rootBoneTransform.Translation.Y,
                Z = cBase->DrawObject.Object.Position.Z + rootBoneTransform.Translation.Z
            };

            cBase->DrawObject.Object.Position = newPosition;
        }
    }

    private void RemoveArmature(Armature armature, ArmatureChanged.DeletionReason reason)
    {
        armature.Profile.Armatures.Remove(armature);
        Armatures.Remove(armature.ActorIdentifier);
        _logger.Debug($"Armature {armature} removed from cache");

        _event.Invoke(ArmatureChanged.Type.Deleted, armature, reason);
    }

    private void OnTemplateChange(TemplateChanged.Type type, Templates.Data.Template? template, object? arg3)
    {
        if (type is not TemplateChanged.Type.NewBone &&
            type is not TemplateChanged.Type.UpdatedBone &&
            type is not TemplateChanged.Type.DeletedBone &&
            type is not TemplateChanged.Type.EditorCharacterChanged &&
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
