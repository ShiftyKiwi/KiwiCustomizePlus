// Copyright (c) Customize+.
// Licensed under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using CustomizePlus.Armatures.Data;
using CustomizePlus.Profiles.Data;

namespace CustomizePlus.Core.Data;

/// <summary>Describes whether an inspector value is directly retained, formatted from retained state, or unavailable.</summary>
internal enum ActorEffectiveStateFactKind
{
    Authoritative,
    DerivedPresentation,
    Unavailable,
}

/// <summary>Immutable text suitable for a read-only inspector row.</summary>
internal sealed record ActorEffectiveStateFact(string Value, ActorEffectiveStateFactKind Kind, string Detail = "");

/// <summary>Configured template-assignment information. This never claims per-bone ownership.</summary>
internal sealed record ActorEffectiveTemplateAssignmentSnapshot(
    string Name,
    Guid Id,
    bool Enabled,
    float Weight,
    int SavedTransformCount,
    string RequiredCapabilities);

/// <summary>Current, compact runtime-layer state retained by the published armature.</summary>
internal sealed record ActorEffectiveRuntimeLayerSnapshot(
    string Name,
    bool Enabled,
    bool Active,
    string Summary);

/// <summary>Read-only capability state copied from the published capability manifest.</summary>
internal sealed record ActorEffectiveCapabilitySnapshot(string Name, string State);

/// <summary>
/// Immutable presentation of state production has already published for one armature. This class
/// deliberately observes an existing armature only; it never performs actor/profile/ABS resolution.
/// </summary>
internal sealed record ActorEffectiveStateSnapshot(
    bool IsAvailable,
    ActorEffectiveStateFact Actor,
    ActorEffectiveStateFact Representation,
    ActorEffectiveStateFact WinningProfile,
    ActorEffectiveStateFact ProfileMatchReason,
    ActorEffectiveStateFact TemplateParticipation,
    ActorEffectiveStateFact PerBoneTemplateProvenance,
    ActorEffectiveStateFact EffectiveAdvancedBodyScaling,
    ActorEffectiveStateFact RacePresetProvenance,
    ActorEffectiveStateFact Binding,
    ActorEffectiveStateFact CapabilityState,
    ActorEffectiveStateFact BoneImportance,
    string ProfileName,
    Guid ProfileId,
    bool ProfileEnabled,
    int ProfilePriority,
    bool ProfileUsesOverrides,
    int ProfileOverrideCount,
    bool GlobalAdvancedBodyScalingEnabled,
    string GlobalAdvancedBodyScalingMode,
    bool? EffectiveAdvancedBodyScalingEnabled,
    string EffectiveAdvancedBodyScalingMode,
    float? EffectiveSurfaceBalancingStrength,
    float? EffectiveMassRedistributionStrength,
    float? EffectiveNaturalizationStrength,
    string EffectiveGuardrailMode,
    string EffectivePoseValidationMode,
    long ArmatureRevision,
    long NativeBindingGeneration,
    long ProfileResolutionRevision,
    long DeformationRevision,
    int ResolvedTransformCount,
    int BoundModelBoneCount,
    int PendingPublicationObservations,
    IReadOnlyList<ActorEffectiveTemplateAssignmentSnapshot> TemplateAssignments,
    IReadOnlyList<ActorEffectiveCapabilitySnapshot> Capabilities,
    IReadOnlyList<ActorEffectiveRuntimeLayerSnapshot> RuntimeLayers,
    IReadOnlyList<OptionalLayerHealthSnapshot> OptionalLayerHealth,
    IReadOnlyList<RuntimeTimingSummary> RuntimeTimings,
    long StaleBindingSkips,
    long UnsafeTransformSkips)
{
    public static ActorEffectiveStateSnapshot Unavailable(string reason)
        => new(
            false,
            UnavailableFact(reason),
            UnavailableFact("No published representation selection is available."),
            UnavailableFact("No winning profile is attached to a published armature."),
            UnavailableFact("Profile candidate/rejection history is not retained."),
            UnavailableFact("No published template stack is available."),
            UnavailableFact("Per-bone source-template ownership is not retained under weighted blending."),
            UnavailableFact("No effective Advanced Body Scaling settings are published."),
            UnavailableFact("The applied race/preset stage is not retained separately."),
            UnavailableFact("No binding state is available."),
            UnavailableFact("No capability manifest is available."),
            UnavailableFact("No Bone Importance state is available."),
            string.Empty,
            Guid.Empty,
            false,
            0,
            false,
            0,
            false,
            "Unavailable",
            null,
            "Unavailable",
            null,
            null,
            null,
            "Unavailable",
            "Unavailable",
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            Array.Empty<ActorEffectiveTemplateAssignmentSnapshot>(),
            Array.Empty<ActorEffectiveCapabilitySnapshot>(),
            Array.Empty<ActorEffectiveRuntimeLayerSnapshot>(),
            Array.Empty<OptionalLayerHealthSnapshot>(),
            Array.Empty<RuntimeTimingSummary>(),
            0,
            0);

    /// <summary>
    /// Freezes the state already held by a published armature. It does not call any resolver or
    /// inspect live actor objects, so refreshing this snapshot cannot drive a lifecycle transition.
    /// </summary>
    public static ActorEffectiveStateSnapshot Capture(Armature? armature, AdvancedBodyScalingSettings globalSettings)
    {
        if (armature == null)
            return Unavailable("No published armature is currently available to inspect.");

        var profile = armature.Profile;
        var manifest = armature.GetCapabilityManifestSnapshot();
        var effective = armature.ActiveAdvancedBodyScalingSettings;
        var overrideSummary = AdvancedBodyScalingOverridePresentation.Summarize(profile.AdvancedBodyScalingOverrides.Overrides);
        var nativeWrites = armature.GetDebugNativeWriteDiagnostics();
        var boneImportance = armature.ActiveBoneImportanceResult;

        var assignments = profile.Templates
            .Select(template => new ActorEffectiveTemplateAssignmentSnapshot(
                template.Name.Text,
                template.UniqueId,
                !profile.DisabledTemplates.Contains(template.UniqueId),
                profile.GetTemplateWeight(template.UniqueId),
                template.Bones.Count,
                profile.GetTemplateCompatibilityRequirement(template.UniqueId).RequiredAll.ToString()))
            .ToArray();

        var capabilities = manifest.CapabilityEvidence
            .OrderBy(static pair => pair.Key)
            .Select(static pair => new ActorEffectiveCapabilitySnapshot(pair.Key.ToString(), pair.Value.State.ToString()))
            .ToArray();

        var runtimeLayers = new[]
        {
            new ActorEffectiveRuntimeLayerSnapshot("Pose-space correctives", armature.PoseCorrectiveDebugState.Enabled, armature.PoseCorrectiveDebugState.Active, armature.PoseCorrectiveDebugState.Summary),
            new ActorEffectiveRuntimeLayerSnapshot("Pose-aware joint correctives", armature.PoseAwareJointCorrectiveDebugState.Enabled, armature.PoseAwareJointCorrectiveDebugState.Active, armature.PoseAwareJointCorrectiveDebugState.Summary),
            new ActorEffectiveRuntimeLayerSnapshot("Full IK retargeting", armature.FullIkRetargetingDebugState.Enabled, armature.FullIkRetargetingDebugState.Active, armature.FullIkRetargetingDebugState.Summary),
            new ActorEffectiveRuntimeLayerSnapshot("Motion warping", armature.MotionWarpingDebugState.Enabled, armature.MotionWarpingDebugState.Active, armature.MotionWarpingDebugState.Summary),
            new ActorEffectiveRuntimeLayerSnapshot("Full-body IK", armature.FullBodyIkDebugState.Enabled, armature.FullBodyIkDebugState.Active, armature.FullBodyIkDebugState.Summary),
        };

        var binding = armature.IsSkeletonBindingCurrent
            ? new ActorEffectiveStateFact("Current", ActorEffectiveStateFactKind.Authoritative)
            : new ActorEffectiveStateFact("Waiting", ActorEffectiveStateFactKind.Authoritative, armature.LastSkeletonBindingIssue);
        var absValue = effective == null
            ? UnavailableFact("No effective Advanced Body Scaling settings have been published for this armature.")
            : new ActorEffectiveStateFact(
                effective.Enabled
                    ? $"Enabled ({effective.Mode})"
                    : "Disabled",
                ActorEffectiveStateFactKind.Authoritative,
                "This is the effective settings copy published by the armature after production resolution.");

        return new ActorEffectiveStateSnapshot(
            true,
            new ActorEffectiveStateFact(armature.ActorIdentifier.ToString(), ActorEffectiveStateFactKind.Authoritative),
            UnavailableFact("The published armature does not retain the selected object index or representation context."),
            new ActorEffectiveStateFact(profile.Name.Text, ActorEffectiveStateFactKind.Authoritative, "This profile is already attached to the published armature."),
            UnavailableFact("The profile matcher retains the selected winner, not the full candidate/rejection history."),
            new ActorEffectiveStateFact(
                $"{assignments.Length} configured assignment(s); {armature.ResolvedBoneTransforms.Count} resolved static transform(s).",
                ActorEffectiveStateFactKind.DerivedPresentation,
                "Assignment enabled state and weight are configured values. Per-template runtime applicability is not retained."),
            UnavailableFact("Weighted template blending does not retain definitive per-bone source ownership."),
            absValue,
            UnavailableFact("The published effective settings do not retain which race/preset stage supplied each value."),
            binding,
            new ActorEffectiveStateFact(
                manifest.BindingCurrent ? "Published / current" : "Published / waiting for current binding",
                ActorEffectiveStateFactKind.Authoritative,
                $"Manifest revision {manifest.Revision}; {manifest.StableObservations} stable observation(s)."),
            new ActorEffectiveStateFact(
                $"{boneImportance.LiveSourceLabel}; {boneImportance.VisibleRuntimeModeLabel}; {boneImportance.VisibleActorTierLabel}",
                ActorEffectiveStateFactKind.Authoritative,
                boneImportance.VisibleRuntimeSummary),
            profile.Name.Text,
            profile.UniqueId,
            profile.Enabled,
            profile.Priority,
            profile.AdvancedBodyScalingOverrides.UseProfileOverrides,
            overrideSummary.TotalCount,
            globalSettings.Enabled,
            globalSettings.Mode.ToString(),
            effective?.Enabled,
            effective?.Mode.ToString() ?? "Unavailable",
            effective?.SurfaceBalancingStrength,
            effective?.MassRedistributionStrength,
            effective?.NaturalizationStrength,
            effective?.GuardrailMode.ToString() ?? "Unavailable",
            effective?.PoseValidationMode.ToString() ?? "Unavailable",
            armature.ArmatureRevision,
            armature.NativeBindingGeneration,
            armature.ProfileResolutionRevision,
            armature.DeformationRevision,
            armature.ResolvedBoneTransforms.Count,
            armature.BoundModelBoneCount,
            armature.PendingPublicationObservations,
            assignments,
            capabilities,
            runtimeLayers,
            armature.GetOptionalLayerHealthSnapshot(),
            armature.PerformanceMetrics.Snapshot(),
            nativeWrites.SkippedStaleBinding,
            nativeWrites.SkippedUnsafeTransform);
    }

    private static ActorEffectiveStateFact UnavailableFact(string detail)
        => new("Unavailable", ActorEffectiveStateFactKind.Unavailable, detail);
}
