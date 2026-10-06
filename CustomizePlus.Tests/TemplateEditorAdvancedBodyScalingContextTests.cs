using System;
using CustomizePlus.Core.Data;
using CustomizePlus.Profiles;
using CustomizePlus.Profiles.Data;
using CustomizePlus.Profiles.Enums;
using CustomizePlus.Templates.Data;
using Penumbra.GameData.Enums;
using Xunit;

namespace CustomizePlus.Tests;

public sealed class TemplateEditorAdvancedBodyScalingContextTests
{
    [Fact]
    public void WinningEnabledAssignment_UsesProfileOverridesWithoutAliasing()
    {
        var template = CreateTemplate();
        var source = CreateProfile(template, new AdvancedBodyScalingProfileSettings
        {
            UseProfileOverrides = true,
            Overrides = new AdvancedBodyScalingOverrides
            {
                Mode = AdvancedBodyScalingMode.Automatic,
                NaturalizationStrength = 0.4f,
            },
        });
        var lowerPriority = CreateProfile(template, new AdvancedBodyScalingProfileSettings
        {
            UseProfileOverrides = true,
            Overrides = new AdvancedBodyScalingOverrides { Mode = AdvancedBodyScalingMode.Manual },
        });

        var resolvedSource = ProfileManager.ResolveEditorProfileContext(new[] { source, lowerPriority }, template.UniqueId);

        Assert.Same(source, resolvedSource);
        var editorSettings = resolvedSource!.AdvancedBodyScalingOverrides.DeepCopy();
        Assert.NotSame(source.AdvancedBodyScalingOverrides, editorSettings);
        Assert.NotSame(source.AdvancedBodyScalingOverrides.Overrides, editorSettings.Overrides);

        source.AdvancedBodyScalingOverrides.Overrides.NaturalizationStrength = 0.9f;
        var effective = editorSettings.Resolve(new AdvancedBodyScalingSettings
        {
            Enabled = true,
            Mode = AdvancedBodyScalingMode.Strong,
            NaturalizationStrength = 1f,
        }, Race.Unknown);

        Assert.True(effective.Enabled);
        Assert.Equal(AdvancedBodyScalingMode.Automatic, effective.Mode);
        Assert.Equal(0.4f, effective.NaturalizationStrength);
        Assert.Null(editorSettings.Overrides.Enabled);
    }

    [Fact]
    public void ProfileEnabledOverride_OverridesDisabledGlobalDuringLiveEditing()
    {
        var effective = ResolveEditorEffectiveSettings(globalEnabled: false, profileEnabled: true, profileMode: null);

        Assert.True(effective.Enabled);
    }

    [Fact]
    public void ProfileDisabledOverride_OverridesEnabledGlobalDuringLiveEditing()
    {
        var effective = ResolveEditorEffectiveSettings(globalEnabled: true, profileEnabled: false, profileMode: null);

        Assert.False(effective.Enabled);
    }

    [Fact]
    public void InheritedEnabledState_RemainsDisabledWhenGlobalIsDisabledDuringLiveEditing()
    {
        var effective = ResolveEditorEffectiveSettings(globalEnabled: false, profileEnabled: null, profileMode: null);

        Assert.False(effective.Enabled);
    }

    [Fact]
    public void ClearedModeOverride_InheritsGlobalModeDuringLiveEditing()
    {
        var effective = ResolveEditorEffectiveSettings(globalEnabled: true, profileEnabled: null, profileMode: null);

        Assert.Equal(AdvancedBodyScalingMode.Strong, effective.Mode);
    }

    [Fact]
    public void DisabledWinningAssignment_DoesNotFallBackToLowerPriorityProfile()
    {
        var template = CreateTemplate();
        var winner = CreateProfile(template, new AdvancedBodyScalingProfileSettings());
        winner.DisabledTemplates.Add(template.UniqueId);
        var lowerPriority = CreateProfile(template, new AdvancedBodyScalingProfileSettings());

        var resolved = ProfileManager.ResolveEditorProfileContext(new[] { winner, lowerPriority }, template.UniqueId);

        Assert.Null(resolved);
    }

    [Fact]
    public void MissingWinningAssignment_DoesNotBorrowLowerPriorityProfileContext()
    {
        var editedTemplate = CreateTemplate();
        var winner = CreateProfile(CreateTemplate(), new AdvancedBodyScalingProfileSettings());
        var lowerPriority = CreateProfile(editedTemplate, new AdvancedBodyScalingProfileSettings());

        var resolved = ProfileManager.ResolveEditorProfileContext(new[] { winner, lowerPriority }, editedTemplate.UniqueId);

        Assert.Null(resolved);
    }

    [Fact]
    public void ContextSelection_DoesNotRetainThePreviousPreviewActorProfile()
    {
        var template = CreateTemplate();
        var actorAProfile = CreateProfile(template, new AdvancedBodyScalingProfileSettings
        {
            UseProfileOverrides = true,
            Overrides = new AdvancedBodyScalingOverrides
            {
                Mode = AdvancedBodyScalingMode.Automatic,
                NaturalizationStrength = 0.4f,
            },
        });
        var actorBProfile = CreateProfile(template, new AdvancedBodyScalingProfileSettings
        {
            UseProfileOverrides = true,
            Overrides = new AdvancedBodyScalingOverrides
            {
                Mode = AdvancedBodyScalingMode.Manual,
                NaturalizationStrength = 0.7f,
            },
        });

        var actorAContext = ProfileManager.ResolveEditorProfileContext(new[] { actorAProfile }, template.UniqueId);
        var actorBContext = ProfileManager.ResolveEditorProfileContext(new[] { actorBProfile }, template.UniqueId);

        Assert.Same(actorAProfile, actorAContext);
        Assert.Same(actorBProfile, actorBContext);
        Assert.Equal(AdvancedBodyScalingMode.Automatic, actorAContext!.AdvancedBodyScalingOverrides.Overrides.Mode);
        Assert.Equal(AdvancedBodyScalingMode.Manual, actorBContext!.AdvancedBodyScalingOverrides.Overrides.Mode);
        Assert.Equal(0.4f, actorAContext.AdvancedBodyScalingOverrides.Overrides.NaturalizationStrength);
        Assert.Equal(0.7f, actorBContext.AdvancedBodyScalingOverrides.Overrides.NaturalizationStrength);
    }

    private static AdvancedBodyScalingSettings ResolveEditorEffectiveSettings(
        bool globalEnabled,
        bool? profileEnabled,
        AdvancedBodyScalingMode? profileMode)
    {
        var template = CreateTemplate();
        var profile = CreateProfile(template, new AdvancedBodyScalingProfileSettings
        {
            UseProfileOverrides = true,
            Overrides = new AdvancedBodyScalingOverrides
            {
                Enabled = profileEnabled,
                Mode = profileMode,
            },
        });
        var source = ProfileManager.ResolveEditorProfileContext(new[] { profile }, template.UniqueId);
        Assert.NotNull(source);

        return source!.AdvancedBodyScalingOverrides.DeepCopy().Resolve(new AdvancedBodyScalingSettings
        {
            Enabled = globalEnabled,
            Mode = AdvancedBodyScalingMode.Strong,
        }, Race.Unknown);
    }

    private static Profile CreateProfile(Template template, AdvancedBodyScalingProfileSettings settings)
        => new()
        {
            Enabled = true,
            ProfileType = ProfileType.Normal,
            Priority = 100,
            AdvancedBodyScalingOverrides = settings,
            Templates = { template },
        };

    private static Template CreateTemplate()
        => new()
        {
            UniqueId = Guid.NewGuid(),
            Name = "Editor ABS context fixture",
        };
}
