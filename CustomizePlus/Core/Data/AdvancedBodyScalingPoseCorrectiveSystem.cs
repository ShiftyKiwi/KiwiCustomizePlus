// Copyright (c) Customize+.
// Licensed under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using CustomizePlus.Armatures.Data;
using CustomizePlus.Core.Extensions;
using FFXIVClientStructs.FFXIV.Client.Graphics.Scene;

namespace CustomizePlus.Core.Data;

internal static unsafe class AdvancedBodyScalingPoseCorrectiveSystem
{
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
        IReadOnlyList<DriverCondition> Drivers);

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

    private static readonly CorrectiveDefinition[] Definitions =
    {
        new(
            AdvancedBodyScalingCorrectiveRegion.NeckShoulder,
            "Neck / Shoulder",
            "Reduces detached-shoulder and pillar-neck transition issues when the neck and shoulder frame are stressed.",
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
            }),
        new(
            AdvancedBodyScalingCorrectiveRegion.ClavicleUpperChest,
            "Clavicle / Upper Chest",
            "Softens harsh clavicle-to-chest bridges when shoulder spread and upper torso tension pull the region apart.",
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
            }),
        new(
            AdvancedBodyScalingCorrectiveRegion.ShoulderUpperArm,
            "Shoulder / Upper Arm",
            "Reduces sharp shoulder-to-upper-arm discontinuity when raised or spread arms expose the bridge line.",
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
            }),
        new(
            AdvancedBodyScalingCorrectiveRegion.ElbowForearm,
            "Elbow / Forearm",
            "Softens abrupt upper-arm to forearm taper so elbows hold a more continuous silhouette in motion.",
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
            }),
        new(
            AdvancedBodyScalingCorrectiveRegion.WaistHips,
            "Waist / Hips",
            "Reduces abrupt waist-to-hip transition when bends and twists load the lower torso.",
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
            }),
        new(
            AdvancedBodyScalingCorrectiveRegion.HipUpperThigh,
            "Hip / Upper Thigh",
            "Reduces pelvis-to-thigh mass jumps when legs raise, stride, or fold deeply.",
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
            }),
        new(
            AdvancedBodyScalingCorrectiveRegion.ThighKneeCalf,
            "Thigh / Knee / Calf",
            "Softens harsh thigh-to-calf taper so knees and lower legs read more continuously in stressed poses.",
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
            }),
    };

    private static readonly IReadOnlyDictionary<AdvancedBodyScalingCorrectiveRegion, CorrectiveDefinition> DefinitionMap =
        Definitions.ToDictionary(definition => definition.Region);

    public static IReadOnlyList<AdvancedBodyScalingCorrectiveRegion> GetOrderedRegions()
        => Definitions.Select(definition => definition.Region).ToArray();

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
            _ => "No supported corrective morph path was found in the current plugin/runtime, so Customize+ is using a conservative corrective-transform fallback on supported bones only.",
        };

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
        debugState.Reset(path, GetPathDescription(path), profileOverridesActive);
        scaleMultipliers.Clear();

        if (cBase == null || !settings.Enabled || settings.Mode == AdvancedBodyScalingMode.Manual)
        {
            activationState.Clear();
            return;
        }

        var correctiveSettings = settings.PoseCorrectives;
        if (!correctiveSettings.Enabled || correctiveSettings.Strength <= 0f)
        {
            activationState.Clear();
            return;
        }

        var previousState = activationState.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
        activationState.Clear();
        foreach (var definition in Definitions)
        {
            var regionSettings = correctiveSettings.GetRegionSettings(definition.Region);
            if (!regionSettings.Enabled || regionSettings.Strength <= 0f)
                continue;

            var signals = BuildSignals(armature.GetAppliedBoneTransform, definition);
            var samples = definition.Drivers
                .Select(driver => new DriverSample(driver.Type, EvaluateLiveDriver(armature, cBase, definition, driver.Type, signals)))
                .ToList();
            var driverStrength = WeightedAverage(samples, definition.Drivers);
            var previousActivation = previousState.TryGetValue(definition.Region, out var cached) ? cached : 0f;
            var activation = ComputeActivation(driverStrength, previousActivation, regionSettings);
            if (activation <= 0.001f)
                continue;

            activationState[definition.Region] = activation;
            var tuningFactor = GetRegionTuningFactor(settings, definition.RelatedRegions);
            var correctionStrength = ComputeCorrectionStrength(settings, regionSettings, activation, tuningFactor);
            if (correctionStrength <= 0.005f)
                continue;

            ApplyDefinitionCorrection(scaleMultipliers, armature, definition, regionSettings, correctionStrength);
            ApplySpecialBias(scaleMultipliers, armature, definition.Region, regionSettings, correctionStrength);

            debugState.ActiveRegions.Add(new AdvancedBodyScalingCorrectiveDebugRegionState
            {
                Region = definition.Region,
                Label = definition.Label,
                DriverStrength = driverStrength,
                Discontinuity = signals.Discontinuity,
                Activation = activation,
                Strength = correctionStrength,
                EstimatedRiskReduction = EstimateRiskReductionFraction(regionSettings, correctionStrength),
                DriverSummary = BuildDriverSummary(samples),
                Description = definition.Description,
            });
        }
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
            if (!regionSettings.Enabled || regionSettings.Strength <= 0f)
                continue;

            var signals = BuildSignals(Resolver, definition);
            var staticPoseWeight = poseWeights != null && poseWeights.TryGetValue(definition.Region, out var weight)
                ? Math.Clamp(weight, 0f, 1f)
                : GetPassivePoseWeight(signals);

            var samples = definition.Drivers
                .Select(driver => new DriverSample(driver.Type, EvaluateStaticDriver(driver.Type, staticPoseWeight, signals)))
                .ToList();
            var driverStrength = WeightedAverage(samples, definition.Drivers);
            var activation = ComputeActivation(driverStrength, 0f, regionSettings, useDeadzone: false);
            if (activation <= 0.001f)
                continue;

            var tuningFactor = GetRegionTuningFactor(settings, definition.RelatedRegions);
            var correctionStrength = ComputeCorrectionStrength(settings, regionSettings, activation, tuningFactor);
            if (correctionStrength <= 0.005f)
                continue;

            estimates.Add(new AdvancedBodyScalingCorrectiveDebugRegionState
            {
                Region = definition.Region,
                Label = definition.Label,
                DriverStrength = driverStrength,
                Discontinuity = signals.Discontinuity,
                Activation = activation,
                Strength = correctionStrength,
                EstimatedRiskReduction = EstimateRiskReductionFraction(regionSettings, correctionStrength),
                DriverSummary = BuildDriverSummary(samples),
                Description = definition.Description,
            });
        }

        return estimates
            .OrderByDescending(entry => entry.Strength)
            .ToList();
    }

    private static bool HasSupportedMorphPath()
        => false;

    private static float EvaluateLiveDriver(
        Armature armature,
        CharacterBase* cBase,
        CorrectiveDefinition definition,
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
            _ => poseWeight,
        };

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
        float driverStrength,
        float previousActivation,
        AdvancedBodyScalingCorrectiveRegionSettings regionSettings,
        bool useDeadzone = true)
    {
        if (driverStrength <= regionSettings.ActivationThreshold)
            return previousActivation > 0f ? previousActivation * (1f - Math.Clamp(1f - regionSettings.Smoothing, 0.18f, 1f)) : 0f;

        if (useDeadzone && previousActivation <= 0.001f && driverStrength < regionSettings.ActivationThreshold + regionSettings.ActivationDeadzone)
            return 0f;

        var target = Remap(driverStrength, regionSettings.ActivationThreshold, 1f);
        var response = Math.Clamp(1f - regionSettings.Smoothing, 0.18f, 1f);
        var smoothed = previousActivation + ((target - previousActivation) * response);
        return smoothed < 0.003f ? 0f : Math.Clamp(smoothed, 0f, 1f);
    }

    private static float ComputeCorrectionStrength(
        AdvancedBodyScalingSettings settings,
        AdvancedBodyScalingCorrectiveRegionSettings regionSettings,
        float activation,
        float tuningFactor)
    {
        var correctionStrength = activation * settings.PoseCorrectives.Strength * regionSettings.Strength * regionSettings.Priority * tuningFactor;
        if (settings.AnimationSafeModeEnabled)
            correctionStrength = MathF.Min(correctionStrength, 0.82f);

        return Math.Clamp(correctionStrength, 0f, 1f);
    }

    private static float EstimateRiskReductionFraction(
        AdvancedBodyScalingCorrectiveRegionSettings regionSettings,
        float correctionStrength)
        => Math.Clamp(correctionStrength * (0.22f + (regionSettings.MaxCorrection * 4.0f)), 0f, 0.35f);

    private static float GetPassivePoseWeight(CorrectiveSignals signals)
        => Math.Clamp((signals.ContinuityStress * 0.70f) + (signals.TaperStress * 0.30f), 0.15f, 1f);

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

    private static void ApplyDefinitionCorrection(
        Dictionary<string, Vector3> scaleMultipliers,
        Armature armature,
        CorrectiveDefinition definition,
        AdvancedBodyScalingCorrectiveRegionSettings regionSettings,
        float correctionStrength)
    {
        var avgA = AverageCurrentScale(armature.GetAppliedBoneTransform, definition.GroupA);
        var avgB = AverageCurrentScale(armature.GetAppliedBoneTransform, definition.GroupB);
        var target = (avgA + avgB) * 0.5f;
        var blendStrength = correctionStrength * regionSettings.MaxCorrection;

        ApplyUniformTargetCorrection(scaleMultipliers, armature, definition.GroupA, target, blendStrength, regionSettings.MaxCorrection);
        ApplyUniformTargetCorrection(scaleMultipliers, armature, definition.GroupB, target, blendStrength, regionSettings.MaxCorrection);
        ApplyUniformTargetCorrection(scaleMultipliers, armature, definition.BridgeBones, target, blendStrength * regionSettings.Falloff, regionSettings.MaxCorrection * regionSettings.Falloff);
    }

    private static void ApplySpecialBias(
        Dictionary<string, Vector3> scaleMultipliers,
        Armature armature,
        AdvancedBodyScalingCorrectiveRegion region,
        AdvancedBodyScalingCorrectiveRegionSettings regionSettings,
        float correctionStrength)
    {
        if (region == AdvancedBodyScalingCorrectiveRegion.NeckShoulder)
            ApplyNeckAxisBias(scaleMultipliers, armature, correctionStrength, regionSettings.MaxCorrection);
    }

    private static void ApplyNeckAxisBias(
        Dictionary<string, Vector3> scaleMultipliers,
        Armature armature,
        float correctionStrength,
        float maxCorrection)
    {
        var neck = armature.GetAppliedBoneTransform("j_kubi");
        if (neck == null || neck.LockState != BoneLockState.Unlocked)
            return;

        var axisFactor = Math.Clamp(correctionStrength * (0.60f + (maxCorrection * 6f)), 0f, 0.05f);
        var multiplier = new Vector3(
            1f + (axisFactor * 0.25f),
            1f - axisFactor,
            1f + (axisFactor * 0.25f));
        var targetScale = neck.ApplyScalePins(neck.Scaling * multiplier);
        AddMultiplier(scaleMultipliers, "j_kubi", new Vector3(
            SafeDivide(targetScale.X, neck.Scaling.X),
            SafeDivide(targetScale.Y, neck.Scaling.Y),
            SafeDivide(targetScale.Z, neck.Scaling.Z)));
    }
    private static void ApplyUniformTargetCorrection(
        Dictionary<string, Vector3> scaleMultipliers,
        Armature armature,
        IReadOnlyList<string> bones,
        float targetUniform,
        float blendStrength,
        float maxAdjustment)
    {
        foreach (var boneName in bones)
        {
            var transform = armature.GetAppliedBoneTransform(boneName);
            if (transform == null || transform.LockState != BoneLockState.Unlocked)
                continue;

            var currentUniform = AdvancedBodyScalingPipeline.GetUniformScale(transform.Scaling);
            if (currentUniform <= 0.0001f)
                continue;

            var blended = Lerp(currentUniform, targetUniform, blendStrength);
            var delta = Math.Clamp(blended - currentUniform, -maxAdjustment, maxAdjustment);
            if (MathF.Abs(delta) <= 0.0005f)
                continue;

            var targetScale = transform.Scaling * ((currentUniform + delta) / currentUniform);
            targetScale = transform.ApplyScalePins(targetScale);
            AddMultiplier(scaleMultipliers, boneName, new Vector3(
                SafeDivide(targetScale.X, transform.Scaling.X),
                SafeDivide(targetScale.Y, transform.Scaling.Y),
                SafeDivide(targetScale.Z, transform.Scaling.Z)));
        }
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
}
