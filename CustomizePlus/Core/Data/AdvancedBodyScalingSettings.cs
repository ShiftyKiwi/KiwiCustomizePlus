// Copyright (c) Customize+.
// Licensed under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using Penumbra.GameData.Enums;

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
    Tail = 8,
    NeckShoulder = 9
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
            [AdvancedBodyRegion.NeckShoulder] = new AdvancedBodyScalingRegionProfile
            {
                InfluenceMultiplier = 0.85f,
                SmoothingMultiplier = 0.85f,
                GuardrailMultiplier = 0.85f,
                MassRedistributionMultiplier = 0.85f,
                PoseValidationMultiplier = 0.85f
            },
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
public sealed class AdvancedBodyScalingNeckCompensationPreset
{
    private float _neckLengthCompensation;
    private float _neckShoulderBlendStrength;
    private float _clavicleShoulderSmoothing;

    private static readonly AdvancedBodyScalingNeckCompensationPreset LongNeckStrong = new()
    {
        NeckLengthCompensation = 0.16f,
        NeckShoulderBlendStrength = 0.55f,
        ClavicleShoulderSmoothing = 0.45f
    };

    private static readonly AdvancedBodyScalingNeckCompensationPreset ModerateLongNeck = new()
    {
        NeckLengthCompensation = 0.12f,
        NeckShoulderBlendStrength = 0.45f,
        ClavicleShoulderSmoothing = 0.38f
    };

    private static readonly AdvancedBodyScalingNeckCompensationPreset LightLongNeck = new()
    {
        NeckLengthCompensation = 0.05f,
        NeckShoulderBlendStrength = 0.40f,
        ClavicleShoulderSmoothing = 0.32f
    };

    private static readonly AdvancedBodyScalingNeckCompensationPreset Neutral = new()
    {
        NeckLengthCompensation = 0.01f,
        NeckShoulderBlendStrength = 0.35f,
        ClavicleShoulderSmoothing = 0.25f
    };

    private static readonly AdvancedBodyScalingNeckCompensationPreset Compact = new()
    {
        NeckLengthCompensation = 0.00f,
        NeckShoulderBlendStrength = 0.25f,
        ClavicleShoulderSmoothing = 0.20f
    };

    public float NeckLengthCompensation
    {
        get => _neckLengthCompensation;
        set => _neckLengthCompensation = Math.Clamp(value, 0f, 1f);
    }

    public float NeckShoulderBlendStrength
    {
        get => _neckShoulderBlendStrength;
        set => _neckShoulderBlendStrength = Math.Clamp(value, 0f, 1f);
    }

    public float ClavicleShoulderSmoothing
    {
        get => _clavicleShoulderSmoothing;
        set => _clavicleShoulderSmoothing = Math.Clamp(value, 0f, 1f);
    }

    public AdvancedBodyScalingNeckCompensationPreset DeepCopy()
        => new()
        {
            NeckLengthCompensation = NeckLengthCompensation,
            NeckShoulderBlendStrength = NeckShoulderBlendStrength,
            ClavicleShoulderSmoothing = ClavicleShoulderSmoothing
        };

    public static AdvancedBodyScalingNeckCompensationPreset CreateDefault(Race race)
        => race switch
        {
            Race.Elezen => LongNeckStrong.DeepCopy(),
            Race.Viera => ModerateLongNeck.DeepCopy(),
            Race.Miqote => LightLongNeck.DeepCopy(),
            Race.Hyur => Neutral.DeepCopy(),
            Race.AuRa => Neutral.DeepCopy(),
            Race.Roegadyn => Compact.DeepCopy(),
            Race.Hrothgar => Compact.DeepCopy(),
            Race.Lalafell => Compact.DeepCopy(),
            _ => new AdvancedBodyScalingNeckCompensationPreset()
        };

    public static Dictionary<Race, AdvancedBodyScalingNeckCompensationPreset> CreateDefaults()
        => new[]
        {
            Race.Elezen,
            Race.Viera,
            Race.Miqote,
            Race.Hyur,
            Race.AuRa,
            Race.Roegadyn,
            Race.Hrothgar,
            Race.Lalafell
        }.ToDictionary(race => race, CreateDefault);
}

[Serializable]
public sealed class AdvancedBodyScalingSettings
{
    public bool Enabled { get; set; } = false;
    public AdvancedBodyScalingMode Mode { get; set; } = AdvancedBodyScalingMode.Manual;
    public bool AnimationSafeModeEnabled { get; set; } = false;

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

    private float _neckLengthCompensation;

    public float NeckLengthCompensation
    {
        get => _neckLengthCompensation;
        set => _neckLengthCompensation = Math.Clamp(value, 0f, 1f);
    }

    private float _neckShoulderBlendStrength = 0.35f;

    public float NeckShoulderBlendStrength
    {
        get => _neckShoulderBlendStrength;
        set => _neckShoulderBlendStrength = Math.Clamp(value, 0f, 1f);
    }

    private float _clavicleShoulderSmoothing = 0.25f;

    public float ClavicleShoulderSmoothing
    {
        get => _clavicleShoulderSmoothing;
        set => _clavicleShoulderSmoothing = Math.Clamp(value, 0f, 1f);
    }

    public bool UseRaceSpecificNeckCompensation { get; set; } = false;

    public Dictionary<Race, AdvancedBodyScalingNeckCompensationPreset> RaceNeckPresets { get; set; }
        = AdvancedBodyScalingNeckCompensationPreset.CreateDefaults();

    public Dictionary<AdvancedBodyRegion, AdvancedBodyScalingRegionProfile> RegionProfiles { get; set; }
        = AdvancedBodyScalingRegionProfile.CreateDefaults();

    public AdvancedBodyScalingPoseCorrectiveSettings PoseCorrectives { get; set; } = new();

    public AdvancedBodyScalingFullIkRetargetingSettings FullIkRetargeting { get; set; } = new();

    public AdvancedBodyScalingFullBodyIkSettings FullBodyIk { get; set; } = new();

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

    public AdvancedBodyScalingSettings ApplyRaceNeckPreset(Race race)
    {
        if (!UseRaceSpecificNeckCompensation || race == Race.Unknown)
            return this;

        if (RaceNeckPresets == null || !RaceNeckPresets.TryGetValue(race, out var preset))
            return this;

        var resolved = DeepCopy();
        resolved.NeckLengthCompensation = preset.NeckLengthCompensation;
        resolved.NeckShoulderBlendStrength = preset.NeckShoulderBlendStrength;
        resolved.ClavicleShoulderSmoothing = preset.ClavicleShoulderSmoothing;

        return resolved;
    }

    public AdvancedBodyScalingSettings CreateRuntimeResolvedSettings()
    {
        if (!AnimationSafeModeEnabled)
            return this;

        var resolved = DeepCopy();
        resolved.ApplyAnimationSafeBias();
        return resolved;
    }

    private void ApplyAnimationSafeBias()
    {
        SurfaceBalancingStrength = MathF.Max(SurfaceBalancingStrength, 0.65f);
        MassRedistributionStrength = MathF.Min(MassRedistributionStrength, 0.70f);
        NaturalizationStrength = MathF.Min(NaturalizationStrength, 0.55f);
        NeckLengthCompensation = MathF.Min(NeckLengthCompensation, 0.75f);
        NeckShoulderBlendStrength = MathF.Max(NeckShoulderBlendStrength, 0.45f);
        ClavicleShoulderSmoothing = MathF.Max(ClavicleShoulderSmoothing, 0.35f);

        if (GuardrailMode == AdvancedBodyScalingGuardrailMode.Off)
            GuardrailMode = AdvancedBodyScalingGuardrailMode.Standard;

        if (PoseValidationMode == AdvancedBodyScalingPoseValidationMode.Off)
            PoseValidationMode = AdvancedBodyScalingPoseValidationMode.Standard;

        foreach (var (region, profile) in RegionProfiles)
            ApplyAnimationSafeRegionBias(region, profile);

        PoseCorrectives.Strength = MathF.Min(PoseCorrectives.Strength, 0.85f);
        foreach (var region in AdvancedBodyScalingPoseCorrectiveSystem.GetOrderedRegions())
        {
            var corrective = PoseCorrectives.GetRegionSettings(region);
            corrective.Strength = MathF.Min(corrective.Strength, 0.85f);
            corrective.Smoothing = MathF.Max(corrective.Smoothing, 0.7f);
            corrective.ActivationDeadzone = MathF.Max(corrective.ActivationDeadzone, 0.06f);
            corrective.MaxCorrection = MathF.Min(corrective.MaxCorrection, 0.035f);
        }

        FullIkRetargeting.GlobalStrength = MathF.Min(FullIkRetargeting.GlobalStrength, 0.32f);
        FullIkRetargeting.PelvisStrength = MathF.Min(FullIkRetargeting.PelvisStrength, 0.28f);
        FullIkRetargeting.SpineStrength = MathF.Min(FullIkRetargeting.SpineStrength, 0.30f);
        FullIkRetargeting.ArmStrength = MathF.Min(FullIkRetargeting.ArmStrength, 0.30f);
        FullIkRetargeting.LegStrength = MathF.Min(FullIkRetargeting.LegStrength, 0.32f);
        FullIkRetargeting.HeadStrength = MathF.Min(FullIkRetargeting.HeadStrength, 0.22f);
        FullIkRetargeting.ReachAdaptationStrength = MathF.Min(FullIkRetargeting.ReachAdaptationStrength, 0.50f);
        FullIkRetargeting.StrideAdaptationStrength = MathF.Min(FullIkRetargeting.StrideAdaptationStrength, 0.46f);
        FullIkRetargeting.PosturePreservationStrength = MathF.Min(FullIkRetargeting.PosturePreservationStrength, 0.42f);
        FullIkRetargeting.MotionSafetyBias = MathF.Max(FullIkRetargeting.MotionSafetyBias, 0.86f);
        FullIkRetargeting.BlendBias = MathF.Min(FullIkRetargeting.BlendBias, 0.62f);
        FullIkRetargeting.MaxCorrectionClamp = MathF.Min(FullIkRetargeting.MaxCorrectionClamp, 0.18f);
        foreach (var chain in Enum.GetValues<AdvancedBodyScalingFullBodyIkChain>())
        {
            var chainSettings = FullIkRetargeting.GetChainSettings(chain);
            chainSettings.Strength = MathF.Min(chainSettings.Strength, AdvancedBodyScalingFullIkRetargetingTuning.GetRecommendedChainStrengthMax(chain));
        }

        FullBodyIk.GlobalStrength = MathF.Min(FullBodyIk.GlobalStrength, 0.34f);
        FullBodyIk.IterationCount = Math.Min(FullBodyIk.IterationCount, 4);
        FullBodyIk.ConvergenceTolerance = MathF.Max(FullBodyIk.ConvergenceTolerance, 0.020f);
        FullBodyIk.PelvisCompensationStrength = MathF.Min(FullBodyIk.PelvisCompensationStrength, 0.38f);
        FullBodyIk.SpineRedistributionStrength = MathF.Min(FullBodyIk.SpineRedistributionStrength, 0.34f);
        FullBodyIk.LegStrength = MathF.Min(FullBodyIk.LegStrength, 0.38f);
        FullBodyIk.ArmStrength = MathF.Min(FullBodyIk.ArmStrength, 0.34f);
        FullBodyIk.HeadAlignmentStrength = MathF.Min(FullBodyIk.HeadAlignmentStrength, 0.26f);
        FullBodyIk.GroundingBias = MathF.Min(FullBodyIk.GroundingBias, 0.62f);
        FullBodyIk.MotionSafetyBias = MathF.Max(FullBodyIk.MotionSafetyBias, 0.86f);
        FullBodyIk.MaxCorrectionClamp = MathF.Min(FullBodyIk.MaxCorrectionClamp, 0.24f);
        foreach (var chain in Enum.GetValues<AdvancedBodyScalingFullBodyIkChain>())
        {
            var chainSettings = FullBodyIk.GetChainSettings(chain);
            chainSettings.Strength = MathF.Min(chainSettings.Strength, AdvancedBodyScalingFullBodyIkTuning.GetRecommendedChainStrengthMax(chain));
        }
    }

    private static void ApplyAnimationSafeRegionBias(AdvancedBodyRegion region, AdvancedBodyScalingRegionProfile profile)
    {
        switch (region)
        {
            case AdvancedBodyRegion.NeckShoulder:
                profile.InfluenceMultiplier = MathF.Min(profile.InfluenceMultiplier, 0.75f);
                profile.SmoothingMultiplier = MathF.Max(profile.SmoothingMultiplier, 0.95f);
                profile.GuardrailMultiplier = MathF.Max(profile.GuardrailMultiplier, 0.95f);
                profile.MassRedistributionMultiplier = MathF.Min(profile.MassRedistributionMultiplier, 0.75f);
                profile.PoseValidationMultiplier = MathF.Max(profile.PoseValidationMultiplier, 0.95f);
                break;
            case AdvancedBodyRegion.Arms:
                profile.InfluenceMultiplier = MathF.Min(profile.InfluenceMultiplier, 0.72f);
                profile.SmoothingMultiplier = MathF.Max(profile.SmoothingMultiplier, 0.9f);
                profile.GuardrailMultiplier = MathF.Max(profile.GuardrailMultiplier, 0.85f);
                profile.MassRedistributionMultiplier = MathF.Min(profile.MassRedistributionMultiplier, 0.7f);
                profile.PoseValidationMultiplier = MathF.Max(profile.PoseValidationMultiplier, 0.85f);
                break;
            case AdvancedBodyRegion.Hands:
                profile.InfluenceMultiplier = MathF.Min(profile.InfluenceMultiplier, 0.2f);
                profile.MassRedistributionMultiplier = MathF.Min(profile.MassRedistributionMultiplier, 0.2f);
                profile.NaturalizationMultiplier = MathF.Min(profile.NaturalizationMultiplier, 0.35f);
                break;
            case AdvancedBodyRegion.Legs:
                profile.InfluenceMultiplier = MathF.Min(profile.InfluenceMultiplier, 0.75f);
                profile.SmoothingMultiplier = MathF.Max(profile.SmoothingMultiplier, 0.9f);
                profile.GuardrailMultiplier = MathF.Max(profile.GuardrailMultiplier, 0.9f);
                profile.MassRedistributionMultiplier = MathF.Min(profile.MassRedistributionMultiplier, 0.75f);
                profile.PoseValidationMultiplier = MathF.Max(profile.PoseValidationMultiplier, 0.9f);
                break;
            case AdvancedBodyRegion.Feet:
                profile.InfluenceMultiplier = MathF.Min(profile.InfluenceMultiplier, 0.3f);
                profile.MassRedistributionMultiplier = MathF.Min(profile.MassRedistributionMultiplier, 0.3f);
                profile.NaturalizationMultiplier = MathF.Min(profile.NaturalizationMultiplier, 0.35f);
                break;
            case AdvancedBodyRegion.Toes:
                profile.InfluenceMultiplier = MathF.Min(profile.InfluenceMultiplier, 0.15f);
                profile.MassRedistributionMultiplier = MathF.Min(profile.MassRedistributionMultiplier, 0.15f);
                profile.NaturalizationMultiplier = MathF.Min(profile.NaturalizationMultiplier, 0.25f);
                break;
            case AdvancedBodyRegion.Tail:
                profile.InfluenceMultiplier = MathF.Min(profile.InfluenceMultiplier, 0.2f);
                profile.MassRedistributionMultiplier = MathF.Min(profile.MassRedistributionMultiplier, 0.2f);
                profile.NaturalizationMultiplier = MathF.Min(profile.NaturalizationMultiplier, 0.25f);
                break;
            default:
                profile.SmoothingMultiplier = MathF.Max(profile.SmoothingMultiplier, 0.85f);
                break;
        }
    }

    public void ResetToDefaults()
    {
        var defaults = new AdvancedBodyScalingSettings();
        Enabled = defaults.Enabled;
        Mode = defaults.Mode;
        AnimationSafeModeEnabled = defaults.AnimationSafeModeEnabled;
        SurfaceBalancingStrength = defaults.SurfaceBalancingStrength;
        MassRedistributionStrength = defaults.MassRedistributionStrength;
        GuardrailMode = defaults.GuardrailMode;
        PoseValidationMode = defaults.PoseValidationMode;
        NaturalizationStrength = defaults.NaturalizationStrength;
        NeckLengthCompensation = defaults.NeckLengthCompensation;
        NeckShoulderBlendStrength = defaults.NeckShoulderBlendStrength;
        ClavicleShoulderSmoothing = defaults.ClavicleShoulderSmoothing;
        UseRaceSpecificNeckCompensation = defaults.UseRaceSpecificNeckCompensation;
        RaceNeckPresets = AdvancedBodyScalingNeckCompensationPreset.CreateDefaults();
        RegionProfiles = AdvancedBodyScalingRegionProfile.CreateDefaults();
        PoseCorrectives = new AdvancedBodyScalingPoseCorrectiveSettings();
        FullIkRetargeting = new AdvancedBodyScalingFullIkRetargetingSettings();
        FullBodyIk = new AdvancedBodyScalingFullBodyIkSettings();
    }

    public AdvancedBodyScalingSettings DeepCopy()
        => new()
        {
            Enabled = Enabled,
            Mode = Mode,
            AnimationSafeModeEnabled = AnimationSafeModeEnabled,
            SurfaceBalancingStrength = SurfaceBalancingStrength,
            MassRedistributionStrength = MassRedistributionStrength,
            GuardrailMode = GuardrailMode,
            PoseValidationMode = PoseValidationMode,
            NaturalizationStrength = NaturalizationStrength,
            NeckLengthCompensation = NeckLengthCompensation,
            NeckShoulderBlendStrength = NeckShoulderBlendStrength,
            ClavicleShoulderSmoothing = ClavicleShoulderSmoothing,
            UseRaceSpecificNeckCompensation = UseRaceSpecificNeckCompensation,
            RaceNeckPresets = RaceNeckPresets == null
                ? new Dictionary<Race, AdvancedBodyScalingNeckCompensationPreset>()
                : RaceNeckPresets.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.DeepCopy()),
            RegionProfiles = RegionProfiles.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.DeepCopy()),
            PoseCorrectives = PoseCorrectives.DeepCopy(),
            FullIkRetargeting = FullIkRetargeting.DeepCopy(),
            FullBodyIk = FullBodyIk.DeepCopy(),
        };
}
