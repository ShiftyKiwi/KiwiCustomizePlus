// Copyright (c) Customize+.
// Licensed under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;

namespace CustomizePlus.Core.Data;

[Serializable]
public enum AdvancedBodyScalingFullBodyIkChain
{
    PelvisRoot = 0,
    Spine = 1,
    NeckHead = 2,
    LeftArm = 3,
    RightArm = 4,
    LeftLeg = 5,
    RightLeg = 6,
}

internal static class AdvancedBodyScalingFullBodyIkTuning
{
    public const float RecommendedGlobalStrengthMax = 0.40f;
    public const float RecommendedPelvisStrengthMax = 0.42f;
    public const float RecommendedSpineStrengthMax = 0.36f;
    public const float RecommendedArmStrengthMax = 0.50f;
    public const float RecommendedLegStrengthMax = 0.42f;
    public const float RecommendedHeadStrengthMax = 0.32f;
    public const float RecommendedGroundingBiasMax = 0.65f;
    public const float RecommendedMotionSafetyBiasMin = 0.72f;
    public const float RecommendedMaxCorrectionClampMax = 0.28f;

    public const float UiMaxGlobalStrength = 0.60f;
    public const float UiMaxPelvisStrength = 0.60f;
    public const float UiMaxSpineStrength = 0.55f;
    public const float UiMaxArmStrength = 0.70f;
    public const float UiMaxLegStrength = 0.60f;
    public const float UiMaxHeadStrength = 0.50f;
    public const float UiMaxGroundingBias = 0.80f;
    public const float UiMaxCorrectionClamp = 0.45f;

    public static float GetRecommendedChainStrengthMax(AdvancedBodyScalingFullBodyIkChain chain)
        => chain switch
        {
            AdvancedBodyScalingFullBodyIkChain.PelvisRoot => 0.60f,
            AdvancedBodyScalingFullBodyIkChain.Spine => 0.52f,
            AdvancedBodyScalingFullBodyIkChain.NeckHead => 0.42f,
            AdvancedBodyScalingFullBodyIkChain.LeftArm => 0.60f,
            AdvancedBodyScalingFullBodyIkChain.RightArm => 0.60f,
            AdvancedBodyScalingFullBodyIkChain.LeftLeg => 0.52f,
            AdvancedBodyScalingFullBodyIkChain.RightLeg => 0.52f,
            _ => 0.60f,
        };

    public static float GetUiMaxChainStrength(AdvancedBodyScalingFullBodyIkChain chain)
        => chain switch
        {
            AdvancedBodyScalingFullBodyIkChain.PelvisRoot => 0.80f,
            AdvancedBodyScalingFullBodyIkChain.Spine => 0.75f,
            AdvancedBodyScalingFullBodyIkChain.NeckHead => 0.65f,
            AdvancedBodyScalingFullBodyIkChain.LeftArm => 0.85f,
            AdvancedBodyScalingFullBodyIkChain.RightArm => 0.85f,
            AdvancedBodyScalingFullBodyIkChain.LeftLeg => 0.72f,
            AdvancedBodyScalingFullBodyIkChain.RightLeg => 0.72f,
            _ => 0.80f,
        };
}

[Serializable]
public sealed class AdvancedBodyScalingFullBodyIkChainSettings
{
    private float _strength = 1f;

    public bool Enabled { get; set; } = true;

    public float Strength
    {
        get => _strength;
        set => _strength = Math.Clamp(value, 0f, 1f);
    }

    public AdvancedBodyScalingFullBodyIkChainSettings DeepCopy()
        => new()
        {
            Enabled = Enabled,
            Strength = Strength,
        };

    public static AdvancedBodyScalingFullBodyIkChainSettings CreateDefault(AdvancedBodyScalingFullBodyIkChain chain)
        => chain switch
        {
            AdvancedBodyScalingFullBodyIkChain.PelvisRoot => new AdvancedBodyScalingFullBodyIkChainSettings
            {
                Strength = 0.54f,
            },
            AdvancedBodyScalingFullBodyIkChain.Spine => new AdvancedBodyScalingFullBodyIkChainSettings
            {
                Strength = 0.44f,
            },
            AdvancedBodyScalingFullBodyIkChain.NeckHead => new AdvancedBodyScalingFullBodyIkChainSettings
            {
                Strength = 0.30f,
            },
            AdvancedBodyScalingFullBodyIkChain.LeftArm => new AdvancedBodyScalingFullBodyIkChainSettings
            {
                Strength = 0.40f,
            },
            AdvancedBodyScalingFullBodyIkChain.RightArm => new AdvancedBodyScalingFullBodyIkChainSettings
            {
                Strength = 0.40f,
            },
            AdvancedBodyScalingFullBodyIkChain.LeftLeg => new AdvancedBodyScalingFullBodyIkChainSettings
            {
                Strength = 0.46f,
            },
            AdvancedBodyScalingFullBodyIkChain.RightLeg => new AdvancedBodyScalingFullBodyIkChainSettings
            {
                Strength = 0.46f,
            },
            _ => new AdvancedBodyScalingFullBodyIkChainSettings(),
        };

    public static Dictionary<AdvancedBodyScalingFullBodyIkChain, AdvancedBodyScalingFullBodyIkChainSettings> CreateDefaults()
        => Enum
            .GetValues<AdvancedBodyScalingFullBodyIkChain>()
            .ToDictionary(chain => chain, CreateDefault);
}

[Serializable]
public sealed class AdvancedBodyScalingFullBodyIkSettings
{
    private float _globalStrength = 0.26f;
    private int _iterationCount = 4;
    private float _convergenceTolerance = 0.018f;
    private float _pelvisCompensationStrength = 0.30f;
    private float _spineRedistributionStrength = 0.28f;
    private float _legStrength = 0.34f;
    private float _armStrength = 0.28f;
    private float _headAlignmentStrength = 0.20f;
    private float _groundingBias = 0.52f;
    private float _motionSafetyBias = 0.82f;
    private float _maxCorrectionClamp = 0.22f;

    public bool Enabled { get; set; } = false;

    public float GlobalStrength
    {
        get => _globalStrength;
        set => _globalStrength = Math.Clamp(value, 0f, 1f);
    }

    public int IterationCount
    {
        get => _iterationCount;
        set => _iterationCount = Math.Clamp(value, 1, 12);
    }

    public float ConvergenceTolerance
    {
        get => _convergenceTolerance;
        set => _convergenceTolerance = Math.Clamp(value, 0.001f, 0.100f);
    }

    public float PelvisCompensationStrength
    {
        get => _pelvisCompensationStrength;
        set => _pelvisCompensationStrength = Math.Clamp(value, 0f, 1f);
    }

    public float SpineRedistributionStrength
    {
        get => _spineRedistributionStrength;
        set => _spineRedistributionStrength = Math.Clamp(value, 0f, 1f);
    }

    public float LegStrength
    {
        get => _legStrength;
        set => _legStrength = Math.Clamp(value, 0f, 1f);
    }

    public float ArmStrength
    {
        get => _armStrength;
        set => _armStrength = Math.Clamp(value, 0f, 1f);
    }

    public float HeadAlignmentStrength
    {
        get => _headAlignmentStrength;
        set => _headAlignmentStrength = Math.Clamp(value, 0f, 1f);
    }

    public float GroundingBias
    {
        get => _groundingBias;
        set => _groundingBias = Math.Clamp(value, 0f, 1f);
    }

    public float MotionSafetyBias
    {
        get => _motionSafetyBias;
        set => _motionSafetyBias = Math.Clamp(value, 0f, 1f);
    }

    public float MaxCorrectionClamp
    {
        get => _maxCorrectionClamp;
        set => _maxCorrectionClamp = Math.Clamp(value, 0f, 1f);
    }

    public Dictionary<AdvancedBodyScalingFullBodyIkChain, AdvancedBodyScalingFullBodyIkChainSettings> Chains { get; set; }
        = AdvancedBodyScalingFullBodyIkChainSettings.CreateDefaults();

    public AdvancedBodyScalingFullBodyIkChainSettings GetChainSettings(AdvancedBodyScalingFullBodyIkChain chain)
    {
        if (!Chains.TryGetValue(chain, out var settings))
        {
            settings = AdvancedBodyScalingFullBodyIkChainSettings.CreateDefault(chain);
            Chains[chain] = settings;
        }

        return settings;
    }

    public void ResetToDefaults()
    {
        var defaults = new AdvancedBodyScalingFullBodyIkSettings();
        Enabled = defaults.Enabled;
        GlobalStrength = defaults.GlobalStrength;
        IterationCount = defaults.IterationCount;
        ConvergenceTolerance = defaults.ConvergenceTolerance;
        PelvisCompensationStrength = defaults.PelvisCompensationStrength;
        SpineRedistributionStrength = defaults.SpineRedistributionStrength;
        LegStrength = defaults.LegStrength;
        ArmStrength = defaults.ArmStrength;
        HeadAlignmentStrength = defaults.HeadAlignmentStrength;
        GroundingBias = defaults.GroundingBias;
        MotionSafetyBias = defaults.MotionSafetyBias;
        MaxCorrectionClamp = defaults.MaxCorrectionClamp;
        Chains = AdvancedBodyScalingFullBodyIkChainSettings.CreateDefaults();
    }

    public AdvancedBodyScalingFullBodyIkSettings DeepCopy()
        => new()
        {
            Enabled = Enabled,
            GlobalStrength = GlobalStrength,
            IterationCount = IterationCount,
            ConvergenceTolerance = ConvergenceTolerance,
            PelvisCompensationStrength = PelvisCompensationStrength,
            SpineRedistributionStrength = SpineRedistributionStrength,
            LegStrength = LegStrength,
            ArmStrength = ArmStrength,
            HeadAlignmentStrength = HeadAlignmentStrength,
            GroundingBias = GroundingBias,
            MotionSafetyBias = MotionSafetyBias,
            MaxCorrectionClamp = MaxCorrectionClamp,
            Chains = Chains.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.DeepCopy())
        };
}

[Serializable]
public sealed class AdvancedBodyScalingFullBodyIkChainOverrides
{
    public bool? Enabled { get; set; }
    public float? Strength { get; set; }

    public bool IsEmpty => !Enabled.HasValue && !Strength.HasValue;

    public void ApplyTo(AdvancedBodyScalingFullBodyIkChainSettings settings)
    {
        if (Enabled.HasValue)
            settings.Enabled = Enabled.Value;

        if (Strength.HasValue)
            settings.Strength = Strength.Value;
    }

    public AdvancedBodyScalingFullBodyIkChainOverrides DeepCopy()
        => new()
        {
            Enabled = Enabled,
            Strength = Strength,
        };
}

internal sealed class AdvancedBodyScalingFullBodyIkEstimate
{
    public AdvancedBodyScalingFullBodyIkChain Chain { get; init; }
    public string Label { get; init; } = string.Empty;
    public bool IsValid { get; init; } = true;
    public bool IsSolved { get; init; }
    public bool LockLimited { get; init; }
    public float Activation { get; init; }
    public float Strength { get; init; }
    public float EstimatedRiskReduction { get; init; }
    public float EstimatedBeforeRisk { get; init; }
    public float EstimatedAfterRisk { get; init; }
    public string DriverSummary { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string SkipReason { get; init; } = string.Empty;
}

internal sealed class AdvancedBodyScalingFullBodyIkRegionEstimate
{
    public float EstimatedRiskReduction { get; init; }
    public float Strength { get; init; }
    public IReadOnlyList<string> ChainLabels { get; init; } = Array.Empty<string>();
    public string Summary { get; init; } = string.Empty;
}

internal sealed class AdvancedBodyScalingFullBodyIkChainDebugState
{
    public AdvancedBodyScalingFullBodyIkChain Chain { get; init; }
    public string Label { get; init; } = string.Empty;
    public bool IsValid { get; init; }
    public bool IsSolved { get; init; }
    public bool LockLimited { get; init; }
    public bool Clamped { get; init; }
    public bool Rejected { get; init; }
    public bool Damped { get; init; }
    public bool SafetyLimited { get; init; }
    public float Activation { get; init; }
    public float Strength { get; init; }
    public float CorrectionMagnitude { get; init; }
    public float ResidualError { get; init; }
    public float EstimatedBeforeRisk { get; init; }
    public float EstimatedAfterRisk { get; init; }
    public string DriverSummary { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string SkipReason { get; init; } = string.Empty;
    public string SafetySummary { get; init; } = string.Empty;
}

internal sealed class AdvancedBodyScalingFullBodyIkDebugState
{
    public bool Enabled { get; private set; }
    public bool Active { get; private set; }
    public bool ProfileOverridesActive { get; private set; }
    public bool LocksLimited { get; private set; }
    public bool SafetyLimited { get; private set; }
    public bool Converged { get; private set; }
    public int IterationCountUsed { get; private set; }
    public float ConvergenceTolerance { get; private set; }
    public float EstimatedBeforeRisk { get; private set; }
    public float EstimatedAfterRisk { get; private set; }
    public float MaxResidualError { get; private set; }
    public string Summary { get; private set; } = string.Empty;
    public string SettingsSourceLabel { get; private set; } = "Global settings";
    public List<AdvancedBodyScalingFullBodyIkChainDebugState> Chains { get; } = new();

    public void Reset(bool enabled, bool profileOverridesActive, int iterationCount, float convergenceTolerance)
    {
        Enabled = enabled;
        Active = false;
        ProfileOverridesActive = profileOverridesActive;
        LocksLimited = false;
        SafetyLimited = false;
        Converged = false;
        IterationCountUsed = iterationCount;
        ConvergenceTolerance = convergenceTolerance;
        EstimatedBeforeRisk = 0f;
        EstimatedAfterRisk = 0f;
        MaxResidualError = 0f;
        Summary = string.Empty;
        SettingsSourceLabel = profileOverridesActive ? "Profile overrides active" : "Global settings";
        Chains.Clear();
    }

    public void FinalizeState(bool active, bool converged, bool locksLimited, bool safetyLimited, float beforeRisk, float afterRisk, float maxResidualError, string summary)
    {
        Active = active;
        Converged = converged;
        LocksLimited = locksLimited;
        SafetyLimited = safetyLimited;
        EstimatedBeforeRisk = beforeRisk;
        EstimatedAfterRisk = afterRisk;
        MaxResidualError = maxResidualError;
        Summary = summary;
    }
}
