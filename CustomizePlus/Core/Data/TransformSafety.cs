// Copyright (c) Customize+.
// Licensed under the MIT license.

using System;
using System.Numerics;
using CustomizePlus.Core.Extensions;
using FFXIVClientStructs.Havok.Common.Base.Math.QsTransform;

namespace CustomizePlus.Core.Data;

/// <summary>
/// Small, allocation-free validation helpers for values that can reach live game transforms.
/// </summary>
internal static class TransformSafety
{
    public const float MinimumDenominator = 0.00001f;
    private const float MinimumQuaternionLengthSquared = 0.00000001f;

    public static bool IsFinite(float value)
        => float.IsFinite(value);

    public static bool IsFinite(Vector2 value)
        => IsFinite(value.X) && IsFinite(value.Y);

    public static bool IsFinite(Vector3 value)
        => IsFinite(value.X) && IsFinite(value.Y) && IsFinite(value.Z);

    public static bool IsFinite(Quaternion value)
        => IsFinite(value.X) && IsFinite(value.Y) && IsFinite(value.Z) && IsFinite(value.W);

    public static bool IsFinite(Matrix4x4 value)
        => IsFinite(value.M11) && IsFinite(value.M12) && IsFinite(value.M13) && IsFinite(value.M14)
           && IsFinite(value.M21) && IsFinite(value.M22) && IsFinite(value.M23) && IsFinite(value.M24)
           && IsFinite(value.M31) && IsFinite(value.M32) && IsFinite(value.M33) && IsFinite(value.M34)
           && IsFinite(value.M41) && IsFinite(value.M42) && IsFinite(value.M43) && IsFinite(value.M44);

    public static Vector3 SanitizeVector(Vector3 value, Vector3 fallback)
        => new(
            IsFinite(value.X) ? value.X : fallback.X,
            IsFinite(value.Y) ? value.Y : fallback.Y,
            IsFinite(value.Z) ? value.Z : fallback.Z);

    public static float ClampFinite(float value, float min, float max, float fallback)
        => IsFinite(value) ? Math.Clamp(value, min, max) : fallback;

    public static float WrapDegrees(float angle)
    {
        if (!IsFinite(angle))
            return 0f;

        var wrapped = angle % 360f;
        if (wrapped > 180f)
            wrapped -= 360f;
        else if (wrapped < -180f)
            wrapped += 360f;

        return wrapped;
    }

    public static bool TryNormalize(Quaternion value, out Quaternion normalized)
    {
        normalized = Quaternion.Identity;
        if (!IsFinite(value))
            return false;

        var lengthSquared = value.LengthSquared();
        if (!IsFinite(lengthSquared) || lengthSquared <= MinimumQuaternionLengthSquared)
            return false;

        var inverseLength = 1f / MathF.Sqrt(lengthSquared);
        if (!IsFinite(inverseLength))
            return false;

        normalized = value * inverseLength;
        return IsFinite(normalized);
    }

    public static bool TryDivide(Vector3 numerator, Vector3 denominator, out Vector3 result)
    {
        result = Vector3.One;
        if (!IsFinite(numerator) || !IsFinite(denominator))
            return false;

        if (MathF.Abs(denominator.X) < MinimumDenominator
            || MathF.Abs(denominator.Y) < MinimumDenominator
            || MathF.Abs(denominator.Z) < MinimumDenominator)
            return false;

        result = new Vector3(
            numerator.X / denominator.X,
            numerator.Y / denominator.Y,
            numerator.Z / denominator.Z);
        return IsFinite(result);
    }

    public static unsafe bool TrySanitizeNativeTransform(ref hkQsTransformf transform)
    {
        if (!IsFinite(transform.Translation.X) || !IsFinite(transform.Translation.Y)
            || !IsFinite(transform.Translation.Z) || !IsFinite(transform.Translation.W)
            || !IsFinite(transform.Scale.X) || !IsFinite(transform.Scale.Y)
            || !IsFinite(transform.Scale.Z) || !IsFinite(transform.Scale.W))
            return false;

        if (!TryNormalize(transform.Rotation.ToQuaternion(), out var rotation))
            return false;

        transform.Rotation.X = rotation.X;
        transform.Rotation.Y = rotation.Y;
        transform.Rotation.Z = rotation.Z;
        transform.Rotation.W = rotation.W;
        return true;
    }
}
