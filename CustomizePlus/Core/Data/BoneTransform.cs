using System;
using System.Numerics;
using System.Runtime.Serialization;
using CustomizePlus.Core.Extensions;
using CustomizePlus.Game.Services.GPose.ExternalTools;
using FFXIVClientStructs.Havok.Common.Base.Math.QsTransform;

namespace CustomizePlus.Core.Data;

//not the correct terms but they double as user-visible labels so ¯\_(ツ)_/¯
public enum BoneAttribute
{
    //hard-coding the backing values for legacy purposes
    Position = 0,
    Rotation = 1,
    Scale = 2,
    ChildScaling = 3
}

[Serializable]
public class BoneTransform
{
    //TODO if if ever becomes a point of concern, I might be able to marginally speed things up
    //by natively storing translation and scaling values as their own vector4s
    //that way the cost of translating back and forth to vector3s would be frontloaded
    //to when the user is updating things instead of during the render loop

    public BoneTransform()
    {
        Translation = Vector3.Zero;
        Rotation = Vector3.Zero;
        Scaling = Vector3.One;
        ChildScaling = Vector3.One;
        PropagationFalloff = Constants.DefaultPropagationFalloff;
    }

    public BoneTransform(BoneTransform original)
    {
        UpdateToMatch(original);
    }

    private Vector3 _translation;
    public Vector3 Translation
    {
        get => _translation;
        set => _translation = ClampVector(value);
    }

    private Vector3 _rotation;
    public Vector3 Rotation
    {
        get => _rotation;
        set
        {
            _rotation = ClampAngles(value);
            _runtimeRotationQuaternion = null;
        }
    }

    private Vector3 _scaling;
    public Vector3 Scaling
    {
        get => _scaling;
        set => _scaling = ClampVector(value);
    }

    private Vector3 _childScaling;
    public Vector3 ChildScaling
    {
        get => _childScaling;
        set => _childScaling = ClampVector(value);
    }

    public bool PropagateTranslation = false;
    public bool PropagateRotation = false;
    public bool PropagateScale = false;
    public bool ChildScalingIndependent = false;
    private float _propagationFalloff = Constants.DefaultPropagationFalloff;
    [NonSerialized]
    private Quaternion? _runtimeRotationQuaternion;

    public float PropagationFalloff
    {
        get => _propagationFalloff;
        set => _propagationFalloff = Math.Clamp(value, 0f, 1f);
    }

    public bool ShouldSerializeChildScaling() => ChildScalingIndependent;
    public bool ShouldSerializePropagationFalloff() => MathF.Abs(PropagationFalloff - Constants.DefaultPropagationFalloff) > 0.0001f;

    [OnDeserialized]
    internal void OnDeserialized(StreamingContext context)
    {
        //Sanitize all values on deserialization
        _translation = ClampToDefaultLimits(_translation);
        _rotation = ClampAngles(_rotation);
        _scaling = ClampToDefaultLimits(_scaling);
        _propagationFalloff = Math.Clamp(_propagationFalloff, 0f, 1f);

        if (_childScaling == Vector3.Zero && !ChildScalingIndependent)
            _childScaling = Vector3.One;
        else
            _childScaling = ClampToDefaultLimits(_childScaling);
    }

    //"considerPropagationAsEdit" only should be true if you know what you are doing
    //currently is here only to allow bones to be not removed when live editing is off in the editor and propagation is on on the bone
    public bool IsEdited(bool considerPropagationAsEdit = false)
    {
        bool propagation = false;
        if (considerPropagationAsEdit)
            propagation = PropagateTranslation || PropagateRotation || PropagateScale
                || (MathF.Abs(PropagationFalloff - Constants.DefaultPropagationFalloff) > 0.0001f
                    && (PropagateTranslation || PropagateRotation || PropagateScale));

        return !Translation.IsApproximately(Vector3.Zero, 0.00001f)
               || HasEffectiveRotation()
               || !Scaling.IsApproximately(Vector3.One, 0.00001f)
               || (ChildScalingIndependent && !ChildScaling.IsApproximately(Vector3.One, 0.00001f))
               || propagation;
    }

    public bool HasEffectiveRotation()
    {
        var rotation = Quaternion.Normalize(GetEffectiveRotationQuaternion());
        return 1f - MathF.Abs(Quaternion.Dot(rotation, Quaternion.Identity)) > 0.000001f;
    }

    public BoneTransform DeepCopy()
    {
        return new BoneTransform
        {
            Translation = Translation,
            Rotation = Rotation,
            Scaling = Scaling,
            PropagateTranslation = PropagateTranslation,
            PropagateRotation = PropagateRotation,
            PropagateScale = PropagateScale,
            ChildScaling = ChildScaling,
            ChildScalingIndependent = ChildScalingIndependent,
            PropagationFalloff = PropagationFalloff,
        };
    }

    public void UpdateAttribute(BoneAttribute which, Vector3 newValue, bool shouldPropagate)
    {
        if (which == BoneAttribute.Position)
        {
            Translation = newValue;
            PropagateTranslation = shouldPropagate;
        }
        else if (which == BoneAttribute.Rotation)
        {
            Rotation = newValue;
            PropagateRotation = shouldPropagate;
        }
        else if (which == BoneAttribute.ChildScaling)
        {
            ChildScaling = newValue;
        }
        else
        {
            Scaling = newValue;
            PropagateScale = shouldPropagate;
            if (!shouldPropagate)
            {
                ChildScaling = Vector3.One;
                ChildScalingIndependent = false;
                PropagationFalloff = Constants.DefaultPropagationFalloff;
            }
        }
    }

    public void UpdateToMatch(BoneTransform newValues)
    {
        Translation = newValues.Translation;
        Rotation = newValues.Rotation;
        Scaling = newValues.Scaling;
        ChildScaling = newValues.ChildScaling;
        PropagateTranslation = newValues.PropagateTranslation;
        PropagateRotation = newValues.PropagateRotation;
        PropagateScale = newValues.PropagateScale;
        ChildScalingIndependent = newValues.ChildScalingIndependent;
        PropagationFalloff = newValues.PropagationFalloff;
        _runtimeRotationQuaternion = newValues._runtimeRotationQuaternion;
    }

    /// <summary>
    ///     Flip a bone's transforms from left to right, so you can use it to update its sibling.
    ///     IVCS bones need to use the special reflection instead.
    /// </summary>
    public BoneTransform GetStandardReflection()
    {
        return new BoneTransform
        {
            Translation = new Vector3(Translation.X, Translation.Y, -1 * Translation.Z),
            Rotation = new Vector3(-1 * Rotation.X, -1 * Rotation.Y, Rotation.Z),
            Scaling = Scaling,
            ChildScaling = ChildScaling,
            PropagateTranslation = PropagateTranslation,
            PropagateRotation = PropagateRotation,
            PropagateScale = PropagateScale,
            ChildScalingIndependent = ChildScalingIndependent,
            PropagationFalloff = PropagationFalloff,
        };
    }

    /// <summary>
    ///     Flip a bone's transforms from left to right, so you can use it to update its sibling.
    ///     IVCS bones are oriented in a system with different symmetries, so they're handled specially.
    /// </summary>
    public BoneTransform GetSpecialReflection()
    {
        return new BoneTransform
        {
            Translation = new Vector3(Translation.X, -1 * Translation.Y, Translation.Z),
            Rotation = new Vector3(Rotation.X, -1 * Rotation.Y, -1 * Rotation.Z),
            Scaling = Scaling,
            ChildScaling = ChildScaling,
            PropagateTranslation = PropagateTranslation,
            PropagateRotation = PropagateRotation,
            PropagateScale = PropagateScale,
            ChildScalingIndependent = ChildScalingIndependent,
            PropagationFalloff = PropagationFalloff,
        };
    }

    /// <summary>
    /// Sanitize all vectors inside of this container.
    /// </summary>
    private void Sanitize()
    {
        _translation = ClampVector(_translation);
        _rotation = ClampAngles(_rotation);
        _scaling = ClampVector(_scaling);
        _childScaling = _childScaling == Vector3.Zero ? Vector3.One : ClampVector(_childScaling);
        _propagationFalloff = Math.Clamp(_propagationFalloff, 0f, 1f);
    }

    /// <summary>
    /// Clamp all vector values to be within allowed limits.
    /// </summary>
    private Vector3 ClampVector(Vector3 vector)
    {
        return new Vector3
        {
            X = Math.Clamp(vector.X, Constants.MinVectorValueLimit, Constants.MaxVectorValueLimit),
            Y = Math.Clamp(vector.Y, Constants.MinVectorValueLimit, Constants.MaxVectorValueLimit),
            Z = Math.Clamp(vector.Z, Constants.MinVectorValueLimit, Constants.MaxVectorValueLimit)
        };
    }

    private static Vector3 ClampAngles(Vector3 rotVec)
    {
        static float Clamp(float angle)
        {
            if (angle > 180)
                angle -= 360;
            else if (angle < -180)
                angle += 360;

            return angle;
        }

        rotVec.X = Clamp(rotVec.X);
        rotVec.Y = Clamp(rotVec.Y);
        rotVec.Z = Clamp(rotVec.Z);

        return rotVec;
    }

    public hkQsTransformf ModifyExistingTransform(hkQsTransformf tr)
    {
        return ModifyExistingTranslationWithRotation(ModifyExistingRotation(ModifyExistingScale(tr)));
    }

    public hkQsTransformf ModifyExistingScale(hkQsTransformf tr)
    {
        if (PosingModeDetectService.IsAnamnesisScalingFrozen) return tr;

        tr.Scale.X *= Scaling.X;
        tr.Scale.Y *= Scaling.Y;
        tr.Scale.Z *= Scaling.Z;

        return tr;
    }

    public hkQsTransformf ModifyExistingRotation(hkQsTransformf tr)
    {
        if (PosingModeDetectService.IsAnamnesisRotationFrozen) return tr;

        var newRotation = Quaternion.Multiply(tr.Rotation.ToQuaternion(), GetEffectiveRotationQuaternion());
        tr.Rotation.X = newRotation.X;
        tr.Rotation.Y = newRotation.Y;
        tr.Rotation.Z = newRotation.Z;
        tr.Rotation.W = newRotation.W;

        return tr;
    }

    public hkQsTransformf ModifyExistingTranslationWithRotation(hkQsTransformf tr)
    {
        if (PosingModeDetectService.IsAnamnesisPositionFrozen) return tr;

        var adjustedTranslation = Vector4.Transform(Translation, tr.Rotation.ToQuaternion());
        tr.Translation.X += adjustedTranslation.X;
        tr.Translation.Y += adjustedTranslation.Y;
        tr.Translation.Z += adjustedTranslation.Z;
        tr.Translation.W += adjustedTranslation.W;

        return tr;
    }

    public hkQsTransformf ModifyExistingTranslation(hkQsTransformf tr)
    {
        if (PosingModeDetectService.IsAnamnesisPositionFrozen) return tr;

        tr.Translation.X += Translation.X;
        tr.Translation.Y += Translation.Y;
        tr.Translation.Z += Translation.Z;

        return tr;
    }

    /// <summary>
    ///     Clamp all vector values to be within allowed limits.
    /// </summary>
    private static Vector3 ClampToDefaultLimits(Vector3 vector)
    {
        vector.X = Math.Clamp(vector.X, Constants.MinVectorValueLimit, Constants.MaxVectorValueLimit);
        vector.Y = Math.Clamp(vector.Y, Constants.MinVectorValueLimit, Constants.MaxVectorValueLimit);
        vector.Z = Math.Clamp(vector.Z, Constants.MinVectorValueLimit, Constants.MaxVectorValueLimit);

        return vector;
    }

    public static Vector3 FromQuaternionDegrees(Quaternion quaternion)
    {
        quaternion = Quaternion.Normalize(quaternion);

        var sinrCosp = 2f * ((quaternion.W * quaternion.X) + (quaternion.Y * quaternion.Z));
        var cosrCosp = 1f - (2f * ((quaternion.X * quaternion.X) + (quaternion.Y * quaternion.Y)));
        var pitch = MathF.Atan2(sinrCosp, cosrCosp);

        var sinp = 2f * ((quaternion.W * quaternion.Y) - (quaternion.Z * quaternion.X));
        float yaw;
        if (MathF.Abs(sinp) >= 1f)
            yaw = MathF.CopySign(MathF.PI / 2f, sinp);
        else
            yaw = MathF.Asin(sinp);

        var sinyCosp = 2f * ((quaternion.W * quaternion.Z) + (quaternion.X * quaternion.Y));
        var cosyCosp = 1f - (2f * ((quaternion.Y * quaternion.Y) + (quaternion.Z * quaternion.Z)));
        var roll = MathF.Atan2(sinyCosp, cosyCosp);

        return new Vector3(
            pitch * 180f / MathF.PI,
            yaw * 180f / MathF.PI,
            roll * 180f / MathF.PI);
    }

    public bool SmoothTowards(BoneTransform target, float deltaSeconds, float sharpness = Constants.TransformTransitionSharpness)
    {
        if (deltaSeconds <= 0f)
        {
            UpdateToMatch(target);
            _runtimeRotationQuaternion = target.GetEffectiveRotationQuaternion();
            return true;
        }

        var alpha = 1f - MathF.Exp(-sharpness * deltaSeconds);
        Translation = Vector3.Lerp(Translation, target.Translation, alpha);
        Scaling = Vector3.Lerp(Scaling, target.Scaling, alpha);
        ChildScaling = Vector3.Lerp(ChildScaling, target.ChildScaling, alpha);
        PropagationFalloff += (target.PropagationFalloff - PropagationFalloff) * alpha;
        ChildScalingIndependent = target.ChildScalingIndependent;
        PropagateTranslation = target.PropagateTranslation;
        PropagateRotation = target.PropagateRotation;
        PropagateScale = target.PropagateScale;

        var currentRotation = GetEffectiveRotationQuaternion();
        var targetRotation = target.GetEffectiveRotationQuaternion();
        if (Quaternion.Dot(currentRotation, targetRotation) < 0f)
            targetRotation = Quaternion.Negate(targetRotation);

        _rotation = target.Rotation;
        _runtimeRotationQuaternion = Quaternion.Normalize(Quaternion.Slerp(currentRotation, targetRotation, alpha));

        return Translation.IsApproximately(target.Translation, 0.0001f)
            && (1f - MathF.Abs(Quaternion.Dot(_runtimeRotationQuaternion.Value, targetRotation))) < 0.0001f
            && Scaling.IsApproximately(target.Scaling, 0.0001f)
            && ChildScaling.IsApproximately(target.ChildScaling, 0.0001f)
            && MathF.Abs(PropagationFalloff - target.PropagationFalloff) < 0.0001f
            && ChildScalingIndependent == target.ChildScalingIndependent;
    }

    private Quaternion GetEffectiveRotationQuaternion()
        => _runtimeRotationQuaternion ?? Rotation.ToQuaternion();
}
