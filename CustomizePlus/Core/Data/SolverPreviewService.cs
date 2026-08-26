// Copyright (c) Customize+.
// Licensed under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using CustomizePlus.Profiles.Data;

namespace CustomizePlus.Core.Data;

internal sealed record SolverPreviewRow(string BoneName, Vector3 CurrentScale, Vector3 BaselineScale, Vector3 Delta);

internal sealed record SolverPreviewResult(
    IReadOnlyList<SolverPreviewRow> Rows,
    int ChangedBoneCount,
    string Mode,
    bool SavedStateMutated)
{
    public static SolverPreviewResult Unavailable(string reason) => new(Array.Empty<SolverPreviewRow>(), 0, reason, false);
}

/// <summary>
/// On-demand, managed-only A/B solve. It runs the same static resolver and M5 solver
/// on copied dictionaries, never installs an actor override or writes settings to disk.
/// </summary>
internal static class SolverPreviewService
{
    public static SolverPreviewResult CompareCurrentToNaturalizationOff(
        Profile profile,
        SkeletonCapabilityManifest manifest,
        AdvancedBodyScalingSettings? activeSettings,
        AdvancedBodyScalingBoneImportanceResult? boneImportance,
        IEnumerable<string> liveBones)
    {
        if (activeSettings == null || !activeSettings.Enabled || activeSettings.Mode == AdvancedBodyScalingMode.Manual || !manifest.BindingCurrent)
            return SolverPreviewResult.Unavailable("Advanced Body Scaling is not currently active on a valid published skeleton.");

        var currentSettings = activeSettings.DeepCopy();
        var baselineSettings = activeSettings.DeepCopy();
        baselineSettings.ProportionalBalanceEnabled = false;
        baselineSettings.SurfaceSmoothnessEnabled = false;
        baselineSettings.CrossSectionConditioningEnabled = false;
        baselineSettings.LocalVolumeIntentEnabled = false;
        baselineSettings.ShapeFairnessEnabled = false;

        var current = Solve(profile, manifest, currentSettings, boneImportance, liveBones);
        var baseline = Solve(profile, manifest, baselineSettings, boneImportance, liveBones);
        var rows = current.Keys.Union(baseline.Keys, StringComparer.Ordinal)
            .Select(name =>
            {
                current.TryGetValue(name, out var currentTransform);
                baseline.TryGetValue(name, out var baselineTransform);
                var currentScale = currentTransform?.Scaling ?? Vector3.One;
                var baselineScale = baselineTransform?.Scaling ?? Vector3.One;
                return new SolverPreviewRow(name, currentScale, baselineScale, currentScale - baselineScale);
            })
            .Where(static row => row.Delta.LengthSquared() > 0.000001f)
            .OrderByDescending(static row => row.Delta.LengthSquared())
            .ThenBy(static row => row.BoneName, StringComparer.Ordinal)
            .ToArray();
        return new SolverPreviewResult(rows, rows.Length, "Current vs Naturalization Off", false);
    }

    private static Dictionary<string, BoneTransform> Solve(
        Profile profile,
        SkeletonCapabilityManifest manifest,
        AdvancedBodyScalingSettings settings,
        AdvancedBodyScalingBoneImportanceResult? boneImportance,
        IEnumerable<string> liveBones)
    {
        var resolved = ProfileTransformResolver.Resolve(profile, manifest);
        var output = resolved.EffectiveTransforms.ToDictionary(static pair => pair.Key, static pair => pair.Value.DeepCopy(), StringComparer.Ordinal);
        var explicitRows = output.Keys.ToHashSet(StringComparer.Ordinal);
        var conditioned = AdvancedBodyScalingPipeline.Apply(output, settings, boneImportance: boneImportance);
        AdvancedBodyScalingDeformationSolver.Apply(conditioned, explicitRows, liveBones.ToHashSet(StringComparer.Ordinal), manifest, boneImportance, settings);
        return conditioned;
    }
}
