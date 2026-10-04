// Copyright (c) Customize+.
// Licensed under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;

namespace CustomizePlus.Core.Data;

/// <summary>Presentation groups for the profile Advanced Body Scaling override editor.</summary>
public enum AdvancedBodyScalingOverrideSection
{
    GeneralAndShaping,
    RaceSpecificNeck,
    BoneImportance,
    PoseCorrectives,
    FullIkRetargeting,
    MotionWarping,
    FullBodyIk,
    RegionTuning,
}

/// <summary>Counts explicit nullable profile overrides without resolving runtime settings.</summary>
public readonly record struct AdvancedBodyScalingOverrideSummary(
    int GeneralAndShaping,
    int RaceSpecificNeck,
    int BoneImportance,
    int PoseCorrectives,
    int FullIkRetargeting,
    int MotionWarping,
    int FullBodyIk,
    int RegionTuning)
{
    public int TotalCount
        => GeneralAndShaping + RaceSpecificNeck + BoneImportance + PoseCorrectives + FullIkRetargeting + MotionWarping + FullBodyIk + RegionTuning;

    public int GetCount(AdvancedBodyScalingOverrideSection section)
        => section switch
        {
            AdvancedBodyScalingOverrideSection.GeneralAndShaping => GeneralAndShaping,
            AdvancedBodyScalingOverrideSection.RaceSpecificNeck => RaceSpecificNeck,
            AdvancedBodyScalingOverrideSection.BoneImportance => BoneImportance,
            AdvancedBodyScalingOverrideSection.PoseCorrectives => PoseCorrectives,
            AdvancedBodyScalingOverrideSection.FullIkRetargeting => FullIkRetargeting,
            AdvancedBodyScalingOverrideSection.MotionWarping => MotionWarping,
            AdvancedBodyScalingOverrideSection.FullBodyIk => FullBodyIk,
            AdvancedBodyScalingOverrideSection.RegionTuning => RegionTuning,
            _ => 0,
        };
}

/// <summary>
/// Small presentation helpers for the bespoke Profile overrides UI. These methods deliberately
/// inspect only nullable override state; runtime resolution remains in AdvancedBodyScalingProfileSettings.
/// </summary>
public static class AdvancedBodyScalingOverridePresentation
{
    public static AdvancedBodyScalingOverrideSummary Summarize(AdvancedBodyScalingOverrides overrides)
        => new(
            CountDefined(
                overrides.Enabled,
                overrides.Mode,
                overrides.AnimationSafeModeEnabled,
                overrides.SurfaceBalancingStrength,
                overrides.MassRedistributionStrength,
                overrides.BilateralConsistencyEnabled,
                overrides.HierarchicalShapingEnabled,
                overrides.HierarchicalAuthoredRelaxationEnabled,
                overrides.ProportionalBalanceEnabled,
                overrides.ProportionalBalanceStrength,
                overrides.SurfaceSmoothnessEnabled,
                overrides.SurfaceSmoothnessStrength,
                overrides.CrossSectionConditioningEnabled,
                overrides.CrossSectionConditioningStrength,
                overrides.ShapeFairnessEnabled,
                overrides.ShapeFairnessStrength,
                overrides.LocalVolumeIntentEnabled,
                overrides.LocalVolumeIntentStrength,
                overrides.PoseAwareJointCorrectivesEnabled,
                overrides.PoseAwareJointCorrectivesStrength,
                overrides.GuardrailMode,
                overrides.NaturalizationStrength,
                overrides.PoseValidationMode,
                overrides.NeckLengthCompensation,
                overrides.NeckShoulderBlendStrength,
                overrides.ClavicleShoulderSmoothing),
            overrides.HasRaceSpecificNeckOverrides ? 1 : 0,
            CountDefined(
                overrides.ModelDerivedBoneImportanceEnabled,
                overrides.PreferTrueSkinWeightImportance,
                overrides.BoneImportanceHeuristicBlend),
            CountDefined(
                overrides.PoseCorrectivesEnabled,
                overrides.PoseCorrectiveStrength,
                overrides.PoseCorrectivePoseMapSharpness,
                overrides.PoseCorrectiveDamping,
                overrides.PoseCorrectiveMaxCorrectionClamp)
            + CountCorrectiveRegions(overrides.PoseCorrectiveRegionOverrides),
            CountDefined(
                overrides.FullIkRetargetingEnabled,
                overrides.FullIkRetargetingStrength,
                overrides.FullIkRetargetingPelvisStrength,
                overrides.FullIkRetargetingSpineStrength,
                overrides.FullIkRetargetingArmStrength,
                overrides.FullIkRetargetingLegStrength,
                overrides.FullIkRetargetingHeadStrength,
                overrides.FullIkRetargetingReachAdaptationStrength,
                overrides.FullIkRetargetingStrideAdaptationStrength,
                overrides.FullIkRetargetingPosturePreservationStrength,
                overrides.FullIkRetargetingMotionSafetyBias,
                overrides.FullIkRetargetingBlendBias,
                overrides.FullIkRetargetingMaxCorrectionClamp)
            + CountChains(overrides.FullIkRetargetingChainOverrides),
            CountDefined(
                overrides.MotionWarpingEnabled,
                overrides.MotionWarpingStrength,
                overrides.MotionWarpingStrideStrength,
                overrides.MotionWarpingOrientationStrength,
                overrides.MotionWarpingPostureStrength,
                overrides.MotionWarpingMotionSafetyBias,
                overrides.MotionWarpingBlendBias,
                overrides.MotionWarpingMaxCorrectionClamp)
            + CountChains(overrides.MotionWarpingChainOverrides),
            CountDefined(
                overrides.FullBodyIkEnabled,
                overrides.FullBodyIkStrength,
                overrides.FullBodyIkIterationCount,
                overrides.FullBodyIkConvergenceTolerance,
                overrides.FullBodyIkPelvisCompensationStrength,
                overrides.FullBodyIkSpineRedistributionStrength,
                overrides.FullBodyIkLegStrength,
                overrides.FullBodyIkArmStrength,
                overrides.FullBodyIkHeadAlignmentStrength,
                overrides.FullBodyIkGroundingBias,
                overrides.FullBodyIkMotionSafetyBias,
                overrides.FullBodyIkMaxCorrectionClamp)
            + CountChains(overrides.FullBodyIkChainOverrides),
            CountRegions(overrides.RegionOverrides));

    public static bool ShouldShowRow(bool showOverridesOnly, bool isOverridden)
        => !showOverridesOnly || isOverridden;

    private static int CountDefined(params object?[] values)
    {
        var count = 0;
        foreach (var value in values)
        {
            if (value != null)
                ++count;
        }

        return count;
    }

    private static int CountRegions(IReadOnlyDictionary<AdvancedBodyRegion, AdvancedBodyScalingRegionProfileOverrides> regions)
    {
        var count = 0;
        foreach (var value in regions.Values)
        {
            if (value == null)
                continue;

            count += CountDefined(
                value.InfluenceMultiplier,
                value.SmoothingMultiplier,
                value.GuardrailMultiplier,
                value.MassRedistributionMultiplier,
                value.PoseValidationMultiplier,
                value.NaturalizationMultiplier,
                value.AllowNaturalization,
                value.AllowGuardrails,
                value.AllowPoseValidation);
        }

        return count;
    }

    private static int CountCorrectiveRegions(IReadOnlyDictionary<AdvancedBodyScalingCorrectiveRegion, AdvancedBodyScalingCorrectiveRegionOverrides> regions)
    {
        var count = 0;
        foreach (var value in regions.Values)
        {
            if (value != null)
                count += CountDefined(value.Enabled, value.Strength);
        }

        return count;
    }

    private static int CountChains<TChain>(IReadOnlyDictionary<AdvancedBodyScalingFullBodyIkChain, TChain> chains)
        where TChain : class
        => chains.Values.Sum(chain => chain switch
        {
            AdvancedBodyScalingFullIkRetargetingChainOverrides value => CountDefined(value.Enabled, value.Strength),
            AdvancedBodyScalingMotionWarpingChainOverrides value => CountDefined(value.Enabled, value.Strength),
            AdvancedBodyScalingFullBodyIkChainOverrides value => CountDefined(value.Enabled, value.Strength),
            _ => 0,
        });
}
