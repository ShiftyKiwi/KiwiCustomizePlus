// Copyright (c) Customize+.
// Licensed under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;

namespace CustomizePlus.Core.Data;

internal static class AdvancedBodyScalingFullIkRetargetingTuning
{
    public const float RecommendedGlobalStrengthMax = 0.38f;
    public const float RecommendedPelvisStrengthMax = 0.34f;
    public const float RecommendedSpineStrengthMax = 0.38f;
    public const float RecommendedArmStrengthMax = 0.40f;
    public const float RecommendedLegStrengthMax = 0.38f;
    public const float RecommendedHeadStrengthMax = 0.28f;
    public const float RecommendedReachAdaptationMax = 0.58f;
    public const float RecommendedStrideAdaptationMax = 0.52f;
    public const float RecommendedPosturePreservationMax = 0.48f;
    public const float RecommendedMotionSafetyBiasMin = 0.72f;
    public const float RecommendedBlendBiasMax = 0.72f;
    public const float RecommendedMaxCorrectionClampMax = 0.22f;

    public const float UiMaxGlobalStrength = 0.60f;
    public const float UiMaxPelvisStrength = 0.60f;
    public const float UiMaxSpineStrength = 0.65f;
    public const float UiMaxArmStrength = 0.65f;
    public const float UiMaxLegStrength = 0.60f;
    public const float UiMaxHeadStrength = 0.45f;
    public const float UiMaxReachAdaptation = 0.85f;
    public const float UiMaxStrideAdaptation = 0.80f;
    public const float UiMaxPosturePreservation = 0.75f;
    public const float UiMaxBlendBias = 0.90f;
    public const float UiMaxCorrectionClamp = 0.40f;

    public static float GetRecommendedChainStrengthMax(AdvancedBodyScalingFullBodyIkChain chain)
        => chain switch
        {
            AdvancedBodyScalingFullBodyIkChain.PelvisRoot => 0.54f,
            AdvancedBodyScalingFullBodyIkChain.Spine => 0.58f,
            AdvancedBodyScalingFullBodyIkChain.NeckHead => 0.42f,
            AdvancedBodyScalingFullBodyIkChain.LeftArm => 0.56f,
            AdvancedBodyScalingFullBodyIkChain.RightArm => 0.56f,
            AdvancedBodyScalingFullBodyIkChain.LeftLeg => 0.52f,
            AdvancedBodyScalingFullBodyIkChain.RightLeg => 0.52f,
            _ => 0.56f,
        };

    public static float GetUiMaxChainStrength(AdvancedBodyScalingFullBodyIkChain chain)
        => chain switch
        {
            AdvancedBodyScalingFullBodyIkChain.PelvisRoot => 0.80f,
            AdvancedBodyScalingFullBodyIkChain.Spine => 0.85f,
            AdvancedBodyScalingFullBodyIkChain.NeckHead => 0.65f,
            AdvancedBodyScalingFullBodyIkChain.LeftArm => 0.80f,
            AdvancedBodyScalingFullBodyIkChain.RightArm => 0.80f,
            AdvancedBodyScalingFullBodyIkChain.LeftLeg => 0.75f,
            AdvancedBodyScalingFullBodyIkChain.RightLeg => 0.75f,
            _ => 0.80f,
        };
}

[Serializable]
public sealed class AdvancedBodyScalingFullIkRetargetingChainSettings
{
    private float _strength = 1f;

    public bool Enabled { get; set; } = true;

    public float Strength
    {
        get => _strength;
        set => _strength = Math.Clamp(value, 0f, 1f);
    }

    public AdvancedBodyScalingFullIkRetargetingChainSettings DeepCopy()
        => new()
        {
            Enabled = Enabled,
            Strength = Strength,
        };

    public static AdvancedBodyScalingFullIkRetargetingChainSettings CreateDefault(AdvancedBodyScalingFullBodyIkChain chain)
        => chain switch
        {
            AdvancedBodyScalingFullBodyIkChain.PelvisRoot => new AdvancedBodyScalingFullIkRetargetingChainSettings
            {
                Strength = 0.50f,
            },
            AdvancedBodyScalingFullBodyIkChain.Spine => new AdvancedBodyScalingFullIkRetargetingChainSettings
            {
                Strength = 0.52f,
            },
            AdvancedBodyScalingFullBodyIkChain.NeckHead => new AdvancedBodyScalingFullIkRetargetingChainSettings
            {
                Strength = 0.40f,
            },
            AdvancedBodyScalingFullBodyIkChain.LeftArm => new AdvancedBodyScalingFullIkRetargetingChainSettings
            {
                Strength = 0.48f,
            },
            AdvancedBodyScalingFullBodyIkChain.RightArm => new AdvancedBodyScalingFullIkRetargetingChainSettings
            {
                Strength = 0.48f,
            },
            AdvancedBodyScalingFullBodyIkChain.LeftLeg => new AdvancedBodyScalingFullIkRetargetingChainSettings
            {
                Strength = 0.46f,
            },
            AdvancedBodyScalingFullBodyIkChain.RightLeg => new AdvancedBodyScalingFullIkRetargetingChainSettings
            {
                Strength = 0.46f,
            },
            _ => new AdvancedBodyScalingFullIkRetargetingChainSettings(),
        };

    public static Dictionary<AdvancedBodyScalingFullBodyIkChain, AdvancedBodyScalingFullIkRetargetingChainSettings> CreateDefaults()
        => Enum
            .GetValues<AdvancedBodyScalingFullBodyIkChain>()
            .ToDictionary(chain => chain, CreateDefault);
}

[Serializable]
public sealed class AdvancedBodyScalingFullIkRetargetingSettings
{
    private float _globalStrength = 0.22f;
    private float _pelvisStrength = 0.22f;
    private float _spineStrength = 0.25f;
    private float _armStrength = 0.24f;
    private float _legStrength = 0.26f;
    private float _headStrength = 0.18f;
    private float _reachAdaptationStrength = 0.42f;
    private float _strideAdaptationStrength = 0.38f;
    private float _posturePreservationStrength = 0.34f;
    private float _motionSafetyBias = 0.84f;
    private float _blendBias = 0.55f;
    private float _maxCorrectionClamp = 0.16f;

    public bool Enabled { get; set; } = false;

    public float GlobalStrength
    {
        get => _globalStrength;
        set => _globalStrength = Math.Clamp(value, 0f, 1f);
    }

    public float PelvisStrength
    {
        get => _pelvisStrength;
        set => _pelvisStrength = Math.Clamp(value, 0f, 1f);
    }

    public float SpineStrength
    {
        get => _spineStrength;
        set => _spineStrength = Math.Clamp(value, 0f, 1f);
    }

    public float ArmStrength
    {
        get => _armStrength;
        set => _armStrength = Math.Clamp(value, 0f, 1f);
    }

    public float LegStrength
    {
        get => _legStrength;
        set => _legStrength = Math.Clamp(value, 0f, 1f);
    }

    public float HeadStrength
    {
        get => _headStrength;
        set => _headStrength = Math.Clamp(value, 0f, 1f);
    }

    public float ReachAdaptationStrength
    {
        get => _reachAdaptationStrength;
        set => _reachAdaptationStrength = Math.Clamp(value, 0f, 1f);
    }

    public float StrideAdaptationStrength
    {
        get => _strideAdaptationStrength;
        set => _strideAdaptationStrength = Math.Clamp(value, 0f, 1f);
    }

    public float PosturePreservationStrength
    {
        get => _posturePreservationStrength;
        set => _posturePreservationStrength = Math.Clamp(value, 0f, 1f);
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

    public Dictionary<AdvancedBodyScalingFullBodyIkChain, AdvancedBodyScalingFullIkRetargetingChainSettings> Chains { get; set; }
        = AdvancedBodyScalingFullIkRetargetingChainSettings.CreateDefaults();

    public AdvancedBodyScalingFullIkRetargetingChainSettings GetChainSettings(AdvancedBodyScalingFullBodyIkChain chain)
    {
        if (!Chains.TryGetValue(chain, out var settings))
        {
            settings = AdvancedBodyScalingFullIkRetargetingChainSettings.CreateDefault(chain);
            Chains[chain] = settings;
        }

        return settings;
    }

    public void ResetToDefaults()
    {
        var defaults = new AdvancedBodyScalingFullIkRetargetingSettings();
        Enabled = defaults.Enabled;
        GlobalStrength = defaults.GlobalStrength;
        PelvisStrength = defaults.PelvisStrength;
        SpineStrength = defaults.SpineStrength;
        ArmStrength = defaults.ArmStrength;
        LegStrength = defaults.LegStrength;
        HeadStrength = defaults.HeadStrength;
        ReachAdaptationStrength = defaults.ReachAdaptationStrength;
        StrideAdaptationStrength = defaults.StrideAdaptationStrength;
        PosturePreservationStrength = defaults.PosturePreservationStrength;
        MotionSafetyBias = defaults.MotionSafetyBias;
        BlendBias = defaults.BlendBias;
        MaxCorrectionClamp = defaults.MaxCorrectionClamp;
        Chains = AdvancedBodyScalingFullIkRetargetingChainSettings.CreateDefaults();
    }

    public AdvancedBodyScalingFullIkRetargetingSettings DeepCopy()
        => new()
        {
            Enabled = Enabled,
            GlobalStrength = GlobalStrength,
            PelvisStrength = PelvisStrength,
            SpineStrength = SpineStrength,
            ArmStrength = ArmStrength,
            LegStrength = LegStrength,
            HeadStrength = HeadStrength,
            ReachAdaptationStrength = ReachAdaptationStrength,
            StrideAdaptationStrength = StrideAdaptationStrength,
            PosturePreservationStrength = PosturePreservationStrength,
            MotionSafetyBias = MotionSafetyBias,
            BlendBias = BlendBias,
            MaxCorrectionClamp = MaxCorrectionClamp,
            Chains = Chains.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.DeepCopy()),
        };
}

[Serializable]
public sealed class AdvancedBodyScalingFullIkRetargetingChainOverrides
{
    public bool? Enabled { get; set; }
    public float? Strength { get; set; }

    public bool IsEmpty => !Enabled.HasValue && !Strength.HasValue;

    public void ApplyTo(AdvancedBodyScalingFullIkRetargetingChainSettings settings)
    {
        if (Enabled.HasValue)
            settings.Enabled = Enabled.Value;

        if (Strength.HasValue)
            settings.Strength = Strength.Value;
    }

    public AdvancedBodyScalingFullIkRetargetingChainOverrides DeepCopy()
        => new()
        {
            Enabled = Enabled,
            Strength = Strength,
        };
}

internal sealed class AdvancedBodyScalingFullIkRetargetingEstimate
{
    public AdvancedBodyScalingFullBodyIkChain Chain { get; init; }
    public string Label { get; init; } = string.Empty;
    public bool IsValid { get; init; } = true;
    public bool IsActive { get; init; }
    public float Strength { get; init; }
    public float BlendAmount { get; init; }
    public float ProportionDelta { get; init; }
    public float ReachDelta { get; init; }
    public float StrideDelta { get; init; }
    public float PostureDelta { get; init; }
    public float EstimatedRiskReduction { get; init; }
    public float EstimatedBeforeRisk { get; init; }
    public float EstimatedAfterRisk { get; init; }
    public string DriverSummary { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string SkipReason { get; init; } = string.Empty;
}

internal sealed class AdvancedBodyScalingFullIkRetargetingRegionEstimate
{
    public float EstimatedRiskReduction { get; init; }
    public float Strength { get; init; }
    public IReadOnlyList<string> ChainLabels { get; init; } = Array.Empty<string>();
    public string Summary { get; init; } = string.Empty;
}

internal sealed class AdvancedBodyScalingFullIkRetargetingChainDebugState
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
    public float ReachDelta { get; init; }
    public float StrideDelta { get; init; }
    public float PostureDelta { get; init; }
    public float CorrectionMagnitude { get; init; }
    public float EstimatedBeforeRisk { get; init; }
    public float EstimatedAfterRisk { get; init; }
    public string DriverSummary { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string SkipReason { get; init; } = string.Empty;
    public string SafetySummary { get; init; } = string.Empty;
}

internal sealed class AdvancedBodyScalingFullIkRetargetingDebugState
{
    public bool Enabled { get; private set; }
    public bool Active { get; private set; }
    public bool ProfileOverridesActive { get; private set; }
    public bool LocksLimited { get; private set; }
    public bool SafetyLimited { get; private set; }
    public bool FullBodyIkFollowupActive { get; private set; }
    public float MotionSafetyBias { get; private set; }
    public float BlendBias { get; private set; }
    public float EstimatedBeforeRisk { get; private set; }
    public float EstimatedAfterRisk { get; private set; }
    public string Summary { get; private set; } = string.Empty;
    public string FullBodyIkFollowupSummary { get; private set; } = string.Empty;
    public string SettingsSourceLabel { get; private set; } = "Global settings";
    public List<AdvancedBodyScalingFullIkRetargetingChainDebugState> Chains { get; } = new();

    public void Reset(bool enabled, bool profileOverridesActive, float motionSafetyBias, float blendBias)
    {
        Enabled = enabled;
        Active = false;
        ProfileOverridesActive = profileOverridesActive;
        LocksLimited = false;
        SafetyLimited = false;
        FullBodyIkFollowupActive = false;
        MotionSafetyBias = motionSafetyBias;
        BlendBias = blendBias;
        EstimatedBeforeRisk = 0f;
        EstimatedAfterRisk = 0f;
        Summary = string.Empty;
        FullBodyIkFollowupSummary = string.Empty;
        SettingsSourceLabel = profileOverridesActive ? "Profile overrides active" : "Global settings";
        Chains.Clear();
    }

    public void FinalizeState(bool active, bool locksLimited, bool safetyLimited, float beforeRisk, float afterRisk, string summary)
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
