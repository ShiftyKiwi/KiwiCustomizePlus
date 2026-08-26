using System.Numerics;
using CustomizePlus.Core.Data;
using CustomizePlus.Profiles.Data;
using CustomizePlus.Templates.Data;
using Newtonsoft.Json.Linq;
using Xunit;

namespace CustomizePlus.Tests;

public sealed class PersistenceRoundTripTests
{
    [Fact]
    public void TemplateSerialization_RoundTripsLocksPinsAndPropagationFalloff()
    {
        var template = new Template();
        var transform = new BoneTransform
        {
            Translation = new Vector3(0.03f, -0.02f, 0.01f),
            Rotation = new Vector3(12f, -7f, 3f),
            Scaling = new Vector3(1.23f, 0.91f, 1.15f),
            ChildScaling = new Vector3(1.10f, 0.95f, 1.08f),
            ChildScalingIndependent = true,
            PropagateTranslation = true,
            PropagateRotation = true,
            PropagateScale = true,
            PropagationFalloff = 0.37f,
            LockState = BoneLockState.Locked,
            PinX = true,
            PinZ = true,
        };
        template.Bones["j_sebo_c"] = transform;

        var restored = Template.Load(template.JsonSerialize());
        var result = Assert.Single(restored.Bones).Value;

        Assert.Equal(transform.Translation, result.Translation);
        Assert.Equal(transform.Rotation, result.Rotation);
        Assert.Equal(transform.Scaling, result.Scaling);
        Assert.Equal(transform.ChildScaling, result.ChildScaling);
        Assert.Equal(transform.ChildScalingIndependent, result.ChildScalingIndependent);
        Assert.Equal(transform.PropagateTranslation, result.PropagateTranslation);
        Assert.Equal(transform.PropagateRotation, result.PropagateRotation);
        Assert.Equal(transform.PropagateScale, result.PropagateScale);
        Assert.Equal(transform.PropagationFalloff, result.PropagationFalloff);
        Assert.Equal(transform.LockState, result.LockState);
        Assert.Equal(transform.PinX, result.PinX);
        Assert.Equal(transform.PinY, result.PinY);
        Assert.Equal(transform.PinZ, result.PinZ);
    }

    [Fact]
    public void ProfileSerialization_RetainsTemplateWeightCompatibilityAndAdvancedOverrides()
    {
        var template = new Template();
        var profile = new Profile
        {
            Enabled = true,
            Priority = 12,
            AdvancedBodyScalingOverrides = new AdvancedBodyScalingProfileSettings
            {
                UseProfileOverrides = true,
                Overrides = new AdvancedBodyScalingOverrides
                {
                    Enabled = true,
                    NaturalizationStrength = 0.42f,
                },
            },
        };
        profile.Templates.Add(template);
        profile.DisabledTemplates.Add(template.UniqueId);
        profile.SetTemplateWeight(template.UniqueId, 0.63f);
        profile.SetTemplateCompatibilityRequirement(template.UniqueId, new TemplateCompatibilityRequirement(SkeletonCapability.IVCS2 | SkeletonCapability.NFLB));

        var json = profile.JsonSerialize();
        var serializedTemplate = Assert.Single(json["Templates"]!);
        var advanced = Assert.IsType<Newtonsoft.Json.Linq.JObject>(json["AdvancedBodyScaling"]);

        Assert.Equal(Profile.Version, json.Value<int>("Version"));
        Assert.False(serializedTemplate.Value<bool>("Enabled"));
        Assert.Equal(0.63f, serializedTemplate.Value<float>("Weight"));
        Assert.Equal((int)(SkeletonCapability.IVCS2 | SkeletonCapability.NFLB), serializedTemplate.Value<int>("RequiredCapabilities"));
        Assert.True(advanced.Value<bool>("UseProfileOverrides"));
        var overrides = Assert.IsType<Newtonsoft.Json.Linq.JObject>(advanced["Overrides"]);
        Assert.True(overrides.Value<bool>("Enabled"));
        Assert.Equal(0.42f, overrides.Value<float>("NaturalizationStrength"));
    }

    [Fact]
    public void LegacyAdvancedBodySettings_UseSafeCurrentDefaultsAndRemainIdempotent()
    {
        var legacy = JObject.Parse("""
        {
          "Enabled": true,
          "Mode": 3,
          "SurfaceBalancingStrength": 0.72,
          "NaturalizationStrength": 0.18
        }
        """);

        var migrated = legacy.ToObject<AdvancedBodyScalingSettings>();
        Assert.NotNull(migrated);
        Assert.True(migrated!.Enabled);
        Assert.Equal(AdvancedBodyScalingMode.Strong, migrated.Mode);
        Assert.False(migrated.ProportionalBalanceEnabled);
        Assert.False(migrated.SurfaceSmoothnessEnabled);
        Assert.False(migrated.CrossSectionConditioningEnabled);
        Assert.False(migrated.ShapeFairnessEnabled);
        Assert.False(migrated.LocalVolumeIntentEnabled);
        Assert.False(migrated.PoseAwareJointCorrectivesEnabled);
        Assert.True(migrated.BilateralConsistencyEnabled);

        var firstSave = JToken.FromObject(migrated);
        var reloaded = firstSave.ToObject<AdvancedBodyScalingSettings>();
        Assert.NotNull(reloaded);
        Assert.True(JToken.DeepEquals(firstSave, JToken.FromObject(reloaded!)));
    }

    [Fact]
    public void AdvancedBodySettings_RoundTripAllRecentShapingAndRuntimeLayers()
    {
        var settings = new AdvancedBodyScalingSettings
        {
            Enabled = true,
            Mode = AdvancedBodyScalingMode.Strong,
            AnimationSafeModeEnabled = true,
            SurfaceBalancingStrength = 0.81f,
            MassRedistributionStrength = 0.73f,
            BilateralConsistencyEnabled = false,
            ProportionalBalanceEnabled = true,
            ProportionalBalanceStrength = 0.64f,
            SurfaceSmoothnessEnabled = true,
            SurfaceSmoothnessStrength = 0.58f,
            CrossSectionConditioningEnabled = true,
            CrossSectionConditioningStrength = 0.61f,
            ShapeFairnessEnabled = true,
            ShapeFairnessStrength = 0.55f,
            LocalVolumeIntentEnabled = true,
            LocalVolumeIntentStrength = 0.52f,
            PoseAwareJointCorrectivesEnabled = true,
            PoseAwareJointCorrectivesStrength = 0.49f,
            GuardrailMode = AdvancedBodyScalingGuardrailMode.Strong,
            PoseValidationMode = AdvancedBodyScalingPoseValidationMode.Strong,
            NaturalizationStrength = 0.31f,
            ModelDerivedBoneImportanceEnabled = true,
            PreferTrueSkinWeightImportance = true,
            BoneImportanceHeuristicBlend = 0.87f,
            NeckLengthCompensation = 1.08f,
            NeckShoulderBlendStrength = 0.76f,
            ClavicleShoulderSmoothing = 0.68f,
            UseRaceSpecificNeckCompensation = true,
        };
        settings.PoseCorrectives.Enabled = true;
        settings.PoseCorrectives.Strength = 0.57f;
        settings.FullIkRetargeting.Enabled = true;
        settings.FullIkRetargeting.GlobalStrength = 0.53f;
        settings.MotionWarping.Enabled = true;
        settings.MotionWarping.GlobalStrength = 0.51f;
        settings.FullBodyIk.Enabled = true;
        settings.FullBodyIk.GlobalStrength = 0.48f;

        var serialized = JToken.FromObject(settings);
        var roundTripped = serialized.ToObject<AdvancedBodyScalingSettings>();
        Assert.NotNull(roundTripped);
        Assert.True(JToken.DeepEquals(serialized, JToken.FromObject(roundTripped!)));

        var overrides = new AdvancedBodyScalingOverrides
        {
            BilateralConsistencyEnabled = false,
            ProportionalBalanceEnabled = true,
            ProportionalBalanceStrength = 0.64f,
            SurfaceSmoothnessEnabled = true,
            CrossSectionConditioningEnabled = true,
            ShapeFairnessEnabled = true,
            LocalVolumeIntentEnabled = true,
            PoseAwareJointCorrectivesEnabled = true,
            PoseAwareJointCorrectivesStrength = 0.49f,
            MotionWarpingEnabled = true,
            FullBodyIkEnabled = true,
            FullIkRetargetingEnabled = true,
        };
        var profileRoundTrip = JToken.FromObject(overrides).ToObject<AdvancedBodyScalingOverrides>();
        Assert.NotNull(profileRoundTrip);
        Assert.True(JToken.DeepEquals(JToken.FromObject(overrides), JToken.FromObject(profileRoundTrip!)));
    }
}
