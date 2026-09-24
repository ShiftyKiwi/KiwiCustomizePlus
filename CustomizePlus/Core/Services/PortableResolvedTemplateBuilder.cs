// Copyright (c) Customize+.
// Licensed under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using CustomizePlus.Armatures.Data;
using CustomizePlus.Core.Data;
using CustomizePlus.Profiles.Data;
using CustomizePlus.Templates.Data;

namespace CustomizePlus.Core.Services;

/// <summary>
/// Creates a normal, portable template from the already-published static output
/// of the hardened runtime resolver. This deliberately consumes the validated
/// armature snapshot instead of discovering or reading native pose state itself.
/// </summary>
public sealed class PortableResolvedTemplateBuilder
{
    internal PortableResolvedTemplateBuildResult TryBuild(Profile profile, IEnumerable<Armature> armatures)
    {
        var matchingArmatures = armatures
            .Where(armature => armature.Profile.UniqueId == profile.UniqueId)
            .ToList();

        return matchingArmatures.Count switch
        {
            0 => PortableResolvedTemplateBuildResult.Unavailable(
                "No live armature is currently using this profile. Apply it to one stable actor before copying its resolved shape."),
            1 => TryBuild(profile, matchingArmatures[0]),
            _ => PortableResolvedTemplateBuildResult.Unavailable(
                "This profile is active on multiple actors. Its race- and model-dependent static result is ambiguous, so no resolved template was copied."),
        };
    }

    internal PortableResolvedTemplateBuildResult TryBuild(Profile profile, Armature armature)
    {
        if (armature.Profile.UniqueId != profile.UniqueId)
            return PortableResolvedTemplateBuildResult.Unavailable("The selected profile does not match the live armature used for export.");

        if (!armature.IsBuilt || !armature.IsSkeletonBindingCurrent || armature.IsAwaitingAppearanceContextRebind)
            return PortableResolvedTemplateBuildResult.Unavailable(
                "Waiting for a stable validated skeleton binding before copying the resolved shape.");

        var settings = armature.ActiveAdvancedBodyScalingSettings;
        if (settings == null || !settings.Enabled || settings.Mode == AdvancedBodyScalingMode.Manual)
            return PortableResolvedTemplateBuildResult.Unavailable(
                "Advanced Body Scaling must be enabled in a non-manual mode for this profile before copying its resolved shape.");

        return BuildFromStaticTransforms(profile, armature.ResolvedBoneTransforms);
    }

    /// <summary>
    /// Produces the portable, ordinary-template form of an already-resolved static snapshot.
    /// Kept separate from live-armature selection so IPC can reuse this exact flattening path later.
    /// </summary>
    internal static PortableResolvedTemplateBuildResult BuildFromStaticTransforms(
        Profile profile,
        IReadOnlyDictionary<string, BoneTransform> staticTransforms)
    {
        if (staticTransforms.Count == 0)
            return PortableResolvedTemplateBuildResult.Unavailable("The stable resolver produced no portable bone transforms for this profile.");

        var now = DateTimeOffset.UtcNow;
        var template = new Template
        {
            Name = $"{profile.Name.Text} [Resolved]",
            CreationDate = now,
            ModifiedDate = now,
            UniqueId = Guid.NewGuid(),
            IsWriteProtected = false,
            Bones = staticTransforms.ToDictionary(
                static pair => pair.Key,
                static pair => pair.Value.DeepCopy(),
                StringComparer.Ordinal),
        };

        return PortableResolvedTemplateBuildResult.Succeeded(template);
    }
}

internal sealed record PortableResolvedTemplateBuildResult(Template? Template, string Reason)
{
    public bool Success => Template != null;

    public static PortableResolvedTemplateBuildResult Succeeded(Template template)
        => new(template, string.Empty);

    public static PortableResolvedTemplateBuildResult Unavailable(string reason)
        => new(null, reason);
}
