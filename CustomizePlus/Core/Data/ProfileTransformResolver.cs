// Copyright (c) Customize+.
// Licensed under the MIT license.

using System;
using System.Collections.Generic;
using System.Numerics;
using CustomizePlus.Core.Extensions;
using CustomizePlus.Profiles.Data;
using CustomizePlus.Templates.Data;

namespace CustomizePlus.Core.Data;

internal static class ProfileTransformResolver
{
    internal sealed class Resolution
    {
        public Dictionary<string, Template> BoneOwners { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, BoneTransform> EffectiveTransforms { get; } = new(StringComparer.Ordinal);
    }

    public static Resolution Resolve(Profile profile)
    {
        var resolution = new Resolution();
        var accumulators = new Dictionary<string, WeightedBoneAccumulator>(StringComparer.Ordinal);

        foreach (var template in profile.Templates)
        {
            if (profile.DisabledTemplates.Contains(template.UniqueId))
                continue;

            var templateWeight = profile.GetTemplateWeight(template.UniqueId);
            if (templateWeight <= 0f)
                continue;

            foreach (var (boneName, transform) in template.Bones)
            {
                resolution.BoneOwners[boneName] = template;

                if (!accumulators.TryGetValue(boneName, out var accumulator))
                {
                    accumulator = new WeightedBoneAccumulator();
                    accumulators.Add(boneName, accumulator);
                }

                accumulator.Add(transform, templateWeight);
            }
        }

        foreach (var (boneName, accumulator) in accumulators)
        {
            var transform = accumulator.ToBoneTransform();
            if (transform.IsEdited(true))
                resolution.EffectiveTransforms[boneName] = transform;
        }

        return resolution;
    }

    private sealed class WeightedBoneAccumulator
    {
        private int _contributionCount;
        private BoneTransform? _singleTransform;
        private float _totalWeight;
        private Vector3 _translationSum;
        private Vector3 _scaleOffsetSum;
        private Vector3 _childScaleOffsetSum;
        private Vector4 _rotationSum;
        private bool _hasRotation;
        private bool _propagateTranslation;
        private bool _propagateRotation;
        private bool _propagateScale;
        private bool _childScalingIndependent;
        private float _falloffSum;
        private float _falloffWeight;
        private BoneLockState _lockState = BoneLockState.Unlocked;

        public void Add(BoneTransform transform, float weight)
        {
            if (weight <= 0f)
                return;

            _contributionCount++;
            _singleTransform ??= transform.DeepCopy();
            _totalWeight += weight;
            _translationSum += transform.Translation * weight;
            _scaleOffsetSum += (transform.Scaling - Vector3.One) * weight;

            var childScaling = transform.ChildScalingIndependent ? transform.ChildScaling : Vector3.One;
            _childScaleOffsetSum += (childScaling - Vector3.One) * weight;
            _childScalingIndependent |= transform.ChildScalingIndependent;

            var rotation = transform.Rotation.ToQuaternion();
            var rotationVector = rotation.GetAsNumericsVector();

            if (!_hasRotation)
            {
                _rotationSum = rotationVector * weight;
                _hasRotation = true;
            }
            else
            {
                if (Vector4.Dot(_rotationSum, rotationVector) < 0f)
                    rotationVector *= -1f;

                _rotationSum += rotationVector * weight;
            }

            _propagateTranslation |= transform.PropagateTranslation;
            _propagateRotation |= transform.PropagateRotation;
            _propagateScale |= transform.PropagateScale;

            if (transform.LockState == BoneLockState.Locked)
                _lockState = BoneLockState.Locked;
            else if (transform.LockState == BoneLockState.Priority && _lockState == BoneLockState.Unlocked)
                _lockState = BoneLockState.Priority;

            if (transform.PropagateTranslation || transform.PropagateRotation || transform.PropagateScale)
            {
                _falloffSum += transform.PropagationFalloff * weight;
                _falloffWeight += weight;
            }
        }

        public BoneTransform ToBoneTransform()
        {
            if (_totalWeight <= 0f)
                return new BoneTransform();

            if (_contributionCount == 1 && _singleTransform != null)
                return _singleTransform.DeepCopy();

            var inverseWeight = 1f / _totalWeight;
            var rotation = Quaternion.Identity;

            if (_hasRotation)
            {
                var averaged = _rotationSum * inverseWeight;
                if (averaged.LengthSquared() > 0f)
                    rotation = Quaternion.Normalize(new Quaternion(averaged.X, averaged.Y, averaged.Z, averaged.W));
            }

            var childScaling = Vector3.One + (_childScaleOffsetSum * inverseWeight);
            var childScalingIndependent = _childScalingIndependent && !childScaling.IsApproximately(Vector3.One, 0.00001f);

            return new BoneTransform
            {
                Translation = _translationSum * inverseWeight,
                Rotation = BoneTransform.FromQuaternionDegrees(rotation),
                Scaling = Vector3.One + (_scaleOffsetSum * inverseWeight),
                ChildScaling = childScaling,
                ChildScalingIndependent = childScalingIndependent,
                PropagateTranslation = _propagateTranslation,
                PropagateRotation = _propagateRotation,
                PropagateScale = _propagateScale,
                PropagationFalloff = _falloffWeight > 0f
                    ? _falloffSum / _falloffWeight
                    : Constants.DefaultPropagationFalloff,
                LockState = _lockState,
            };
        }
    }
}
