// Copyright (c) Customize+.
// Licensed under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace CustomizePlus.Core.Data;

/// <summary>
/// A rebuild-time, bone-only continuity pass for a small set of curated vanilla
/// anatomical chains. It is deliberately an ephemeral derived layer: callers
/// pass a freshly resolved transform map and this system never mutates templates.
/// </summary>
internal static class AdvancedBodyScalingHierarchicalShapingSystem
{
    internal const float MaximumAuthoredRelaxationPercent = 1f;

    private const float MaximumLogCorrection = 0.009950331f; // log(1.01)
    private const float RelaxationGain = 0.35f;
    private const float Epsilon = 0.00001f;
    private const float BilateralIntentTolerance = 0.0025f;

    private sealed record ChainRegion(string Name, IReadOnlyList<IReadOnlyList<string>> Paths);

    // These are curated semantic paths rather than parent walks. They only include
    // stable vanilla body controls and intentionally never enter equipment or extras.
    private static readonly ChainRegion[] Regions =
    {
        new(
            "chest -> shoulder -> upper arm",
            new IReadOnlyList<string>[]
            {
                new[] { "j_sebo_b", "j_sebo_c", "j_sako_l", "n_hkata_l", "j_ude_a_l" },
                new[] { "j_sebo_b", "j_sebo_c", "j_sako_r", "n_hkata_r", "j_ude_a_r" },
            }),
        new(
            "ribs -> abdomen -> waist",
            new IReadOnlyList<string>[]
            {
                new[] { "j_sebo_b", "j_sebo_a", "j_kosi" },
            }),
        new(
            "pelvis -> thigh -> knee -> calf",
            new IReadOnlyList<string>[]
            {
                new[] { "j_kosi", "j_asi_a_l", "j_asi_b_l", "j_asi_c_l" },
                new[] { "j_kosi", "j_asi_a_r", "j_asi_b_r", "j_asi_c_r" },
            }),
    };

    internal static HierarchicalShapingDiagnostics Apply(
        IDictionary<string, BoneTransform> output,
        IReadOnlySet<string> explicitTransforms,
        IReadOnlySet<string> liveBoneNames,
        SkeletonCapabilityManifest manifest,
        AdvancedBodyScalingSettings settings)
    {
        if (!settings.HierarchicalShapingEnabled)
            return HierarchicalShapingDiagnostics.CreateInactive(settings);

        if (output.Count == 0 || liveBoneNames.Count == 0 || !manifest.BindingCurrent)
            return HierarchicalShapingDiagnostics.CreateUnavailable(settings, "The validated live skeleton binding is unavailable.");

        var diagnostics = new MutableDiagnostics(settings);
        foreach (var region in Regions)
            ApplyRegion(region, output, explicitTransforms, liveBoneNames, manifest, settings, diagnostics);

        return diagnostics.Freeze();
    }

    /// <summary>
    /// Replays an already admitted rebuild result only after every hard receiver gate
    /// is rechecked. A caller must additionally require an exact input cache-key match.
    /// </summary>
    internal static bool TryApplyCached(
        IDictionary<string, BoneTransform> output,
        IReadOnlySet<string> explicitTransforms,
        IReadOnlySet<string> liveBoneNames,
        SkeletonCapabilityManifest manifest,
        AdvancedBodyScalingSettings settings,
        HierarchicalShapingDiagnostics cached,
        out HierarchicalShapingDiagnostics diagnostics)
    {
        diagnostics = HierarchicalShapingDiagnostics.CreateInactive(settings);
        if (!settings.HierarchicalShapingEnabled
            || !cached.Enabled
            || cached.AuthoredRelaxationEnabled != settings.HierarchicalAuthoredRelaxationEnabled
            || !manifest.BindingCurrent)
            return false;

        var deltas = new Dictionary<string, Vector3>(StringComparer.Ordinal);
        var candidates = new Dictionary<string, BoneTransform>(StringComparer.Ordinal);
        foreach (var (bone, logCorrection) in cached.LogScaleCorrections)
        {
            if (!IsMutableReceiver(bone, output, explicitTransforms, liveBoneNames, manifest, settings)
                || !output.TryGetValue(bone, out var existing))
            {
                return false;
            }

            var candidate = new BoneTransform(existing)
            {
                Scaling = existing.Scaling * MathF.Exp(logCorrection),
            };
            if (!TransformSafety.IsFinite(candidate.Scaling))
                return false;

            deltas[bone] = candidate.Scaling - existing.Scaling;
            candidates[bone] = candidate;
        }

        foreach (var (bone, candidate) in candidates)
            output[bone] = candidate;

        diagnostics = cached.WithCachedContributions(deltas);
        return true;
    }

    private static void ApplyRegion(
        ChainRegion region,
        IDictionary<string, BoneTransform> output,
        IReadOnlySet<string> explicitTransforms,
        IReadOnlySet<string> liveBoneNames,
        SkeletonCapabilityManifest manifest,
        AdvancedBodyScalingSettings settings,
        MutableDiagnostics diagnostics)
    {
        var bones = region.Paths.SelectMany(static path => path).Distinct(StringComparer.Ordinal).ToArray();
        if (bones.Any(bone => !TryGetTrustedScale(bone, output, liveBoneNames, manifest, out _)))
        {
            diagnostics.RecordRegion(region.Name, HierarchicalShapingRegionStatus.Unavailable, 0, 0, 0f, 0f, "A required live trusted body control is unavailable.");
            return;
        }

        if (HasDeliberateBilateralAsymmetry(region, output))
        {
            diagnostics.RecordRegion(region.Name, HierarchicalShapingRegionStatus.PreservedAsymmetry, 0, 0, 0f, 0f, "A deliberate left/right authored contrast is preserved.");
            return;
        }

        var mutable = bones
            .Where(bone => IsMutableReceiver(bone, output, explicitTransforms, liveBoneNames, manifest, settings))
            .ToHashSet(StringComparer.Ordinal);
        if (mutable.Count == 0)
        {
            diagnostics.RecordRegion(region.Name, HierarchicalShapingRegionStatus.Constrained, 0, 0, 0f, 0f, "All eligible controls are explicit, locked, pinned, or excluded.");
            return;
        }

        var before = GetRegionEnergy(region, output);
        var proposed = BuildProposals(region, output, mutable);
        PreserveEquivalentBilateralProposals(region, output, mutable, proposed);
        if (proposed.Count == 0)
        {
            diagnostics.RecordRegion(region.Name, HierarchicalShapingRegionStatus.NoUsefulCorrection, mutable.Count, 0, before, before, "The trusted chain is already continuous within the 1% bound.");
            return;
        }

        var after = GetRegionEnergy(region, output, proposed);
        if (!(after + Epsilon < before))
        {
            diagnostics.RecordRegion(region.Name, HierarchicalShapingRegionStatus.NoUsefulCorrection, mutable.Count, 0, before, after, "The bounded candidate did not improve the local continuity metric.");
            return;
        }

        foreach (var (bone, logDelta) in proposed)
        {
            var previous = output.TryGetValue(bone, out var existing) ? existing : new BoneTransform();
            var candidate = new BoneTransform(previous)
            {
                Scaling = previous.Scaling * MathF.Exp(logDelta),
            };
            if (!TransformSafety.IsFinite(candidate.Scaling))
                continue;

            output[bone] = candidate;
            diagnostics.RecordContribution(bone, region.Name, previous.Scaling, candidate.Scaling, logDelta);
        }

        diagnostics.RecordRegion(region.Name, HierarchicalShapingRegionStatus.Applied, mutable.Count, proposed.Count, before, after, "Applied bounded scale-only continuity support.");
    }

    private static Dictionary<string, float> BuildProposals(
        ChainRegion region,
        IDictionary<string, BoneTransform> output,
        IReadOnlySet<string> mutable)
    {
        var candidates = new Dictionary<string, List<float>>(StringComparer.Ordinal);
        foreach (var path in region.Paths)
        {
            for (var index = 1; index < path.Count - 1; index++)
            {
                var bone = path[index];
                if (!mutable.Contains(bone)
                    || !TryGetUniformLogScale(output, bone, out var current)
                    || !TryGetUniformLogScale(output, path[index - 1], out var previous)
                    || !TryGetUniformLogScale(output, path[index + 1], out var next))
                {
                    continue;
                }

                var desired = (previous + next) * 0.5f;
                var correction = Math.Clamp((desired - current) * RelaxationGain, -MaximumLogCorrection, MaximumLogCorrection);
                if (MathF.Abs(correction) <= Epsilon)
                    continue;

                if (!candidates.TryGetValue(bone, out var values))
                    candidates[bone] = values = new List<float>();
                values.Add(correction);
            }
        }

        return candidates.ToDictionary(
            static pair => pair.Key,
            static pair => pair.Value.Average(),
            StringComparer.Ordinal);
    }

    private static void PreserveEquivalentBilateralProposals(
        ChainRegion region,
        IDictionary<string, BoneTransform> output,
        IReadOnlySet<string> mutable,
        IDictionary<string, float> proposed)
    {
        if (region.Paths.Count != 2)
            return;

        var left = region.Paths[0];
        var right = region.Paths[1];
        for (var index = 0; index < Math.Min(left.Count, right.Count); index++)
        {
            var leftBone = left[index];
            var rightBone = right[index];
            if (string.Equals(leftBone, rightBone, StringComparison.Ordinal))
                continue;

            var hasLeft = proposed.TryGetValue(leftBone, out var leftCorrection);
            var hasRight = proposed.TryGetValue(rightBone, out var rightCorrection);
            if (!hasLeft || !hasRight || !mutable.Contains(leftBone) || !mutable.Contains(rightBone))
            {
                proposed.Remove(leftBone);
                proposed.Remove(rightBone);
                continue;
            }

            // Equivalent left/right inputs receive exactly the same multiplier.
            var shared = Math.Clamp((leftCorrection + rightCorrection) * 0.5f, -MaximumLogCorrection, MaximumLogCorrection);
            proposed[leftBone] = shared;
            proposed[rightBone] = shared;
        }
    }

    private static bool HasDeliberateBilateralAsymmetry(ChainRegion region, IDictionary<string, BoneTransform> output)
    {
        if (region.Paths.Count != 2)
            return false;

        var left = region.Paths[0];
        var right = region.Paths[1];
        for (var index = 0; index < Math.Min(left.Count, right.Count); index++)
        {
            if (string.Equals(left[index], right[index], StringComparison.Ordinal))
                continue;
            if (!TryGetUniformLogScale(output, left[index], out var leftScale)
                || !TryGetUniformLogScale(output, right[index], out var rightScale))
            {
                return true;
            }

            if (MathF.Abs(leftScale - rightScale) > BilateralIntentTolerance)
                return true;
        }

        return false;
    }

    private static float GetRegionEnergy(
        ChainRegion region,
        IDictionary<string, BoneTransform> output,
        IDictionary<string, float>? proposed = null)
    {
        var energy = 0f;
        foreach (var path in region.Paths)
        {
            for (var index = 1; index < path.Count; index++)
            {
                if (!TryGetUniformLogScale(output, path[index - 1], out var previous)
                    || !TryGetUniformLogScale(output, path[index], out var current))
                {
                    return float.PositiveInfinity;
                }

                if (proposed != null)
                {
                    if (proposed.TryGetValue(path[index - 1], out var previousCorrection))
                        previous += previousCorrection;
                    if (proposed.TryGetValue(path[index], out var currentCorrection))
                        current += currentCorrection;
                }

                var difference = current - previous;
                energy += difference * difference;
            }
        }

        return energy;
    }

    private static bool IsMutableReceiver(
        string bone,
        IDictionary<string, BoneTransform> output,
        IReadOnlySet<string> explicitTransforms,
        IReadOnlySet<string> liveBoneNames,
        SkeletonCapabilityManifest manifest,
        AdvancedBodyScalingSettings settings)
    {
        if (!TryGetTrustedScale(bone, output, liveBoneNames, manifest, out var transform)
            || transform.LockState != BoneLockState.Unlocked
            || transform.HasPinnedScaleAxes())
        {
            return false;
        }

        return settings.HierarchicalAuthoredRelaxationEnabled || !explicitTransforms.Contains(bone);
    }

    private static bool TryGetTrustedScale(
        string bone,
        IDictionary<string, BoneTransform> output,
        IReadOnlySet<string> liveBoneNames,
        SkeletonCapabilityManifest manifest,
        out BoneTransform transform)
    {
        transform = output.TryGetValue(bone, out var existing) ? existing : new BoneTransform();
        if (!liveBoneNames.Contains(bone) || !manifest.BindingCurrent || !TransformSafety.IsFinite(transform.Scaling))
            return false;

        // Curated vanilla structural anatomy is the only production admission set.
        // Modded/extended controls remain untouched even when they have a metadata entry.
        var metadata = BoneData.GetMetadata(bone);
        return metadata.Origin == BoneOrigin.Vanilla
               && metadata.Role == BoneFunctionalRole.StructuralAnatomical
               && metadata.HasTrust(BoneAutomationTrust.AdvancedCorrectiveSafe);
    }

    private static bool TryGetUniformLogScale(IDictionary<string, BoneTransform> output, string bone, out float value)
    {
        var scale = output.TryGetValue(bone, out var transform) ? transform.Scaling : Vector3.One;
        if (!TransformSafety.IsFinite(scale) || scale.X <= 0f || scale.Y <= 0f || scale.Z <= 0f)
        {
            value = 0f;
            return false;
        }

        value = (MathF.Log(scale.X) + MathF.Log(scale.Y) + MathF.Log(scale.Z)) / 3f;
        return float.IsFinite(value);
    }

    private sealed class MutableDiagnostics
    {
        private readonly AdvancedBodyScalingSettings _settings;
        private readonly List<HierarchicalShapingRegionDiagnostic> _regions = new();
        private readonly Dictionary<string, Vector3> _originalScales = new(StringComparer.Ordinal);
        private readonly Dictionary<string, Vector3> _scaleDeltas = new(StringComparer.Ordinal);
        private readonly Dictionary<string, float> _logCorrections = new(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _contributionRegions = new(StringComparer.Ordinal);

        public MutableDiagnostics(AdvancedBodyScalingSettings settings)
            => _settings = settings;

        public void RecordRegion(
            string region,
            HierarchicalShapingRegionStatus status,
            int eligible,
            int corrected,
            float before,
            float after,
            string reason)
            => _regions.Add(new HierarchicalShapingRegionDiagnostic(region, status, eligible, corrected, before, after, reason));

        public void RecordContribution(string bone, string region, Vector3 before, Vector3 after, float logDelta)
        {
            if (!_originalScales.TryGetValue(bone, out var original))
                _originalScales[bone] = original = before;

            _scaleDeltas[bone] = after - original;
            _logCorrections[bone] = _logCorrections.TryGetValue(bone, out var accumulated)
                ? accumulated + logDelta
                : logDelta;
            _contributionRegions[bone] = _contributionRegions.TryGetValue(bone, out var previousRegion)
                ? $"{previousRegion}; {region}"
                : region;
        }

        public HierarchicalShapingDiagnostics Freeze()
            => new(
                true,
                _settings.HierarchicalAuthoredRelaxationEnabled,
                _regions,
                _scaleDeltas,
                _logCorrections,
                _contributionRegions,
                "uncached",
                false);
    }
}

internal enum HierarchicalShapingRegionStatus
{
    Applied,
    Unavailable,
    Constrained,
    PreservedAsymmetry,
    NoUsefulCorrection,
}

internal sealed record HierarchicalShapingRegionDiagnostic(
    string Region,
    HierarchicalShapingRegionStatus Status,
    int EligibleBoneCount,
    int CorrectedBoneCount,
    float ContinuityErrorBefore,
    float ContinuityErrorAfter,
    string Reason);

internal sealed record HierarchicalShapingDiagnostics(
    bool Enabled,
    bool AuthoredRelaxationEnabled,
    IReadOnlyList<HierarchicalShapingRegionDiagnostic> Regions,
    IReadOnlyDictionary<string, Vector3> ContributionScaleDeltas,
    IReadOnlyDictionary<string, float> LogScaleCorrections,
    IReadOnlyDictionary<string, string> ContributionRegions,
    string CacheKey,
    bool CacheHit)
{
    public int CorrectedBoneCount => ContributionScaleDeltas.Count;
    public int AppliedRegionCount => Regions.Count(static region => region.Status == HierarchicalShapingRegionStatus.Applied);
    public float MaximumScaleDelta => ContributionScaleDeltas.Count == 0
        ? 0f
        : ContributionScaleDeltas.Values.Max(static delta => delta.Length());

    public static HierarchicalShapingDiagnostics CreateInactive(AdvancedBodyScalingSettings? settings)
        => new(false, settings?.HierarchicalAuthoredRelaxationEnabled ?? false, Array.Empty<HierarchicalShapingRegionDiagnostic>(),
            new Dictionary<string, Vector3>(StringComparer.Ordinal), new Dictionary<string, float>(StringComparer.Ordinal),
            new Dictionary<string, string>(StringComparer.Ordinal), "inactive", false);

    public static HierarchicalShapingDiagnostics CreateUnavailable(AdvancedBodyScalingSettings settings, string reason)
        => new(true, settings.HierarchicalAuthoredRelaxationEnabled,
            new[] { new HierarchicalShapingRegionDiagnostic("all", HierarchicalShapingRegionStatus.Unavailable, 0, 0, 0f, 0f, reason) },
            new Dictionary<string, Vector3>(StringComparer.Ordinal), new Dictionary<string, float>(StringComparer.Ordinal),
            new Dictionary<string, string>(StringComparer.Ordinal), "binding-unavailable", false);

    public HierarchicalShapingDiagnostics WithCacheMetadata(string cacheKey, bool cacheHit)
        => this with { CacheKey = cacheKey, CacheHit = cacheHit };

    public HierarchicalShapingDiagnostics WithCachedContributions(IReadOnlyDictionary<string, Vector3> deltas)
        => this with { ContributionScaleDeltas = deltas, CacheHit = true };
}
