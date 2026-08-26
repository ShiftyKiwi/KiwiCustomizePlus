// Copyright (c) Customize+.
// Licensed under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using CustomizePlus.Armatures.Data;
using CustomizePlus.Core.Data;
using CustomizePlus.Templates.Data;

namespace CustomizePlus.Core.Services;

internal enum BoneExplainabilityReasonCode
{
    BoneMissing,
    CapabilityMissing,
    CompatibilityDormant,
    ManualOnly,
    AutomationTrustInsufficient,
    UnknownCustom,
    ExplicitAuthority,
    AxisLocked,
    AxisPinned,
    NoModelInfluence,
    BIWAttenuated,
    SemanticBoundary,
    ClothingExcluded,
    PropExcluded,
    AppendageExcluded,
    BindingNotCurrent,
    AppearanceTransitionPending,
    NativeSafetyBlocked,
    NoContribution,
    SolverDisabled,
}

internal enum BoneTransformStageKind
{
    Scale,
    AdditiveDelta,
    Multiplier,
    Factor,
}

internal sealed record BoneTransformStage(string Name, Vector3 Value, string Detail, bool IsActive, BoneTransformStageKind Kind = BoneTransformStageKind.Scale);

internal sealed record BoneExplainabilityReport(
    string BoneName,
    string CanonicalName,
    string DisplayName,
    BoneMetadata Metadata,
    string? LiveParent,
    string? CuratedParent,
    string? Mirror,
    bool IsLive,
    bool IsExplicit,
    BoneTransform? ExplicitTransform,
    BoneTransform? ResolvedTransform,
    float? BoneImportance,
    IReadOnlyList<BoneTransformStage> Stages,
    IReadOnlyList<BoneExplainabilityReasonCode> Reasons,
    string Summary)
{
    public bool DidMove => ResolvedTransform is { } transform && transform.Scaling != Vector3.One;
}

// Keeps user-visible reason selection independently testable without constructing native armatures.
internal sealed record BoneExplainabilityDecisionInput(
    bool ArmatureAvailable,
    bool AppearanceTransitionPending,
    bool BindingCurrent,
    bool HasBindingIssue,
    bool BonePresent,
    bool CapabilityRequired,
    bool CapabilityPresent,
    bool CompatibilityDormant,
    BoneMetadata Metadata,
    bool ExplicitAuthority,
    bool AxisLocked,
    bool AxisPinned,
    bool SolverEnabled,
    bool ModelDerivedImportanceActive,
    float? BoneImportance,
    bool HasResolvedContribution);

/// <summary>
/// Builds a point-in-time explanation from the published managed armature state.
/// It never asks Havok for data and does not mutate template/profile state.
/// </summary>
public sealed class BoneExplainabilityService
{
    private readonly LocalBoneMetadataService _localMetadata;

    public BoneExplainabilityService(LocalBoneMetadataService localMetadata)
        => _localMetadata = localMetadata;

    internal BoneExplainabilityReport Explain(Armature? armature, Template? template, string boneName)
    {
        var canonical = BoneData.GetCanonicalBoneName(boneName);
        var metadata = BoneData.GetMetadata(canonical);
        BoneTransform? explicitTransform = null;
        template?.Bones.TryGetValue(canonical, out explicitTransform);
        if (explicitTransform == null)
            template?.Bones.TryGetValue(boneName, out explicitTransform);
        ModelBone? modelBone = null;
        var isLive = armature?.TryGetPublishedBone(canonical, out modelBone) == true
            || armature?.TryGetPublishedBone(boneName, out modelBone) == true;
        var resolved = armature?.GetAppliedBoneTransform(canonical) ?? armature?.GetAppliedBoneTransform(boneName);
        var explicitAuthority = armature?.IsExplicitTemplateTransform(canonical) == true || explicitTransform != null;
        var compatibilityDormant = armature != null && template != null
            && ProfileTransformResolver.Resolve(armature.Profile, armature.GetCapabilityManifestSnapshot()).TemplateApplicability
                .Any(item => item.TemplateId == template.UniqueId && item.Enabled && !item.Active);
        float? importance = null;
        if (armature?.ActiveBoneImportanceResult.Scores.TryGetValue(canonical, out var score) == true)
            importance = score;

        var reasons = BuildReasons(armature, metadata, modelBone, explicitTransform, explicitAuthority, compatibilityDormant, importance, canonical);
        var stages = BuildStages(armature, explicitTransform, resolved, canonical, importance);
        var summary = BuildSummary(metadata, resolved, explicitAuthority, reasons);
        return new BoneExplainabilityReport(
            boneName,
            canonical,
            _localMetadata.GetDisplayName(canonical),
            metadata,
            modelBone?.ParentBone?.BoneName,
            metadata.ExpectedParent,
            BoneData.GetBoneMirror(canonical),
            isLive,
            explicitAuthority,
            explicitTransform?.DeepCopy(),
            resolved?.DeepCopy(),
            importance,
            stages,
            reasons,
            summary);
    }

    private static IReadOnlyList<BoneExplainabilityReasonCode> BuildReasons(
        Armature? armature,
        BoneMetadata metadata,
        ModelBone? bone,
        BoneTransform? explicitTransform,
        bool explicitAuthority,
        bool compatibilityDormant,
        float? importance,
        string canonical)
        => EvaluateReasons(new BoneExplainabilityDecisionInput(
            ArmatureAvailable: armature != null,
            AppearanceTransitionPending: armature?.IsAwaitingAppearanceContextRebind == true,
            BindingCurrent: armature?.IsSkeletonBindingCurrent == true,
            HasBindingIssue: armature != null && !string.Equals(armature.LastSkeletonBindingIssue, "none", StringComparison.OrdinalIgnoreCase),
            BonePresent: bone != null,
            CapabilityRequired: GetRequiredCapability(metadata.Origin) != SkeletonCapability.None,
            CapabilityPresent: GetRequiredCapability(metadata.Origin) == SkeletonCapability.None || armature?.GetCapabilityManifestSnapshot().Has(GetRequiredCapability(metadata.Origin)) == true,
            CompatibilityDormant: compatibilityDormant,
            Metadata: metadata,
            ExplicitAuthority: explicitAuthority,
            AxisLocked: explicitTransform?.LockState == BoneLockState.Locked,
            AxisPinned: explicitTransform?.HasPinnedScaleAxes() == true,
            SolverEnabled: armature?.ActiveAdvancedBodyScalingSettings?.Enabled == true && armature.ActiveAdvancedBodyScalingSettings.Mode != AdvancedBodyScalingMode.Manual,
            ModelDerivedImportanceActive: armature?.ActiveBoneImportanceResult.ModelDerivedActive == true,
            BoneImportance: importance,
            HasResolvedContribution: armature?.ResolvedBoneTransforms.ContainsKey(canonical) == true || explicitTransform != null));

    internal static IReadOnlyList<BoneExplainabilityReasonCode> EvaluateReasons(BoneExplainabilityDecisionInput input)
    {
        var reasons = new List<BoneExplainabilityReasonCode>();
        if (!input.ArmatureAvailable)
            return [BoneExplainabilityReasonCode.BindingNotCurrent];
        if (input.AppearanceTransitionPending)
            reasons.Add(BoneExplainabilityReasonCode.AppearanceTransitionPending);
        if (!input.BindingCurrent)
            reasons.Add(BoneExplainabilityReasonCode.BindingNotCurrent);
        if (!input.BindingCurrent && input.HasBindingIssue)
            reasons.Add(BoneExplainabilityReasonCode.NativeSafetyBlocked);
        if (!input.BonePresent)
            reasons.Add(BoneExplainabilityReasonCode.BoneMissing);
        if (input.CapabilityRequired && !input.CapabilityPresent)
            reasons.Add(BoneExplainabilityReasonCode.CapabilityMissing);
        if (input.CompatibilityDormant)
            reasons.Add(BoneExplainabilityReasonCode.CompatibilityDormant);
        if (input.Metadata.Origin == BoneOrigin.UnknownCustom)
            reasons.Add(BoneExplainabilityReasonCode.UnknownCustom);
        if (input.Metadata.Trust == BoneAutomationTrust.ManualOnly)
            reasons.Add(BoneExplainabilityReasonCode.ManualOnly);
        if (!input.Metadata.HasTrust(BoneAutomationTrust.AdvancedCorrectiveSafe))
            reasons.Add(BoneExplainabilityReasonCode.AutomationTrustInsufficient);
        if (input.Metadata.Role == BoneFunctionalRole.ClothingRig)
            reasons.Add(BoneExplainabilityReasonCode.ClothingExcluded);
        if (input.Metadata.Role is BoneFunctionalRole.PropRig or BoneFunctionalRole.GearAttachment)
            reasons.Add(BoneExplainabilityReasonCode.PropExcluded);
        if (input.Metadata.Role == BoneFunctionalRole.ArticulatedAppendage)
            reasons.Add(BoneExplainabilityReasonCode.AppendageExcluded);
        if (input.Metadata.Role is BoneFunctionalRole.ClothingRig or BoneFunctionalRole.PropRig or BoneFunctionalRole.GearAttachment or BoneFunctionalRole.ArticulatedAppendage or BoneFunctionalRole.Unknown)
            reasons.Add(BoneExplainabilityReasonCode.SemanticBoundary);
        if (input.ExplicitAuthority)
            reasons.Add(BoneExplainabilityReasonCode.ExplicitAuthority);
        if (input.AxisLocked)
            reasons.Add(BoneExplainabilityReasonCode.AxisLocked);
        if (input.AxisPinned)
            reasons.Add(BoneExplainabilityReasonCode.AxisPinned);
        if (!input.SolverEnabled)
            reasons.Add(BoneExplainabilityReasonCode.SolverDisabled);
        if (input.ModelDerivedImportanceActive && input.BoneImportance.GetValueOrDefault() <= 0f)
            reasons.Add(BoneExplainabilityReasonCode.NoModelInfluence);
        if (input.ModelDerivedImportanceActive && input.BoneImportance is > 0f and < 0.20f)
            reasons.Add(BoneExplainabilityReasonCode.BIWAttenuated);
        if (!input.HasResolvedContribution)
            reasons.Add(BoneExplainabilityReasonCode.NoContribution);
        return reasons.Distinct().ToArray();
    }

    private static SkeletonCapability GetRequiredCapability(BoneOrigin origin)
        => origin switch
        {
            BoneOrigin.IVCS1 => SkeletonCapability.IVCS1,
            BoneOrigin.IVCS2 => SkeletonCapability.IVCS2,
            BoneOrigin.YAS => SkeletonCapability.YAS,
            BoneOrigin.NFLB => SkeletonCapability.NFLB,
            BoneOrigin.Skelomae => SkeletonCapability.Skelomae,
            _ => SkeletonCapability.None,
        };

    private static IReadOnlyList<BoneTransformStage> BuildStages(
        Armature? armature,
        BoneTransform? explicitTransform,
        BoneTransform? resolved,
        string boneName,
        float? importance)
    {
        var stages = new List<BoneTransformStage>
        {
            new("Baseline scale", Vector3.One, "Unmodified bone-space scale.", true),
        };
        if (explicitTransform != null)
            stages.Add(new("Explicit/resolved input scale", explicitTransform.Scaling, "User-authored transform; locks and pins remain authoritative.", explicitTransform.IsEdited(true)));

        var solver = armature?.DeformationQualityDiagnostics.Solver;
        if (solver?.StaticInputScales.TryGetValue(boneName, out var staticInput) == true && explicitTransform == null)
            stages.Add(new("Resolved static input scale", staticInput, "Static profile-resolver result before automatic body support and conditioning.", staticInput != Vector3.One));
        if (solver?.ContributionSources.TryGetValue(boneName, out var sources) == true)
        {
            foreach (var (source, label) in new[]
            {
                (DeformationContributionSource.AutomaticSupport, "Automatic support / transition"),
                (DeformationContributionSource.ProportionalBalance, "Proportional balance"),
                (DeformationContributionSource.SurfaceSmoothness, "Surface smoothness"),
                (DeformationContributionSource.LocalVolumeIntent, "Local volume intent"),
                (DeformationContributionSource.CrossSectionConditioning, "Cross-section conditioning"),
                (DeformationContributionSource.ShapeFairness, "Shape fairness"),
            })
            {
                if ((sources & source) != 0)
                {
                    var delta = solver.ContributionScaleDeltas.TryGetValue(boneName, out var stageDeltas)
                                && stageDeltas.TryGetValue(source, out var recordedDelta)
                        ? recordedDelta
                        : Vector3.Zero;
                    stages.Add(new(label, delta, "Recorded additive scale delta during the current static body-shaping rebuild.", true, BoneTransformStageKind.AdditiveDelta));
                }
            }
        }
        if (importance.HasValue)
            stages.Add(new("BIW attenuation factor", new Vector3(importance.Value), "A weighting factor for optional automatic work; it is not a transform and never overrides explicit rows.", importance.Value > 0f, BoneTransformStageKind.Factor));
        if (armature?.TryGetPoseCorrectiveScale(boneName, out var runtimeMultiplier) == true)
            stages.Add(new("Runtime pose multiplier", runtimeMultiplier, "Runtime pose support multiplies the static result only for eligible automatic receivers.", runtimeMultiplier != Vector3.One, BoneTransformStageKind.Multiplier));
        if (resolved != null)
            stages.Add(new("Static final scale", resolved.Scaling, "Published, safety-clamped static transform target. Runtime multipliers are shown separately when active.", resolved.IsEdited(true)));
        return stages;
    }

    private static string BuildSummary(BoneMetadata metadata, BoneTransform? resolved, bool explicitAuthority, IReadOnlyList<BoneExplainabilityReasonCode> reasons)
    {
        if (resolved?.IsEdited(true) == true)
            return explicitAuthority
                ? "This bone is shaped by an explicit template transform. Automatic systems preserve that authority."
                : "This bone has a published transform after the active profile and bounded automatic layers resolved.";
        if (reasons.Contains(BoneExplainabilityReasonCode.ClothingExcluded))
            return "Automatic body shaping deliberately stops before clothing controls.";
        if (reasons.Contains(BoneExplainabilityReasonCode.PropExcluded))
            return "Automatic body shaping deliberately stops before prop or gear controls.";
        if (metadata.Origin == BoneOrigin.UnknownCustom)
            return "This is an unknown/custom control. It remains manual-only until a curated registry update supports it.";
        return "No automatic or explicit contribution currently changes this bone.";
    }
}
