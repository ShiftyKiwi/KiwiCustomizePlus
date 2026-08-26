using System.Numerics;
using CustomizePlus.Core.Data;
using Xunit;

namespace CustomizePlus.Tests;

public sealed class PoseCorrectiveValidationFixtureTests
{
    [Fact]
    public void DeterministicFixture_ActivatesReturnsToNeutralAndDoesNotAccumulateAcrossCycles()
    {
        var first = AdvancedBodyScalingPoseCorrectiveSystem.RunDeterministicValidationFixture(25);
        var second = AdvancedBodyScalingPoseCorrectiveSystem.RunDeterministicValidationFixture(25);

        Assert.Equal(25, first.Cycles);
        Assert.Equal(200, first.ActiveFrames);
        Assert.Equal(first.ActiveFrames, first.ActiveCorrectiveFrames);
        Assert.True(first.MaximumActiveScaleDelta > 0.0005f);
        Assert.InRange(first.MaximumPostCycleNeutralScaleDelta, 0f, 0.0005f);
        Assert.InRange(first.FinalNeutralScaleDelta, 0f, 0.0005f);
        Assert.InRange(MathF.Abs(first.MaximumActiveScaleDelta - second.MaximumActiveScaleDelta), 0f, 0.000001f);
        Assert.InRange(MathF.Abs(first.MaximumPostCycleNeutralScaleDelta - second.MaximumPostCycleNeutralScaleDelta), 0f, 0.000001f);
        Assert.InRange(MathF.Abs(first.FinalNeutralScaleDelta - second.FinalNeutralScaleDelta), 0f, 0.000001f);
    }

    [Fact]
    public void StaticRbfSupport_IsRepeatableAndDoesNotMutateIndependentTransformFields()
    {
        var settings = new AdvancedBodyScalingSettings
        {
            Enabled = true,
            Mode = AdvancedBodyScalingMode.Strong,
            PoseCorrectives = { Enabled = true, Strength = 0.75f },
        };
        var firstActor = CreateField();
        var secondActor = CreateField();
        var firstBefore = firstActor.ToDictionary(pair => pair.Key, pair => pair.Value.DeepCopy(), StringComparer.Ordinal);
        var first = AdvancedBodyScalingPoseCorrectiveSystem.EstimateStaticSupport(firstActor, settings);
        var second = AdvancedBodyScalingPoseCorrectiveSystem.EstimateStaticSupport(secondActor, settings);

        Assert.NotEmpty(first);
        Assert.Equal(first.Select(item => item.Region), second.Select(item => item.Region));
        Assert.All(first, item => Assert.True(float.IsFinite(item.Activation) && float.IsFinite(item.Strength)));
        foreach (var (name, before) in firstBefore)
            Assert.Equal(before.Scaling, firstActor[name].Scaling);
    }

    private static Dictionary<string, BoneTransform> CreateField()
        => new(StringComparer.Ordinal)
        {
            ["j_kubi"] = Transform(1.00f),
            ["j_sako_l"] = Transform(1.26f),
            ["j_sako_r"] = Transform(1.22f),
            ["n_hkata_l"] = Transform(1.18f),
            ["n_hkata_r"] = Transform(1.16f),
            ["j_sebo_c"] = Transform(1.08f),
            ["j_sebo_b"] = Transform(1.06f),
            ["j_mune_l"] = Transform(1.10f),
            ["j_mune_r"] = Transform(1.10f),
        };

    private static BoneTransform Transform(float scale)
        => new() { Scaling = new Vector3(scale) };
}
