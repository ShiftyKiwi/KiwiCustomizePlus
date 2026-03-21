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
    private readonly record struct DriverCondition(AdvancedBodyScalingCorrectiveDriverType Type, float Weight);

    private sealed record CorrectiveDefinition(
        AdvancedBodyScalingCorrectiveRegion Region,
        string Label,
        string Description,
        float DiscontinuityStart,
        float DiscontinuityFull,
        float MaxAdjustment,
        IReadOnlyList<string> GroupA,
        IReadOnlyList<string> GroupB,
        IReadOnlyList<string> BridgeBones,
        IReadOnlyList<DriverCondition> Drivers);

    private enum AdvancedBodyScalingCorrectiveDriverType
    {
        ShoulderRaise = 0,
        ClavicleTension = 1,
        NeckStress = 2,
        HipFlexion = 3,
        TorsoTwist = 4,
    }

    private static readonly CorrectiveDefinition[] Definitions =
    {
        new(
            AdvancedBodyScalingCorrectiveRegion.NeckShoulder,
            "Neck / Shoulder",
            "Reduces detached-shoulder and pillar-neck transitions in stressed poses.",
            0.08f,
            0.28f,
            0.035f,
            new[] { "j_kubi" },
            new[] { "j_sako_l", "j_sako_r", "n_hkata_l", "n_hkata_r" },
            new[] { "j_sebo_c" },
            new[]
            {
                new DriverCondition(AdvancedBodyScalingCorrectiveDriverType.ShoulderRaise, 0.45f),
                new DriverCondition(AdvancedBodyScalingCorrectiveDriverType.NeckStress, 0.35f),
                new DriverCondition(AdvancedBodyScalingCorrectiveDriverType.TorsoTwist, 0.20f),
            }),
        new(
            AdvancedBodyScalingCorrectiveRegion.ClavicleUpperChest,
            "Clavicle / Upper Chest",
            "Softens harsh clavicle-to-chest bridging under shoulder spread and torso tension.",
            0.07f,
            0.25f,
            0.03f,
            new[] { "j_sako_l", "j_sako_r", "n_hkata_l", "n_hkata_r" },
            new[] { "j_sebo_b", "j_sebo_c", "j_mune_l", "j_mune_r" },
            Array.Empty<string>(),
            new[]
            {
                new DriverCondition(AdvancedBodyScalingCorrectiveDriverType.ClavicleTension, 0.50f),
                new DriverCondition(AdvancedBodyScalingCorrectiveDriverType.ShoulderRaise, 0.25f),
                new DriverCondition(AdvancedBodyScalingCorrectiveDriverType.TorsoTwist, 0.25f),
            }),
        new(
            AdvancedBodyScalingCorrectiveRegion.HipUpperThigh,
            "Hip / Upper Thigh",
            "Reduces abrupt pelvis-to-thigh mass transition in flexed or twisted leg poses.",
            0.07f,
            0.24f,
            0.03f,
            new[] { "iv_shiri_l", "iv_shiri_r", "ya_shiri_phys_l", "ya_shiri_phys_r", "j_kosi" },
            new[] { "j_asi_a_l", "j_asi_a_r", "j_asi_b_l", "j_asi_b_r" },
            Array.Empty<string>(),
            new[]
            {
                new DriverCondition(AdvancedBodyScalingCorrectiveDriverType.HipFlexion, 0.70f),
                new DriverCondition(AdvancedBodyScalingCorrectiveDriverType.TorsoTwist, 0.30f),
            }),
    };

    public static AdvancedBodyScalingCorrectivePath DetectSupportedPath()
        => HasSupportedMorphPath() ? AdvancedBodyScalingCorrectivePath.SupportedMorph : AdvancedBodyScalingCorrectivePath.TransformFallback;

    public static string GetPathDescription(AdvancedBodyScalingCorrectivePath path)
        => path switch
        {
            AdvancedBodyScalingCorrectivePath.SupportedMorph => "Using an existing supported corrective morph path.",
            _ => "No supported corrective morph path was found in the current plugin/runtime, so Customize+ is using a limited corrective-transform fallback on supported bones only.",
        };

    public static void Evaluate(
        Armature armature,
        CharacterBase* cBase,
        AdvancedBodyScalingSettings settings,
        Dictionary<string, Vector3> scaleMultipliers,
        AdvancedBodyScalingPoseCorrectiveDebugState debugState)
    {
        var path = DetectSupportedPath();
        debugState.Reset(path, GetPathDescription(path));
        scaleMultipliers.Clear();

        if (cBase == null || !settings.Enabled || settings.Mode == AdvancedBodyScalingMode.Manual)
            return;

        var correctiveSettings = settings.PoseCorrectives;
        if (!correctiveSettings.Enabled || correctiveSettings.Strength <= 0f)
            return;

        foreach (var definition in Definitions)
        {
            var regionSettings = correctiveSettings.GetRegionSettings(definition.Region);
            if (!regionSettings.Enabled || regionSettings.Strength <= 0f)
                continue;

            var driverStrength = EvaluateDrivers(armature, cBase, definition.Drivers);
            if (driverStrength <= 0.001f)
                continue;

            var discontinuity = EvaluateDiscontinuity(armature, definition);
            var discontinuityStrength = Remap(discontinuity, definition.DiscontinuityStart, definition.DiscontinuityFull);
            if (discontinuityStrength <= 0f)
                continue;

            var activation = driverStrength * Lerp(0.35f, 1f, discontinuityStrength) * correctiveSettings.Strength * regionSettings.Strength;
            if (settings.AnimationSafeModeEnabled)
                activation = MathF.Min(activation, 0.85f);

            if (activation <= 0.01f)
                continue;

            ApplyBridgeCorrection(scaleMultipliers, armature, definition, activation);
            if (definition.Region == AdvancedBodyScalingCorrectiveRegion.NeckShoulder)
                ApplyNeckAxisBias(scaleMultipliers, armature, activation);

            debugState.ActiveRegions.Add(new AdvancedBodyScalingCorrectiveDebugRegionState
            {
                Region = definition.Region,
                Label = definition.Label,
                Strength = activation,
                DriverSummary = BuildDriverSummary(armature, cBase, definition.Drivers),
                Description = definition.Description,
            });
        }
    }

    private static bool HasSupportedMorphPath()
        => false;

    private static float EvaluateDrivers(Armature armature, CharacterBase* cBase, IReadOnlyList<DriverCondition> drivers)
    {
        if (drivers.Count == 0)
            return 0f;

        var totalWeight = 0f;
        var weighted = 0f;
        foreach (var driver in drivers)
        {
            var strength = EvaluateDriver(armature, cBase, driver.Type);
            weighted += strength * driver.Weight;
            totalWeight += driver.Weight;
        }

        if (totalWeight <= 0f)
            return 0f;

        return Math.Clamp(weighted / totalWeight, 0f, 1f);
    }

    private static string BuildDriverSummary(Armature armature, CharacterBase* cBase, IReadOnlyList<DriverCondition> drivers)
        => string.Join(", ",
            drivers
                .Select(driver => (driver.Type, Strength: EvaluateDriver(armature, cBase, driver.Type)))
                .Where(entry => entry.Strength > 0.05f)
                .OrderByDescending(entry => entry.Strength)
                .Select(entry => $"{GetDriverLabel(entry.Type)} {entry.Strength:0.00}")
                .Take(3));

    private static string GetDriverLabel(AdvancedBodyScalingCorrectiveDriverType type)
        => type switch
        {
            AdvancedBodyScalingCorrectiveDriverType.ShoulderRaise => "shoulder raise",
            AdvancedBodyScalingCorrectiveDriverType.ClavicleTension => "clavicle tension",
            AdvancedBodyScalingCorrectiveDriverType.NeckStress => "neck stress",
            AdvancedBodyScalingCorrectiveDriverType.HipFlexion => "hip flexion",
            AdvancedBodyScalingCorrectiveDriverType.TorsoTwist => "torso twist",
            _ => type.ToString(),
        };

    private static float EvaluateDriver(Armature armature, CharacterBase* cBase, AdvancedBodyScalingCorrectiveDriverType type)
        => type switch
        {
            AdvancedBodyScalingCorrectiveDriverType.ShoulderRaise => AverageStrength(
                DriverStrengthForBone(armature, cBase, "j_ude_a_l", 20f, 75f),
                DriverStrengthForBone(armature, cBase, "j_ude_a_r", 20f, 75f)),
            AdvancedBodyScalingCorrectiveDriverType.ClavicleTension => AverageStrength(
                DriverStrengthForBone(armature, cBase, "j_sako_l", 10f, 45f),
                DriverStrengthForBone(armature, cBase, "j_sako_r", 10f, 45f),
                DriverStrengthForBone(armature, cBase, "j_ude_a_l", 30f, 75f) * 0.6f,
                DriverStrengthForBone(armature, cBase, "j_ude_a_r", 30f, 75f) * 0.6f),
            AdvancedBodyScalingCorrectiveDriverType.NeckStress => DriverStrengthForBone(armature, cBase, "j_kubi", 12f, 40f),
            AdvancedBodyScalingCorrectiveDriverType.HipFlexion => AverageStrength(
                DriverStrengthForBone(armature, cBase, "j_asi_a_l", 18f, 70f),
                DriverStrengthForBone(armature, cBase, "j_asi_a_r", 18f, 70f)),
            AdvancedBodyScalingCorrectiveDriverType.TorsoTwist => AverageStrength(
                DriverStrengthForBone(armature, cBase, "j_kosi", 10f, 35f),
                DriverStrengthForBone(armature, cBase, "j_sebo_b", 10f, 35f)),
            _ => 0f,
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

    private static float EvaluateDiscontinuity(Armature armature, CorrectiveDefinition definition)
    {
        var avgA = AverageCurrentScale(armature, definition.GroupA);
        var avgB = AverageCurrentScale(armature, definition.GroupB);
        var discontinuity = MathF.Abs(avgA - avgB);

        if (definition.BridgeBones.Count > 0)
        {
            var bridge = AverageCurrentScale(armature, definition.BridgeBones);
            discontinuity = MathF.Max(discontinuity, MathF.Abs(avgA - bridge));
            discontinuity = MathF.Max(discontinuity, MathF.Abs(bridge - avgB));
        }

        return discontinuity;
    }

    private static void ApplyBridgeCorrection(
        Dictionary<string, Vector3> scaleMultipliers,
        Armature armature,
        CorrectiveDefinition definition,
        float activation)
    {
        var avgA = AverageCurrentScale(armature, definition.GroupA);
        var avgB = AverageCurrentScale(armature, definition.GroupB);
        var target = (avgA + avgB) * 0.5f;
        var blendStrength = activation * definition.MaxAdjustment;

        ApplyUniformTargetCorrection(scaleMultipliers, armature, definition.GroupA, target, blendStrength, definition.MaxAdjustment);
        ApplyUniformTargetCorrection(scaleMultipliers, armature, definition.GroupB, target, blendStrength, definition.MaxAdjustment);
        ApplyUniformTargetCorrection(scaleMultipliers, armature, definition.BridgeBones, target, blendStrength * 0.8f, definition.MaxAdjustment * 0.8f);
    }

    private static void ApplyNeckAxisBias(Dictionary<string, Vector3> scaleMultipliers, Armature armature, float activation)
    {
        var neck = armature.GetAppliedBoneTransform("j_kubi");
        if (neck == null || neck.LockState != BoneLockState.Unlocked)
            return;

        var multiplier = new Vector3(
            1f + (0.01f * activation),
            1f - (0.03f * activation),
            1f + (0.01f * activation));
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
        if (multiplier.IsApproximately(Vector3.One, 0.0005f))
            return;

        if (scaleMultipliers.TryGetValue(boneName, out var existing))
            scaleMultipliers[boneName] = new Vector3(existing.X * multiplier.X, existing.Y * multiplier.Y, existing.Z * multiplier.Z);
        else
            scaleMultipliers[boneName] = multiplier;
    }

    private static float AverageCurrentScale(Armature armature, IReadOnlyList<string> bones)
    {
        var values = bones
            .Select(armature.GetAppliedBoneTransform)
            .Where(transform => transform != null)
            .Select(transform => AdvancedBodyScalingPipeline.GetUniformScale(transform!.Scaling))
            .ToList();

        return values.Count == 0 ? 1f : values.Average();
    }

    private static ModelBone? TryGetBone(Armature armature, string boneName)
        => armature.GetAllBones().FirstOrDefault(bone => bone.BoneName == boneName);

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
