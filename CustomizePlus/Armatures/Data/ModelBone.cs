// Copyright (c) Customize+.
// Licensed under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using CustomizePlus.Core.Data;
using CustomizePlus.Core.Extensions;
using CustomizePlus.Templates.Data;
using FFXIVClientStructs.FFXIV.Client.Graphics.Scene;
using FFXIVClientStructs.Havok.Animation.Rig;
using FFXIVClientStructs.Havok.Common.Base.Math.QsTransform;
using static FFXIVClientStructs.Havok.Animation.Rig.hkaPose;

namespace CustomizePlus.Armatures.Data;

/// <summary>
///     Represents a single bone of an ingame character's skeleton.
/// </summary>
public unsafe class ModelBone
{
    public enum PoseType
    {
        // BindPose and World are descriptive enum values only. Production access and writes are
        // deliberately limited to verified Local and Model Havok pose frames.
        Local, Model, BindPose, World
    }

    public readonly Armature MasterArmature;

    public readonly int PartialSkeletonIndex;
    public readonly int BoneIndex;
    internal int ParentBoneIndex => _parentBoneIndex;

    /// <summary>
    /// Gets the model bone corresponding to this model bone's parent, if it exists.
    /// (It should in all cases but the root of the skeleton)
    /// </summary>
    public ModelBone? ParentBone => _parentPartialIndex >= 0 && _parentBoneIndex >= 0
        ? MasterArmature[_parentPartialIndex, _parentBoneIndex]
        : null;
    private int _parentPartialIndex = -1;
    private int _parentBoneIndex = -1;

    /// <summary>
    /// Gets each model bone for which this model bone corresponds to a direct parent thereof.
    /// A model bone may have zero children.
    /// </summary>
    public IEnumerable<ModelBone> ChildBones => _childPartialIndices.Zip(_childBoneIndices, (x, y) => MasterArmature[x, y]);
    public IEnumerable<ModelBone> GetDescendants()
    {
        var list = ChildBones.ToList();
        for (var i = 0; i < list.Count; i++)
        {
            list.AddRange(list[i].ChildBones.ToList());
        }
        return list;
    }
    private List<int> _childPartialIndices = new();
    private List<int> _childBoneIndices = new();

    /// <summary>
    /// Gets the model bone that forms a mirror image of this model bone, if one exists.
    /// </summary>
    public ModelBone? TwinBone => _twinPartialIndex >= 0 && _twinBoneIndex >= 0
        ? MasterArmature[_twinPartialIndex, _twinBoneIndex]
        : null;
    private int _twinPartialIndex = -1;
    private int _twinBoneIndex = -1;

    /// <summary>
    /// The name of the bone within the in-game skeleton. Referred to in some places as its "code name".
    /// </summary>
    public string BoneName;

    /// <summary>
    /// The resolved target transform for this model bone after profile/template resolution.
    /// </summary>
    public BoneTransform? CustomizedTransform { get; private set; }

    /// <summary>
    /// The smoothed transform currently applied to the live skeleton.
    /// </summary>
    public BoneTransform? AppliedTransform => _appliedTransform;
    private BoneTransform? _appliedTransform;

    /// <summary>
    /// True if bone is linked to any template or is still transitioning back to identity.
    /// </summary>
    public bool IsActive => CustomizedTransform != null || (_appliedTransform != null && _appliedTransform.IsEdited(true));

    public ModelBone(Armature arm, string codeName, int partialIdx, int boneIdx)
    {
        MasterArmature = arm;
        PartialSkeletonIndex = partialIdx;
        BoneIndex = boneIdx;

        BoneName = codeName;
    }

    /// <summary>
    /// Link bone to a template owner and update its resolved transform target.
    /// </summary>
    public bool LinkToTemplate(Template? template, BoneTransform? resolvedTransform = null)
    {
        if (template == null)
        {
            if (resolvedTransform == null)
            {
                var hadState = CustomizedTransform != null || _appliedTransform != null;
                CustomizedTransform = null;

                if (hadState)
                    Plugin.Logger.Verbose($"Unlinked {BoneName} from all templates");

                return hadState;
            }

            CustomizedTransform ??= new BoneTransform();
            CustomizedTransform.UpdateToMatch(resolvedTransform);
            _appliedTransform ??= new BoneTransform();

            return true;
        }

        if (!template.Bones.ContainsKey(BoneName))
            return false;

        if (resolvedTransform == null)
        {
            CustomizedTransform = null;
            return _appliedTransform != null;
        }

        CustomizedTransform ??= new BoneTransform();
        CustomizedTransform.UpdateToMatch(resolvedTransform);
        _appliedTransform ??= new BoneTransform();

        return true;
    }

    public bool UpdateRuntimeTransform(float deltaSeconds, float transitionSharpness)
    {
        if (CustomizedTransform == null)
        {
            if (_appliedTransform == null)
                return false;

            if (_appliedTransform.SmoothTowards(new BoneTransform(), deltaSeconds, transitionSharpness) && !_appliedTransform.IsEdited(true))
            {
                _appliedTransform = null;
                return false;
            }

            return true;
        }

        _appliedTransform ??= new BoneTransform();
        _appliedTransform.SmoothTowards(CustomizedTransform, deltaSeconds, transitionSharpness);
        return true;
    }

    /// <summary>
    /// Indicate a bone to act as this model bone's "parent".
    /// </summary>
    public void AddParent(int parentPartialIdx, int parentBoneIdx)
    {
        if (_parentPartialIndex != -1 || _parentBoneIndex != -1)
        {
            throw new Exception($"Tried to add redundant parent to model bone -- {this}");
        }

        _parentPartialIndex = parentPartialIdx;
        _parentBoneIndex = parentBoneIdx;
    }

    /// <summary>
    /// Indicate that a bone is one of this model bone's "children".
    /// </summary>
    public void AddChild(int childPartialIdx, int childBoneIdx)
    {
        _childPartialIndices.Add(childPartialIdx);
        _childBoneIndices.Add(childBoneIdx);
    }

    /// <summary>
    /// Indicate a bone that acts as this model bone's mirror image, or "twin".
    /// </summary>
    public void AddTwin(int twinPartialIdx, int twinBoneIdx)
    {
        _twinPartialIndex = twinPartialIdx;
        _twinBoneIndex = twinBoneIdx;
    }

    public override string ToString()
    {
        //string numCopies = _copyIndices.Count > 0 ? $" ({_copyIndices.Count} copies)" : string.Empty;
        return $"{BoneName} ({BoneData.GetBoneDisplayName(BoneName)}) @ <{PartialSkeletonIndex}, {BoneIndex}>";
    }

    /// <summary>
    /// Get the lineage of this model bone, going back to the skeleton's root bone.
    /// </summary>
    public IEnumerable<ModelBone> GetAncestors(bool includeSelf = true)
    {
        var ancestors = new List<ModelBone>();
        if (includeSelf)
            ancestors.Add(this);

        var parent = ParentBone;
        while (parent != null)
        {
            ancestors.Add(parent);
            parent = parent.ParentBone;
        }

        return ancestors;
    }

    /// <summary>
    /// Gets all model bones with a lineage that contains this one.
    /// </summary>
    public IEnumerable<ModelBone> GetDescendants(bool includeSelf = false) => includeSelf
        ? GetDescendants(this)
        : GetDescendants(null);

    private IEnumerable<ModelBone> GetDescendants(ModelBone? first)
    {
        var output = first != null
            ? new List<ModelBone>() { first }
            : new List<ModelBone>();

        output.AddRange(ChildBones);

        using (var iter = output.GetEnumerator())
        {
            while (iter.MoveNext())
            {
                output.AddRange(iter.Current.ChildBones);
                yield return iter.Current;
            }
        }
    }

    public IEnumerable<(ModelBone Bone, int Depth)> GetDescendantsWithDepth(bool includeSelf = false)
    {
        Queue<(ModelBone Bone, int Depth)> queue = new();

        if (includeSelf)
            queue.Enqueue((this, 0));
        else
        {
            foreach (var child in ChildBones)
                queue.Enqueue((child, 1));
        }

        while (queue.Count > 0)
        {
            var next = queue.Dequeue();
            yield return next;

            foreach (var child in next.Bone.ChildBones)
                queue.Enqueue((child, next.Depth + 1));
        }
    }

    /// <summary>
    /// Given a character base to which this model bone's master armature (presumably) applies,
    /// return the game's transform value for this model's in-game sibling within the given reference frame.
    /// </summary>
    public hkQsTransformf GetGameTransform(CharacterBase* cBase, PoseType refFrame)
    {
        if (!TryGetPose(cBase, PartialSkeletonIndex, BoneIndex, BoneName, ParentBoneIndex, out var targetPose))
            return Constants.NullTransform;

        var transform = refFrame switch
        {
            PoseType.Local => targetPose->LocalPose[BoneIndex],
            PoseType.Model => targetPose->ModelPose[BoneIndex],
            // BindPose/World have no verified safe runtime read path in the supported API.
            _ => Constants.NullTransform
        };

        return TransformSafety.TrySanitizeNativeTransform(ref transform)
            ? transform
            : Constants.NullTransform;
    }

    public hkQsTransformf* GetGameTransformAccess(CharacterBase* cBase, PoseType refFrame)
    {
        if (!TryGetPose(cBase, PartialSkeletonIndex, BoneIndex, BoneName, ParentBoneIndex, out var targetPose))
            return null;

        // Access callers can write through the returned pointer during child propagation.
        // Keep that raw matrix path subject to the same pose-sync boundary as SetGameTransform.
        if (targetPose->ModelInSync == 0)
            return null;

        var access = refFrame switch
        {
            PoseType.Local => targetPose->AccessBoneLocalSpace(BoneIndex),
            PoseType.Model => targetPose->AccessBoneModelSpace(BoneIndex, PropagateOrNot.DontPropagate),
            // BindPose/World have no verified safe runtime access path in the supported API.
            _ => null
        };

        if (access == null)
            return null;

        var transform = *access;
        return TransformSafety.TrySanitizeNativeTransform(ref transform)
            ? access
            : null;
    }

    private NativeTransformWriteOutcome SetGameTransform(CharacterBase* cBase, hkQsTransformf transform, PoseType refFrame)
    {
        return SetGameTransform(cBase, transform, PartialSkeletonIndex, BoneIndex, BoneName, ParentBoneIndex, refFrame);
    }

    private NativeTransformWriteOutcome SetGameTransform(
        CharacterBase* cBase,
        hkQsTransformf transform,
        int partialIndex,
        int boneIndex,
        string expectedBoneName,
        int expectedParentIndex,
        PoseType refFrame)
    {
        if (!TransformSafety.TrySanitizeNativeTransform(ref transform))
            return NativeTransformWriteOutcome.SkippedUnsafeTransform;

        if (!TryGetPose(cBase, partialIndex, boneIndex, expectedBoneName, expectedParentIndex, out var targetPose, out var poseFailure))
            return poseFailure;

        if (targetPose->ModelInSync == 0)
            return NativeTransformWriteOutcome.SkippedPoseNotInSync;

        switch (refFrame)
        {
            case PoseType.Local:
                targetPose->LocalPose.Data[boneIndex] = transform;
                return NativeTransformWriteOutcome.Accepted;

            case PoseType.Model:
                targetPose->ModelPose.Data[boneIndex] = transform;
                return NativeTransformWriteOutcome.Accepted;

            default:
                return NativeTransformWriteOutcome.SkippedUnsupportedFrame;
        }
    }

    private bool TryGetPose(
        CharacterBase* cBase,
        int partialIndex,
        int boneIndex,
        string expectedBoneName,
        int expectedParentIndex,
        out hkaPose* targetPose)
        => TryGetPose(cBase, partialIndex, boneIndex, expectedBoneName, expectedParentIndex, out targetPose, out _);

    private bool TryGetPose(
        CharacterBase* cBase,
        int partialIndex,
        int boneIndex,
        string expectedBoneName,
        int expectedParentIndex,
        out hkaPose* targetPose,
        out NativeTransformWriteOutcome failureOutcome)
    {
        targetPose = null;
        failureOutcome = NativeTransformWriteOutcome.SkippedMissingBone;
        if (cBase == null || cBase->Skeleton == null)
            return false;

        var skelly = cBase->Skeleton;
        if (partialIndex < 0 || partialIndex >= skelly->PartialSkeletonCount)
            return false;

        var pSkelly = skelly->PartialSkeletons[partialIndex];
        targetPose = pSkelly.GetHavokPose(Constants.TruePoseIndex);
        if (targetPose == null || targetPose->Skeleton == null)
            return false;

        // Bounds remain mandatory even for an already-validated primary binding.
        if (boneIndex < 0
            || boneIndex >= targetPose->Skeleton->Bones.Length
            || boneIndex >= targetPose->LocalPose.Length
            || boneIndex >= targetPose->ModelPose.Length
            || boneIndex >= targetPose->Skeleton->ParentIndices.Length)
            return false;

        // Armature.IsSkeletonUpdated validates the complete primary topology before every
        // apply pass. For that exact CharacterBase/pose identity, avoid rematerializing the
        // native bone name for every read and write. Other draw objects retain the full check.
        if (!MasterArmature.HasValidatedNativeWritePose(cBase, partialIndex, targetPose)
            && (!string.Equals(targetPose->Skeleton->Bones[boneIndex].Name.String, expectedBoneName, StringComparison.Ordinal)
                || targetPose->Skeleton->ParentIndices[boneIndex] != expectedParentIndex))
        {
            failureOutcome = NativeTransformWriteOutcome.SkippedStaleBinding;
            return false;
        }

        return true;
    }

    /// <summary>
    /// Apply this model bone's associated transformation to its in-game sibling within
    /// the skeleton of the given character base.
    /// </summary>
    public void ApplyModelTransform(CharacterBase* cBase)
    {
        var appliedTransform = AppliedTransform;
        if (!IsActive || appliedTransform == null)
            return;

        if (cBase == null || !appliedTransform.IsEdited())
            return;

        var effectiveTransform = appliedTransform;
        if (appliedTransform.LockState == BoneLockState.Unlocked &&
            MasterArmature.TryGetPoseCorrectiveScale(BoneName, out var correctiveScale) &&
            !correctiveScale.IsApproximately(Vector3.One, 0.0005f))
        {
            effectiveTransform = new BoneTransform(appliedTransform)
            {
                Scaling = appliedTransform.ApplyScalePins(appliedTransform.Scaling * correctiveScale),
            };
        }

        ApplyEffectiveTransform(cBase, effectiveTransform, recordPrimaryWrite: true);
    }

    public void ApplyRuntimeCorrection(CharacterBase* cBase, BoneTransform correction)
    {
        if (cBase == null || correction == null || !correction.IsEdited(true))
            return;

        ApplyEffectiveTransform(cBase, correction, recordPrimaryWrite: false);
    }

#if DEBUG
    /// <summary>
    /// Applies the temporary diagnostic input through the normal guarded model-bone path.
    /// This is deliberately unavailable from release builds and never changes template state.
    /// </summary>
    internal void ApplyDebugPoseCorrectiveValidationTransform(CharacterBase* cBase)
    {
        if (cBase == null || !MasterArmature.TryGetDebugPoseCorrectiveValidationTransform(BoneName, out var diagnosticTransform))
            return;

        var before = GetGameTransform(cBase, PoseType.Model);
        var correctiveScale = MasterArmature.TryGetPoseCorrectiveScale(BoneName, out var scale) ? scale : Vector3.One;
        var effective = new BoneTransform(diagnosticTransform)
        {
            Scaling = diagnosticTransform.ApplyScalePins(diagnosticTransform.Scaling * correctiveScale),
        };
        ApplyEffectiveTransform(cBase, effective, recordPrimaryWrite: true);
        var after = GetGameTransform(cBase, PoseType.Model);
        if (!before.Equals(Constants.NullTransform) && !after.Equals(Constants.NullTransform))
        {
            MasterArmature.RecordDebugPoseCorrectiveValidationApplication(
                BoneName,
                before.Translation.ToVector3(),
                before.Rotation.ToQuaternion(),
                before.Scale.ToVector3(),
                after.Translation.ToVector3(),
                after.Rotation.ToQuaternion(),
                after.Scale.ToVector3(),
                correctiveScale);
        }
    }
#endif


    private void ApplyEffectiveTransform(CharacterBase* cBase, BoneTransform effectiveTransform, bool recordPrimaryWrite)
    {
        if (cBase == null || effectiveTransform == null || !effectiveTransform.IsEdited(true))
            return;

        if (recordPrimaryWrite)
            MasterArmature.RecordDebugNativeWriteAttempt();

        void RecordOutcome(NativeTransformWriteOutcome outcome)
        {
            if (recordPrimaryWrite)
                MasterArmature.RecordDebugNativeWriteOutcome(outcome);
        }

        var doPropagate = effectiveTransform.PropagateTranslation ||
                          effectiveTransform.PropagateRotation ||
                          effectiveTransform.PropagateScale;

        if (!doPropagate)
        {
            var gameTransform = GetGameTransform(cBase, PoseType.Model);
            if (gameTransform.Equals(Constants.NullTransform))
            {
                RecordOutcome(NativeTransformWriteOutcome.SkippedMissingBone);
                return;
            }

            var modify_Transform = effectiveTransform.ModifyExistingTransform(gameTransform);
            if (modify_Transform.Equals(Constants.NullTransform))
            {
                RecordOutcome(NativeTransformWriteOutcome.SkippedUnsafeTransform);
                return;
            }

            RecordOutcome(SetGameTransform(cBase, modify_Transform, PoseType.Model));

            return;
        }

        var gameTransformAccess = GetGameTransformAccess(cBase, PoseType.Model);
        if (gameTransformAccess == null)
        {
            RecordOutcome(NativeTransformWriteOutcome.SkippedMissingBone);
            return;
        }

        var initialGameTransform = *gameTransformAccess;
        if (!TransformSafety.TrySanitizeNativeTransform(ref initialGameTransform))
        {
            RecordOutcome(NativeTransformWriteOutcome.SkippedUnsafeTransform);
            return;
        }

        var initialPos = initialGameTransform.Translation.ToVector3();
        var initialRot = initialGameTransform.Rotation.ToQuaternion();
        var initialScale = initialGameTransform.Scale.ToVector3();

        var modTransform = effectiveTransform.ModifyExistingTransform(*gameTransformAccess);
        RecordOutcome(SetGameTransform(cBase, modTransform, PoseType.Model));

        if (!TryGetPose(cBase, PartialSkeletonIndex, BoneIndex, BoneName, ParentBoneIndex, out var pose) || pose->ModelInSync == 0)
            return;

        var access2 = GetGameTransformAccess(cBase, PoseType.Model);
        if (access2 == null)
            return;

        var childTransform = *access2;
        if (!TransformSafety.TrySanitizeNativeTransform(ref childTransform))
            return;

        var childScaleToUse = childTransform.Scale.ToVector3();

        if (effectiveTransform.ChildScalingIndependent)
        {
            childScaleToUse = new Vector3(
                initialScale.X * effectiveTransform.ChildScaling.X,
                initialScale.Y * effectiveTransform.ChildScaling.Y,
                initialScale.Z * effectiveTransform.ChildScaling.Z
            );
        }

        var shouldPropagateScale = effectiveTransform.PropagateScale &&
            (!effectiveTransform.Scaling.Equals(Vector3.One) ||
             (effectiveTransform.ChildScalingIndependent && !effectiveTransform.ChildScaling.Equals(Vector3.One)));

        PropagateChildren(cBase, access2, effectiveTransform, initialPos, initialRot, initialScale,
            effectiveTransform.PropagateTranslation && !effectiveTransform.Translation.Equals(Vector3.Zero),
            effectiveTransform.PropagateRotation && effectiveTransform.HasEffectiveRotation(),
            shouldPropagateScale,
            childScaleToUse);
    }


    public unsafe void PropagateChildren(CharacterBase* cBase, hkQsTransformf* transform, BoneTransform appliedTransform, Vector3 initialPos, Quaternion initialRot, Vector3 initialScale, bool propagateTranslation, bool propagateRotation, bool propagateScale, Vector3 childScale, bool includePartials = true)
    {
        // Bone parenting
        // Adapted from Anamnesis Studio code shared by Yuki - thank you!

        if (transform == null || appliedTransform == null
            || !TransformSafety.IsFinite(initialPos)
            || !TransformSafety.IsFinite(initialScale)
            || !TransformSafety.IsFinite(childScale)
            || !TransformSafety.TryNormalize(initialRot, out var normalizedInitialRotation))
            return;

        var sourceTransform = *transform;
        if (!TransformSafety.TrySanitizeNativeTransform(ref sourceTransform))
            return;

        // Original parent-bone position after its offsets have been applied.
        var sourcePos = sourceTransform.Translation.ToVector3();
        var sourceRotation = sourceTransform.Rotation.ToQuaternion();
        var deltaRotationCandidate = Quaternion.Multiply(sourceRotation, Quaternion.Conjugate(normalizedInitialRotation));
        if (!TransformSafety.TryNormalize(deltaRotationCandidate, out var deltaRot)
            || !TransformSafety.TryDivide(childScale, initialScale, out var deltaScale))
            return;

        var deltaPos = sourcePos - initialPos;
        if (!TransformSafety.IsFinite(deltaPos))
            return;

        foreach (var (child, depth) in GetDescendantsWithDepth())
        {
            var attenuation = TransformSafety.ClampFinite(
                MathF.Pow(appliedTransform.PropagationFalloff, depth),
                0f,
                1f,
                0f);
            if (attenuation <= 0f)
                continue;

            try
            {
                var access = child.GetGameTransformAccess(cBase, PoseType.Model);
                if (access == null)
                    continue;

                var childTransform = *access;
                if (!TransformSafety.TrySanitizeNativeTransform(ref childTransform)
                    || !InteropAlloc.TryGetMatrix(access, out var matrix))
                    continue;

                var offset = access->Translation.ToVector3() - sourcePos;
                if (!TransformSafety.IsFinite(offset))
                    continue;

                if (propagateScale)
                {
                    var scaleMatrix = Matrix4x4.CreateScale(Vector3.Lerp(Vector3.One, deltaScale, attenuation), Vector3.Zero);
                    matrix *= scaleMatrix;
                    offset = Vector3.Transform(offset, scaleMatrix);
                }
                if (propagateRotation)
                {
                    var weightedRotation = Quaternion.Slerp(Quaternion.Identity, deltaRot, attenuation);
                    if (!TransformSafety.TryNormalize(weightedRotation, out weightedRotation))
                        continue;

                    matrix *= Matrix4x4.CreateFromQuaternion(weightedRotation);
                    offset = Vector3.Transform(offset, weightedRotation);
                }

                matrix.Translation = sourcePos + offset;
                if (propagateTranslation)
                    matrix.Translation += deltaPos * attenuation;

                if (TransformSafety.IsFinite(matrix))
                    InteropAlloc.TrySetMatrix(access, matrix);
            }
            catch (Exception)
            {
                // A malformed child must not prevent safe siblings from receiving propagation.
            }
        }
    }

    public void ApplyModelScale(CharacterBase* cBase)
    {
        if (AppliedTransform != null)
            ApplyTransFunc(cBase, AppliedTransform, AppliedTransform.ModifyExistingScale);
    }

    public void ApplyModelRotation(CharacterBase* cBase)
    {
        if (AppliedTransform != null)
            ApplyTransFunc(cBase, AppliedTransform, AppliedTransform.ModifyExistingRotation);
    }

    public void ApplyModelFullTranslation(CharacterBase* cBase)
    {
        if (AppliedTransform != null)
            ApplyTransFunc(cBase, AppliedTransform, AppliedTransform.ModifyExistingTranslationWithRotation);
    }

    public void ApplyStraightModelTranslation(CharacterBase* cBase)
    {
        if (AppliedTransform != null)
            ApplyTransFunc(cBase, AppliedTransform, AppliedTransform.ModifyExistingTranslation);
    }

    private void ApplyTransFunc(CharacterBase* cBase, BoneTransform appliedTransform, Func<hkQsTransformf, hkQsTransformf> modTrans)
    {
        if (!IsActive)
            return;

        if (cBase != null
            && appliedTransform.IsEdited()
            && GetGameTransform(cBase, PoseType.Model) is hkQsTransformf gameTransform
            && !gameTransform.Equals(Constants.NullTransform))
        {
            var modTransform = modTrans(gameTransform);

            if (!modTransform.Equals(gameTransform) && !modTransform.Equals(Constants.NullTransform))
            {
                SetGameTransform(cBase, modTransform, PoseType.Model);
            }
        }
    }


    /// <summary>
    /// Checks for a non-zero and non-identity (root) scale.
    /// </summary>
    /// <param name="mb">The bone to check</param>
    /// <returns>If the scale should be applied.</returns>
    public bool IsModifiedScale()
    {
        var appliedTransform = AppliedTransform;
        if (!IsActive || appliedTransform == null)
            return false;
        return appliedTransform.Scaling.X != 0 && appliedTransform.Scaling.X != 1 ||
               appliedTransform.Scaling.Y != 0 && appliedTransform.Scaling.Y != 1 ||
               appliedTransform.Scaling.Z != 0 && appliedTransform.Scaling.Z != 1;
    }
}
