// Copyright (c) Customize+.
// Licensed under the MIT license.

using System;
using System.Numerics;

namespace CustomizePlus.Core.Data;

/// <summary>
/// Safe log-scale decomposition shared by the static shape-conditioning passes.
/// The mean is a local log-volume proxy; deviations describe anisotropic shape.
/// </summary>
internal readonly record struct AdvancedBodyScalingLogScale(Vector3 Mean, Vector3 Deviation)
{
    public float ScalarMean => Mean.X;

    public float Anisotropy => MathF.Max(Deviation.X, MathF.Max(Deviation.Y, Deviation.Z))
        - MathF.Min(Deviation.X, MathF.Min(Deviation.Y, Deviation.Z));

    public static bool TryCreate(Vector3 scale, out AdvancedBodyScalingLogScale result)
    {
        result = default;
        if (!TransformSafety.IsFinite(scale) || scale.X <= 0f || scale.Y <= 0f || scale.Z <= 0f)
            return false;

        var log = new Vector3(MathF.Log(scale.X), MathF.Log(scale.Y), MathF.Log(scale.Z));
        if (!TransformSafety.IsFinite(log))
            return false;

        var mean = (log.X + log.Y + log.Z) / 3f;
        if (!float.IsFinite(mean))
            return false;

        result = new AdvancedBodyScalingLogScale(new Vector3(mean), log - new Vector3(mean));
        return true;
    }

    public bool TryReconstruct(out Vector3 scale)
    {
        scale = Vector3.One;
        var log = Mean + Deviation;
        if (!TransformSafety.IsFinite(log))
            return false;

        scale = new Vector3(MathF.Exp(log.X), MathF.Exp(log.Y), MathF.Exp(log.Z));
        return TransformSafety.IsFinite(scale) && scale.X > 0f && scale.Y > 0f && scale.Z > 0f;
    }

    public AdvancedBodyScalingLogScale WithMean(float mean)
        => new(new Vector3(mean), Deviation);

    public AdvancedBodyScalingLogScale WithDeviation(Vector3 deviation)
        => new(Mean, deviation);

    public AdvancedBodyScalingLogScale WithLog(Vector3 log)
    {
        var mean = (log.X + log.Y + log.Z) / 3f;
        return new AdvancedBodyScalingLogScale(new Vector3(mean), log - new Vector3(mean));
    }
}

internal static class AdvancedBodyScalingShapeConditioningMath
{
    public static float SmoothStep(float edge0, float edge1, float value)
    {
        if (edge1 <= edge0)
            return value >= edge1 ? 1f : 0f;

        var t = Math.Clamp((value - edge0) / (edge1 - edge0), 0f, 1f);
        return t * t * (3f - (2f * t));
    }

    public static float JointResponse(float angleDegrees, float startDegrees, float fullDegrees)
        => SmoothStep(startDegrees, fullDegrees, Math.Clamp(angleDegrees, 0f, 180f));
}
