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

    /// <summary>
    /// This is a movement flag for every object. Used to prevent calls to ApplyRootTranslation from both movement and render hooks.
    /// Sized dynamically because object table indices are not a stable contract.
    /// </summary>
    private bool[] _objectMovementFlagsArr = new bool[1024];
    private DateTime _lastRenderAtUtc;

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

        foreach (var obj in _objectManager)
        {
            var actorIdentifier = obj.Key.CreatePermanent();

            //warn: in cutscenes the game creates a copy of your character and object #0,
            //so we need to check if there is at least one object being rendered
            if (obj.Value.Objects == null || obj.Value.Objects.Count == 0 || !obj.Value.Objects.Any(x => x.IsRenderedByGame()))
                continue;

            if (!Armatures.ContainsKey(actorIdentifier))
            {
                var activeProfile = _profileManager.GetEnabledProfilesByActor(actorIdentifier).FirstOrDefault();
                if (activeProfile == null)
                    continue;

                var newArm = new Armature(actorIdentifier, activeProfile);
                TryLinkSkeleton(newArm);
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
                    armature.Profile = activeProfile;
                    activeProfile.Armatures.Add(armature);
                }

                var actorForSettings = obj.Value.Objects.Count > 0 ? obj.Value.Objects[0] : Actor.Null;
                var advancedBodyScaling = ResolveAdvancedBodyScaling(armature.Profile, actorForSettings);
                var boneImportance = actorForSettings
                    ? _boneImportanceService.ResolveForActor(actorForSettings, advancedBodyScaling, armature.ActiveBoneImportanceResult.ModelSignature)
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
            TryLinkSkeleton(armature);
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

    /// <summary>
    /// Returns whether or not a link can be established between the armature and an in-game object.
    /// If unbuilt, the armature will be rebuilded.
    /// </summary>
    private bool TryLinkSkeleton(Armature armature)
    {
        if (!_objectManager.TryGetValue(armature.ActorIdentifier, out var actorData) ||
            actorData.Objects == null ||
            actorData.Objects.Count == 0)
            return false;

        //we assume that all other objects are a copy of object #0
        var actor = actorData.Objects[0];

        var advancedBodyScaling = ResolveAdvancedBodyScaling(armature.Profile, actor);
        var boneImportance = _boneImportanceService.ResolveForActor(actor, advancedBodyScaling, armature.ActiveBoneImportanceResult.ModelSignature);
        var skeletonUpdated = armature.IsSkeletonUpdated(actor.Model.AsCharacterBase);
        var boneImportanceSignatureChanged = boneImportance.ModelSignatureChanged;
        if (!armature.IsBuilt || skeletonUpdated || boneImportanceSignatureChanged)
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
                _logger.Debug($"Resolved bone-importance model signature changed for actor #{actor.AsObject->ObjectIndex} tied to \"{armature}\", refreshing bindings.");
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
