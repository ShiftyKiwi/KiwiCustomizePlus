using System.Numerics;
using CustomizePlus.Core.Data;
using Xunit;

namespace CustomizePlus.Tests;

public class TransformSafetyTests
{
    [Fact]
    public void WrapDegrees_NormalizesLargeFiniteAngles()
    {
        Assert.Equal(0f, TransformSafety.WrapDegrees(720f));
        Assert.Equal(90f, TransformSafety.WrapDegrees(810f));
        Assert.Equal(-90f, TransformSafety.WrapDegrees(-810f));
    }

    [Fact]
    public void WrapDegrees_NeutralizesNonFiniteAngles()
    {
        Assert.Equal(0f, TransformSafety.WrapDegrees(float.NaN));
        Assert.Equal(0f, TransformSafety.WrapDegrees(float.PositiveInfinity));
    }

    [Fact]
    public void TryDivide_RejectsZeroAndNonFiniteDenominators()
    {
        Assert.False(TransformSafety.TryDivide(Vector3.One, new Vector3(1f, 0f, 1f), out _));
        Assert.False(TransformSafety.TryDivide(Vector3.One, new Vector3(1f, float.NaN, 1f), out _));
    }

    [Fact]
    public void TryDivide_ReturnsFiniteComponentWiseResult()
    {
        Assert.True(TransformSafety.TryDivide(new Vector3(4f, 6f, 8f), new Vector3(2f, 3f, 4f), out var result));
        Assert.Equal(Vector3.One * 2f, result);
    }

    [Fact]
    public void TryNormalize_RejectsDegenerateQuaternion()
    {
        Assert.False(TransformSafety.TryNormalize(Quaternion.Zero, out _));
        Assert.False(TransformSafety.TryNormalize(new Quaternion(float.NaN, 0f, 0f, 1f), out _));
    }

    [Fact]
    public void TryNormalize_ReturnsUnitQuaternion()
    {
        Assert.True(TransformSafety.TryNormalize(new Quaternion(0f, 0f, 3f, 4f), out var result));
        Assert.InRange(result.LengthSquared(), 0.99999f, 1.00001f);
    }

    [Fact]
    public void BoneTransform_NormalizesLargeAndNonFiniteEditorAngles()
    {
        var transform = new BoneTransform
        {
            Rotation = new Vector3(810f, -810f, float.NaN),
        };

        Assert.Equal(90f, transform.Rotation.X);
        Assert.Equal(-90f, transform.Rotation.Y);
        Assert.Equal(0f, transform.Rotation.Z);
    }

    [Fact]
    public void BoneTransform_NeutralizesNonFiniteScaleComponents()
    {
        var transform = new BoneTransform
        {
            Scaling = new Vector3(1.2f, float.PositiveInfinity, 0.8f),
        };

        Assert.Equal(new Vector3(1.2f, 1f, 0.8f), transform.Scaling);
    }

    [Fact]
    public void SkeletonTopology_AllowsMultipleRoots()
    {
        Assert.True(SkeletonTopologyValidator.HasValidTopology(new[] { -1, 0, -1, 2 }));
    }

    [Theory]
    [InlineData(new[] { 1, 0 })]
    [InlineData(new[] { -1, 3 })]
    [InlineData(new[] { -1, 1 })]
    public void SkeletonTopology_RejectsCyclesAndInvalidParents(int[] parents)
    {
        Assert.False(SkeletonTopologyValidator.HasValidTopology(parents));
    }

    [Fact]
    public void BoneTransform_DeterministicFuzzNeverRetainsUnsafeEditorValues()
    {
        var random = new Random(0x43504C55);
        var specialValues = new[]
        {
            float.NaN, float.PositiveInfinity, float.NegativeInfinity, 0f, -0.000001f,
            0.000001f, -1000000f, 1000000f,
        };

        for (var index = 0; index < 256; ++index)
        {
            float NextValue()
                => index % 8 == 0
                    ? specialValues[index % specialValues.Length]
                    : ((float)random.NextDouble() - 0.5f) * 2000000f;

            var transform = new BoneTransform
            {
                Translation = new Vector3(NextValue(), NextValue(), NextValue()),
                Rotation = new Vector3(NextValue(), NextValue(), NextValue()),
                Scaling = new Vector3(NextValue(), NextValue(), NextValue()),
                ChildScaling = new Vector3(NextValue(), NextValue(), NextValue()),
                PropagationFalloff = NextValue(),
            };

            Assert.True(TransformSafety.IsFinite(transform.Translation));
            Assert.True(TransformSafety.IsFinite(transform.Rotation));
            Assert.True(TransformSafety.IsFinite(transform.Scaling));
            Assert.True(TransformSafety.IsFinite(transform.ChildScaling));
            Assert.InRange(transform.PropagationFalloff, 0f, 1f);
            Assert.InRange(transform.Rotation.X, -180f, 180f);
            Assert.InRange(transform.Rotation.Y, -180f, 180f);
            Assert.InRange(transform.Rotation.Z, -180f, 180f);

            var quaternion = new Quaternion(NextValue(), NextValue(), NextValue(), NextValue());
            if (TransformSafety.TryNormalize(quaternion, out var normalized))
            {
                Assert.True(TransformSafety.IsFinite(normalized));
                Assert.InRange(normalized.LengthSquared(), 0.9999f, 1.0001f);
            }
        }
    }
}
