// Copyright (c) Customize+.
// Licensed under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace CustomizePlus.Core.Data;

internal static class AdvancedBodyScalingMotionWarpingTuning
{
    public const string ImplementationTierLabel = "Tier C - Locomotion warping only";

    public const float RecommendedGlobalStrengthMax = 0.34f;
    public const float RecommendedStrideWarpStrengthMax = 0.42f;
    public const float RecommendedOrientationWarpStrengthMax = 0.36f;
    public const float RecommendedPostureWarpStrengthMax = 0.34f;
    public const float RecommendedMotionSafetyBiasMin = 0.78f;
    public const float RecommendedBlendBiasMax = 0.68f;
    public const float RecommendedMaxCorrectionClampMax = 0.18f;

    public const float UiMaxGlobalStrength = 0.55f;
    public const float UiMaxStrideWarpStrength = 0.75f;
    public const float UiMaxOrientationWarpStrength = 0.70f;
    public const float UiMaxPostureWarpStrength = 0.65f;
    public const float UiMaxBlendBias = 0.90f;
    public const float UiMaxCorrectionClamp = 0.34f;

    public static float GetRecommendedChainStrengthMax(AdvancedBodyScalingFullBodyIkChain chain)
        => chain switch
        {
            AdvancedBodyScalingFullBodyIkChain.PelvisRoot => 0.52f,
            AdvancedBodyScalingFullBodyIkChain.Spine => 0.56f,
            AdvancedBodyScalingFullBodyIkChain.NeckHead => 0.40f,
            AdvancedBodyScalingFullBodyIkChain.LeftArm => 0.44f,
            AdvancedBodyScalingFullBodyIkChain.RightArm => 0.44f,
            AdvancedBodyScalingFullBodyIkChain.LeftLeg => 0.58f,
            AdvancedBodyScalingFullBodyIkChain.RightLeg => 0.58f,
            _ => 0.50f,
        };

    public static float GetUiMaxChainStrength(AdvancedBodyScalingFullBodyIkChain chain)
        => chain switch
        {
            AdvancedBodyScalingFullBodyIkChain.PelvisRoot => 0.75f,
            AdvancedBodyScalingFullBodyIkChain.Spine => 0.80f,
            AdvancedBodyScalingFullBodyIkChain.NeckHead => 0.60f,
            AdvancedBodyScalingFullBodyIkChain.LeftArm => 0.65f,
            AdvancedBodyScalingFullBodyIkChain.RightArm => 0.65f,
            AdvancedBodyScalingFullBodyIkChain.LeftLeg => 0.80f,
            AdvancedBodyScalingFullBodyIkChain.RightLeg => 0.80f,
            _ => 0.70f,
        };
}

[Serializable]
public sealed class AdvancedBodyScalingMotionWarpingChainSettings
{
    private float _strength = 1f;

    public bool Enabled { get; set; } = true;

    public float Strength
    {
        get => _strength;
        set => _strength = Math.Clamp(value, 0f, 1f);
    }

    public AdvancedBodyScalingMotionWarpingChainSettings DeepCopy()
        => new()
        {
            Enabled = Enabled,
            Strength = Strength,
        };

    public static AdvancedBodyScalingMotionWarpingChainSettings CreateDefault(AdvancedBodyScalingFullBodyIkChain chain)
        => chain switch
        {
            AdvancedBodyScalingFullBodyIkChain.PelvisRoot => new AdvancedBodyScalingMotionWarpingChainSettings
            {
                Strength = 0.52f,
            },
            AdvancedBodyScalingFullBodyIkChain.Spine => new AdvancedBodyScalingMotionWarpingChainSettings
            {
                Strength = 0.54f,
            },
            AdvancedBodyScalingFullBodyIkChain.NeckHead => new AdvancedBodyScalingMotionWarpingChainSettings
            {
                Strength = 0.34f,
            },
            AdvancedBodyScalingFullBodyIkChain.LeftArm => new AdvancedBodyScalingMotionWarpingChainSettings
            {
                Strength = 0.32f,
            },
            AdvancedBodyScalingFullBodyIkChain.RightArm => new AdvancedBodyScalingMotionWarpingChainSettings
            {
                Strength = 0.32f,
            },
            AdvancedBodyScalingFullBodyIkChain.LeftLeg => new AdvancedBodyScalingMotionWarpingChainSettings
            {
                Strength = 0.58f,
            },
            AdvancedBodyScalingFullBodyIkChain.RightLeg => new AdvancedBodyScalingMotionWarpingChainSettings
            {
                Strength = 0.58f,
            },
            _ => new AdvancedBodyScalingMotionWarpingChainSettings(),
        };

    public static Dictionary<AdvancedBodyScalingFullBodyIkChain, AdvancedBodyScalingMotionWarpingChainSettings> CreateDefaults()
        => Enum
            .GetValues<AdvancedBodyScalingFullBodyIkChain>()
            .ToDictionary(chain => chain, CreateDefault);
}

[Serializable]
public sealed class AdvancedBodyScalingMotionWarpingSettings
{
    private float _globalStrength = 0.20f;
    private float _strideWarpStrength = 0.28f;
    private float _orientationWarpStrength = 0.20f;
    private float _postureWarpStrength = 0.22f;
    private float _motionSafetyBias = 0.86f;
    private float _blendBias = 0.52f;
    private float _maxCorrectionClamp = 0.12f;

    public bool Enabled { get; set; } = false;

    public float GlobalStrength
    {
        get => _globalStrength;
        set => _globalStrength = Math.Clamp(value, 0f, 1f);
    }

    public float StrideWarpStrength
    {
        get => _strideWarpStrength;
        set => _strideWarpStrength = Math.Clamp(value, 0f, 1f);
    }

    public float OrientationWarpStrength
    {
        get => _orientationWarpStrength;
        set => _orientationWarpStrength = Math.Clamp(value, 0f, 1f);
    }

    public float PostureWarpStrength
    {
        get => _postureWarpStrength;
        set => _postureWarpStrength = Math.Clamp(value, 0f, 1f);
    }

    public float MotionSafetyBias
    {
        get => _motionSafetyBias;
        set => _motionSafetyBias = Math.Clamp(value, 0f, 1f);
    }

    public float BlendBias
    {
        get => _blendBias;
        set => _blendBias = Math.Clamp(value, 0f, 1f);
    }

    public float MaxCorrectionClamp
    {
        get => _maxCorrectionClamp;
        set => _maxCorrectionClamp = Math.Clamp(value, 0f, 1f);
    }

    public Dictionary<AdvancedBodyScalingFullBodyIkChain, AdvancedBodyScalingMotionWarpingChainSettings> Chains { get; set; }
        = AdvancedBodyScalingMotionWarpingChainSettings.CreateDefaults();

    public AdvancedBodyScalingMotionWarpingChainSettings GetChainSettings(AdvancedBodyScalingFullBodyIkChain chain)
    {
        if (!Chains.TryGetValue(chain, out var settings))
        {
            settings = AdvancedBodyScalingMotionWarpingChainSettings.CreateDefault(chain);
            Chains[chain] = settings;
        }

        return settings;
    }

    public void ResetToDefaults()
    {
        var defaults = new AdvancedBodyScalingMotionWarpingSettings();
        Enabled = defaults.Enabled;
        GlobalStrength = defaults.GlobalStrength;
        StrideWarpStrength = defaults.StrideWarpStrength;
        OrientationWarpStrength = defaults.OrientationWarpStrength;
        PostureWarpStrength = defaults.PostureWarpStrength;
        MotionSafetyBias = defaults.MotionSafetyBias;
        BlendBias = defaults.BlendBias;
        MaxCorrectionClamp = defaults.MaxCorrectionClamp;
        Chains = AdvancedBodyScalingMotionWarpingChainSettings.CreateDefaults();
    }

    public AdvancedBodyScalingMotionWarpingSettings DeepCopy()
        => new()
        {
            Enabled = Enabled,
            GlobalStrength = GlobalStrength,
            StrideWarpStrength = StrideWarpStrength,
            OrientationWarpStrength = OrientationWarpStrength,
            PostureWarpStrength = PostureWarpStrength,
            MotionSafetyBias = MotionSafetyBias,
            BlendBias = BlendBias,
            MaxCorrectionClamp = MaxCorrectionClamp,
            Chains = Chains.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.DeepCopy()),
        };
}

[Serializable]
public sealed class AdvancedBodyScalingMotionWarpingChainOverrides
{
    public bool? Enabled { get; set; }
    public float? Strength { get; set; }

    public bool IsEmpty => !Enabled.HasValue && !Strength.HasValue;

    public void ApplyTo(AdvancedBodyScalingMotionWarpingChainSettings settings)
    {
        if (Enabled.HasValue)
            settings.Enabled = Enabled.Value;

        if (Strength.HasValue)
            settings.Strength = Strength.Value;
    }

    public AdvancedBodyScalingMotionWarpingChainOverrides DeepCopy()
        => new()
        {
            Enabled = Enabled,
            Strength = Strength,
        };
}

internal sealed class AdvancedBodyScalingMotionWarpingContext
{
    public bool HasObservation { get; set; }
    public bool HasLocomotion { get; set; }
    public float PlanarSpeed { get; set; }
    public float LocomotionAmount { get; set; }
    public float TurnAmount { get; set; }
    public float FacingRadians { get; set; }
    public Vector3 WorldDirection { get; set; }
    public Vector3 LocalDirection { get; set; }
    public string Summary { get; set; } = "Waiting for locomotion context.";

    public void Reset(string summary = "Waiting for locomotion context.")
    {
        HasObservation = false;
        HasLocomotion = false;
        PlanarSpeed = 0f;
        LocomotionAmount = 0f;
        TurnAmount = 0f;
        FacingRadians = 0f;
        WorldDirection = Vector3.Zero;
        LocalDirection = Vector3.Zero;
        Summary = summary;
    }
}

internal sealed class AdvancedBodyScalingMotionWarpingEstimate
{
    public AdvancedBodyScalingFullBodyIkChain Chain { get; init; }
    public string Label { get; init; } = string.Empty;
    public bool IsValid { get; init; } = true;
    public bool IsActive { get; init; }
    public float Strength { get; init; }
    public float BlendAmount { get; init; }
    public float ProportionDelta { get; init; }
    public float StridePressure { get; init; }
    public float OrientationPressure { get; init; }
    public float PosturePressure { get; init; }
    public float EstimatedRiskReduction { get; init; }
    public float EstimatedBeforeRisk { get; init; }
    public float EstimatedAfterRisk { get; init; }
    public string DriverSummary { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string SkipReason { get; init; } = string.Empty;
}

internal sealed class AdvancedBodyScalingMotionWarpingRegionEstimate
{
    public float EstimatedRiskReduction { get; init; }
    public float Strength { get; init; }
    public IReadOnlyList<string> ChainLabels { get; init; } = Array.Empty<string>();
    public string Summary { get; init; } = string.Empty;
}

internal sealed class AdvancedBodyScalingMotionWarpingChainDebugState
{
    public AdvancedBodyScalingFullBodyIkChain Chain { get; init; }
    public string Label { get; init; } = string.Empty;
    public bool IsValid { get; init; }
    public bool IsActive { get; init; }
    public bool LockLimited { get; init; }
    public bool Clamped { get; init; }
    public bool Rejected { get; init; }
    public bool Damped { get; init; }
    public bool SafetyLimited { get; init; }
    public float Strength { get; init; }
    public float BlendAmount { get; init; }
    public float ProportionDelta { get; init; }
    public float StridePressure { get; init; }
    public float OrientationPressure { get; init; }
    public float PosturePressure { get; init; }
    public float MovementAlignment { get; init; }
    public float CorrectionMagnitude { get; init; }
    public float EstimatedBeforeRisk { get; init; }
    public float EstimatedAfterRisk { get; init; }
    public string DriverSummary { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string SkipReason { get; init; } = string.Empty;
    public string SafetySummary { get; init; } = string.Empty;
}

internal sealed class AdvancedBodyScalingMotionWarpingDebugState
{
    public bool Enabled { get; private set; }
    public bool Active { get; private set; }
    public bool ProfileOverridesActive { get; private set; }
    public bool LocomotionObserved { get; private set; }
    public bool SafetyLimited { get; private set; }
    public bool LocksLimited { get; private set; }
    public bool FullBodyIkFollowupActive { get; private set; }
    public float PlanarSpeed { get; private set; }
    public float LocomotionAmount { get; private set; }
    public float MotionSafetyBias { get; private set; }
    public float BlendBias { get; private set; }
    public float EstimatedBeforeRisk { get; private set; }
    public float EstimatedAfterRisk { get; private set; }
    public string Summary { get; private set; } = string.Empty;
    public string ContextSummary { get; private set; } = string.Empty;
    public string FullBodyIkFollowupSummary { get; private set; } = string.Empty;
    public string SettingsSourceLabel { get; private set; } = "Global settings";
    public string ImplementationTierLabel { get; private set; } = AdvancedBodyScalingMotionWarpingTuning.ImplementationTierLabel;
    public List<AdvancedBodyScalingMotionWarpingChainDebugState> Chains { get; } = new();

    public void Reset(
        bool enabled,
        bool profileOverridesActive,
        float motionSafetyBias,
        float blendBias,
        AdvancedBodyScalingMotionWarpingContext? context)
    {
        Enabled = enabled;
        Active = false;
        ProfileOverridesActive = profileOverridesActive;
        LocomotionObserved = context?.HasLocomotion ?? false;
        SafetyLimited = false;
        LocksLimited = false;
        FullBodyIkFollowupActive = false;
        PlanarSpeed = context?.PlanarSpeed ?? 0f;
        LocomotionAmount = context?.LocomotionAmount ?? 0f;
        MotionSafetyBias = motionSafetyBias;
        BlendBias = blendBias;
        EstimatedBeforeRisk = 0f;
        EstimatedAfterRisk = 0f;
        Summary = string.Empty;
        ContextSummary = context?.Summary ?? "Waiting for locomotion context.";
        FullBodyIkFollowupSummary = string.Empty;
        SettingsSourceLabel = profileOverridesActive ? "Profile overrides active" : "Global settings";
        ImplementationTierLabel = AdvancedBodyScalingMotionWarpingTuning.ImplementationTierLabel;
        Chains.Clear();
    }

    public void FinalizeState(
        bool active,
        bool locksLimited,
        bool safetyLimited,
        float beforeRisk,
        float afterRisk,
        string summary)
    {
        Active = active;
        LocksLimited = locksLimited;
        SafetyLimited = safetyLimited;
        EstimatedBeforeRisk = beforeRisk;
        EstimatedAfterRisk = afterRisk;
        Summary = summary;
    }

    public void SetFullBodyIkFollowup(bool active, string summary)
    {
        FullBodyIkFollowupActive = active;
        FullBodyIkFollowupSummary = summary;
    }
}
