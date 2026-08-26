using System.Numerics;
using CustomizePlus.Core.Data;
using CustomizePlus.Profiles.Data;
using Xunit;

namespace CustomizePlus.Tests;

public class ProfileTransformResolverTests
{
    [Fact]
    public void EnabledAssignedTemplate_ResolvesItsExplicitTransform()
    {
        var template = CreateContribution("j_kosi", 1.15f, enabled: true, weight: 1f);

        var resolution = ProfileTransformResolver.ResolveContributions(new[] { template });

        Assert.Equal(1, resolution.EffectiveTransforms.Count);
        Assert.Equal(1.15f, resolution.EffectiveTransforms["j_kosi"].Scaling.X);
        Assert.Equal(template.TemplateId, resolution.BoneOwnerIds["j_kosi"]);
    }

    [Fact]
    public void DisabledTemplate_DoesNotResolve_AndReenableRestoresContribution()
    {
        var disabledTemplate = CreateContribution("j_kosi", 1.15f, enabled: false, weight: 1f);

        Assert.Empty(ProfileTransformResolver.ResolveContributions(new[] { disabledTemplate }).EffectiveTransforms);

        var enabledTemplate = disabledTemplate with { Enabled = true };
        Assert.True(ProfileTransformResolver.ResolveContributions(new[] { enabledTemplate }).EffectiveTransforms.ContainsKey("j_kosi"));
    }

    [Fact]
    public void TemplateWeightAndBoneEdit_AreReflectedByFreshResolution()
    {
        var baseline = CreateContribution("j_kosi", 1.00f, enabled: true, weight: 0.5f);
        var template = CreateContribution("j_kosi", 1.20f, enabled: true, weight: 0.5f);

        Assert.Equal(1.10f, ProfileTransformResolver.ResolveContributions(new[] { baseline, template }).EffectiveTransforms["j_kosi"].Scaling.X, 3);

        template.Bones["j_kosi"].Scaling = new Vector3(1.40f, 1f, 1f);
        Assert.Equal(1.20f, ProfileTransformResolver.ResolveContributions(new[] { baseline, template }).EffectiveTransforms["j_kosi"].Scaling.X, 3);
    }

    [Fact]
    public void ExplicitManualOnlyBone_RemainsAvailableToAssignedTemplates()
    {
        const string manualOnlyBone = "nf_shrt_example";
        Assert.False(BoneData.HasAutomationTrust(manualOnlyBone, BoneAutomationTrust.TemplateSafe));

        var template = CreateContribution(manualOnlyBone, 1.10f, enabled: true, weight: 1f);
        var resolution = ProfileTransformResolver.ResolveContributions(new[] { template });

        Assert.True(resolution.EffectiveTransforms.ContainsKey(manualOnlyBone));
        Assert.False(BoneData.HasAutomationTrust(manualOnlyBone, BoneAutomationTrust.SemanticSafe));
    }

    [Fact]
    public void MissingLiveSkeletonBone_RemainsInProfileOwnedResolutionForLaterRebinding()
    {
        var template = CreateContribution("race_specific_future_bone", 1.10f, enabled: true, weight: 1f);

        var resolution = ProfileTransformResolver.ResolveContributions(new[] { template });

        Assert.True(resolution.EffectiveTransforms.ContainsKey("race_specific_future_bone"));
        Assert.Equal(template.TemplateId, resolution.BoneOwnerIds["race_specific_future_bone"]);
    }

    [Fact]
    public void CapabilityRequirement_IsDormantUntilTheRequiredCapabilityIsPresent()
    {
        var requirement = new TemplateCompatibilityRequirement(SkeletonCapability.YAS);
        var absent = SkeletonCapabilityManifest.Unavailable;
        var present = CreateManifest(SkeletonCapability.YAS, SkeletonCapabilityState.Present);

        Assert.False(requirement.Evaluate(absent).IsActive);
        Assert.True(requirement.Evaluate(present).IsActive);
        Assert.True(TemplateCompatibilityRequirement.Always.Evaluate(absent).IsActive);
    }

    private static SkeletonCapabilityManifest CreateManifest(SkeletonCapability capability, SkeletonCapabilityState state)
        => new(
            capability,
            0,
            1,
            "test",
            2,
            true,
            new SkeletonTopologySummary(1, 1, 1, 0, 0, new[] { 1 }, Array.Empty<int>(), true),
            new Dictionary<SkeletonCapability, SkeletonCapabilityEvidence>
            {
                [capability] = new(state, Array.Empty<string>(), Array.Empty<string>()),
            },
            BoneAnimationCompatibility.None,
            new Dictionary<string, int>(),
            Array.Empty<string>(),
            Array.Empty<string>());

    private static ProfileTransformResolver.TemplateContribution CreateContribution(
        string boneName,
        float scaleX,
        bool enabled,
        float weight)
    {
        return new ProfileTransformResolver.TemplateContribution(
            Guid.NewGuid(),
            new Dictionary<string, BoneTransform>
            {
                [boneName] = new BoneTransform
                {
                    Scaling = new Vector3(scaleX, 1f, 1f),
                },
            },
            enabled,
            weight);
    }
}
