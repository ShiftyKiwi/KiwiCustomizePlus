// Copyright (c) Customize+.
// Licensed under the MIT license.

using CustomizePlus.Core.Data;
using CustomizePlus.Core.Extensions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace CustomizePlus.Core.Services;

public sealed class SemanticBodyGoalService
{
    private const float MinimumGoalValue = -1f;
    private const float MaximumGoalValue = 1f;
    private const float MinimumScale = 0.70f;
    private const float MaximumScale = 1.30f;
    private const float Epsilon = 0.0005f;

    public SemanticBodyGoalService()
    {
        Goals = BuildGoals();
        Recipes = BuildRecipes();
    }

    public IReadOnlyList<SemanticBodyGoal> Goals { get; }
    public IReadOnlyList<ShapeRecipe> Recipes { get; }

    public Dictionary<string, float> CreateDefaultGoalValues()
        => Goals.ToDictionary(goal => goal.Id, _ => 0f, StringComparer.Ordinal);

    public Dictionary<string, float> CreateRecipeGoalValues(ShapeRecipe recipe)
    {
        var values = CreateDefaultGoalValues();
        foreach (var (goalId, value) in recipe.GoalValues)
        {
            if (values.ContainsKey(goalId))
                values[goalId] = ClampGoalValue(value);
        }

        return values;
    }

    public SemanticBodyGoalPreview BuildPreview(
        IReadOnlyDictionary<string, float> goalValues,
        IReadOnlyDictionary<string, BoneTransform> templateBones,
        IReadOnlySet<string> liveBoneNames,
        int signature)
    {
        var rows = new List<SemanticBodyGoalPreviewRow>();
        var finalTransforms = new Dictionary<string, BoneTransform>(StringComparer.Ordinal);

        foreach (var goal in Goals)
        {
            if (!goalValues.TryGetValue(goal.Id, out var rawGoalValue))
                continue;

            var goalValue = ClampGoalValue(rawGoalValue);
            if (MathF.Abs(goalValue) <= Epsilon)
                continue;

            foreach (var target in goal.Targets)
                AddPreviewRow(goal, target, goalValue, templateBones, liveBoneNames, finalTransforms, rows);
        }

        return new SemanticBodyGoalPreview(
            rows,
            finalTransforms,
            rows.Count(row => !row.IsSkipped),
            rows.Count(row => row.IsSkipped),
            signature);
    }

    private static void AddPreviewRow(
        SemanticBodyGoal goal,
        SemanticBodyGoalTarget target,
        float goalValue,
        IReadOnlyDictionary<string, BoneTransform> templateBones,
        IReadOnlySet<string> liveBoneNames,
        Dictionary<string, BoneTransform> finalTransforms,
        List<SemanticBodyGoalPreviewRow> rows)
    {
        templateBones.TryGetValue(target.BoneName, out var existingTransform);
        var currentTransform = finalTransforms.TryGetValue(target.BoneName, out var accumulatedTransform)
            ? new BoneTransform(accumulatedTransform)
            : new BoneTransform(existingTransform ?? new BoneTransform());
        var beforeScale = currentTransform.Scaling;

        var blockReason = GetBlockedReason(target.BoneName, existingTransform, liveBoneNames);
        if (!string.IsNullOrWhiteSpace(blockReason))
        {
            rows.Add(CreateSkippedRow(goal, target.BoneName, beforeScale, blockReason));
            return;
        }

        var desiredScale = ClampScale(beforeScale + (target.ScaleDeltaPerUnit * goalValue));
        var pinReason = ApplyPins(currentTransform, ref desiredScale);
        var delta = desiredScale - beforeScale;
        if (delta.IsApproximately(Vector3.Zero, Epsilon))
        {
            rows.Add(CreateSkippedRow(goal, target.BoneName, beforeScale, string.IsNullOrWhiteSpace(pinReason) ? "No scale change after clamps." : pinReason));
            return;
        }

        currentTransform.Scaling = desiredScale;
        finalTransforms[target.BoneName] = currentTransform;

        rows.Add(new SemanticBodyGoalPreviewRow(
            goal.DisplayName,
            target.BoneName,
            BoneData.GetBoneDisplayName(target.BoneName),
            beforeScale,
            desiredScale,
            delta,
            false,
            string.IsNullOrWhiteSpace(pinReason) ? "Previewed" : pinReason));
    }

    private static string? GetBlockedReason(
        string boneName,
        BoneTransform? existingTransform,
        IReadOnlySet<string> liveBoneNames)
    {
        if (BoneData.GetBoneFamily(boneName) == BoneData.BoneFamily.Unknown)
            return "Unknown/custom bone skipped.";

        if (!BoneData.IsDefaultBone(boneName))
            return "Non-default or modded bone skipped.";

        if (BoneData.IsIVCSCompatibleBone(boneName))
            return "IVCS/modded-compatible bone skipped by MVP safety rules.";

        if (liveBoneNames.Count > 0 && !liveBoneNames.Contains(boneName))
            return "Bone is not present on the current preview skeleton.";

        if (existingTransform?.LockState != null && existingTransform.LockState != BoneLockState.Unlocked)
            return $"Row is {existingTransform.LockState}; semantic goals respect row locks.";

        return null;
    }

    private static string ApplyPins(BoneTransform transform, ref Vector3 desiredScale)
    {
        if (!transform.HasPinnedScaleAxes())
            return string.Empty;

        var pinned = new List<string>();
        if (transform.PinX)
        {
            desiredScale.X = transform.Scaling.X;
            pinned.Add("X");
        }

        if (transform.PinY)
        {
            desiredScale.Y = transform.Scaling.Y;
            pinned.Add("Y");
        }

        if (transform.PinZ)
        {
            desiredScale.Z = transform.Scaling.Z;
            pinned.Add("Z");
        }

        return pinned.Count == 3
            ? "All scale axes are pinned."
            : $"{string.Join("/", pinned)} scale {(pinned.Count == 1 ? "axis is" : "axes are")} pinned; preview uses only unpinned axes.";
    }

    private static SemanticBodyGoalPreviewRow CreateSkippedRow(
        SemanticBodyGoal goal,
        string boneName,
        Vector3 scale,
        string reason)
        => new(
            goal.DisplayName,
            boneName,
            BoneData.GetBoneDisplayName(boneName),
            scale,
            scale,
            Vector3.Zero,
            true,
            reason);

    private static Vector3 ClampScale(Vector3 scale)
        => new(
            Math.Clamp(scale.X, MinimumScale, MaximumScale),
            Math.Clamp(scale.Y, MinimumScale, MaximumScale),
            Math.Clamp(scale.Z, MinimumScale, MaximumScale));

    private static float ClampGoalValue(float value)
        => Math.Clamp(value, MinimumGoalValue, MaximumGoalValue);

    private static IReadOnlyList<SemanticBodyGoal> BuildGoals()
        =>
        [
            new(
                "broader_shoulders",
                "Broader Shoulders",
                "Adds a conservative scale emphasis to built-in shoulder and clavicle support bones.",
                [
                    new("n_hkata_l", new Vector3(0.055f, 0.020f, 0.055f)),
                    new("n_hkata_r", new Vector3(0.055f, 0.020f, 0.055f)),
                    new("j_sako_l", new Vector3(0.035f, 0.015f, 0.035f)),
                    new("j_sako_r", new Vector3(0.035f, 0.015f, 0.035f))
                ]),
            new(
                "narrower_waist",
                "Narrower Waist",
                "Gently reduces waist and lower-spine scale for taper-oriented templates.",
                [
                    new("j_kosi", new Vector3(-0.055f, -0.010f, -0.055f)),
                    new("j_sebo_a", new Vector3(-0.035f, -0.005f, -0.035f))
                ]),
            new(
                "wider_hips",
                "Wider Hips",
                "Adds conservative width support around built-in hip and upper-leg roots.",
                [
                    new("j_asi_a_l", new Vector3(0.050f, 0.020f, 0.050f)),
                    new("j_asi_a_r", new Vector3(0.050f, 0.020f, 0.050f)),
                    new("j_kosi", new Vector3(0.025f, 0.005f, 0.025f))
                ]),
            new(
                "stronger_arms",
                "Stronger Arms",
                "Adds light uniform scale to built-in upper-arm and forearm bones.",
                [
                    new("j_ude_a_l", new Vector3(0.050f, 0.050f, 0.050f)),
                    new("j_ude_a_r", new Vector3(0.050f, 0.050f, 0.050f)),
                    new("j_ude_b_l", new Vector3(0.035f, 0.035f, 0.035f)),
                    new("j_ude_b_r", new Vector3(0.035f, 0.035f, 0.035f))
                ]),
            new(
                "thicker_thighs",
                "Thicker Thighs",
                "Adds conservative upper-leg scale with a small knee transition.",
                [
                    new("j_asi_a_l", new Vector3(0.055f, 0.055f, 0.055f)),
                    new("j_asi_a_r", new Vector3(0.055f, 0.055f, 0.055f)),
                    new("j_asi_b_l", new Vector3(0.020f, 0.020f, 0.020f)),
                    new("j_asi_b_r", new Vector3(0.020f, 0.020f, 0.020f))
                ]),
            new(
                "fuller_chest",
                "Fuller Chest",
                "Adds a small scale emphasis to built-in chest/breast support bones.",
                [
                    new("j_mune_l", new Vector3(0.045f, 0.045f, 0.045f)),
                    new("j_mune_r", new Vector3(0.045f, 0.045f, 0.045f)),
                    new("j_sebo_b", new Vector3(0.020f, 0.005f, 0.020f))
                ]),
            new(
                "softer_calves",
                "Softer Calves / Lower-Leg Balance",
                "Adds or removes lower-leg scale gently while keeping knees involved as a transition.",
                [
                    new("j_asi_c_l", new Vector3(0.040f, 0.040f, 0.040f)),
                    new("j_asi_c_r", new Vector3(0.040f, 0.040f, 0.040f)),
                    new("j_asi_b_l", new Vector3(0.015f, 0.015f, 0.015f)),
                    new("j_asi_b_r", new Vector3(0.015f, 0.015f, 0.015f))
                ])
        ];

    private static IReadOnlyList<ShapeRecipe> BuildRecipes()
        =>
        [
            new(
                "athletic_taper",
                "Athletic Taper",
                "Broader shoulders, stronger arms, and modest waist taper.",
                new Dictionary<string, float>(StringComparer.Ordinal)
                {
                    ["broader_shoulders"] = 0.75f,
                    ["narrower_waist"] = 0.45f,
                    ["stronger_arms"] = 0.55f,
                    ["thicker_thighs"] = 0.25f
                }),
            new(
                "soft_hourglass",
                "Soft Hourglass",
                "Waist taper with fuller chest, wider hips, and softer lower-body support.",
                new Dictionary<string, float>(StringComparer.Ordinal)
                {
                    ["narrower_waist"] = 0.50f,
                    ["wider_hips"] = 0.65f,
                    ["fuller_chest"] = 0.45f,
                    ["thicker_thighs"] = 0.35f,
                    ["softer_calves"] = 0.20f
                }),
            new(
                "broad_v_frame",
                "Broad V-Frame",
                "Shoulder and arm emphasis with a restrained lower-body counterweight.",
                new Dictionary<string, float>(StringComparer.Ordinal)
                {
                    ["broader_shoulders"] = 0.90f,
                    ["stronger_arms"] = 0.60f,
                    ["narrower_waist"] = 0.35f,
                    ["wider_hips"] = -0.20f
                }),
            new(
                "balanced_lower_body",
                "Balanced Lower Body",
                "Hip, thigh, and lower-leg support for templates that need a steadier lower half.",
                new Dictionary<string, float>(StringComparer.Ordinal)
                {
                    ["wider_hips"] = 0.45f,
                    ["thicker_thighs"] = 0.60f,
                    ["softer_calves"] = 0.45f,
                    ["broader_shoulders"] = 0.15f
                })
        ];
}
