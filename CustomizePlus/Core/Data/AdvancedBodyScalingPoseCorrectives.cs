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
    HipUpperThigh = 2,
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

    public bool Enabled { get; set; } = true;

    public float Strength
    {
        get => _strength;
        set => _strength = Math.Clamp(value, 0f, 1f);
    }

    public AdvancedBodyScalingCorrectiveRegionSettings DeepCopy()
        => new()
        {
            Enabled = Enabled,
            Strength = Strength,
        };

    public static Dictionary<AdvancedBodyScalingCorrectiveRegion, AdvancedBodyScalingCorrectiveRegionSettings> CreateDefaults()
        => new()
        {
            [AdvancedBodyScalingCorrectiveRegion.NeckShoulder] = new AdvancedBodyScalingCorrectiveRegionSettings(),
            [AdvancedBodyScalingCorrectiveRegion.ClavicleUpperChest] = new AdvancedBodyScalingCorrectiveRegionSettings(),
            [AdvancedBodyScalingCorrectiveRegion.HipUpperThigh] = new AdvancedBodyScalingCorrectiveRegionSettings(),
        };
}

[Serializable]
public sealed class AdvancedBodyScalingPoseCorrectiveSettings
{
    private float _strength = 0.5f;

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
            settings = new AdvancedBodyScalingCorrectiveRegionSettings();
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
    public float Strength { get; init; }
    public string DriverSummary { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
}

internal sealed class AdvancedBodyScalingPoseCorrectiveDebugState
{
    public AdvancedBodyScalingCorrectivePath Path { get; private set; } = AdvancedBodyScalingCorrectivePath.TransformFallback;
    public string PathDescription { get; private set; } = string.Empty;
    public List<AdvancedBodyScalingCorrectiveDebugRegionState> ActiveRegions { get; } = new();

    public void Reset(AdvancedBodyScalingCorrectivePath path, string pathDescription)
    {
        Path = path;
        PathDescription = pathDescription;
        ActiveRegions.Clear();
    }
}
