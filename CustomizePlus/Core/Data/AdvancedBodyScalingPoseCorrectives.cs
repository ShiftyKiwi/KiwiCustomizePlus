// Copyright (c) Customize+.
// Licensed under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;

namespace CustomizePlus.Core.Data;

[Serializable]
public enum AdvancedBodyScalingCorrectiveRegion
{
    NeckShoulder = 0,
    ClavicleUpperChest = 1,
    ShoulderUpperArm = 2,
    ElbowForearm = 3,
    WaistHips = 4,
    HipUpperThigh = 5,
    ThighKneeCalf = 6,
}

public enum AdvancedBodyScalingCorrectivePath
{
    SupportedMorph = 0,
    TransformFallback = 1,
}

internal static class AdvancedBodyScalingPoseCorrectiveTuning
{
    public const float RecommendedGlobalStrengthMax = 0.72f;
    public const float RecommendedPoseMapSharpnessMax = 0.80f;
    public const float RecommendedDampingMin = 0.52f;
    public const float RecommendedMaxCorrectionClampMax = 0.045f;
    public const float RecommendedRegionStrengthMax = 0.82f;
    public const float RecommendedRegionFalloffMin = 0.35f;

    public const float UiPoseMapSharpnessMin = 0.10f;
    public const float UiPoseMapSharpnessMax = 1.20f;
    public const float UiMaxCorrectionClampMax = 0.075f;
}

[Serializable]
public sealed class AdvancedBodyScalingCorrectiveRegionSettings
{
    private float _strength = 1f;
    private float _activationThreshold = 0.15f;
    private float _activationDeadzone = 0.05f;
    private float _smoothing = 0.65f;
    private float _falloff = 0.85f;
    private float _maxCorrection = 0.03f;
    private float _priority = 1f;

    public bool Enabled { get; set; } = true;

    public float Strength
    {
        get => _strength;
        set => _strength = Math.Clamp(value, 0f, 1f);
    }

    public float ActivationThreshold
    {
        get => _activationThreshold;
        set => _activationThreshold = Math.Clamp(value, 0f, 1f);
    }

    public float ActivationDeadzone
    {
        get => _activationDeadzone;
        set => _activationDeadzone = Math.Clamp(value, 0f, 0.5f);
    }

    public float Smoothing
    {
        get => _smoothing;
        set => _smoothing = Math.Clamp(value, 0f, 1f);
    }

    public float Falloff
    {
        get => _falloff;
        set => _falloff = Math.Clamp(value, 0f, 1f);
    }

    public float MaxCorrection
    {
        get => _maxCorrection;
        set => _maxCorrection = Math.Clamp(value, 0f, 0.10f);
    }

    public float Priority
    {
        get => _priority;
        set => _priority = Math.Clamp(value, 0.1f, 1.5f);
    }

    public AdvancedBodyScalingCorrectiveRegionSettings DeepCopy()
        => new()
        {
            Enabled = Enabled,
            Strength = Strength,
            ActivationThreshold = ActivationThreshold,
            ActivationDeadzone = ActivationDeadzone,
            Smoothing = Smoothing,
            Falloff = Falloff,
            MaxCorrection = MaxCorrection,
            Priority = Priority,
        };

    public static AdvancedBodyScalingCorrectiveRegionSettings CreateDefault(AdvancedBodyScalingCorrectiveRegion region)
        => region switch
        {
            AdvancedBodyScalingCorrectiveRegion.NeckShoulder => new AdvancedBodyScalingCorrectiveRegionSettings
            {
                Strength = 0.76f,
                ActivationThreshold = 0.16f,
                ActivationDeadzone = 0.06f,
                Smoothing = 0.80f,
                Falloff = 0.94f,
                MaxCorrection = 0.040f,
                Priority = 1.04f,
            },
            AdvancedBodyScalingCorrectiveRegion.ClavicleUpperChest => new AdvancedBodyScalingCorrectiveRegionSettings
            {
                Strength = 0.70f,
                ActivationThreshold = 0.15f,
                ActivationDeadzone = 0.05f,
                Smoothing = 0.76f,
                Falloff = 0.91f,
                MaxCorrection = 0.034f,
                Priority = 1.02f,
            },
            AdvancedBodyScalingCorrectiveRegion.ShoulderUpperArm => new AdvancedBodyScalingCorrectiveRegionSettings
            {
                Strength = 0.60f,
                ActivationThreshold = 0.15f,
                ActivationDeadzone = 0.05f,
                Smoothing = 0.70f,
                Falloff = 0.84f,
                MaxCorrection = 0.028f,
                Priority = 0.92f,
            },
            AdvancedBodyScalingCorrectiveRegion.ElbowForearm => new AdvancedBodyScalingCorrectiveRegionSettings
            {
                Strength = 0.52f,
                ActivationThreshold = 0.16f,
                ActivationDeadzone = 0.05f,
                Smoothing = 0.66f,
                Falloff = 0.80f,
                MaxCorrection = 0.024f,
                Priority = 0.86f,
            },
            AdvancedBodyScalingCorrectiveRegion.WaistHips => new AdvancedBodyScalingCorrectiveRegionSettings
            {
                Strength = 0.56f,
                ActivationThreshold = 0.15f,
                ActivationDeadzone = 0.05f,
                Smoothing = 0.70f,
                Falloff = 0.86f,
                MaxCorrection = 0.028f,
                Priority = 0.95f,
            },
            AdvancedBodyScalingCorrectiveRegion.HipUpperThigh => new AdvancedBodyScalingCorrectiveRegionSettings
            {
                Strength = 0.62f,
                ActivationThreshold = 0.15f,
                ActivationDeadzone = 0.05f,
                Smoothing = 0.70f,
                Falloff = 0.86f,
                MaxCorrection = 0.030f,
                Priority = 1.00f,
            },
            AdvancedBodyScalingCorrectiveRegion.ThighKneeCalf => new AdvancedBodyScalingCorrectiveRegionSettings
            {
                Strength = 0.56f,
                ActivationThreshold = 0.15f,
                ActivationDeadzone = 0.05f,
                Smoothing = 0.68f,
                Falloff = 0.82f,
                MaxCorrection = 0.026f,
                Priority = 0.90f,
            },
            _ => new AdvancedBodyScalingCorrectiveRegionSettings(),
        };

    public static Dictionary<AdvancedBodyScalingCorrectiveRegion, AdvancedBodyScalingCorrectiveRegionSettings> CreateDefaults()
        => AdvancedBodyScalingPoseCorrectiveSystem
            .GetOrderedRegions()
            .ToDictionary(region => region, CreateDefault);
}

[Serializable]
public sealed class AdvancedBodyScalingPoseCorrectiveSettings
{
    private float _strength = 0.58f;
    private float _poseMapSharpness = 0.58f;
    private float _damping = 0.72f;
    private float _maxCorrectionClamp = 0.036f;

    public bool Enabled { get; set; }

    public float Strength
    {
        get => _strength;
        set => _strength = Math.Clamp(value, 0f, 1f);
    }

    public float PoseMapSharpness
    {
        get => _poseMapSharpness;
        set => _poseMapSharpness = Math.Clamp(value, AdvancedBodyScalingPoseCorrectiveTuning.UiPoseMapSharpnessMin, AdvancedBodyScalingPoseCorrectiveTuning.UiPoseMapSharpnessMax);
    }

    public float Damping
    {
        get => _damping;
        set => _damping = Math.Clamp(value, 0f, 1f);
    }

    public float MaxCorrectionClamp
    {
        get => _maxCorrectionClamp;
        set => _maxCorrectionClamp = Math.Clamp(value, 0f, 0.10f);
    }

    public Dictionary<AdvancedBodyScalingCorrectiveRegion, AdvancedBodyScalingCorrectiveRegionSettings> Regions { get; set; }
        = AdvancedBodyScalingCorrectiveRegionSettings.CreateDefaults();

    public AdvancedBodyScalingCorrectiveRegionSettings GetRegionSettings(AdvancedBodyScalingCorrectiveRegion region)
    {
        if (!Regions.TryGetValue(region, out var settings))
        {
            settings = AdvancedBodyScalingCorrectiveRegionSettings.CreateDefault(region);
            Regions[region] = settings;
        }

        return settings;
    }

    public void ResetToDefaults()
    {
        var defaults = new AdvancedBodyScalingPoseCorrectiveSettings();
        Enabled = defaults.Enabled;
        Strength = defaults.Strength;
        PoseMapSharpness = defaults.PoseMapSharpness;
        Damping = defaults.Damping;
        MaxCorrectionClamp = defaults.MaxCorrectionClamp;
        Regions = AdvancedBodyScalingCorrectiveRegionSettings.CreateDefaults();
    }

    public AdvancedBodyScalingPoseCorrectiveSettings DeepCopy()
        => new()
        {
            Enabled = Enabled,
            Strength = Strength,
            PoseMapSharpness = PoseMapSharpness,
            Damping = Damping,
            MaxCorrectionClamp = MaxCorrectionClamp,
            Regions = Regions.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.DeepCopy()),
        };
}

internal sealed class AdvancedBodyScalingCorrectiveSampleDebugState
{
    public string Name { get; init; } = string.Empty;
    public float Weight { get; init; }
    public float Distance { get; init; }
    public string Summary { get; init; } = string.Empty;
}

internal sealed class AdvancedBodyScalingCorrectiveRuntimeState
{
    public float Activation { get; set; }
    public float RawActivation { get; set; }
    public bool IsActive { get; set; }
    public bool BroadInterpolation { get; set; }
    public string DominantSampleName { get; set; } = string.Empty;
    public float DominantSampleWeight { get; set; }
    public float[] SmoothedDriverVector { get; set; } = Array.Empty<float>();
}

internal sealed class AdvancedBodyScalingCorrectiveDebugRegionState
{
    public AdvancedBodyScalingCorrectiveRegion Region { get; init; }
    public string Label { get; init; } = string.Empty;
    public float DriverStrength { get; init; }
    public float Discontinuity { get; init; }
    public float Activation { get; init; }
    public float RawActivation { get; init; }
    public float Strength { get; init; }
    public float EstimatedRiskReduction { get; init; }
    public bool SafetyLimited { get; init; }
    public bool LocksOrPinsLimited { get; init; }
    public bool Clamped { get; init; }
    public bool Damped { get; init; }
    public int SampleCount { get; init; }
    public int InfluenceSampleCount { get; init; }
    public bool ShortlistApplied { get; init; }
    public bool BroadInterpolation { get; init; }
    public string AdaptiveMode { get; init; } = string.Empty;
    public int AdaptiveShortlistMax { get; init; }
    public int AdaptiveShortlistFloor { get; init; }
    public float AdaptiveSharpnessScale { get; init; }
    public float AdaptiveFalloffScale { get; init; }
    public float AdaptiveDampingScale { get; init; }
    public bool AdaptiveMeaningfulChange { get; init; }
    public string AdaptiveSummary { get; init; } = string.Empty;
    public bool PoseHistoryActive { get; init; }
    public bool HysteresisHeld { get; init; }
    public bool DominantSamplePersistenceUsed { get; init; }
    public bool BroadModeMemoryUsed { get; init; }
    public float DriverHistoryRetention { get; init; }
    public float DriverHistoryChangeMagnitude { get; init; }
    public string TransitionSummary { get; init; } = string.Empty;
    public string DriverSummary { get; init; } = string.Empty;
    public string DriverVectorSummary { get; init; } = string.Empty;
    public string SampleSummary { get; init; } = string.Empty;
    public string Summary { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public List<AdvancedBodyScalingCorrectiveSampleDebugState> InfluentialSamples { get; } = new();
}

internal sealed class AdvancedBodyScalingPoseCorrectiveDebugState
{
    public AdvancedBodyScalingCorrectivePath Path { get; private set; } = AdvancedBodyScalingCorrectivePath.TransformFallback;
    public string PathDescription { get; private set; } = string.Empty;
    public string SettingsSourceLabel { get; private set; } = "Global settings";
    public bool ProfileOverridesActive { get; private set; }
    public bool Enabled { get; private set; }
    public bool Active { get; private set; }
    public float GlobalStrength { get; private set; }
    public float PoseMapSharpness { get; private set; }
    public float Damping { get; private set; }
    public float MaxCorrectionClamp { get; private set; }
    public string Summary { get; private set; } = string.Empty;
    public List<string> Advisories { get; } = new();
    public List<AdvancedBodyScalingCorrectiveDebugRegionState> ActiveRegions { get; } = new();

    public void Reset(
        AdvancedBodyScalingCorrectivePath path,
        string pathDescription,
        bool profileOverridesActive,
        bool enabled = false,
        float globalStrength = 0f,
        float poseMapSharpness = 0f,
        float damping = 0f,
        float maxCorrectionClamp = 0f)
    {
        Path = path;
        PathDescription = pathDescription;
        ProfileOverridesActive = profileOverridesActive;
        SettingsSourceLabel = profileOverridesActive ? "Profile overrides active" : "Global settings";
        Enabled = enabled;
        Active = false;
        GlobalStrength = globalStrength;
        PoseMapSharpness = poseMapSharpness;
        Damping = damping;
        MaxCorrectionClamp = maxCorrectionClamp;
        Summary = string.Empty;
        Advisories.Clear();
        ActiveRegions.Clear();
    }

    public void FinalizeState(bool active, string summary, IEnumerable<string>? advisories = null)
    {
        Active = active;
        Summary = summary;
        Advisories.Clear();

        if (advisories == null)
            return;

        foreach (var advisory in advisories.Where(entry => !string.IsNullOrWhiteSpace(entry)).Distinct(StringComparer.Ordinal))
            Advisories.Add(advisory);
    }
}
