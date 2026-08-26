using System.Numerics;
using CustomizePlus.Core.Data;
using Xunit;

namespace CustomizePlus.Tests;

public class AdvancedBodyScalingDeformationSolverTests
{
    [Fact]
    public void BilateralConsistency_NormalizesEquivalentCuratedPrimaryIntent()
    {
        var transforms = CreatePairedChestField(1.50f, 1.50f);
        var explicitTransforms = new HashSet<string>(StringComparer.Ordinal) { "j_mune_l", "j_mune_r" };

        var diagnostics = AdvancedBodyScalingDeformationSolver.Apply(
            transforms, explicitTransforms, new HashSet<string>(transforms.Keys, StringComparer.Ordinal), CreateManifest(), null,
            new AdvancedBodyScalingSettings { BilateralConsistencyEnabled = true });

        Assert.True(diagnostics.BilateralNormalizationCount > 0);
        Assert.Equal(transforms["j_sako_l"].Scaling, transforms["j_sako_r"].Scaling);
        Assert.Equal(new Vector3(1.50f), transforms["j_mune_l"].Scaling);
        Assert.Equal(new Vector3(1.50f), transforms["j_mune_r"].Scaling);
    }

    [Fact]
    public void BilateralConsistency_PreservesDeliberateCuratedPrimaryAsymmetry()
    {
        var transforms = CreatePairedChestField(1.50f, 1.20f);
        var explicitTransforms = new HashSet<string>(StringComparer.Ordinal) { "j_mune_l", "j_mune_r" };

        var diagnostics = AdvancedBodyScalingDeformationSolver.Apply(
            transforms, explicitTransforms, new HashSet<string>(transforms.Keys, StringComparer.Ordinal), CreateManifest(), null,
            new AdvancedBodyScalingSettings { BilateralConsistencyEnabled = true });

        Assert.Equal(0, diagnostics.BilateralNormalizationCount);
        Assert.NotEqual(transforms["j_sako_l"].Scaling, transforms["j_sako_r"].Scaling);
        Assert.Equal(new Vector3(1.50f), transforms["j_mune_l"].Scaling);
        Assert.Equal(new Vector3(1.20f), transforms["j_mune_r"].Scaling);
    }

    [Fact]
    public void BilateralConsistency_Disabled_DoesNotForceMirroredAutomaticOutput()
    {
        var transforms = CreatePairedChestField(1.50f, 1.50f);
        var explicitTransforms = new HashSet<string>(StringComparer.Ordinal) { "j_mune_l", "j_mune_r" };

        var diagnostics = AdvancedBodyScalingDeformationSolver.Apply(
            transforms, explicitTransforms, new HashSet<string>(transforms.Keys, StringComparer.Ordinal), CreateManifest(), null,
            new AdvancedBodyScalingSettings { BilateralConsistencyEnabled = false });

        Assert.Equal(0, diagnostics.BilateralNormalizationCount);
    }

    [Fact]
    public void BilateralConsistency_ExplicitMirroredReceiverRemainsAnAnchor()
    {
        var transforms = CreatePairedChestField(1.50f, 1.20f);
        transforms["j_sako_l"] = Transform(new Vector3(1.33f));
        var explicitTransforms = new HashSet<string>(StringComparer.Ordinal) { "j_mune_l", "j_mune_r", "j_sako_l" };

        AdvancedBodyScalingDeformationSolver.Apply(
            transforms, explicitTransforms, new HashSet<string>(transforms.Keys, StringComparer.Ordinal), CreateManifest(), null,
            new AdvancedBodyScalingSettings { BilateralConsistencyEnabled = true });

        Assert.Equal(new Vector3(1.33f), transforms["j_sako_l"].Scaling);
        Assert.NotEqual(transforms["j_sako_l"].Scaling, transforms["j_sako_r"].Scaling);
    }

    [Fact]
    public void ProportionalBalance_CorrectsAutomaticReceiversAroundExplicitPrimaryAnchors()
    {
        var explicitTransforms = new HashSet<string>(StringComparer.Ordinal)
        {
            "j_mune_l", "j_mune_r", "j_sako_l", "j_sako_r", "n_hkata_l", "n_hkata_r",
        };
        var baseline = CreateAnchoredChestAndShoulderField();
        var balanced = Clone(baseline);
        var liveBones = new HashSet<string>(baseline.Keys, StringComparer.Ordinal);

        var off = AdvancedBodyScalingDeformationSolver.Apply(
            baseline, explicitTransforms, liveBones, CreateManifest(), null,
            new AdvancedBodyScalingSettings { ProportionalBalanceEnabled = false });
        var on = AdvancedBodyScalingDeformationSolver.Apply(
            balanced, explicitTransforms, liveBones, CreateManifest(), null,
            new AdvancedBodyScalingSettings { ProportionalBalanceEnabled = true, ProportionalBalanceStrength = 1f });

        Assert.Empty(off.CorrectedRelationships);
        Assert.Contains("chest -> shoulders", on.CorrectedRelationships);
        Assert.True(on.MaximumProportionalCorrection > 0f);
        Assert.True(on.MaximumProportionalImbalanceAfter < on.MaximumProportionalImbalanceBefore);

        // Explicit primary rows are fixed anchors; only automatic support/transition rows may move.
        foreach (var bone in explicitTransforms)
            Assert.Equal(baseline[bone].Scaling, balanced[bone].Scaling);

        Assert.NotEqual(baseline["j_sebo_c"].Scaling, balanced["j_sebo_c"].Scaling);
        Assert.NotEqual(baseline["j_ude_a_l"].Scaling, balanced["j_ude_a_l"].Scaling);

        // Inspector provenance records the actual bounded stage delta, not merely a textual source flag.
        Assert.True(on.ContributionScaleDeltas.TryGetValue("j_sebo_c", out var stages));
        Assert.True(stages.TryGetValue(DeformationContributionSource.ProportionalBalance, out var recordedDelta));
        Assert.InRange(Vector3.Distance(recordedDelta, balanced["j_sebo_c"].Scaling - baseline["j_sebo_c"].Scaling), 0f, 0.00001f);

        Assert.True(on.StaticInputScales.TryGetValue("j_sebo_c", out var staticInput));
        var allStaticDeltas = on.ContributionScaleDeltas["j_sebo_c"].Values.Aggregate(Vector3.Zero, static (sum, delta) => sum + delta);
        Assert.InRange(Vector3.Distance(staticInput + allStaticDeltas, balanced["j_sebo_c"].Scaling), 0f, 0.00001f);
    }

    [Fact]
    public void ProportionalBalance_SkipsRelationshipWhenNoAutomaticReceiverRemains()
    {
        var transforms = CreateAnchoredChestAndShoulderField();
        var explicitTransforms = new HashSet<string>(transforms.Keys, StringComparer.Ordinal)
        {
            "j_sebo_c", "j_ude_a_l", "j_ude_a_r",
        };
        transforms["j_sebo_c"] = Transform(Vector3.One);
        transforms["j_ude_a_l"] = Transform(Vector3.One);
        transforms["j_ude_a_r"] = Transform(Vector3.One);

        var before = Clone(transforms);
        var diagnostics = AdvancedBodyScalingDeformationSolver.Apply(
            transforms, explicitTransforms, new HashSet<string>(transforms.Keys, StringComparer.Ordinal), CreateManifest(), null,
            new AdvancedBodyScalingSettings { ProportionalBalanceEnabled = true, ProportionalBalanceStrength = 1f });

        Assert.DoesNotContain("chest -> shoulders", diagnostics.CorrectedRelationships);
        Assert.True(diagnostics.ProportionalSkippedExplicitOrLockedCount > 0);
        Assert.Equal(0f, diagnostics.MaximumProportionalCorrection);
        foreach (var bone in explicitTransforms)
            Assert.Equal(before[bone].Scaling, transforms[bone].Scaling);
    }

    [Fact]
    public void AutomaticSupport_DoesNotUseIvcs1SecondaryWithoutIvcs1Capability()
    {
        var transforms = new Dictionary<string, BoneTransform>(StringComparer.Ordinal)
        {
            ["j_mune_l"] = Transform(new Vector3(1.35f)),
            ["j_mune_r"] = Transform(new Vector3(1.35f)),
            ["j_sebo_b"] = Transform(new Vector3(1.35f)),
            ["iv_c_mune_l"] = Transform(Vector3.One),
        };
        var manifest = CreateManifest();

        var diagnostics = AdvancedBodyScalingDeformationSolver.Apply(
            transforms,
            new HashSet<string>(new[] { "j_mune_l", "j_mune_r", "j_sebo_b" }, StringComparer.Ordinal),
            new HashSet<string>(transforms.Keys, StringComparer.Ordinal),
            manifest,
            null,
            new AdvancedBodyScalingSettings());

        Assert.Equal(0, diagnostics.SecondaryContributionCount);
        Assert.Equal(Vector3.One, transforms["iv_c_mune_l"].Scaling);
    }

    private static Dictionary<string, BoneTransform> CreateAnchoredChestAndShoulderField()
        => new(StringComparer.Ordinal)
        {
            ["j_mune_l"] = Transform(new Vector3(1.80f, 1.35f, 1.65f)),
            ["j_mune_r"] = Transform(new Vector3(1.80f, 1.35f, 1.65f)),
            ["j_sako_l"] = Transform(new Vector3(1.20f, 1.08f, 1.15f)),
            ["j_sako_r"] = Transform(new Vector3(1.20f, 1.08f, 1.15f)),
            ["n_hkata_l"] = Transform(new Vector3(1.20f, 1.08f, 1.15f)),
            ["n_hkata_r"] = Transform(new Vector3(1.20f, 1.08f, 1.15f)),
            ["j_sebo_c"] = Transform(new Vector3(1.10f, 1.04f, 1.08f)),
            ["j_ude_a_l"] = Transform(new Vector3(1.08f, 1.03f, 1.06f)),
            ["j_ude_a_r"] = Transform(new Vector3(1.08f, 1.03f, 1.06f)),
        };

    private static Dictionary<string, BoneTransform> CreatePairedChestField(float leftPrimary, float rightPrimary)
        => new(StringComparer.Ordinal)
        {
            ["j_mune_l"] = Transform(new Vector3(leftPrimary)),
            ["j_mune_r"] = Transform(new Vector3(rightPrimary)),
            ["j_sako_l"] = Transform(Vector3.One),
            ["j_sako_r"] = Transform(Vector3.One),
            ["n_hkata_l"] = Transform(Vector3.One),
            ["n_hkata_r"] = Transform(Vector3.One),
        };

    private static BoneTransform Transform(Vector3 scaling)
        => new() { Scaling = scaling };

    private static Dictionary<string, BoneTransform> Clone(IReadOnlyDictionary<string, BoneTransform> source)
        => source.ToDictionary(pair => pair.Key, pair => new BoneTransform(pair.Value), StringComparer.Ordinal);

    private static SkeletonCapabilityManifest CreateManifest()
        => new(
            SkeletonCapability.VanillaCore,
            0,
            1,
            "test",
            2,
            true,
            new SkeletonTopologySummary(1, 1, 1, 0, 0, new[] { 1 }, Array.Empty<int>(), true),
            new Dictionary<SkeletonCapability, SkeletonCapabilityEvidence>
            {
                [SkeletonCapability.VanillaCore] = new(SkeletonCapabilityState.Present, Array.Empty<string>(), Array.Empty<string>()),
            },
            BoneAnimationCompatibility.None,
            new Dictionary<string, int>(),
            Array.Empty<string>(),
            Array.Empty<string>());
}
