// Copyright (c) Customize+.
// Licensed under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;

namespace CustomizePlus.Core.Data;

/// <summary>
/// Bounded, read-only quality measurements for the existing scaling pipeline.
/// These diagnostics never alter explicit transforms or corrective output.
/// </summary>
internal sealed record DeformationQualityDiagnostics(
    float MaxBilateralDifference,
    string MaxBilateralPair,
    float MaxContinuityDifference,
    string MaxContinuityBoundary,
    float ProportionalImbalanceScore,
    float SurfaceGradientScore,
    IReadOnlyList<string> Warnings,
    DeformationQualitySolverDiagnostics Solver)
{
    public static DeformationQualityDiagnostics Empty { get; } = new(0f, "none", 0f, "none", 0f, 0f, Array.Empty<string>(), DeformationQualitySolverDiagnostics.Inactive);
}

internal static class DeformationQualityAnalyzer
{
    private static readonly (string Left, string Right)[] BilateralPairs =
    {
        ("j_mune_l", "j_mune_r"), ("j_sako_l", "j_sako_r"), ("n_hkata_l", "n_hkata_r"),
        ("j_ude_a_l", "j_ude_a_r"), ("j_ude_b_l", "j_ude_b_r"),
        ("j_asi_a_l", "j_asi_a_r"), ("j_asi_b_l", "j_asi_b_r"), ("j_asi_c_l", "j_asi_c_r"),
    };

    private static readonly (string First, string Second, string Label)[] ContinuityBoundaries =
    {
        ("j_sebo_c", "j_sako_l", "chest-clavicle left"), ("j_sebo_c", "j_sako_r", "chest-clavicle right"),
        ("n_hkata_l", "j_ude_a_l", "shoulder-upper arm left"), ("n_hkata_r", "j_ude_a_r", "shoulder-upper arm right"),
        ("j_ude_a_l", "j_ude_b_l", "upper arm-forearm left"), ("j_ude_a_r", "j_ude_b_r", "upper arm-forearm right"),
        ("j_kosi", "j_asi_a_l", "pelvis-thigh left"), ("j_kosi", "j_asi_a_r", "pelvis-thigh right"),
        ("j_asi_a_l", "j_asi_b_l", "thigh-knee left"), ("j_asi_a_r", "j_asi_b_r", "thigh-knee right"),
    };

    public static DeformationQualityDiagnostics Analyze(
        IReadOnlyDictionary<string, BoneTransform> transforms,
        DeformationQualitySolverDiagnostics? solver = null)
    {
        var maxBilateral = 0f;
        var bilateralPair = "none";
        foreach (var (left, right) in BilateralPairs)
        {
            if (!TryUniformScale(transforms, left, out var leftScale) || !TryUniformScale(transforms, right, out var rightScale))
                continue;

            var difference = MathF.Abs(leftScale - rightScale);
            if (difference > maxBilateral)
            {
                maxBilateral = difference;
                bilateralPair = $"{left}/{right}";
            }
        }

        var maxContinuity = 0f;
        var continuityBoundary = "none";
        foreach (var (first, second, label) in ContinuityBoundaries)
        {
            if (!TryUniformScale(transforms, first, out var firstScale) || !TryUniformScale(transforms, second, out var secondScale))
                continue;

            var difference = MathF.Abs(firstScale - secondScale);
            if (difference > maxContinuity)
            {
                maxContinuity = difference;
                continuityBoundary = label;
            }
        }

        var warnings = new List<string>();
        if (maxBilateral > 0.18f)
            warnings.Add($"Strong left/right scale difference at {bilateralPair} ({maxBilateral:0.000}).");
        if (maxContinuity > 0.24f)
            warnings.Add($"Large scale discontinuity at {continuityBoundary} ({maxContinuity:0.000}).");
        var solverDiagnostics = solver ?? DeformationQualitySolverDiagnostics.Inactive;
        if (solverDiagnostics.DoubleContributionPreventionCount > 0)
            warnings.Add($"Automatic secondary contribution was blended at {solverDiagnostics.DoubleContributionPreventionCount} shared boundary/bone location(s).");
        if (solverDiagnostics.ClampedContributionCount > 0)
            warnings.Add($"Automatic deformation rejected {solverDiagnostics.ClampedContributionCount} unsafe contribution(s).");
        if (solverDiagnostics.FallbackCount > 0)
            warnings.Add($"{solverDiagnostics.FallbackCount} automatic support contribution(s) were skipped because trust or capability evidence was unavailable.");
        return new DeformationQualityDiagnostics(
            maxBilateral,
            bilateralPair,
            maxContinuity,
            continuityBoundary,
            solverDiagnostics.MaximumProportionalImbalanceAfter,
            solverDiagnostics.MaximumPostSmoothingGradient,
            warnings,
            solverDiagnostics);
    }

    private static bool TryUniformScale(IReadOnlyDictionary<string, BoneTransform> transforms, string bone, out float scale)
    {
        if (!transforms.TryGetValue(bone, out var transform) || !TransformSafety.IsFinite(transform.Scaling))
        {
            scale = 1f;
            return false;
        }

        scale = (transform.Scaling.X + transform.Scaling.Y + transform.Scaling.Z) / 3f;
        return true;
    }
}
