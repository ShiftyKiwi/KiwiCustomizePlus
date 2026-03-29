// Copyright (c) Customize+.
// Licensed under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;

namespace CustomizePlus.Core.Data;

internal enum AdvancedBodyScalingRiskLevel
{
    Low = 0,
    Moderate = 1,
    High = 2,
}

internal sealed class AdvancedBodyScalingRegionStressResult
{
    public required AdvancedBodyScalingCorrectiveRegion Region { get; init; }
    public required string RegionName { get; init; }
    public required int BaseScore { get; init; }
    public required int CorrectiveOnlyScore { get; init; }
    public required int RetargetingScore { get; init; }
    public required int MotionWarpingScore { get; init; }
    public required int Score { get; init; }
    public required int CorrectiveReductionScore { get; init; }
    public required int RetargetingReductionScore { get; init; }
    public required int MotionWarpingReductionScore { get; init; }
    public required int FullBodyIkReductionScore { get; init; }
    public required float CorrectiveIntensity { get; init; }
    public required float RetargetingIntensity { get; init; }
    public required float MotionWarpingIntensity { get; init; }
    public required float FullBodyIkIntensity { get; init; }
    public required AdvancedBodyScalingRiskLevel BaseRiskLevel { get; init; }
    public required AdvancedBodyScalingRiskLevel CorrectiveOnlyRiskLevel { get; init; }
    public required AdvancedBodyScalingRiskLevel RetargetingRiskLevel { get; init; }
    public required AdvancedBodyScalingRiskLevel MotionWarpingRiskLevel { get; init; }
    public required AdvancedBodyScalingRiskLevel RiskLevel { get; init; }
    public required IReadOnlyList<string> Reasons { get; init; }
    public required IReadOnlyList<string> Bones { get; init; }
    public IReadOnlyList<string> RetargetingChains { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> MotionWarpingChains { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> FullBodyIkChains { get; init; } = Array.Empty<string>();
    public string CorrectiveSummary { get; init; } = string.Empty;
    public string RetargetingSummary { get; init; } = string.Empty;
    public string MotionWarpingSummary { get; init; } = string.Empty;
    public string FullBodyIkSummary { get; init; } = string.Empty;
}

internal sealed class AdvancedBodyScalingPoseStressResult
{
    public required string Name { get; init; }
    public required string Description { get; init; }
    public required int BaseScore { get; init; }
    public required int CorrectiveOnlyScore { get; init; }
    public required int RetargetingScore { get; init; }
    public required int MotionWarpingScore { get; init; }
    public required int Score { get; init; }
    public required AdvancedBodyScalingRiskLevel BaseRiskLevel { get; init; }
    public required AdvancedBodyScalingRiskLevel CorrectiveOnlyRiskLevel { get; init; }
    public required AdvancedBodyScalingRiskLevel RetargetingRiskLevel { get; init; }
    public required AdvancedBodyScalingRiskLevel MotionWarpingRiskLevel { get; init; }
    public required AdvancedBodyScalingRiskLevel RiskLevel { get; init; }
    public required IReadOnlyList<AdvancedBodyScalingRegionStressResult> Regions { get; init; }
}

internal sealed class AdvancedBodyScalingStressTestReport
{
    public required string SourceLabel { get; init; }
    public required int BaseOverallScore { get; init; }
    public required int CorrectiveOverallScore { get; init; }
    public required int RetargetingOverallScore { get; init; }
    public required int MotionWarpingOverallScore { get; init; }
    public required int OverallScore { get; init; }
    public required AdvancedBodyScalingRiskLevel BaseOverallRisk { get; init; }
    public required AdvancedBodyScalingRiskLevel CorrectiveOverallRisk { get; init; }
    public required AdvancedBodyScalingRiskLevel RetargetingOverallRisk { get; init; }
    public required AdvancedBodyScalingRiskLevel MotionWarpingOverallRisk { get; init; }
    public required AdvancedBodyScalingRiskLevel OverallRisk { get; init; }
    public required string Summary { get; init; }
    public required IReadOnlyList<string> RetargetingAdvisories { get; init; }
    public required IReadOnlyList<string> RetargetingHotChains { get; init; }
    public required IReadOnlyList<string> MotionWarpingAdvisories { get; init; }
    public required IReadOnlyList<string> MotionWarpingHotChains { get; init; }
    public required IReadOnlyList<string> FullBodyIkAdvisories { get; init; }
    public required IReadOnlyList<string> FullBodyIkHotChains { get; init; }
    public required IReadOnlyList<AdvancedBodyScalingPoseStressResult> Poses { get; init; }
    public required IReadOnlyList<AdvancedBodyScalingRegionStressResult> RegionSummary { get; init; }
}

internal static class AdvancedBodyScalingStressTestHarness
{
    private static readonly string[] NeckBones = { "j_kubi" };
    private static readonly string[] UpperSpineBones = { "j_sebo_c" };
    private static readonly string[] ChestBridgeBones = { "j_sebo_b", "j_sebo_c", "j_mune_l", "j_mune_r" };
    private static readonly string[] ClavicleBones = { "j_sako_l", "j_sako_r" };
    private static readonly string[] ShoulderRootBones = { "n_hkata_l", "n_hkata_r" };
    private static readonly string[] UpperArmBones = { "j_ude_a_l", "j_ude_a_r" };
    private static readonly string[] ForearmBones = { "j_ude_b_l", "j_ude_b_r" };
    private static readonly string[] HandBones = { "n_hte_l", "n_hte_r", "j_te_l", "j_te_r" };
    private static readonly string[] WaistBones = { "j_sebo_a", "j_kosi", "n_hara" };
    private static readonly string[] HipBones = { "j_kosi", "n_hara" };
    private static readonly string[] ThighBones = { "j_asi_a_l", "j_asi_a_r", "j_asi_b_l", "j_asi_b_r" };
    private static readonly string[] CalfBones = { "j_asi_c_l", "j_asi_c_r" };
    private static readonly string[] FootBones = { "j_asi_d_l", "j_asi_d_r" };

    private sealed record PoseDefinition(
        string Name,
        string Description,
        IReadOnlyDictionary<AdvancedBodyScalingCorrectiveRegion, float> RegionWeights);

    private sealed record RegionEvaluation(float Score, IReadOnlyList<string> Reasons, IReadOnlyList<string> Bones);

    private static readonly IReadOnlyList<PoseDefinition> PoseDefinitions = new[]
    {
        new PoseDefinition(
            "Arms raised",
            "Checks neck and shoulder continuity, clavicle/chest bridging, and elbow taper under overhead motion.",
            new Dictionary<AdvancedBodyScalingCorrectiveRegion, float>
            {
                [AdvancedBodyScalingCorrectiveRegion.NeckShoulder] = 0.95f,
                [AdvancedBodyScalingCorrectiveRegion.ClavicleUpperChest] = 0.95f,
                [AdvancedBodyScalingCorrectiveRegion.ShoulderUpperArm] = 1.00f,
                [AdvancedBodyScalingCorrectiveRegion.ElbowForearm] = 0.82f,
            }),
        new PoseDefinition(
            "Wide arm spread",
            "Checks detached-shoulder risk and upper-arm / forearm continuity when arms are pulled away from the torso.",
            new Dictionary<AdvancedBodyScalingCorrectiveRegion, float>
            {
                [AdvancedBodyScalingCorrectiveRegion.NeckShoulder] = 0.80f,
                [AdvancedBodyScalingCorrectiveRegion.ClavicleUpperChest] = 1.00f,
                [AdvancedBodyScalingCorrectiveRegion.ShoulderUpperArm] = 0.95f,
                [AdvancedBodyScalingCorrectiveRegion.ElbowForearm] = 0.78f,
            }),
        new PoseDefinition(
            "Torso twist",
            "Checks upper torso bridge quality and how well the waist/hip chain stays continuous under rotation.",
            new Dictionary<AdvancedBodyScalingCorrectiveRegion, float>
            {
                [AdvancedBodyScalingCorrectiveRegion.NeckShoulder] = 0.45f,
                [AdvancedBodyScalingCorrectiveRegion.ClavicleUpperChest] = 0.72f,
                [AdvancedBodyScalingCorrectiveRegion.WaistHips] = 1.00f,
                [AdvancedBodyScalingCorrectiveRegion.HipUpperThigh] = 0.62f,
            }),
        new PoseDefinition(
            "Forward bend / squat",
            "Checks waist compression plus hip/thigh and knee/calf continuity under heavy bend stress.",
            new Dictionary<AdvancedBodyScalingCorrectiveRegion, float>
            {
                [AdvancedBodyScalingCorrectiveRegion.WaistHips] = 0.90f,
                [AdvancedBodyScalingCorrectiveRegion.HipUpperThigh] = 1.00f,
                [AdvancedBodyScalingCorrectiveRegion.ThighKneeCalf] = 0.92f,
            }),
        new PoseDefinition(
            "Stride / extended leg",
            "Checks pelvis-to-thigh balance and thigh/knee/calf continuity in leg extension.",
            new Dictionary<AdvancedBodyScalingCorrectiveRegion, float>
            {
                [AdvancedBodyScalingCorrectiveRegion.WaistHips] = 0.65f,
                [AdvancedBodyScalingCorrectiveRegion.HipUpperThigh] = 1.00f,
                [AdvancedBodyScalingCorrectiveRegion.ThighKneeCalf] = 0.95f,
            }),
        new PoseDefinition(
            "Head tilt / neck stress",
            "Checks long-neck, clavicle bridge, and upper shoulder continuity when the head and upper torso are stressed.",
            new Dictionary<AdvancedBodyScalingCorrectiveRegion, float>
            {
                [AdvancedBodyScalingCorrectiveRegion.NeckShoulder] = 1.00f,
                [AdvancedBodyScalingCorrectiveRegion.ClavicleUpperChest] = 0.76f,
                [AdvancedBodyScalingCorrectiveRegion.ShoulderUpperArm] = 0.45f,
            }),
    };

    public static AdvancedBodyScalingStressTestReport Run(
        IReadOnlyDictionary<string, BoneTransform> transforms,
        AdvancedBodyScalingSettings settings,
        string sourceLabel)
    {
        var effectiveSettings = settings.CreateRuntimeResolvedSettings();
        var baseEvaluations = EvaluateBaseRegions(transforms, effectiveSettings);
        var poses = PoseDefinitions.Select(definition => BuildPoseResult(definition, transforms, baseEvaluations, effectiveSettings)).ToList();
        var regionSummary = BuildRegionSummary(poses);
        var baseOverallScore = ComputeOverallScore(poses, regionSummary, static pose => pose.BaseScore, static region => region.BaseScore);
        var correctiveOverallScore = ComputeOverallScore(poses, regionSummary, static pose => pose.CorrectiveOnlyScore, static region => region.CorrectiveOnlyScore);
        var retargetingOverallScore = ComputeOverallScore(poses, regionSummary, static pose => pose.RetargetingScore, static region => region.RetargetingScore);
        var motionWarpingOverallScore = ComputeOverallScore(poses, regionSummary, static pose => pose.MotionWarpingScore, static region => region.MotionWarpingScore);
        var overallScore = ComputeOverallScore(poses, regionSummary, static pose => pose.Score, static region => region.Score);
        var retargetAdvisories = AdvancedBodyScalingFullIkRetargetingSystem.GetTuningAdvisories(effectiveSettings).ToList();
        var motionAdvisories = AdvancedBodyScalingMotionWarpingSystem.GetTuningAdvisories(effectiveSettings).ToList();
        var ikAdvisories = AdvancedBodyScalingFullBodyIkSystem.GetTuningAdvisories(effectiveSettings).ToList();
        var retargetHotChains = regionSummary
            .SelectMany(region => region.RetargetingChains)
            .GroupBy(chain => chain, StringComparer.Ordinal)
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key, StringComparer.Ordinal)
            .Take(3)
            .Select(group => group.Key)
            .ToList();
        var motionHotChains = regionSummary
            .SelectMany(region => region.MotionWarpingChains)
            .GroupBy(chain => chain, StringComparer.Ordinal)
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key, StringComparer.Ordinal)
            .Take(3)
            .Select(group => group.Key)
            .ToList();
        var ikHotChains = regionSummary
            .SelectMany(region => region.FullBodyIkChains)
            .GroupBy(chain => chain, StringComparer.Ordinal)
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key, StringComparer.Ordinal)
            .Take(3)
            .Select(group => group.Key)
            .ToList();

        return new AdvancedBodyScalingStressTestReport
        {
            SourceLabel = sourceLabel,
            BaseOverallScore = baseOverallScore,
            CorrectiveOverallScore = correctiveOverallScore,
            RetargetingOverallScore = retargetingOverallScore,
            MotionWarpingOverallScore = motionWarpingOverallScore,
            OverallScore = overallScore,
            BaseOverallRisk = ToRiskLevel(baseOverallScore),
            CorrectiveOverallRisk = ToRiskLevel(correctiveOverallScore),
            RetargetingOverallRisk = ToRiskLevel(retargetingOverallScore),
            MotionWarpingOverallRisk = ToRiskLevel(motionWarpingOverallScore),
            OverallRisk = ToRiskLevel(overallScore),
            Summary = BuildSummary(baseOverallScore, correctiveOverallScore, retargetingOverallScore, motionWarpingOverallScore, overallScore, ToRiskLevel(overallScore), poses, regionSummary, retargetHotChains, retargetAdvisories, motionHotChains, motionAdvisories, ikHotChains, ikAdvisories),
            RetargetingAdvisories = retargetAdvisories,
            RetargetingHotChains = retargetHotChains,
            MotionWarpingAdvisories = motionAdvisories,
            MotionWarpingHotChains = motionHotChains,
            FullBodyIkAdvisories = ikAdvisories,
            FullBodyIkHotChains = ikHotChains,
            Poses = poses,
            RegionSummary = regionSummary,
        };
    }

    private static Dictionary<AdvancedBodyScalingCorrectiveRegion, RegionEvaluation> EvaluateBaseRegions(
        IReadOnlyDictionary<string, BoneTransform> transforms,
        AdvancedBodyScalingSettings settings)
        => new()
        {
            [AdvancedBodyScalingCorrectiveRegion.NeckShoulder] = EvaluateNeckShoulder(transforms, settings),
            [AdvancedBodyScalingCorrectiveRegion.ClavicleUpperChest] = EvaluateClavicleChest(transforms, settings),
            [AdvancedBodyScalingCorrectiveRegion.ShoulderUpperArm] = EvaluateShoulderUpperArm(transforms, settings),
            [AdvancedBodyScalingCorrectiveRegion.ElbowForearm] = EvaluateElbowForearm(transforms, settings),
            [AdvancedBodyScalingCorrectiveRegion.WaistHips] = EvaluateWaistHips(transforms, settings),
            [AdvancedBodyScalingCorrectiveRegion.HipUpperThigh] = EvaluateHipUpperThigh(transforms, settings),
            [AdvancedBodyScalingCorrectiveRegion.ThighKneeCalf] = EvaluateLegs(transforms, settings),
        };

    private static AdvancedBodyScalingPoseStressResult BuildPoseResult(
        PoseDefinition definition,
        IReadOnlyDictionary<string, BoneTransform> transforms,
        IReadOnlyDictionary<AdvancedBodyScalingCorrectiveRegion, RegionEvaluation> baseEvaluations,
        AdvancedBodyScalingSettings settings)
    {
        var estimates = AdvancedBodyScalingPoseCorrectiveSystem
            .EstimateStaticSupport(transforms, settings, definition.RegionWeights)
            .ToDictionary(entry => entry.Region, entry => entry);

        var regions = new List<AdvancedBodyScalingRegionStressResult>();
        foreach (var (region, weight) in definition.RegionWeights)
        {
            var evaluation = baseEvaluations[region];
            var baseScore = ApplyPoseWeight(region, evaluation.Score, weight, definition.Name, settings);
            var reasons = evaluation.Reasons.Count == 0
                ? new[] { "No strong instability heuristic fired for this region in this pose." }
                : evaluation.Reasons;

            estimates.TryGetValue(region, out var estimate);
            var correctiveIntensity = estimate?.Strength ?? 0f;
            var reductionFraction = estimate?.EstimatedRiskReduction ?? 0f;
            var correctiveReduction = ClampScore(baseScore * reductionFraction);
            var correctiveOnlyScore = ClampScore(baseScore - correctiveReduction);
            var correctiveSummary = estimate == null || correctiveIntensity <= 0.005f
                ? "No strong pose-space corrective response is expected for this region in this pose."
                : $"Estimated corrective activity {correctiveIntensity:0.00} via {estimate.DriverSummary}, trimming about {correctiveReduction} risk points.";
            var retargetEstimate = AdvancedBodyScalingFullIkRetargetingSystem.EstimateRegionRiskReduction(transforms, settings, region, weight);
            var retargetReduction = ClampScore(correctiveOnlyScore * retargetEstimate.EstimatedRiskReduction);
            var retargetingScore = ClampScore(correctiveOnlyScore - retargetReduction);
            var motionEstimate = AdvancedBodyScalingMotionWarpingSystem.EstimateRegionRiskReduction(transforms, settings, region, weight);
            var motionReduction = ClampScore(retargetingScore * motionEstimate.EstimatedRiskReduction);
            var motionWarpingScore = ClampScore(retargetingScore - motionReduction);
            var ikEstimate = AdvancedBodyScalingFullBodyIkSystem.EstimateRegionRiskReduction(transforms, settings, region, weight);
            var ikReduction = ClampScore(motionWarpingScore * ikEstimate.EstimatedRiskReduction);
            var finalScore = ClampScore(motionWarpingScore - ikReduction);

            regions.Add(new AdvancedBodyScalingRegionStressResult
            {
                Region = region,
                RegionName = AdvancedBodyScalingPoseCorrectiveSystem.GetRegionLabel(region),
                BaseScore = baseScore,
                CorrectiveOnlyScore = correctiveOnlyScore,
                RetargetingScore = retargetingScore,
                MotionWarpingScore = motionWarpingScore,
                Score = finalScore,
                CorrectiveReductionScore = correctiveReduction,
                RetargetingReductionScore = retargetReduction,
                MotionWarpingReductionScore = motionReduction,
                FullBodyIkReductionScore = ikReduction,
                CorrectiveIntensity = correctiveIntensity,
                RetargetingIntensity = retargetEstimate.Strength,
                MotionWarpingIntensity = motionEstimate.Strength,
                FullBodyIkIntensity = ikEstimate.Strength,
                BaseRiskLevel = ToRiskLevel(baseScore),
                CorrectiveOnlyRiskLevel = ToRiskLevel(correctiveOnlyScore),
                RetargetingRiskLevel = ToRiskLevel(retargetingScore),
                MotionWarpingRiskLevel = ToRiskLevel(motionWarpingScore),
                RiskLevel = ToRiskLevel(finalScore),
                Reasons = reasons,
                Bones = evaluation.Bones,
                RetargetingChains = retargetEstimate.ChainLabels,
                MotionWarpingChains = motionEstimate.ChainLabels,
                FullBodyIkChains = ikEstimate.ChainLabels,
                CorrectiveSummary = correctiveSummary,
                RetargetingSummary = retargetEstimate.Summary,
                MotionWarpingSummary = motionEstimate.Summary,
                FullBodyIkSummary = ikEstimate.Summary,
            });
        }

        var basePoseScore = ComputePoseScore(regions, static region => region.BaseScore);
        var correctivePoseScore = ComputePoseScore(regions, static region => region.CorrectiveOnlyScore);
        var retargetingPoseScore = ComputePoseScore(regions, static region => region.RetargetingScore);
        var motionWarpingPoseScore = ComputePoseScore(regions, static region => region.MotionWarpingScore);
        var finalPoseScore = ComputePoseScore(regions, static region => region.Score);
        return new AdvancedBodyScalingPoseStressResult
        {
            Name = definition.Name,
            Description = definition.Description,
            BaseScore = basePoseScore,
            CorrectiveOnlyScore = correctivePoseScore,
            RetargetingScore = retargetingPoseScore,
            MotionWarpingScore = motionWarpingPoseScore,
            Score = finalPoseScore,
            BaseRiskLevel = ToRiskLevel(basePoseScore),
            CorrectiveOnlyRiskLevel = ToRiskLevel(correctivePoseScore),
            RetargetingRiskLevel = ToRiskLevel(retargetingPoseScore),
            MotionWarpingRiskLevel = ToRiskLevel(motionWarpingPoseScore),
            RiskLevel = ToRiskLevel(finalPoseScore),
            Regions = regions.OrderByDescending(r => r.Score).ToList(),
        };
    }

    private static IReadOnlyList<AdvancedBodyScalingRegionStressResult> BuildRegionSummary(
        IReadOnlyList<AdvancedBodyScalingPoseStressResult> poses)
        => poses
            .SelectMany(pose => pose.Regions)
            .GroupBy(region => region.Region)
            .Select(group =>
            {
                var highestAfter = group.OrderByDescending(entry => entry.Score).First();
                var highestReduction = group.OrderByDescending(entry => entry.CorrectiveReductionScore).First();
                var highestRetargetReduction = group.OrderByDescending(entry => entry.RetargetingReductionScore).First();
                var highestMotionReduction = group.OrderByDescending(entry => entry.MotionWarpingReductionScore).First();
                var highestIkReduction = group.OrderByDescending(entry => entry.FullBodyIkReductionScore).First();
                var reasons = group
                    .SelectMany(entry => entry.Reasons)
                    .Distinct(StringComparer.Ordinal)
                    .Take(3)
                    .ToList();
                var baseScore = group.Max(entry => entry.BaseScore);
                var correctiveOnlyScore = group.Max(entry => entry.CorrectiveOnlyScore);
                var retargetingScore = group.Max(entry => entry.RetargetingScore);
                var motionWarpingScore = group.Max(entry => entry.MotionWarpingScore);
                var retargetChains = group
                    .SelectMany(entry => entry.RetargetingChains)
                    .Distinct(StringComparer.Ordinal)
                    .Take(3)
                    .ToList();
                var motionChains = group
                    .SelectMany(entry => entry.MotionWarpingChains)
                    .Distinct(StringComparer.Ordinal)
                    .Take(3)
                    .ToList();
                var ikChains = group
                    .SelectMany(entry => entry.FullBodyIkChains)
                    .Distinct(StringComparer.Ordinal)
                    .Take(3)
                    .ToList();

                return new AdvancedBodyScalingRegionStressResult
                {
                    Region = highestAfter.Region,
                    RegionName = highestAfter.RegionName,
                    BaseScore = baseScore,
                    CorrectiveOnlyScore = correctiveOnlyScore,
                    RetargetingScore = retargetingScore,
                    MotionWarpingScore = motionWarpingScore,
                    Score = highestAfter.Score,
                    CorrectiveReductionScore = group.Max(entry => entry.CorrectiveReductionScore),
                    RetargetingReductionScore = group.Max(entry => entry.RetargetingReductionScore),
                    MotionWarpingReductionScore = group.Max(entry => entry.MotionWarpingReductionScore),
                    FullBodyIkReductionScore = group.Max(entry => entry.FullBodyIkReductionScore),
                    CorrectiveIntensity = group.Max(entry => entry.CorrectiveIntensity),
                    RetargetingIntensity = group.Max(entry => entry.RetargetingIntensity),
                    MotionWarpingIntensity = group.Max(entry => entry.MotionWarpingIntensity),
                    FullBodyIkIntensity = group.Max(entry => entry.FullBodyIkIntensity),
                    BaseRiskLevel = ToRiskLevel(baseScore),
                    CorrectiveOnlyRiskLevel = ToRiskLevel(correctiveOnlyScore),
                    RetargetingRiskLevel = ToRiskLevel(retargetingScore),
                    MotionWarpingRiskLevel = ToRiskLevel(motionWarpingScore),
                    RiskLevel = highestAfter.RiskLevel,
                    Reasons = reasons,
                    Bones = highestAfter.Bones,
                    RetargetingChains = retargetChains,
                    MotionWarpingChains = motionChains,
                    FullBodyIkChains = ikChains,
                    CorrectiveSummary = highestReduction.CorrectiveSummary,
                    RetargetingSummary = highestRetargetReduction.RetargetingSummary,
                    MotionWarpingSummary = highestMotionReduction.MotionWarpingSummary,
                    FullBodyIkSummary = highestIkReduction.FullBodyIkSummary,
                };
            })
            .OrderByDescending(result => result.Score)
            .ToList();

    private static int ComputeOverallScore(
        IReadOnlyList<AdvancedBodyScalingPoseStressResult> poses,
        IReadOnlyList<AdvancedBodyScalingRegionStressResult> regionSummary,
        Func<AdvancedBodyScalingPoseStressResult, int> poseScoreSelector,
        Func<AdvancedBodyScalingRegionStressResult, int> regionScoreSelector)
    {
        if (poses.Count == 0)
            return 0;

        var poseMax = poses.Max(poseScoreSelector);
        var poseAverage = (int)MathF.Round((float)poses.Average(poseScoreSelector));
        var topRegions = regionSummary.Take(2).Select(regionScoreSelector).ToList();
        var regionPressure = topRegions.Count == 0 ? 0 : topRegions.Sum() / topRegions.Count;
        return ClampScore((poseMax * 0.45f) + (poseAverage * 0.35f) + (regionPressure * 0.20f));
    }

    private static int ComputePoseScore(
        IReadOnlyList<AdvancedBodyScalingRegionStressResult> regions,
        Func<AdvancedBodyScalingRegionStressResult, int> selector)
    {
        if (regions.Count == 0)
            return 0;

        var maxScore = regions.Max(selector);
        var averageScore = (int)MathF.Round((float)regions.Average(selector));
        return ClampScore((maxScore * 0.65f) + (averageScore * 0.35f));
    }

    private static string BuildSummary(
        int baseOverallScore,
        int correctiveOverallScore,
        int retargetingOverallScore,
        int motionWarpingOverallScore,
        int overallScore,
        AdvancedBodyScalingRiskLevel overallRisk,
        IReadOnlyList<AdvancedBodyScalingPoseStressResult> poses,
        IReadOnlyList<AdvancedBodyScalingRegionStressResult> regionSummary,
        IReadOnlyList<string> retargetHotChains,
        IReadOnlyList<string> retargetAdvisories,
        IReadOnlyList<string> motionHotChains,
        IReadOnlyList<string> motionAdvisories,
        IReadOnlyList<string> ikHotChains,
        IReadOnlyList<string> ikAdvisories)
    {
        var hottest = regionSummary.Take(2).Select(region => region.RegionName).ToList();
        var stressedPose = poses.OrderByDescending(pose => pose.Score).FirstOrDefault();
        var hotSpotText = hottest.Count == 0 ? "no clear hot spots" : string.Join(" and ", hottest);
        var poseText = stressedPose == null ? "No pose data" : stressedPose.Name;
        var correctiveReduction = Math.Max(0, baseOverallScore - correctiveOverallScore);
        var retargetReduction = Math.Max(0, correctiveOverallScore - retargetingOverallScore);
        var motionReduction = Math.Max(0, retargetingOverallScore - motionWarpingOverallScore);
        var ikReduction = Math.Max(0, motionWarpingOverallScore - overallScore);
        var correctiveText = correctiveReduction > 0
            ? $" Pose-space correctives trim about {correctiveReduction} overall risk points before the final IK pass."
            : " Pose-space correctives are either off or not strongly engaged for this setup.";
        var retargetText = retargetReduction > 0
            ? $" Full IK retargeting trims about {retargetReduction} more points after correctives, led by {(retargetHotChains.Count == 0 ? "the supported major chains" : string.Join(", ", retargetHotChains))}."
            : " Full IK retargeting is either off or not strongly engaged for this setup.";
        var motionText = motionReduction > 0
            ? $" Motion warping trims about {motionReduction} more locomotion points after retargeting, led by {(motionHotChains.Count == 0 ? "the supported major chains" : string.Join(", ", motionHotChains))}."
            : " Motion warping is either off, waiting for locomotion, or not strongly engaged for this setup.";
        var ikText = ikReduction > 0
            ? $" Full-body IK trims about {ikReduction} more points after motion warping, led by {(ikHotChains.Count == 0 ? "the supported major chains" : string.Join(", ", ikHotChains))}."
            : " Full-body IK is either off or not strongly engaged for this setup.";
        var advisoryText = retargetAdvisories.Concat(motionAdvisories).Concat(ikAdvisories).FirstOrDefault() is { Length: > 0 } advisory
            ? $" Advisory: {advisory}"
            : string.Empty;

        return overallRisk switch
        {
            AdvancedBodyScalingRiskLevel.High => $"High animation risk. {hotSpotText} look most fragile, with '{poseText}' being the most stress-prone test.{correctiveText}{retargetText}{motionText}{ikText}{advisoryText}",
            AdvancedBodyScalingRiskLevel.Moderate => $"Moderate animation risk. {hotSpotText} should be checked first, especially under '{poseText}'.{correctiveText}{retargetText}{motionText}{ikText}{advisoryText}",
            _ => $"Low animation risk. The current setup stays fairly stable across the built-in pose checks, with '{poseText}' being the most demanding test.{correctiveText}{retargetText}{motionText}{ikText}{advisoryText}",
        };
    }

    private static RegionEvaluation EvaluateNeckShoulder(
        IReadOnlyDictionary<string, BoneTransform> transforms,
        AdvancedBodyScalingSettings settings)
    {
        var reasons = new List<string>();
        var neckLength = AverageAxisScale(transforms, NeckBones, Axis.Y);
        var neckWidth = AverageAxisScale(transforms, NeckBones, Axis.X, Axis.Z);
        var upperSpine = AverageUniformScale(transforms, UpperSpineBones);
        var shoulderFrame = AverageUniformScale(transforms, ClavicleBones.Concat(ShoulderRootBones));
        var bridgeGap = MathF.Max(MathF.Abs(neckWidth - shoulderFrame), MathF.Abs(upperSpine - shoulderFrame));
        var score = 0f;

        if (neckLength > 1.08f)
        {
            score += (neckLength - 1.08f) * 130f;
            reasons.Add("Neck length remains high relative to the shoulder frame.");
        }

        if (neckLength < 0.82f)
        {
            score += (0.82f - neckLength) * 95f;
            reasons.Add("Neck length is compressed enough that head tilt could start to look buried.");
        }

        if (bridgeGap > 0.14f)
        {
            score += (bridgeGap - 0.14f) * 240f;
            reasons.Add("Upper spine, neck, and shoulder bridge scales diverge sharply.");
        }

        if (settings.NeckShoulderBlendStrength < 0.30f && bridgeGap > 0.10f)
        {
            score += 8f;
            reasons.Add("Low neck-to-shoulder blend leaves less smoothing across the transition.");
        }

        if (settings.ClavicleShoulderSmoothing < 0.25f && MathF.Abs(shoulderFrame - upperSpine) > 0.10f)
        {
            score += 8f;
            reasons.Add("Low clavicle smoothing leaves more chance of a detached-shoulder look.");
        }

        if (settings.NeckLengthCompensation <= 0.05f && neckLength > 1.10f)
        {
            score += 10f;
            reasons.Add("Low neck compensation leaves less head-tilt margin for a long-neck setup.");
        }

        if (settings.AnimationSafeModeEnabled)
            score *= 0.88f;

        return new RegionEvaluation(ClampScore(score), TrimReasons(reasons), NeckBones.Concat(UpperSpineBones).Concat(ClavicleBones).Concat(ShoulderRootBones).ToArray());
    }

    private static RegionEvaluation EvaluateClavicleChest(
        IReadOnlyDictionary<string, BoneTransform> transforms,
        AdvancedBodyScalingSettings settings)
    {
        var reasons = new List<string>();
        var clavicles = AverageUniformScale(transforms, ClavicleBones);
        var shoulders = AverageUniformScale(transforms, ShoulderRootBones);
        var chest = AverageUniformScale(transforms, ChestBridgeBones);
        var ratio = chest <= 0.0001f ? 1f : clavicles / chest;
        var score = 0f;

        if (ratio > 1.22f)
        {
            score += (ratio - 1.22f) * 115f;
            reasons.Add("Clavicle mass is running ahead of the upper chest and can separate in arm-heavy motion.");
        }
        else if (ratio < 0.82f)
        {
            score += (0.82f - ratio) * 115f;
            reasons.Add("Upper chest mass is outpacing the clavicles and can collapse the bridge line.");
        }

        var shoulderJump = MathF.Abs(shoulders - chest);
        if (shoulderJump > 0.14f)
        {
            score += (shoulderJump - 0.14f) * 180f;
            reasons.Add("Shoulder roots jump too far from the upper chest bridge.");
        }

        if (settings.GuardrailMode == AdvancedBodyScalingGuardrailMode.Off)
        {
            score += 6f;
            reasons.Add("Guardrails are off, so abrupt clavicle/chest ratios are less likely to be corrected automatically.");
        }

        if (settings.AnimationSafeModeEnabled)
            score *= 0.90f;

        return new RegionEvaluation(ClampScore(score), TrimReasons(reasons), ClavicleBones.Concat(ShoulderRootBones).Concat(ChestBridgeBones).ToArray());
    }

    private static RegionEvaluation EvaluateShoulderUpperArm(
        IReadOnlyDictionary<string, BoneTransform> transforms,
        AdvancedBodyScalingSettings settings)
    {
        var reasons = new List<string>();
        var shoulderFrame = AverageUniformScale(transforms, ClavicleBones.Concat(ShoulderRootBones));
        var upperArm = AverageUniformScale(transforms, UpperArmBones);
        var ratio = upperArm <= 0.0001f ? 1f : shoulderFrame / upperArm;
        var score = 0f;

        if (ratio > 1.24f)
        {
            score += (ratio - 1.24f) * 120f;
            reasons.Add("Shoulder frame mass is running ahead of the upper arm and can create a detached-shoulder look.");
        }
        else if (ratio < 0.84f)
        {
            score += (0.84f - ratio) * 120f;
            reasons.Add("Upper arm mass is outpacing the shoulder bridge and can make the join look abrupt.");
        }

        var leftRight = MathF.Abs(
            AverageUniformScale(transforms, new[] { "j_sako_l", "n_hkata_l", "j_ude_a_l" }) -
            AverageUniformScale(transforms, new[] { "j_sako_r", "n_hkata_r", "j_ude_a_r" }));
        if (leftRight > 0.16f)
        {
            score += (leftRight - 0.16f) * 115f;
            reasons.Add("Left-right shoulder/upper-arm asymmetry may become obvious in mirrored arm poses.");
        }

        if (settings.PoseValidationMode == AdvancedBodyScalingPoseValidationMode.Off)
        {
            score += 6f;
            reasons.Add("Pose-aware correction is off, so shoulder-to-arm continuity has less safety coverage.");
        }

        if (settings.AnimationSafeModeEnabled)
            score *= 0.90f;

        return new RegionEvaluation(ClampScore(score), TrimReasons(reasons), ClavicleBones.Concat(ShoulderRootBones).Concat(UpperArmBones).ToArray());
    }

    private static RegionEvaluation EvaluateElbowForearm(
        IReadOnlyDictionary<string, BoneTransform> transforms,
        AdvancedBodyScalingSettings settings)
    {
        var reasons = new List<string>();
        var upperArm = AverageUniformScale(transforms, UpperArmBones);
        var forearm = AverageUniformScale(transforms, ForearmBones);
        var hand = AverageUniformScale(transforms, HandBones);
        var ratio = forearm <= 0.0001f ? 1f : upperArm / forearm;
        var score = 0f;

        if (ratio > 1.30f)
        {
            score += (ratio - 1.30f) * 140f;
            reasons.Add("Upper arm to forearm taper is abrupt and can crease around the elbow.");
        }
        else if (ratio < 0.90f)
        {
            score += (0.90f - ratio) * 140f;
            reasons.Add("Forearm mass is outpacing the upper arm and can look unstable when bent.");
        }

        var wristJump = MathF.Abs(forearm - hand);
        if (wristJump > 0.18f)
        {
            score += (wristJump - 0.18f) * 150f;
            reasons.Add("Forearm to hand transition is sharp enough to read as a hard break in motion.");
        }

        var leftRight = MathF.Abs(AverageUniformScale(transforms, new[] { "j_ude_a_l", "j_ude_b_l" }) - AverageUniformScale(transforms, new[] { "j_ude_a_r", "j_ude_b_r" }));
        if (leftRight > 0.16f)
        {
            score += (leftRight - 0.16f) * 120f;
            reasons.Add("Arm scaling asymmetry may become obvious when both arms share a wide pose.");
        }

        if (settings.PoseValidationMode == AdvancedBodyScalingPoseValidationMode.Off)
        {
            score += 8f;
            reasons.Add("Pose-aware correction is off, so elbow taper issues have less safety coverage.");
        }

        if (settings.AnimationSafeModeEnabled)
            score *= 0.88f;

        return new RegionEvaluation(ClampScore(score), TrimReasons(reasons), UpperArmBones.Concat(ForearmBones).Concat(HandBones).ToArray());
    }

    private static RegionEvaluation EvaluateWaistHips(
        IReadOnlyDictionary<string, BoneTransform> transforms,
        AdvancedBodyScalingSettings settings)
    {
        var reasons = new List<string>();
        var waist = AverageUniformScale(transforms, WaistBones);
        var hips = AverageUniformScale(transforms, HipBones);
        var ratio = waist <= 0.0001f ? 1f : hips / waist;
        var score = 0f;

        if (ratio > 1.42f)
        {
            score += (ratio - 1.42f) * 125f;
            reasons.Add("Hip mass is running far ahead of the waist and can fold awkwardly in twists or bends.");
        }
        else if (ratio < 0.92f)
        {
            score += (0.92f - ratio) * 120f;
            reasons.Add("Waist support is outpacing the hips and can make the lower torso bridge look abrupt.");
        }

        if (settings.GuardrailMode == AdvancedBodyScalingGuardrailMode.Off)
        {
            score += 8f;
            reasons.Add("Guardrails are off, so waist-to-hip proportion drift is less likely to be corrected automatically.");
        }

        if (settings.AnimationSafeModeEnabled)
            score *= 0.90f;

        return new RegionEvaluation(ClampScore(score), TrimReasons(reasons), WaistBones.Concat(HipBones).ToArray());
    }

    private static RegionEvaluation EvaluateHipUpperThigh(
        IReadOnlyDictionary<string, BoneTransform> transforms,
        AdvancedBodyScalingSettings settings)
    {
        var reasons = new List<string>();
        var hips = AverageUniformScale(transforms, HipBones);
        var thighs = AverageUniformScale(transforms, ThighBones);
        var ratio = thighs <= 0.0001f ? 1f : hips / thighs;
        var score = 0f;

        if (ratio > 1.34f)
        {
            score += (ratio - 1.34f) * 130f;
            reasons.Add("Pelvis support is outpacing the upper thigh and can create a hard mass jump at the leg root.");
        }
        else if (ratio < 0.88f)
        {
            score += (0.88f - ratio) * 125f;
            reasons.Add("Upper thigh mass is running ahead of the hip anchor and can destabilize the bridge in stride poses.");
        }

        var leftRight = MathF.Abs(
            AverageUniformScale(transforms, new[] { "j_kosi", "j_asi_a_l", "j_asi_b_l" }) -
            AverageUniformScale(transforms, new[] { "j_kosi", "j_asi_a_r", "j_asi_b_r" }));
        if (leftRight > 0.18f)
        {
            score += (leftRight - 0.18f) * 110f;
            reasons.Add("Hip-to-thigh asymmetry may stand out when both legs share a strong stride or squat.");
        }

        if (settings.PoseValidationMode == AdvancedBodyScalingPoseValidationMode.Off)
        {
            score += 7f;
            reasons.Add("Pose-aware correction is off, so hip-to-thigh continuity has less safety coverage.");
        }

        if (settings.AnimationSafeModeEnabled)
            score *= 0.90f;

        return new RegionEvaluation(ClampScore(score), TrimReasons(reasons), HipBones.Concat(ThighBones).ToArray());
    }

    private static RegionEvaluation EvaluateLegs(
        IReadOnlyDictionary<string, BoneTransform> transforms,
        AdvancedBodyScalingSettings settings)
    {
        var reasons = new List<string>();
        var thighs = AverageUniformScale(transforms, ThighBones);
        var calves = AverageUniformScale(transforms, CalfBones);
        var feet = AverageUniformScale(transforms, FootBones);
        var ratio = calves <= 0.0001f ? 1f : thighs / calves;
        var score = 0f;

        if (ratio > 1.40f)
        {
            score += (ratio - 1.40f) * 130f;
            reasons.Add("Thigh-to-calf taper is abrupt and may crease sharply around the knee.");
        }
        else if (ratio < 1.00f)
        {
            score += (1.00f - ratio) * 130f;
            reasons.Add("Calf mass is outpacing the thigh and may look unstable when the leg extends.");
        }

        var ankleJump = MathF.Abs(calves - feet);
        if (ankleJump > 0.18f)
        {
            score += (ankleJump - 0.18f) * 145f;
            reasons.Add("Calf-to-foot transition is sharp enough to read as a hard break during stride motion.");
        }

        var leftRight = MathF.Abs(AverageUniformScale(transforms, new[] { "j_asi_a_l", "j_asi_c_l" }) - AverageUniformScale(transforms, new[] { "j_asi_a_r", "j_asi_c_r" }));
        if (leftRight > 0.16f)
        {
            score += (leftRight - 0.16f) * 110f;
            reasons.Add("Leg scaling asymmetry may stand out in mirrored stance or stride poses.");
        }

        if (settings.PoseValidationMode == AdvancedBodyScalingPoseValidationMode.Off)
        {
            score += 8f;
            reasons.Add("Pose-aware correction is off, so knee and calf transitions have less safety coverage.");
        }

        if (settings.AnimationSafeModeEnabled)
            score *= 0.88f;

        return new RegionEvaluation(ClampScore(score), TrimReasons(reasons), ThighBones.Concat(CalfBones).Concat(FootBones).ToArray());
    }

    private static int ApplyPoseWeight(
        AdvancedBodyScalingCorrectiveRegion region,
        float baseScore,
        float weight,
        string poseName,
        AdvancedBodyScalingSettings settings)
    {
        var score = baseScore * weight;

        if (poseName is "Arms raised" or "Wide arm spread")
        {
            if (region == AdvancedBodyScalingCorrectiveRegion.ShoulderUpperArm && settings.PoseValidationMode == AdvancedBodyScalingPoseValidationMode.Off)
                score += 6f;

            if (region == AdvancedBodyScalingCorrectiveRegion.NeckShoulder && settings.NeckShoulderBlendStrength < 0.35f)
                score += 5f;
        }

        if (poseName is "Torso twist" or "Forward bend / squat")
        {
            if (region == AdvancedBodyScalingCorrectiveRegion.WaistHips && settings.GuardrailMode == AdvancedBodyScalingGuardrailMode.Off)
                score += 6f;
        }

        if (poseName is "Stride / extended leg" or "Forward bend / squat")
        {
            if ((region == AdvancedBodyScalingCorrectiveRegion.HipUpperThigh || region == AdvancedBodyScalingCorrectiveRegion.ThighKneeCalf) &&
                settings.PoseValidationMode == AdvancedBodyScalingPoseValidationMode.Off)
            {
                score += 6f;
            }
        }

        if (poseName == "Head tilt / neck stress" && region == AdvancedBodyScalingCorrectiveRegion.NeckShoulder)
        {
            if (settings.NeckLengthCompensation <= 0.05f)
                score += 8f;

            if (settings.ClavicleShoulderSmoothing < 0.30f)
                score += 5f;
        }

        return ClampScore(score);
    }

    private static int ClampScore(float value)
        => (int)Math.Clamp(MathF.Round(value), 0f, 100f);

    private static AdvancedBodyScalingRiskLevel ToRiskLevel(int score)
        => score switch
        {
            >= 67 => AdvancedBodyScalingRiskLevel.High,
            >= 34 => AdvancedBodyScalingRiskLevel.Moderate,
            _ => AdvancedBodyScalingRiskLevel.Low,
        };

    private static IReadOnlyList<string> TrimReasons(IReadOnlyList<string> reasons)
        => reasons
            .Where(reason => !string.IsNullOrWhiteSpace(reason))
            .Distinct(StringComparer.Ordinal)
            .Take(3)
            .ToList();

    private static float AverageUniformScale(IReadOnlyDictionary<string, BoneTransform> transforms, IEnumerable<string> bones)
    {
        var values = bones
            .Where(bone => transforms.TryGetValue(bone, out _))
            .Select(bone => AdvancedBodyScalingPipeline.GetUniformScale(transforms[bone].Scaling))
            .ToList();

        return values.Count == 0 ? 1f : values.Average();
    }

    private static float AverageAxisScale(IReadOnlyDictionary<string, BoneTransform> transforms, IEnumerable<string> bones, params Axis[] axes)
    {
        var values = new List<float>();
        foreach (var bone in bones)
        {
            if (!transforms.TryGetValue(bone, out var transform))
                continue;

            foreach (var axis in axes)
                values.Add(GetAxisScale(transform, axis));
        }

        return values.Count == 0 ? 1f : values.Average();
    }

    private static float GetAxisScale(BoneTransform transform, Axis axis)
        => axis switch
        {
            Axis.X => MathF.Abs(transform.Scaling.X),
            Axis.Y => MathF.Abs(transform.Scaling.Y),
            Axis.Z => MathF.Abs(transform.Scaling.Z),
            _ => 1f,
        };

    private enum Axis
    {
        X,
        Y,
        Z,
    }

    // TODO Phase 4: augment these heuristics with supported mesh/skin-weight-derived bone importance data when available.
    // TODO Phase 5: add optional lightweight collision-risk warnings (arm/torso, thigh/thigh, neck/shoulder massing) without runtime auto-fixes.
}
