using System.Numerics;
using CustomizePlus.Core.Data;
using Xunit;
using Xunit.Abstractions;

namespace CustomizePlus.Tests;

public class AdvancedBodyScalingShapeConditioningTests
{
    private readonly ITestOutputHelper _output;

    public AdvancedBodyScalingShapeConditioningTests(ITestOutputHelper output)
        => _output = output;

    [Fact]
    public void LogScale_RoundTripsFinitePositiveValues_AndRejectsUnsafeInput()
    {
        var source = new Vector3(1.60f, 0.78f, 1.22f);

        Assert.True(AdvancedBodyScalingLogScale.TryCreate(source, out var log));
        Assert.True(log.TryReconstruct(out var reconstructed));
        Assert.InRange(Vector3.Distance(source, reconstructed), 0f, 0.0001f);
        Assert.False(AdvancedBodyScalingLogScale.TryCreate(new Vector3(1f, 0f, 1f), out _));
        Assert.False(AdvancedBodyScalingLogScale.TryCreate(new Vector3(float.NaN, 1f, 1f), out _));
    }

    [Fact]
    public void CrossSectionConditioning_RefinesAutomaticAnisotropyWithoutChangingExplicitAnchors()
    {
        var baseline = CreateCrossSectionFixture();
        var conditioned = Clone(baseline);
        var explicitTransforms = new HashSet<string>(new[] { "j_mune_l", "j_mune_r", "j_sebo_b" }, StringComparer.Ordinal);
        var liveBones = new HashSet<string>(baseline.Keys, StringComparer.Ordinal);

        var off = AdvancedBodyScalingDeformationSolver.Apply(
            baseline, explicitTransforms, liveBones, CreateManifest(), null,
            new AdvancedBodyScalingSettings());
        var on = AdvancedBodyScalingDeformationSolver.Apply(
            conditioned, explicitTransforms, liveBones, CreateManifest(), null,
            new AdvancedBodyScalingSettings { CrossSectionConditioningEnabled = true, CrossSectionConditioningStrength = 1f });

        Assert.True(on.CrossSectionAffectedBoneCount > 0);
        Assert.True(on.MaximumCrossSectionCorrection > 0f);
        Assert.True(on.MaximumCrossSectionAnisotropyAfter < on.MaximumCrossSectionAnisotropyBefore);
        Assert.Equal(baseline["j_mune_l"].Scaling, conditioned["j_mune_l"].Scaling);
        Assert.Equal(baseline["j_mune_r"].Scaling, conditioned["j_mune_r"].Scaling);
        Assert.Equal(baseline["j_sebo_b"].Scaling, conditioned["j_sebo_b"].Scaling);
        Assert.NotEqual(baseline["j_sebo_c"].Scaling, conditioned["j_sebo_c"].Scaling);
        Assert.True(off.CrossSectionAffectedBoneCount == 0);
        _output.WriteLine($"Cross-section: affected={on.CrossSectionAffectedBoneCount}; anisotropy={on.MaximumCrossSectionAnisotropyBefore:0.000}->{on.MaximumCrossSectionAnisotropyAfter:0.000}; correction={on.MaximumCrossSectionCorrection:0.000}.");
    }

    [Fact]
    public void ShapeFairness_ReducesAutomaticSecondDifferenceWithoutMovingExplicitEndpoints()
    {
        var baseline = CreateArmChainFixture();
        var conditioned = Clone(baseline);
        var explicitTransforms = new HashSet<string>(new[] { "j_sebo_b", "j_ude_b_l", "j_ude_b_r" }, StringComparer.Ordinal);
        var liveBones = new HashSet<string>(baseline.Keys, StringComparer.Ordinal);

        var diagnostics = AdvancedBodyScalingDeformationSolver.Apply(
            conditioned, explicitTransforms, liveBones, CreateManifest(), null,
            new AdvancedBodyScalingSettings { ShapeFairnessEnabled = true, ShapeFairnessStrength = 1f });

        Assert.True(diagnostics.ShapeFairnessAffectedBoneCount > 0);
        Assert.True(diagnostics.MaximumFairnessCorrection > 0f);
        Assert.True(diagnostics.MaximumFairnessSecondDifferenceAfter < diagnostics.MaximumFairnessSecondDifferenceBefore);
        Assert.Equal(baseline["j_sebo_b"].Scaling, conditioned["j_sebo_b"].Scaling);
        Assert.Equal(baseline["j_ude_b_l"].Scaling, conditioned["j_ude_b_l"].Scaling);
        Assert.Equal(baseline["j_ude_b_r"].Scaling, conditioned["j_ude_b_r"].Scaling);
        _output.WriteLine($"Fairness: chains={string.Join(", ", diagnostics.ShapeFairnessChains)}; second-difference={diagnostics.MaximumFairnessSecondDifferenceBefore:0.000}->{diagnostics.MaximumFairnessSecondDifferenceAfter:0.000}; correction={diagnostics.MaximumFairnessCorrection:0.000}; magnitude-error={diagnostics.FairnessMagnitudePreservationError:0.000}.");
    }

    [Fact]
    public void LocalVolumeIntent_ImprovesAutomaticVolumeErrorWithoutImposingConstantVolume()
    {
        var baseline = CreateChestFixture();
        var conditioned = Clone(baseline);
        var explicitTransforms = new HashSet<string>(new[] { "j_mune_l", "j_mune_r", "j_sebo_b" }, StringComparer.Ordinal);
        var liveBones = new HashSet<string>(baseline.Keys, StringComparer.Ordinal);

        var diagnostics = AdvancedBodyScalingDeformationSolver.Apply(
            conditioned, explicitTransforms, liveBones, CreateManifest(), null,
            new AdvancedBodyScalingSettings { LocalVolumeIntentEnabled = true, LocalVolumeIntentStrength = 1f });

        Assert.NotEmpty(diagnostics.LocalVolumeIntentRegions);
        Assert.True(diagnostics.MaximumVolumeAxisCorrection > 0f);
        Assert.True(diagnostics.MaximumVolumeErrorAfter < diagnostics.MaximumVolumeErrorBefore);
        Assert.Equal(baseline["j_mune_l"].Scaling, conditioned["j_mune_l"].Scaling);
        Assert.True(conditioned["j_sebo_c"].Scaling.X > 1f);
        _output.WriteLine($"Volume intent: regions={string.Join(", ", diagnostics.LocalVolumeIntentRegions)}; error={diagnostics.MaximumVolumeErrorBefore:0.000}->{diagnostics.MaximumVolumeErrorAfter:0.000}; correction={diagnostics.MaximumVolumeAxisCorrection:0.000}.");
    }

    [Fact]
    public void ShapeConditioning_SkipsWhenOnlyExplicitOrConstrainedReceiversRemain()
    {
        var transforms = CreateChestFixture();
        var explicitTransforms = new HashSet<string>(transforms.Keys, StringComparer.Ordinal);
        var before = Clone(transforms);

        var diagnostics = AdvancedBodyScalingDeformationSolver.Apply(
            transforms, explicitTransforms, new HashSet<string>(transforms.Keys, StringComparer.Ordinal), CreateManifest(), null,
            new AdvancedBodyScalingSettings
            {
                CrossSectionConditioningEnabled = true,
                ShapeFairnessEnabled = true,
                LocalVolumeIntentEnabled = true,
            });

        Assert.Equal(0, diagnostics.CrossSectionAffectedBoneCount);
        Assert.Equal(0, diagnostics.ShapeFairnessAffectedBoneCount);
        Assert.Empty(diagnostics.LocalVolumeIntentRegions);
        foreach (var (bone, transform) in before)
            Assert.Equal(transform.Scaling, transforms[bone].Scaling);
    }

    [Fact]
    public void ShapeConditioning_IsDeterministicForIdenticalInput()
    {
        var first = CreateArmChainFixture();
        var second = Clone(first);
        var explicitTransforms = new HashSet<string>(new[] { "j_sebo_b", "j_ude_b_l", "j_ude_b_r" }, StringComparer.Ordinal);
        var settings = new AdvancedBodyScalingSettings
        {
            CrossSectionConditioningEnabled = true,
            CrossSectionConditioningStrength = 0.7f,
            ShapeFairnessEnabled = true,
            ShapeFairnessStrength = 0.7f,
            LocalVolumeIntentEnabled = true,
            LocalVolumeIntentStrength = 0.7f,
        };

        AdvancedBodyScalingDeformationSolver.Apply(first, explicitTransforms, new HashSet<string>(first.Keys, StringComparer.Ordinal), CreateManifest(), null, settings);
        AdvancedBodyScalingDeformationSolver.Apply(second, explicitTransforms, new HashSet<string>(second.Keys, StringComparer.Ordinal), CreateManifest(), null, settings);

        Assert.Equal(first.Keys.OrderBy(static value => value), second.Keys.OrderBy(static value => value));
        foreach (var bone in first.Keys)
            Assert.InRange(Vector3.Distance(first[bone].Scaling, second[bone].Scaling), 0f, 0.00001f);
    }

    [Fact]
    public void PoseResponse_IsBoundedMonotonicAndNearZeroAtNeutral()
    {
        var neutral = AdvancedBodyScalingPoseAwareJointCorrectiveSystem.EvaluatePoseResponse(0f, 24f, 112f);
        var early = AdvancedBodyScalingPoseAwareJointCorrectiveSystem.EvaluatePoseResponse(24f, 24f, 112f);
        var middle = AdvancedBodyScalingPoseAwareJointCorrectiveSystem.EvaluatePoseResponse(68f, 24f, 112f);
        var deep = AdvancedBodyScalingPoseAwareJointCorrectiveSystem.EvaluatePoseResponse(112f, 24f, 112f);
        var beyond = AdvancedBodyScalingPoseAwareJointCorrectiveSystem.EvaluatePoseResponse(180f, 24f, 112f);

        Assert.InRange(neutral, 0f, 0.00001f);
        Assert.InRange(early, 0f, 0.00001f);
        Assert.True(middle > early && middle < deep);
        Assert.InRange(deep, 0.999f, 1f);
        Assert.InRange(beyond, 0.999f, 1f);
    }

    [Fact]
    public void PoseCorrectiveMultiplier_IsNeutralAtRest_AndBoundedUnderDeepFlexion()
    {
        var neutral = AdvancedBodyScalingPoseAwareJointCorrectiveSystem.CalculateScaleMultiplier(0f, 1f, 1f, 0.026f);
        var flexed = AdvancedBodyScalingPoseAwareJointCorrectiveSystem.CalculateScaleMultiplier(1f, 1f, 1f, 0.026f);
        var attenuated = AdvancedBodyScalingPoseAwareJointCorrectiveSystem.CalculateScaleMultiplier(1f, 0.5f, 0.65f, 0.026f);

        Assert.Equal(1f, neutral);
        Assert.InRange(flexed, 1.0259f, 1.0261f);
        Assert.True(attenuated > 1f && attenuated < flexed);
        Assert.Equal(1f, AdvancedBodyScalingPoseAwareJointCorrectiveSystem.CalculateScaleMultiplier(float.NaN, 1f, 1f, 0.026f));
    }

    [Fact]
    public void CombinedShapeConditioning_RemainsFiniteDeterministicAndKeepsAnchorsFixed()
    {
        var first = CreateCrossSectionFixture();
        var second = Clone(first);
        var explicitTransforms = new HashSet<string>(new[] { "j_mune_l", "j_mune_r", "j_sebo_b" }, StringComparer.Ordinal);
        var settings = new AdvancedBodyScalingSettings
        {
            ProportionalBalanceEnabled = true,
            ProportionalBalanceStrength = 0.65f,
            SurfaceSmoothnessEnabled = true,
            SurfaceSmoothnessStrength = 0.65f,
            CrossSectionConditioningEnabled = true,
            CrossSectionConditioningStrength = 0.65f,
            ShapeFairnessEnabled = true,
            ShapeFairnessStrength = 0.65f,
            LocalVolumeIntentEnabled = true,
            LocalVolumeIntentStrength = 0.65f,
        };

        var diagnostics = AdvancedBodyScalingDeformationSolver.Apply(first, explicitTransforms, new HashSet<string>(first.Keys, StringComparer.Ordinal), CreateManifest(), null, settings);
        AdvancedBodyScalingDeformationSolver.Apply(second, explicitTransforms, new HashSet<string>(second.Keys, StringComparer.Ordinal), CreateManifest(), null, settings);

        foreach (var anchor in explicitTransforms)
        {
            Assert.Equal(CreateCrossSectionFixture()[anchor].Scaling, first[anchor].Scaling);
            Assert.Equal(first[anchor].Scaling, second[anchor].Scaling);
        }

        foreach (var bone in first.Keys)
        {
            Assert.True(float.IsFinite(first[bone].Scaling.X));
            Assert.True(float.IsFinite(first[bone].Scaling.Y));
            Assert.True(float.IsFinite(first[bone].Scaling.Z));
            Assert.InRange(Vector3.Distance(first[bone].Scaling, second[bone].Scaling), 0f, 0.00001f);
        }

        _output.WriteLine($"Combined: fallbacks={diagnostics.FallbackCount}; cross={diagnostics.MaximumCrossSectionCorrection:0.000}; fairness={diagnostics.MaximumFairnessCorrection:0.000}; volume={diagnostics.MaximumVolumeAxisCorrection:0.000}.");
    }

    private static Dictionary<string, BoneTransform> CreateChestFixture()
        => new(StringComparer.Ordinal)
        {
            ["j_mune_l"] = Transform(new Vector3(1.42f, 0.62f, 1.34f)),
            ["j_mune_r"] = Transform(new Vector3(1.42f, 0.62f, 1.34f)),
            ["j_sebo_b"] = Transform(new Vector3(1.30f, 0.72f, 1.24f)),
            ["j_sebo_c"] = Transform(Vector3.One),
            ["j_sako_l"] = Transform(Vector3.One), ["j_sako_r"] = Transform(Vector3.One),
            ["n_hkata_l"] = Transform(Vector3.One), ["n_hkata_r"] = Transform(Vector3.One),
            ["j_ude_a_l"] = Transform(Vector3.One), ["j_ude_a_r"] = Transform(Vector3.One),
            ["j_ude_b_l"] = Transform(Vector3.One), ["j_ude_b_r"] = Transform(Vector3.One),
        };

    private static Dictionary<string, BoneTransform> CreateCrossSectionFixture()
    {
        var fixture = CreateChestFixture();
        // Simulates a pre-existing automatic transition contribution with excessive axis spread.
        fixture["j_sebo_c"] = Transform(new Vector3(1.45f, 0.70f, 1.42f));
        return fixture;
    }

    private static Dictionary<string, BoneTransform> CreateArmChainFixture()
    {
        var fixture = CreateChestFixture();
        fixture["j_sebo_b"] = Transform(new Vector3(1.05f, 1.05f, 1.05f));
        fixture["j_ude_b_l"] = Transform(new Vector3(1.42f, 0.74f, 1.36f));
        fixture["j_ude_b_r"] = Transform(new Vector3(1.42f, 0.74f, 1.36f));
        return fixture;
    }

    private static BoneTransform Transform(Vector3 scaling) => new() { Scaling = scaling };

    private static Dictionary<string, BoneTransform> Clone(IReadOnlyDictionary<string, BoneTransform> source)
        => source.ToDictionary(static pair => pair.Key, static pair => new BoneTransform(pair.Value), StringComparer.Ordinal);

    private static SkeletonCapabilityManifest CreateManifest()
        => new(
            SkeletonCapability.VanillaCore,
            0,
            1,
            "shape-conditioning-test",
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
