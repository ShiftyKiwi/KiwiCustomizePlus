// Copyright (c) Customize+.
// Licensed under the MIT license.

#if DEBUG
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using CustomizePlus.Armatures.Data;
using CustomizePlus.Armatures.Services;
using CustomizePlus.Configuration.Data;
using CustomizePlus.Core.Data;
using CustomizePlus.Templates;
using Dalamud.Plugin;
using Franthropy.Dalamud.AgentBridge;
using OtterGui.Classes;
using OtterGui.Log;

namespace CustomizePlus.Core.Services;

/// <summary>
/// Development-only, read-only bridge for obtaining a cached diagnostic snapshot.
/// It deliberately consumes published armature state and never walks or mutates native skeleton data.
/// </summary>
internal sealed class CustomizePlusAgentBridgeService : IDisposable
{
    private const int SnapshotRefreshIntervalMs = 500;
    private const int DebugReviewRefreshIntervalMs = 250;
    private const string PoseValidationSurfaceId = "debug.pose-corrective-validation";
    private const string PoseValidationActionId = "debug.run-pose-corrective-validation";

    private readonly ArmatureManager _armatureManager;
    private readonly FrameworkManager _framework;
    private readonly PluginConfiguration _configuration;
    private readonly Logger _logger;
    private readonly RuntimeEvidenceService _runtimeEvidence;
    private readonly TemplateEditorManager _templateEditorManager;
    private readonly LocalBoneMetadataService _localBoneMetadata;
    private readonly AgentBridgeHost _host;
    private readonly AgentBridgeUiReviewRegistry _debugReviewControls = new();
    private AgentBridgeSnapshot _snapshot = AgentBridgeSnapshot.Empty;
    private string _lastSnapshotKey = string.Empty;
    private long _lastSnapshotAtMs;
    private long _snapshotRevision;
    private long _snapshotBuildCount;
    private double _snapshotLatestMilliseconds;
    private double _snapshotAverageMilliseconds;
    private double _snapshotMaxMilliseconds;
    private int _snapshotRequested;
    private long _lastDebugReviewAtMs;
    private int _poseValidationRequested;
    private bool _disposed;
    private readonly Dictionary<string, (long Revision, long NativeGeneration, long DeformationRevision, AgentBridgeExtensionSnapshot Summary)> _extensionSummaries = new(StringComparer.Ordinal);

    public CustomizePlusAgentBridgeService(
        ArmatureManager armatureManager,
        FrameworkManager framework,
        PluginConfiguration configuration,
        IDalamudPluginInterface pluginInterface,
        Logger logger,
        RuntimeEvidenceService runtimeEvidence,
        TemplateEditorManager templateEditorManager,
        LocalBoneMetadataService localBoneMetadata)
    {
        _armatureManager = armatureManager;
        _framework = framework;
        _configuration = configuration;
        _logger = logger;
        _runtimeEvidence = runtimeEvidence;
        _templateEditorManager = templateEditorManager;
        _localBoneMetadata = localBoneMetadata;

        if (string.IsNullOrWhiteSpace(configuration.AgentBridgePluginInstanceId))
            configuration.AgentBridgePluginInstanceId = Guid.NewGuid().ToString("N");

        var profile = AgentBridgeProfileIdentity.FromPluginConfigDirectory(pluginInterface.GetPluginConfigDirectory());
        var runtime = AgentBridgeRuntimeIdentity.FromAssembly("CustomizePlus", typeof(global::CustomizePlus.Plugin).Assembly, pluginInterface.AssemblyLocation.FullName);
        var router = new AgentBridgeCommandRouter()
            .Register("get-snapshot", (_, _) =>
            {
                // The framework thread performs the next full materialization. The request itself
                // stays read-only and returns the most recently published snapshot immediately.
                Interlocked.Exchange(ref _snapshotRequested, 1);
                return ValueTask.FromResult(AgentBridgeResponse.Ok("Cached Customize+ diagnostic snapshot.", Volatile.Read(ref _snapshot)));
            })
            .Register("get-control", (request, _) => ValueTask.FromResult(ReviewDebugControl(request)))
            .Register("invoke-control", (request, _) => ValueTask.FromResult(InvokeDebugControl(request)));

        _host = new AgentBridgeHost(new AgentBridgeHostOptions
        {
            ConfigDirectory = pluginInterface.GetPluginConfigDirectory(),
            PluginInstanceId = configuration.AgentBridgePluginInstanceId,
            PipeName = $"CustomizePlus-AgentBridge-{Environment.ProcessId}",
            GetProtectedAccessToken = () => configuration.AgentBridgeProtectedAccessToken,
            SetProtectedAccessToken = token => configuration.AgentBridgeProtectedAccessToken = token,
            SaveConfiguration = configuration.Save,
            CreateManifest = () => new AgentBridgeManifest(
                ProtocolVersion: 1,
                Runtime: runtime,
                ProfileId: profile.Id,
                ProfileAlias: profile.Alias,
                SnapshotSchema: "customizeplus.debug.snapshot.v1",
                Capabilities: new[]
                {
                    new AgentBridgeCapabilityDescriptor("diagnostics.read"),
                    new AgentBridgeCapabilityDescriptor("armature-lifecycle.read"),
                    new AgentBridgeCapabilityDescriptor("bone-importance-state.read"),
                    new AgentBridgeCapabilityDescriptor("runtime-evidence.read"),
                    new AgentBridgeCapabilityDescriptor("performance-counters.read"),
                    new AgentBridgeCapabilityDescriptor("authoring-tools.read"),
                    new AgentBridgeCapabilityDescriptor("pose-corrective-validation.debug"),
                },
                ReviewSurfaces: new[]
                {
                    new AgentBridgeReviewSurfaceDescriptor(PoseValidationSurfaceId, "Customize+ Debug RBF validation", "get-snapshot", PoseValidationSurfaceId, 1),
                },
                CaptureSurfaces: Array.Empty<AgentBridgeCaptureSurfaceDescriptor>(),
                Actions: new[]
                {
                    new AgentBridgeActionDescriptor(PoseValidationActionId, "Run bounded RBF validation", PoseValidationSurfaceId, AgentBridgeUiControlKind.Button, true),
                }),
            HandleRequestAsync = router.HandleAsync,
            EnableAudit = false,
        });

        RefreshSnapshot(force: true);
        _framework.Framework.Update += OnFrameworkUpdate;
        _host.Start();
        _logger.Information("Customize+ DEBUG AgentBridge started with read-only cached diagnostics.");
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _framework.Framework.Update -= OnFrameworkUpdate;
        _host.Dispose();
    }

    private void OnFrameworkUpdate(global::Dalamud.Plugin.Services.IFramework _)
    {
        if (_disposed)
            return;

        ProcessDebugPoseValidationRequest();
        if (Environment.TickCount64 - _lastDebugReviewAtMs >= DebugReviewRefreshIntervalMs)
            RefreshDebugReviewControls();
        if (Environment.TickCount64 - _lastSnapshotAtMs < SnapshotRefreshIntervalMs)
            return;

        RefreshSnapshot(force: Interlocked.Exchange(ref _snapshotRequested, 0) != 0);
    }

    private void RefreshDebugReviewControls()
    {
        _lastDebugReviewAtMs = Environment.TickCount64;
        _debugReviewControls.BeginFrame();
        try
        {
            _debugReviewControls.Register(
                PoseValidationActionId,
                "Run bounded RBF validation",
                AgentBridgeUiControlKind.Button,
                Vector2.Zero,
                Vector2.One,
                enabled: true,
                selected: false,
                value: "Runs one current-player, Debug-only 25-cycle RBF validation fixture.",
                () => Interlocked.Exchange(ref _poseValidationRequested, 1));
        }
        finally
        {
            _debugReviewControls.EndFrame();
        }
    }

    private void ProcessDebugPoseValidationRequest()
    {
        if (Interlocked.Exchange(ref _poseValidationRequested, 0) == 0)
            return;

        if (_armatureManager.TryStartDebugPoseCorrectiveValidation(out var message))
            _logger.Debug($"Started bounded Debug RBF validation: {message}");
        else
            _logger.Warning($"Did not start bounded Debug RBF validation: {message}");
    }

    private AgentBridgeResponse ReviewDebugControl(AgentBridgeRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Target))
            return AgentBridgeResponse.Fail("A debug control ID is required.");

        var review = _debugReviewControls.Review(request.Target);
        return review.Control == null
            ? new AgentBridgeResponse { Success = false, Message = "The requested debug control is not currently available.", Receipt = review }
            : AgentBridgeResponse.Ok("Debug control reviewed.", review);
    }

    private AgentBridgeResponse InvokeDebugControl(AgentBridgeRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Target) || request.FrameId is not { } frameId)
            return AgentBridgeResponse.Fail("A debug control ID and rendered frame ID are required.");

        var invocation = _debugReviewControls.Invoke(request.Target, frameId, request.Arguments);
        return invocation.Success
            ? AgentBridgeResponse.Ok(invocation.Message, invocation.Frame)
            : new AgentBridgeResponse { Success = false, Message = invocation.Message, Receipt = invocation.Frame };
    }

    private void RefreshSnapshot(bool force = false)
    {
        var started = Stopwatch.GetTimestamp();
        _lastSnapshotAtMs = Environment.TickCount64;
        var stateKey = BuildPublishedStateKey();
        if (!force && string.Equals(stateKey, _lastSnapshotKey, StringComparison.Ordinal))
            return;

        var armatures = _armatureManager.Armatures.Values
            .OrderBy(static armature => armature.ActorIdentifier.ToString(), StringComparer.Ordinal)
            .Take(24)
            .Select(armature => new AgentBridgeArmatureSnapshot(
                Actor: armature.ActorIdentifier.ToString(),
                Built: armature.IsBuilt,
                BindingCurrent: armature.IsSkeletonBindingCurrent,
                SkeletonRevision: armature.SkeletonRevision,
                NativeBindingGeneration: armature.NativeBindingGeneration,
                ActorLifetimeGeneration: armature.ActorLifetimeGeneration,
                AwaitingActorReacquisitionPublication: armature.IsAwaitingActorReacquisitionPublication,
                AwaitingAppearanceContextRebind: armature.IsAwaitingAppearanceContextRebind,
                CurrentAppearanceEpoch: armature.CurrentAppearanceEpoch,
                AppearanceEpochState: armature.AppearanceEpochState,
                CurrentAppearanceOperationType: armature.CurrentAppearanceOperationType,
                LatestPendingStableAppearanceEpoch: armature.LatestPendingStableAppearanceEpoch,
                LastAppliedStableAppearanceEpoch: armature.LastAppliedStableAppearanceEpoch,
                PendingAppearanceContext: armature.PendingAppearanceContext,
                LastAppearanceLifecycleEvent: armature.LastAppearanceLifecycleEvent,
                LastAppearanceRebindReason: armature.LastAppearanceRebindReason,
                LastAppearanceRebindEpoch: armature.LastAppearanceRebindEpoch,
                TemplateBindingRevision: armature.TemplateBindingRevision,
                TemplateBindingBuildCount: armature.TemplateBindingBuildCount,
                LastTemplateBindingBuildReason: armature.LastTemplateBindingBuildReason,
                ProfileResolutionRevision: armature.ProfileResolutionRevision,
                DeformationRevision: armature.DeformationRevision,
                DiagnosticsRevision: armature.DiagnosticsRevision,
                ResolvedTransformCount: armature.ResolvedBoneTransforms.Count,
                BoundModelBoneCount: armature.BoundModelBoneCount,
                ActiveModelBoneCount: armature.ActiveBones.Count,
                PendingProfileRebind: armature.IsPendingProfileRebind,
                PendingPublication: armature.PendingPublicationIdentity ?? string.Empty,
                BindingIssue: armature.LastSkeletonBindingIssue,
                RevertRecovery: new AgentBridgeRevertRecoverySnapshot(
                    armature.RevertFinalizedAtMs,
                    armature.RevertStableRebindQueuedAtMs,
                    armature.RevertFirstValidRecoveryObservationAtMs,
                    armature.RevertPublicationAtMs,
                    armature.RevertStableRebindCompletedAtMs,
                    armature.RevertRecoveryLatencyMs),
                Root: GetRootSnapshot(armature),
                BoneImportanceSignature: armature.ActiveBoneImportanceResult.ModelSignature ?? string.Empty,
                Manifest: ToManifestSnapshot(armature.GetCapabilityManifestSnapshot()),
                Extensions: GetExtensionSnapshot(armature),
                NativeWrites: armature.GetDebugNativeWriteDiagnostics(),
                PoseJointCorrectives: new AgentBridgePoseJointCorrectiveSnapshot(
                    armature.PoseAwareJointCorrectiveDebugState.Enabled,
                    armature.PoseAwareJointCorrectiveDebugState.Active,
                    armature.PoseAwareJointCorrectiveDebugState.Strength,
                    armature.PoseAwareJointCorrectiveDebugState.ActiveCategories.Contains("elbows"),
                    armature.PoseAwareJointCorrectiveDebugState.ActiveCategories.Contains("knees"),
                    armature.PoseAwareJointCorrectiveDebugState.ActiveCategories.Contains("shoulders"),
                    armature.PoseAwareJointCorrectiveDebugState.ActiveCategories.Contains("hips"),
                    armature.PoseAwareJointCorrectiveDebugState.EligibleJointCount,
                    armature.PoseAwareJointCorrectiveDebugState.CorrectedJointCount,
                    armature.PoseAwareJointCorrectiveDebugState.MaximumPoseWeight,
                    armature.PoseAwareJointCorrectiveDebugState.MaximumCorrection,
                    armature.PoseAwareJointCorrectiveDebugState.EvaluationMilliseconds,
                    armature.PoseAwareJointCorrectiveDebugState.WriteCount,
                    armature.PoseAwareJointCorrectiveDebugState.SafetySkipCount,
                    armature.PoseAwareJointCorrectiveDebugState.PoseCorrectiveRevision,
                    armature.PoseAwareJointCorrectiveDebugState.Summary),
                PoseRbfCorrectives: new AgentBridgePoseRbfCorrectiveSnapshot(
                    armature.PoseCorrectiveDebugState.Enabled,
                    armature.PoseCorrectiveDebugState.Active,
                    armature.PoseCorrectiveDebugState.GlobalStrength,
                    armature.PoseCorrectiveDebugState.ActiveRegions.Count,
                    armature.PoseCorrectiveDebugState.ActiveRegions
                        .Select(static region => new AgentBridgePoseRbfRegionSnapshot(
                            region.Region.ToString(),
                            region.Label,
                            region.Activation,
                            region.RawActivation,
                            region.Strength,
                            region.InfluentialSamples.FirstOrDefault()?.Name ?? string.Empty,
                            region.InfluentialSamples.FirstOrDefault()?.Weight ?? 0f,
                            region.PoseHistoryActive,
                            region.HysteresisHeld,
                            region.Summary))
                        .ToArray(),
                    armature.PoseCorrectiveDebugState.Summary),
                PoseValidation: ToPoseValidationSnapshot(armature.DebugPoseCorrectiveValidationSnapshot),
                Quality: new AgentBridgeQualitySnapshot(
                    armature.DeformationQualityDiagnostics.MaxBilateralDifference,
                    armature.DeformationQualityDiagnostics.MaxBilateralPair,
                    armature.DeformationQualityDiagnostics.MaxContinuityDifference,
                    armature.DeformationQualityDiagnostics.MaxContinuityBoundary,
                    armature.DeformationQualityDiagnostics.ProportionalImbalanceScore,
                    armature.DeformationQualityDiagnostics.SurfaceGradientScore,
                    armature.DeformationQualityDiagnostics.Warnings,
                    armature.DeformationQualityDiagnostics.Solver),
                Performance: armature.PerformanceMetrics.Snapshot(),
                TemplateApplicability: ProfileTransformResolver.Resolve(armature.Profile, armature.GetCapabilityManifestSnapshot())
                    .TemplateApplicability
                    .Select(static item => new AgentBridgeTemplateApplicabilitySnapshot(
                        item.TemplateId.ToString(), item.TemplateName, item.Enabled, item.Requirement.ToDisplayString(), item.Active, item.Reason, item.SavedTransformCount))
                    .ToArray()))
            .ToArray();
        var evidence = _runtimeEvidence.BuildSummary(_armatureManager.Armatures.Values.FirstOrDefault(static armature => armature.IsBuilt));
        var authoringKey = $"{_templateEditorManager.IsEditorActive}:{_templateEditorManager.IsEditorPaused}:{_templateEditorManager.EditorSessionId}:{_templateEditorManager.EditorRevision}:{_templateEditorManager.CurrentlyEditedTemplate?.UniqueId}:{_templateEditorManager.EditHistory.UndoCount}:{_templateEditorManager.EditHistory.RedoCount}:{_templateEditorManager.EditHistory.LatestLabel}:{_templateEditorManager.ProfileContextPreviewActive}:{_templateEditorManager.ProfileContextTemplateCount}:{_localBoneMetadata.LoadedPackCount}:{_localBoneMetadata.LoadedEntryCount}";
        _lastSnapshotKey = stateKey;
        var lifecycle = _armatureManager.GetDebugSelfLifecycleTrace()
            .TakeLast(12)
            .Select(static entry => entry.ToDisplayLine())
            .ToArray();
        var activeTemplates = armatures.Sum(static armature => armature.TemplateApplicability.Count(static item => item.Active));
        var dormantTemplates = armatures.Sum(static armature => armature.TemplateApplicability.Count(static item => item.Enabled && !item.Active));
        Volatile.Write(ref _snapshot, new AgentBridgeSnapshot(
            Schema: "customizeplus.debug.snapshot.v1",
            CapturedAtUtc: DateTimeOffset.UtcNow,
            Revision: ++_snapshotRevision,
            ArmatureCount: _armatureManager.Armatures.Count,
            Armatures: armatures,
            Diagnostics: new AgentBridgeDiagnosticsSnapshot(
                armatures.All(static armature => armature.BindingCurrent),
                activeTemplates,
                dormantTemplates,
                armatures.Sum(static armature => armature.NativeWrites.SkippedStaleBinding),
                armatures.Sum(static armature => armature.NativeWrites.SkippedUnsafeTransform)),
            Authoring: new AgentBridgeAuthoringToolSnapshot(
                _templateEditorManager.IsEditorActive,
                _templateEditorManager.IsEditorPaused,
                _templateEditorManager.CurrentlyEditedTemplate?.Name.Text ?? string.Empty,
                _templateEditorManager.EditorSessionId.ToString(),
                _templateEditorManager.EditorRevision,
                _templateEditorManager.EditHistory.UndoCount,
                _templateEditorManager.EditHistory.RedoCount,
                _templateEditorManager.EditHistory.LatestLabel,
                _templateEditorManager.ProfileContextPreviewActive,
                _templateEditorManager.ProfileContextTemplateCount,
                _localBoneMetadata.LoadedPackCount,
                _localBoneMetadata.LoadedEntryCount),
            RecentSelfLifecycle: lifecycle,
            Evidence: evidence,
            BridgePerformance: new AgentBridgePerformanceSnapshot(_snapshotBuildCount + 1, _snapshotLatestMilliseconds, _snapshotAverageMilliseconds, _snapshotMaxMilliseconds)));
        RecordSnapshotTiming(started);
    }

    private string BuildPublishedStateKey()
    {
        var armatureState = _armatureManager.Armatures.Values
            .OrderBy(static armature => armature.ActorIdentifier.ToString(), StringComparer.Ordinal)
            .Take(24)
            .Select(static armature => string.Join(':',
                armature.ActorIdentifier,
                armature.IsBuilt,
                armature.IsSkeletonBindingCurrent,
                armature.SkeletonRevision,
                armature.NativeBindingGeneration,
                armature.ActorLifetimeGeneration,
                armature.CurrentAppearanceEpoch,
                armature.AppearanceEpochState,
                armature.TemplateBindingRevision,
                armature.TemplateBindingBuildCount,
                armature.ProfileResolutionRevision,
                armature.DeformationRevision,
                armature.DiagnosticsRevision,
                armature.IsPendingProfileRebind,
                armature.LastSkeletonBindingIssue,
                armature.ActiveBoneImportanceResult.ModelSignature,
                armature.GetCapabilityManifestSnapshot().Revision,
                armature.GetDebugNativeWriteDiagnostics().SkippedStaleBinding,
                armature.GetDebugNativeWriteDiagnostics().SkippedUnsafeTransform));
        var authoring = $"{_templateEditorManager.IsEditorActive}:{_templateEditorManager.IsEditorPaused}:{_templateEditorManager.EditorSessionId}:{_templateEditorManager.EditorRevision}:{_templateEditorManager.CurrentlyEditedTemplate?.UniqueId}:{_templateEditorManager.EditHistory.UndoCount}:{_templateEditorManager.EditHistory.RedoCount}:{_templateEditorManager.EditHistory.LatestLabel}:{_templateEditorManager.ProfileContextPreviewActive}:{_templateEditorManager.ProfileContextTemplateCount}:{_localBoneMetadata.LoadedPackCount}:{_localBoneMetadata.LoadedEntryCount}";
        return string.Join('|', armatureState) + $"|authoring:{authoring}";
    }

    private void RecordSnapshotTiming(long started)
    {
        var elapsed = (Stopwatch.GetTimestamp() - started) * 1000d / Stopwatch.Frequency;
        _snapshotBuildCount++;
        _snapshotLatestMilliseconds = elapsed;
        _snapshotAverageMilliseconds += (elapsed - _snapshotAverageMilliseconds) / _snapshotBuildCount;
        _snapshotMaxMilliseconds = Math.Max(_snapshotMaxMilliseconds, elapsed);
    }

    private static AgentBridgeManifestSnapshot ToManifestSnapshot(SkeletonCapabilityManifest manifest)
        => new(
            manifest.Revision,
            manifest.StructuralFingerprint,
            manifest.BindingCurrent,
            manifest.Topology.TotalBoneCount,
            manifest.Topology.PartialBoneCounts.Count,
            Enum.GetValues<SkeletonCapability>()
                .Where(static capability => capability != SkeletonCapability.None)
                .Select(capability => new AgentBridgeCapabilitySnapshot(capability.ToString(), manifest.GetState(capability).ToString()))
                .ToArray());

    private AgentBridgeExtensionSnapshot GetExtensionSnapshot(Armature armature)
    {
        var actor = armature.ActorIdentifier.ToString();
        if (_extensionSummaries.TryGetValue(actor, out var cached)
            && cached.Revision == armature.SkeletonRevision
            && cached.NativeGeneration == armature.NativeBindingGeneration
            && cached.DeformationRevision == armature.DeformationRevision)
        {
            return cached.Summary;
        }

        var names = armature.GetAllBones().Select(static bone => bone.BoneName).ToArray();
        var nflb = names.Select(static name => (Name: name, Metadata: BoneData.GetMetadata(name)))
            .Where(static entry => entry.Metadata.Origin == BoneOrigin.NFLB)
            .ToArray();
        var skelomae = names.Select(static name => (Name: name, Metadata: BoneData.GetMetadata(name)))
            .Where(static entry => entry.Metadata.Origin == BoneOrigin.Skelomae)
            .ToArray();
        var activeNflb = armature.ResolvedBoneTransforms.Keys.Count(name => BoneData.GetMetadata(name).Origin == BoneOrigin.NFLB);
        var activeSkelomae = armature.ResolvedBoneTransforms.Keys.Count(name => BoneData.GetMetadata(name).Origin == BoneOrigin.Skelomae);
        var summary = new AgentBridgeExtensionSnapshot(
            NflbKnown: nflb.Length,
            NflbBody: nflb.Count(static entry => entry.Metadata.Role == BoneFunctionalRole.BodyExtension),
            NflbClothing: nflb.Count(static entry => entry.Metadata.Role == BoneFunctionalRole.ClothingRig),
            NflbProps: nflb.Count(static entry => entry.Metadata.Role == BoneFunctionalRole.PropRig),
            NflbUnknown: names.Count(static name => name.StartsWith("nf_", StringComparison.Ordinal) && BoneData.GetMetadata(name).Role == BoneFunctionalRole.Unknown),
            ExplicitActiveNflb: activeNflb,
            AutomatedNflbBody: armature.DeformationQualityDiagnostics.Solver.AutomatedNflbBodyControls,
            AutomatedNflbClothing: 0,
            AutomatedNflbProps: 0,
            SkelomaeKnown: skelomae.Length,
            SkelomaeBody: skelomae.Count(static entry => entry.Metadata.Role == BoneFunctionalRole.BodyExtension),
            SkelomaeTongue: skelomae.Count(static entry => entry.Metadata.Role == BoneFunctionalRole.ArticulatedBodyFeature),
            SkelomaeWings: skelomae.Count(static entry => entry.Metadata.Role == BoneFunctionalRole.ArticulatedAppendage),
            ExplicitActiveSkelomae: activeSkelomae,
            AutomatedSkelomaeBody: armature.DeformationQualityDiagnostics.Solver.AutomatedSkelomaeBodyControls,
            AutomatedTongue: 0,
            AutomatedWings: 0);
        _extensionSummaries[actor] = (armature.SkeletonRevision, armature.NativeBindingGeneration, armature.DeformationRevision, summary);
        return summary;
    }

    // Published armature state only: this makes the root-scale boundary diagnosable without touching native draw data.
    private static AgentBridgeRootSnapshot GetRootSnapshot(Armature armature)
    {
        armature.ResolvedBoneTransforms.TryGetValue(Constants.RootBoneName, out var resolved);
        var mainRoot = armature.IsBuilt
            ? armature.GetAllBones().FirstOrDefault(static bone => bone.PartialSkeletonIndex == 0 && bone.BoneIndex == 0)
            : null;

        var drawApplication = armature.RootScaleDiagnostics;
        return new AgentBridgeRootSnapshot(
            MainRootBoneName: mainRoot?.BoneName ?? string.Empty,
            MainRootActive: mainRoot?.IsActive ?? false,
            HasResolvedRootTransform: resolved != null,
            ResolvedScale: AgentBridgeVector3.From(resolved?.Scaling ?? System.Numerics.Vector3.One),
            TargetScale: AgentBridgeVector3.From(mainRoot?.CustomizedTransform?.Scaling ?? System.Numerics.Vector3.One),
            AppliedScale: AgentBridgeVector3.From(mainRoot?.AppliedTransform?.Scaling ?? System.Numerics.Vector3.One),
            DrawApplication: new AgentBridgeRootScaleApplicationSnapshot(
                drawApplication.Attempts,
                drawApplication.Applied,
                drawApplication.RootScaleModified,
                drawApplication.ActorEligible,
                AgentBridgeVector3.From(drawApplication.ObservedBefore),
                AgentBridgeVector3.From(drawApplication.Requested),
                AgentBridgeVector3.From(drawApplication.ObservedAfter)));
    }

    private static AgentBridgePoseCorrectiveValidationSnapshot ToPoseValidationSnapshot(DebugPoseCorrectiveValidationSnapshot validation)
        => new(
            validation.Status,
            validation.Detail,
            validation.Phase,
            validation.CompletedCycles,
            validation.TargetCycles,
            validation.FramesInPhase,
            validation.FramesRequired,
            validation.NativeApplicationCount,
            validation.MaximumActiveScaleDelta,
            validation.MaximumIntermediateScaleDelta,
            validation.MaximumReturnTransitionScaleDelta,
            validation.MaximumPostCycleTranslationDelta,
            validation.MaximumPostCycleRotationDegrees,
            validation.MaximumPostCycleScaleDelta,
            validation.ActiveCorrectionObserved,
            validation.NeutralReturnWithinTolerance,
            validation.ElapsedMilliseconds,
            validation.TargetBones,
            validation.Samples.Select(static sample => new AgentBridgePoseCorrectiveValidationSampleSnapshot(
                sample.Phase,
                sample.BoneName,
                new AgentBridgeVector3(sample.BeforeTranslation.X, sample.BeforeTranslation.Y, sample.BeforeTranslation.Z),
                new AgentBridgeQuaternion(sample.BeforeRotation.X, sample.BeforeRotation.Y, sample.BeforeRotation.Z, sample.BeforeRotation.W),
                new AgentBridgeVector3(sample.BeforeScale.X, sample.BeforeScale.Y, sample.BeforeScale.Z),
                new AgentBridgeVector3(sample.AfterTranslation.X, sample.AfterTranslation.Y, sample.AfterTranslation.Z),
                new AgentBridgeQuaternion(sample.AfterRotation.X, sample.AfterRotation.Y, sample.AfterRotation.Z, sample.AfterRotation.W),
                new AgentBridgeVector3(sample.AfterScale.X, sample.AfterScale.Y, sample.AfterScale.Z),
                new AgentBridgeVector3(sample.CorrectiveScale.X, sample.CorrectiveScale.Y, sample.CorrectiveScale.Z),
                sample.TranslationDelta,
                sample.RotationDegreesDelta,
                sample.ScaleDelta)).ToArray(),
            validation.EvaluationTimings.Select(static timing => new AgentBridgePoseCorrectiveTimingSnapshot(
                timing.Phase,
                timing.Samples,
                timing.AverageMilliseconds,
                timing.MaximumMilliseconds)).ToArray());

    private sealed record AgentBridgeSnapshot(
        string Schema,
        DateTimeOffset CapturedAtUtc,
        long Revision,
        int ArmatureCount,
        IReadOnlyList<AgentBridgeArmatureSnapshot> Armatures,
        AgentBridgeDiagnosticsSnapshot Diagnostics,
        AgentBridgeAuthoringToolSnapshot Authoring,
        IReadOnlyList<string> RecentSelfLifecycle,
        RuntimeEvidenceSummary Evidence,
        AgentBridgePerformanceSnapshot BridgePerformance)
    {
        public static AgentBridgeSnapshot Empty { get; } = new(
            "customizeplus.debug.snapshot.v1",
            DateTimeOffset.MinValue,
            0,
            0,
            Array.Empty<AgentBridgeArmatureSnapshot>(),
            new AgentBridgeDiagnosticsSnapshot(false, 0, 0, 0, 0),
            new AgentBridgeAuthoringToolSnapshot(false, false, string.Empty, string.Empty, 0, 0, 0, string.Empty, false, 0, 0, 0),
            Array.Empty<string>(),
            new RuntimeEvidenceSummary(0, "No comparison run.", string.Empty),
            new AgentBridgePerformanceSnapshot(0, 0d, 0d, 0d));
    }

    private sealed record AgentBridgeArmatureSnapshot(
        string Actor,
        bool Built,
        bool BindingCurrent,
        long SkeletonRevision,
        long NativeBindingGeneration,
        long ActorLifetimeGeneration,
        bool AwaitingActorReacquisitionPublication,
        bool AwaitingAppearanceContextRebind,
        long CurrentAppearanceEpoch,
        string AppearanceEpochState,
        string CurrentAppearanceOperationType,
        long LatestPendingStableAppearanceEpoch,
        long LastAppliedStableAppearanceEpoch,
        string PendingAppearanceContext,
        string LastAppearanceLifecycleEvent,
        string LastAppearanceRebindReason,
        long LastAppearanceRebindEpoch,
        long TemplateBindingRevision,
        long TemplateBindingBuildCount,
        string LastTemplateBindingBuildReason,
        long ProfileResolutionRevision,
        long DeformationRevision,
        long DiagnosticsRevision,
        int ResolvedTransformCount,
        int BoundModelBoneCount,
        int ActiveModelBoneCount,
        bool PendingProfileRebind,
        string PendingPublication,
        string BindingIssue,
        AgentBridgeRevertRecoverySnapshot RevertRecovery,
        AgentBridgeRootSnapshot Root,
        string BoneImportanceSignature,
        AgentBridgeManifestSnapshot Manifest,
        AgentBridgeExtensionSnapshot Extensions,
        ArmatureNativeWriteDiagnostics NativeWrites,
        AgentBridgePoseJointCorrectiveSnapshot PoseJointCorrectives,
        AgentBridgePoseRbfCorrectiveSnapshot PoseRbfCorrectives,
        AgentBridgePoseCorrectiveValidationSnapshot PoseValidation,
        AgentBridgeQualitySnapshot Quality,
        IReadOnlyList<RuntimeTimingSummary> Performance,
        IReadOnlyList<AgentBridgeTemplateApplicabilitySnapshot> TemplateApplicability)
    {
        public string Key
            => $"{Actor}:{Built}:{BindingCurrent}:{SkeletonRevision}:{NativeBindingGeneration}:{ActorLifetimeGeneration}:{AwaitingActorReacquisitionPublication}:{AwaitingAppearanceContextRebind}:{CurrentAppearanceEpoch}:{AppearanceEpochState}:{CurrentAppearanceOperationType}:{LatestPendingStableAppearanceEpoch}:{LastAppliedStableAppearanceEpoch}:{PendingAppearanceContext}:{LastAppearanceLifecycleEvent}:{LastAppearanceRebindReason}:{LastAppearanceRebindEpoch}:{TemplateBindingRevision}:{TemplateBindingBuildCount}:{LastTemplateBindingBuildReason}:{ProfileResolutionRevision}:{DeformationRevision}:{DiagnosticsRevision}:{ResolvedTransformCount}:{BoundModelBoneCount}:{ActiveModelBoneCount}:{PendingProfileRebind}:{PendingPublication}:{BindingIssue}:{RevertRecovery}:{Root}:{BoneImportanceSignature}:{Manifest.Revision}:{Manifest.StructuralFingerprint}:{Extensions}:{NativeWrites}:{PoseJointCorrectives}:{PoseRbfCorrectives}:{PoseValidation.Status}:{PoseValidation.Phase}:{PoseValidation.CompletedCycles}:{PoseValidation.NativeApplicationCount}:{PoseValidation.MaximumActiveScaleDelta}:{PoseValidation.MaximumPostCycleScaleDelta}:{Quality}:{string.Join(',', Performance.Select(static item => item.Stage + ':' + item.Samples))}:{string.Join(',', TemplateApplicability.Select(static item => item.Key))}";
    }

    private sealed record AgentBridgeRevertRecoverySnapshot(
        long FinalizedAtMs,
        long StableRebindQueuedAtMs,
        long FirstValidRecoveryObservationAtMs,
        long PublicationAtMs,
        long StableRebindCompletedAtMs,
        long TotalLatencyMs);

    private sealed record AgentBridgeRootSnapshot(
        string MainRootBoneName,
        bool MainRootActive,
        bool HasResolvedRootTransform,
        AgentBridgeVector3 ResolvedScale,
        AgentBridgeVector3 TargetScale,
        AgentBridgeVector3 AppliedScale,
        AgentBridgeRootScaleApplicationSnapshot DrawApplication);

    private sealed record AgentBridgeRootScaleApplicationSnapshot(
        long Attempts,
        long Applied,
        bool RootScaleModified,
        bool ActorEligible,
        AgentBridgeVector3 ObservedBefore,
        AgentBridgeVector3 Requested,
        AgentBridgeVector3 ObservedAfter);

    private sealed record AgentBridgeVector3(float X, float Y, float Z)
    {
        public static AgentBridgeVector3 From(System.Numerics.Vector3 value)
            => new(value.X, value.Y, value.Z);
    }

    private sealed record AgentBridgeQuaternion(float X, float Y, float Z, float W);

    private sealed record AgentBridgeManifestSnapshot(
        long Revision,
        string StructuralFingerprint,
        bool BindingCurrent,
        int BoneCount,
        int PartialCount,
        IReadOnlyList<AgentBridgeCapabilitySnapshot> Capabilities);

    private sealed record AgentBridgeCapabilitySnapshot(string Capability, string State);

    private sealed record AgentBridgeExtensionSnapshot(
        int NflbKnown,
        int NflbBody,
        int NflbClothing,
        int NflbProps,
        int NflbUnknown,
        int ExplicitActiveNflb,
        int AutomatedNflbBody,
        int AutomatedNflbClothing,
        int AutomatedNflbProps,
        int SkelomaeKnown,
        int SkelomaeBody,
        int SkelomaeTongue,
        int SkelomaeWings,
        int ExplicitActiveSkelomae,
        int AutomatedSkelomaeBody,
        int AutomatedTongue,
        int AutomatedWings);

    private sealed record AgentBridgeDiagnosticsSnapshot(
        bool Healthy,
        int ActiveTemplateAssignments,
        int DormantTemplateAssignments,
        long StaleBindingSkips,
        long UnsafeTransformSkips);

    private sealed record AgentBridgeAuthoringToolSnapshot(
        bool EditorActive,
        bool EditorPaused,
        string EditedTemplate,
        string EditorSessionId,
        long EditorRevision,
        int UndoCount,
        int RedoCount,
        string LatestTransactionLabel,
        bool ProfileContextPreviewActive,
        int ProfileContextTemplateCount,
        int LoadedMetadataPacks,
        int LoadedMetadataEntries);

    private sealed record AgentBridgePerformanceSnapshot(
        long SnapshotBuildCount,
        double LatestMilliseconds,
        double AverageMilliseconds,
        double MaxMilliseconds);

    private sealed record AgentBridgeQualitySnapshot(
        float MaxBilateralDifference,
        string MaxBilateralPair,
        float MaxContinuityDifference,
        string MaxContinuityBoundary,
        float ProportionalImbalanceScore,
        float SurfaceGradientScore,
        IReadOnlyList<string> Warnings,
        DeformationQualitySolverDiagnostics Solver);

    private sealed record AgentBridgePoseJointCorrectiveSnapshot(
        bool Enabled,
        bool Active,
        float Strength,
        bool ElbowsActive,
        bool KneesActive,
        bool ShouldersActive,
        bool HipsActive,
        int EligibleJointCount,
        int CorrectedJointCount,
        float MaximumPoseWeight,
        float MaximumCorrection,
        double EvaluationMilliseconds,
        int WriteCount,
        int SafetySkipCount,
        long PoseCorrectiveRevision,
        string Summary);

    private sealed record AgentBridgePoseRbfCorrectiveSnapshot(
        bool Enabled,
        bool Active,
        float Strength,
        int ActiveRegionCount,
        IReadOnlyList<AgentBridgePoseRbfRegionSnapshot> Regions,
        string Summary);

    private sealed record AgentBridgePoseRbfRegionSnapshot(
        string Region,
        string Label,
        float Activation,
        float RawActivation,
        float Strength,
        string DominantSample,
        float DominantSampleWeight,
        bool PoseHistoryActive,
        bool HysteresisHeld,
        string Summary);

    private sealed record AgentBridgePoseCorrectiveValidationSnapshot(
        string Status,
        string Detail,
        string Phase,
        int CompletedCycles,
        int TargetCycles,
        int FramesInPhase,
        int FramesRequired,
        int NativeApplicationCount,
        float MaximumActiveScaleDelta,
        float MaximumIntermediateScaleDelta,
        float MaximumReturnTransitionScaleDelta,
        float MaximumPostCycleTranslationDelta,
        float MaximumPostCycleRotationDegrees,
        float MaximumPostCycleScaleDelta,
        bool ActiveCorrectionObserved,
        bool NeutralReturnWithinTolerance,
        long ElapsedMilliseconds,
        IReadOnlyList<string> TargetBones,
        IReadOnlyList<AgentBridgePoseCorrectiveValidationSampleSnapshot> Samples,
        IReadOnlyList<AgentBridgePoseCorrectiveTimingSnapshot> EvaluationTimings);

    private sealed record AgentBridgePoseCorrectiveValidationSampleSnapshot(
        string Phase,
        string BoneName,
        AgentBridgeVector3 BeforeTranslation,
        AgentBridgeQuaternion BeforeRotation,
        AgentBridgeVector3 BeforeScale,
        AgentBridgeVector3 AfterTranslation,
        AgentBridgeQuaternion AfterRotation,
        AgentBridgeVector3 AfterScale,
        AgentBridgeVector3 CorrectiveScale,
        float TranslationDelta,
        float RotationDegreesDelta,
        float ScaleDelta);

    private sealed record AgentBridgePoseCorrectiveTimingSnapshot(
        string Phase,
        long Samples,
        double AverageMilliseconds,
        double MaximumMilliseconds);

    private sealed record AgentBridgeTemplateApplicabilitySnapshot(
        string TemplateId,
        string TemplateName,
        bool Enabled,
        string Requirement,
        bool Active,
        string Reason,
        int SavedTransformCount)
    {
        public string Key => $"{TemplateId}:{Enabled}:{Requirement}:{Active}:{Reason}:{SavedTransformCount}";
    }
}
#endif
