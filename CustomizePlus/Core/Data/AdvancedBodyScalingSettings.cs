// Copyright (c) Customize+.
// Licensed under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;

namespace CustomizePlus.Core.Data;

public enum AdvancedBodyScalingMode
{
    Manual = 0,
    Assist = 1,
    Automatic = 2,
    Strong = 3
}

[Serializable]
public enum AdvancedBodyRegion
{
    Spine = 0,
    Chest = 1,
    Pelvis = 2,
    Arms = 3,
    Hands = 4,
    Legs = 5,
    Feet = 6,
    Toes = 7,
    Tail = 8
}

[Serializable]
public enum AdvancedBodyScalingGuardrailMode
{
    Off = 0,
    Standard = 1,
    Strong = 2
}

[Serializable]
public enum AdvancedBodyScalingPoseValidationMode
{
    Off = 0,
    Standard = 1,
    Strong = 2
}

[Serializable]
public sealed class AdvancedBodyScalingRegionProfile
{
    private float _influenceMultiplier = 1f;
    private float _smoothingMultiplier = 1f;
    private float _guardrailMultiplier = 1f;
    private float _massRedistributionMultiplier = 1f;
    private float _poseValidationMultiplier = 1f;
    private float _naturalizationMultiplier = 1f;

    public float InfluenceMultiplier
    {
        get => _influenceMultiplier;
        set => _influenceMultiplier = Math.Clamp(value, 0f, 1f);
    }

    public float SmoothingMultiplier
    {
        get => _smoothingMultiplier;
        set => _smoothingMultiplier = Math.Clamp(value, 0f, 1f);
    }

    public float GuardrailMultiplier
    {
        get => _guardrailMultiplier;
        set => _guardrailMultiplier = Math.Clamp(value, 0f, 1f);
    }

    public float MassRedistributionMultiplier
    {
        get => _massRedistributionMultiplier;
        set => _massRedistributionMultiplier = Math.Clamp(value, 0f, 1f);
    }

    public float PoseValidationMultiplier
    {
        get => _poseValidationMultiplier;
        set => _poseValidationMultiplier = Math.Clamp(value, 0f, 1f);
    }

    public float NaturalizationMultiplier
    {
        get => _naturalizationMultiplier;
        set => _naturalizationMultiplier = Math.Clamp(value, 0f, 1f);
    }

    public bool AllowNaturalization { get; set; } = true;
    public bool AllowGuardrails { get; set; } = true;
    public bool AllowPoseValidation { get; set; } = true;

    public AdvancedBodyScalingRegionProfile DeepCopy()
        => new()
        {
            InfluenceMultiplier = InfluenceMultiplier,
            SmoothingMultiplier = SmoothingMultiplier,
            GuardrailMultiplier = GuardrailMultiplier,
            MassRedistributionMultiplier = MassRedistributionMultiplier,
            PoseValidationMultiplier = PoseValidationMultiplier,
            NaturalizationMultiplier = NaturalizationMultiplier,
            AllowNaturalization = AllowNaturalization,
            AllowGuardrails = AllowGuardrails,
            AllowPoseValidation = AllowPoseValidation
        };

    public static Dictionary<AdvancedBodyRegion, AdvancedBodyScalingRegionProfile> CreateDefaults()
        => new()
        {
            [AdvancedBodyRegion.Spine] = new AdvancedBodyScalingRegionProfile(),
            [AdvancedBodyRegion.Chest] = new AdvancedBodyScalingRegionProfile
            {
                InfluenceMultiplier = 0.9f,
                SmoothingMultiplier = 0.9f,
                GuardrailMultiplier = 0.9f,
                MassRedistributionMultiplier = 0.9f
            },
            [AdvancedBodyRegion.Pelvis] = new AdvancedBodyScalingRegionProfile
            {
                InfluenceMultiplier = 0.9f,
                SmoothingMultiplier = 0.9f,
                GuardrailMultiplier = 0.9f,
                MassRedistributionMultiplier = 0.9f
            },
            [AdvancedBodyRegion.Arms] = new AdvancedBodyScalingRegionProfile
            {
                InfluenceMultiplier = 0.75f,
                SmoothingMultiplier = 0.8f,
                GuardrailMultiplier = 0.7f,
                MassRedistributionMultiplier = 0.75f,
                PoseValidationMultiplier = 0.7f
            },
            [AdvancedBodyRegion.Hands] = new AdvancedBodyScalingRegionProfile
            {
                InfluenceMultiplier = 0.3f,
                SmoothingMultiplier = 0.35f,
                GuardrailMultiplier = 0.15f,
                MassRedistributionMultiplier = 0.3f,
                PoseValidationMultiplier = 0.15f,
                NaturalizationMultiplier = 0.35f,
                AllowGuardrails = false,
                AllowPoseValidation = false,
                AllowNaturalization = false
            },
            [AdvancedBodyRegion.Legs] = new AdvancedBodyScalingRegionProfile
            {
                InfluenceMultiplier = 0.8f,
                SmoothingMultiplier = 0.85f,
                GuardrailMultiplier = 0.8f,
                MassRedistributionMultiplier = 0.8f,
                PoseValidationMultiplier = 0.8f
            },
            [AdvancedBodyRegion.Feet] = new AdvancedBodyScalingRegionProfile
            {
                InfluenceMultiplier = 0.4f,
                SmoothingMultiplier = 0.45f,
                GuardrailMultiplier = 0.2f,
                MassRedistributionMultiplier = 0.4f,
                PoseValidationMultiplier = 0.2f,
                NaturalizationMultiplier = 0.35f,
                AllowGuardrails = false,
                AllowPoseValidation = false,
                AllowNaturalization = false
            },
            [AdvancedBodyRegion.Toes] = new AdvancedBodyScalingRegionProfile
            {
                InfluenceMultiplier = 0.2f,
                SmoothingMultiplier = 0.3f,
                GuardrailMultiplier = 0.08f,
                MassRedistributionMultiplier = 0.2f,
                PoseValidationMultiplier = 0.08f,
                NaturalizationMultiplier = 0.25f,
                AllowGuardrails = false,
                AllowPoseValidation = false,
                AllowNaturalization = false
            },
            [AdvancedBodyRegion.Tail] = new AdvancedBodyScalingRegionProfile
            {
                InfluenceMultiplier = 0.25f,
                SmoothingMultiplier = 0.35f,
                GuardrailMultiplier = 0.08f,
                MassRedistributionMultiplier = 0.25f,
                PoseValidationMultiplier = 0.08f,
                NaturalizationMultiplier = 0.25f,
                AllowGuardrails = false,
                AllowPoseValidation = false,
                AllowNaturalization = false
            }
        };
}

[Serializable]
public sealed class AdvancedBodyScalingSettings
{
    public bool Enabled { get; set; } = false;
    public AdvancedBodyScalingMode Mode { get; set; } = AdvancedBodyScalingMode.Manual;

    private float _surfaceBalancingStrength = 0.9f;
    public float SurfaceBalancingStrength
    {
        get => _surfaceBalancingStrength;
        set => _surfaceBalancingStrength = Math.Clamp(value, 0f, 1f);
    }

    private float _massRedistributionStrength = 0.9f;
    public float MassRedistributionStrength
    {
        get => _massRedistributionStrength;
        set => _massRedistributionStrength = Math.Clamp(value, 0f, 1f);
    }

    public AdvancedBodyScalingGuardrailMode GuardrailMode { get; set; } = AdvancedBodyScalingGuardrailMode.Standard;
    public AdvancedBodyScalingPoseValidationMode PoseValidationMode { get; set; } = AdvancedBodyScalingPoseValidationMode.Standard;

    private float _naturalizationStrength = 0.2f;

    public float NaturalizationStrength
    {
        get => _naturalizationStrength;
        set => _naturalizationStrength = Math.Clamp(value, 0f, 1f);
    }

    public Dictionary<AdvancedBodyRegion, AdvancedBodyScalingRegionProfile> RegionProfiles { get; set; }
        = AdvancedBodyScalingRegionProfile.CreateDefaults();

    public AdvancedBodyScalingRegionProfile GetRegionProfile(AdvancedBodyRegion region)
    {
        if (!RegionProfiles.TryGetValue(region, out var profile))
        {
            var defaults = AdvancedBodyScalingRegionProfile.CreateDefaults();
            profile = defaults.TryGetValue(region, out var fallback)
                ? fallback
                : new AdvancedBodyScalingRegionProfile();
            RegionProfiles[region] = profile;
        }

        return profile;
    }

    public void ResetToDefaults()
    {
        var defaults = new AdvancedBodyScalingSettings();
        Enabled = defaults.Enabled;
        Mode = defaults.Mode;
        SurfaceBalancingStrength = defaults.SurfaceBalancingStrength;
        MassRedistributionStrength = defaults.MassRedistributionStrength;
        GuardrailMode = defaults.GuardrailMode;
        PoseValidationMode = defaults.PoseValidationMode;
        NaturalizationStrength = defaults.NaturalizationStrength;
        RegionProfiles = AdvancedBodyScalingRegionProfile.CreateDefaults();
    }

    public AdvancedBodyScalingSettings DeepCopy()
        => new()
        {
            Enabled = Enabled,
            Mode = Mode,
            SurfaceBalancingStrength = SurfaceBalancingStrength,
            MassRedistributionStrength = MassRedistributionStrength,
            GuardrailMode = GuardrailMode,
            PoseValidationMode = PoseValidationMode,
            NaturalizationStrength = NaturalizationStrength,
            RegionProfiles = RegionProfiles.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.DeepCopy())
        };
}
