using System.Numerics;
using CustomizePlus.Core.Data;
using Xunit;

namespace CustomizePlus.Tests;

/// <summary>
/// Covers the managed, deterministic contract surface for optional runtime layers. Native pose
/// sampling and writes remain guarded integration behavior and are observed through DAB instead.
/// </summary>
public sealed class AdvancedBodyScalingRuntimeLayerContractTests
{
    [Fact]
    public void OptionalRuntimeLayers_Disabled_ReturnNoStaticSupport()
    {
        var settings = new AdvancedBodyScalingSettings();
        var transforms = CreateSupportedTransformField();

        Assert.Empty(AdvancedBodyScalingPoseCorrectiveSystem.EstimateStaticSupport(transforms, settings));
        Assert.Empty(AdvancedBodyScalingFullIkRetargetingSystem.EstimateStaticSupport(transforms, settings));
        Assert.Empty(AdvancedBodyScalingMotionWarpingSystem.EstimateStaticSupport(transforms, settings));
        Assert.Empty(AdvancedBodyScalingFullBodyIkSystem.EstimateStaticSupport(transforms, settings));
    }

    [Fact]
    public void OptionalRuntimeLayers_MissingChainData_SkipSafely()
    {
        var settings = CreateEnabledSettings();
        var incomplete = new Dictionary<string, BoneTransform>(StringComparer.Ordinal);

        Assert.Empty(AdvancedBodyScalingPoseCorrectiveSystem.EstimateStaticSupport(incomplete, settings));
        Assert.All(AdvancedBodyScalingFullIkRetargetingSystem.EstimateStaticSupport(incomplete, settings), estimate => Assert.False(estimate.IsValid));
        Assert.All(AdvancedBodyScalingMotionWarpingSystem.EstimateStaticSupport(incomplete, settings), estimate => Assert.False(estimate.IsValid));
        Assert.All(AdvancedBodyScalingFullBodyIkSystem.EstimateStaticSupport(incomplete, settings), estimate => Assert.False(estimate.IsValid));
    }

    [Fact]
    public void OptionalRuntimeLayers_SupportedDisproportion_IsFiniteBoundedAndDeterministic()
    {
        var settings = CreateEnabledSettings();
        var transforms = CreateSupportedTransformField();
        transforms["j_sako_l"] = Transform(1.45f);
        transforms["j_ude_a_l"] = Transform(1.55f);
        transforms["j_ude_b_l"] = Transform(1.50f);
        transforms["j_asi_a_l"] = Transform(1.42f);
        transforms["j_asi_b_l"] = Transform(1.38f);

        var poseWeights = AdvancedBodyScalingPoseCorrectiveSystem
            .GetOrderedRegions()
            .ToDictionary(region => region, _ => 0.72f);
        var rbfFirst = AdvancedBodyScalingPoseCorrectiveSystem.EstimateStaticSupport(transforms, settings, poseWeights);
        var rbfSecond = AdvancedBodyScalingPoseCorrectiveSystem.EstimateStaticSupport(transforms, settings, poseWeights);
        var retargetFirst = AdvancedBodyScalingFullIkRetargetingSystem.EstimateStaticSupport(transforms, settings);
        var retargetSecond = AdvancedBodyScalingFullIkRetargetingSystem.EstimateStaticSupport(transforms, settings);
        var motionFirst = AdvancedBodyScalingMotionWarpingSystem.EstimateStaticSupport(transforms, settings);
        var motionSecond = AdvancedBodyScalingMotionWarpingSystem.EstimateStaticSupport(transforms, settings);
        var fullBodyFirst = AdvancedBodyScalingFullBodyIkSystem.EstimateStaticSupport(transforms, settings);
        var fullBodySecond = AdvancedBodyScalingFullBodyIkSystem.EstimateStaticSupport(transforms, settings);

        Assert.NotEmpty(rbfFirst);
        Assert.Equal(rbfFirst.Count, rbfSecond.Count);
        for (var i = 0; i < rbfFirst.Count; ++i)
        {
            AssertFinite(rbfFirst[i].Activation, rbfFirst[i].Strength, rbfFirst[i].EstimatedRiskReduction);
            Assert.InRange(rbfFirst[i].EstimatedRiskReduction, 0f, 1f);
            Assert.Equal(rbfFirst[i].Region, rbfSecond[i].Region);
            Assert.InRange(MathF.Abs(rbfFirst[i].Strength - rbfSecond[i].Strength), 0f, 0.00001f);
        }

        AssertLayerEstimates(retargetFirst, retargetSecond, estimate => estimate.Strength, estimate => estimate.BlendAmount, estimate => estimate.EstimatedRiskReduction);
        AssertLayerEstimates(motionFirst, motionSecond, estimate => estimate.Strength, estimate => estimate.BlendAmount, estimate => estimate.EstimatedRiskReduction);
        AssertLayerEstimates(fullBodyFirst, fullBodySecond, estimate => estimate.Strength, estimate => estimate.Activation, estimate => estimate.EstimatedRiskReduction);
    }

    [Fact]
    public void PoseCorrectives_ExplicitlyUseTransformFallback_NotAnUnimplementedMorphPath()
    {
        var path = AdvancedBodyScalingPoseCorrectiveSystem.DetectSupportedPath();

        Assert.Equal(AdvancedBodyScalingCorrectivePath.TransformFallback, path);
        Assert.Contains("RBF-driven transform corrective path", AdvancedBodyScalingPoseCorrectiveSystem.GetPathDescription(path), StringComparison.Ordinal);
    }

    private static AdvancedBodyScalingSettings CreateEnabledSettings()
        => new()
        {
            Enabled = true,
            Mode = AdvancedBodyScalingMode.Strong,
            PoseCorrectives = { Enabled = true, Strength = 0.64f },
            FullIkRetargeting = { Enabled = true, GlobalStrength = 0.48f },
            MotionWarping = { Enabled = true, GlobalStrength = 0.46f },
            FullBodyIk = { Enabled = true, GlobalStrength = 0.48f, IterationCount = 4, ConvergenceTolerance = 0.02f },
        };

    private static Dictionary<string, BoneTransform> CreateSupportedTransformField()
    {
        var names = new[]
        {
            "j_kosi", "j_sebo_a", "j_sebo_b", "j_sebo_c", "j_kubi", "j_kao", "n_hara",
            "j_mune_l", "j_mune_r", "j_sako_l", "j_sako_r", "n_hkata_l", "n_hkata_r",
            "j_ude_a_l", "j_ude_a_r", "j_ude_b_l", "j_ude_b_r", "n_hte_l", "n_hte_r", "j_te_l", "j_te_r",
            "j_asi_a_l", "j_asi_a_r", "j_asi_b_l", "j_asi_b_r", "j_asi_c_l", "j_asi_c_r", "j_asi_d_l", "j_asi_d_r",
        };

        return names.ToDictionary(name => name, _ => Transform(1f), StringComparer.Ordinal);
    }

    private static BoneTransform Transform(float uniformScale)
        => new() { Scaling = new Vector3(uniformScale) };

    private static void AssertLayerEstimates<T>(
        IReadOnlyList<T> first,
        IReadOnlyList<T> second,
        Func<T, float> strength,
        Func<T, float> pressure,
        Func<T, float> reduction)
    {
        Assert.NotEmpty(first);
        Assert.Equal(first.Count, second.Count);
        Assert.True(first.Any(entry => strength(entry) > 0f));

        for (var i = 0; i < first.Count; ++i)
        {
            AssertFinite(strength(first[i]), pressure(first[i]), reduction(first[i]));
            Assert.InRange(reduction(first[i]), 0f, 1f);
            Assert.InRange(MathF.Abs(strength(first[i]) - strength(second[i])), 0f, 0.00001f);
            Assert.InRange(MathF.Abs(pressure(first[i]) - pressure(second[i])), 0f, 0.00001f);
            Assert.InRange(MathF.Abs(reduction(first[i]) - reduction(second[i])), 0f, 0.00001f);
        }
    }

    private static void AssertFinite(params float[] values)
    {
        foreach (var value in values)
            Assert.True(float.IsFinite(value));
    }
}
