// Copyright (c) Customize+.
// Licensed under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;

namespace CustomizePlus.Core.Data;

[Serializable]
public sealed class AdvancedBodyScalingProfileSettings
{
    public bool UseProfileOverrides { get; set; } = false;
    public AdvancedBodyScalingOverrides Overrides { get; set; } = new();

    public AdvancedBodyScalingSettings Resolve(AdvancedBodyScalingSettings baseline)
        => UseProfileOverrides ? Overrides.MergeOnto(baseline) : baseline.DeepCopy();

    public AdvancedBodyScalingProfileSettings DeepCopy()
        => new()
        {
            UseProfileOverrides = UseProfileOverrides,
            Overrides = Overrides.DeepCopy()
        };
}

[Serializable]
public sealed class AdvancedBodyScalingOverrides
{
    public bool? Enabled { get; set; }
    public AdvancedBodyScalingMode? Mode { get; set; }
    public bool? AnimationSafeModeEnabled { get; set; }
    public bool? PoseCorrectivesEnabled { get; set; }
    public float? PoseCorrectiveStrength { get; set; }
    public bool? FullIkRetargetingEnabled { get; set; }
    public float? FullIkRetargetingStrength { get; set; }
    public float? FullIkRetargetingPelvisStrength { get; set; }
    public float? FullIkRetargetingSpineStrength { get; set; }
    public float? FullIkRetargetingArmStrength { get; set; }
    public float? FullIkRetargetingLegStrength { get; set; }
    public float? FullIkRetargetingHeadStrength { get; set; }
    public float? FullIkRetargetingReachAdaptationStrength { get; set; }
    public float? FullIkRetargetingStrideAdaptationStrength { get; set; }
    public float? FullIkRetargetingPosturePreservationStrength { get; set; }
    public float? FullIkRetargetingMotionSafetyBias { get; set; }
    public float? FullIkRetargetingBlendBias { get; set; }
    public float? FullIkRetargetingMaxCorrectionClamp { get; set; }
    public bool? FullBodyIkEnabled { get; set; }
    public float? FullBodyIkStrength { get; set; }
    public int? FullBodyIkIterationCount { get; set; }
    public float? FullBodyIkConvergenceTolerance { get; set; }
    public float? FullBodyIkPelvisCompensationStrength { get; set; }
    public float? FullBodyIkSpineRedistributionStrength { get; set; }
    public float? FullBodyIkLegStrength { get; set; }
    public float? FullBodyIkArmStrength { get; set; }
    public float? FullBodyIkHeadAlignmentStrength { get; set; }
    public float? FullBodyIkGroundingBias { get; set; }
    public float? FullBodyIkMotionSafetyBias { get; set; }
    public float? FullBodyIkMaxCorrectionClamp { get; set; }
    public float? SurfaceBalancingStrength { get; set; }
    public float? MassRedistributionStrength { get; set; }
    public AdvancedBodyScalingGuardrailMode? GuardrailMode { get; set; }
    public float? NaturalizationStrength { get; set; }
    public AdvancedBodyScalingPoseValidationMode? PoseValidationMode { get; set; }
    public float? NeckLengthCompensation { get; set; }
    public float? NeckShoulderBlendStrength { get; set; }
    public float? ClavicleShoulderSmoothing { get; set; }
    public Dictionary<AdvancedBodyRegion, AdvancedBodyScalingRegionProfileOverrides> RegionOverrides { get; set; } = new();
    public Dictionary<AdvancedBodyScalingCorrectiveRegion, AdvancedBodyScalingCorrectiveRegionOverrides> PoseCorrectiveRegionOverrides { get; set; } = new();
    public Dictionary<AdvancedBodyScalingFullBodyIkChain, AdvancedBodyScalingFullIkRetargetingChainOverrides> FullIkRetargetingChainOverrides { get; set; } = new();
    public Dictionary<AdvancedBodyScalingFullBodyIkChain, AdvancedBodyScalingFullBodyIkChainOverrides> FullBodyIkChainOverrides { get; set; } = new();

    public AdvancedBodyScalingSettings MergeOnto(AdvancedBodyScalingSettings baseline)
    {
        var merged = baseline.DeepCopy();

        if (Enabled.HasValue)
            merged.Enabled = Enabled.Value;

        if (Mode.HasValue)
            merged.Mode = Mode.Value;

        if (AnimationSafeModeEnabled.HasValue)
            merged.AnimationSafeModeEnabled = AnimationSafeModeEnabled.Value;

        if (PoseCorrectivesEnabled.HasValue)
            merged.PoseCorrectives.Enabled = PoseCorrectivesEnabled.Value;

        if (PoseCorrectiveStrength.HasValue)
            merged.PoseCorrectives.Strength = PoseCorrectiveStrength.Value;

        if (FullIkRetargetingEnabled.HasValue)
            merged.FullIkRetargeting.Enabled = FullIkRetargetingEnabled.Value;

        if (FullIkRetargetingStrength.HasValue)
            merged.FullIkRetargeting.GlobalStrength = FullIkRetargetingStrength.Value;

        if (FullIkRetargetingPelvisStrength.HasValue)
            merged.FullIkRetargeting.PelvisStrength = FullIkRetargetingPelvisStrength.Value;

        if (FullIkRetargetingSpineStrength.HasValue)
            merged.FullIkRetargeting.SpineStrength = FullIkRetargetingSpineStrength.Value;

        if (FullIkRetargetingArmStrength.HasValue)
            merged.FullIkRetargeting.ArmStrength = FullIkRetargetingArmStrength.Value;

        if (FullIkRetargetingLegStrength.HasValue)
            merged.FullIkRetargeting.LegStrength = FullIkRetargetingLegStrength.Value;

        if (FullIkRetargetingHeadStrength.HasValue)
            merged.FullIkRetargeting.HeadStrength = FullIkRetargetingHeadStrength.Value;

        if (FullIkRetargetingReachAdaptationStrength.HasValue)
            merged.FullIkRetargeting.ReachAdaptationStrength = FullIkRetargetingReachAdaptationStrength.Value;

        if (FullIkRetargetingStrideAdaptationStrength.HasValue)
            merged.FullIkRetargeting.StrideAdaptationStrength = FullIkRetargetingStrideAdaptationStrength.Value;

        if (FullIkRetargetingPosturePreservationStrength.HasValue)
            merged.FullIkRetargeting.PosturePreservationStrength = FullIkRetargetingPosturePreservationStrength.Value;

        if (FullIkRetargetingMotionSafetyBias.HasValue)
            merged.FullIkRetargeting.MotionSafetyBias = FullIkRetargetingMotionSafetyBias.Value;

        if (FullIkRetargetingBlendBias.HasValue)
            merged.FullIkRetargeting.BlendBias = FullIkRetargetingBlendBias.Value;

        if (FullIkRetargetingMaxCorrectionClamp.HasValue)
            merged.FullIkRetargeting.MaxCorrectionClamp = FullIkRetargetingMaxCorrectionClamp.Value;

        if (FullBodyIkEnabled.HasValue)
            merged.FullBodyIk.Enabled = FullBodyIkEnabled.Value;

        if (FullBodyIkStrength.HasValue)
            merged.FullBodyIk.GlobalStrength = FullBodyIkStrength.Value;

        if (FullBodyIkIterationCount.HasValue)
            merged.FullBodyIk.IterationCount = FullBodyIkIterationCount.Value;

        if (FullBodyIkConvergenceTolerance.HasValue)
            merged.FullBodyIk.ConvergenceTolerance = FullBodyIkConvergenceTolerance.Value;

        if (FullBodyIkPelvisCompensationStrength.HasValue)
            merged.FullBodyIk.PelvisCompensationStrength = FullBodyIkPelvisCompensationStrength.Value;

        if (FullBodyIkSpineRedistributionStrength.HasValue)
            merged.FullBodyIk.SpineRedistributionStrength = FullBodyIkSpineRedistributionStrength.Value;

        if (FullBodyIkLegStrength.HasValue)
            merged.FullBodyIk.LegStrength = FullBodyIkLegStrength.Value;

        if (FullBodyIkArmStrength.HasValue)
            merged.FullBodyIk.ArmStrength = FullBodyIkArmStrength.Value;

        if (FullBodyIkHeadAlignmentStrength.HasValue)
            merged.FullBodyIk.HeadAlignmentStrength = FullBodyIkHeadAlignmentStrength.Value;

        if (FullBodyIkGroundingBias.HasValue)
            merged.FullBodyIk.GroundingBias = FullBodyIkGroundingBias.Value;

        if (FullBodyIkMotionSafetyBias.HasValue)
            merged.FullBodyIk.MotionSafetyBias = FullBodyIkMotionSafetyBias.Value;

        if (FullBodyIkMaxCorrectionClamp.HasValue)
            merged.FullBodyIk.MaxCorrectionClamp = FullBodyIkMaxCorrectionClamp.Value;

        if (SurfaceBalancingStrength.HasValue)
            merged.SurfaceBalancingStrength = SurfaceBalancingStrength.Value;

        if (MassRedistributionStrength.HasValue)
            merged.MassRedistributionStrength = MassRedistributionStrength.Value;

        if (GuardrailMode.HasValue)
            merged.GuardrailMode = GuardrailMode.Value;

        if (NaturalizationStrength.HasValue)
            merged.NaturalizationStrength = NaturalizationStrength.Value;

        if (PoseValidationMode.HasValue)
            merged.PoseValidationMode = PoseValidationMode.Value;

        if (NeckLengthCompensation.HasValue)
            merged.NeckLengthCompensation = NeckLengthCompensation.Value;

        if (NeckShoulderBlendStrength.HasValue)
            merged.NeckShoulderBlendStrength = NeckShoulderBlendStrength.Value;

        if (ClavicleShoulderSmoothing.HasValue)
            merged.ClavicleShoulderSmoothing = ClavicleShoulderSmoothing.Value;

        if (RegionOverrides.Count > 0)
        {
            foreach (var (region, overrides) in RegionOverrides)
            {
                if (overrides == null)
                    continue;

                var profile = merged.GetRegionProfile(region);
                overrides.ApplyTo(profile);
            }
        }

        if (PoseCorrectiveRegionOverrides.Count > 0)
        {
            foreach (var (region, overrides) in PoseCorrectiveRegionOverrides)
            {
                if (overrides == null)
                    continue;

                var profile = merged.PoseCorrectives.GetRegionSettings(region);
                overrides.ApplyTo(profile);
            }
        }

        if (FullIkRetargetingChainOverrides.Count > 0)
        {
            foreach (var (chain, overrides) in FullIkRetargetingChainOverrides)
            {
                if (overrides == null)
                    continue;

                var settings = merged.FullIkRetargeting.GetChainSettings(chain);
                overrides.ApplyTo(settings);
            }
        }

        if (FullBodyIkChainOverrides.Count > 0)
        {
            foreach (var (chain, overrides) in FullBodyIkChainOverrides)
            {
                if (overrides == null)
                    continue;

                var settings = merged.FullBodyIk.GetChainSettings(chain);
                overrides.ApplyTo(settings);
            }
        }

        return merged;
    }

    public AdvancedBodyScalingOverrides DeepCopy()
        => new()
        {
            Enabled = Enabled,
            Mode = Mode,
            AnimationSafeModeEnabled = AnimationSafeModeEnabled,
            PoseCorrectivesEnabled = PoseCorrectivesEnabled,
            PoseCorrectiveStrength = PoseCorrectiveStrength,
            FullIkRetargetingEnabled = FullIkRetargetingEnabled,
            FullIkRetargetingStrength = FullIkRetargetingStrength,
            FullIkRetargetingPelvisStrength = FullIkRetargetingPelvisStrength,
            FullIkRetargetingSpineStrength = FullIkRetargetingSpineStrength,
            FullIkRetargetingArmStrength = FullIkRetargetingArmStrength,
            FullIkRetargetingLegStrength = FullIkRetargetingLegStrength,
            FullIkRetargetingHeadStrength = FullIkRetargetingHeadStrength,
            FullIkRetargetingReachAdaptationStrength = FullIkRetargetingReachAdaptationStrength,
            FullIkRetargetingStrideAdaptationStrength = FullIkRetargetingStrideAdaptationStrength,
            FullIkRetargetingPosturePreservationStrength = FullIkRetargetingPosturePreservationStrength,
            FullIkRetargetingMotionSafetyBias = FullIkRetargetingMotionSafetyBias,
            FullIkRetargetingBlendBias = FullIkRetargetingBlendBias,
            FullIkRetargetingMaxCorrectionClamp = FullIkRetargetingMaxCorrectionClamp,
            FullBodyIkEnabled = FullBodyIkEnabled,
            FullBodyIkStrength = FullBodyIkStrength,
            FullBodyIkIterationCount = FullBodyIkIterationCount,
            FullBodyIkConvergenceTolerance = FullBodyIkConvergenceTolerance,
            FullBodyIkPelvisCompensationStrength = FullBodyIkPelvisCompensationStrength,
            FullBodyIkSpineRedistributionStrength = FullBodyIkSpineRedistributionStrength,
            FullBodyIkLegStrength = FullBodyIkLegStrength,
            FullBodyIkArmStrength = FullBodyIkArmStrength,
            FullBodyIkHeadAlignmentStrength = FullBodyIkHeadAlignmentStrength,
            FullBodyIkGroundingBias = FullBodyIkGroundingBias,
            FullBodyIkMotionSafetyBias = FullBodyIkMotionSafetyBias,
            FullBodyIkMaxCorrectionClamp = FullBodyIkMaxCorrectionClamp,
            SurfaceBalancingStrength = SurfaceBalancingStrength,
            MassRedistributionStrength = MassRedistributionStrength,
            GuardrailMode = GuardrailMode,
            NaturalizationStrength = NaturalizationStrength,
            PoseValidationMode = PoseValidationMode,
            NeckLengthCompensation = NeckLengthCompensation,
            NeckShoulderBlendStrength = NeckShoulderBlendStrength,
            ClavicleShoulderSmoothing = ClavicleShoulderSmoothing,
            RegionOverrides = RegionOverrides.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.DeepCopy()),
            PoseCorrectiveRegionOverrides = PoseCorrectiveRegionOverrides.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.DeepCopy()),
            FullIkRetargetingChainOverrides = FullIkRetargetingChainOverrides.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.DeepCopy()),
            FullBodyIkChainOverrides = FullBodyIkChainOverrides.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.DeepCopy())
        };
}

[Serializable]
public sealed class AdvancedBodyScalingRegionProfileOverrides
{
    public float? InfluenceMultiplier { get; set; }
    public float? SmoothingMultiplier { get; set; }
    public float? GuardrailMultiplier { get; set; }
    public float? MassRedistributionMultiplier { get; set; }
    public float? PoseValidationMultiplier { get; set; }
    public float? NaturalizationMultiplier { get; set; }
    public bool? AllowNaturalization { get; set; }
    public bool? AllowGuardrails { get; set; }
    public bool? AllowPoseValidation { get; set; }

    public bool IsEmpty =>
        !InfluenceMultiplier.HasValue &&
        !SmoothingMultiplier.HasValue &&
        !GuardrailMultiplier.HasValue &&
        !MassRedistributionMultiplier.HasValue &&
        !PoseValidationMultiplier.HasValue &&
        !NaturalizationMultiplier.HasValue &&
        !AllowNaturalization.HasValue &&
        !AllowGuardrails.HasValue &&
        !AllowPoseValidation.HasValue;

    public void ApplyTo(AdvancedBodyScalingRegionProfile profile)
    {
        if (InfluenceMultiplier.HasValue)
            profile.InfluenceMultiplier = InfluenceMultiplier.Value;

        if (SmoothingMultiplier.HasValue)
            profile.SmoothingMultiplier = SmoothingMultiplier.Value;

        if (GuardrailMultiplier.HasValue)
            profile.GuardrailMultiplier = GuardrailMultiplier.Value;

        if (MassRedistributionMultiplier.HasValue)
            profile.MassRedistributionMultiplier = MassRedistributionMultiplier.Value;

        if (PoseValidationMultiplier.HasValue)
            profile.PoseValidationMultiplier = PoseValidationMultiplier.Value;

        if (NaturalizationMultiplier.HasValue)
            profile.NaturalizationMultiplier = NaturalizationMultiplier.Value;

        if (AllowNaturalization.HasValue)
            profile.AllowNaturalization = AllowNaturalization.Value;

        if (AllowGuardrails.HasValue)
            profile.AllowGuardrails = AllowGuardrails.Value;

        if (AllowPoseValidation.HasValue)
            profile.AllowPoseValidation = AllowPoseValidation.Value;
    }

    public AdvancedBodyScalingRegionProfileOverrides DeepCopy()
        => new()
        {
            InfluenceMultiplier = InfluenceMultiplier,
            SmoothingMultiplier = SmoothingMultiplier,
            GuardrailMultiplier = GuardrailMultiplier,
            MassRedistributionMultiplier = MassRedistributionMultiplier,
            PoseValidationMultiplier = PoseValidationMultiplier,
            NaturalizationMultiplier = NaturalizationMultiplier,
            AllowNaturalization = AllowNaturalization,
            AllowGuardrails = AllowGuardrails,
            AllowPoseValidation = AllowPoseValidation
        };
}

[Serializable]
public sealed class AdvancedBodyScalingCorrectiveRegionOverrides
{
    public bool? Enabled { get; set; }
    public float? Strength { get; set; }

    public bool IsEmpty => !Enabled.HasValue && !Strength.HasValue;

    public void ApplyTo(AdvancedBodyScalingCorrectiveRegionSettings profile)
    {
        if (Enabled.HasValue)
            profile.Enabled = Enabled.Value;

        if (Strength.HasValue)
            profile.Strength = Strength.Value;
    }

    public AdvancedBodyScalingCorrectiveRegionOverrides DeepCopy()
        => new()
        {
            Enabled = Enabled,
            Strength = Strength,
        };
}
