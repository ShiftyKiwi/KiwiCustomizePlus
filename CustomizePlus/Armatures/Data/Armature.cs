// Copyright (c) Customize+.
// Licensed under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using FFXIVClientStructs.FFXIV.Client.Graphics.Scene;
using Penumbra.GameData.Actors;
using CustomizePlus.Core.Data;
using CustomizePlus.Profiles.Data;
using CustomizePlus.Templates.Data;
using CustomizePlus.GameData.Extensions;

namespace CustomizePlus.Armatures.Data;

/// <summary>
/// Represents a "copy" of the ingame skeleton upon which the linked character profile is meant to operate.
/// Acts as an interface by which the in-game skeleton can be manipulated on a bone-by-bone basis.
/// </summary>
public unsafe class Armature
{
    /// <summary>
    /// Gets the Customize+ profile for which this mockup applies transformations.
    /// </summary>
    public Profile Profile { get; set; }

    /// <summary>
    /// Static identifier of the actor associated with this armature
    /// </summary>
    public ActorIdentifier ActorIdentifier { get; init; }

    /// <summary>
    /// Gets or sets a value indicating whether or not this armature has any renderable objects on which it should act.
    /// </summary>
    public bool IsVisible { get; set; }

    /// <summary>
    /// Represents date and time when actor associated with this armature was last seen.
    /// Implemented mostly as a armature cleanup protection hack for mare and penumbra.
    /// </summary>
    public DateTime LastSeen { get; private set; }

    /// <summary>
    /// Gets a value indicating whether or not this armature has successfully built itself with bone information.
    /// </summary>
    public bool IsBuilt => _partialSkeletons.Any();

    /// <summary>
    /// Internal flag telling ArmatureManager that it should attempt to rebind profile to (another) profile whenever possible.
    /// </summary>
    public bool IsPendingProfileRebind { get; set; }

    /// <summary>
    /// For debugging purposes, each armature is assigned a globally-unique ID number upon creation.
    /// </summary>
    private static uint _nextGlobalId;
    private readonly uint _localId;

    /// <summary>
    /// Binding telling which bones are bound to each template for this armature. Built from template list in profile.
    /// </summary>
    public Dictionary<string, Template> BoneTemplateBinding { get; init; }

    /// <summary>
    /// Resolved target transforms for this armature after weighted profile evaluation.
    /// </summary>
    public Dictionary<string, BoneTransform> ResolvedBoneTransforms { get; init; }

    public AdvancedBodyScalingSettings? ActiveAdvancedBodyScalingSettings { get; private set; }

    internal AdvancedBodyScalingPoseCorrectiveDebugState PoseCorrectiveDebugState { get; } = new();
    internal AdvancedBodyScalingFullIkRetargetingDebugState FullIkRetargetingDebugState { get; } = new();
    internal AdvancedBodyScalingMotionWarpingDebugState MotionWarpingDebugState { get; } = new();
    internal AdvancedBodyScalingFullBodyIkDebugState FullBodyIkDebugState { get; } = new();

    private readonly Dictionary<string, Vector3> _poseCorrectiveScaleMultipliers = new(StringComparer.Ordinal);
    private readonly Dictionary<AdvancedBodyScalingCorrectiveRegion, float> _poseCorrectiveActivationState = new();
    private readonly Dictionary<string, BoneTransform> _fullIkRetargetingCorrections = new(StringComparer.Ordinal);
    private readonly Dictionary<string, BoneTransform> _motionWarpingCorrections = new(StringComparer.Ordinal);
    private readonly Dictionary<string, BoneTransform> _fullBodyIkCorrections = new(StringComparer.Ordinal);
    private readonly AdvancedBodyScalingMotionWarpingContext _motionWarpingContext = new();
    private Vector3 _lastMotionSampleWorldPosition;
    private Vector3 _smoothedMotionDirectionWorld = Vector3.Zero;
    private float _smoothedPlanarSpeed;
    private bool _hasMotionSample;

    private List<ModelBone> _activeBones;
    public IReadOnlyList<ModelBone> ActiveBones => _activeBones;

    /// <summary>
    /// Each skeleton is made up of several smaller "partial" skeletons.
    /// Each partial skeleton has its own list of bones, with a root bone at index zero.
    /// The root bone of a partial skeleton may also be a regular bone in a different partial skeleton.
    /// </summary>
    private ModelBone[][] _partialSkeletons;

    #region Bone Accessors -------------------------------------------------------------------------------

    /// <summary>
    /// Gets the number of partial skeletons contained in this armature.
    /// </summary>
    public int PartialSkeletonCount => _partialSkeletons.Length;

    /// <summary>
    /// Get the list of bones belonging to the partial skeleton at the given index.
    /// </summary>
    public ModelBone[] this[int i]
    {
        get => _partialSkeletons[i];
    }

    /// <summary>
    /// Returns the number of bones contained within the partial skeleton with the given index.
    /// </summary>
    public int GetBoneCountOfPartial(int partialIndex) => _partialSkeletons[partialIndex].Length;

    /// <summary>
    /// Get the bone at index 'j' within the partial skeleton at index 'i'.
    /// </summary>
    public ModelBone this[int i, int j]
    {
        get => _partialSkeletons[i][j];
    }

    /// <summary>
    /// Return the bone at the given indices, if it exists
    /// </summary>
    public ModelBone? GetBoneAt(int partialIndex, int boneIndex)
    {
        if (_partialSkeletons.Length > partialIndex
            && _partialSkeletons[partialIndex].Length > boneIndex)
        {
            return this[partialIndex, boneIndex];
        }

        return null;
    }

    /// <summary>
    /// Returns the root bone of the partial skeleton with the given index.
    /// </summary>
    public ModelBone GetRootBoneOfPartial(int partialIndex) => this[partialIndex, 0];

    public ModelBone MainRootBone => GetRootBoneOfPartial(0);

    /// <summary>
    /// Get the total number of bones in each partial skeleton combined.
    /// </summary>
    // In exactly one partial skeleton will the root bone be an independent bone. In all others, it's a reference to a separate, real bone.
    // For that reason we must subtract the number of duplicate bones
    public int TotalBoneCount => _partialSkeletons.Sum(x => x.Length);

    public IEnumerable<ModelBone> GetAllBones()
    {
        for (var i = 0; i < _partialSkeletons.Length; ++i)
        {
            for (var j = 0; j < _partialSkeletons[i].Length; ++j)
            {
                yield return this[i, j];
            }
        }
    }

    //----------------------------------------------------------------------------------------------------
    #endregion

    public Armature(ActorIdentifier actorIdentifier, Profile profile)
    {
        _localId = _nextGlobalId++;

        _partialSkeletons = Array.Empty<ModelBone[]>();

        BoneTemplateBinding = new Dictionary<string, Template>();
        ResolvedBoneTransforms = new Dictionary<string, BoneTransform>(StringComparer.Ordinal);
        _activeBones = new List<ModelBone>();

        ActorIdentifier = actorIdentifier;
        Profile = profile;
        IsVisible = false;

        UpdateLastSeen();

        Profile.Armatures.Add(this);

        Plugin.Logger.Debug($"Instantiated {this}, attached to {Profile}");
    }

    /// <inheritdoc/>
    public override string ToString()
    {
        return IsBuilt
            ? $"Armature (#{_localId}) on {ActorIdentifier.IncognitoDebug()} ({Profile}) with {TotalBoneCount} bone/s"
            : $"Armature (#{_localId}) on {ActorIdentifier.IncognitoDebug()} ({Profile}) with no skeleton reference";
    }

    public bool IsSkeletonUpdated(CharacterBase* cBase)
    {
        if (cBase == null)
            return false;
        else if (cBase->Skeleton->PartialSkeletonCount != _partialSkeletons.Length)
            return true;
        else
        {
            for (var i = 0; i < cBase->Skeleton->PartialSkeletonCount; ++i)
            {
                if (i == 2)
                    continue; //hair is handled separately

                var newPose = cBase->Skeleton->PartialSkeletons[i].GetHavokPose(Constants.TruePoseIndex);

                if (newPose != null
                    && newPose->Skeleton->Bones.Length != _partialSkeletons[i].Length)
                    return true;
            }

            //handle hair separately because different hairstyles can have the same amount of bones.
            if(cBase->Skeleton->PartialSkeletonCount > 2)
            {
                var newPose = cBase->Skeleton->PartialSkeletons[2].GetHavokPose(Constants.TruePoseIndex);

                if(newPose != null)
                {
                    if(newPose->Skeleton->Bones.Length != _partialSkeletons[2].Length)
                        return true;

                    for(var i = 0; i < newPose->Skeleton->Bones.Length; i++)
                    {
                        if (newPose->Skeleton->Bones[i].Name.String != _partialSkeletons[2][i].BoneName)
                            return true;
                    }
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Rebuild the armature using the provided character base as a reference.
    /// </summary>
    public void RebuildSkeleton(CharacterBase* cBase, bool enableSoftScaleLimits = true, bool enableAutomaticChildCompensation = true, AdvancedBodyScalingSettings? advancedBodyScaling = null)
    {
        if (cBase == null)
            return;

        var newPartials = ParseBonesFromObject(this, cBase);

        _partialSkeletons = newPartials.Select(x => x.ToArray()).ToArray();

        RebuildBoneTemplateBinding(enableSoftScaleLimits, enableAutomaticChildCompensation, advancedBodyScaling); //todo: intentionally not calling ArmatureChanged.Type.Updated because this is pending rewrite

        Plugin.Logger.Debug($"Rebuilt {this}");
    }

    public BoneTransform? GetAppliedBoneTransform(string boneName)
    {
        var liveBone = GetAllBones().FirstOrDefault(b => b.BoneName == boneName && b.AppliedTransform != null);
        if (liveBone?.AppliedTransform != null)
            return liveBone.AppliedTransform;

        if (ResolvedBoneTransforms.TryGetValue(boneName, out var boneTransform))
            return boneTransform;

        return null;
    }

    /// <summary>
    /// Update last time actor for this armature was last seen in the game
    /// </summary>
    public void UpdateLastSeen(DateTime? dateTime = null)
    {
        if(dateTime == null)
            dateTime = DateTime.UtcNow;

        LastSeen = (DateTime)dateTime;
    }

    private static unsafe List<List<ModelBone>> ParseBonesFromObject(Armature arm, CharacterBase* cBase)
    {
        List<List<ModelBone>> newPartials = new();

        try
        {
            //build the skeleton
            for (var pSkeleIndex = 0; pSkeleIndex < cBase->Skeleton->PartialSkeletonCount; ++pSkeleIndex)
            {
                var currentPartial = cBase->Skeleton->PartialSkeletons[pSkeleIndex];
                var currentPose = currentPartial.GetHavokPose(Constants.TruePoseIndex);

                newPartials.Add(new());

                if (currentPose == null)
                    continue;

                for (var boneIndex = 0; boneIndex < currentPose->Skeleton->Bones.Length; ++boneIndex)
                {
                    if (currentPose->Skeleton->Bones[boneIndex].Name.String is string boneName &&
                        boneName != null)
                    {
                        //time to build a new bone
                        ModelBone newBone = new(arm, boneName, pSkeleIndex, boneIndex);
                        Plugin.Logger.Verbose($"Created new bone: {boneName} on {pSkeleIndex}->{boneIndex} arm: {arm._localId}");

                        if (currentPose->Skeleton->ParentIndices[boneIndex] is short parentIndex
                            && parentIndex >= 0)
                        {
                            newBone.AddParent(pSkeleIndex, parentIndex);
                            newPartials[pSkeleIndex][parentIndex].AddChild(pSkeleIndex, boneIndex);
                        }

                        foreach (var mb in newPartials.SelectMany(x => x))
                        {
                            if (AreTwinnedNames(boneName, mb.BoneName))
                            {
                                newBone.AddTwin(mb.PartialSkeletonIndex, mb.BoneIndex);
                                mb.AddTwin(pSkeleIndex, boneIndex);
                                break;
                            }
                        }

                        //linking is performed later

                        newPartials.Last().Add(newBone);
                    }
                    else
                    {
                        Plugin.Logger.Error($"Failed to process bone @ <{pSkeleIndex}, {boneIndex}> while parsing bones from {cBase->ToString()}");
                    }
                }
            }

            BoneData.LogNewBones(newPartials.SelectMany(x => x.Select(y => y.BoneName)).ToArray());
        }
        catch (Exception ex)
        {
            Plugin.Logger.Error($"Error parsing armature skeleton from {cBase->ToString()}:\n\t{ex}");
        }

        return newPartials;
    }

    public void RebuildBoneTemplateBinding(bool enableSoftScaleLimits = true, bool enableAutomaticChildCompensation = true, AdvancedBodyScalingSettings? advancedBodyScaling = null)
    {
        ActiveAdvancedBodyScalingSettings = advancedBodyScaling?.DeepCopy();
        if (ActiveAdvancedBodyScalingSettings == null)
        {
            ClearPoseCorrectives();
            ClearFullIkRetargeting();
            ClearMotionWarping();
        }

        ClearFullIkRetargeting();
        ClearMotionWarping();
        ClearFullBodyIk();

        var resolution = ProfileTransformResolver.Resolve(Profile);
        var effectiveTransforms = resolution.EffectiveTransforms;

        if (advancedBodyScaling != null && advancedBodyScaling.Enabled && advancedBodyScaling.Mode != AdvancedBodyScalingMode.Manual)
            effectiveTransforms = AdvancedBodyScalingPipeline.Apply(effectiveTransforms, advancedBodyScaling);

        BoneTemplateBinding.Clear();
        ResolvedBoneTransforms.Clear();
        _activeBones.Clear();

        foreach (var kvPair in resolution.BoneOwners)
            BoneTemplateBinding[kvPair.Key] = kvPair.Value;

        foreach (var kvPair in effectiveTransforms)
        {
            var adjusted = BoneRuntimeSafeguards.Apply(
                kvPair.Key,
                kvPair.Value,
                enableSoftScaleLimits,
                enableAutomaticChildCompensation);

            if (adjusted.IsEdited())
                ResolvedBoneTransforms[kvPair.Key] = adjusted;
        }

        foreach (var bone in GetAllBones())
        {
            BoneTemplateBinding.TryGetValue(bone.BoneName, out var template);
            ResolvedBoneTransforms.TryGetValue(bone.BoneName, out var transform);
            bone.LinkToTemplate(template, transform);

            if (bone.IsActive)
                _activeBones.Add(bone);
        }

        _activeBones = _activeBones
            .OrderBy(b => b.PartialSkeletonIndex)
            .ThenBy(b => b.BoneIndex)
            .ToList();

        Plugin.Logger.Debug($"Rebuilt template binding for armature {_localId}");
    }

    public unsafe void EvaluatePoseCorrectives(CharacterBase* cBase)
    {
        if (cBase == null || ActiveAdvancedBodyScalingSettings == null)
        {
            ClearPoseCorrectives();
            return;
        }

        AdvancedBodyScalingPoseCorrectiveSystem.Evaluate(this, cBase, ActiveAdvancedBodyScalingSettings, Profile.AdvancedBodyScalingOverrides.UseProfileOverrides, _poseCorrectiveActivationState, _poseCorrectiveScaleMultipliers, PoseCorrectiveDebugState);
    }

    public bool TryGetPoseCorrectiveScale(string boneName, out Vector3 correctiveScale)
    {
        if (_poseCorrectiveScaleMultipliers.TryGetValue(boneName, out correctiveScale))
            return true;

        correctiveScale = Vector3.One;
        return false;
    }

    public void ClearPoseCorrectives()
    {
        _poseCorrectiveScaleMultipliers.Clear();
        _poseCorrectiveActivationState.Clear();
        var path = AdvancedBodyScalingPoseCorrectiveSystem.DetectSupportedPath();
        PoseCorrectiveDebugState.Reset(path, AdvancedBodyScalingPoseCorrectiveSystem.GetPathDescription(path), Profile.AdvancedBodyScalingOverrides.UseProfileOverrides);
    }

    public unsafe void EvaluateAndApplyFullBodyIk(CharacterBase* cBase, float deltaSeconds)
    {
        if (cBase == null || ActiveAdvancedBodyScalingSettings == null)
        {
            ClearFullBodyIk();
            return;
        }

        AdvancedBodyScalingFullBodyIkSystem.EvaluateAndApply(
            this,
            cBase,
            ActiveAdvancedBodyScalingSettings,
            Profile.AdvancedBodyScalingOverrides.UseProfileOverrides,
            deltaSeconds,
            _fullBodyIkCorrections,
            FullBodyIkDebugState);

        MotionWarpingDebugState.SetFullBodyIkFollowup(FullBodyIkDebugState.Active, FullBodyIkDebugState.Summary);
        FullIkRetargetingDebugState.SetFullBodyIkFollowup(FullBodyIkDebugState.Active, FullBodyIkDebugState.Summary);
    }

    public unsafe void EvaluateAndApplyFullIkRetargeting(CharacterBase* cBase, float deltaSeconds)
    {
        if (cBase == null || ActiveAdvancedBodyScalingSettings == null)
        {
            ClearFullIkRetargeting();
            return;
        }

        AdvancedBodyScalingFullIkRetargetingSystem.EvaluateAndApply(
            this,
            cBase,
            ActiveAdvancedBodyScalingSettings,
            Profile.AdvancedBodyScalingOverrides.UseProfileOverrides,
            deltaSeconds,
            _fullIkRetargetingCorrections,
            FullIkRetargetingDebugState);
    }

    public unsafe void EvaluateAndApplyMotionWarping(CharacterBase* cBase, float deltaSeconds)
    {
        if (cBase == null || ActiveAdvancedBodyScalingSettings == null)
        {
            ClearMotionWarping();
            return;
        }

        AdvancedBodyScalingMotionWarpingSystem.EvaluateAndApply(
            this,
            cBase,
            ActiveAdvancedBodyScalingSettings,
            Profile.AdvancedBodyScalingOverrides.UseProfileOverrides,
            deltaSeconds,
            _motionWarpingContext,
            _motionWarpingCorrections,
            MotionWarpingDebugState);
    }

    public void ClearFullBodyIk()
    {
        _fullBodyIkCorrections.Clear();
        FullBodyIkDebugState.Reset(false, Profile.AdvancedBodyScalingOverrides.UseProfileOverrides, 0, 0f);
        FullBodyIkDebugState.FinalizeState(false, false, false, false, 0f, 0f, 0f, "Full-body IK is inactive.");
        MotionWarpingDebugState.SetFullBodyIkFollowup(false, "Full-body IK is inactive.");
        FullIkRetargetingDebugState.SetFullBodyIkFollowup(false, "Full-body IK is inactive.");
    }

    public void ClearFullIkRetargeting()
    {
        _fullIkRetargetingCorrections.Clear();
        FullIkRetargetingDebugState.Reset(false, Profile.AdvancedBodyScalingOverrides.UseProfileOverrides, 0f, 0f);
        FullIkRetargetingDebugState.FinalizeState(false, false, false, 0f, 0f, "Full IK retargeting is inactive.");
        FullIkRetargetingDebugState.SetFullBodyIkFollowup(false, "Full-body IK follow-up has not run.");
    }

    public void ClearMotionWarping()
    {
        _motionWarpingCorrections.Clear();
        MotionWarpingDebugState.Reset(false, Profile.AdvancedBodyScalingOverrides.UseProfileOverrides, 0f, 0f, _motionWarpingContext);
        MotionWarpingDebugState.FinalizeState(false, false, false, 0f, 0f, "Motion warping is inactive.");
        MotionWarpingDebugState.SetFullBodyIkFollowup(false, "Full-body IK follow-up has not run.");
    }

    public void ResetMotionWarpingContext(string summary = "Waiting for locomotion context.")
    {
        _motionWarpingContext.Reset(summary);
        _smoothedMotionDirectionWorld = Vector3.Zero;
        _smoothedPlanarSpeed = 0f;
        _hasMotionSample = false;
    }

    public void UpdateMotionWarpingContext(Vector3 worldPosition, float facingRadians, float deltaSeconds)
    {
        if (deltaSeconds <= 0f)
        {
            _motionWarpingContext.Reset("Waiting for locomotion context.");
            return;
        }

        if (!_hasMotionSample)
        {
            _lastMotionSampleWorldPosition = worldPosition;
            _hasMotionSample = true;
            _motionWarpingContext.Reset("Waiting for locomotion context.");
            _motionWarpingContext.HasObservation = true;
            _motionWarpingContext.FacingRadians = facingRadians;
            return;
        }

        var delta = worldPosition - _lastMotionSampleWorldPosition;
        _lastMotionSampleWorldPosition = worldPosition;
        var planarDelta = new Vector3(delta.X, 0f, delta.Z);
        var rawSpeed = planarDelta.Length() / MathF.Max(deltaSeconds, 0.0001f);
        var smoothing = Math.Clamp(deltaSeconds * 10f, 0f, 1f);
        _smoothedPlanarSpeed += (rawSpeed - _smoothedPlanarSpeed) * smoothing;

        if (planarDelta.LengthSquared() > 0.000001f)
        {
            var rawDirection = Vector3.Normalize(planarDelta);
            _smoothedMotionDirectionWorld = _smoothedMotionDirectionWorld.LengthSquared() <= 0.0001f
                ? rawDirection
                : Vector3.Normalize(Vector3.Lerp(_smoothedMotionDirectionWorld, rawDirection, smoothing));
        }

        var localDirection = _smoothedMotionDirectionWorld.LengthSquared() <= 0.0001f
            ? Vector3.Zero
            : Vector3.Transform(_smoothedMotionDirectionWorld, Quaternion.CreateFromAxisAngle(Vector3.UnitY, -facingRadians));
        localDirection = new Vector3(localDirection.X, 0f, localDirection.Z);
        if (localDirection.LengthSquared() > 0.0001f)
            localDirection = Vector3.Normalize(localDirection);

        var locomotionAmount = Remap(_smoothedPlanarSpeed, 0.10f, 1.65f);
        var turnAmount = localDirection.LengthSquared() <= 0.0001f
            ? 0f
            : Math.Clamp(MathF.Abs(localDirection.X) + (MathF.Max(0f, -localDirection.Z) * 0.35f), 0f, 1f) * locomotionAmount;

        _motionWarpingContext.HasObservation = true;
        _motionWarpingContext.HasLocomotion = locomotionAmount > 0.02f;
        _motionWarpingContext.PlanarSpeed = _smoothedPlanarSpeed;
        _motionWarpingContext.LocomotionAmount = locomotionAmount;
        _motionWarpingContext.TurnAmount = turnAmount;
        _motionWarpingContext.FacingRadians = facingRadians;
        _motionWarpingContext.WorldDirection = _smoothedMotionDirectionWorld;
        _motionWarpingContext.LocalDirection = localDirection;
        _motionWarpingContext.Summary = _motionWarpingContext.HasLocomotion
            ? $"Observed locomotion at {_smoothedPlanarSpeed:0.00} units/s with locomotion pressure {locomotionAmount:0.00}."
            : "Movement is below the locomotion activation threshold, so motion warping stays conservative.";
    }

    public void UpdateRuntimeTransforms(float deltaSeconds, float transitionSharpness)
    {
        for (var i = _activeBones.Count - 1; i >= 0; --i)
        {
            if (!_activeBones[i].UpdateRuntimeTransform(deltaSeconds, transitionSharpness))
                _activeBones.RemoveAt(i);
        }
    }

    private static bool AreTwinnedNames(string name1, string name2)
    {
        return name1[^1] == 'r' ^ name2[^1] == 'r'
            && name1[^1] == 'l' ^ name2[^1] == 'l'
            && name1[0..^1] == name2[0..^1];
    }

    private static float Remap(float value, float start, float full)
    {
        if (full <= start)
            return value >= full ? 1f : 0f;

        return Math.Clamp((value - start) / (full - start), 0f, 1f);
    }
}
