// Copyright (c) Customize+.
// Licensed under the MIT license.

using System;
using System.Collections.Generic;
using System.Numerics;
using CustomizePlus.Core.Extensions;

namespace CustomizePlus.Core.Data;

internal static class BoneRuntimeSafeguards
{
    private readonly record struct ScaleSafetyRule(
        float SafeMin,
        float SafeMax,
        float SoftBelow,
        float SoftAbove,
        float ChildTransfer,
        float VolumeCompensation);

    private static readonly ScaleSafetyRule ChestRule = new(0.65f, 1.55f, 0.15f, 0.45f, 0.45f, 0.35f);
    private static readonly ScaleSafetyRule GroinRule = new(0.60f, 1.60f, 0.15f, 0.40f, 0.50f, 0.25f);
    private static readonly ScaleSafetyRule LegSoftTissueRule = new(0.60f, 1.70f, 0.15f, 0.45f, 0.65f, 0.15f);

    private static readonly Dictionary<string, ScaleSafetyRule> NamedRules = new(StringComparer.Ordinal)
    {
        ["j_mune_l"] = ChestRule,
        ["j_mune_r"] = ChestRule,
        ["iv_c_mune_l"] = ChestRule,
        ["iv_c_mune_r"] = ChestRule,
        ["iv_shiri_l"] = GroinRule,
        ["iv_shiri_r"] = GroinRule,
        ["ya_shiri_phys_l"] = GroinRule,
        ["ya_shiri_phys_r"] = GroinRule,
        ["ya_daitai_phys_l"] = LegSoftTissueRule,
        ["ya_daitai_phys_r"] = LegSoftTissueRule,
        ["iv_daitai_phys_l"] = LegSoftTissueRule,
        ["iv_daitai_phys_r"] = LegSoftTissueRule,
    };

    private static readonly Dictionary<BoneData.BoneFamily, ScaleSafetyRule> FamilyRules = new()
    {
        [BoneData.BoneFamily.Spine] = new ScaleSafetyRule(0.60f, 1.80f, 0.15f, 0.50f, 0.70f, 0.10f),
        [BoneData.BoneFamily.Chest] = new ScaleSafetyRule(0.60f, 1.65f, 0.15f, 0.45f, 0.55f, 0.20f),
        [BoneData.BoneFamily.Groin] = new ScaleSafetyRule(0.60f, 1.65f, 0.15f, 0.45f, 0.60f, 0.15f),
        [BoneData.BoneFamily.Tail] = new ScaleSafetyRule(0.55f, 1.65f, 0.15f, 0.40f, 0.60f, 0.15f),
        [BoneData.BoneFamily.Hair] = new ScaleSafetyRule(0.60f, 1.50f, 0.10f, 0.30f, 0.65f, 0.10f),
        [BoneData.BoneFamily.Skirt] = new ScaleSafetyRule(0.60f, 1.60f, 0.10f, 0.35f, 0.65f, 0.10f),
        [BoneData.BoneFamily.Cape] = new ScaleSafetyRule(0.60f, 1.60f, 0.10f, 0.35f, 0.65f, 0.10f),
        [BoneData.BoneFamily.Armor] = new ScaleSafetyRule(0.60f, 1.60f, 0.10f, 0.35f, 0.70f, 0.05f),
        [BoneData.BoneFamily.Arms] = new ScaleSafetyRule(0.55f, 1.85f, 0.15f, 0.55f, 0.80f, 0.05f),
        [BoneData.BoneFamily.Legs] = new ScaleSafetyRule(0.55f, 1.85f, 0.15f, 0.55f, 0.80f, 0.05f),
    };

    public static BoneTransform Apply(
        string boneName,
        BoneTransform transform,
        bool enableSoftScaleLimits,
        bool enableAutomaticChildCompensation)
    {
        if (!enableSoftScaleLimits && !enableAutomaticChildCompensation)
            return transform.DeepCopy();

        if (!TryGetRule(boneName, out var rule))
            return transform.DeepCopy();

        var adjusted = transform.DeepCopy();

        if (enableSoftScaleLimits)
        {
            adjusted.Scaling = SanitizeScaleVector(adjusted.Scaling, rule);

            if (adjusted.ChildScalingIndependent)
                adjusted.ChildScaling = SanitizeScaleVector(adjusted.ChildScaling, rule);
        }

        if (enableAutomaticChildCompensation
            && adjusted.PropagateScale
            && !adjusted.ChildScalingIndependent
            && !adjusted.Scaling.IsApproximately(Vector3.One, 0.00001f))
        {
            adjusted.ChildScalingIndependent = true;
            adjusted.ChildScaling = BuildCompensatedChildScale(adjusted.Scaling, rule, enableSoftScaleLimits);
        }

        return adjusted;
    }

    private static bool TryGetRule(string boneName, out ScaleSafetyRule rule)
    {
        if (NamedRules.TryGetValue(boneName, out rule))
            return true;

        return FamilyRules.TryGetValue(BoneData.GetBoneFamily(boneName), out rule);
    }

    private static Vector3 BuildCompensatedChildScale(Vector3 parentScale, ScaleSafetyRule rule, bool enableSoftScaleLimits)
    {
        var damped = Vector3.One + ((parentScale - Vector3.One) * rule.ChildTransfer);
        if (rule.VolumeCompensation <= 0f)
            return enableSoftScaleLimits ? SanitizeScaleVector(damped, rule) : damped;

        var balanced = GetVolumeBalancedScale(parentScale);
        var compensated = Vector3.Lerp(damped, balanced, rule.VolumeCompensation);
        return enableSoftScaleLimits ? SanitizeScaleVector(compensated, rule) : compensated;
    }

    private static Vector3 GetVolumeBalancedScale(Vector3 scale)
    {
        const float epsilon = 0.05f;
        var x = Math.Max(MathF.Abs(scale.X), epsilon);
        var y = Math.Max(MathF.Abs(scale.Y), epsilon);
        var z = Math.Max(MathF.Abs(scale.Z), epsilon);
        var uniform = MathF.Pow(x * y * z, 1f / 3f);
        uniform = Math.Clamp(uniform, 0.20f, 4.00f);
        return new Vector3(uniform);
    }

    private static Vector3 SanitizeScaleVector(Vector3 scale, ScaleSafetyRule rule)
        => new(
            SanitizeAxis(scale.X, rule),
            SanitizeAxis(scale.Y, rule),
            SanitizeAxis(scale.Z, rule));

    private static float SanitizeAxis(float value, ScaleSafetyRule rule)
    {
        var sanitized = value;
        if (sanitized < rule.SafeMin)
            sanitized = rule.SafeMin - CompressOverflow(rule.SafeMin - sanitized, rule.SoftBelow);
        else if (sanitized > rule.SafeMax)
            sanitized = rule.SafeMax + CompressOverflow(sanitized - rule.SafeMax, rule.SoftAbove);

        var hardMin = Math.Max(0.20f, rule.SafeMin - rule.SoftBelow);
        var hardMax = rule.SafeMax + rule.SoftAbove;
        return Math.Clamp(sanitized, hardMin, hardMax);
    }

    private static float CompressOverflow(float overflow, float allowance)
    {
        if (overflow <= 0f || allowance <= 0f)
            return 0f;

        return allowance * overflow / (allowance + overflow);
    }
}
