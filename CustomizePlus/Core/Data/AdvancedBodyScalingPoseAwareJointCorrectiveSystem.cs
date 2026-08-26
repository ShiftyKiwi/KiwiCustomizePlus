// Copyright (c) Customize+.
// Licensed under the MIT license.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using CustomizePlus.Armatures.Data;
using CustomizePlus.Core.Extensions;
using FFXIVClientStructs.FFXIV.Client.Graphics.Scene;

namespace CustomizePlus.Core.Data;

/// <summary>
/// Lightweight, runtime-only support for a deliberately small group of joint transitions.
/// It consumes the published armature snapshot and never rebuilds static deformation state.
/// </summary>
internal static unsafe class AdvancedBodyScalingPoseAwareJointCorrectiveSystem
{
    private const float ResponseEpsilon = 0.0025f;
    private const float MaximumUniformScaleCorrection = 0.026f;

    private readonly record struct JointDefinition(
        string Category,
        string JointBone,
        string[] Receivers,
        float StartDegrees,
        float FullDegrees,
        float MaximumCorrection);

    private static readonly JointDefinition[] Definitions =
    {
        new("elbows", "j_ude_b_l", new[] { "j_ude_a_l", "j_ude_b_l" }, 24f, 112f, 0.024f),
        new("elbows", "j_ude_b_r", new[] { "j_ude_a_r", "j_ude_b_r" }, 24f, 112f, 0.024f),
        new("knees", "j_asi_b_l", new[] { "j_asi_a_l", "j_asi_b_l", "j_asi_c_l" }, 20f, 108f, 0.026f),
        new("knees", "j_asi_b_r", new[] { "j_asi_a_r", "j_asi_b_r", "j_asi_c_r" }, 20f, 108f, 0.026f),
        new("shoulders", "j_ude_a_l", new[] { "j_sako_l", "n_hkata_l", "j_ude_a_l" }, 22f, 98f, 0.020f),
        new("shoulders", "j_ude_a_r", new[] { "j_sako_r", "n_hkata_r", "j_ude_a_r" }, 22f, 98f, 0.020f),
        new("hips", "j_asi_a_l", new[] { "j_kosi", "j_asi_a_l" }, 20f, 96f, 0.020f),
        new("hips", "j_asi_a_r", new[] { "j_kosi", "j_asi_a_r" }, 20f, 96f, 0.020f),
    };

    internal static float EvaluatePoseResponse(float angleDegrees, float startDegrees, float fullDegrees)
        => AdvancedBodyScalingShapeConditioningMath.JointResponse(angleDegrees, startDegrees, fullDegrees);

    internal static float CalculateScaleMultiplier(float poseWeight, float strength, float importance, float maximumCorrection)
    {
        if (!float.IsFinite(poseWeight) || !float.IsFinite(strength) || !float.IsFinite(importance) || !float.IsFinite(maximumCorrection))
            return 1f;

        var correction = Math.Clamp(
            Math.Max(0f, poseWeight) * Math.Clamp(strength, 0f, 1f) * Math.Clamp(importance, 0f, 1f) * Math.Clamp(maximumCorrection, 0f, MaximumUniformScaleCorrection),
            0f,
            MaximumUniformScaleCorrection);
        return 1f + correction;
    }

    public static void Evaluate(
        Armature armature,
        CharacterBase* cBase,
        AdvancedBodyScalingSettings settings,
        Dictionary<string, Vector3> scaleMultipliers,
        PoseAwareJointCorrectiveDebugState debugState)
    {
        scaleMultipliers.Clear();
        debugState.Reset(settings.PoseAwareJointCorrectivesEnabled, settings.PoseAwareJointCorrectivesStrength);
        var started = Stopwatch.GetTimestamp();

        try
        {
            if (cBase == null || !armature.IsSkeletonBindingCurrent || !settings.Enabled || settings.Mode == AdvancedBodyScalingMode.Manual)
            {
                debugState.FinalizeState(false, "Pose-aware joint correctives are inactive because the static advanced-scaling binding is unavailable.");
                return;
            }

            if (!settings.PoseAwareJointCorrectivesEnabled || settings.PoseAwareJointCorrectivesStrength <= 0f)
            {
                debugState.FinalizeState(false, "Pose-aware joint correctives are disabled.");
                return;
            }

            var manifest = armature.GetCapabilityManifestSnapshot();
            foreach (var definition in Definitions)
            {
                if (!armature.TryGetPublishedBone(definition.JointBone, out var jointBone))
                {
                    debugState.RecordSafetySkip();
                    continue;
                }

                var angle = GetLocalRotationAngleDegrees(jointBone, cBase);
                var response = EvaluatePoseResponse(angle, definition.StartDegrees, definition.FullDegrees);
                if (response <= ResponseEpsilon)
                    continue;

                debugState.RecordEligibleJoint(definition.Category, response);
                var corrected = false;
                foreach (var receiver in definition.Receivers)
                {
                    if (!TryGetEligibleAutomaticReceiver(armature, receiver, manifest, out var importance))
                    {
                        debugState.RecordSafetySkip();
                        continue;
                    }

                    var multiplierValue = CalculateScaleMultiplier(
                        response,
                        settings.PoseAwareJointCorrectivesStrength,
                        importance,
                        definition.MaximumCorrection);
                    var correction = multiplierValue - 1f;
                    if (correction <= ResponseEpsilon)
                        continue;

                    var multiplier = new Vector3(multiplierValue);
                    if (scaleMultipliers.TryGetValue(receiver, out var existing))
                        scaleMultipliers[receiver] = existing * multiplier;
                    else
                        scaleMultipliers[receiver] = multiplier;

                    corrected = true;
                    debugState.RecordCorrection(definition.Category, response, correction);
                }

                if (corrected)
                    debugState.RecordCorrectedJoint();
            }

            debugState.FinalizeState(
                debugState.CorrectedJointCount > 0,
                debugState.CorrectedJointCount > 0
                    ? "Applied bounded scale support to trusted automatic joint-transition receivers."
                    : "No eligible automatic joint-transition receiver needed pose support this frame.");
        }
        finally
        {
            debugState.RecordEvaluationMilliseconds((Stopwatch.GetTimestamp() - started) * 1000d / Stopwatch.Frequency);
        }
    }

    private static float GetLocalRotationAngleDegrees(ModelBone bone, CharacterBase* cBase)
    {
        var transform = bone.GetGameTransform(cBase, ModelBone.PoseType.Local);
        if (transform.Equals(Constants.NullTransform))
            return 0f;

        var rotation = transform.Rotation.ToQuaternion();
        if (!float.IsFinite(rotation.X) || !float.IsFinite(rotation.Y) || !float.IsFinite(rotation.Z) || !float.IsFinite(rotation.W)
            || rotation.LengthSquared() <= float.Epsilon)
            return 0f;

        rotation = Quaternion.Normalize(rotation);
        var w = Math.Clamp(MathF.Abs(rotation.W), 0f, 1f);
        var angle = 2f * MathF.Acos(w) * 180f / MathF.PI;
        return float.IsFinite(angle) ? Math.Clamp(angle, 0f, 180f) : 0f;
    }

    private static bool TryGetEligibleAutomaticReceiver(
        Armature armature,
        string boneName,
        SkeletonCapabilityManifest manifest,
        out float importance)
    {
        importance = 0f;
        if (armature.IsExplicitTemplateTransform(boneName))
            return false;

        var transform = armature.GetAppliedBoneTransform(boneName);
        if (transform == null || transform.LockState != BoneLockState.Unlocked || transform.HasPinnedScaleAxes()
            || !TransformSafety.IsFinite(transform.Scaling) || transform.Scaling.X <= 0f || transform.Scaling.Y <= 0f || transform.Scaling.Z <= 0f)
            return false;

        var metadata = BoneData.GetMetadata(boneName);
        if (!metadata.HasTrust(BoneAutomationTrust.AdvancedCorrectiveSafe)
            || metadata.Role is BoneFunctionalRole.ClothingRig or BoneFunctionalRole.PropRig or BoneFunctionalRole.ArticulatedAppendage or BoneFunctionalRole.ArticulatedBodyFeature or BoneFunctionalRole.Unknown
            || !IsCapabilitySupported(metadata.Origin, manifest))
            return false;

        if (armature.ActiveBoneImportanceResult.ModelDerivedActive && armature.ActiveBoneImportanceResult.Scores.TryGetValue(boneName, out var score))
            importance = Math.Clamp(0.35f + (score * 0.65f), 0.35f, 1f);
        else
            importance = 0.65f;

        return true;
    }

    private static bool IsCapabilitySupported(BoneOrigin origin, SkeletonCapabilityManifest manifest)
        => origin switch
        {
            BoneOrigin.IVCS2 => manifest.GetState(SkeletonCapability.IVCS2) is SkeletonCapabilityState.Present or SkeletonCapabilityState.Partial,
            BoneOrigin.YAS => manifest.GetState(SkeletonCapability.YAS) is SkeletonCapabilityState.Present or SkeletonCapabilityState.Partial,
            BoneOrigin.NFLB => manifest.GetState(SkeletonCapability.NFLB) is SkeletonCapabilityState.Present or SkeletonCapabilityState.Partial,
            BoneOrigin.Skelomae => manifest.GetState(SkeletonCapability.Skelomae) is SkeletonCapabilityState.Present or SkeletonCapabilityState.Partial,
            BoneOrigin.UnknownCustom => false,
            _ => true,
        };
}

internal sealed class PoseAwareJointCorrectiveDebugState
{
    private readonly HashSet<string> _activeCategories = new(StringComparer.Ordinal);

    public bool Enabled { get; private set; }
    public bool Active { get; private set; }
    public float Strength { get; private set; }
    public int EligibleJointCount { get; private set; }
    public int CorrectedJointCount { get; private set; }
    public int WriteCount { get; private set; }
    public int SafetySkipCount { get; private set; }
    public float MaximumPoseWeight { get; private set; }
    public float MaximumCorrection { get; private set; }
    public double EvaluationMilliseconds { get; private set; }
    public long PoseCorrectiveRevision { get; private set; }
    public string Summary { get; private set; } = string.Empty;
    public IReadOnlyCollection<string> ActiveCategories => _activeCategories;

    public void Reset(bool enabled, float strength)
    {
        Enabled = enabled;
        Active = false;
        Strength = strength;
        EligibleJointCount = 0;
        CorrectedJointCount = 0;
        WriteCount = 0;
        SafetySkipCount = 0;
        MaximumPoseWeight = 0f;
        MaximumCorrection = 0f;
        EvaluationMilliseconds = 0d;
        Summary = string.Empty;
        _activeCategories.Clear();
    }

    public void RecordEligibleJoint(string category, float response)
    {
        EligibleJointCount++;
        MaximumPoseWeight = MathF.Max(MaximumPoseWeight, response);
        _activeCategories.Add(category);
    }

    public void RecordCorrection(string category, float response, float correction)
    {
        WriteCount++;
        MaximumPoseWeight = MathF.Max(MaximumPoseWeight, response);
        MaximumCorrection = MathF.Max(MaximumCorrection, correction);
        _activeCategories.Add(category);
    }

    public void RecordCorrectedJoint() => CorrectedJointCount++;
    public void RecordSafetySkip() => SafetySkipCount++;
    public void RecordEvaluationMilliseconds(double elapsed) => EvaluationMilliseconds = double.IsFinite(elapsed) && elapsed >= 0d ? elapsed : 0d;

    public void FinalizeState(bool active, string summary)
    {
        Active = active;
        Summary = summary;
        PoseCorrectiveRevision++;
    }
}
