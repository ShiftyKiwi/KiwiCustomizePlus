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
        public List<TemplateApplicability> TemplateApplicability { get; } = new();
    }

    internal readonly record struct TemplateContribution(
        Guid TemplateId,
        IReadOnlyDictionary<string, BoneTransform> Bones,
        bool Enabled,
        float Weight);

    internal readonly record struct TemplateApplicability(
        Guid TemplateId,
        string TemplateName,
        bool Enabled,
        TemplateCompatibilityRequirement Requirement,
        bool Active,
        string Reason,
        int SavedTransformCount);

    internal sealed class ContributionResolution
    {
        public Dictionary<string, Guid> BoneOwnerIds { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, BoneTransform> EffectiveTransforms { get; } = new(StringComparer.Ordinal);
    }

    public static Resolution Resolve(Profile profile, SkeletonCapabilityManifest? manifest = null)
    {
        var resolution = new Resolution();
        var templatesById = new Dictionary<Guid, Template>();
        var contributions = new List<TemplateContribution>(profile.Templates.Count);
        manifest ??= SkeletonCapabilityManifest.Unavailable;

        foreach (var template in profile.Templates)
        {
            templatesById[template.UniqueId] = template;
            var enabled = !profile.DisabledTemplates.Contains(template.UniqueId);
            var requirement = profile.GetTemplateCompatibilityRequirement(template.UniqueId);
            var applicability = requirement.Evaluate(manifest);
            resolution.TemplateApplicability.Add(new TemplateApplicability(
                template.UniqueId,
                template.Name.Text,
                enabled,
                requirement,
                enabled && applicability.IsActive,
                applicability.Reason,
                template.Bones.Count));
            contributions.Add(new TemplateContribution(
                template.UniqueId,
                template.Bones,
                enabled && applicability.IsActive,
                profile.GetTemplateWeight(template.UniqueId)));
        }

        var contributionResolution = ResolveContributions(contributions);
        foreach (var (boneName, templateId) in contributionResolution.BoneOwnerIds)
        {
            if (templatesById.TryGetValue(templateId, out var template))
                resolution.BoneOwners[boneName] = template;
        }

        foreach (var (boneName, transform) in contributionResolution.EffectiveTransforms)
            resolution.EffectiveTransforms[boneName] = transform;

        return resolution;
    }

    /// <summary>
    /// Resolves an already-selected template stack without consulting profile ownership.
    /// This keeps the weighting/composition policy testable while callers retain their
    /// normal profile selection and invalidation responsibilities.
    /// </summary>
    internal static ContributionResolution ResolveContributions(IEnumerable<TemplateContribution> contributions)
    {
        var resolution = new ContributionResolution();
        var accumulators = new Dictionary<string, WeightedBoneAccumulator>(StringComparer.Ordinal);

        foreach (var template in contributions)
        {
            if (!template.Enabled)
                continue;

            var templateWeight = template.Weight;
            if (!TransformSafety.IsFinite(templateWeight) || templateWeight <= 0f)
                continue;

            foreach (var (boneName, transform) in template.Bones)
            {
                resolution.BoneOwnerIds[boneName] = template.TemplateId;

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
        private bool _pinX;
        private bool _pinY;
        private bool _pinZ;

        public void Add(BoneTransform transform, float weight)
        {
            if (!TransformSafety.IsFinite(weight) || weight <= 0f || !TransformSafety.IsFinite(_totalWeight + weight))
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
            if (!TransformSafety.TryNormalize(rotation, out rotation))
                rotation = Quaternion.Identity;

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
            _pinX |= transform.PinX;
            _pinY |= transform.PinY;
            _pinZ |= transform.PinZ;

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
            if (!TransformSafety.IsFinite(_totalWeight) || _totalWeight <= 0f)
                return new BoneTransform();

            if (_contributionCount == 1 && _singleTransform != null)
                return _singleTransform.DeepCopy();

            var inverseWeight = 1f / _totalWeight;
            if (!TransformSafety.IsFinite(inverseWeight))
                return new BoneTransform();

            var rotation = Quaternion.Identity;

            if (_hasRotation)
            {
                var averaged = _rotationSum * inverseWeight;
                var rotationCandidate = new Quaternion(averaged.X, averaged.Y, averaged.Z, averaged.W);
                if (TransformSafety.TryNormalize(rotationCandidate, out var normalizedRotation))
                    rotation = normalizedRotation;
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
                PinX = _pinX,
                PinY = _pinY,
                PinZ = _pinZ,
            };
        }
    }
}
