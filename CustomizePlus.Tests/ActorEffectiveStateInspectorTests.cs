using CustomizePlus.Armatures.Data;
using CustomizePlus.Core.Data;
using CustomizePlus.Profiles.Data;
using CustomizePlus.Templates.Data;
using Newtonsoft.Json.Linq;
using Penumbra.GameData.Actors;
using Penumbra.GameData.Enums;
using Xunit;

namespace CustomizePlus.Tests;

public sealed class ActorEffectiveStateInspectorTests
{
    [Fact]
    public void MissingArmature_IsExplicitlyUnavailable()
    {
        var snapshot = ActorEffectiveStateSnapshot.Capture(null, new AdvancedBodyScalingSettings());

        Assert.False(snapshot.IsAvailable);
        Assert.Equal(ActorEffectiveStateFactKind.Unavailable, snapshot.Actor.Kind);
        Assert.Equal("Unavailable", snapshot.Representation.Value);
        Assert.Empty(snapshot.TemplateAssignments);
    }

    [Fact]
    public void Capture_ReportsConfiguredAssignmentsWithoutClaimingPerBoneOwnership()
    {
        var profile = new Profile
        {
            Name = "Inspector profile",
            Enabled = true,
            Priority = 42,
        };
        var first = new Template();
        first.Bones["j_sebo_c"] = new BoneTransform();
        var second = new Template();
        second.Bones["j_sako_l"] = new BoneTransform();
        profile.Templates.Add(first);
        profile.Templates.Add(second);
        profile.SetTemplateWeight(first.UniqueId, 0.35f);
        profile.SetTemplateWeight(second.UniqueId, 0.65f);
        profile.DisabledTemplates.Add(second.UniqueId);
        var armature = new Armature(ActorIdentifier.Invalid, profile);
        var profileBefore = profile.JsonSerialize();
        var armatureCountBefore = profile.Armatures.Count;

        var snapshot = ActorEffectiveStateSnapshot.Capture(armature, new AdvancedBodyScalingSettings());

        Assert.True(snapshot.IsAvailable);
        Assert.Equal(2, snapshot.TemplateAssignments.Count);
        Assert.Equal(0.35f, snapshot.TemplateAssignments[0].Weight);
        Assert.True(snapshot.TemplateAssignments[0].Enabled);
        Assert.Equal(0.65f, snapshot.TemplateAssignments[1].Weight);
        Assert.False(snapshot.TemplateAssignments[1].Enabled);
        Assert.Equal(ActorEffectiveStateFactKind.Unavailable, snapshot.PerBoneTemplateProvenance.Kind);
        Assert.Contains("blending", snapshot.PerBoneTemplateProvenance.Detail, StringComparison.OrdinalIgnoreCase);
        Assert.True(JToken.DeepEquals(profileBefore, profile.JsonSerialize()));
        Assert.Equal(armatureCountBefore, profile.Armatures.Count);
        Assert.Equal(0, armature.ProfileResolutionRevision);
    }

    [Fact]
    public void Capture_UsesPublishedEffectiveAdvancedBodyScalingWithoutResolvingAgain()
    {
        var profile = new Profile
        {
            Name = "Effective settings profile",
            Enabled = true,
            AdvancedBodyScalingOverrides = new AdvancedBodyScalingProfileSettings
            {
                UseProfileOverrides = true,
                Overrides = new AdvancedBodyScalingOverrides
                {
                    Enabled = true,
                    Mode = AdvancedBodyScalingMode.Strong,
                    SurfaceBalancingStrength = 0.72f,
                },
            },
        };
        var armature = new Armature(ActorIdentifier.Invalid, profile);
        var global = new AdvancedBodyScalingSettings
        {
            Enabled = false,
            Mode = AdvancedBodyScalingMode.Manual,
            SurfaceBalancingStrength = 0.11f,
        };
        var expectedEffective = profile.AdvancedBodyScalingOverrides.Resolve(global, Race.Elezen);
        armature.RebuildBoneTemplateBinding(advancedBodyScaling: expectedEffective, rebuildReason: "inspector test");
        // Simulate a later configuration edit before production has published a new binding.
        // The inspector must show the armature's already-published settings, not resolve again.
        profile.AdvancedBodyScalingOverrides.Overrides.Enabled = false;
        profile.AdvancedBodyScalingOverrides.Overrides.Mode = AdvancedBodyScalingMode.Manual;
        var globalBefore = JToken.FromObject(global);
        var profileBefore = profile.JsonSerialize();
        var revisionBefore = armature.ProfileResolutionRevision;

        var snapshot = ActorEffectiveStateSnapshot.Capture(armature, global);

        Assert.False(snapshot.GlobalAdvancedBodyScalingEnabled);
        Assert.True(snapshot.EffectiveAdvancedBodyScalingEnabled);
        Assert.Equal(AdvancedBodyScalingMode.Strong.ToString(), snapshot.EffectiveAdvancedBodyScalingMode);
        Assert.Equal(0.72f, snapshot.EffectiveSurfaceBalancingStrength);
        Assert.Equal(ActorEffectiveStateFactKind.Authoritative, snapshot.EffectiveAdvancedBodyScaling.Kind);
        Assert.Equal(ActorEffectiveStateFactKind.Unavailable, snapshot.RacePresetProvenance.Kind);
        Assert.True(JToken.DeepEquals(globalBefore, JToken.FromObject(global)));
        Assert.True(JToken.DeepEquals(profileBefore, profile.JsonSerialize()));
        Assert.Equal(revisionBefore, armature.ProfileResolutionRevision);
    }

    [Fact]
    public void Capture_DoesNotChangePublishedBindingOrResolvedTransforms()
    {
        var profile = new Profile { Name = "Read-only profile", Enabled = true };
        var template = new Template();
        template.Bones["j_sebo_c"] = new BoneTransform();
        profile.Templates.Add(template);
        var armature = new Armature(ActorIdentifier.Invalid, profile);
        armature.RebuildBoneTemplateBinding(rebuildReason: "inspector test");
        var resolvedBefore = armature.ResolvedBoneTransforms.ToDictionary(pair => pair.Key, pair => pair.Value.DeepCopy());
        var armatureRevisionBefore = armature.ArmatureRevision;
        var bindingGenerationBefore = armature.NativeBindingGeneration;

        _ = ActorEffectiveStateSnapshot.Capture(armature, new AdvancedBodyScalingSettings());

        Assert.Equal(armatureRevisionBefore, armature.ArmatureRevision);
        Assert.Equal(bindingGenerationBefore, armature.NativeBindingGeneration);
        Assert.Equal(resolvedBefore.Keys.OrderBy(static key => key), armature.ResolvedBoneTransforms.Keys.OrderBy(static key => key));
        foreach (var (name, transform) in resolvedBefore)
            Assert.True(AuthoringTooling.TransformEquals(transform, armature.ResolvedBoneTransforms[name]));
    }
}
