// Copyright (c) Customize+.
// Licensed under the MIT license.

using System;
using System.Linq;
using System.Text;
using CustomizePlus.Armatures.Services;
using CustomizePlus.Configuration.Data;
using CustomizePlus.Core.Data;
using CustomizePlus.Core.Extensions;
using CustomizePlus.Core.Helpers;
using CustomizePlus.Profiles;
using CustomizePlus.Templates;
using Dalamud.Plugin;

namespace CustomizePlus.Core.Services;

//Based on Penumbra's support log
public class SupportLogBuilderService
{
    private readonly PluginConfiguration _configuration;
    private readonly TemplateManager _templateManager;
    private readonly ProfileManager _profileManager;
    private readonly ArmatureManager _armatureManager;
    private readonly IDalamudPluginInterface _dalamudPluginInterface;
    private readonly PcpService _pcpService;

    public SupportLogBuilderService(
        PluginConfiguration configuration,
        TemplateManager templateManager,
        ProfileManager profileManager,
        ArmatureManager armatureManager,
        IDalamudPluginInterface dalamudPluginInterface,
        PcpService pcpService)
    {
        _configuration = configuration;
        _templateManager = templateManager;
        _profileManager = profileManager;
        _armatureManager = armatureManager;
        _dalamudPluginInterface = dalamudPluginInterface;
        _pcpService = pcpService;
    }

    public string BuildSupportLog()
    {
        var sb = new StringBuilder(102400); //it's fair to assume this will very often be quite large
        sb.AppendLine("**Settings**");
        sb.Append($"> **`Plugin Version:                 `** {VersionHelper.Version}\n");
        sb.Append($"> **`Commit Hash:                    `** {ThisAssembly.Git.Commit}+{ThisAssembly.Git.Sha}\n");
        sb.Append($"> **`Plugin enabled:                 `** {_configuration.PluginEnabled}\n");
        sb.AppendLine("**Settings -> Editor Settings**");
        sb.Append($"> **`Preview character (editor):     `** {_configuration.EditorConfiguration.PreviewCharacter.Incognito(null)}\n");
        sb.Append($"> **`Set preview character on login: `** {_configuration.EditorConfiguration.SetPreviewToCurrentCharacterOnLogin}\n");
        sb.Append($"> **`Root editing:                   `** {_configuration.EditorConfiguration.RootPositionEditingEnabled}\n");
        sb.AppendLine("**Settings -> Profile application**");
        sb.Append($"> **`Character window:               `** {_configuration.ProfileApplicationSettings.ApplyInCharacterWindow}\n");
        sb.Append($"> **`Try On:                         `** {_configuration.ProfileApplicationSettings.ApplyInTryOn}\n");
        sb.Append($"> **`Cards:                          `** {_configuration.ProfileApplicationSettings.ApplyInCards}\n");
        sb.Append($"> **`Inspect:                        `** {_configuration.ProfileApplicationSettings.ApplyInInspect}\n");
        sb.Append($"> **`Lobby:                          `** {_configuration.ProfileApplicationSettings.ApplyInLobby}\n");
        sb.AppendLine("**Dalamud**");
        sb.Append($"> **`Dalamud Version:                `** {_dalamudPluginInterface.GetDalamudVersion().Version}\n");
        sb.Append($"> **`Branch:                         `** {_dalamudPluginInterface.GetDalamudVersion().BetaTrack ?? "Release"}\n");
        sb.AppendLine("**Relevant plugins**");
        GatherRelevantPlugins(sb);
        sb.AppendLine("**Integrations**");
        sb.Append($"> **`Penumbra (PCP):                 `** {_configuration.IntegrationSettings.PenumbraPCPIntegrationEnabled} (Penumbra is{(!_pcpService.IsPenumbraAvailable ? " NOT " : " ")}available)\n");
        sb.AppendLine("**Settings -> Advanced Body Scaling**");
        var advanced = _configuration.AdvancedBodyScalingSettings;
        var retarget = advanced.FullIkRetargeting;
        var motionWarping = advanced.MotionWarping;
        var ik = advanced.FullBodyIk;
        sb.Append($"> **`Enabled:                        `** {advanced.Enabled}\n");
        sb.Append($"> **`Automation mode:                `** {advanced.Mode}\n");
        sb.Append($"> **`Animation-safe mode:            `** {advanced.AnimationSafeModeEnabled}\n");
        sb.Append($"> **`Proportional balance:           `** {advanced.ProportionalBalanceEnabled} (strength {advanced.ProportionalBalanceStrength:0.00})\n");
        sb.Append($"> **`Surface smoothness:            `** {advanced.SurfaceSmoothnessEnabled} (strength {advanced.SurfaceSmoothnessStrength:0.00})\n");
        sb.Append($"> **`Model-derived bone importance:  `** {advanced.ModelDerivedBoneImportanceEnabled} (prefer skin weights {advanced.PreferTrueSkinWeightImportance}, blend {advanced.BoneImportanceHeuristicBlend:0.00})\n");
        sb.Append($"> **`BIW full-quality actors:        `** self {advanced.FullBoneImportanceOnSelf}, profiled {advanced.FullBoneImportanceOnProfiledActors}; target/focus and nearby non-profiled actors use heuristic/cached fallback unless explicitly profiled\n");
        sb.Append($"> **`RBF pose-space correctives:     `** {advanced.PoseCorrectives.Enabled} (strength {advanced.PoseCorrectives.Strength:0.00}, sharpness {advanced.PoseCorrectives.PoseMapSharpness:0.00})\n");
        sb.Append($"> **`Corrective damping/clamp:       `** {advanced.PoseCorrectives.Damping:0.00} / {advanced.PoseCorrectives.MaxCorrectionClamp:0.000}\n");
        sb.Append($"> **`Corrective transition memory:   `** built-in hysteresis and pose-history smoothing\n");
        sb.Append($"> **`Full IK Retargeting:            `** {retarget.Enabled} (strength {retarget.GlobalStrength:0.00}, blend {retarget.BlendBias:0.00})\n");
        sb.Append($"> **`Retarget pelvis/spine:          `** {retarget.PelvisStrength:0.00} / {retarget.SpineStrength:0.00}\n");
        sb.Append($"> **`Retarget arms/legs/head:        `** {retarget.ArmStrength:0.00} / {retarget.LegStrength:0.00} / {retarget.HeadStrength:0.00}\n");
        sb.Append($"> **`Retarget reach/stride/posture:  `** {retarget.ReachAdaptationStrength:0.00} / {retarget.StrideAdaptationStrength:0.00} / {retarget.PosturePreservationStrength:0.00}\n");
        sb.Append($"> **`Retarget safety/clamp:          `** {retarget.MotionSafetyBias:0.00} / {retarget.MaxCorrectionClamp:0.00}\n");
        sb.Append($"> **`Motion Warping:                 `** {motionWarping.Enabled} ({AdvancedBodyScalingMotionWarpingSystem.GetImplementationTierLabel()}, strength {motionWarping.GlobalStrength:0.00}, blend {motionWarping.BlendBias:0.00})\n");
        sb.Append($"> **`Motion stride/orient/posture:   `** {motionWarping.StrideWarpStrength:0.00} / {motionWarping.OrientationWarpStrength:0.00} / {motionWarping.PostureWarpStrength:0.00}\n");
        sb.Append($"> **`Motion safety/clamp:            `** {motionWarping.MotionSafetyBias:0.00} / {motionWarping.MaxCorrectionClamp:0.00}\n");
        sb.Append($"> **`Full-Body IK:                   `** {ik.Enabled} (strength {ik.GlobalStrength:0.00}, iterations {ik.IterationCount}, tolerance {ik.ConvergenceTolerance:0.000})\n");
        sb.Append($"> **`IK pelvis/spine:                `** {ik.PelvisCompensationStrength:0.00} / {ik.SpineRedistributionStrength:0.00}\n");
        sb.Append($"> **`IK arms/legs/head:              `** {ik.ArmStrength:0.00} / {ik.LegStrength:0.00} / {ik.HeadAlignmentStrength:0.00}\n");
        sb.Append($"> **`IK grounding/safety/clamp:      `** {ik.GroundingBias:0.00} / {ik.MotionSafetyBias:0.00} / {ik.MaxCorrectionClamp:0.00}\n");
        sb.AppendLine("**Templates**");
        sb.Append($"> **`Count:                          `** {_templateManager.Templates.Count}\n");
        foreach (var template in _templateManager.Templates)
        {
            sb.Append($">   > **`{template.ToString(),-32}`**\n");
        }
        sb.AppendLine("**Profiles**");
        sb.Append($"> **`Default profile:                `** {_profileManager.DefaultProfile?.ToString() ?? "None"}\n");
        sb.Append($"> **`Default local player profile:   `** {_profileManager.DefaultLocalPlayerProfile?.ToString() ?? "None"}\n");
        sb.Append($"> **`Count:                          `** {_profileManager.Profiles.Count}\n");
        foreach (var profile in _profileManager.Profiles)
        {
            sb.Append($">   > =====\n");
            sb.Append($">   > **`{profile.ToString(),-32}`*\n");
            sb.Append($">   > **`Name:                       `** {profile.Name.Text.Incognify()}\n");
            sb.Append($">   > **`Type:                       `** {profile.ProfileType} \n");
            sb.Append($">   > **`Characters:             `** {string.Join(',', profile.Characters.Select(x => x.Incognito(null)))}\n");
            sb.Append($">   > **`Templates:`**\n");
            sb.Append($">   >   > **`Count:                  `** {profile.Templates.Count}\n");
            foreach (var template in profile.Templates)
            {
                sb.Append($">   >   > **`{template.ToString(),-32}`**\n");

                var requirement = profile.GetTemplateCompatibilityRequirement(template.UniqueId);
                sb.Append($">   >   >   > **`Compatibility:              `** {requirement.ToDisplayString()}\n");

                if (profile.DisabledTemplates.Contains(template.UniqueId))
                    sb.Append($">   >   >   >  **`Disabled`**\n");
            }
            sb.Append($">   > **`Armatures:`**\n");
            sb.Append($">   >   > **`Count:                  `** {profile.Armatures.Count}\n");
            foreach (var armature in profile.Armatures)
            {
                sb.Append($">   >   > **`{armature.ToString(),-32}`**\n");
            }
            sb.Append($">   > =====\n");
        }
        sb.AppendLine("**Armatures**");
        sb.Append($"> **`Count:                          `** {_armatureManager.Armatures.Count}\n");
        foreach (var kvPair in _armatureManager.Armatures)
        {
            var identifier = kvPair.Key;
            var armature = kvPair.Value;
            sb.Append($">   > =====\n");
            sb.Append($">   > **`{armature.ToString(),-32}`**\n");
            sb.Append($">   > **`Actor:                      `** {armature.ActorIdentifier.Incognito(null) ?? "None"}\n");
            sb.Append($">   > **`Built:                      `** {armature.IsBuilt}\n");
            sb.Append($">   > **`Armature revision:          `** {armature.SkeletonRevision}\n");
            sb.Append($">   > **`Native binding generation:  `** {armature.NativeBindingGeneration}\n");
            sb.Append($">   > **`Actor lifetime generation: `** {armature.ActorLifetimeGeneration}; reacquisition pending={armature.IsAwaitingActorReacquisitionPublication}\n");
            sb.Append($">   > **`Template binding revision: `** {armature.TemplateBindingRevision}\n");
            sb.Append($">   > **`Profile resolution revision:`** {armature.ProfileResolutionRevision}\n");
            sb.Append($">   > **`Deformation revision:     `** {armature.DeformationRevision}\n");
            sb.Append($">   > **`Diagnostics revision:     `** {armature.DiagnosticsRevision}\n");
            sb.Append($">   > **`Skeleton binding current:   `** {armature.IsSkeletonBindingCurrent}\n");
            sb.Append($">   > **`Visible:                    `** {armature.IsVisible}\n");
            sb.Append($">   > **`Pending rebind:             `** {armature.IsPendingProfileRebind}\n");
            sb.Append($">   > **`Last seen:                  `** {armature.LastSeen}\n");
            sb.Append($">   > **`Profile:                    `** {armature.Profile?.ToString() ?? "None"}\n");
            sb.Append($">   > **`Resolved transforms:        `** {armature.ResolvedBoneTransforms.Count}\n");
            sb.Append($">   > **`Active ModelBones:          `** {armature.ActiveBones.Count}\n");
            var manifest = armature.GetCapabilityManifestSnapshot();
            sb.Append($">   > **`Manifest revision:           `** {manifest.Revision}\n");
            sb.Append($">   > **`Manifest fingerprint:        `** {(string.IsNullOrWhiteSpace(manifest.StructuralFingerprint) ? "Unavailable" : manifest.StructuralFingerprint)}\n");
            sb.Append($">   > **`Capabilities:               `** {string.Join(", ", manifest.CapabilityEvidence.OrderBy(static pair => pair.Key).Select(static pair => $"{pair.Key}={pair.Value.State}"))}\n");
            if (armature.Profile is { } profile)
            {
                var applicability = ProfileTransformResolver.Resolve(profile, manifest).TemplateApplicability;
                foreach (var item in applicability)
                    sb.Append($">   > **`Template applicability:      `** {item.TemplateName}: {(item.Active ? "Active" : "Dormant")} ({item.Requirement.ToDisplayString()}; {item.Reason})\n");
            }
            else
            {
                sb.Append($">   > **`Template applicability:      `** none (no resolved profile)\n");
            }
            sb.Append($">   > **`Bone importance source:     `** {armature.ActiveBoneImportanceResult.SourceLabel} ({armature.ActiveBoneImportanceResult.StageLabel})\n");
            sb.Append($">   > **`Bone importance resolve:    `** {armature.ActiveBoneImportanceResult.ResolutionLabel}\n");
            sb.Append($">   > **`Bone importance mode:       `** {armature.ActiveBoneImportanceResult.AggregateModeLabel} ({armature.ActiveBoneImportanceResult.ContributingPartCount} contributing part{(armature.ActiveBoneImportanceResult.ContributingPartCount == 1 ? string.Empty : "s")})\n");
            sb.Append($">   > **`Bone importance runtime:    `** {armature.ActiveBoneImportanceResult.VisibleRuntimeModeLabel} on {armature.ActiveBoneImportanceResult.VisibleActorTierLabel} (full eligible {armature.ActiveBoneImportanceResult.VisibleFullQualityEligible}, downgraded {armature.ActiveBoneImportanceResult.VisibleCrowdSafeDowngraded}, stable-throttled {armature.ActiveBoneImportanceResult.VisibleStableThrottled})\n");
            sb.Append($">   > **`Bone importance cache:      `** {(armature.ActiveBoneImportanceResult.CacheHit ? "Hit" : "Miss / not cached")}\n");
            if (!string.IsNullOrWhiteSpace(armature.ActiveBoneImportanceResult.RefreshStatus))
                sb.Append($">   > **`Bone importance refresh:    `** {armature.ActiveBoneImportanceResult.RefreshStatus}\n");
            sb.Append($">   > **`Bone importance refine:     `** area-aware {armature.ActiveBoneImportanceResult.AreaAwareRefinementActive}, classification-aware {armature.ActiveBoneImportanceResult.ClassificationRefinementActive}, confidence-weighted {armature.ActiveBoneImportanceResult.ConfidenceWeightedAggregationActive}\n");
            if (!string.IsNullOrWhiteSpace(armature.ActiveBoneImportanceResult.ConfidenceSummary))
                sb.Append($">   > **`Bone importance trust:      `** {armature.ActiveBoneImportanceResult.ConfidenceSummary}\n");
            if (!string.IsNullOrWhiteSpace(armature.ActiveBoneImportanceResult.VisibleRuntimeSummary))
                sb.Append($">   > **`Bone importance runtime dt: `** {armature.ActiveBoneImportanceResult.VisibleRuntimeSummary}\n");
            sb.Append($">   > **`Bone importance applied:    `** {armature.BoneImportanceAppliedToPipeline}\n");
            var quality = armature.DeformationQualityDiagnostics;
            var solver = quality.Solver;
            sb.Append($">   > **`Body-support regions:      `** {(solver.ActiveRegions.Count == 0 ? "none" : string.Join(", ", solver.ActiveRegions))}\n");
            sb.Append($">   > **`Automatic support:         `** primary={solver.PrimaryContributionCount}; support={solver.SupportContributionCount}; transition={solver.TransitionContributionCount}; secondary={solver.SecondaryContributionCount}; secondary-magnitude={solver.SecondaryContributionMagnitude:0.000}\n");
            sb.Append($">   > **`Body-support safeguards:   `** bilateral-normalized={solver.BilateralNormalizationCount}; duplicate-suppressed={solver.DoubleContributionPreventionCount}; clamped={solver.ClampedContributionCount}; fallback={solver.FallbackCount}\n");
            sb.Append($">   > **`Proportional balance:      `** enabled={solver.ProportionalBalanceEnabled}; strength={solver.ProportionalBalanceStrength:0.00}; relationships={(solver.CorrectedRelationships.Count == 0 ? "none" : string.Join(", ", solver.CorrectedRelationships))}; max-correction={solver.MaximumProportionalCorrection:0.000}; skipped-explicit={solver.ProportionalSkippedExplicitOrLockedCount}\n");
            sb.Append($">   > **`Surface smoothness:        `** enabled={solver.SurfaceSmoothnessEnabled}; strength={solver.SurfaceSmoothnessStrength:0.00}; affected={solver.SurfaceSmoothnessAffectedBoneCount}; regions={(solver.SurfaceSmoothnessRegions.Count == 0 ? "none" : string.Join(", ", solver.SurfaceSmoothnessRegions))}; gradient={solver.MaximumPreSmoothingGradient:0.000}->{solver.MaximumPostSmoothingGradient:0.000}; boundary-skips={solver.SurfaceSmoothnessSkippedBoundaryCount}; magnitude-error={solver.SurfaceMagnitudePreservationError:0.000}\n");
            sb.Append($">   > **`Body-shaping quality:     `** bilateral={quality.MaxBilateralDifference:0.000} ({quality.MaxBilateralPair}); continuity={quality.MaxContinuityDifference:0.000} ({quality.MaxContinuityBoundary}); proportional={quality.ProportionalImbalanceScore:0.000}; gradient={quality.SurfaceGradientScore:0.000}\n");
            sb.Append($">   > **`M6 NFLB:                   `** automated-body={solver.AutomatedNflbBodyControls}; automated-clothing=0; automated-props=0\n");
            sb.Append($">   > **`M7 Skelomae:               `** automated-body={solver.AutomatedSkelomaeBodyControls}; automated-tongue=0; automated-wings=0\n");
            foreach (var timing in armature.PerformanceMetrics.Snapshot())
                sb.Append($">   > **`Timing {timing.Stage,-22}`** latest={timing.LatestMilliseconds:0.000}ms; avg={timing.AverageMilliseconds:0.000}ms; max={timing.MaxMilliseconds:0.000}ms; samples={timing.Samples}\n");
            if (!string.IsNullOrWhiteSpace(armature.ActiveBoneImportanceResult.ModelIdentity))
                sb.Append($">   > **`Bone importance model:      `** {armature.ActiveBoneImportanceResult.ModelIdentity}\n");
            if (!string.IsNullOrWhiteSpace(armature.ActiveBoneImportanceResult.ModelSignature))
                sb.Append($">   > **`Bone importance signature:  `** {armature.ActiveBoneImportanceResult.ModelSignature}\n");
            if (!string.IsNullOrWhiteSpace(armature.ActiveBoneImportanceResult.ResolutionDetail))
                sb.Append($">   > **`Bone importance detail:     `** {armature.ActiveBoneImportanceResult.ResolutionDetail}\n");
            if (!string.IsNullOrWhiteSpace(armature.ActiveBoneImportanceResult.RequestedGamePath))
                sb.Append($">   > **`Bone importance requested:  `** {armature.ActiveBoneImportanceResult.RequestedGamePath}\n");
            if (!string.IsNullOrWhiteSpace(armature.ActiveBoneImportanceResult.ModelPath))
                sb.Append($">   > **`Bone importance path:       `** {armature.ActiveBoneImportanceResult.ModelPath}\n");
            if (!string.IsNullOrWhiteSpace(armature.ActiveBoneImportanceResult.Summary))
                sb.Append($">   > **`Bone importance summary:    `** {armature.ActiveBoneImportanceResult.Summary}\n");
            if (!string.IsNullOrWhiteSpace(armature.ActiveBoneImportanceResult.RefinementSummary))
                sb.Append($">   > **`Bone importance refine det: `** {armature.ActiveBoneImportanceResult.RefinementSummary}\n");
            if (!string.IsNullOrWhiteSpace(armature.ActiveBoneImportanceResult.ResolutionTrace))
                sb.Append($">   > **`Bone importance trace:      `** {armature.ActiveBoneImportanceResult.ResolutionTrace}\n");
            foreach (var part in armature.ActiveBoneImportanceResult.PartDetails)
                sb.Append($">   > **`Bone importance part:       `** {part}\n");
            foreach (var missing in armature.ActiveBoneImportanceResult.MissingPartDetails)
                sb.Append($">   > **`Bone importance missing:    `** {missing}\n");
            foreach (var sample in armature.ActiveBoneImportanceResult.SampleValues.Take(4))
                sb.Append($">   > **`Bone importance sample:     `** {sample}\n");
            sb.Append($">   > **`Bone template bindings:`**\n");
            foreach (var bindingKvPair in armature.BoneTemplateBinding)
            {
                sb.Append($">   >   > **`{BoneData.GetBoneDisplayName(bindingKvPair.Key)} ({bindingKvPair.Key}) -> {bindingKvPair.Value.ToString()}`**\n");
            }
            sb.Append($">   > =====\n");
        }

        var lifecycleTrace = _armatureManager.GetDebugSelfLifecycleTrace();
        if (lifecycleTrace.Count > 0)
        {
            sb.AppendLine("**Self Armature Lifecycle Trace (Debug)**");
            foreach (var entry in lifecycleTrace)
                sb.Append($"> **`{entry.ToSupportLine()}`**\n");
        }

        return sb.ToString();
    }


    private void GatherRelevantPlugins(StringBuilder sb)
    {
        ReadOnlySpan<string> relevantPlugins =
        [
            "MareSynchronos", "Ktisis", "Brio", "DynamicBridge", "SimpleHeels",
            "IllusioVitae", "LoporritSync", "AQuestReborn", "RoleplayingVoiceDalamud", "AetherRemote",
            "CustomizePlusPlus", "CharacterSelectPlugin"
        ];
        var plugins = _dalamudPluginInterface.InstalledPlugins
            .GroupBy(p => p.InternalName)
            .ToDictionary(g => g.Key, g =>
            {
                var item = g.OrderByDescending(p => p.IsLoaded).ThenByDescending(p => p.Version).First();
                return (item.IsLoaded, item.Version, item.Name);
            });
        foreach (var plugin in relevantPlugins)
        {
            if (plugins.TryGetValue(plugin, out var data))
                sb.Append($"> **`{data.Name + ':',-32}`** {data.Version}{(data.IsLoaded ? string.Empty : " (Disabled)")}\n");
        }
    }
}
