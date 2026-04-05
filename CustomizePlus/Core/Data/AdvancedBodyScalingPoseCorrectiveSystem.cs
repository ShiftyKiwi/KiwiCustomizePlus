// Copyright (c) Customize+.
// Licensed under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using CustomizePlus.Armatures.Data;
using CustomizePlus.Core.Extensions;
using FFXIVClientStructs.FFXIV.Client.Graphics.Scene;

namespace CustomizePlus.Core.Data;

internal static unsafe class AdvancedBodyScalingPoseCorrectiveSystem
{
    private const int RequiredSampleCountPerRegion = 32;
    private const int MaxShortlistedInfluenceSamples = 8;
    private const int MinShortlistedInfluenceSamples = 5;
    private const float MinimumShortlistedRawWeight = 0.012f;

    private static readonly string[] NeckBones = { "j_kubi" };
    private static readonly string[] UpperSpineBones = { "j_sebo_c" };
    private static readonly string[] ChestBridgeBones = { "j_sebo_b", "j_sebo_c", "j_mune_l", "j_mune_r" };
    private static readonly string[] ClavicleBones = { "j_sako_l", "j_sako_r" };
    private static readonly string[] ShoulderRootBones = { "n_hkata_l", "n_hkata_r" };
    private static readonly string[] UpperArmBones = { "j_ude_a_l", "j_ude_a_r" };
    private static readonly string[] ForearmBones = { "j_ude_b_l", "j_ude_b_r" };
    private static readonly string[] WristBones = { "n_hte_l", "n_hte_r", "j_te_l", "j_te_r" };
    private static readonly string[] WaistBones = { "j_sebo_a", "j_kosi", "n_hara" };
    private static readonly string[] HipAnchorBones = { "j_kosi", "n_hara" };
    private static readonly string[] ThighBones = { "j_asi_a_l", "j_asi_a_r", "j_asi_b_l", "j_asi_b_r" };
    private static readonly string[] CalfBones = { "j_asi_c_l", "j_asi_c_r" };
    private static readonly string[] FootBones = { "j_asi_d_l", "j_asi_d_r" };

    private readonly record struct DriverCondition(AdvancedBodyScalingCorrectiveDriverType Type, float Weight);
    private readonly record struct DriverSample(AdvancedBodyScalingCorrectiveDriverType Type, float Strength);
    private readonly record struct CorrectiveSignals(float Discontinuity, float ContinuityStress, float TaperStress);
    private readonly record struct PoseSampleOutput(
        float Activation,
        float GroupABlend,
        float GroupBBlend,
        float BridgeBlend,
        float TargetBias,
        float TaperBlend,
        float AxisBias);

    private sealed record PoseSample(
        string Name,
        IReadOnlyList<float> Key,
        PoseSampleOutput Output,
        float Radius,
        string Summary,
        bool Enabled = true,
        float Weight = 1f);

    private sealed record CorrectiveDefinition(
        AdvancedBodyScalingCorrectiveRegion Region,
        string Label,
        string Description,
        float DiscontinuityStart,
        float DiscontinuityFull,
        IReadOnlyList<AdvancedBodyRegion> RelatedRegions,
        IReadOnlyList<string> GroupA,
        IReadOnlyList<string> GroupB,
        IReadOnlyList<string> BridgeBones,
        IReadOnlyList<DriverCondition> Drivers,
        IReadOnlyList<PoseSample> Samples);

    private enum AdvancedBodyScalingCorrectiveDriverType
    {
        ArmElevation = 0,
        ShoulderSpread = 1,
        ClavicleTension = 2,
        NeckTilt = 3,
        NeckTwist = 4,
        HipFlexion = 5,
        KneeStress = 6,
        TorsoTwist = 7,
        ForwardBend = 8,
        TaperStress = 9,
        CrossRegionContinuity = 10,
    }

    private readonly record struct SampleInfluence(
        PoseSample Sample,
        float Weight,
        float RawWeight,
        float Distance);

    private sealed class RegionSolveResult
    {
        public required float DriverStrength { get; init; }
        public required float RawActivation { get; init; }
        public required PoseSampleOutput Output { get; init; }
        public required IReadOnlyList<SampleInfluence> Influences { get; init; }
        public required int TotalSampleCount { get; init; }
        public required int InfluenceSampleCount { get; init; }
        public required bool ShortlistApplied { get; init; }
        public required bool BroadInterpolation { get; init; }
        public required bool SafetyLimited { get; init; }
        public required string Summary { get; init; }
    }

    private sealed class CorrectionApplicationMetrics
    {
        public bool AnyApplied { get; set; }
        public bool LocksOrPinsLimited { get; set; }
        public bool Clamped { get; set; }

        public void Merge(CorrectionApplicationMetrics other)
        {
            AnyApplied |= other.AnyApplied;
            LocksOrPinsLimited |= other.LocksOrPinsLimited;
            Clamped |= other.Clamped;
        }
    }

    private static readonly CorrectiveDefinition[] Definitions =
    {
        new(
            AdvancedBodyScalingCorrectiveRegion.NeckShoulder,
            "Neck / Shoulder",
            "Smooths the neck-to-shoulder bridge with RBF pose interpolation so raised, tilted, and twisted upper-body poses transition more naturally.",
            0.08f,
            0.28f,
            new[] { AdvancedBodyRegion.NeckShoulder, AdvancedBodyRegion.Spine },
            NeckBones,
            ClavicleBones.Concat(ShoulderRootBones).ToArray(),
            UpperSpineBones,
            new[]
            {
                new DriverCondition(AdvancedBodyScalingCorrectiveDriverType.ArmElevation, 0.28f),
                new DriverCondition(AdvancedBodyScalingCorrectiveDriverType.NeckTilt, 0.18f),
                new DriverCondition(AdvancedBodyScalingCorrectiveDriverType.NeckTwist, 0.14f),
                new DriverCondition(AdvancedBodyScalingCorrectiveDriverType.TorsoTwist, 0.10f),
                new DriverCondition(AdvancedBodyScalingCorrectiveDriverType.CrossRegionContinuity, 0.30f),
            },
            BuildNeckShoulderSamples()),
        new(
            AdvancedBodyScalingCorrectiveRegion.ClavicleUpperChest,
            "Clavicle / Upper Chest",
            "Uses sampled shoulder/chest poses to soften harsh clavicle and underarm bridges with smoother multi-driver interpolation.",
            0.07f,
            0.25f,
            new[] { AdvancedBodyRegion.NeckShoulder, AdvancedBodyRegion.Chest },
            ClavicleBones.Concat(ShoulderRootBones).ToArray(),
            ChestBridgeBones,
            UpperSpineBones,
            new[]
            {
                new DriverCondition(AdvancedBodyScalingCorrectiveDriverType.ShoulderSpread, 0.24f),
                new DriverCondition(AdvancedBodyScalingCorrectiveDriverType.ClavicleTension, 0.24f),
                new DriverCondition(AdvancedBodyScalingCorrectiveDriverType.TorsoTwist, 0.14f),
                new DriverCondition(AdvancedBodyScalingCorrectiveDriverType.CrossRegionContinuity, 0.24f),
                new DriverCondition(AdvancedBodyScalingCorrectiveDriverType.TaperStress, 0.14f),
            },
            BuildClavicleUpperChestSamples()),
        new(
            AdvancedBodyScalingCorrectiveRegion.ShoulderUpperArm,
            "Shoulder / Upper Arm",
            "Interpolates between stored shoulder and arm stress poses so the upper-arm bridge reads less binary under spread and overhead motion.",
            0.07f,
            0.24f,
            new[] { AdvancedBodyRegion.NeckShoulder, AdvancedBodyRegion.Arms },
            ClavicleBones.Concat(ShoulderRootBones).ToArray(),
            UpperArmBones,
            Array.Empty<string>(),
            new[]
            {
                new DriverCondition(AdvancedBodyScalingCorrectiveDriverType.ArmElevation, 0.28f),
                new DriverCondition(AdvancedBodyScalingCorrectiveDriverType.ShoulderSpread, 0.22f),
                new DriverCondition(AdvancedBodyScalingCorrectiveDriverType.ClavicleTension, 0.12f),
                new DriverCondition(AdvancedBodyScalingCorrectiveDriverType.CrossRegionContinuity, 0.22f),
                new DriverCondition(AdvancedBodyScalingCorrectiveDriverType.TaperStress, 0.16f),
            },
            BuildShoulderUpperArmSamples()),
        new(
            AdvancedBodyScalingCorrectiveRegion.ElbowForearm,
            "Elbow / Forearm",
            "Uses sampled elbow and taper stress poses to soften the upper-arm to forearm transition without changing the overall arm style.",
            0.07f,
            0.23f,
            new[] { AdvancedBodyRegion.Arms },
            UpperArmBones,
            ForearmBones,
            WristBones,
            new[]
            {
                new DriverCondition(AdvancedBodyScalingCorrectiveDriverType.ArmElevation, 0.22f),
                new DriverCondition(AdvancedBodyScalingCorrectiveDriverType.TaperStress, 0.32f),
                new DriverCondition(AdvancedBodyScalingCorrectiveDriverType.CrossRegionContinuity, 0.26f),
                new DriverCondition(AdvancedBodyScalingCorrectiveDriverType.ShoulderSpread, 0.10f),
                new DriverCondition(AdvancedBodyScalingCorrectiveDriverType.KneeStress, 0.10f),
            },
            BuildElbowForearmSamples()),
        new(
            AdvancedBodyScalingCorrectiveRegion.WaistHips,
            "Waist / Hips",
            "Interpolates stored lower-torso poses so forward bends and twists produce smoother waist-to-hip continuity.",
            0.07f,
            0.24f,
            new[] { AdvancedBodyRegion.Spine, AdvancedBodyRegion.Pelvis },
            WaistBones,
            HipAnchorBones,
            Array.Empty<string>(),
            new[]
            {
                new DriverCondition(AdvancedBodyScalingCorrectiveDriverType.TorsoTwist, 0.22f),
                new DriverCondition(AdvancedBodyScalingCorrectiveDriverType.ForwardBend, 0.22f),
                new DriverCondition(AdvancedBodyScalingCorrectiveDriverType.CrossRegionContinuity, 0.32f),
                new DriverCondition(AdvancedBodyScalingCorrectiveDriverType.TaperStress, 0.24f),
            },
            BuildWaistHipsSamples()),
        new(
            AdvancedBodyScalingCorrectiveRegion.HipUpperThigh,
            "Hip / Upper Thigh",
            "Uses sampled stride and fold poses to make pelvis-to-thigh transitions blend more smoothly under hip flexion and body load.",
            0.07f,
            0.24f,
            new[] { AdvancedBodyRegion.Pelvis, AdvancedBodyRegion.Legs },
            HipAnchorBones,
            ThighBones,
            Array.Empty<string>(),
            new[]
            {
                new DriverCondition(AdvancedBodyScalingCorrectiveDriverType.HipFlexion, 0.28f),
                new DriverCondition(AdvancedBodyScalingCorrectiveDriverType.ForwardBend, 0.14f),
                new DriverCondition(AdvancedBodyScalingCorrectiveDriverType.TorsoTwist, 0.10f),
                new DriverCondition(AdvancedBodyScalingCorrectiveDriverType.CrossRegionContinuity, 0.24f),
                new DriverCondition(AdvancedBodyScalingCorrectiveDriverType.TaperStress, 0.24f),
            },
            BuildHipUpperThighSamples()),
        new(
            AdvancedBodyScalingCorrectiveRegion.ThighKneeCalf,
            "Thigh / Knee / Calf",
            "Interpolates stored knee and lower-leg stress poses so taper and bend transitions remain smoother under stride and crouch motion.",
            0.07f,
            0.24f,
            new[] { AdvancedBodyRegion.Legs },
            ThighBones,
            CalfBones,
            FootBones,
            new[]
            {
                new DriverCondition(AdvancedBodyScalingCorrectiveDriverType.HipFlexion, 0.16f),
                new DriverCondition(AdvancedBodyScalingCorrectiveDriverType.KneeStress, 0.18f),
                new DriverCondition(AdvancedBodyScalingCorrectiveDriverType.TaperStress, 0.34f),
                new DriverCondition(AdvancedBodyScalingCorrectiveDriverType.CrossRegionContinuity, 0.24f),
                new DriverCondition(AdvancedBodyScalingCorrectiveDriverType.ForwardBend, 0.08f),
            },
            BuildThighKneeCalfSamples()),
    };

    private static readonly IReadOnlyDictionary<AdvancedBodyScalingCorrectiveRegion, CorrectiveDefinition> DefinitionMap =
        Definitions.ToDictionary(definition => definition.Region);

    public static IReadOnlyList<AdvancedBodyScalingCorrectiveRegion> GetOrderedRegions()
        => Definitions.Select(definition => definition.Region).ToArray();

    public static int GetRegionSampleCount(AdvancedBodyScalingCorrectiveRegion region)
        => TryGetDefinition(region)?.Samples.Count ?? 0;

    public static string GetRegionLabel(AdvancedBodyScalingCorrectiveRegion region)
        => TryGetDefinition(region)?.Label ?? region.ToString();

    public static string GetRegionDescription(AdvancedBodyScalingCorrectiveRegion region)
        => TryGetDefinition(region)?.Description ?? string.Empty;

    public static AdvancedBodyScalingCorrectivePath DetectSupportedPath()
        => HasSupportedMorphPath() ? AdvancedBodyScalingCorrectivePath.SupportedMorph : AdvancedBodyScalingCorrectivePath.TransformFallback;

    public static string GetPathDescription(AdvancedBodyScalingCorrectivePath path)
        => path switch
        {
            AdvancedBodyScalingCorrectivePath.SupportedMorph => "Using an existing supported corrective morph path.",
            _ => "No supported corrective morph path was found in the current plugin/runtime, so Customize+ is using an RBF-driven transform corrective path on supported bones only.",
        };

    public static IReadOnlyList<string> GetTuningAdvisories(AdvancedBodyScalingSettings settings)
    {
        var advisories = new List<string>();
        if (!settings.Enabled || settings.Mode == AdvancedBodyScalingMode.Manual || !settings.PoseCorrectives.Enabled)
            return advisories;

        var pose = settings.PoseCorrectives;
        if (pose.Strength > AdvancedBodyScalingPoseCorrectiveTuning.RecommendedGlobalStrengthMax)
            advisories.Add("Global RBF corrective strength exceeds the recommended range and may make pose corrections read too strongly.");
        if (pose.PoseMapSharpness > AdvancedBodyScalingPoseCorrectiveTuning.RecommendedPoseMapSharpnessMax)
            advisories.Add("Pose-map sharpness exceeds the recommended range and may make corrective interpolation too peaky between nearby poses.");
        if (pose.Damping < AdvancedBodyScalingPoseCorrectiveTuning.RecommendedDampingMin)
            advisories.Add("Pose-space damping is below the recommended range; small pose changes may look noisier or more abrupt.");
        if (pose.MaxCorrectionClamp > AdvancedBodyScalingPoseCorrectiveTuning.RecommendedMaxCorrectionClampMax)
            advisories.Add("Global max corrective clamp exceeds the recommended range and may allow visibly artificial transform corrections.");

        foreach (var region in GetOrderedRegions())
        {
            var regionSettings = pose.GetRegionSettings(region);
            if (!regionSettings.Enabled)
                continue;

            if (regionSettings.Strength > AdvancedBodyScalingPoseCorrectiveTuning.RecommendedRegionStrengthMax)
                advisories.Add($"{GetRegionLabel(region)} corrective strength exceeds the recommended range and may blend too aggressively.");
            if (regionSettings.Falloff < AdvancedBodyScalingPoseCorrectiveTuning.RecommendedRegionFalloffMin)
                advisories.Add($"{GetRegionLabel(region)} sample falloff is very narrow and may make interpolation feel too binary.");
        }

        return advisories;
    }

    public static void Evaluate(
        Armature armature,
        CharacterBase* cBase,
        AdvancedBodyScalingSettings settings,
        bool profileOverridesActive,
        Dictionary<AdvancedBodyScalingCorrectiveRegion, float> activationState,
        Dictionary<string, Vector3> scaleMultipliers,
        AdvancedBodyScalingPoseCorrectiveDebugState debugState)
    {
        var path = DetectSupportedPath();
        var poseSettings = settings.PoseCorrectives;
        debugState.Reset(
            path,
            GetPathDescription(path),
            profileOverridesActive,
            poseSettings.Enabled,
            poseSettings.Strength,
            poseSettings.PoseMapSharpness,
            poseSettings.Damping,
            poseSettings.MaxCorrectionClamp);

        scaleMultipliers.Clear();

        if (cBase == null || !settings.Enabled || settings.Mode == AdvancedBodyScalingMode.Manual)
        {
            activationState.Clear();
            debugState.FinalizeState(false, "RBF pose-space correctives are inactive.");
            return;
        }

        if (!poseSettings.Enabled || poseSettings.Strength <= 0f)
        {
            activationState.Clear();
            debugState.FinalizeState(false, "RBF pose-space correctives are disabled.");
            return;
        }

        var advisories = GetTuningAdvisories(settings);
        var previousState = activationState.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
        activationState.Clear();

        var anyActive = false;
        var anySafetyLimited = false;
        var anyLocksLimited = false;

        foreach (var definition in Definitions)
        {
            var regionSettings = poseSettings.GetRegionSettings(definition.Region);
            if (!regionSettings.Enabled || regionSettings.Strength <= 0f || !HasUsableScaleData(armature.GetAppliedBoneTransform, definition))
                continue;

            var signals = BuildSignals(armature.GetAppliedBoneTransform, definition);
            var driverSamples = BuildLiveDriverSamples(armature, cBase, definition, signals);
            var driverVector = driverSamples.Select(sample => sample.Strength).ToArray();
            var driverStrength = WeightedAverage(driverSamples, definition.Drivers);
            var rbf = SolveRbf(definition, poseSettings, regionSettings, driverVector);
            var previousActivation = previousState.TryGetValue(definition.Region, out var cached) ? cached : 0f;
            var activation = ComputeActivation(rbf.RawActivation, previousActivation, poseSettings, regionSettings);
            if (activation > 0.001f)
                activationState[definition.Region] = activation;

            var tuningFactor = GetRegionTuningFactor(settings, definition.RelatedRegions);
            var correctionStrength = ComputeCorrectionStrength(settings, poseSettings, regionSettings, activation, tuningFactor);
            if (correctionStrength <= 0.005f)
                continue;

            var metrics = ApplyDefinitionCorrection(scaleMultipliers, armature, definition, signals, poseSettings, regionSettings, correctionStrength, rbf.Output);
            ApplySpecialBias(scaleMultipliers, armature, definition.Region, poseSettings, regionSettings, correctionStrength, rbf.Output, metrics);

            anyActive |= metrics.AnyApplied;
            anySafetyLimited |= rbf.SafetyLimited || metrics.Clamped;
            anyLocksLimited |= metrics.LocksOrPinsLimited;

            debugState.ActiveRegions.Add(BuildDebugState(definition, driverStrength, signals, activation, correctionStrength, driverSamples, rbf, metrics, regionSettings, poseSettings));
        }

        debugState.FinalizeState(anyActive, BuildOverallSummary(debugState.ActiveRegions, anySafetyLimited, anyLocksLimited), advisories);
    }

    public static IReadOnlyList<AdvancedBodyScalingCorrectiveDebugRegionState> EstimateStaticSupport(
        IReadOnlyDictionary<string, BoneTransform> transforms,
        AdvancedBodyScalingSettings settings,
        IReadOnlyDictionary<AdvancedBodyScalingCorrectiveRegion, float>? poseWeights = null)
    {
        var estimates = new List<AdvancedBodyScalingCorrectiveDebugRegionState>();
        if (!settings.Enabled || settings.Mode == AdvancedBodyScalingMode.Manual || !settings.PoseCorrectives.Enabled || settings.PoseCorrectives.Strength <= 0f)
            return estimates;

        BoneTransform? Resolver(string boneName)
            => transforms.TryGetValue(boneName, out var transform) ? transform : null;

        foreach (var definition in Definitions)
        {
            var regionSettings = settings.PoseCorrectives.GetRegionSettings(definition.Region);
            if (!regionSettings.Enabled || regionSettings.Strength <= 0f || !HasUsableScaleData(Resolver, definition))
                continue;

            var signals = BuildSignals(Resolver, definition);
            var staticPoseWeight = poseWeights != null && poseWeights.TryGetValue(definition.Region, out var weight)
                ? Math.Clamp(weight, 0f, 1f)
                : GetPassivePoseWeight(signals);
            var driverSamples = BuildStaticDriverSamples(definition, staticPoseWeight, signals);
            var driverVector = driverSamples.Select(sample => sample.Strength).ToArray();
            var driverStrength = WeightedAverage(driverSamples, definition.Drivers);
            var rbf = SolveRbf(definition, settings.PoseCorrectives, regionSettings, driverVector);
            var activation = ComputeActivation(rbf.RawActivation, 0f, settings.PoseCorrectives, regionSettings, useDeadzone: false);
            var tuningFactor = GetRegionTuningFactor(settings, definition.RelatedRegions);
            var correctionStrength = ComputeCorrectionStrength(settings, settings.PoseCorrectives, regionSettings, activation, tuningFactor);
            if (correctionStrength <= 0.005f)
                continue;

            estimates.Add(BuildDebugState(definition, driverStrength, signals, activation, correctionStrength, driverSamples, rbf, new CorrectionApplicationMetrics(), regionSettings, settings.PoseCorrectives));
        }

        return estimates
            .OrderByDescending(entry => entry.Strength)
            .ThenBy(entry => entry.Label, StringComparer.Ordinal)
            .ToList();
    }

    private static bool HasSupportedMorphPath()
        => false;

    private static IReadOnlyList<DriverSample> BuildLiveDriverSamples(
        Armature armature,
        CharacterBase* cBase,
        CorrectiveDefinition definition,
        CorrectiveSignals signals)
    {
        var samples = new DriverSample[definition.Drivers.Count];
        for (var i = 0; i < definition.Drivers.Count; ++i)
        {
            var driver = definition.Drivers[i];
            samples[i] = new DriverSample(driver.Type, EvaluateLiveDriver(armature, cBase, driver.Type, signals));
        }

        return samples;
    }

    private static IReadOnlyList<DriverSample> BuildStaticDriverSamples(
        CorrectiveDefinition definition,
        float poseWeight,
        CorrectiveSignals signals)
    {
        var samples = new DriverSample[definition.Drivers.Count];
        for (var i = 0; i < definition.Drivers.Count; ++i)
        {
            var driver = definition.Drivers[i];
            samples[i] = new DriverSample(driver.Type, EvaluateStaticDriver(driver.Type, poseWeight, signals));
        }

        return samples;
    }

    private static RegionSolveResult SolveRbf(
        CorrectiveDefinition definition,
        AdvancedBodyScalingPoseCorrectiveSettings settings,
        AdvancedBodyScalingCorrectiveRegionSettings regionSettings,
        IReadOnlyList<float> driverVector)
    {
        var influences = new List<SampleInfluence>(definition.Samples.Count);
        foreach (var sample in definition.Samples.Where(sample => sample.Enabled))
        {
            if (sample.Key.Count != driverVector.Count)
                continue;

            var weight = ComputeSampleWeight(driverVector, definition, sample, settings, regionSettings, out var distance);
            influences.Add(new SampleInfluence(sample, 0f, weight, distance));
        }

        if (influences.Count == 0)
        {
            return new RegionSolveResult
            {
                DriverStrength = 0f,
                RawActivation = 0f,
                Output = default,
                Influences = Array.Empty<SampleInfluence>(),
                TotalSampleCount = 0,
                InfluenceSampleCount = 0,
                ShortlistApplied = false,
                BroadInterpolation = false,
                SafetyLimited = true,
                Summary = "No RBF pose samples were available for this region.",
            };
        }

        var totalSampleCount = influences.Count;
        influences = influences
            .OrderByDescending(entry => entry.RawWeight)
            .ThenBy(entry => entry.Distance)
            .ToList();

        if (influences.Count > MaxShortlistedInfluenceSamples)
        {
            var shortlisted = influences.Take(MaxShortlistedInfluenceSamples).ToList();
            var retainedFloorIndex = Math.Min(shortlisted.Count - 1, MinShortlistedInfluenceSamples - 1);
            var retainedFloor = MathF.Max(
                MinimumShortlistedRawWeight,
                shortlisted[retainedFloorIndex].RawWeight * 0.25f);

            shortlisted = shortlisted
                .Where((entry, index) => index < MinShortlistedInfluenceSamples || entry.RawWeight >= retainedFloor || entry.Distance <= 0.16f)
                .Take(MaxShortlistedInfluenceSamples)
                .ToList();

            while (shortlisted.Count < Math.Min(MinShortlistedInfluenceSamples, influences.Count))
                shortlisted.Add(influences[shortlisted.Count]);

            influences = shortlisted;
        }

        var shortlistApplied = totalSampleCount > influences.Count;

        var rawWeightTotal = influences.Sum(entry => entry.RawWeight);
        if (rawWeightTotal <= 0.0001f)
        {
            var nearest = influences[0];
            influences[0] = nearest with { Weight = 1f, RawWeight = 1f };
            for (var i = 1; i < influences.Count; ++i)
                influences[i] = influences[i] with { Weight = 0f };
        }
        else
        {
            for (var i = 0; i < influences.Count; ++i)
            {
                var influence = influences[i];
                influences[i] = influence with { Weight = Math.Clamp(influence.RawWeight / rawWeightTotal, 0f, 1f) };
            }
        }

        var broadInterpolation = influences.Count(entry => entry.Weight >= 0.14f) >= 4 || influences.Count(entry => entry.Weight >= 0.07f) >= 6;
        var blendedOutput = BlendSampleOutput(influences);
        var minDistance = influences.Min(entry => entry.Distance);
        var strongestRawWeight = influences.Max(entry => entry.RawWeight);
        var strongestNormalizedWeight = influences.Max(entry => entry.Weight);
        var coverageFromDistance = 1f - Math.Clamp(minDistance / 0.95f, 0f, 1f);
        var coverageFromWeight = Math.Clamp((strongestRawWeight - 0.10f) / 0.70f, 0f, 1f);
        var coverage = Math.Clamp((coverageFromDistance * 0.55f) + (coverageFromWeight * 0.45f), 0f, 1f);
        var safetyLimited = false;

        if (coverage < 0.35f)
            safetyLimited = true;

        if (strongestNormalizedWeight > 0.92f && minDistance > 0.30f)
        {
            coverage *= 0.82f;
            safetyLimited = true;
        }

        if (minDistance > 0.65f)
        {
            coverage *= 0.70f;
            safetyLimited = true;
        }

        return new RegionSolveResult
        {
            DriverStrength = 0f,
            RawActivation = Math.Clamp(blendedOutput.Activation * coverage, 0f, 1f),
            Output = blendedOutput,
            Influences = influences,
            TotalSampleCount = totalSampleCount,
            InfluenceSampleCount = influences.Count,
            ShortlistApplied = shortlistApplied,
            BroadInterpolation = broadInterpolation,
            SafetyLimited = safetyLimited,
            Summary = strongestNormalizedWeight <= 0.001f
                ? "No meaningful pose sample influence was found."
                : $"Dominant RBF samples: {BuildSampleSummary(influences, totalSampleCount, shortlistApplied, broadInterpolation)}.",
        };
    }

    private static float EvaluateLiveDriver(
        Armature armature,
        CharacterBase* cBase,
        AdvancedBodyScalingCorrectiveDriverType type,
        CorrectiveSignals signals)
        => type switch
        {
            AdvancedBodyScalingCorrectiveDriverType.ArmElevation => AverageStrength(
                DriverStrengthForBone(armature, cBase, "j_ude_a_l", 20f, 75f),
                DriverStrengthForBone(armature, cBase, "j_ude_a_r", 20f, 75f)),
            AdvancedBodyScalingCorrectiveDriverType.ShoulderSpread => AverageStrength(
                DriverStrengthForBone(armature, cBase, "j_sako_l", 8f, 40f),
                DriverStrengthForBone(armature, cBase, "j_sako_r", 8f, 40f),
                DriverStrengthForBone(armature, cBase, "j_ude_a_l", 24f, 80f) * 0.75f,
                DriverStrengthForBone(armature, cBase, "j_ude_a_r", 24f, 80f) * 0.75f),
            AdvancedBodyScalingCorrectiveDriverType.ClavicleTension => AverageStrength(
                DriverStrengthForBone(armature, cBase, "j_sako_l", 10f, 45f),
                DriverStrengthForBone(armature, cBase, "j_sako_r", 10f, 45f)),
            AdvancedBodyScalingCorrectiveDriverType.NeckTilt => DriverStrengthForBone(armature, cBase, "j_kubi", 10f, 32f),
            AdvancedBodyScalingCorrectiveDriverType.NeckTwist => AverageStrength(
                DriverStrengthForBone(armature, cBase, "j_kubi", 12f, 40f),
                DriverStrengthForBone(armature, cBase, "j_sebo_c", 10f, 35f) * 0.6f),
            AdvancedBodyScalingCorrectiveDriverType.HipFlexion => AverageStrength(
                DriverStrengthForBone(armature, cBase, "j_asi_a_l", 18f, 70f),
                DriverStrengthForBone(armature, cBase, "j_asi_a_r", 18f, 70f)),
            AdvancedBodyScalingCorrectiveDriverType.KneeStress => AverageStrength(
                DriverStrengthForBone(armature, cBase, "j_asi_c_l", 15f, 55f),
                DriverStrengthForBone(armature, cBase, "j_asi_c_r", 15f, 55f),
                DriverStrengthForBone(armature, cBase, "j_ude_b_l", 15f, 50f) * 0.45f,
                DriverStrengthForBone(armature, cBase, "j_ude_b_r", 15f, 50f) * 0.45f),
            AdvancedBodyScalingCorrectiveDriverType.TorsoTwist => AverageStrength(
                DriverStrengthForBone(armature, cBase, "j_kosi", 10f, 35f),
                DriverStrengthForBone(armature, cBase, "j_sebo_b", 10f, 35f),
                DriverStrengthForBone(armature, cBase, "j_sebo_c", 10f, 35f) * 0.6f),
            AdvancedBodyScalingCorrectiveDriverType.ForwardBend => AverageStrength(
                DriverStrengthForBone(armature, cBase, "j_kosi", 14f, 45f),
                DriverStrengthForBone(armature, cBase, "j_sebo_a", 14f, 45f),
                DriverStrengthForBone(armature, cBase, "j_sebo_b", 14f, 45f) * 0.6f),
            AdvancedBodyScalingCorrectiveDriverType.TaperStress => signals.TaperStress,
            AdvancedBodyScalingCorrectiveDriverType.CrossRegionContinuity => signals.ContinuityStress,
            _ => 0f,
        };

    private static float EvaluateStaticDriver(
        AdvancedBodyScalingCorrectiveDriverType type,
        float poseWeight,
        CorrectiveSignals signals)
        => type switch
        {
            AdvancedBodyScalingCorrectiveDriverType.TaperStress => signals.TaperStress,
            AdvancedBodyScalingCorrectiveDriverType.CrossRegionContinuity => signals.ContinuityStress,
            AdvancedBodyScalingCorrectiveDriverType.TorsoTwist => Math.Clamp((poseWeight * 0.75f) + (signals.ContinuityStress * 0.25f), 0f, 1f),
            AdvancedBodyScalingCorrectiveDriverType.ForwardBend => Math.Clamp((poseWeight * 0.72f) + (signals.TaperStress * 0.28f), 0f, 1f),
            _ => poseWeight,
        };

    private static AdvancedBodyScalingCorrectiveDebugRegionState BuildDebugState(
        CorrectiveDefinition definition,
        float driverStrength,
        CorrectiveSignals signals,
        float activation,
        float correctionStrength,
        IReadOnlyList<DriverSample> driverSamples,
        RegionSolveResult rbf,
        CorrectionApplicationMetrics metrics,
        AdvancedBodyScalingCorrectiveRegionSettings regionSettings,
        AdvancedBodyScalingPoseCorrectiveSettings settings)
    {
        var dominant = rbf.Influences
            .Where(entry => entry.Weight > 0.03f)
            .Take(3)
            .Select(entry => new AdvancedBodyScalingCorrectiveSampleDebugState
            {
                Name = entry.Sample.Name,
                Weight = entry.Weight,
                Distance = entry.Distance,
                Summary = entry.Sample.Summary,
            })
            .ToList();

        var debugState = new AdvancedBodyScalingCorrectiveDebugRegionState
        {
            Region = definition.Region,
            Label = definition.Label,
            DriverStrength = driverStrength,
            Discontinuity = signals.Discontinuity,
            Activation = activation,
            RawActivation = rbf.RawActivation,
            Strength = correctionStrength,
            EstimatedRiskReduction = EstimateRiskReductionFraction(settings, regionSettings, correctionStrength),
            SafetyLimited = rbf.SafetyLimited || metrics.Clamped,
            LocksOrPinsLimited = metrics.LocksOrPinsLimited,
            Clamped = metrics.Clamped,
            Damped = activation + 0.01f < rbf.RawActivation,
            SampleCount = rbf.TotalSampleCount,
            InfluenceSampleCount = rbf.InfluenceSampleCount,
            ShortlistApplied = rbf.ShortlistApplied,
            BroadInterpolation = rbf.BroadInterpolation,
            DriverSummary = BuildDriverSummary(driverSamples),
            DriverVectorSummary = BuildDriverVectorSummary(driverSamples),
            SampleSummary = BuildSampleSummary(rbf.Influences, rbf.TotalSampleCount, rbf.ShortlistApplied, rbf.BroadInterpolation),
            Summary = BuildRegionSummary(activation, correctionStrength, rbf, metrics),
            Description = definition.Description,
        };

        debugState.InfluentialSamples.AddRange(dominant);
        return debugState;
    }

    private static string BuildOverallSummary(
        IReadOnlyList<AdvancedBodyScalingCorrectiveDebugRegionState> activeRegions,
        bool safetyLimited,
        bool locksLimited)
    {
        if (activeRegions.Count == 0)
            return "No supported corrective region is strongly active in the current pose.";

        var focusText = string.Join(
            " and ",
            activeRegions
                .OrderByDescending(entry => entry.Strength)
                .Take(2)
                .Select(entry => entry.Label));

        if (safetyLimited && locksLimited)
            return $"RBF pose-space correctives are active around {focusText}, with both safety limiting and lock/pin authority constraining the output.";

        if (safetyLimited)
            return $"RBF pose-space correctives are active around {focusText}, with conservative safety limiting softening the output.";

        if (locksLimited)
            return $"RBF pose-space correctives are active around {focusText}, with locks or pinned axes constraining part of the result.";

        return $"RBF pose-space correctives are active around {focusText}.";
    }

    private static string BuildRegionSummary(
        float activation,
        float correctionStrength,
        RegionSolveResult rbf,
        CorrectionApplicationMetrics metrics)
    {
        var builder = new StringBuilder();
        builder.Append($"RBF activation {activation:0.00}, corrective strength {correctionStrength:0.00}. ");
        builder.Append(rbf.Summary);

        if (metrics.Clamped)
            builder.Append(" Correction clamping kept the transform output conservative.");

        if (metrics.LocksOrPinsLimited)
            builder.Append(" Locks or pinned axes limited part of the correction.");

        return builder.ToString();
    }

    private static float ComputeSampleWeight(
        IReadOnlyList<float> driverVector,
        CorrectiveDefinition definition,
        PoseSample sample,
        AdvancedBodyScalingPoseCorrectiveSettings settings,
        AdvancedBodyScalingCorrectiveRegionSettings regionSettings,
        out float distance)
    {
        var weightedDistance = 0f;
        var totalWeight = 0f;
        for (var i = 0; i < definition.Drivers.Count && i < driverVector.Count; ++i)
        {
            var delta = driverVector[i] - sample.Key[i];
            var weight = definition.Drivers[i].Weight;
            weightedDistance += delta * delta * weight;
            totalWeight += weight;
        }

        distance = totalWeight <= 0f
            ? 0f
            : MathF.Sqrt(weightedDistance / totalWeight);

        var sharpnessScale = Lerp(1.20f, 0.58f, Math.Clamp(settings.PoseMapSharpness / AdvancedBodyScalingPoseCorrectiveTuning.UiPoseMapSharpnessMax, 0f, 1f));
        var falloffScale = Lerp(0.65f, 1.35f, regionSettings.Falloff);
        var radius = MathF.Max(0.12f, sample.Radius * sharpnessScale * falloffScale);
        var gaussian = MathF.Exp(-(distance * distance) / (2f * radius * radius));
        return Math.Clamp(gaussian * sample.Weight, 0f, 1f);
    }

    private static PoseSampleOutput BlendSampleOutput(IReadOnlyList<SampleInfluence> influences)
    {
        var activation = 0f;
        var groupA = 0f;
        var groupB = 0f;
        var bridge = 0f;
        var targetBias = 0f;
        var taperBlend = 0f;
        var axisBias = 0f;

        foreach (var influence in influences)
        {
            activation += influence.Sample.Output.Activation * influence.Weight;
            groupA += influence.Sample.Output.GroupABlend * influence.Weight;
            groupB += influence.Sample.Output.GroupBBlend * influence.Weight;
            bridge += influence.Sample.Output.BridgeBlend * influence.Weight;
            targetBias += influence.Sample.Output.TargetBias * influence.Weight;
            taperBlend += influence.Sample.Output.TaperBlend * influence.Weight;
            axisBias += influence.Sample.Output.AxisBias * influence.Weight;
        }

        return new PoseSampleOutput(
            Math.Clamp(activation, 0f, 1f),
            Math.Clamp(groupA, 0f, 1f),
            Math.Clamp(groupB, 0f, 1f),
            Math.Clamp(bridge, 0f, 1f),
            Math.Clamp(targetBias, -1f, 1f),
            Math.Clamp(taperBlend, 0f, 1f),
            Math.Clamp(axisBias, 0f, 1f));
    }

    private static string BuildDriverSummary(IReadOnlyList<DriverSample> samples)
    {
        var topDrivers = samples
            .Where(sample => sample.Strength > 0.05f)
            .OrderByDescending(sample => sample.Strength)
            .Take(3)
            .Select(sample => $"{GetDriverLabel(sample.Type)} {sample.Strength:0.00}")
            .ToList();

        return topDrivers.Count == 0 ? "driver activity low" : string.Join(", ", topDrivers);
    }

    private static string BuildDriverVectorSummary(IReadOnlyList<DriverSample> samples)
        => samples.Count == 0
            ? "No driver vector available."
            : string.Join(", ", samples.Select(sample => $"{GetDriverLabel(sample.Type)} {sample.Strength:0.00}"));

    private static string BuildSampleSummary(
        IReadOnlyList<SampleInfluence> influences,
        int totalSampleCount,
        bool shortlistApplied,
        bool broadInterpolation)
    {
        var dominant = influences
            .Where(entry => entry.Weight > 0.03f)
            .Take(3)
            .Select(entry => $"{entry.Sample.Name} {entry.Weight:0.00} (dist {entry.Distance:0.00})")
            .ToList();

        var selectionText = shortlistApplied
            ? $"nearest-sample shortlist {influences.Count}/{Math.Max(totalSampleCount, influences.Count)}"
            : $"full sample set {Math.Max(totalSampleCount, influences.Count)}";
        var blendText = broadInterpolation ? "broad interpolation" : "focused interpolation";
        return dominant.Count == 0
            ? $"{blendText} using {selectionText}, but no meaningful sample influence was found"
            : $"{blendText} using {selectionText}: {string.Join(", ", dominant)}";
    }

    private static string GetDriverLabel(AdvancedBodyScalingCorrectiveDriverType type)
        => type switch
        {
            AdvancedBodyScalingCorrectiveDriverType.ArmElevation => "arm elevation",
            AdvancedBodyScalingCorrectiveDriverType.ShoulderSpread => "shoulder spread",
            AdvancedBodyScalingCorrectiveDriverType.ClavicleTension => "clavicle tension",
            AdvancedBodyScalingCorrectiveDriverType.NeckTilt => "neck tilt",
            AdvancedBodyScalingCorrectiveDriverType.NeckTwist => "neck twist",
            AdvancedBodyScalingCorrectiveDriverType.HipFlexion => "hip flexion",
            AdvancedBodyScalingCorrectiveDriverType.KneeStress => "joint stress",
            AdvancedBodyScalingCorrectiveDriverType.TorsoTwist => "torso twist",
            AdvancedBodyScalingCorrectiveDriverType.ForwardBend => "forward bend",
            AdvancedBodyScalingCorrectiveDriverType.TaperStress => "taper stress",
            AdvancedBodyScalingCorrectiveDriverType.CrossRegionContinuity => "continuity stress",
            _ => type.ToString(),
        };

    private static float DriverStrengthForBone(Armature armature, CharacterBase* cBase, string boneName, float startAngle, float fullAngle)
        => Remap(GetLocalRotationAngleDegrees(armature, cBase, boneName), startAngle, fullAngle);

    private static float GetLocalRotationAngleDegrees(Armature armature, CharacterBase* cBase, string boneName)
    {
        var bone = TryGetBone(armature, boneName);
        if (bone == null)
            return 0f;

        var transform = bone.GetGameTransform(cBase, ModelBone.PoseType.Local);
        if (transform.Equals(Constants.NullTransform))
            return 0f;

        var rotation = Quaternion.Normalize(transform.Rotation.ToQuaternion());
        var w = Math.Clamp(MathF.Abs(rotation.W), 0f, 1f);
        var angle = 2f * MathF.Acos(w);
        return angle * 180f / MathF.PI;
    }

    private static bool HasUsableScaleData(Func<string, BoneTransform?> resolver, CorrectiveDefinition definition)
        => HasAnyScaleData(resolver, definition.GroupA) && HasAnyScaleData(resolver, definition.GroupB);

    private static bool HasAnyScaleData(Func<string, BoneTransform?> resolver, IReadOnlyList<string> bones)
        => bones.Any(bone => resolver(bone) != null);

    private static CorrectiveSignals BuildSignals(Func<string, BoneTransform?> resolver, CorrectiveDefinition definition)
    {
        var avgA = AverageCurrentScale(resolver, definition.GroupA);
        var avgB = AverageCurrentScale(resolver, definition.GroupB);
        var discontinuity = MathF.Abs(avgA - avgB);

        if (definition.BridgeBones.Count > 0)
        {
            var bridge = AverageCurrentScale(resolver, definition.BridgeBones);
            discontinuity = MathF.Max(discontinuity, MathF.Abs(avgA - bridge));
            discontinuity = MathF.Max(discontinuity, MathF.Abs(bridge - avgB));
        }

        var ratioGap = MathF.Abs(SafeDivide(avgA, avgB) - 1f);
        var continuityStress = Remap(discontinuity, definition.DiscontinuityStart, definition.DiscontinuityFull);
        var taperStress = Math.Max(continuityStress, Remap(ratioGap, 0.10f, 0.32f));
        return new CorrectiveSignals(discontinuity, continuityStress, taperStress);
    }

    private static float ComputeActivation(
        float rawActivation,
        float previousActivation,
        AdvancedBodyScalingPoseCorrectiveSettings settings,
        AdvancedBodyScalingCorrectiveRegionSettings regionSettings,
        bool useDeadzone = true)
    {
        var combinedDamping = Math.Clamp((settings.Damping * 0.45f) + (regionSettings.Smoothing * 0.55f), 0f, 0.97f);
        var response = Math.Clamp(1f - combinedDamping, 0.10f, 1f);

        if (rawActivation <= regionSettings.ActivationThreshold)
            return previousActivation > 0f ? previousActivation * (1f - Math.Clamp(response * 0.85f, 0.12f, 0.55f)) : 0f;

        if (useDeadzone && previousActivation <= 0.001f && rawActivation < regionSettings.ActivationThreshold + regionSettings.ActivationDeadzone)
            return 0f;

        var target = Remap(rawActivation, regionSettings.ActivationThreshold, 1f);
        var smoothed = previousActivation + ((target - previousActivation) * response);
        return smoothed < 0.003f ? 0f : Math.Clamp(smoothed, 0f, 1f);
    }

    private static float ComputeCorrectionStrength(
        AdvancedBodyScalingSettings settings,
        AdvancedBodyScalingPoseCorrectiveSettings poseSettings,
        AdvancedBodyScalingCorrectiveRegionSettings regionSettings,
        float activation,
        float tuningFactor)
    {
        var correctionStrength = activation * poseSettings.Strength * regionSettings.Strength * regionSettings.Priority * tuningFactor;
        if (settings.AnimationSafeModeEnabled)
            correctionStrength = MathF.Min(correctionStrength, 0.80f);

        return Math.Clamp(correctionStrength, 0f, 1f);
    }

    private static float EstimateRiskReductionFraction(
        AdvancedBodyScalingPoseCorrectiveSettings settings,
        AdvancedBodyScalingCorrectiveRegionSettings regionSettings,
        float correctionStrength)
    {
        var clamp = MathF.Min(regionSettings.MaxCorrection, settings.MaxCorrectionClamp);
        return Math.Clamp(correctionStrength * (0.18f + (clamp * 5.0f)), 0f, 0.38f);
    }

    private static float GetPassivePoseWeight(CorrectiveSignals signals)
        => Math.Clamp((signals.ContinuityStress * 0.68f) + (signals.TaperStress * 0.32f), 0.10f, 1f);

    private static float GetRegionTuningFactor(
        AdvancedBodyScalingSettings settings,
        IReadOnlyList<AdvancedBodyRegion> relatedRegions)
    {
        var factors = new List<float>();
        foreach (var region in relatedRegions)
        {
            var profile = settings.GetRegionProfile(region);
            var factor =
                (profile.SmoothingMultiplier * 0.40f) +
                (profile.PoseValidationMultiplier * 0.35f) +
                (profile.GuardrailMultiplier * 0.25f);

            if (!profile.AllowPoseValidation)
                factor *= 0.85f;

            if (!profile.AllowGuardrails)
                factor *= 0.9f;

            factors.Add(Math.Clamp(factor, 0.25f, 1.15f));
        }

        return factors.Count == 0 ? 1f : Math.Clamp(factors.Average(), 0.25f, 1.15f);
    }

    private static CorrectionApplicationMetrics ApplyDefinitionCorrection(
        Dictionary<string, Vector3> scaleMultipliers,
        Armature armature,
        CorrectiveDefinition definition,
        CorrectiveSignals signals,
        AdvancedBodyScalingPoseCorrectiveSettings settings,
        AdvancedBodyScalingCorrectiveRegionSettings regionSettings,
        float correctionStrength,
        PoseSampleOutput output)
    {
        var metrics = new CorrectionApplicationMetrics();
        var avgA = AverageCurrentScale(armature.GetAppliedBoneTransform, definition.GroupA);
        var avgB = AverageCurrentScale(armature.GetAppliedBoneTransform, definition.GroupB);
        var midpoint = (avgA + avgB) * 0.5f;
        var targetBias = Math.Clamp(output.TargetBias * (0.45f + (signals.ContinuityStress * 0.55f)), -0.65f, 0.65f);
        var target = midpoint + ((avgB - avgA) * 0.5f * targetBias);
        var effectiveMaxCorrection = MathF.Min(regionSettings.MaxCorrection, settings.MaxCorrectionClamp);
        var taperBoost = 1f + (signals.TaperStress * output.TaperBlend * 0.65f);

        var groupABlend = Math.Clamp(correctionStrength * effectiveMaxCorrection * output.GroupABlend * taperBoost, 0f, 1f);
        var groupBBlend = Math.Clamp(correctionStrength * effectiveMaxCorrection * output.GroupBBlend * taperBoost, 0f, 1f);
        var bridgeBlend = Math.Clamp(correctionStrength * effectiveMaxCorrection * output.BridgeBlend * regionSettings.Falloff * taperBoost, 0f, 1f);

        metrics.Merge(ApplyUniformTargetCorrection(scaleMultipliers, armature, definition.GroupA, target, groupABlend, effectiveMaxCorrection * MathF.Max(0.25f, output.GroupABlend)));
        metrics.Merge(ApplyUniformTargetCorrection(scaleMultipliers, armature, definition.GroupB, target, groupBBlend, effectiveMaxCorrection * MathF.Max(0.25f, output.GroupBBlend)));
        metrics.Merge(ApplyUniformTargetCorrection(scaleMultipliers, armature, definition.BridgeBones, target, bridgeBlend, effectiveMaxCorrection * regionSettings.Falloff * MathF.Max(0.25f, output.BridgeBlend)));
        return metrics;
    }

    private static void ApplySpecialBias(
        Dictionary<string, Vector3> scaleMultipliers,
        Armature armature,
        AdvancedBodyScalingCorrectiveRegion region,
        AdvancedBodyScalingPoseCorrectiveSettings settings,
        AdvancedBodyScalingCorrectiveRegionSettings regionSettings,
        float correctionStrength,
        PoseSampleOutput output,
        CorrectionApplicationMetrics metrics)
    {
        if (region != AdvancedBodyScalingCorrectiveRegion.NeckShoulder || output.AxisBias <= 0.001f)
            return;

        ApplyNeckAxisBias(scaleMultipliers, armature, correctionStrength * output.AxisBias, MathF.Min(regionSettings.MaxCorrection, settings.MaxCorrectionClamp), metrics);
    }

    private static void ApplyNeckAxisBias(
        Dictionary<string, Vector3> scaleMultipliers,
        Armature armature,
        float correctionStrength,
        float maxCorrection,
        CorrectionApplicationMetrics metrics)
    {
        var neck = armature.GetAppliedBoneTransform("j_kubi");
        if (neck == null || neck.LockState != BoneLockState.Unlocked)
        {
            metrics.LocksOrPinsLimited = true;
            return;
        }

        var axisFactor = Math.Clamp(correctionStrength * (0.44f + (maxCorrection * 5.5f)), 0f, 0.035f);
        var multiplier = new Vector3(
            1f + (axisFactor * 0.20f),
            1f - axisFactor,
            1f + (axisFactor * 0.20f));
        var desiredScale = neck.Scaling * multiplier;
        var targetScale = neck.ApplyScalePins(desiredScale);
        metrics.LocksOrPinsLimited |= !targetScale.IsApproximately(desiredScale, 0.0005f);

        AddMultiplier(scaleMultipliers, "j_kubi", new Vector3(
            SafeDivide(targetScale.X, neck.Scaling.X),
            SafeDivide(targetScale.Y, neck.Scaling.Y),
            SafeDivide(targetScale.Z, neck.Scaling.Z)));
        metrics.AnyApplied = true;
    }

    private static CorrectionApplicationMetrics ApplyUniformTargetCorrection(
        Dictionary<string, Vector3> scaleMultipliers,
        Armature armature,
        IReadOnlyList<string> bones,
        float targetUniform,
        float blendStrength,
        float maxAdjustment)
    {
        var metrics = new CorrectionApplicationMetrics();
        if (bones.Count == 0 || blendStrength <= 0.0005f || maxAdjustment <= 0.0001f)
            return metrics;

        foreach (var boneName in bones)
        {
            var transform = armature.GetAppliedBoneTransform(boneName);
            if (transform == null)
                continue;

            if (transform.LockState != BoneLockState.Unlocked)
            {
                metrics.LocksOrPinsLimited = true;
                continue;
            }

            var currentUniform = AdvancedBodyScalingPipeline.GetUniformScale(transform.Scaling);
            if (currentUniform <= 0.0001f)
                continue;

            var blended = Lerp(currentUniform, targetUniform, Math.Clamp(blendStrength, 0f, 1f));
            var unclampedDelta = blended - currentUniform;
            var delta = Math.Clamp(unclampedDelta, -maxAdjustment, maxAdjustment);
            metrics.Clamped |= MathF.Abs(delta - unclampedDelta) > 0.0001f;
            if (MathF.Abs(delta) <= 0.0005f)
                continue;

            var desiredScale = transform.Scaling * ((currentUniform + delta) / currentUniform);
            var targetScale = transform.ApplyScalePins(desiredScale);
            metrics.LocksOrPinsLimited |= !targetScale.IsApproximately(desiredScale, 0.0005f);

            AddMultiplier(scaleMultipliers, boneName, new Vector3(
                SafeDivide(targetScale.X, transform.Scaling.X),
                SafeDivide(targetScale.Y, transform.Scaling.Y),
                SafeDivide(targetScale.Z, transform.Scaling.Z)));
            metrics.AnyApplied = true;
        }

        return metrics;
    }

    private static void AddMultiplier(Dictionary<string, Vector3> scaleMultipliers, string boneName, Vector3 multiplier)
    {
        multiplier = ClampMultiplier(multiplier);
        if (multiplier.IsApproximately(Vector3.One, 0.0005f))
            return;

        if (scaleMultipliers.TryGetValue(boneName, out var existing))
            scaleMultipliers[boneName] = ClampMultiplier(new Vector3(existing.X * multiplier.X, existing.Y * multiplier.Y, existing.Z * multiplier.Z));
        else
            scaleMultipliers[boneName] = multiplier;
    }

    private static Vector3 ClampMultiplier(Vector3 multiplier)
        => new(
            Math.Clamp(multiplier.X, 0.88f, 1.12f),
            Math.Clamp(multiplier.Y, 0.88f, 1.12f),
            Math.Clamp(multiplier.Z, 0.88f, 1.12f));

    private static float AverageCurrentScale(Func<string, BoneTransform?> resolver, IReadOnlyList<string> bones)
    {
        var values = bones
            .Select(resolver)
            .Where(transform => transform != null)
            .Select(transform => AdvancedBodyScalingPipeline.GetUniformScale(transform!.Scaling))
            .ToList();

        return values.Count == 0 ? 1f : values.Average();
    }

    private static ModelBone? TryGetBone(Armature armature, string boneName)
        => armature.GetAllBones().FirstOrDefault(bone => bone.BoneName == boneName);

    private static CorrectiveDefinition? TryGetDefinition(AdvancedBodyScalingCorrectiveRegion region)
        => DefinitionMap.TryGetValue(region, out var definition) ? definition : null;

    private static float WeightedAverage(IReadOnlyList<DriverSample> samples, IReadOnlyList<DriverCondition> drivers)
    {
        var totalWeight = 0f;
        var weighted = 0f;
        for (var i = 0; i < Math.Min(samples.Count, drivers.Count); ++i)
        {
            weighted += samples[i].Strength * drivers[i].Weight;
            totalWeight += drivers[i].Weight;
        }

        if (totalWeight <= 0f)
            return 0f;

        return Math.Clamp(weighted / totalWeight, 0f, 1f);
    }

    private static float AverageStrength(params float[] values)
    {
        var active = values.Where(value => value > 0f).ToList();
        return active.Count == 0 ? 0f : Math.Clamp(active.Average(), 0f, 1f);
    }

    private static float Remap(float value, float start, float full)
    {
        if (full <= start)
            return value >= full ? 1f : 0f;

        return Math.Clamp((value - start) / (full - start), 0f, 1f);
    }

    private static float Lerp(float a, float b, float t)
        => a + ((b - a) * t);

    private static float SafeDivide(float numerator, float denominator)
        => MathF.Abs(denominator) <= 0.0001f ? 1f : numerator / denominator;

    private static IReadOnlyList<PoseSample> BuildNeckShoulderSamples()
        => BuildPoseLibrary(
            AdvancedBodyScalingCorrectiveRegion.NeckShoulder,
            Sample("Neutral", 0.50f, Output(0f, 0f, 0f, 0f, 0f, 0f, 0f), 0f, 0f, 0f, 0f, 0f),
            Sample("Neck tilt mild", 0.48f, Output(0.44f, 0.46f, 0.36f, 0.54f, 0.02f, 0.08f, 0.40f), 0.08f, 0.32f, 0.08f, 0.06f, 0.18f),
            Sample("Neck tilt strong", 0.34f, Output(0.86f, 0.80f, 0.66f, 0.88f, 0.06f, 0.16f, 1.00f), 0.12f, 0.78f, 0.14f, 0.10f, 0.36f),
            Sample("Neck twist mild", 0.46f, Output(0.42f, 0.40f, 0.34f, 0.46f, 0.02f, 0.08f, 0.38f), 0.06f, 0.08f, 0.34f, 0.16f, 0.18f),
            Sample("Neck twist strong", 0.32f, Output(0.82f, 0.74f, 0.62f, 0.82f, 0.04f, 0.14f, 0.92f), 0.10f, 0.14f, 0.82f, 0.28f, 0.34f),
            Sample("Shoulder raise mild", 0.44f, Output(0.52f, 0.48f, 0.52f, 0.64f, 0.10f, 0.12f, 0.44f), 0.34f, 0.12f, 0.08f, 0.08f, 0.24f),
            Sample("Shoulder raise strong", 0.30f, Output(0.92f, 0.78f, 0.74f, 0.94f, 0.16f, 0.22f, 0.82f), 0.88f, 0.18f, 0.12f, 0.14f, 0.58f),
            Sample("Upper torso twist mild", 0.44f, Output(0.46f, 0.42f, 0.40f, 0.56f, 0.02f, 0.08f, 0.34f), 0.16f, 0.12f, 0.12f, 0.42f, 0.22f),
            Sample("Upper torso twist strong", 0.32f, Output(0.76f, 0.62f, 0.60f, 0.82f, 0.04f, 0.14f, 0.58f), 0.26f, 0.18f, 0.22f, 0.92f, 0.46f),
            Sample("Bridge continuity mild", 0.38f, Output(0.50f, 0.46f, 0.50f, 0.72f, 0.12f, 0.16f, 0.48f), 0.14f, 0.10f, 0.10f, 0.12f, 0.44f),
            Sample("Bridge continuity strong", 0.28f, Output(0.84f, 0.72f, 0.80f, 0.98f, 0.20f, 0.24f, 0.70f), 0.20f, 0.16f, 0.16f, 0.18f, 0.96f),
            Sample("Tilt plus shoulder raise", 0.32f, Output(0.78f, 0.70f, 0.66f, 0.86f, 0.10f, 0.18f, 0.82f), 0.52f, 0.54f, 0.12f, 0.12f, 0.46f),
            Sample("Twist plus shoulder raise", 0.30f, Output(0.80f, 0.68f, 0.68f, 0.84f, 0.12f, 0.20f, 0.86f), 0.58f, 0.18f, 0.58f, 0.18f, 0.48f),
            Sample("Tilt plus torso twist", 0.30f, Output(0.76f, 0.70f, 0.60f, 0.86f, 0.06f, 0.18f, 0.90f), 0.18f, 0.56f, 0.18f, 0.58f, 0.40f),
            Sample("Twist plus torso load", 0.30f, Output(0.78f, 0.66f, 0.62f, 0.82f, 0.08f, 0.18f, 0.82f), 0.22f, 0.18f, 0.60f, 0.70f, 0.42f),
            Sample("Raised loaded bridge", 0.28f, Output(0.90f, 0.76f, 0.78f, 0.96f, 0.18f, 0.26f, 0.76f), 0.84f, 0.34f, 0.24f, 0.26f, 0.82f),
            Sample("Compressed bridge edge", 0.28f, Output(0.86f, 0.74f, 0.82f, 0.98f, 0.22f, 0.28f, 0.68f), 0.26f, 0.26f, 0.18f, 0.12f, 1.00f),
            Sample("Extended bridge edge", 0.28f, Output(0.72f, 0.64f, 0.70f, 0.88f, 0.14f, 0.20f, 0.60f), 0.30f, 0.20f, 0.26f, 0.34f, 0.86f),
            Sample("Mixed upper-body strain", 0.26f, Output(0.88f, 0.76f, 0.72f, 0.92f, 0.12f, 0.24f, 0.94f), 0.62f, 0.48f, 0.40f, 0.44f, 0.72f),
            Sample("Twisted overhead frame", 0.26f, Output(0.94f, 0.80f, 0.78f, 0.94f, 0.18f, 0.26f, 1.00f), 0.92f, 0.28f, 0.34f, 0.62f, 0.64f),
            Sample("Tilted support arc", 0.34f, Output(0.68f, 0.62f, 0.58f, 0.82f, 0.08f, 0.14f, 0.72f), 0.20f, 0.44f, 0.12f, 0.22f, 0.58f),
            Sample("Twist-supported bridge", 0.34f, Output(0.70f, 0.60f, 0.60f, 0.80f, 0.08f, 0.14f, 0.70f), 0.18f, 0.18f, 0.46f, 0.48f, 0.58f),
            Sample("Supported shoulder frame", 0.34f, Output(0.74f, 0.64f, 0.66f, 0.88f, 0.10f, 0.16f, 0.62f), 0.50f, 0.18f, 0.12f, 0.18f, 0.66f),
            Sample("Soft neck bridge load", 0.36f, Output(0.60f, 0.54f, 0.56f, 0.78f, 0.06f, 0.12f, 0.56f), 0.12f, 0.16f, 0.14f, 0.18f, 0.52f),
            Sample("Tilt plus continuity arc", 0.32f, Output(0.80f, 0.70f, 0.68f, 0.90f, 0.12f, 0.18f, 0.86f), 0.28f, 0.54f, 0.14f, 0.22f, 0.72f),
            Sample("Twist plus continuity arc", 0.32f, Output(0.80f, 0.68f, 0.68f, 0.88f, 0.12f, 0.18f, 0.84f), 0.22f, 0.18f, 0.58f, 0.34f, 0.74f),
            Sample("Overhead support transition", 0.30f, Output(0.86f, 0.72f, 0.72f, 0.92f, 0.14f, 0.20f, 0.88f), 0.76f, 0.24f, 0.18f, 0.24f, 0.70f),
            Sample("Twisted support arc", 0.30f, Output(0.84f, 0.70f, 0.70f, 0.90f, 0.12f, 0.20f, 0.90f), 0.32f, 0.26f, 0.68f, 0.74f, 0.62f),
            Sample("Loaded tilt-twist frame", 0.28f, Output(0.90f, 0.78f, 0.76f, 0.94f, 0.16f, 0.24f, 0.96f), 0.48f, 0.58f, 0.46f, 0.48f, 0.76f),
            Sample("Continuity-heavy overhead frame", 0.28f, Output(0.92f, 0.78f, 0.78f, 0.98f, 0.18f, 0.24f, 0.82f), 0.82f, 0.30f, 0.18f, 0.22f, 0.96f),
            Sample("Compressed raised support", 0.28f, Output(0.88f, 0.76f, 0.82f, 1.00f, 0.20f, 0.28f, 0.74f), 0.68f, 0.34f, 0.22f, 0.18f, 1.00f),
            Sample("Extended twist-transition", 0.30f, Output(0.76f, 0.66f, 0.70f, 0.90f, 0.12f, 0.18f, 0.68f), 0.26f, 0.20f, 0.42f, 0.56f, 0.90f));

    private static IReadOnlyList<PoseSample> BuildClavicleUpperChestSamples()
        => BuildPoseLibrary(
            AdvancedBodyScalingCorrectiveRegion.ClavicleUpperChest,
            Sample("Neutral", 0.54f, Output(0f, 0f, 0f, 0f, 0f, 0f, 0f), 0f, 0f, 0f, 0f, 0f),
            Sample("Shoulder spread mild", 0.46f, Output(0.48f, 0.40f, 0.46f, 0.58f, 0.04f, 0.10f, 0f), 0.34f, 0.18f, 0.12f, 0.28f, 0.16f),
            Sample("Shoulder spread strong", 0.32f, Output(0.88f, 0.70f, 0.76f, 0.92f, 0.10f, 0.18f, 0f), 0.92f, 0.28f, 0.18f, 0.52f, 0.26f),
            Sample("Clavicle tension mild", 0.46f, Output(0.50f, 0.44f, 0.48f, 0.60f, 0.02f, 0.10f, 0f), 0.18f, 0.34f, 0.14f, 0.24f, 0.18f),
            Sample("Clavicle tension strong", 0.32f, Output(0.86f, 0.68f, 0.74f, 0.90f, 0.04f, 0.18f, 0f), 0.28f, 0.90f, 0.20f, 0.54f, 0.28f),
            Sample("Upper chest twist mild", 0.44f, Output(0.46f, 0.38f, 0.44f, 0.56f, 0.02f, 0.08f, 0f), 0.22f, 0.20f, 0.42f, 0.30f, 0.18f),
            Sample("Upper chest twist strong", 0.32f, Output(0.78f, 0.62f, 0.68f, 0.84f, 0.04f, 0.14f, 0f), 0.30f, 0.28f, 0.90f, 0.54f, 0.24f),
            Sample("Underarm compression mild", 0.40f, Output(0.56f, 0.42f, 0.50f, 0.74f, 0.06f, 0.20f, 0f), 0.26f, 0.22f, 0.12f, 0.46f, 0.58f),
            Sample("Underarm compression strong", 0.28f, Output(0.84f, 0.62f, 0.72f, 1.00f, 0.10f, 0.34f, 0f), 0.42f, 0.30f, 0.18f, 0.82f, 0.96f),
            Sample("Underarm extension", 0.38f, Output(0.62f, 0.48f, 0.60f, 0.82f, 0.08f, 0.16f, 0f), 0.68f, 0.30f, 0.10f, 0.28f, 0.18f),
            Sample("Spread plus tension", 0.32f, Output(0.80f, 0.64f, 0.72f, 0.88f, 0.06f, 0.16f, 0f), 0.68f, 0.78f, 0.16f, 0.56f, 0.26f),
            Sample("Spread plus twist", 0.30f, Output(0.80f, 0.60f, 0.70f, 0.86f, 0.04f, 0.18f, 0f), 0.76f, 0.36f, 0.62f, 0.64f, 0.28f),
            Sample("Tension plus twist", 0.30f, Output(0.82f, 0.62f, 0.72f, 0.88f, 0.04f, 0.18f, 0f), 0.42f, 0.74f, 0.68f, 0.60f, 0.24f),
            Sample("Spread plus underarm load", 0.30f, Output(0.88f, 0.64f, 0.76f, 1.00f, 0.08f, 0.30f, 0f), 0.86f, 0.48f, 0.22f, 0.74f, 0.72f),
            Sample("Tension plus taper load", 0.28f, Output(0.86f, 0.60f, 0.72f, 0.96f, 0.06f, 0.32f, 0f), 0.52f, 0.84f, 0.22f, 0.72f, 0.84f),
            Sample("Broad chest bridge", 0.30f, Output(0.78f, 0.58f, 0.66f, 0.94f, 0.06f, 0.18f, 0f), 0.58f, 0.46f, 0.30f, 0.86f, 0.42f),
            Sample("Compressed underarm bridge", 0.28f, Output(0.90f, 0.66f, 0.74f, 1.00f, 0.10f, 0.36f, 0f), 0.44f, 0.38f, 0.20f, 1.00f, 0.92f),
            Sample("Extended underarm bridge", 0.28f, Output(0.74f, 0.56f, 0.68f, 0.88f, 0.08f, 0.14f, 0f), 0.82f, 0.34f, 0.26f, 0.70f, 0.26f),
            Sample("Loaded upper-body frame", 0.26f, Output(0.88f, 0.68f, 0.74f, 0.96f, 0.06f, 0.22f, 0f), 0.72f, 0.72f, 0.36f, 0.84f, 0.44f),
            Sample("Mixed upper-body stress", 0.26f, Output(0.94f, 0.72f, 0.78f, 1.00f, 0.10f, 0.34f, 0f), 0.88f, 0.86f, 0.64f, 0.92f, 0.86f),
            Sample("Chest spread transition", 0.36f, Output(0.66f, 0.52f, 0.60f, 0.78f, 0.04f, 0.14f, 0f), 0.52f, 0.26f, 0.18f, 0.42f, 0.20f),
            Sample("Clavicle lift support", 0.36f, Output(0.70f, 0.56f, 0.62f, 0.82f, 0.04f, 0.14f, 0f), 0.28f, 0.56f, 0.18f, 0.40f, 0.22f),
            Sample("Twist-supported chest frame", 0.34f, Output(0.70f, 0.54f, 0.62f, 0.82f, 0.04f, 0.12f, 0f), 0.26f, 0.24f, 0.58f, 0.46f, 0.20f),
            Sample("Underarm reach transition", 0.34f, Output(0.74f, 0.56f, 0.64f, 0.88f, 0.06f, 0.22f, 0f), 0.58f, 0.30f, 0.16f, 0.56f, 0.54f),
            Sample("Elevated spread support", 0.32f, Output(0.82f, 0.62f, 0.72f, 0.90f, 0.06f, 0.18f, 0f), 0.70f, 0.58f, 0.18f, 0.62f, 0.28f),
            Sample("Tension continuity brace", 0.32f, Output(0.82f, 0.60f, 0.70f, 0.92f, 0.06f, 0.20f, 0f), 0.34f, 0.74f, 0.24f, 0.72f, 0.34f),
            Sample("Twist plus taper load", 0.30f, Output(0.84f, 0.60f, 0.72f, 0.94f, 0.08f, 0.28f, 0f), 0.30f, 0.34f, 0.72f, 0.66f, 0.72f),
            Sample("Broad chest compression", 0.30f, Output(0.86f, 0.62f, 0.74f, 0.98f, 0.08f, 0.30f, 0f), 0.56f, 0.44f, 0.20f, 0.94f, 0.84f),
            Sample("Extended chest frame", 0.32f, Output(0.74f, 0.56f, 0.66f, 0.86f, 0.06f, 0.14f, 0f), 0.78f, 0.36f, 0.20f, 0.52f, 0.18f),
            Sample("Spread taper load", 0.30f, Output(0.88f, 0.62f, 0.74f, 0.96f, 0.08f, 0.32f, 0f), 0.78f, 0.42f, 0.24f, 0.78f, 0.88f),
            Sample("Upper bridge stabilizer", 0.32f, Output(0.78f, 0.58f, 0.68f, 0.92f, 0.06f, 0.20f, 0f), 0.42f, 0.38f, 0.28f, 0.82f, 0.44f),
            Sample("High-load underarm lattice", 0.28f, Output(0.92f, 0.68f, 0.78f, 1.00f, 0.10f, 0.34f, 0f), 0.82f, 0.70f, 0.54f, 0.96f, 0.92f));

    private static IReadOnlyList<PoseSample> BuildShoulderUpperArmSamples()
        => BuildPoseLibrary(
            AdvancedBodyScalingCorrectiveRegion.ShoulderUpperArm,
            Sample("Neutral", 0.54f, Output(0f, 0f, 0f, 0f, 0f, 0f, 0f), 0f, 0f, 0f, 0f, 0f),
            Sample("Arm raise mild", 0.46f, Output(0.50f, 0.48f, 0.56f, 0.26f, 0.04f, 0.10f, 0f), 0.36f, 0.18f, 0.12f, 0.24f, 0.16f),
            Sample("Arm raise strong", 0.32f, Output(0.90f, 0.78f, 0.82f, 0.40f, 0.10f, 0.20f, 0f), 0.94f, 0.22f, 0.16f, 0.52f, 0.24f),
            Sample("Forward reach mild", 0.44f, Output(0.54f, 0.50f, 0.58f, 0.24f, 0.06f, 0.12f, 0f), 0.42f, 0.12f, 0.18f, 0.34f, 0.20f),
            Sample("Forward reach strong", 0.34f, Output(0.82f, 0.70f, 0.78f, 0.30f, 0.10f, 0.20f, 0f), 0.86f, 0.16f, 0.26f, 0.62f, 0.28f),
            Sample("Arm spread mild", 0.44f, Output(0.48f, 0.44f, 0.52f, 0.20f, 0.04f, 0.10f, 0f), 0.20f, 0.34f, 0.10f, 0.28f, 0.18f),
            Sample("Arm spread strong", 0.34f, Output(0.80f, 0.68f, 0.78f, 0.30f, 0.06f, 0.18f, 0f), 0.24f, 0.78f, 0.14f, 0.52f, 0.24f),
            Sample("Shoulder tension mild", 0.44f, Output(0.52f, 0.46f, 0.54f, 0.22f, 0.02f, 0.10f, 0f), 0.22f, 0.20f, 0.34f, 0.30f, 0.20f),
            Sample("Shoulder tension strong", 0.34f, Output(0.82f, 0.66f, 0.74f, 0.28f, 0.04f, 0.18f, 0f), 0.38f, 0.30f, 0.88f, 0.56f, 0.26f),
            Sample("Bridge continuity mild", 0.38f, Output(0.58f, 0.48f, 0.56f, 0.24f, 0.06f, 0.16f, 0f), 0.28f, 0.22f, 0.22f, 0.46f, 0.30f),
            Sample("Bridge continuity strong", 0.28f, Output(0.84f, 0.66f, 0.74f, 0.30f, 0.08f, 0.30f, 0f), 0.42f, 0.34f, 0.30f, 0.92f, 0.72f),
            Sample("Raise plus spread", 0.30f, Output(0.84f, 0.72f, 0.80f, 0.32f, 0.08f, 0.18f, 0f), 0.70f, 0.68f, 0.22f, 0.60f, 0.28f),
            Sample("Raise plus tension", 0.30f, Output(0.86f, 0.70f, 0.78f, 0.30f, 0.06f, 0.18f, 0f), 0.72f, 0.30f, 0.66f, 0.62f, 0.26f),
            Sample("Spread plus tension", 0.30f, Output(0.84f, 0.68f, 0.76f, 0.28f, 0.04f, 0.18f, 0f), 0.38f, 0.78f, 0.72f, 0.58f, 0.24f),
            Sample("Raise plus taper", 0.28f, Output(0.88f, 0.70f, 0.78f, 0.28f, 0.08f, 0.34f, 0f), 0.82f, 0.22f, 0.18f, 0.64f, 0.78f),
            Sample("Compressed upper-arm bridge", 0.28f, Output(0.86f, 0.66f, 0.78f, 0.24f, 0.10f, 0.32f, 0f), 0.30f, 0.38f, 0.26f, 1.00f, 0.88f),
            Sample("Extended upper-arm bridge", 0.28f, Output(0.74f, 0.60f, 0.70f, 0.20f, 0.06f, 0.14f, 0f), 0.62f, 0.24f, 0.18f, 0.74f, 0.34f),
            Sample("Raised loaded reach", 0.26f, Output(0.92f, 0.76f, 0.82f, 0.36f, 0.10f, 0.24f, 0f), 0.92f, 0.44f, 0.30f, 0.84f, 0.50f),
            Sample("Wide loaded frame", 0.26f, Output(0.88f, 0.70f, 0.80f, 0.30f, 0.08f, 0.22f, 0f), 0.64f, 0.92f, 0.34f, 0.76f, 0.42f),
            Sample("Strong mixed shoulder stress", 0.26f, Output(0.96f, 0.78f, 0.84f, 0.34f, 0.08f, 0.36f, 0f), 0.92f, 0.78f, 0.72f, 0.92f, 0.88f),
            Sample("Forward spread transition", 0.34f, Output(0.72f, 0.60f, 0.68f, 0.24f, 0.06f, 0.14f, 0f), 0.54f, 0.46f, 0.18f, 0.40f, 0.24f),
            Sample("Rear sweep support", 0.34f, Output(0.70f, 0.58f, 0.66f, 0.20f, 0.04f, 0.14f, 0f), 0.28f, 0.22f, 0.22f, 0.46f, 0.22f),
            Sample("Elevated tension bridge", 0.32f, Output(0.82f, 0.68f, 0.76f, 0.28f, 0.06f, 0.18f, 0f), 0.70f, 0.32f, 0.58f, 0.62f, 0.30f),
            Sample("Elevated taper edge", 0.30f, Output(0.86f, 0.68f, 0.76f, 0.28f, 0.08f, 0.30f, 0f), 0.78f, 0.24f, 0.20f, 0.60f, 0.82f),
            Sample("Wide taper arm", 0.30f, Output(0.82f, 0.64f, 0.74f, 0.26f, 0.06f, 0.28f, 0f), 0.42f, 0.74f, 0.22f, 0.68f, 0.76f),
            Sample("Clavicle-led reach", 0.32f, Output(0.78f, 0.64f, 0.72f, 0.26f, 0.06f, 0.18f, 0f), 0.48f, 0.28f, 0.70f, 0.48f, 0.28f),
            Sample("Compression release frame", 0.32f, Output(0.76f, 0.60f, 0.70f, 0.22f, 0.06f, 0.16f, 0f), 0.36f, 0.32f, 0.26f, 0.82f, 0.46f),
            Sample("Bridge-loaded overhead reach", 0.28f, Output(0.92f, 0.74f, 0.82f, 0.34f, 0.10f, 0.24f, 0f), 0.86f, 0.38f, 0.34f, 0.92f, 0.54f),
            Sample("Forward tension strain", 0.30f, Output(0.86f, 0.70f, 0.78f, 0.30f, 0.08f, 0.22f, 0f), 0.72f, 0.26f, 0.72f, 0.72f, 0.34f),
            Sample("Spread continuity brace", 0.30f, Output(0.84f, 0.66f, 0.76f, 0.28f, 0.08f, 0.22f, 0f), 0.40f, 0.70f, 0.28f, 0.88f, 0.40f),
            Sample("Extended loaded shoulder frame", 0.30f, Output(0.82f, 0.68f, 0.76f, 0.24f, 0.08f, 0.20f, 0f), 0.64f, 0.30f, 0.32f, 0.78f, 0.36f),
            Sample("Overhead spread taper strain", 0.28f, Output(0.94f, 0.76f, 0.82f, 0.34f, 0.10f, 0.34f, 0f), 0.90f, 0.62f, 0.30f, 0.88f, 0.90f));

    private static IReadOnlyList<PoseSample> BuildElbowForearmSamples()
        => BuildPoseLibrary(
            AdvancedBodyScalingCorrectiveRegion.ElbowForearm,
            Sample("Neutral", 0.56f, Output(0f, 0f, 0f, 0f, 0f, 0f, 0f), 0f, 0f, 0f, 0f, 0f),
            Sample("Bent elbow mild", 0.46f, Output(0.48f, 0.46f, 0.50f, 0.60f, 0.04f, 0.14f, 0f), 0.24f, 0.34f, 0.28f, 0.16f, 0.32f),
            Sample("Bent elbow strong", 0.32f, Output(0.86f, 0.74f, 0.78f, 0.92f, 0.06f, 0.34f, 0f), 0.36f, 0.82f, 0.58f, 0.22f, 0.92f),
            Sample("Extended forearm mild", 0.48f, Output(0.40f, 0.38f, 0.44f, 0.54f, 0.02f, 0.12f, 0f), 0.18f, 0.22f, 0.26f, 0.14f, 0.18f),
            Sample("Extended forearm strong", 0.38f, Output(0.62f, 0.52f, 0.60f, 0.72f, 0.04f, 0.18f, 0f), 0.28f, 0.42f, 0.40f, 0.18f, 0.46f),
            Sample("Wide-arm elbow mild", 0.44f, Output(0.52f, 0.44f, 0.50f, 0.64f, 0.04f, 0.16f, 0f), 0.26f, 0.28f, 0.32f, 0.38f, 0.30f),
            Sample("Wide-arm elbow strong", 0.32f, Output(0.78f, 0.62f, 0.70f, 0.82f, 0.04f, 0.24f, 0f), 0.42f, 0.44f, 0.52f, 0.88f, 0.62f),
            Sample("Joint stress mild", 0.44f, Output(0.56f, 0.48f, 0.54f, 0.68f, 0.02f, 0.18f, 0f), 0.20f, 0.30f, 0.30f, 0.16f, 0.42f),
            Sample("Joint stress strong", 0.32f, Output(0.82f, 0.66f, 0.72f, 0.88f, 0.04f, 0.28f, 0f), 0.28f, 0.54f, 0.48f, 0.22f, 0.96f),
            Sample("Taper stress mild", 0.42f, Output(0.54f, 0.46f, 0.52f, 0.72f, 0.04f, 0.24f, 0f), 0.18f, 0.46f, 0.28f, 0.12f, 0.24f),
            Sample("Taper stress strong", 0.30f, Output(0.88f, 0.70f, 0.76f, 0.96f, 0.06f, 0.38f, 0f), 0.24f, 0.94f, 0.54f, 0.18f, 0.62f),
            Sample("Bend plus taper", 0.30f, Output(0.84f, 0.68f, 0.74f, 0.90f, 0.06f, 0.32f, 0f), 0.34f, 0.72f, 0.48f, 0.20f, 0.78f),
            Sample("Bend plus spread", 0.30f, Output(0.82f, 0.64f, 0.72f, 0.88f, 0.04f, 0.26f, 0f), 0.40f, 0.52f, 0.46f, 0.66f, 0.68f),
            Sample("Spread plus taper", 0.30f, Output(0.86f, 0.64f, 0.72f, 0.92f, 0.04f, 0.30f, 0f), 0.30f, 0.76f, 0.50f, 0.72f, 0.56f),
            Sample("Compressed elbow bridge", 0.28f, Output(0.90f, 0.70f, 0.78f, 0.98f, 0.08f, 0.40f, 0f), 0.22f, 0.84f, 0.88f, 0.20f, 0.74f),
            Sample("Extended elbow bridge", 0.34f, Output(0.76f, 0.60f, 0.68f, 0.86f, 0.04f, 0.22f, 0f), 0.20f, 0.40f, 0.66f, 0.24f, 0.40f),
            Sample("Loaded forearm fold", 0.28f, Output(0.92f, 0.72f, 0.78f, 0.98f, 0.06f, 0.36f, 0f), 0.44f, 0.82f, 0.62f, 0.30f, 0.92f),
            Sample("Wide loaded elbow", 0.28f, Output(0.90f, 0.70f, 0.76f, 0.98f, 0.06f, 0.34f, 0f), 0.46f, 0.64f, 0.58f, 0.92f, 0.82f),
            Sample("Deep joint fold", 0.26f, Output(0.92f, 0.72f, 0.78f, 0.96f, 0.06f, 0.36f, 0f), 0.30f, 0.88f, 0.72f, 0.24f, 1.00f),
            Sample("Focused taper edge", 0.26f, Output(0.86f, 0.68f, 0.74f, 0.94f, 0.04f, 0.40f, 0f), 0.16f, 1.00f, 0.92f, 0.12f, 0.54f),
            Sample("Bent support transition", 0.36f, Output(0.66f, 0.58f, 0.62f, 0.78f, 0.04f, 0.18f, 0f), 0.30f, 0.46f, 0.34f, 0.22f, 0.48f),
            Sample("Extended support transition", 0.38f, Output(0.58f, 0.50f, 0.56f, 0.72f, 0.02f, 0.16f, 0f), 0.22f, 0.28f, 0.34f, 0.18f, 0.28f),
            Sample("Elevated elbow load", 0.34f, Output(0.72f, 0.60f, 0.68f, 0.82f, 0.04f, 0.20f, 0f), 0.56f, 0.40f, 0.38f, 0.24f, 0.52f),
            Sample("Elevated taper load", 0.32f, Output(0.78f, 0.62f, 0.70f, 0.88f, 0.04f, 0.28f, 0f), 0.48f, 0.72f, 0.42f, 0.20f, 0.62f),
            Sample("Continuity brace fold", 0.32f, Output(0.80f, 0.64f, 0.72f, 0.90f, 0.06f, 0.24f, 0f), 0.30f, 0.58f, 0.44f, 0.54f, 0.70f),
            Sample("Spread-led elbow support", 0.32f, Output(0.76f, 0.60f, 0.68f, 0.84f, 0.04f, 0.22f, 0f), 0.34f, 0.42f, 0.40f, 0.74f, 0.48f),
            Sample("Twisted fold transition", 0.30f, Output(0.82f, 0.64f, 0.72f, 0.88f, 0.04f, 0.24f, 0f), 0.28f, 0.56f, 0.72f, 0.26f, 0.76f),
            Sample("Wide taper brace", 0.30f, Output(0.84f, 0.64f, 0.72f, 0.90f, 0.04f, 0.30f, 0f), 0.26f, 0.82f, 0.50f, 0.78f, 0.62f),
            Sample("High-load elbow lattice", 0.28f, Output(0.90f, 0.70f, 0.78f, 0.96f, 0.06f, 0.34f, 0f), 0.52f, 0.78f, 0.66f, 0.72f, 0.94f),
            Sample("Extended loaded forearm", 0.30f, Output(0.80f, 0.64f, 0.72f, 0.90f, 0.04f, 0.24f, 0f), 0.36f, 0.44f, 0.58f, 0.28f, 0.48f),
            Sample("Bridge-heavy deep fold", 0.28f, Output(0.92f, 0.72f, 0.80f, 1.00f, 0.08f, 0.38f, 0f), 0.34f, 0.86f, 0.82f, 0.46f, 0.96f),
            Sample("Focused support taper fold", 0.28f, Output(0.88f, 0.68f, 0.76f, 0.96f, 0.06f, 0.40f, 0f), 0.22f, 0.92f, 0.76f, 0.18f, 0.72f));

    private static IReadOnlyList<PoseSample> BuildWaistHipsSamples()
        => BuildPoseLibrary(
            AdvancedBodyScalingCorrectiveRegion.WaistHips,
            Sample("Neutral", 0.54f, Output(0f, 0f, 0f, 0f, 0f, 0f, 0f), 0f, 0f, 0f, 0f),
            Sample("Torso twist mild", 0.46f, Output(0.44f, 0.42f, 0.46f, 0f, 0.02f, 0.08f, 0f), 0.34f, 0.10f, 0.26f, 0.18f),
            Sample("Torso twist strong", 0.32f, Output(0.82f, 0.68f, 0.72f, 0f, 0.04f, 0.12f, 0f), 0.92f, 0.18f, 0.54f, 0.22f),
            Sample("Forward bend mild", 0.46f, Output(0.48f, 0.46f, 0.50f, 0f, 0.04f, 0.10f, 0f), 0.12f, 0.34f, 0.28f, 0.24f),
            Sample("Forward bend strong", 0.32f, Output(0.88f, 0.74f, 0.78f, 0f, 0.06f, 0.18f, 0f), 0.18f, 0.94f, 0.58f, 0.40f),
            Sample("Continuity load mild", 0.40f, Output(0.50f, 0.48f, 0.52f, 0f, 0.04f, 0.10f, 0f), 0.22f, 0.18f, 0.44f, 0.26f),
            Sample("Continuity load strong", 0.28f, Output(0.84f, 0.72f, 0.76f, 0f, 0.08f, 0.16f, 0f), 0.30f, 0.24f, 0.98f, 0.48f),
            Sample("Taper load mild", 0.40f, Output(0.52f, 0.50f, 0.54f, 0f, 0.04f, 0.16f, 0f), 0.14f, 0.18f, 0.38f, 0.44f),
            Sample("Taper load strong", 0.28f, Output(0.84f, 0.72f, 0.76f, 0f, 0.08f, 0.30f, 0f), 0.22f, 0.28f, 0.78f, 0.96f),
            Sample("Twist plus bend", 0.34f, Output(0.82f, 0.70f, 0.74f, 0f, 0.06f, 0.16f, 0f), 0.60f, 0.62f, 0.54f, 0.36f),
            Sample("Twist plus continuity", 0.30f, Output(0.82f, 0.68f, 0.72f, 0f, 0.04f, 0.14f, 0f), 0.82f, 0.22f, 0.74f, 0.32f),
            Sample("Bend plus continuity", 0.30f, Output(0.86f, 0.72f, 0.76f, 0f, 0.08f, 0.20f, 0f), 0.28f, 0.82f, 0.74f, 0.46f),
            Sample("Bend plus taper", 0.30f, Output(0.88f, 0.74f, 0.78f, 0f, 0.08f, 0.28f, 0f), 0.22f, 0.78f, 0.62f, 0.82f),
            Sample("Twist plus taper", 0.30f, Output(0.84f, 0.70f, 0.74f, 0f, 0.06f, 0.26f, 0f), 0.74f, 0.24f, 0.68f, 0.72f),
            Sample("Compressed waist bridge", 0.28f, Output(0.90f, 0.76f, 0.80f, 0f, 0.10f, 0.34f, 0f), 0.26f, 0.52f, 0.96f, 0.90f),
            Sample("Extended waist bridge", 0.30f, Output(0.76f, 0.66f, 0.70f, 0f, 0.06f, 0.14f, 0f), 0.52f, 0.24f, 0.70f, 0.34f),
            Sample("Loaded torso fold", 0.28f, Output(0.92f, 0.78f, 0.82f, 0f, 0.10f, 0.26f, 0f), 0.56f, 0.94f, 0.82f, 0.62f),
            Sample("Twisted lower torso frame", 0.28f, Output(0.90f, 0.74f, 0.80f, 0f, 0.06f, 0.22f, 0f), 0.96f, 0.44f, 0.82f, 0.54f),
            Sample("Mixed lower-torso strain", 0.26f, Output(0.94f, 0.78f, 0.82f, 0f, 0.10f, 0.30f, 0f), 0.76f, 0.76f, 0.88f, 0.78f),
            Sample("Edge-case compressed fold", 0.26f, Output(0.96f, 0.80f, 0.84f, 0f, 0.10f, 0.34f, 0f), 0.42f, 1.00f, 1.00f, 0.98f),
            Sample("Supported waist twist", 0.36f, Output(0.68f, 0.58f, 0.62f, 0f, 0.04f, 0.12f, 0f), 0.56f, 0.16f, 0.42f, 0.24f),
            Sample("Supported forward fold", 0.36f, Output(0.70f, 0.60f, 0.64f, 0f, 0.06f, 0.14f, 0f), 0.20f, 0.58f, 0.42f, 0.28f),
            Sample("Bridge transition mild", 0.38f, Output(0.62f, 0.54f, 0.58f, 0f, 0.04f, 0.12f, 0f), 0.24f, 0.20f, 0.56f, 0.36f),
            Sample("Twist plus taper transition", 0.32f, Output(0.80f, 0.68f, 0.72f, 0f, 0.06f, 0.24f, 0f), 0.66f, 0.24f, 0.62f, 0.64f),
            Sample("Bend plus taper transition", 0.32f, Output(0.82f, 0.70f, 0.74f, 0f, 0.08f, 0.24f, 0f), 0.24f, 0.68f, 0.58f, 0.70f),
            Sample("Continuity-driven waist brace", 0.32f, Output(0.82f, 0.68f, 0.72f, 0f, 0.06f, 0.18f, 0f), 0.30f, 0.26f, 0.88f, 0.42f),
            Sample("Extended lower torso frame", 0.34f, Output(0.74f, 0.64f, 0.68f, 0f, 0.04f, 0.14f, 0f), 0.46f, 0.18f, 0.60f, 0.28f),
            Sample("Compressed continuity fold", 0.30f, Output(0.88f, 0.74f, 0.78f, 0f, 0.08f, 0.28f, 0f), 0.28f, 0.56f, 0.96f, 0.88f),
            Sample("Twisted taper brace", 0.30f, Output(0.86f, 0.72f, 0.76f, 0f, 0.06f, 0.28f, 0f), 0.82f, 0.30f, 0.76f, 0.74f),
            Sample("Folded support arc", 0.30f, Output(0.84f, 0.72f, 0.76f, 0f, 0.08f, 0.22f, 0f), 0.44f, 0.84f, 0.68f, 0.52f),
            Sample("Broad lower-torso strain", 0.28f, Output(0.90f, 0.76f, 0.80f, 0f, 0.08f, 0.30f, 0f), 0.72f, 0.66f, 0.90f, 0.86f),
            Sample("Bridge-heavy compressed waist", 0.28f, Output(0.94f, 0.78f, 0.82f, 0f, 0.10f, 0.34f, 0f), 0.34f, 0.72f, 1.00f, 1.00f));

    private static IReadOnlyList<PoseSample> BuildHipUpperThighSamples()
        => BuildPoseLibrary(
            AdvancedBodyScalingCorrectiveRegion.HipUpperThigh,
            Sample("Neutral", 0.54f, Output(0f, 0f, 0f, 0f, 0f, 0f, 0f), 0f, 0f, 0f, 0f, 0f),
            Sample("Hip flexion mild", 0.46f, Output(0.50f, 0.48f, 0.56f, 0f, 0.08f, 0.12f, 0f), 0.36f, 0.12f, 0.08f, 0.28f, 0.18f),
            Sample("Hip flexion strong", 0.32f, Output(0.90f, 0.74f, 0.82f, 0f, 0.12f, 0.20f, 0f), 0.94f, 0.18f, 0.12f, 0.54f, 0.26f),
            Sample("Stride mild", 0.44f, Output(0.54f, 0.50f, 0.58f, 0f, 0.10f, 0.14f, 0f), 0.42f, 0.10f, 0.10f, 0.36f, 0.24f),
            Sample("Stride strong", 0.34f, Output(0.84f, 0.68f, 0.78f, 0f, 0.14f, 0.20f, 0f), 0.84f, 0.14f, 0.14f, 0.68f, 0.32f),
            Sample("Abduction mild", 0.44f, Output(0.52f, 0.48f, 0.56f, 0f, 0.08f, 0.12f, 0f), 0.26f, 0.12f, 0.16f, 0.44f, 0.22f),
            Sample("Abduction strong", 0.34f, Output(0.80f, 0.64f, 0.74f, 0f, 0.12f, 0.22f, 0f), 0.48f, 0.18f, 0.20f, 0.82f, 0.34f),
            Sample("Torso bend contribution", 0.40f, Output(0.60f, 0.56f, 0.62f, 0f, 0.08f, 0.16f, 0f), 0.30f, 0.48f, 0.12f, 0.46f, 0.28f),
            Sample("Torso twist contribution", 0.40f, Output(0.58f, 0.54f, 0.60f, 0f, 0.06f, 0.14f, 0f), 0.28f, 0.18f, 0.46f, 0.42f, 0.24f),
            Sample("Pelvis tilt mild", 0.42f, Output(0.56f, 0.52f, 0.58f, 0f, 0.08f, 0.16f, 0f), 0.22f, 0.34f, 0.18f, 0.38f, 0.24f),
            Sample("Pelvis tilt strong", 0.32f, Output(0.80f, 0.68f, 0.74f, 0f, 0.10f, 0.20f, 0f), 0.34f, 0.70f, 0.22f, 0.62f, 0.32f),
            Sample("Flexion plus twist", 0.30f, Output(0.84f, 0.70f, 0.76f, 0f, 0.10f, 0.18f, 0f), 0.72f, 0.20f, 0.60f, 0.60f, 0.30f),
            Sample("Flexion plus stride", 0.30f, Output(0.88f, 0.72f, 0.80f, 0f, 0.12f, 0.22f, 0f), 0.82f, 0.16f, 0.18f, 0.74f, 0.34f),
            Sample("Stride plus pelvis tilt", 0.30f, Output(0.88f, 0.72f, 0.80f, 0f, 0.12f, 0.24f, 0f), 0.64f, 0.54f, 0.20f, 0.72f, 0.36f),
            Sample("Bend plus flexion", 0.28f, Output(0.92f, 0.76f, 0.84f, 0f, 0.14f, 0.30f, 0f), 0.76f, 0.82f, 0.16f, 0.78f, 0.40f),
            Sample("Compressed hip bridge", 0.28f, Output(0.92f, 0.74f, 0.86f, 0f, 0.16f, 0.34f, 0f), 0.30f, 0.42f, 0.18f, 1.00f, 0.92f),
            Sample("Extended hip bridge", 0.30f, Output(0.78f, 0.64f, 0.74f, 0f, 0.10f, 0.16f, 0f), 0.58f, 0.16f, 0.18f, 0.72f, 0.34f),
            Sample("Loaded upper thigh frame", 0.28f, Output(0.92f, 0.76f, 0.84f, 0f, 0.12f, 0.26f, 0f), 0.88f, 0.40f, 0.26f, 0.84f, 0.44f),
            Sample("Mixed lower-body stress", 0.26f, Output(0.96f, 0.80f, 0.88f, 0f, 0.16f, 0.34f, 0f), 0.92f, 0.72f, 0.44f, 0.90f, 0.78f),
            Sample("Edge-case loaded fold", 0.26f, Output(0.98f, 0.82f, 0.88f, 0f, 0.16f, 0.34f, 0f), 1.00f, 0.92f, 0.22f, 1.00f, 0.96f),
            Sample("Flexion support transition", 0.36f, Output(0.68f, 0.60f, 0.68f, 0f, 0.08f, 0.14f, 0f), 0.52f, 0.20f, 0.10f, 0.36f, 0.20f),
            Sample("Stride support transition", 0.36f, Output(0.72f, 0.62f, 0.70f, 0f, 0.10f, 0.16f, 0f), 0.60f, 0.18f, 0.12f, 0.48f, 0.24f),
            Sample("Twist-supported hip frame", 0.34f, Output(0.72f, 0.62f, 0.68f, 0f, 0.08f, 0.16f, 0f), 0.34f, 0.22f, 0.54f, 0.46f, 0.24f),
            Sample("Bend-supported hip frame", 0.34f, Output(0.76f, 0.66f, 0.72f, 0f, 0.10f, 0.18f, 0f), 0.36f, 0.62f, 0.16f, 0.48f, 0.28f),
            Sample("Flexion plus taper transition", 0.32f, Output(0.82f, 0.68f, 0.76f, 0f, 0.12f, 0.26f, 0f), 0.72f, 0.20f, 0.14f, 0.54f, 0.72f),
            Sample("Stride plus taper transition", 0.32f, Output(0.82f, 0.68f, 0.76f, 0f, 0.12f, 0.28f, 0f), 0.64f, 0.18f, 0.16f, 0.72f, 0.76f),
            Sample("Continuity-loaded hip brace", 0.32f, Output(0.84f, 0.70f, 0.78f, 0f, 0.12f, 0.22f, 0f), 0.40f, 0.30f, 0.24f, 0.88f, 0.44f),
            Sample("Extended stride frame", 0.32f, Output(0.78f, 0.66f, 0.74f, 0f, 0.10f, 0.18f, 0f), 0.72f, 0.18f, 0.18f, 0.58f, 0.28f),
            Sample("Compressed flexion brace", 0.30f, Output(0.88f, 0.72f, 0.82f, 0f, 0.14f, 0.30f, 0f), 0.52f, 0.46f, 0.18f, 0.96f, 0.88f),
            Sample("Twist plus stride lattice", 0.30f, Output(0.88f, 0.72f, 0.80f, 0f, 0.12f, 0.24f, 0f), 0.70f, 0.20f, 0.62f, 0.78f, 0.36f),
            Sample("Folded hip support arc", 0.30f, Output(0.90f, 0.74f, 0.82f, 0f, 0.14f, 0.28f, 0f), 0.66f, 0.70f, 0.20f, 0.74f, 0.42f),
            Sample("High-load lower-body lattice", 0.28f, Output(0.94f, 0.78f, 0.86f, 0f, 0.16f, 0.34f, 0f), 0.88f, 0.56f, 0.40f, 0.96f, 0.92f));

    private static IReadOnlyList<PoseSample> BuildThighKneeCalfSamples()
        => BuildPoseLibrary(
            AdvancedBodyScalingCorrectiveRegion.ThighKneeCalf,
            Sample("Neutral", 0.56f, Output(0f, 0f, 0f, 0f, 0f, 0f, 0f), 0f, 0f, 0f, 0f, 0f),
            Sample("Knee bend mild", 0.46f, Output(0.48f, 0.46f, 0.50f, 0.60f, 0.04f, 0.14f, 0f), 0.18f, 0.34f, 0.22f, 0.26f, 0.18f),
            Sample("Knee bend strong", 0.32f, Output(0.86f, 0.78f, 0.82f, 0.90f, 0.06f, 0.20f, 0f), 0.26f, 0.92f, 0.46f, 0.58f, 0.26f),
            Sample("Stride mild", 0.44f, Output(0.52f, 0.50f, 0.54f, 0.64f, 0.04f, 0.16f, 0f), 0.34f, 0.28f, 0.30f, 0.34f, 0.16f),
            Sample("Stride strong", 0.34f, Output(0.80f, 0.70f, 0.76f, 0.86f, 0.06f, 0.20f, 0f), 0.72f, 0.44f, 0.46f, 0.62f, 0.22f),
            Sample("Calf taper mild", 0.44f, Output(0.50f, 0.46f, 0.52f, 0.66f, 0.04f, 0.20f, 0f), 0.12f, 0.22f, 0.46f, 0.30f, 0.12f),
            Sample("Calf taper strong", 0.30f, Output(0.84f, 0.70f, 0.76f, 0.92f, 0.04f, 0.34f, 0f), 0.20f, 0.32f, 0.96f, 0.58f, 0.20f),
            Sample("Squat mild", 0.40f, Output(0.58f, 0.56f, 0.60f, 0.72f, 0.04f, 0.18f, 0f), 0.28f, 0.46f, 0.34f, 0.42f, 0.34f),
            Sample("Squat strong", 0.30f, Output(0.90f, 0.78f, 0.84f, 0.96f, 0.06f, 0.26f, 0f), 0.42f, 0.84f, 0.52f, 0.68f, 0.82f),
            Sample("Bend plus stride", 0.34f, Output(0.82f, 0.70f, 0.76f, 0.86f, 0.04f, 0.20f, 0f), 0.56f, 0.48f, 0.40f, 0.58f, 0.52f),
            Sample("Knee bend plus continuity", 0.30f, Output(0.88f, 0.76f, 0.82f, 0.94f, 0.06f, 0.24f, 0f), 0.24f, 0.78f, 0.42f, 0.84f, 0.30f),
            Sample("Knee bend plus taper", 0.30f, Output(0.90f, 0.74f, 0.80f, 0.96f, 0.06f, 0.32f, 0f), 0.22f, 0.72f, 0.82f, 0.66f, 0.28f),
            Sample("Stride plus taper", 0.30f, Output(0.86f, 0.70f, 0.76f, 0.92f, 0.04f, 0.30f, 0f), 0.62f, 0.40f, 0.72f, 0.60f, 0.20f),
            Sample("Compressed knee bridge", 0.28f, Output(0.92f, 0.76f, 0.82f, 0.98f, 0.06f, 0.36f, 0f), 0.22f, 0.72f, 0.88f, 1.00f, 0.36f),
            Sample("Extended knee bridge", 0.34f, Output(0.76f, 0.64f, 0.70f, 0.88f, 0.04f, 0.20f, 0f), 0.36f, 0.36f, 0.54f, 0.74f, 0.18f),
            Sample("Strong locomotion stress", 0.30f, Output(0.92f, 0.76f, 0.82f, 0.96f, 0.06f, 0.24f, 0f), 0.74f, 0.58f, 0.56f, 0.70f, 0.28f),
            Sample("Deep loaded bend", 0.28f, Output(0.94f, 0.78f, 0.84f, 0.98f, 0.06f, 0.30f, 0f), 0.38f, 0.94f, 0.68f, 0.82f, 0.94f),
            Sample("Long stride loaded", 0.28f, Output(0.90f, 0.76f, 0.82f, 0.94f, 0.06f, 0.26f, 0f), 0.88f, 0.52f, 0.62f, 0.78f, 0.32f),
            Sample("Mixed leg strain", 0.26f, Output(0.96f, 0.80f, 0.86f, 0.98f, 0.06f, 0.34f, 0f), 0.72f, 0.82f, 0.82f, 0.88f, 0.76f),
            Sample("Edge-case combined leg stress", 0.26f, Output(0.98f, 0.82f, 0.88f, 1.00f, 0.08f, 0.38f, 0f), 0.94f, 1.00f, 0.96f, 1.00f, 0.98f),
            Sample("Supported knee bend", 0.36f, Output(0.68f, 0.60f, 0.66f, 0.78f, 0.04f, 0.16f, 0f), 0.22f, 0.54f, 0.26f, 0.34f, 0.22f),
            Sample("Supported stride frame", 0.36f, Output(0.70f, 0.62f, 0.66f, 0.80f, 0.04f, 0.18f, 0f), 0.52f, 0.34f, 0.30f, 0.44f, 0.20f),
            Sample("Supported taper frame", 0.36f, Output(0.68f, 0.58f, 0.64f, 0.82f, 0.04f, 0.22f, 0f), 0.18f, 0.28f, 0.62f, 0.38f, 0.18f),
            Sample("Stride plus continuity brace", 0.32f, Output(0.82f, 0.68f, 0.74f, 0.90f, 0.04f, 0.22f, 0f), 0.64f, 0.42f, 0.40f, 0.84f, 0.24f),
            Sample("Bend plus taper brace", 0.32f, Output(0.84f, 0.70f, 0.76f, 0.92f, 0.06f, 0.30f, 0f), 0.26f, 0.70f, 0.72f, 0.56f, 0.32f),
            Sample("Extended locomotion frame", 0.34f, Output(0.76f, 0.64f, 0.70f, 0.88f, 0.04f, 0.18f, 0f), 0.46f, 0.30f, 0.46f, 0.54f, 0.16f),
            Sample("Compressed squat bridge", 0.30f, Output(0.90f, 0.74f, 0.80f, 0.98f, 0.06f, 0.32f, 0f), 0.30f, 0.82f, 0.62f, 0.96f, 0.82f),
            Sample("Forward-loaded stride", 0.30f, Output(0.88f, 0.74f, 0.80f, 0.94f, 0.06f, 0.24f, 0f), 0.72f, 0.48f, 0.42f, 0.66f, 0.56f),
            Sample("Taper-heavy locomotion load", 0.30f, Output(0.88f, 0.72f, 0.78f, 0.94f, 0.04f, 0.34f, 0f), 0.34f, 0.44f, 0.88f, 0.64f, 0.30f),
            Sample("Deep folded locomotion brace", 0.28f, Output(0.92f, 0.76f, 0.82f, 0.98f, 0.06f, 0.30f, 0f), 0.42f, 0.88f, 0.60f, 0.74f, 0.88f),
            Sample("Bridge-heavy long stride", 0.28f, Output(0.92f, 0.76f, 0.82f, 1.00f, 0.06f, 0.28f, 0f), 0.90f, 0.54f, 0.58f, 0.96f, 0.38f),
            Sample("High-load mixed leg lattice", 0.28f, Output(0.96f, 0.80f, 0.86f, 1.00f, 0.08f, 0.36f, 0f), 0.82f, 0.88f, 0.84f, 0.94f, 0.90f));

    private static IReadOnlyList<PoseSample> BuildPoseLibrary(AdvancedBodyScalingCorrectiveRegion region, params PoseSample[] samples)
    {
        if (samples.Length != RequiredSampleCountPerRegion)
            throw new InvalidOperationException($"{region} must define exactly {RequiredSampleCountPerRegion} built-in pose samples.");

        return samples;
    }

    private static PoseSampleOutput Output(
        float activation,
        float groupABlend,
        float groupBBlend,
        float bridgeBlend,
        float targetBias,
        float taperBlend,
        float axisBias)
        => new(activation, groupABlend, groupBBlend, bridgeBlend, targetBias, taperBlend, axisBias);

    private static PoseSample Sample(string name, float radius, PoseSampleOutput output, params float[] key)
        => new(name, key, output, radius, name);

    private static PoseSample Sample(string name, string summary, float radius, PoseSampleOutput output, params float[] key)
        => new(name, key, output, radius, summary);
}
