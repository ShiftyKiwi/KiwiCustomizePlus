using CustomizePlus.Core.Data;
using CustomizePlus.Profiles.Data;
using Newtonsoft.Json.Linq;
using Penumbra.GameData.Enums;
using Xunit;

namespace CustomizePlus.Tests;

public sealed class AdvancedBodyScalingOverridePresentationTests
{
    [Fact]
    public void EmptyOverrides_HaveNoVisibleOverrideSections()
    {
        var summary = AdvancedBodyScalingOverridePresentation.Summarize(new AdvancedBodyScalingOverrides());

        Assert.Equal(0, summary.TotalCount);
        foreach (var section in Enum.GetValues<AdvancedBodyScalingOverrideSection>())
            Assert.Equal(0, summary.GetCount(section));

        Assert.True(AdvancedBodyScalingOverridePresentation.ShouldShowRow(false, false));
        Assert.False(AdvancedBodyScalingOverridePresentation.ShouldShowRow(true, false));
    }

    [Fact]
    public void Summary_CountsScalarAndNestedOverridesBySection()
    {
        var overrides = new AdvancedBodyScalingOverrides
        {
            Enabled = false,
            SurfaceBalancingStrength = 0.72f,
            GuardrailMode = AdvancedBodyScalingGuardrailMode.Standard,
            UseRaceSpecificNeckCompensation = true,
            ModelDerivedBoneImportanceEnabled = true,
            PoseCorrectivesEnabled = true,
            FullIkRetargetingStrength = 0.4f,
            MotionWarpingEnabled = true,
            FullBodyIkIterationCount = 4,
        };
        overrides.PoseCorrectiveRegionOverrides[AdvancedBodyScalingCorrectiveRegion.ClavicleUpperChest] = new AdvancedBodyScalingCorrectiveRegionOverrides
        {
            Enabled = false,
            Strength = 0.3f,
        };
        overrides.FullIkRetargetingChainOverrides[AdvancedBodyScalingFullBodyIkChain.LeftArm] = new AdvancedBodyScalingFullIkRetargetingChainOverrides
        {
            Strength = 0.5f,
        };
        overrides.MotionWarpingChainOverrides[AdvancedBodyScalingFullBodyIkChain.LeftLeg] = new AdvancedBodyScalingMotionWarpingChainOverrides
        {
            Enabled = false,
        };
        overrides.FullBodyIkChainOverrides[AdvancedBodyScalingFullBodyIkChain.Spine] = new AdvancedBodyScalingFullBodyIkChainOverrides
        {
            Enabled = true,
            Strength = 0.6f,
        };
        overrides.RegionOverrides[AdvancedBodyRegion.Chest] = new AdvancedBodyScalingRegionProfileOverrides
        {
            SmoothingMultiplier = 0.6f,
            AllowNaturalization = false,
        };

        var summary = AdvancedBodyScalingOverridePresentation.Summarize(overrides);

        Assert.Equal(3, summary.GeneralAndShaping);
        Assert.Equal(1, summary.RaceSpecificNeck);
        Assert.Equal(1, summary.BoneImportance);
        Assert.Equal(3, summary.PoseCorrectives);
        Assert.Equal(2, summary.FullIkRetargeting);
        Assert.Equal(2, summary.MotionWarping);
        Assert.Equal(3, summary.FullBodyIk);
        Assert.Equal(2, summary.RegionTuning);
        Assert.Equal(17, summary.TotalCount);
        Assert.True(AdvancedBodyScalingOverridePresentation.ShouldShowRow(true, true));
    }

    [Fact]
    public void PresentationFilter_DoesNotMutateOverridesOrProfileSerialization()
    {
        var profile = new Profile
        {
            AdvancedBodyScalingOverrides = new AdvancedBodyScalingProfileSettings
            {
                UseProfileOverrides = true,
                Overrides = new AdvancedBodyScalingOverrides
                {
                    AnimationSafeModeEnabled = false,
                    NaturalizationStrength = 0.42f,
                },
            },
        };
        var before = profile.JsonSerialize();

        var summary = AdvancedBodyScalingOverridePresentation.Summarize(profile.AdvancedBodyScalingOverrides.Overrides);
        _ = summary.GetCount(AdvancedBodyScalingOverrideSection.GeneralAndShaping);
        _ = AdvancedBodyScalingOverridePresentation.ShouldShowRow(true, false);
        _ = AdvancedBodyScalingOverridePresentation.ShouldShowRow(true, true);

        Assert.True(JToken.DeepEquals(before, profile.JsonSerialize()));
    }

    [Fact]
    public void ClearingSingleOverride_ReturnsToInheritedResolution()
    {
        var baseline = new AdvancedBodyScalingSettings
        {
            Enabled = false,
            Mode = AdvancedBodyScalingMode.Assist,
            SurfaceBalancingStrength = 0.21f,
        };
        var settings = new AdvancedBodyScalingProfileSettings
        {
            UseProfileOverrides = true,
            Overrides = new AdvancedBodyScalingOverrides
            {
                Enabled = true,
                Mode = AdvancedBodyScalingMode.Strong,
                SurfaceBalancingStrength = 0.78f,
            },
        };

        var overridden = settings.Resolve(baseline, Race.Unknown);
        settings.Overrides.Enabled = null;
        settings.Overrides.Mode = null;
        settings.Overrides.SurfaceBalancingStrength = null;
        var inherited = settings.Resolve(baseline, Race.Unknown);

        Assert.True(overridden.Enabled);
        Assert.Equal(AdvancedBodyScalingMode.Strong, overridden.Mode);
        Assert.Equal(0.78f, overridden.SurfaceBalancingStrength);
        Assert.False(inherited.Enabled);
        Assert.Equal(AdvancedBodyScalingMode.Assist, inherited.Mode);
        Assert.Equal(0.21f, inherited.SurfaceBalancingStrength);
    }

    [Fact]
    public void PresentationHelpers_LeaveRuntimeResolutionUnchanged()
    {
        var baseline = new AdvancedBodyScalingSettings
        {
            Enabled = true,
            Mode = AdvancedBodyScalingMode.Assist,
            NaturalizationStrength = 0.1f,
        };
        var settings = new AdvancedBodyScalingProfileSettings
        {
            UseProfileOverrides = true,
            Overrides = new AdvancedBodyScalingOverrides
            {
                Enabled = false,
                Mode = AdvancedBodyScalingMode.Strong,
                NaturalizationStrength = 0.35f,
            },
        };
        var before = settings.Resolve(baseline, Race.Unknown);

        var summary = AdvancedBodyScalingOverridePresentation.Summarize(settings.Overrides);
        foreach (var section in Enum.GetValues<AdvancedBodyScalingOverrideSection>())
            _ = summary.GetCount(section);

        var after = settings.Resolve(baseline, Race.Unknown);
        Assert.True(JToken.DeepEquals(JToken.FromObject(before), JToken.FromObject(after)));
    }
}
