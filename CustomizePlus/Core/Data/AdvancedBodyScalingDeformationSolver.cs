// Copyright (c) Customize+.
// Licensed under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace CustomizePlus.Core.Data;

[Flags]
internal enum DeformationContributionSource
{
    None = 0,
    AutomaticSupport = 1 << 0,
    ProportionalBalance = 1 << 1,
    SurfaceSmoothness = 1 << 2,
    LocalVolumeIntent = 1 << 3,
    CrossSectionConditioning = 1 << 4,
    ShapeFairness = 1 << 5,
}

/// <summary>
/// Bounded, rebuild-time automatic support layer for Advanced Body Scaling.
/// It only creates transforms for live, curated support/transition bones that
/// have no explicit template transform. User-authored rows remain authoritative.
/// </summary>
internal static class AdvancedBodyScalingDeformationSolver
{
    private const float Epsilon = 0.0005f;
    private const float GradientThreshold = 0.025f;
    private const float MinAutoScale = 0.70f;
    private const float MaxAutoScale = 1.45f;
    private const float MaxSurfaceCorrection = 0.045f;
    private const float MaxCrossSectionLogCorrection = 0.038f;
    private const float MaxVolumeMeanCorrection = 0.035f;
    private const float MaxFairnessLogCorrection = 0.032f;
    private const float CrossSectionAnisotropyStart = 0.16f;
    private const float FairnessCurvatureThreshold = 0.045f;
    private const float BilateralIntentTolerance = 0.0025f;

    private enum ContributionKind
    {
        Primary,
        Support,
        Transition,
        Secondary,
    }

    private sealed record RegionDefinition(
        string Name,
        IReadOnlyList<string> Primary,
        IReadOnlyList<string> Support,
        IReadOnlyList<string> Transition,
        IReadOnlyList<string> Secondary,
        SkeletonCapability RequiredCapability = SkeletonCapability.None);

    private sealed record RegionRelationship(
        string SourceRegion,
        string ReceiverRegion,
        float SupportWeight,
        float MaxCorrection);

    private sealed record SmoothingEdge(string First, string Second, string Region, float Weight);

    private sealed record FairnessChain(string Name, IReadOnlyList<string> Bones);

    private sealed class WeightedScaleAccumulator
    {
        public Vector3 Sum;
        public float Weight;
        public int Count;

        public void Add(Vector3 scale, float weight)
        {
            Sum += scale * weight;
            Weight += weight;
            Count++;
        }
    }

    private static readonly RegionDefinition[] Regions =
    {
        new("chest", new[] { "j_mune_l", "j_mune_r", "j_sebo_b" }, new[] { "j_sebo_c" }, new[] { "j_sako_l", "j_sako_r", "n_hkata_l", "n_hkata_r" }, new[] { "iv_kyokin_phys_l", "iv_kyokin_phys_r", "iv_c_mune_l", "iv_c_mune_r", "forebreas_l", "forebreas_r", "nf_nipple_l", "nf_nipple_r" }),
        new("shoulders", new[] { "n_hkata_l", "n_hkata_r", "j_sako_l", "j_sako_r" }, new[] { "j_sebo_c" }, new[] { "j_ude_a_l", "j_ude_a_r" }, Array.Empty<string>()),
        new("upper arms", new[] { "j_ude_a_l", "j_ude_a_r" }, new[] { "iv_nitoukin_l", "iv_nitoukin_r" }, new[] { "j_ude_b_l", "j_ude_b_r" }, Array.Empty<string>()),
        new("forearms", new[] { "j_ude_b_l", "j_ude_b_r" }, Array.Empty<string>(), new[] { "j_te_l", "j_te_r" }, Array.Empty<string>()),
        new("abdomen", new[] { "j_sebo_a", "j_sebo_b" }, new[] { "j_sebo_c" }, new[] { "j_kosi" }, new[] { "iv_fukubu_phys", "iv_fukubu_phys_l", "iv_fukubu_phys_r", "ya_fukubu_phys", "belly_sebo_a", "belly_kosi" }),
        new("waist", new[] { "j_kosi" }, new[] { "j_sebo_a" }, new[] { "j_asi_a_l", "j_asi_a_r" }, new[] { "ya_fukubu_phys", "belly_kosi" }),
        new("pelvis/glutes", new[] { "j_kosi" }, new[] { "j_asi_a_l", "j_asi_a_r" }, new[] { "j_sebo_a" }, new[] { "iv_shiri_l", "iv_shiri_r", "ya_shiri_phys_l", "ya_shiri_phys_r", "butt_left", "butt_right", "nf_iv_shiri_l", "nf_iv_shiri_r" }),
        new("thighs", new[] { "j_asi_a_l", "j_asi_a_r" }, new[] { "j_asi_b_l", "j_asi_b_r" }, new[] { "j_asi_c_l", "j_asi_c_r" }, new[] { "iv_daitai_phys_l", "iv_daitai_phys_r", "ya_daitai_phys_l", "ya_daitai_phys_r", "thigh_l", "thigh_r", "nf_iv_daitai_phys_l", "nf_iv_daitai_phys_r" }),
        new("calves", new[] { "j_asi_b_l", "j_asi_b_r" }, new[] { "j_asi_c_l", "j_asi_c_r" }, new[] { "j_asi_d_l", "j_asi_d_r" }, Array.Empty<string>()),
        new("neck/traps", new[] { "j_kubi", "j_sebo_c" }, new[] { "j_sako_l", "j_sako_r" }, new[] { "n_hkata_l", "n_hkata_r" }, Array.Empty<string>()),
    };

    // These relationships represent support flow from a requested region to an otherwise automatic neighbor.
    // They deliberately encode no "ideal" body ratios.
    private static readonly RegionRelationship[] ProportionalRelationships =
    {
        new("chest", "shoulders", 0.42f, 0.055f),
        new("shoulders", "upper arms", 0.34f, 0.045f),
        new("upper arms", "forearms", 0.30f, 0.040f),
        new("abdomen", "waist", 0.35f, 0.045f),
        new("waist", "pelvis/glutes", 0.32f, 0.045f),
        new("pelvis/glutes", "thighs", 0.42f, 0.055f),
        new("thighs", "calves", 0.30f, 0.040f),
        new("neck/traps", "shoulders", 0.25f, 0.035f),
    };

    // Bone-space adjacency only. This intentionally never crosses into clothing, props, wings, tongues,
    // or unknown/manual bones. Explicit rows act as stationary anchors.
    private static readonly SmoothingEdge[] SmoothingEdges =
    {
        new("j_sebo_b", "j_sebo_c", "chest", 0.65f),
        new("j_sebo_c", "j_sako_l", "chest/shoulder", 0.60f), new("j_sebo_c", "j_sako_r", "chest/shoulder", 0.60f),
        new("j_sako_l", "n_hkata_l", "shoulders", 0.70f), new("j_sako_r", "n_hkata_r", "shoulders", 0.70f),
        new("n_hkata_l", "j_ude_a_l", "shoulder/upper arm", 0.50f), new("n_hkata_r", "j_ude_a_r", "shoulder/upper arm", 0.50f),
        new("j_ude_a_l", "j_ude_b_l", "upper arm/forearm", 0.40f), new("j_ude_a_r", "j_ude_b_r", "upper arm/forearm", 0.40f),
        new("j_sebo_b", "j_sebo_a", "abdomen", 0.65f), new("j_sebo_a", "j_kosi", "waist", 0.60f),
        new("j_kosi", "j_asi_a_l", "pelvis/thigh", 0.55f), new("j_kosi", "j_asi_a_r", "pelvis/thigh", 0.55f),
        new("j_asi_a_l", "j_asi_b_l", "thigh/knee", 0.42f), new("j_asi_a_r", "j_asi_b_r", "thigh/knee", 0.42f),
        new("j_asi_b_l", "j_asi_c_l", "knee/calf", 0.38f), new("j_asi_b_r", "j_asi_c_r", "knee/calf", 0.38f),
        new("j_kubi", "j_sebo_c", "neck/traps", 0.45f),
    };

    // These are curated semantic chains, not raw parent walks. They intentionally stop at
    // body controls and never enter clothing, props, appendages, or unknown controls.
    private static readonly FairnessChain[] FairnessChains =
    {
        new("chest-left-arm", new[] { "j_sebo_b", "j_sebo_c", "j_sako_l", "n_hkata_l", "j_ude_a_l", "j_ude_b_l" }),
        new("chest-right-arm", new[] { "j_sebo_b", "j_sebo_c", "j_sako_r", "n_hkata_r", "j_ude_a_r", "j_ude_b_r" }),
        new("spine-pelvis-left-leg", new[] { "j_sebo_b", "j_sebo_a", "j_kosi", "j_asi_a_l", "j_asi_b_l", "j_asi_c_l" }),
        new("spine-pelvis-right-leg", new[] { "j_sebo_b", "j_sebo_a", "j_kosi", "j_asi_a_r", "j_asi_b_r", "j_asi_c_r" }),
    };

    private static readonly IReadOnlyDictionary<string, RegionDefinition> RegionsByName =
        Regions.ToDictionary(static region => region.Name, StringComparer.Ordinal);

    private static readonly IReadOnlyDictionary<string, string> CuratedSecondaryMirrors = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["iv_kyokin_phys_l"] = "iv_kyokin_phys_r", ["iv_kyokin_phys_r"] = "iv_kyokin_phys_l",
        ["iv_fukubu_phys_l"] = "iv_fukubu_phys_r", ["iv_fukubu_phys_r"] = "iv_fukubu_phys_l",
        ["iv_daitai_phys_l"] = "iv_daitai_phys_r", ["iv_daitai_phys_r"] = "iv_daitai_phys_l",
        ["iv_shiri_l"] = "iv_shiri_r", ["iv_shiri_r"] = "iv_shiri_l",
        ["ya_shiri_phys_l"] = "ya_shiri_phys_r", ["ya_shiri_phys_r"] = "ya_shiri_phys_l",
        ["ya_daitai_phys_l"] = "ya_daitai_phys_r", ["ya_daitai_phys_r"] = "ya_daitai_phys_l",
        ["nf_nipple_l"] = "nf_nipple_r", ["nf_nipple_r"] = "nf_nipple_l",
        ["nf_iv_daitai_phys_l"] = "nf_iv_daitai_phys_r", ["nf_iv_daitai_phys_r"] = "nf_iv_daitai_phys_l",
        ["nf_iv_shiri_l"] = "nf_iv_shiri_r", ["nf_iv_shiri_r"] = "nf_iv_shiri_l",
        ["butt_left"] = "butt_right", ["butt_right"] = "butt_left",
        ["thigh_l"] = "thigh_r", ["thigh_r"] = "thigh_l",
        ["forebreas_l"] = "forebreas_r", ["forebreas_r"] = "forebreas_l",
    };

    /// <summary>
    /// Returns the static solver's configured automatic receivers for data-driven validation.
    /// This is derived from the same region definitions used by the production solve.
    /// </summary>
    internal static IReadOnlyList<string> GetConfiguredAutomaticReceiversForValidation()
        => Regions.SelectMany(static region => region.Support.Concat(region.Transition).Concat(region.Secondary))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static name => name, StringComparer.Ordinal)
            .ToArray();

    public static DeformationQualitySolverDiagnostics Apply(
        IDictionary<string, BoneTransform> output,
        IReadOnlySet<string> explicitTransforms,
        IReadOnlySet<string> liveBoneNames,
        SkeletonCapabilityManifest manifest,
        AdvancedBodyScalingBoneImportanceResult? boneImportance,
        AdvancedBodyScalingSettings settings)
    {
        if (output.Count == 0 || liveBoneNames.Count == 0 || !manifest.BindingCurrent)
            return DeformationQualitySolverDiagnostics.CreateInactive(settings);

        var diagnostics = new MutableDiagnostics(settings);
        diagnostics.CaptureStaticInputScales(output);
        foreach (var region in Regions)
            ApplyRegion(region, output, explicitTransforms, liveBoneNames, manifest, boneImportance, settings, diagnostics);

        if (settings.ProportionalBalanceEnabled)
            ApplyProportionalBalance(output, explicitTransforms, manifest, boneImportance, settings, diagnostics);

        if (settings.SurfaceSmoothnessEnabled)
            ApplySurfaceSmoothness(output, explicitTransforms, manifest, boneImportance, settings, diagnostics);

        // Keep the shared log-space passes together: volume sets intended local size,
        // cross-section shapes its axis distribution, then fairness conditions curvature.
        if (settings.LocalVolumeIntentEnabled)
            ApplyIsolatedConditioningPass(output, diagnostics, () => ApplyLocalVolumeIntent(output, explicitTransforms, manifest, boneImportance, settings, diagnostics));

        if (settings.CrossSectionConditioningEnabled)
            ApplyIsolatedConditioningPass(output, diagnostics, () => ApplyCrossSectionConditioning(output, explicitTransforms, manifest, boneImportance, settings, diagnostics));

        if (settings.ShapeFairnessEnabled)
            ApplyIsolatedConditioningPass(output, diagnostics, () => ApplyShapeFairness(output, explicitTransforms, manifest, boneImportance, settings, diagnostics));

        return diagnostics.Freeze();
    }

    // Conditioning is an optional refinement. A bad optional contribution must never discard the prior safe M5 field.
    private static void ApplyIsolatedConditioningPass(
        IDictionary<string, BoneTransform> output,
        MutableDiagnostics diagnostics,
        Action apply)
    {
        var safeStage = output.ToDictionary(
            static pair => pair.Key,
            static pair => new BoneTransform(pair.Value),
            StringComparer.Ordinal);
        var diagnosticCheckpoint = diagnostics.CaptureContributionCheckpoint();
        try
        {
            apply();
        }
        catch
        {
            output.Clear();
            foreach (var (bone, transform) in safeStage)
                output[bone] = transform;
            diagnostics.RestoreContributionCheckpoint(diagnosticCheckpoint);
            diagnostics.FallbackCount++;
        }
    }

    private static void ApplyRegion(
        RegionDefinition region,
        IDictionary<string, BoneTransform> output,
        IReadOnlySet<string> explicitTransforms,
        IReadOnlySet<string> liveBoneNames,
        SkeletonCapabilityManifest manifest,
        AdvancedBodyScalingBoneImportanceResult? boneImportance,
        AdvancedBodyScalingSettings settings,
        MutableDiagnostics diagnostics)
    {
        if (region.RequiredCapability != SkeletonCapability.None && !manifest.Has(region.RequiredCapability))
            return;

        var sourceScales = region.Primary
            .Where(output.ContainsKey)
            .Select(name => output[name].Scaling)
            .Where(TransformSafety.IsFinite)
            .ToArray();
        if (sourceScales.Length == 0)
            return;

        var primary = Average(sourceScales);
        if (Vector3.DistanceSquared(primary, Vector3.One) <= Epsilon * Epsilon)
            return;

        diagnostics.ActiveRegions.Add(region.Name);
        diagnostics.PrimaryCount += region.Primary.Count(name => output.ContainsKey(name));
        ApplyAutomaticBones(region, region.Support, ContributionKind.Support, 0.42f, primary, output, explicitTransforms, liveBoneNames, manifest, boneImportance, settings, diagnostics);
        ApplyAutomaticBones(region, region.Transition, ContributionKind.Transition, 0.25f, primary, output, explicitTransforms, liveBoneNames, manifest, boneImportance, settings, diagnostics);
        ApplyAutomaticBones(region, region.Secondary, ContributionKind.Secondary, 0.32f, primary, output, explicitTransforms, liveBoneNames, manifest, boneImportance, settings, diagnostics);
    }

    private static void ApplyAutomaticBones(
        RegionDefinition region,
        IReadOnlyList<string> bones,
        ContributionKind kind,
        float baseWeight,
        Vector3 primary,
        IDictionary<string, BoneTransform> output,
        IReadOnlySet<string> explicitTransforms,
        IReadOnlySet<string> liveBoneNames,
        SkeletonCapabilityManifest manifest,
        AdvancedBodyScalingBoneImportanceResult? boneImportance,
        AdvancedBodyScalingSettings settings,
        MutableDiagnostics diagnostics)
    {
        foreach (var bone in bones)
        {
            if (!liveBoneNames.Contains(bone))
                continue;
            if (explicitTransforms.Contains(bone))
                continue;

            var metadata = BoneData.GetMetadata(bone);
            if (!CanContributeAutomatically(metadata, manifest))
            {
                diagnostics.FallbackCount++;
                continue;
            }
            if (IsExcludedRole(metadata))
                continue;

            var weight = baseWeight * GetImportanceWeight(bone, boneImportance);
            if (kind == ContributionKind.Secondary)
                weight *= GetSecondaryWeight(metadata, manifest);
            if (weight <= Epsilon)
                continue;

            // A paired support bone follows the corresponding curated primary side when one
            // exists. Central controls retain the shared regional intent.
            var sideAwarePrimary = GetPrimaryIntentForBone(region, bone, primary, output);
            var target = BuildVolumeConsciousScale(sideAwarePrimary, weight);
            if (!TransformSafety.IsFinite(target))
            {
                diagnostics.ClampedCount++;
                continue;
            }

            // A second region may touch a shared boundary. Blend once rather than stacking full contributions.
            if (output.TryGetValue(bone, out var existing) && existing.IsEdited())
            {
                target = Vector3.Lerp(existing.Scaling, target, 0.5f);
                diagnostics.DoubleContributionPreventions++;
            }

            if (!ApplyBilateralNormalization(bone, target, region, output, explicitTransforms, liveBoneNames, settings, diagnostics))
            {
                var before = output.TryGetValue(bone, out var prior) ? prior.Scaling : Vector3.One;
                output[bone] = new BoneTransform { Scaling = target };
                diagnostics.RecordAutomaticBone(bone, region.Name);
                diagnostics.RecordScaleDelta(bone, DeformationContributionSource.AutomaticSupport, before, target);
            }

            switch (kind)
            {
                case ContributionKind.Support:
                    diagnostics.SupportCount++;
                    break;
                case ContributionKind.Transition:
                    diagnostics.TransitionCount++;
                    break;
                case ContributionKind.Secondary:
                    diagnostics.SecondaryCount++;
                    diagnostics.SecondaryMagnitude += Vector3.Distance(target, Vector3.One);
                    diagnostics.RecordSecondary(metadata.Origin);
                    break;
            }
        }
    }

    private static bool ApplyBilateralNormalization(
        string bone,
        Vector3 target,
        RegionDefinition region,
        IDictionary<string, BoneTransform> output,
        IReadOnlySet<string> explicitTransforms,
        IReadOnlySet<string> liveBoneNames,
        AdvancedBodyScalingSettings settings,
        MutableDiagnostics diagnostics)
    {
        if (!settings.BilateralConsistencyEnabled)
            return false;

        var mirror = BoneData.GetAutomationMirror(bone)
            ?? (CuratedSecondaryMirrors.TryGetValue(bone, out var curatedMirror) ? curatedMirror : null);
        if (mirror == null || !liveBoneNames.Contains(mirror) || explicitTransforms.Contains(mirror))
            return false;

        // Explicit left/right primary intent is authoritative. A region can still generate
        // automatic support for each side, but it must not collapse that deliberate contrast.
        if (HasMateriallyAsymmetricPrimaryIntent(region, output, explicitTransforms))
            return false;

        if (output.TryGetValue(mirror, out var other) && other.IsEdited())
            target = Vector3.Lerp(target, other.Scaling, 0.5f);

        var previous = output.TryGetValue(bone, out var existing) ? existing.Scaling : Vector3.One;
        var previousMirror = output.TryGetValue(mirror, out var existingMirror) ? existingMirror.Scaling : Vector3.One;
        output[bone] = new BoneTransform { Scaling = target };
        output[mirror] = new BoneTransform { Scaling = target };
        diagnostics.RecordAutomaticBone(bone, region.Name);
        diagnostics.RecordAutomaticBone(mirror, region.Name);
        diagnostics.RecordScaleDelta(bone, DeformationContributionSource.AutomaticSupport, previous, target);
        diagnostics.RecordScaleDelta(mirror, DeformationContributionSource.AutomaticSupport, previousMirror, target);
        diagnostics.BilateralNormalizations++;
        return true;
    }

    private static Vector3 GetPrimaryIntentForBone(
        RegionDefinition region,
        string targetBone,
        Vector3 sharedPrimary,
        IDictionary<string, BoneTransform> output)
    {
        if (!TryGetMirrorOrientation(targetBone, out var targetOrientation))
            return sharedPrimary;

        var scaleSum = Vector3.Zero;
        var scaleCount = 0;
        foreach (var primaryName in region.Primary)
        {
            if (!output.TryGetValue(primaryName, out var primaryTransform)
                || !TransformSafety.IsFinite(primaryTransform.Scaling))
                continue;

            // Central primaries provide shared context to each side. Paired primaries only
            // contribute when their curated mirror orientation matches the target control.
            if (!TryGetMirrorOrientation(primaryName, out var primaryOrientation)
                || primaryOrientation == targetOrientation)
            {
                scaleSum += primaryTransform.Scaling;
                scaleCount++;
            }
        }

        return scaleCount == 0 ? sharedPrimary : scaleSum / scaleCount;
    }

    private static bool HasMateriallyAsymmetricPrimaryIntent(
        RegionDefinition region,
        IDictionary<string, BoneTransform> output,
        IReadOnlySet<string> explicitTransforms)
    {
        foreach (var primary in region.Primary)
        {
            var mirror = BoneData.GetAutomationMirror(primary);
            if (mirror == null || string.CompareOrdinal(primary, mirror) >= 0
                || !output.TryGetValue(primary, out var left)
                || !output.TryGetValue(mirror, out var right)
                || (!explicitTransforms.Contains(primary) && !explicitTransforms.Contains(mirror)))
                continue;

            if (Vector3.Distance(left.Scaling, right.Scaling) > BilateralIntentTolerance)
                return true;
        }

        return false;
    }

    private static bool TryGetMirrorOrientation(string bone, out bool canonicalOrientation)
    {
        var mirror = BoneData.GetAutomationMirror(bone)
            ?? (CuratedSecondaryMirrors.TryGetValue(bone, out var curatedMirror) ? curatedMirror : null);
        if (mirror == null)
        {
            canonicalOrientation = false;
            return false;
        }

        // Orientation comes from a curated mirror pair, not a guessed name suffix.
        canonicalOrientation = string.CompareOrdinal(bone, mirror) < 0;
        return true;
    }

    private static void ApplyProportionalBalance(
        IDictionary<string, BoneTransform> output,
        IReadOnlySet<string> explicitTransforms,
        SkeletonCapabilityManifest manifest,
        AdvancedBodyScalingBoneImportanceResult? boneImportance,
        AdvancedBodyScalingSettings settings,
        MutableDiagnostics diagnostics)
    {
        foreach (var relationship in ProportionalRelationships)
        {
            var source = RegionsByName[relationship.SourceRegion];
            var receiver = RegionsByName[relationship.ReceiverRegion];
            if (!TryGetExplicitPrimaryIntent(source, output, explicitTransforms, out var requestedScale))
                continue;

            // Authored receiver rows remain anchors. Only non-explicit, unlocked automatic
            // support, transition, or trusted secondary controls may receive this correction.
            var candidates = GetAutomaticRegionBones(receiver, output, explicitTransforms, manifest, diagnostics).ToArray();
            if (candidates.Length == 0)
            {
                diagnostics.ProportionalSkippedExplicitOrLocked++;
                continue;
            }

            var desiredSupport = BuildVolumeConsciousScale(requestedScale, relationship.SupportWeight);
            var corrected = false;
            var maximumCorrection = 0f;
            var maximumImbalanceBefore = 0f;
            var maximumImbalanceAfter = 0f;
            foreach (var bone in candidates)
            {
                var current = output[bone].Scaling;
                maximumImbalanceBefore = MathF.Max(maximumImbalanceBefore, Vector3.Distance(desiredSupport, current));
                var correction = (desiredSupport - current)
                    * (settings.ProportionalBalanceStrength * GetImportanceWeight(bone, boneImportance));
                correction = Vector3.Clamp(correction,
                    new Vector3(-relationship.MaxCorrection),
                    new Vector3(relationship.MaxCorrection));
                if (correction.LengthSquared() <= Epsilon * Epsilon)
                    continue;

                var candidate = Vector3.Clamp(current + correction, new Vector3(MinAutoScale), new Vector3(MaxAutoScale));
                if (!TransformSafety.IsFinite(candidate))
                {
                    diagnostics.FallbackCount++;
                    continue;
                }

                output[bone].Scaling = candidate;
                diagnostics.RecordContribution(bone, DeformationContributionSource.ProportionalBalance);
                diagnostics.RecordScaleDelta(bone, DeformationContributionSource.ProportionalBalance, current, candidate);
                corrected = true;
                maximumCorrection = MathF.Max(maximumCorrection, correction.Length());
                maximumImbalanceAfter = MathF.Max(maximumImbalanceAfter, Vector3.Distance(desiredSupport, candidate));
            }

            if (corrected)
                diagnostics.RecordProportional(relationship, maximumCorrection, maximumImbalanceBefore, maximumImbalanceAfter);
        }
    }

    private static void ApplySurfaceSmoothness(
        IDictionary<string, BoneTransform> output,
        IReadOnlySet<string> explicitTransforms,
        SkeletonCapabilityManifest manifest,
        AdvancedBodyScalingBoneImportanceResult? boneImportance,
        AdvancedBodyScalingSettings settings,
        MutableDiagnostics diagnostics)
    {
        var eligible = diagnostics.AutomaticBones
            .Where(bone => IsEligibleAutomaticBone(bone, output, explicitTransforms, manifest))
            .ToHashSet(StringComparer.Ordinal);
        if (eligible.Count == 0)
            return;

        var preMagnitudes = GetAutomaticRegionMagnitudes(output, diagnostics.AutomaticBoneRegions, eligible);
        var preGradient = GetMaximumGradient(output, eligible);
        var neighbors = new Dictionary<string, WeightedScaleAccumulator>(StringComparer.Ordinal);

        foreach (var edge in SmoothingEdges)
        {
            if (!output.TryGetValue(edge.First, out var first) || !output.TryGetValue(edge.Second, out var second)
                || !TransformSafety.IsFinite(first.Scaling) || !TransformSafety.IsFinite(second.Scaling))
            {
                diagnostics.SurfaceSkippedBoundaries++;
                continue;
            }

            var firstEligible = eligible.Contains(edge.First);
            var secondEligible = eligible.Contains(edge.Second);
            if (!firstEligible && !secondEligible)
                continue;

            if (firstEligible)
                AddNeighbor(neighbors, edge.First, second.Scaling, edge.Weight);
            if (secondEligible)
                AddNeighbor(neighbors, edge.Second, first.Scaling, edge.Weight);
        }

        var pending = new Dictionary<string, Vector3>(StringComparer.Ordinal);
        foreach (var (bone, accumulator) in neighbors)
        {
            if (accumulator.Weight <= Epsilon || !output.TryGetValue(bone, out var transform))
                continue;

            var average = accumulator.Sum / accumulator.Weight;
            if (Vector3.Distance(transform.Scaling, average) < GradientThreshold)
                continue;

            var correction = (average - transform.Scaling)
                * (settings.SurfaceSmoothnessStrength * (accumulator.Weight / accumulator.Count) * GetImportanceWeight(bone, boneImportance));
            correction = Vector3.Clamp(correction, new Vector3(-MaxSurfaceCorrection), new Vector3(MaxSurfaceCorrection));
            var candidate = Vector3.Clamp(transform.Scaling + correction, new Vector3(MinAutoScale), new Vector3(MaxAutoScale));
            if (!TransformSafety.IsFinite(candidate))
            {
                diagnostics.FallbackCount++;
                continue;
            }

            pending[bone] = candidate;
        }

        foreach (var (bone, scale) in pending)
        {
            var before = output[bone].Scaling;
            output[bone].Scaling = scale;
            diagnostics.RecordSurfaceSmoothness(bone);
            diagnostics.RecordScaleDelta(bone, DeformationContributionSource.SurfaceSmoothness, before, scale);
        }

        var preservationError = PreserveAutomaticRegionMagnitude(output, diagnostics.AutomaticBoneRegions, eligible, preMagnitudes);
        diagnostics.RecordSurfaceMetrics(preGradient, GetMaximumGradient(output, eligible), preservationError);
    }

    private static void ApplyLocalVolumeIntent(
        IDictionary<string, BoneTransform> output,
        IReadOnlySet<string> explicitTransforms,
        SkeletonCapabilityManifest manifest,
        AdvancedBodyScalingBoneImportanceResult? boneImportance,
        AdvancedBodyScalingSettings settings,
        MutableDiagnostics diagnostics)
    {
        var eligible = GetEligibleAutomaticBones(output, explicitTransforms, manifest, diagnostics);
        diagnostics.VolumeSkippedUntrustedOrConstrained += diagnostics.AutomaticBones.Count - eligible.Count;
        if (eligible.Count == 0)
            return;

        foreach (var bone in eligible)
        {
            if (!output.TryGetValue(bone, out var transform)
                || !AdvancedBodyScalingLogScale.TryCreate(transform.Scaling, out var shape)
                || !diagnostics.AutomaticBoneRegions.TryGetValue(bone, out var regionName)
                || !RegionsByName.TryGetValue(regionName, out var region)
                || !TryGetRegionPrimaryLogMean(region, output, out var targetMean))
            {
                diagnostics.VolumeSkippedUntrustedOrConstrained++;
                continue;
            }

            var before = MathF.Abs(targetMean - shape.ScalarMean);
            if (before <= Epsilon)
                continue;

            var correction = Math.Clamp(
                (targetMean - shape.ScalarMean)
                * (settings.LocalVolumeIntentStrength * 0.34f * GetImportanceWeight(bone, boneImportance)),
                -MaxVolumeMeanCorrection,
                MaxVolumeMeanCorrection);
            if (MathF.Abs(correction) <= Epsilon)
                continue;

            var candidateShape = shape.WithMean(shape.ScalarMean + correction);
            if (!candidateShape.TryReconstruct(out var candidate))
            {
                diagnostics.FallbackCount++;
                continue;
            }

            candidate = Vector3.Clamp(candidate, new Vector3(MinAutoScale), new Vector3(MaxAutoScale));
            if (!TransformSafety.IsFinite(candidate))
            {
                diagnostics.FallbackCount++;
                continue;
            }

            var priorScale = transform.Scaling;
            transform.Scaling = candidate;
            diagnostics.RecordVolumeIntent(bone, regionName, before, MathF.Abs(targetMean - (shape.ScalarMean + correction)), MathF.Abs(correction));
            diagnostics.RecordScaleDelta(bone, DeformationContributionSource.LocalVolumeIntent, priorScale, candidate);
        }
    }

    private static void ApplyCrossSectionConditioning(
        IDictionary<string, BoneTransform> output,
        IReadOnlySet<string> explicitTransforms,
        SkeletonCapabilityManifest manifest,
        AdvancedBodyScalingBoneImportanceResult? boneImportance,
        AdvancedBodyScalingSettings settings,
        MutableDiagnostics diagnostics)
    {
        var eligible = GetEligibleAutomaticBones(output, explicitTransforms, manifest, diagnostics);
        diagnostics.CrossSectionSkippedUntrustedOrConstrained += diagnostics.AutomaticBones.Count - eligible.Count;
        if (eligible.Count == 0)
            return;

        foreach (var bone in eligible)
        {
            if (!output.TryGetValue(bone, out var transform)
                || !AdvancedBodyScalingLogScale.TryCreate(transform.Scaling, out var shape))
            {
                diagnostics.CrossSectionSkippedUntrustedOrConstrained++;
                continue;
            }

            var before = shape.Anisotropy;
            if (before <= CrossSectionAnisotropyStart)
                continue;

            var neighboringDeviation = GetNeighboringDeviation(bone, output);
            var blendedTarget = Vector3.Lerp(shape.Deviation, neighboringDeviation, 0.22f);
            // Retain a portion of the local anisotropy, then only temper the excessive automatic tail.
            var correctionWeight = settings.CrossSectionConditioningStrength
                * AdvancedBodyScalingShapeConditioningMath.SmoothStep(CrossSectionAnisotropyStart, 0.56f, before)
                * GetImportanceWeight(bone, boneImportance);
            var targetDeviation = Vector3.Lerp(shape.Deviation, blendedTarget * 0.55f, correctionWeight);
            var delta = Vector3.Clamp(targetDeviation - shape.Deviation,
                new Vector3(-MaxCrossSectionLogCorrection), new Vector3(MaxCrossSectionLogCorrection));
            if (delta.LengthSquared() <= Epsilon * Epsilon)
                continue;

            var candidateShape = shape.WithDeviation(shape.Deviation + delta);
            if (!candidateShape.TryReconstruct(out var candidate))
            {
                diagnostics.FallbackCount++;
                continue;
            }

            candidate = Vector3.Clamp(candidate, new Vector3(MinAutoScale), new Vector3(MaxAutoScale));
            if (!TransformSafety.IsFinite(candidate)
                || !AdvancedBodyScalingLogScale.TryCreate(candidate, out var afterShape))
            {
                diagnostics.FallbackCount++;
                continue;
            }

            var priorScale = transform.Scaling;
            transform.Scaling = candidate;
            diagnostics.RecordCrossSection(
                bone,
                diagnostics.AutomaticBoneRegions.GetValueOrDefault(bone, "automatic"),
                before,
                afterShape.Anisotropy,
                delta.Length());
            diagnostics.RecordScaleDelta(bone, DeformationContributionSource.CrossSectionConditioning, priorScale, candidate);
        }
    }

    private static void ApplyShapeFairness(
        IDictionary<string, BoneTransform> output,
        IReadOnlySet<string> explicitTransforms,
        SkeletonCapabilityManifest manifest,
        AdvancedBodyScalingBoneImportanceResult? boneImportance,
        AdvancedBodyScalingSettings settings,
        MutableDiagnostics diagnostics)
    {
        var eligible = GetEligibleAutomaticBones(output, explicitTransforms, manifest, diagnostics);
        if (eligible.Count == 0)
            return;

        var preMagnitudes = GetAutomaticRegionMagnitudes(output, diagnostics.AutomaticBoneRegions, eligible);
        var pending = new Dictionary<string, (Vector3 Sum, int Count)>(StringComparer.Ordinal);

        foreach (var chain in FairnessChains)
        {
            var chainCorrected = false;
            for (var index = 1; index < chain.Bones.Count - 1; ++index)
            {
                var previousName = chain.Bones[index - 1];
                var currentName = chain.Bones[index];
                var nextName = chain.Bones[index + 1];
                if (!eligible.Contains(currentName)
                    || !output.TryGetValue(previousName, out var previous)
                    || !output.TryGetValue(currentName, out var current)
                    || !output.TryGetValue(nextName, out var next)
                    || !AdvancedBodyScalingLogScale.TryCreate(previous.Scaling, out var previousShape)
                    || !AdvancedBodyScalingLogScale.TryCreate(current.Scaling, out var currentShape)
                    || !AdvancedBodyScalingLogScale.TryCreate(next.Scaling, out var nextShape))
                {
                    diagnostics.FairnessSkippedBoundaries++;
                    continue;
                }

                var previousLog = previousShape.Mean + previousShape.Deviation;
                var currentLog = currentShape.Mean + currentShape.Deviation;
                var nextLog = nextShape.Mean + nextShape.Deviation;
                var secondDifference = nextLog - (currentLog * 2f) + previousLog;
                var before = secondDifference.Length();
                if (before <= FairnessCurvatureThreshold)
                    continue;

                var correction = Vector3.Clamp(
                    secondDifference * (0.25f * settings.ShapeFairnessStrength * GetImportanceWeight(currentName, boneImportance)),
                    new Vector3(-MaxFairnessLogCorrection), new Vector3(MaxFairnessLogCorrection));
                if (correction.LengthSquared() <= Epsilon * Epsilon)
                    continue;

                if (pending.TryGetValue(currentName, out var accumulator))
                    pending[currentName] = (accumulator.Sum + correction, accumulator.Count + 1);
                else
                    pending[currentName] = (correction, 1);

                diagnostics.RecordFairnessBefore(chain.Name, before);
                chainCorrected = true;
            }

            if (chainCorrected)
                diagnostics.FairnessChainsCorrected.Add(chain.Name);
        }

        foreach (var (bone, correction) in pending)
        {
            if (!output.TryGetValue(bone, out var transform)
                || !AdvancedBodyScalingLogScale.TryCreate(transform.Scaling, out var shape))
                continue;

            var delta = Vector3.Clamp(correction.Sum / correction.Count,
                new Vector3(-MaxFairnessLogCorrection), new Vector3(MaxFairnessLogCorrection));
            var candidateShape = shape.WithLog(shape.Mean + shape.Deviation + delta);
            if (!candidateShape.TryReconstruct(out var candidate))
            {
                diagnostics.FallbackCount++;
                continue;
            }

            candidate = Vector3.Clamp(candidate, new Vector3(MinAutoScale), new Vector3(MaxAutoScale));
            if (!TransformSafety.IsFinite(candidate))
            {
                diagnostics.FallbackCount++;
                continue;
            }

            var priorScale = transform.Scaling;
            transform.Scaling = candidate;
            diagnostics.RecordFairnessCorrection(bone, delta.Length());
            diagnostics.RecordScaleDelta(bone, DeformationContributionSource.ShapeFairness, priorScale, candidate);
        }

        var preservationError = PreserveAutomaticRegionMagnitude(output, diagnostics.AutomaticBoneRegions, eligible, preMagnitudes);
        diagnostics.RecordFairnessAfter(GetMaximumSecondDifference(output, eligible), preservationError);
    }

    private static HashSet<string> GetEligibleAutomaticBones(
        IDictionary<string, BoneTransform> output,
        IReadOnlySet<string> explicitTransforms,
        SkeletonCapabilityManifest manifest,
        MutableDiagnostics diagnostics)
        => diagnostics.AutomaticBones
            .Where(bone => IsEligibleAutomaticBone(bone, output, explicitTransforms, manifest))
            .ToHashSet(StringComparer.Ordinal);

    private static bool TryGetRegionPrimaryLogMean(
        RegionDefinition region,
        IDictionary<string, BoneTransform> output,
        out float mean)
    {
        var means = region.Primary
            .Where(output.ContainsKey)
            .Select(name => output[name].Scaling)
            .Select(scale => AdvancedBodyScalingLogScale.TryCreate(scale, out var shape) ? shape.ScalarMean : float.NaN)
            .Where(float.IsFinite)
            .ToArray();
        if (means.Length == 0)
        {
            mean = 0f;
            return false;
        }

        mean = means.Average();
        return true;
    }

    private static Vector3 GetNeighboringDeviation(string bone, IDictionary<string, BoneTransform> output)
    {
        var sum = Vector3.Zero;
        var count = 0;
        foreach (var edge in SmoothingEdges)
        {
            var neighbor = string.Equals(edge.First, bone, StringComparison.Ordinal) ? edge.Second
                : string.Equals(edge.Second, bone, StringComparison.Ordinal) ? edge.First
                : null;
            if (neighbor == null || !output.TryGetValue(neighbor, out var transform)
                || !AdvancedBodyScalingLogScale.TryCreate(transform.Scaling, out var shape))
                continue;

            sum += shape.Deviation;
            count++;
        }

        return count == 0 ? Vector3.Zero : sum / count;
    }

    private static float GetMaximumSecondDifference(IDictionary<string, BoneTransform> output, IReadOnlySet<string> eligible)
    {
        var maximum = 0f;
        foreach (var chain in FairnessChains)
        {
            for (var index = 1; index < chain.Bones.Count - 1; ++index)
            {
                var currentName = chain.Bones[index];
                if (!eligible.Contains(currentName)
                    || !output.TryGetValue(chain.Bones[index - 1], out var previous)
                    || !output.TryGetValue(currentName, out var current)
                    || !output.TryGetValue(chain.Bones[index + 1], out var next)
                    || !AdvancedBodyScalingLogScale.TryCreate(previous.Scaling, out var previousShape)
                    || !AdvancedBodyScalingLogScale.TryCreate(current.Scaling, out var currentShape)
                    || !AdvancedBodyScalingLogScale.TryCreate(next.Scaling, out var nextShape))
                    continue;

                var previousLog = previousShape.Mean + previousShape.Deviation;
                var currentLog = currentShape.Mean + currentShape.Deviation;
                var nextLog = nextShape.Mean + nextShape.Deviation;
                maximum = MathF.Max(maximum, (nextLog - (currentLog * 2f) + previousLog).Length());
            }
        }

        return maximum;
    }

    private static IEnumerable<string> GetAutomaticRegionBones(
        RegionDefinition region,
        IDictionary<string, BoneTransform> output,
        IReadOnlySet<string> explicitTransforms,
        SkeletonCapabilityManifest manifest,
        MutableDiagnostics diagnostics)
        => EnumerateRegionBones(region).Where(bone => diagnostics.AutomaticBones.Contains(bone)
            && IsEligibleAutomaticBone(bone, output, explicitTransforms, manifest));

    private static bool TryGetExplicitPrimaryIntent(
        RegionDefinition region,
        IDictionary<string, BoneTransform> output,
        IReadOnlySet<string> explicitTransforms,
        out Vector3 intent)
    {
        var scales = region.Primary
            .Where(explicitTransforms.Contains)
            .Where(output.ContainsKey)
            .Select(name => output[name].Scaling)
            .Where(TransformSafety.IsFinite)
            .ToArray();
        if (scales.Length == 0)
        {
            intent = Vector3.One;
            return false;
        }

        intent = Average(scales);
        return Vector3.DistanceSquared(intent, Vector3.One) > Epsilon * Epsilon;
    }

    private static IEnumerable<string> EnumerateRegionBones(RegionDefinition region)
        => region.Primary.Concat(region.Support).Concat(region.Transition).Concat(region.Secondary);

    private static bool IsEligibleAutomaticBone(
        string bone,
        IDictionary<string, BoneTransform> output,
        IReadOnlySet<string> explicitTransforms,
        SkeletonCapabilityManifest manifest)
    {
        if (explicitTransforms.Contains(bone) || !output.TryGetValue(bone, out var transform)
            || !TransformSafety.IsFinite(transform.Scaling)
            || transform.LockState != BoneLockState.Unlocked || transform.HasPinnedScaleAxes())
            return false;

        var metadata = BoneData.GetMetadata(bone);
        return CanContributeAutomatically(metadata, manifest) && !IsExcludedRole(metadata);
    }

    private static bool IsExcludedRole(BoneMetadata metadata)
        => metadata.Role is BoneFunctionalRole.ClothingRig
            or BoneFunctionalRole.PropRig
            or BoneFunctionalRole.ArticulatedAppendage
            or BoneFunctionalRole.ArticulatedBodyFeature
            or BoneFunctionalRole.Unknown;

    private static void AddNeighbor(
        IDictionary<string, WeightedScaleAccumulator> neighbors,
        string bone,
        Vector3 scale,
        float weight)
    {
        if (!neighbors.TryGetValue(bone, out var accumulator))
        {
            accumulator = new WeightedScaleAccumulator();
            neighbors[bone] = accumulator;
        }

        accumulator.Add(scale, weight);
    }

    private static Dictionary<string, float> GetAutomaticRegionMagnitudes(
        IDictionary<string, BoneTransform> output,
        IReadOnlyDictionary<string, string> automaticRegions,
        IReadOnlySet<string> eligible)
    {
        var magnitudes = new Dictionary<string, float>(StringComparer.Ordinal);
        foreach (var (bone, region) in automaticRegions)
        {
            if (!eligible.Contains(bone) || !output.TryGetValue(bone, out var transform))
                continue;

            magnitudes[region] = magnitudes.GetValueOrDefault(region) + Vector3.Distance(transform.Scaling, Vector3.One);
        }

        return magnitudes;
    }

    private static float PreserveAutomaticRegionMagnitude(
        IDictionary<string, BoneTransform> output,
        IReadOnlyDictionary<string, string> automaticRegions,
        IReadOnlySet<string> eligible,
        IReadOnlyDictionary<string, float> preMagnitudes)
    {
        var maxError = 0f;
        foreach (var (region, originalMagnitude) in preMagnitudes)
        {
            if (originalMagnitude <= Epsilon)
                continue;

            var currentMagnitude = GetAutomaticRegionMagnitude(output, automaticRegions, eligible, region);
            if (currentMagnitude <= Epsilon)
            {
                maxError = MathF.Max(maxError, originalMagnitude);
                continue;
            }

            var normalization = Math.Clamp(originalMagnitude / currentMagnitude, 0.85f, 1.15f);
            foreach (var (bone, sourceRegion) in automaticRegions)
            {
                if (!string.Equals(region, sourceRegion, StringComparison.Ordinal) || !eligible.Contains(bone)
                    || !output.TryGetValue(bone, out var transform))
                    continue;

                var rescaled = Vector3.One + ((transform.Scaling - Vector3.One) * normalization);
                if (TransformSafety.IsFinite(rescaled))
                    transform.Scaling = Vector3.Clamp(rescaled, new Vector3(MinAutoScale), new Vector3(MaxAutoScale));
            }

            maxError = MathF.Max(maxError, MathF.Abs(originalMagnitude - GetAutomaticRegionMagnitude(output, automaticRegions, eligible, region)));
        }

        return maxError;
    }

    private static float GetAutomaticRegionMagnitude(
        IDictionary<string, BoneTransform> output,
        IReadOnlyDictionary<string, string> automaticRegions,
        IReadOnlySet<string> eligible,
        string targetRegion)
    {
        var magnitude = 0f;
        foreach (var (bone, region) in automaticRegions)
        {
            if (!string.Equals(region, targetRegion, StringComparison.Ordinal) || !eligible.Contains(bone)
                || !output.TryGetValue(bone, out var transform))
                continue;

            magnitude += Vector3.Distance(transform.Scaling, Vector3.One);
        }

        return magnitude;
    }

    private static float GetMaximumGradient(IDictionary<string, BoneTransform> output, IReadOnlySet<string> eligible)
    {
        var maximum = 0f;
        foreach (var edge in SmoothingEdges)
        {
            if (!eligible.Contains(edge.First) && !eligible.Contains(edge.Second))
                continue;
            if (!output.TryGetValue(edge.First, out var first) || !output.TryGetValue(edge.Second, out var second))
                continue;

            maximum = MathF.Max(maximum, Vector3.Distance(first.Scaling, second.Scaling));
        }

        return maximum;
    }

    private static bool CanContributeAutomatically(BoneMetadata metadata, SkeletonCapabilityManifest manifest)
    {
        if (!metadata.HasTrust(BoneAutomationTrust.AdvancedCorrectiveSafe))
            return false;

        return metadata.Origin switch
        {
            BoneOrigin.IVCS1 => manifest.GetState(SkeletonCapability.IVCS1) is SkeletonCapabilityState.Present or SkeletonCapabilityState.Partial,
            BoneOrigin.IVCS2 => manifest.GetState(SkeletonCapability.IVCS2) is SkeletonCapabilityState.Present or SkeletonCapabilityState.Partial,
            BoneOrigin.YAS => manifest.GetState(SkeletonCapability.YAS) is SkeletonCapabilityState.Present or SkeletonCapabilityState.Partial,
            BoneOrigin.NFLB => manifest.GetState(SkeletonCapability.NFLB) is SkeletonCapabilityState.Present or SkeletonCapabilityState.Partial,
            BoneOrigin.Skelomae => manifest.GetState(SkeletonCapability.Skelomae) is SkeletonCapabilityState.Present or SkeletonCapabilityState.Partial,
            _ => true,
        };
    }

    private static float GetSecondaryWeight(BoneMetadata metadata, SkeletonCapabilityManifest manifest)
    {
        var capabilityWeight = metadata.Origin switch
        {
            BoneOrigin.IVCS2 => manifest.GetState(SkeletonCapability.IVCS2) == SkeletonCapabilityState.Present ? 1f : 0.70f,
            BoneOrigin.YAS => manifest.GetState(SkeletonCapability.YAS) == SkeletonCapabilityState.Present ? 0.85f : 0.60f,
            BoneOrigin.NFLB => manifest.GetState(SkeletonCapability.NFLB) == SkeletonCapabilityState.Present ? 0.70f : 0.45f,
            BoneOrigin.Skelomae => manifest.GetState(SkeletonCapability.Skelomae) == SkeletonCapabilityState.Present ? 0.70f : 0.45f,
            _ => 1f,
        };
        return capabilityWeight;
    }

    private static float GetImportanceWeight(string bone, AdvancedBodyScalingBoneImportanceResult? result)
    {
        if (result == null || !result.ModelDerivedActive || !result.Scores.TryGetValue(bone, out var score))
            return 0.75f;
        return Math.Clamp(0.35f + (score * 0.65f), 0.35f, 1f);
    }

    private static Vector3 BuildVolumeConsciousScale(Vector3 primary, float weight)
    {
        var delta = primary - Vector3.One;
        // Cross axes carry more fullness; longitudinal movement is intentionally tempered.
        var target = new Vector3(
            1f + (delta.X * weight),
            1f + (delta.Y * weight * 0.62f),
            1f + (delta.Z * weight));
        return Vector3.Clamp(target, new Vector3(MinAutoScale), new Vector3(MaxAutoScale));
    }

    private static Vector3 Average(IReadOnlyList<Vector3> values)
    {
        var result = Vector3.Zero;
        foreach (var value in values)
            result += value;
        return result / values.Count;
    }

    private sealed class MutableDiagnostics
    {
        public MutableDiagnostics(AdvancedBodyScalingSettings settings)
        {
            ProportionalBalanceEnabled = settings.ProportionalBalanceEnabled;
            ProportionalBalanceStrength = settings.ProportionalBalanceStrength;
            SurfaceSmoothnessEnabled = settings.SurfaceSmoothnessEnabled;
            SurfaceSmoothnessStrength = settings.SurfaceSmoothnessStrength;
            CrossSectionConditioningEnabled = settings.CrossSectionConditioningEnabled;
            CrossSectionConditioningStrength = settings.CrossSectionConditioningStrength;
            ShapeFairnessEnabled = settings.ShapeFairnessEnabled;
            ShapeFairnessStrength = settings.ShapeFairnessStrength;
            LocalVolumeIntentEnabled = settings.LocalVolumeIntentEnabled;
            LocalVolumeIntentStrength = settings.LocalVolumeIntentStrength;
        }

        public HashSet<string> ActiveRegions { get; } = new(StringComparer.Ordinal);
        public HashSet<string> AutomaticBones { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, string> AutomaticBoneRegions { get; } = new(StringComparer.Ordinal);
        public Dictionary<BoneOrigin, int> SecondaryOrigins { get; } = new();
        public HashSet<string> ProportionalRegions { get; } = new(StringComparer.Ordinal);
        public HashSet<string> CorrectedRelationships { get; } = new(StringComparer.Ordinal);
        public HashSet<string> SurfaceRegions { get; } = new(StringComparer.Ordinal);
        public HashSet<string> SurfaceBones { get; } = new(StringComparer.Ordinal);
        public HashSet<string> CrossSectionRegions { get; } = new(StringComparer.Ordinal);
        public HashSet<string> FairnessChainsCorrected { get; } = new(StringComparer.Ordinal);
        public HashSet<string> FairnessBones { get; } = new(StringComparer.Ordinal);
        public HashSet<string> VolumeRegions { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, DeformationContributionSource> ContributionSources { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, Dictionary<DeformationContributionSource, Vector3>> ContributionScaleDeltas { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, Vector3> StaticInputScales { get; } = new(StringComparer.Ordinal);
        public readonly bool ProportionalBalanceEnabled;
        public readonly float ProportionalBalanceStrength;
        public readonly bool SurfaceSmoothnessEnabled;
        public readonly float SurfaceSmoothnessStrength;
        public readonly bool CrossSectionConditioningEnabled;
        public readonly float CrossSectionConditioningStrength;
        public readonly bool ShapeFairnessEnabled;
        public readonly float ShapeFairnessStrength;
        public readonly bool LocalVolumeIntentEnabled;
        public readonly float LocalVolumeIntentStrength;
        public int PrimaryCount;
        public int SupportCount;
        public int TransitionCount;
        public int SecondaryCount;
        public int ClampedCount;
        public int DoubleContributionPreventions;
        public int FallbackCount;
        public int BilateralNormalizations;
        public int ProportionalSkippedExplicitOrLocked;
        public int SurfaceSkippedBoundaries;
        public int CrossSectionSkippedUntrustedOrConstrained;
        public int VolumeSkippedUntrustedOrConstrained;
        public int FairnessSkippedBoundaries;
        public float SecondaryMagnitude;
        public float MaximumProportionalCorrection;
        public float MaximumProportionalImbalanceBefore;
        public float MaximumProportionalImbalanceAfter;
        public float MaximumPreSmoothingGradient;
        public float MaximumPostSmoothingGradient;
        public float SurfaceMagnitudePreservationError;
        public float MaximumCrossSectionAnisotropyBefore;
        public float MaximumCrossSectionAnisotropyAfter;
        public float MaximumCrossSectionCorrection;
        public float MaximumFairnessSecondDifferenceBefore;
        public float MaximumFairnessSecondDifferenceAfter;
        public float MaximumFairnessCorrection;
        public float FairnessMagnitudePreservationError;
        public float MaximumVolumeErrorBefore;
        public float MaximumVolumeErrorAfter;
        public float MaximumVolumeAxisCorrection;

        public void RecordAutomaticBone(string bone, string region)
        {
            AutomaticBones.Add(bone);
            if (!AutomaticBoneRegions.ContainsKey(bone))
                AutomaticBoneRegions[bone] = region;
            RecordContribution(bone, DeformationContributionSource.AutomaticSupport);
        }

        public void CaptureStaticInputScales(IEnumerable<KeyValuePair<string, BoneTransform>> output)
        {
            StaticInputScales.Clear();
            foreach (var (bone, transform) in output)
            {
                if (TransformSafety.IsFinite(transform.Scaling))
                    StaticInputScales[bone] = transform.Scaling;
            }
        }

        public void RecordContribution(string bone, DeformationContributionSource source)
            => ContributionSources[bone] = ContributionSources.GetValueOrDefault(bone) | source;

        public void RecordScaleDelta(string bone, DeformationContributionSource source, Vector3 before, Vector3 after)
        {
            var delta = after - before;
            if (!TransformSafety.IsFinite(delta) || delta.LengthSquared() <= Epsilon * Epsilon)
                return;
            RecordContribution(bone, source);
            if (!ContributionScaleDeltas.TryGetValue(bone, out var byStage))
                ContributionScaleDeltas[bone] = byStage = new Dictionary<DeformationContributionSource, Vector3>();
            byStage[source] = byStage.GetValueOrDefault(source) + delta;
        }

        public ContributionCheckpoint CaptureContributionCheckpoint()
            => new(
                new Dictionary<string, DeformationContributionSource>(ContributionSources, StringComparer.Ordinal),
                ContributionScaleDeltas.ToDictionary(
                    static pair => pair.Key,
                    static pair => new Dictionary<DeformationContributionSource, Vector3>(pair.Value),
                    StringComparer.Ordinal));

        public void RestoreContributionCheckpoint(ContributionCheckpoint checkpoint)
        {
            ContributionSources.Clear();
            foreach (var (bone, source) in checkpoint.Sources)
                ContributionSources[bone] = source;

            ContributionScaleDeltas.Clear();
            foreach (var (bone, deltas) in checkpoint.ScaleDeltas)
                ContributionScaleDeltas[bone] = new Dictionary<DeformationContributionSource, Vector3>(deltas);
        }

        public void RecordSecondary(BoneOrigin origin)
            => SecondaryOrigins[origin] = SecondaryOrigins.GetValueOrDefault(origin) + 1;

        public sealed record ContributionCheckpoint(
            IReadOnlyDictionary<string, DeformationContributionSource> Sources,
            IReadOnlyDictionary<string, Dictionary<DeformationContributionSource, Vector3>> ScaleDeltas);

        public void RecordProportional(
            RegionRelationship relationship,
            float maximumCorrection,
            float maximumImbalanceBefore,
            float maximumImbalanceAfter)
        {
            ProportionalRegions.Add(relationship.ReceiverRegion);
            CorrectedRelationships.Add($"{relationship.SourceRegion} -> {relationship.ReceiverRegion}");
            MaximumProportionalCorrection = MathF.Max(MaximumProportionalCorrection, maximumCorrection);
            MaximumProportionalImbalanceBefore = MathF.Max(MaximumProportionalImbalanceBefore, maximumImbalanceBefore);
            MaximumProportionalImbalanceAfter = MathF.Max(MaximumProportionalImbalanceAfter, maximumImbalanceAfter);
        }

        public void RecordSurfaceSmoothness(string bone)
        {
            SurfaceBones.Add(bone);
            RecordContribution(bone, DeformationContributionSource.SurfaceSmoothness);
            if (AutomaticBoneRegions.TryGetValue(bone, out var region))
                SurfaceRegions.Add(region);
        }

        public void RecordSurfaceMetrics(float preGradient, float postGradient, float preservationError)
        {
            MaximumPreSmoothingGradient = MathF.Max(MaximumPreSmoothingGradient, preGradient);
            MaximumPostSmoothingGradient = MathF.Max(MaximumPostSmoothingGradient, postGradient);
            SurfaceMagnitudePreservationError = MathF.Max(SurfaceMagnitudePreservationError, preservationError);
        }

        public void RecordCrossSection(string bone, string region, float before, float after, float correction)
        {
            CrossSectionRegions.Add(region);
            RecordContribution(bone, DeformationContributionSource.CrossSectionConditioning);
            MaximumCrossSectionAnisotropyBefore = MathF.Max(MaximumCrossSectionAnisotropyBefore, before);
            MaximumCrossSectionAnisotropyAfter = MathF.Max(MaximumCrossSectionAnisotropyAfter, after);
            MaximumCrossSectionCorrection = MathF.Max(MaximumCrossSectionCorrection, correction);
        }

        public void RecordVolumeIntent(string bone, string region, float before, float after, float correction)
        {
            VolumeRegions.Add(region);
            RecordContribution(bone, DeformationContributionSource.LocalVolumeIntent);
            MaximumVolumeErrorBefore = MathF.Max(MaximumVolumeErrorBefore, before);
            MaximumVolumeErrorAfter = MathF.Max(MaximumVolumeErrorAfter, after);
            MaximumVolumeAxisCorrection = MathF.Max(MaximumVolumeAxisCorrection, correction);
        }

        public void RecordFairnessBefore(string chain, float before)
        {
            MaximumFairnessSecondDifferenceBefore = MathF.Max(MaximumFairnessSecondDifferenceBefore, before);
        }

        public void RecordFairnessCorrection(string bone, float correction)
        {
            FairnessBones.Add(bone);
            RecordContribution(bone, DeformationContributionSource.ShapeFairness);
            MaximumFairnessCorrection = MathF.Max(MaximumFairnessCorrection, correction);
        }

        public void RecordFairnessAfter(float after, float preservationError)
        {
            MaximumFairnessSecondDifferenceAfter = MathF.Max(MaximumFairnessSecondDifferenceAfter, after);
            FairnessMagnitudePreservationError = MathF.Max(FairnessMagnitudePreservationError, preservationError);
        }

        public DeformationQualitySolverDiagnostics Freeze()
        {
            var diagnostics = new DeformationQualitySolverDiagnostics(
                ActiveRegions.OrderBy(static name => name, StringComparer.Ordinal).ToArray(),
                PrimaryCount,
                SupportCount,
                TransitionCount,
                SecondaryCount,
                SecondaryMagnitude,
                ClampedCount,
                DoubleContributionPreventions,
                FallbackCount,
                BilateralNormalizations,
                SecondaryOrigins.GetValueOrDefault(BoneOrigin.NFLB),
                SecondaryOrigins.GetValueOrDefault(BoneOrigin.Skelomae),
                SecondaryOrigins.GetValueOrDefault(BoneOrigin.IVCS2),
                SecondaryOrigins.GetValueOrDefault(BoneOrigin.YAS),
                ProportionalBalanceEnabled,
                ProportionalBalanceStrength,
                ProportionalRegions.OrderBy(static name => name, StringComparer.Ordinal).ToArray(),
                CorrectedRelationships.OrderBy(static name => name, StringComparer.Ordinal).ToArray(),
                MaximumProportionalCorrection,
                MaximumProportionalImbalanceBefore,
                MaximumProportionalImbalanceAfter,
                ProportionalSkippedExplicitOrLocked,
                SurfaceSmoothnessEnabled,
                SurfaceSmoothnessStrength,
                SurfaceRegions.OrderBy(static name => name, StringComparer.Ordinal).ToArray(),
                SurfaceBones.Count,
                MaximumPreSmoothingGradient,
                MaximumPostSmoothingGradient,
                SurfaceSkippedBoundaries,
                SurfaceMagnitudePreservationError,
                CrossSectionConditioningEnabled,
                CrossSectionConditioningStrength,
                CrossSectionRegions.OrderBy(static name => name, StringComparer.Ordinal).ToArray(),
                CrossSectionRegions.Count == 0 ? 0 : CrossSectionRegions.Sum(region => AutomaticBoneRegions.Count(pair => string.Equals(pair.Value, region, StringComparison.Ordinal))),
                MaximumCrossSectionAnisotropyBefore,
                MaximumCrossSectionAnisotropyAfter,
                MaximumCrossSectionCorrection,
                CrossSectionSkippedUntrustedOrConstrained,
                ShapeFairnessEnabled,
                ShapeFairnessStrength,
                FairnessChainsCorrected.OrderBy(static name => name, StringComparer.Ordinal).ToArray(),
                FairnessBones.Count,
                MaximumFairnessSecondDifferenceBefore,
                MaximumFairnessSecondDifferenceAfter,
                MaximumFairnessCorrection,
                FairnessSkippedBoundaries,
                FairnessMagnitudePreservationError,
                LocalVolumeIntentEnabled,
                LocalVolumeIntentStrength,
                VolumeRegions.OrderBy(static name => name, StringComparer.Ordinal).ToArray(),
                MaximumVolumeErrorBefore,
                MaximumVolumeErrorAfter,
                MaximumVolumeAxisCorrection,
                VolumeSkippedUntrustedOrConstrained)
            {
                ContributionSources = new Dictionary<string, DeformationContributionSource>(ContributionSources, StringComparer.Ordinal),
                ContributionScaleDeltas = ContributionScaleDeltas.ToDictionary(
                    static pair => pair.Key,
                    static pair => (IReadOnlyDictionary<DeformationContributionSource, Vector3>)new Dictionary<DeformationContributionSource, Vector3>(pair.Value),
                    StringComparer.Ordinal),
                StaticInputScales = new Dictionary<string, Vector3>(StaticInputScales, StringComparer.Ordinal),
            };
            return diagnostics;
        }
    }
}

internal sealed record DeformationQualitySolverDiagnostics(
    IReadOnlyList<string> ActiveRegions,
    int PrimaryContributionCount,
    int SupportContributionCount,
    int TransitionContributionCount,
    int SecondaryContributionCount,
    float SecondaryContributionMagnitude,
    int ClampedContributionCount,
    int DoubleContributionPreventionCount,
    int FallbackCount,
    int BilateralNormalizationCount,
    int AutomatedNflbBodyControls,
    int AutomatedSkelomaeBodyControls,
    int AutomatedIvcs2Controls,
    int AutomatedYasControls,
    bool ProportionalBalanceEnabled,
    float ProportionalBalanceStrength,
    IReadOnlyList<string> ProportionalAffectedRegions,
    IReadOnlyList<string> CorrectedRelationships,
    float MaximumProportionalCorrection,
    float MaximumProportionalImbalanceBefore,
    float MaximumProportionalImbalanceAfter,
    int ProportionalSkippedExplicitOrLockedCount,
    bool SurfaceSmoothnessEnabled,
    float SurfaceSmoothnessStrength,
    IReadOnlyList<string> SurfaceSmoothnessRegions,
    int SurfaceSmoothnessAffectedBoneCount,
    float MaximumPreSmoothingGradient,
    float MaximumPostSmoothingGradient,
    int SurfaceSmoothnessSkippedBoundaryCount,
    float SurfaceMagnitudePreservationError,
    bool CrossSectionConditioningEnabled,
    float CrossSectionConditioningStrength,
    IReadOnlyList<string> CrossSectionRegions,
    int CrossSectionAffectedBoneCount,
    float MaximumCrossSectionAnisotropyBefore,
    float MaximumCrossSectionAnisotropyAfter,
    float MaximumCrossSectionCorrection,
    int CrossSectionSkippedUntrustedOrConstrainedCount,
    bool ShapeFairnessEnabled,
    float ShapeFairnessStrength,
    IReadOnlyList<string> ShapeFairnessChains,
    int ShapeFairnessAffectedBoneCount,
    float MaximumFairnessSecondDifferenceBefore,
    float MaximumFairnessSecondDifferenceAfter,
    float MaximumFairnessCorrection,
    int ShapeFairnessSkippedBoundaryCount,
    float FairnessMagnitudePreservationError,
    bool LocalVolumeIntentEnabled,
    float LocalVolumeIntentStrength,
    IReadOnlyList<string> LocalVolumeIntentRegions,
    float MaximumVolumeErrorBefore,
    float MaximumVolumeErrorAfter,
    float MaximumVolumeAxisCorrection,
    int LocalVolumeIntentSkippedUntrustedOrConstrainedCount)
{
    // Internal provenance for future "why is this bone shaped like this?" diagnostics.
    // It deliberately is not emitted as a large default DAB payload.
    internal IReadOnlyDictionary<string, DeformationContributionSource> ContributionSources { get; init; }
        = new Dictionary<string, DeformationContributionSource>(StringComparer.Ordinal);
    internal IReadOnlyDictionary<string, IReadOnlyDictionary<DeformationContributionSource, Vector3>> ContributionScaleDeltas { get; init; }
        = new Dictionary<string, IReadOnlyDictionary<DeformationContributionSource, Vector3>>(StringComparer.Ordinal);
    internal IReadOnlyDictionary<string, Vector3> StaticInputScales { get; init; }
        = new Dictionary<string, Vector3>(StringComparer.Ordinal);

    public static DeformationQualitySolverDiagnostics Inactive { get; } = CreateInactive(null);

    public static DeformationQualitySolverDiagnostics CreateInactive(AdvancedBodyScalingSettings? settings)
        => new(
            Array.Empty<string>(), 0, 0, 0, 0, 0f, 0, 0, 0, 0, 0, 0, 0, 0,
            settings?.ProportionalBalanceEnabled ?? false,
            settings?.ProportionalBalanceStrength ?? 0f,
            Array.Empty<string>(), Array.Empty<string>(), 0f, 0f, 0f, 0,
            settings?.SurfaceSmoothnessEnabled ?? false,
            settings?.SurfaceSmoothnessStrength ?? 0f,
            Array.Empty<string>(), 0, 0f, 0f, 0, 0f,
            settings?.CrossSectionConditioningEnabled ?? false,
            settings?.CrossSectionConditioningStrength ?? 0f,
            Array.Empty<string>(), 0, 0f, 0f, 0f, 0,
            settings?.ShapeFairnessEnabled ?? false,
            settings?.ShapeFairnessStrength ?? 0f,
            Array.Empty<string>(), 0, 0f, 0f, 0f, 0, 0f,
            settings?.LocalVolumeIntentEnabled ?? false,
            settings?.LocalVolumeIntentStrength ?? 0f,
            Array.Empty<string>(), 0f, 0f, 0f, 0);
}
