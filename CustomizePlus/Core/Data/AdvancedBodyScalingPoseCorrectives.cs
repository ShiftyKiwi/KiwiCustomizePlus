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
                Strength = 0.72f,
                ActivationThreshold = 0.18f,
                ActivationDeadzone = 0.06f,
                Smoothing = 0.74f,
                Falloff = 0.90f,
                MaxCorrection = 0.038f,
                Priority = 1.00f,
            },
            AdvancedBodyScalingCorrectiveRegion.ClavicleUpperChest => new AdvancedBodyScalingCorrectiveRegionSettings
            {
                Strength = 0.65f,
                ActivationThreshold = 0.16f,
                ActivationDeadzone = 0.05f,
                Smoothing = 0.70f,
                Falloff = 0.88f,
                MaxCorrection = 0.032f,
                Priority = 0.95f,
            },
            AdvancedBodyScalingCorrectiveRegion.ShoulderUpperArm => new AdvancedBodyScalingCorrectiveRegionSettings
            {
                Strength = 0.58f,
                ActivationThreshold = 0.16f,
                ActivationDeadzone = 0.05f,
                Smoothing = 0.66f,
                Falloff = 0.84f,
                MaxCorrection = 0.028f,
                Priority = 0.90f,
            },
            AdvancedBodyScalingCorrectiveRegion.ElbowForearm => new AdvancedBodyScalingCorrectiveRegionSettings
            {
                Strength = 0.52f,
                ActivationThreshold = 0.17f,
                ActivationDeadzone = 0.05f,
                Smoothing = 0.62f,
                Falloff = 0.82f,
                MaxCorrection = 0.024f,
                Priority = 0.86f,
            },
            AdvancedBodyScalingCorrectiveRegion.WaistHips => new AdvancedBodyScalingCorrectiveRegionSettings
            {
                Strength = 0.56f,
                ActivationThreshold = 0.15f,
                ActivationDeadzone = 0.05f,
                Smoothing = 0.68f,
                Falloff = 0.86f,
                MaxCorrection = 0.028f,
                Priority = 0.95f,
            },
            AdvancedBodyScalingCorrectiveRegion.HipUpperThigh => new AdvancedBodyScalingCorrectiveRegionSettings
            {
                Strength = 0.62f,
                ActivationThreshold = 0.15f,
                ActivationDeadzone = 0.05f,
                Smoothing = 0.68f,
                Falloff = 0.86f,
                MaxCorrection = 0.030f,
                Priority = 1.00f,
            },
            AdvancedBodyScalingCorrectiveRegion.ThighKneeCalf => new AdvancedBodyScalingCorrectiveRegionSettings
            {
                Strength = 0.56f,
                ActivationThreshold = 0.16f,
                ActivationDeadzone = 0.05f,
                Smoothing = 0.64f,
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
    private float _strength = 0.55f;

    public bool Enabled { get; set; }

    public float Strength
    {
        get => _strength;
        set => _strength = Math.Clamp(value, 0f, 1f);
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
        Regions = AdvancedBodyScalingCorrectiveRegionSettings.CreateDefaults();
    }

    public AdvancedBodyScalingPoseCorrectiveSettings DeepCopy()
        => new()
        {
            Enabled = Enabled,
            Strength = Strength,
            Regions = Regions.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.DeepCopy()),
        };
}

internal sealed class AdvancedBodyScalingCorrectiveDebugRegionState
{
    public AdvancedBodyScalingCorrectiveRegion Region { get; init; }
    public string Label { get; init; } = string.Empty;
    public float DriverStrength { get; init; }
    public float Discontinuity { get; init; }
    public float Activation { get; init; }
    public float Strength { get; init; }
    public float EstimatedRiskReduction { get; init; }
    public string DriverSummary { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
}

internal sealed class AdvancedBodyScalingPoseCorrectiveDebugState
{
    public AdvancedBodyScalingCorrectivePath Path { get; private set; } = AdvancedBodyScalingCorrectivePath.TransformFallback;
    public string PathDescription { get; private set; } = string.Empty;
    public string SettingsSourceLabel { get; private set; } = "Global settings";
    public bool ProfileOverridesActive { get; private set; }
    public List<AdvancedBodyScalingCorrectiveDebugRegionState> ActiveRegions { get; } = new();

    public void Reset(AdvancedBodyScalingCorrectivePath path, string pathDescription, bool profileOverridesActive)
    {
        Path = path;
        PathDescription = pathDescription;
        ProfileOverridesActive = profileOverridesActive;
        SettingsSourceLabel = profileOverridesActive ? "Profile overrides active" : "Global settings";
        ActiveRegions.Clear();
    }
}
