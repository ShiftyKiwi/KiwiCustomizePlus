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
        Local, Model, BindPose, World
    }

    public readonly Armature MasterArmature;

    public readonly int PartialSkeletonIndex;
    public readonly int BoneIndex;

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

        Plugin.Logger.Verbose($"Linking {BoneName} to {template.Name}");
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
    public IEnumerable<ModelBone> GetAncestors(bool includeSelf = true) => includeSelf
        ? GetAncestors(new List<ModelBone>() { this })
        : GetAncestors(new List<ModelBone>());

    private IEnumerable<ModelBone> GetAncestors(List<ModelBone> tail)
    {
        tail.Add(this);
        if (ParentBone is ModelBone mb && mb != null)
        {
            return mb.GetAncestors(tail);
        }
        else
        {
            return tail;
        }
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

        var skelly = cBase->Skeleton;
        var pSkelly = skelly->PartialSkeletons[PartialSkeletonIndex];
        var targetPose = pSkelly.GetHavokPose(Constants.TruePoseIndex);
        //hkaPose* targetPose = cBase->Skeleton->PartialSkeletons[PartialSkeletonIndex].GetHavokPose(Constants.TruePoseIndex);

        if (targetPose == null) return Constants.NullTransform;

        if (BoneIndex >= targetPose->Skeleton->Bones.Length) return Constants.NullTransform;

        return refFrame switch
        {
            PoseType.Local => targetPose->LocalPose[BoneIndex],
            PoseType.Model => targetPose->ModelPose[BoneIndex],
            _ => Constants.NullTransform
            //TODO properly implement the other options
        };
    }

    public hkQsTransformf* GetGameTransformAccess(CharacterBase* cBase, PoseType refFrame)
    {

        var skelly = cBase->Skeleton;
        var pSkelly = skelly->PartialSkeletons[PartialSkeletonIndex];
        var targetPose = pSkelly.GetHavokPose(Constants.TruePoseIndex);
        //hkaPose* targetPose = cBase->Skeleton->PartialSkeletons[PartialSkeletonIndex].GetHavokPose(Constants.TruePoseIndex);

        if (targetPose == null)
            return null;

        // It's really gonna crash without it, skeleton changes aren't getting picked up fast enough
        if (BoneIndex >= targetPose->Skeleton->Bones.Length) return null;

        return refFrame switch
        {
            PoseType.Local => targetPose->AccessBoneLocalSpace(BoneIndex),
            PoseType.Model => targetPose->AccessBoneModelSpace(BoneIndex, PropagateOrNot.DontPropagate),
            _ => null
            //TODO properly implement the other options
        }; ;
    }

    private void SetGameTransform(CharacterBase* cBase, hkQsTransformf transform, PoseType refFrame)
    {
        SetGameTransform(cBase, transform, PartialSkeletonIndex, BoneIndex, refFrame);
    }

    private static void SetGameTransform(CharacterBase* cBase, hkQsTransformf transform, int partialIndex, int boneIndex, PoseType refFrame)
    {
        var skelly = cBase->Skeleton;
        var pSkelly = skelly->PartialSkeletons[partialIndex];
        var targetPose = pSkelly.GetHavokPose(Constants.TruePoseIndex);
        //hkaPose* targetPose = cBase->Skeleton->PartialSkeletons[PartialSkeletonIndex].GetHavokPose(Constants.TruePoseIndex);

        if (targetPose == null || targetPose->ModelInSync == 0) return;

        switch (refFrame)
        {
            case PoseType.Local:
                targetPose->LocalPose.Data[boneIndex] = transform;
                return;

            case PoseType.Model:
                targetPose->ModelPose.Data[boneIndex] = transform;
                return;

            default:
                return;

                //TODO properly implement the other options
        }
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

        ApplyEffectiveTransform(cBase, effectiveTransform);
    }

    public void ApplyRuntimeCorrection(CharacterBase* cBase, BoneTransform correction)
    {
        if (cBase == null || correction == null || !correction.IsEdited(true))
            return;

        ApplyEffectiveTransform(cBase, correction);
    }

    private void ApplyEffectiveTransform(CharacterBase* cBase, BoneTransform effectiveTransform)
    {
        if (cBase == null || effectiveTransform == null || !effectiveTransform.IsEdited(true))
            return;

        var doPropagate = effectiveTransform.PropagateTranslation ||
                          effectiveTransform.PropagateRotation ||
                          effectiveTransform.PropagateScale;

        if (!doPropagate)
        {
            var gameTransform = GetGameTransform(cBase, PoseType.Model);
            if (!gameTransform.Equals(Constants.NullTransform))
            {
                var modify_Transform = effectiveTransform.ModifyExistingTransform(gameTransform);
                if (!modify_Transform.Equals(Constants.NullTransform))
                {
                    SetGameTransform(cBase, modify_Transform, PoseType.Model);
                }
            }

            return;
        }

        var gameTransformAccess = GetGameTransformAccess(cBase, PoseType.Model);
        if (gameTransformAccess == null)
            return;

        var initialPos = gameTransformAccess->Translation.ToVector3();
        var initialRot = gameTransformAccess->Rotation.ToQuaternion();
        var initialScale = gameTransformAccess->Scale.ToVector3();

        var modTransform = effectiveTransform.ModifyExistingTransform(*gameTransformAccess);
        SetGameTransform(cBase, modTransform, PoseType.Model);

        var pose = cBase->Skeleton->PartialSkeletons[PartialSkeletonIndex].GetHavokPose(Constants.TruePoseIndex);
        if (pose == null || pose->ModelInSync == 0)
            return;

        var access2 = GetGameTransformAccess(cBase, PoseType.Model);
        if (access2 == null)
            return;

        var childScaleToUse = access2->Scale.ToVector3();

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

        // Original Parent Bone position after it had its offsets applied
        var sourcePos = transform->Translation.ToVector3();

        var deltaRot = transform->Rotation.ToQuaternion() / initialRot;
        var deltaPos = sourcePos - initialPos;
        var deltaScale = childScale / initialScale;

        foreach (var (child, depth) in GetDescendantsWithDepth())
        {
            var attenuation = Math.Clamp(MathF.Pow(appliedTransform.PropagationFalloff, depth), 0f, 1f);
            if (attenuation <= 0f)
                continue;

            // Plugin.Logger.Debug($"Propagating to {child.BoneName}...");
            var access = child.GetGameTransformAccess(cBase, PoseType.Model);
            if (access != null)
            {
                var offset = access->Translation.ToVector3() - sourcePos;

                var matrix = InteropAlloc.GetMatrix(access);
                if (propagateScale)
                {
                    var scaleMatrix = Matrix4x4.CreateScale(Vector3.Lerp(Vector3.One, deltaScale, attenuation), Vector3.Zero);
                    matrix *= scaleMatrix;
                    offset = Vector3.Transform(offset, scaleMatrix);
                }
                if (propagateRotation)
                {
                    var weightedRotation = Quaternion.Slerp(Quaternion.Identity, deltaRot, attenuation);
                    matrix *= Matrix4x4.CreateFromQuaternion(weightedRotation);
                    offset = Vector3.Transform(offset, weightedRotation);
                }

                matrix.Translation = sourcePos + offset;
                if (propagateTranslation)
                    matrix.Translation += deltaPos * attenuation;

                InteropAlloc.SetMatrix(access, matrix);
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
