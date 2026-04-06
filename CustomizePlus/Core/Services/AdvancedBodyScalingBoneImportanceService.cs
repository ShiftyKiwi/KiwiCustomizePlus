// Copyright (c) Customize+.
// Licensed under the MIT license.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CustomizePlus.Core.Data;
using CustomizePlus.Interop.Ipc;
using Dalamud.Plugin.Services;
using Penumbra.GameData.Data;
using Penumbra.GameData.Enums;
using Penumbra.GameData.Files;
using Penumbra.GameData.Interop;
using Penumbra.GameData.Structs;
using OtterGui.Log;

namespace CustomizePlus.Core.Services;

public sealed class AdvancedBodyScalingBoneImportanceService
{
    private const float Epsilon = 0.0001f;
    private const float CoarseNormalizationFloor = 0.18f;
    private const float SkinNormalizationFloor = 0.10f;
    private const float ClassificationInfluence = 0.78f;
    private const float MinimumActiveScore = 0.03f;

    private readonly IDataManager _dataManager;
    private readonly Logger _logger;
    private readonly PenumbraIpcHandler _penumbraIpc;
    private readonly object _cacheLock = new();
    private readonly Dictionary<string, CachedModelImportance> _modelCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, CachedAggregateImportance> _aggregateCache = new(StringComparer.OrdinalIgnoreCase);

    private sealed class CachedModelImportance
    {
        public required string ModelPath { get; init; }
        public required bool Stage1Available { get; init; }
        public required bool Stage2Available { get; init; }
        public required ImportanceCoverageProfile CoarseProfile { get; init; }
        public required IReadOnlyDictionary<string, float> CoarseAreaScores { get; init; }
        public required IReadOnlyDictionary<string, float> CoarseScores { get; init; }
        public required ImportanceCoverageProfile SkinProfile { get; init; }
        public required IReadOnlyDictionary<string, float> SkinAreaScores { get; init; }
        public required IReadOnlyDictionary<string, float> SkinScores { get; init; }
        public required string Summary { get; init; }
        public required string FailureReason { get; init; }
    }

    private sealed class CachedAggregateImportance
    {
        public required string AggregateIdentity { get; init; }
        public required string ResolvedModelSignature { get; init; }
        public required bool Stage1Available { get; init; }
        public required bool Stage2Available { get; init; }
        public required bool UsesMixedAggregate { get; init; }
        public required bool MultiModelAggregate { get; init; }
        public required int ContributingPartCount { get; init; }
        public required AdvancedBodyScalingBoneImportanceResolutionSource ResolutionSource { get; init; }
        public required IReadOnlyDictionary<string, float> CoarseAreaScores { get; init; }
        public required IReadOnlyDictionary<string, float> CoarseScores { get; init; }
        public required IReadOnlyDictionary<string, float> SkinAreaScores { get; init; }
        public required IReadOnlyDictionary<string, float> SkinScores { get; init; }
        public required IReadOnlyDictionary<string, float> MixedAreaScores { get; init; }
        public required IReadOnlyDictionary<string, float> MixedScores { get; init; }
        public required IReadOnlyList<string> PreferredPartDetails { get; init; }
        public required IReadOnlyList<string> CoarsePartDetails { get; init; }
        public required IReadOnlyList<string> MissingPartDetails { get; init; }
        public required string RequestedPathsSummary { get; init; }
        public required string ResolvedPathsSummary { get; init; }
        public required bool AreaAwareRefinementActive { get; init; }
        public required bool ClassificationRefinementActive { get; init; }
        public required bool ConfidenceWeightedAggregationActive { get; init; }
        public required string RefinementSummary { get; init; }
        public required string ConfidenceSummary { get; init; }
        public required string Summary { get; init; }
        public required string FailureReason { get; init; }
    }

    private enum BoneImportanceClassification
    {
        CoreStructural,
        SecondaryLocal,
        DeemphasizedLocal,
        Ignored,
    }

    private sealed class BoneMetricAccumulator
    {
        public float Influence { get; set; }
        public float VertexCoverage { get; set; }
        public float MeshParticipation { get; set; }
        public float SubmeshParticipation { get; set; }
        public float TriangleCoverage { get; set; }
    }

    private sealed class ImportanceCoverageProfile
    {
        public static readonly ImportanceCoverageProfile Empty = new();

        public int MeaningfulBoneCount { get; init; }
        public int CoreBoneCount { get; init; }
        public int SecondaryBoneCount { get; init; }
        public int DeemphasizedBoneCount { get; init; }
        public float CoreShare { get; init; }
        public float SecondaryShare { get; init; }
        public float DeemphasizedShare { get; init; }
        public float DominantShare { get; init; }
    }

    private enum BodyModelPart
    {
        TopBody,
        BottomLegs,
        HandsGloves,
        FeetShoes,
    }

    private enum ModelPathCandidateKind
    {
        ActiveEquipment,
        CustomizationBody,
    }

    private readonly record struct ModelPartRequest(
        BodyModelPart Part,
        string Label,
        float AggregateWeight,
        IReadOnlyList<ModelPathCandidate> Candidates);

    private readonly record struct ModelPathCandidate(
        string GamePath,
        string ModelIdentity,
        string Description,
        ModelPathCandidateKind Kind,
        ushort SlotId,
        GenderRace GenderRace,
        BodyModelPart Part,
        string PartLabel,
        string ExpectedSuffix,
        float AggregateWeight);

    private readonly record struct ResolvedModelReference(
        string CacheKey,
        string ResolvedPath,
        string RequestedGamePath,
        string ModelIdentity,
        AdvancedBodyScalingBoneImportanceResolutionSource ResolutionSource,
        string ResolutionDetail,
        BodyModelPart Part,
        string PartLabel,
        float AggregateWeight);

    private readonly record struct ModelContribution(
        BodyModelPart Part,
        string PartLabel,
        float AggregateWeight,
        ResolvedModelReference Reference,
        CachedModelImportance Cached,
        bool CacheHit);

    private readonly record struct SlotConfidenceEvaluation(
        float Confidence,
        float EffectiveWeight,
        bool UsesStage2,
        string Summary);

    private readonly record struct BlendElement(byte Stream, byte Offset, MdlFile.VertexType Type);

    public AdvancedBodyScalingBoneImportanceService(IDataManager dataManager, Logger logger, PenumbraIpcHandler penumbraIpc)
    {
        _dataManager = dataManager;
        _logger = logger;
        _penumbraIpc = penumbraIpc;
    }

    internal AdvancedBodyScalingBoneImportanceProbeResult ProbeActorModelSignature(
        Actor actor,
        AdvancedBodyScalingSettings settings,
        string previousModelSignature = "")
    {
        if (!settings.ModelDerivedBoneImportanceEnabled)
        {
            return AdvancedBodyScalingBoneImportanceProbeResult.CreateFallback(
                "Model-derived weighting is disabled in settings.",
                modelSignature: previousModelSignature,
                signatureChanged: false);
        }

        if (!actor || !actor.Model || !actor.Model.IsHuman)
        {
            return AdvancedBodyScalingBoneImportanceProbeResult.CreateFallback(
                "The active actor does not expose a supported human body model.",
                modelSignature: previousModelSignature,
                signatureChanged: false);
        }

        if (!TryBuildModelPartRequests(actor, out var partRequests, out _, out var candidateFailureReason))
        {
            return AdvancedBodyScalingBoneImportanceProbeResult.CreateFallback(
                candidateFailureReason,
                modelSignature: previousModelSignature,
                signatureChanged: false);
        }

        var resolutionAttempts = new List<string>();
        var referencesByPart = new Dictionary<BodyModelPart, ResolvedModelReference>();
        foreach (var request in partRequests)
        {
            var references = ResolveModelReferences(actor, request.Candidates, resolutionAttempts);
            if (references.Count == 0)
                continue;

            referencesByPart[request.Part] = references[0];
        }

        var resolvedModelSignature = BuildReferenceSignature(partRequests, referencesByPart);
        if (referencesByPart.Count == 0)
        {
            var resolutionTrace = string.Join(" ", resolutionAttempts.Where(static attempt => !string.IsNullOrWhiteSpace(attempt)));
            return AdvancedBodyScalingBoneImportanceProbeResult.CreateFallback(
                string.IsNullOrWhiteSpace(resolutionTrace)
                    ? "No usable body model parts could be probed for the active actor."
                    : resolutionTrace,
                modelSignature: resolvedModelSignature,
                signatureChanged: !string.Equals(previousModelSignature, resolvedModelSignature, StringComparison.OrdinalIgnoreCase));
        }

        return new AdvancedBodyScalingBoneImportanceProbeResult
        {
            HasResolvedModelSet = true,
            ModelSignature = resolvedModelSignature,
            ModelSignatureChanged = !string.Equals(previousModelSignature, resolvedModelSignature, StringComparison.OrdinalIgnoreCase),
            Summary = referencesByPart.Count == partRequests.Count
                ? "Resolved model-signature probe matched all requested whole-body slots."
                : $"Resolved model-signature probe matched {referencesByPart.Count} of {partRequests.Count} requested whole-body slots.",
        };
    }

    internal AdvancedBodyScalingBoneImportanceResult ResolveForActor(Actor actor, AdvancedBodyScalingSettings settings, string previousModelSignature = "")
    {
        if (!settings.ModelDerivedBoneImportanceEnabled)
        {
            return AdvancedBodyScalingBoneImportanceResult.CreateFallback(
                "Model-derived weighting is disabled in settings.",
                enabled: false,
                preferSkinWeights: settings.PreferTrueSkinWeightImportance,
                heuristicBlend: settings.BoneImportanceHeuristicBlend,
                modelSignature: previousModelSignature,
                refreshStatus: "Model-derived weighting is disabled, so no model-signature refresh was attempted.");
        }

        if (!actor || !actor.Model || !actor.Model.IsHuman)
        {
            return AdvancedBodyScalingBoneImportanceResult.CreateFallback(
                "The active actor does not expose a supported human body model.",
                enabled: true,
                preferSkinWeights: settings.PreferTrueSkinWeightImportance,
                heuristicBlend: settings.BoneImportanceHeuristicBlend,
                modelSignature: previousModelSignature,
                refreshStatus: "No supported human model was available, so the previous heuristic fallback remained active.");
        }

        if (!TryBuildModelPartRequests(actor, out var partRequests, out var modelIdentity, out var candidateFailureReason))
        {
            return AdvancedBodyScalingBoneImportanceResult.CreateFallback(
                candidateFailureReason,
                enabled: true,
                preferSkinWeights: settings.PreferTrueSkinWeightImportance,
                heuristicBlend: settings.BoneImportanceHeuristicBlend,
                modelSignature: previousModelSignature,
                refreshStatus: "The model-part candidate set could not be built, so heuristic fallback remained active.");
        }

        var resolutionAttempts = new List<string>();
        var missingPartDetails = new List<string>();
        var contributions = ResolveModelContributions(actor, partRequests, resolutionAttempts, missingPartDetails);
        var resolvedModelSignature = BuildResolvedModelSignature(partRequests, contributions);
        if (contributions.Count == 0)
        {
            var resolutionTrace = string.Join(" ", resolutionAttempts.Where(static attempt => !string.IsNullOrWhiteSpace(attempt)));
            return AdvancedBodyScalingBoneImportanceResult.CreateFallback(
                string.IsNullOrWhiteSpace(resolutionTrace)
                    ? "No usable body model parts could be resolved for the active actor."
                    : resolutionTrace,
                enabled: true,
                preferSkinWeights: settings.PreferTrueSkinWeightImportance,
                heuristicBlend: settings.BoneImportanceHeuristicBlend,
                modelSignature: resolvedModelSignature,
                modelIdentity: modelIdentity,
                resolutionDetail: "No usable model resolution source succeeded.",
                resolutionTrace: resolutionTrace,
                modelSignatureChanged: !string.Equals(previousModelSignature, resolvedModelSignature, StringComparison.OrdinalIgnoreCase),
                refreshStatus: BuildRefreshStatus(previousModelSignature, resolvedModelSignature, cacheHit: false, rebuiltAggregate: false, usingFallback: true),
                missingPartDetails: missingPartDetails);
        }

        var aggregateCacheKey = BuildAggregateCacheKey(resolvedModelSignature);
        CachedAggregateImportance aggregate;
        var cacheHit = false;
        lock (_cacheLock)
        {
            if (_aggregateCache.TryGetValue(aggregateCacheKey, out aggregate!))
                cacheHit = true;
        }

        if (!cacheHit)
        {
            aggregate = BuildAggregateImportance(modelIdentity, resolvedModelSignature, contributions, missingPartDetails);
            lock (_cacheLock)
                _aggregateCache[aggregateCacheKey] = aggregate;
        }

        var resolutionTraceFinal = string.Join(" ", resolutionAttempts.Where(static attempt => !string.IsNullOrWhiteSpace(attempt)));
        return BuildResult(aggregate, settings, previousModelSignature, cacheHit, resolutionTraceFinal);
    }

    private AdvancedBodyScalingBoneImportanceResult BuildResult(
        CachedAggregateImportance cached,
        AdvancedBodyScalingSettings settings,
        string previousModelSignature,
        bool cacheHit,
        string resolutionTrace)
    {
        var signatureChanged = !string.Equals(previousModelSignature, cached.ResolvedModelSignature, StringComparison.OrdinalIgnoreCase);
        var refreshStatus = BuildRefreshStatus(previousModelSignature, cached.ResolvedModelSignature, cacheHit, rebuiltAggregate: !cacheHit, usingFallback: false);

        if (!cached.Stage1Available && !cached.Stage2Available)
        {
            return AdvancedBodyScalingBoneImportanceResult.CreateFallback(
                string.IsNullOrWhiteSpace(cached.FailureReason)
                    ? "The resolved model did not expose usable body-bone participation data."
                    : cached.FailureReason,
                enabled: true,
                preferSkinWeights: settings.PreferTrueSkinWeightImportance,
                heuristicBlend: settings.BoneImportanceHeuristicBlend,
                modelSignature: cached.ResolvedModelSignature,
                modelIdentity: cached.AggregateIdentity,
                requestedGamePath: cached.RequestedPathsSummary,
                resolvedModelPath: cached.ResolvedPathsSummary,
                resolutionDetail: cached.Summary,
                resolutionTrace: resolutionTrace,
                modelSignatureChanged: signatureChanged,
                refreshStatus: refreshStatus,
                partDetails: cached.PreferredPartDetails,
                missingPartDetails: cached.MissingPartDetails);
        }

        IReadOnlyDictionary<string, float> selectedScores;
        IReadOnlyDictionary<string, float> selectedAreaScores;
        AdvancedBodyScalingBoneImportanceSource source;
        string summary;
        IReadOnlyList<string> partDetails;

        if (settings.PreferTrueSkinWeightImportance && cached.UsesMixedAggregate)
        {
            selectedScores = cached.MixedScores;
            selectedAreaScores = cached.MixedAreaScores;
            source = AdvancedBodyScalingBoneImportanceSource.MixedAggregate;
            summary = $"Importance source: mixed aggregate. {cached.Summary}";
            partDetails = cached.PreferredPartDetails;
        }
        else if (settings.PreferTrueSkinWeightImportance && cached.Stage2Available)
        {
            selectedScores = cached.SkinScores;
            selectedAreaScores = cached.SkinAreaScores;
            source = AdvancedBodyScalingBoneImportanceSource.ModelWeights;
            summary = $"Importance source: model weights. {cached.Summary}";
            partDetails = cached.PreferredPartDetails;
        }
        else
        {
            selectedScores = cached.CoarseScores;
            selectedAreaScores = cached.CoarseAreaScores;
            source = AdvancedBodyScalingBoneImportanceSource.CoarseParticipation;
            summary = settings.PreferTrueSkinWeightImportance && !cached.Stage2Available
                ? $"Importance source: coarse participation. Stage 2 skin weights were unavailable, so the system fell back to Stage 1. {cached.Summary}"
                : $"Importance source: coarse participation. {cached.Summary}";
            partDetails = cached.CoarsePartDetails;
        }

        return new AdvancedBodyScalingBoneImportanceResult
        {
            Enabled = true,
            PreferTrueSkinWeightImportance = settings.PreferTrueSkinWeightImportance,
            HeuristicBlend = settings.BoneImportanceHeuristicBlend,
            Source = source,
            ResolutionSource = cached.ResolutionSource,
            ModelSignature = cached.ResolvedModelSignature,
            ModelSignatureChanged = signatureChanged,
            ModelIdentity = cached.AggregateIdentity,
            RequestedGamePath = cached.RequestedPathsSummary,
            ModelPath = cached.ResolvedPathsSummary,
            ResolutionDetail = cached.Summary,
            ResolutionTrace = resolutionTrace,
            RefreshStatus = refreshStatus,
            CacheHit = cacheHit,
            Stage1Available = cached.Stage1Available,
            Stage2Available = cached.Stage2Available,
            MultiModelAggregate = cached.MultiModelAggregate,
            ContributingPartCount = cached.ContributingPartCount,
            AreaAwareRefinementActive = cached.AreaAwareRefinementActive,
            ClassificationRefinementActive = cached.ClassificationRefinementActive,
            ConfidenceWeightedAggregationActive = cached.ConfidenceWeightedAggregationActive,
            RefinementSummary = BuildRefinementSummary(selectedScores),
            ConfidenceSummary = cached.ConfidenceSummary,
            Summary = summary,
            Scores = selectedScores,
            SampleValues = BuildRefinedSampleValues(selectedScores, selectedAreaScores),
            PartDetails = partDetails,
            MissingPartDetails = cached.MissingPartDetails,
        };
    }

    private List<ModelContribution> ResolveModelContributions(
        Actor actor,
        IReadOnlyList<ModelPartRequest> partRequests,
        List<string> resolutionAttempts,
        List<string> missingPartDetails)
    {
        var contributions = new List<ModelContribution>();

        foreach (var request in partRequests)
        {
            if (request.Candidates.Count == 0)
            {
                missingPartDetails.Add($"{request.Label}: no active model candidate was available, so this slot was skipped.");
                continue;
            }

            var references = ResolveModelReferences(actor, request.Candidates, resolutionAttempts);
            if (references.Count == 0)
            {
                missingPartDetails.Add($"{request.Label}: no usable resolved model path was available, so this slot was skipped.");
                continue;
            }

            var resolved = false;
            foreach (var reference in references)
            {
                var (cached, cacheHit) = GetOrParseModelImportance(reference);
                if (cached.Stage1Available || cached.Stage2Available)
                {
                    contributions.Add(new ModelContribution(
                        request.Part,
                        request.Label,
                        request.AggregateWeight,
                        reference,
                        cached,
                        cacheHit));
                    resolved = true;
                    break;
                }

                if (!string.IsNullOrWhiteSpace(cached.FailureReason))
                    resolutionAttempts.Add($"{request.Label}: {reference.ResolutionDetail} failed. {cached.FailureReason}");
            }

            if (!resolved)
                missingPartDetails.Add($"{request.Label}: all resolution attempts were unusable, so this slot was skipped.");
        }

        return contributions;
    }

    private (CachedModelImportance Cached, bool CacheHit) GetOrParseModelImportance(ResolvedModelReference reference)
    {
        CachedModelImportance cached;
        var cacheHit = false;
        lock (_cacheLock)
        {
            if (_modelCache.TryGetValue(reference.CacheKey, out cached!))
                cacheHit = true;
        }

        if (!cacheHit)
        {
            cached = ParseModelImportance(reference);
            lock (_cacheLock)
                _modelCache[reference.CacheKey] = cached;
        }

        return (cached, cacheHit);
    }

    private static string BuildResolvedModelSignature(
        IReadOnlyList<ModelPartRequest> requests,
        IReadOnlyList<ModelContribution> contributions)
    {
        var contributionsByPart = contributions.ToDictionary(static contribution => contribution.Part);
        return string.Join("||", requests
            .OrderBy(static request => request.Part)
            .Select(request =>
            {
                if (!contributionsByPart.TryGetValue(request.Part, out var contribution))
                    return $"{request.Part}:missing";

                return $"{request.Part}:{contribution.Reference.ResolutionSource}:{contribution.Reference.CacheKey}:{contribution.Cached.Stage1Available}:{contribution.Cached.Stage2Available}";
              }));
    }

    private static string BuildReferenceSignature(
        IReadOnlyList<ModelPartRequest> requests,
        IReadOnlyDictionary<BodyModelPart, ResolvedModelReference> referencesByPart)
        => string.Join("||", requests
            .OrderBy(static request => request.Part)
            .Select(request =>
            {
                if (!referencesByPart.TryGetValue(request.Part, out var reference))
                    return $"{request.Part}:missing";

                return $"{request.Part}:{reference.ResolutionSource}:{reference.CacheKey}";
            }));

    private static string BuildAggregateCacheKey(string resolvedModelSignature)
        => resolvedModelSignature;

    private static string BuildRefreshStatus(
        string previousModelSignature,
        string currentModelSignature,
        bool cacheHit,
        bool rebuiltAggregate,
        bool usingFallback)
    {
        var signatureChanged = !string.Equals(previousModelSignature, currentModelSignature, StringComparison.OrdinalIgnoreCase);
        if (signatureChanged)
        {
            if (usingFallback)
                return "Resolved model signature changed, but no usable aggregate could be built so heuristic fallback remained active.";

            return rebuiltAggregate
                ? "Resolved model signature changed, so the bone-importance aggregate was rebuilt."
                : "Resolved model signature changed, so a cached aggregate for the new model set was applied.";
        }

        if (usingFallback)
            return "Resolved model signature was unchanged, so the existing heuristic fallback state was kept.";

        return cacheHit
            ? "Resolved model signature was unchanged, so the cached aggregate was reused."
            : "Resolved model signature was unchanged, so the aggregate remained valid without a rebuild.";
    }

    private CachedAggregateImportance BuildAggregateImportance(
        string modelIdentity,
        string resolvedModelSignature,
        IReadOnlyList<ModelContribution> contributions,
        IReadOnlyList<string> missingPartDetails)
    {
        var stage1Contributions = contributions.Where(static contribution => contribution.Cached.Stage1Available).ToList();
        var stage2Contributions = contributions.Where(static contribution => contribution.Cached.Stage2Available).ToList();
        var mixedContributions = contributions
            .Where(static contribution => contribution.Cached.Stage1Available || contribution.Cached.Stage2Available)
            .ToList();
        var coarseConfidence = stage1Contributions.ToDictionary(
            static contribution => contribution.Part,
            static contribution => EvaluateSlotConfidence(contribution, useStage2: false));
        var preferredConfidence = contributions.ToDictionary(
            static contribution => contribution.Part,
            static contribution => EvaluateSlotConfidence(contribution, useStage2: contribution.Cached.Stage2Available));
        var coarseDetailConfidence = contributions.ToDictionary(
            static contribution => contribution.Part,
            static contribution => EvaluateSlotConfidence(contribution, useStage2: !contribution.Cached.Stage1Available && contribution.Cached.Stage2Available));
        var mixedConfidence = mixedContributions.ToDictionary(
            static contribution => contribution.Part,
            static contribution => EvaluateSlotConfidence(contribution, useStage2: contribution.Cached.Stage2Available));

        var coarseAreaScores = AggregateScores(
            stage1Contributions,
            static contribution => contribution.Cached.CoarseAreaScores,
            contribution => coarseConfidence[contribution.Part].EffectiveWeight,
            CoarseNormalizationFloor);
        var skinAreaScores = AggregateScores(
            stage2Contributions,
            static contribution => MergeScores(contribution.Cached.SkinAreaScores, contribution.Cached.CoarseAreaScores),
            contribution => preferredConfidence[contribution.Part].EffectiveWeight,
            SkinNormalizationFloor);
        var mixedAreaScores = AggregateScores(
            mixedContributions,
            static contribution => contribution.Cached.Stage2Available
                ? MergeScores(contribution.Cached.SkinAreaScores, contribution.Cached.CoarseAreaScores)
                : contribution.Cached.CoarseAreaScores,
            contribution => mixedConfidence[contribution.Part].EffectiveWeight,
            SkinNormalizationFloor);
        var coarseScores = ApplyClassificationRefinement(coarseAreaScores);
        var skinScores = ApplyClassificationRefinement(skinAreaScores);
        var mixedScores = ApplyClassificationRefinement(mixedAreaScores);

        var anyPenumbra = contributions.Any(static contribution
            => contribution.Reference.ResolutionSource == AdvancedBodyScalingBoneImportanceResolutionSource.PenumbraResolvedModel);
        var resolutionSource = contributions.Count > 1
            ? anyPenumbra
                ? AdvancedBodyScalingBoneImportanceResolutionSource.PenumbraResolvedAggregate
                : AdvancedBodyScalingBoneImportanceResolutionSource.VanillaAggregate
            : contributions[0].Reference.ResolutionSource;

        var preferredPartDetails = contributions
            .Select(contribution => BuildPartDetail(contribution, preferStage2WhenAvailable: true, preferredConfidence[contribution.Part]))
            .ToList();
        var coarsePartDetails = contributions
            .Select(contribution => BuildPartDetail(contribution, preferStage2WhenAvailable: false, coarseDetailConfidence[contribution.Part]))
            .ToList();

        var requestedPathsSummary = string.Join(" | ", contributions.Select(static contribution
            => $"{contribution.PartLabel}: {contribution.Reference.RequestedGamePath}"));
        var resolvedPathsSummary = string.Join(" | ", contributions.Select(static contribution
            => $"{contribution.PartLabel}: {contribution.Reference.ResolvedPath}"));

        var resolvedPartLabels = string.Join(", ", contributions.Select(static contribution => contribution.PartLabel));
        var summary = contributions.Count > 1
            ? $"Whole-body aggregate using {contributions.Count} model parts ({resolvedPartLabels})."
            : $"Single-model importance using {contributions[0].PartLabel}.";

        if (coarseScores.Count > 0 || skinScores.Count > 0 || mixedScores.Count > 0)
            summary = $"{summary} Area-aware refinement blends influence, vertex coverage, triangle coverage, and mesh/submesh participation before structural classification is applied.";

        summary = $"{summary} Confidence-weighted slot aggregation tempers each slot by source quality, structural breadth, and local/detail bias before final normalization.";

        if (missingPartDetails.Count > 0)
            summary = $"{summary} Missing slots were skipped conservatively.";

        var refinementScores = mixedScores.Count > 0
            ? mixedScores
            : skinScores.Count > 0
                ? skinScores
                : coarseScores;
        var refinementSummary = BuildRefinementSummary(refinementScores);
        var confidenceSummary = BuildConfidenceSummary(mixedConfidence.Count > 0 ? mixedConfidence.Values : preferredConfidence.Values);

        return new CachedAggregateImportance
        {
            AggregateIdentity = modelIdentity,
            ResolvedModelSignature = resolvedModelSignature,
            Stage1Available = coarseScores.Count > 0,
            Stage2Available = skinScores.Count > 0,
            UsesMixedAggregate = stage2Contributions.Count > 0 && mixedContributions.Count > stage2Contributions.Count,
            MultiModelAggregate = contributions.Count > 1,
            ContributingPartCount = contributions.Count,
            ResolutionSource = resolutionSource,
            CoarseAreaScores = coarseAreaScores,
            CoarseScores = coarseScores,
            SkinAreaScores = skinAreaScores,
            SkinScores = skinScores,
            MixedAreaScores = mixedAreaScores,
            MixedScores = mixedScores,
            PreferredPartDetails = preferredPartDetails,
            CoarsePartDetails = coarsePartDetails,
            MissingPartDetails = missingPartDetails.ToList(),
            RequestedPathsSummary = requestedPathsSummary,
            ResolvedPathsSummary = resolvedPathsSummary,
            AreaAwareRefinementActive = coarseAreaScores.Count > 0 || skinAreaScores.Count > 0 || mixedAreaScores.Count > 0,
            ClassificationRefinementActive = coarseScores.Count > 0 || skinScores.Count > 0 || mixedScores.Count > 0,
            ConfidenceWeightedAggregationActive = contributions.Count > 0,
            RefinementSummary = refinementSummary,
            ConfidenceSummary = confidenceSummary,
            Summary = summary,
            FailureReason = contributions.Count == 0
                ? "No usable model contributions were available."
                : string.Empty,
        };
    }

    private static IReadOnlyDictionary<string, float> AggregateScores(
        IReadOnlyList<ModelContribution> contributions,
        Func<ModelContribution, IReadOnlyDictionary<string, float>> selector,
        Func<ModelContribution, float> weightSelector,
        float floor)
    {
        if (contributions.Count == 0)
            return new Dictionary<string, float>(StringComparer.Ordinal);

        var totalWeight = contributions.Sum(contribution => Math.Max(Epsilon, weightSelector(contribution)));
        if (totalWeight <= Epsilon)
            totalWeight = contributions.Count;

        var raw = new Dictionary<string, float>(StringComparer.Ordinal);
        foreach (var contribution in contributions)
        {
            var scores = selector(contribution);
            if (scores.Count == 0)
                continue;

            var normalizedWeight = Math.Max(Epsilon, weightSelector(contribution)) / totalWeight;
            foreach (var (bone, value) in scores)
            {
                raw[bone] = raw.TryGetValue(bone, out var existing)
                    ? existing + (value * normalizedWeight)
                    : value * normalizedWeight;
            }
        }

        return NormalizeScores(raw, floor);
    }

    private static string BuildPartDetail(ModelContribution contribution, bool preferStage2WhenAvailable, SlotConfidenceEvaluation confidence)
    {
        var stageLabel = preferStage2WhenAvailable && contribution.Cached.Stage2Available
            ? "Stage 2 skin weights"
            : contribution.Cached.Stage1Available
                ? "Stage 1 coarse participation"
                : contribution.Cached.Stage2Available
                    ? "Stage 2 skin weights"
                    : "unusable";

        var resolutionLabel = contribution.Reference.ResolutionSource switch
        {
            AdvancedBodyScalingBoneImportanceResolutionSource.PenumbraResolvedModel => "Penumbra-resolved model",
            AdvancedBodyScalingBoneImportanceResolutionSource.VanillaModelPath => "vanilla model path",
            _ => "resolved model",
        };

        return $"{contribution.PartLabel}: {stageLabel} via {resolutionLabel} ({contribution.Reference.ResolutionDetail}, aggregate weight {contribution.AggregateWeight:0.00}, confidence {confidence.Confidence:0.00} -> effective {confidence.EffectiveWeight:0.00}; {confidence.Summary}); requested {contribution.Reference.RequestedGamePath} -> resolved {contribution.Reference.ResolvedPath}";
    }

    private CachedModelImportance ParseModelImportance(ResolvedModelReference reference)
    {
        try
        {
            if (!TryReadModelBytes(reference.ResolvedPath, out var data, out var loadFailureReason))
            {
                return new CachedModelImportance
                {
                    ModelPath = reference.ResolvedPath,
                    Stage1Available = false,
                    Stage2Available = false,
                    CoarseProfile = ImportanceCoverageProfile.Empty,
                    CoarseAreaScores = new Dictionary<string, float>(StringComparer.Ordinal),
                    CoarseScores = new Dictionary<string, float>(StringComparer.Ordinal),
                    SkinProfile = ImportanceCoverageProfile.Empty,
                    SkinAreaScores = new Dictionary<string, float>(StringComparer.Ordinal),
                    SkinScores = new Dictionary<string, float>(StringComparer.Ordinal),
                    Summary = "The resolved model path could not be loaded from the selected source.",
                    FailureReason = loadFailureReason,
                };
            }

            var mdl = new MdlFile(data);
            if (!mdl.Valid)
            {
                return new CachedModelImportance
                {
                    ModelPath = reference.ResolvedPath,
                    Stage1Available = false,
                    Stage2Available = false,
                    CoarseProfile = ImportanceCoverageProfile.Empty,
                    CoarseAreaScores = new Dictionary<string, float>(StringComparer.Ordinal),
                    CoarseScores = new Dictionary<string, float>(StringComparer.Ordinal),
                    SkinProfile = ImportanceCoverageProfile.Empty,
                    SkinAreaScores = new Dictionary<string, float>(StringComparer.Ordinal),
                    SkinScores = new Dictionary<string, float>(StringComparer.Ordinal),
                    Summary = "The resolved model could not be parsed into a valid MdlFile.",
                    FailureReason = $"Resolved model path '{reference.ResolvedPath}' could not be parsed as an MdlFile.",
                };
            }

            var coarseAreaScores = BuildAreaAwareScores(BuildCoarseParticipationMetrics(mdl), CoarseNormalizationFloor);
            var skinAreaScores = BuildAreaAwareScores(BuildSkinWeightMetrics(mdl), SkinNormalizationFloor);
            var coarseProfile = BuildCoverageProfile(coarseAreaScores);
            var skinProfile = BuildCoverageProfile(skinAreaScores);
            var coarseScores = ApplyClassificationRefinement(coarseAreaScores);
            var skinScores = ApplyClassificationRefinement(skinAreaScores);
            var stage1Available = coarseScores.Count > 0;
            var stage2Available = skinScores.Count > 0;

            return new CachedModelImportance
            {
                ModelPath = reference.ResolvedPath,
                Stage1Available = stage1Available,
                Stage2Available = stage2Available,
                CoarseProfile = coarseProfile,
                CoarseAreaScores = coarseAreaScores,
                CoarseScores = coarseScores,
                SkinProfile = skinProfile,
                SkinAreaScores = skinAreaScores,
                SkinScores = skinScores,
                Summary = stage2Available
                    ? $"Parsed resolved model '{reference.ResolvedPath}' with Stage 1 coarse participation and Stage 2 skin-weight aggregation, refined by approximate surface coverage and structural bone classification."
                    : stage1Available
                        ? $"Parsed resolved model '{reference.ResolvedPath}' with Stage 1 coarse participation, refined by approximate surface coverage and structural bone classification; no usable blend-weight stream was found for Stage 2."
                        : $"Parsed resolved model '{reference.ResolvedPath}', but no supported body-bone participation was detected.",
                FailureReason = stage1Available || stage2Available
                    ? string.Empty
                    : $"Resolved model path '{reference.ResolvedPath}' did not expose usable body-bone participation data.",
            };
        }
        catch (Exception ex)
        {
            _logger.Warning($"Failed to parse model-derived bone importance for '{reference.ResolvedPath}': {ex.Message}");
            return new CachedModelImportance
            {
                ModelPath = reference.ResolvedPath,
                Stage1Available = false,
                Stage2Available = false,
                CoarseProfile = ImportanceCoverageProfile.Empty,
                CoarseAreaScores = new Dictionary<string, float>(StringComparer.Ordinal),
                CoarseScores = new Dictionary<string, float>(StringComparer.Ordinal),
                SkinProfile = ImportanceCoverageProfile.Empty,
                SkinAreaScores = new Dictionary<string, float>(StringComparer.Ordinal),
                SkinScores = new Dictionary<string, float>(StringComparer.Ordinal),
                Summary = "Model-derived importance parsing failed, so heuristic fallback remains active.",
                FailureReason = $"Parsing failed for '{reference.ResolvedPath}'.",
            };
        }
    }

    private static Dictionary<string, BoneMetricAccumulator> BuildCoarseParticipationMetrics(MdlFile mdl)
    {
        var metrics = new Dictionary<string, BoneMetricAccumulator>(StringComparer.Ordinal);

        foreach (var meshIndex in EnumeratePrimaryMeshIndices(mdl))
        {
            if (!TryGetMesh(mdl, meshIndex, out var mesh) || !TryGetBoneTable(mdl, mesh.BoneTableIndex, out var boneTable))
                continue;

            var meshVertexCount = Math.Max(1f, mesh.VertexCount);
            var meshTriangleCount = Math.Max(1f, mesh.IndexCount / 3f);
            var meshBones = EnumerateActualBoneIndices(mdl, boneTable).Distinct().ToArray();
            if (meshBones.Length > 0)
            {
                var meshWeight = 1f + (mesh.SubMeshCount * 0.18f);
                var perBoneVertexCoverage = meshVertexCount / meshBones.Length;
                var perBoneTriangleCoverage = meshTriangleCount / meshBones.Length;
                foreach (var actualBoneIndex in meshBones)
                {
                    AddMetric(
                        metrics,
                        mdl.Bones[actualBoneIndex],
                        influence: meshWeight,
                        vertexCoverage: perBoneVertexCoverage * 0.18f,
                        meshParticipation: 1f,
                        triangleCoverage: perBoneTriangleCoverage * 0.18f);
                }
            }

            var submeshVertexEstimate = mesh.SubMeshCount > 0 ? meshVertexCount / mesh.SubMeshCount : meshVertexCount;
            var submeshTriangleEstimate = mesh.SubMeshCount > 0 ? meshTriangleCount / mesh.SubMeshCount : meshTriangleCount;
            for (var i = 0; i < mesh.SubMeshCount; i++)
            {
                var subMeshIndex = mesh.SubMeshIndex + i;
                if (!TryGetSubmesh(mdl, subMeshIndex, out var submesh))
                    continue;

                var submeshBones = EnumerateSubmeshBoneIndices(mdl, boneTable, submesh).Distinct().ToArray();
                if (submeshBones.Length == 0)
                    continue;

                var perBoneVertexCoverage = submeshVertexEstimate / submeshBones.Length;
                var perBoneTriangleCoverage = submeshTriangleEstimate / submeshBones.Length;
                foreach (var actualBoneIndex in submeshBones)
                {
                    AddMetric(
                        metrics,
                        mdl.Bones[actualBoneIndex],
                        influence: 0.35f,
                        vertexCoverage: perBoneVertexCoverage * 0.22f,
                        submeshParticipation: 1f,
                        triangleCoverage: perBoneTriangleCoverage * 0.22f);
                }
            }
        }

        return metrics;
    }

    private static Dictionary<string, BoneMetricAccumulator> BuildSkinWeightMetrics(MdlFile mdl)
    {
        var metrics = new Dictionary<string, BoneMetricAccumulator>(StringComparer.Ordinal);

        foreach (var meshIndex in EnumeratePrimaryMeshIndices(mdl))
        {
            if (!TryGetMesh(mdl, meshIndex, out var mesh) ||
                !TryGetBoneTable(mdl, mesh.BoneTableIndex, out var boneTable) ||
                !TryGetLodForMesh(mdl, meshIndex, out var lod) ||
                !TryResolveVertexDeclaration(mdl, meshIndex, out var declaration) ||
                !TryResolveBlendElements(declaration, out var blendIndices, out var blendWeights) ||
                !TryGetVertexStreamBaseOffset(mdl, lod.VertexDataOffset, mesh, blendIndices.Stream, out var indicesStreamOffset) ||
                !TryGetVertexStreamBaseOffset(mdl, lod.VertexDataOffset, mesh, blendWeights.Stream, out var weightsStreamOffset))
            {
                continue;
            }

            var indexStride = mesh.VertexBufferStride(blendIndices.Stream);
            var weightStride = mesh.VertexBufferStride(blendWeights.Stream);
            if (indexStride <= 0 || weightStride <= 0)
                continue;

            var meshInfluence = new Dictionary<string, float>(StringComparer.Ordinal);
            var meshVertexCoverage = new Dictionary<string, float>(StringComparer.Ordinal);
            var indicesBuffer = new int[4];
            var weightsBuffer = new float[4];
            for (var vertexIndex = 0; vertexIndex < mesh.VertexCount; vertexIndex++)
            {
                var indicesOffset = indicesStreamOffset + (vertexIndex * indexStride) + blendIndices.Offset;
                var weightsOffset = weightsStreamOffset + (vertexIndex * weightStride) + blendWeights.Offset;

                if (!TrySliceVertexData(mdl.RemainingData, indicesOffset, GetTypeSize(blendIndices.Type), out var indexData) ||
                    !TrySliceVertexData(mdl.RemainingData, weightsOffset, GetTypeSize(blendWeights.Type), out var weightData))
                    continue;

                Span<int> indices = indicesBuffer;
                Span<float> weights = weightsBuffer;
                if (!TryReadBlendIndices(indexData, blendIndices.Type, indices, out var indexCount) ||
                    !TryReadBlendWeights(weightData, blendWeights.Type, weights, out var weightCount))
                    continue;

                var count = Math.Min(indexCount, weightCount);
                if (count <= 0)
                    continue;

                NormalizeWeights(weights[..count]);
                for (var i = 0; i < count; i++)
                {
                    var weight = weights[i];
                    if (weight <= Epsilon)
                        continue;

                    var localBoneIndex = indices[i];
                    if (localBoneIndex < 0 || localBoneIndex >= boneTable.BoneCount || localBoneIndex >= boneTable.BoneIndex.Length)
                        continue;

                    var actualBoneIndex = boneTable.BoneIndex[localBoneIndex];
                    if (actualBoneIndex >= mdl.Bones.Length)
                        continue;

                    var boneName = mdl.Bones[actualBoneIndex];
                    if (!IsRelevantBodyBone(boneName))
                        continue;

                    AddAmount(meshInfluence, boneName, weight);
                    AddAmount(meshVertexCoverage, boneName, MathF.Sqrt(weight));
                }
            }

            if (meshInfluence.Count == 0)
                continue;

            var meshTriangleCount = Math.Max(1f, mesh.IndexCount / 3f);
            var meshTotalInfluence = meshInfluence.Values.Sum();
            foreach (var (boneName, influence) in meshInfluence)
            {
                var share = meshTotalInfluence <= Epsilon ? 0f : influence / meshTotalInfluence;
                AddMetric(
                    metrics,
                    boneName,
                    influence: influence,
                    vertexCoverage: meshVertexCoverage.TryGetValue(boneName, out var coverage) ? coverage : 0f,
                    meshParticipation: 1f,
                    triangleCoverage: meshTriangleCount * share);
            }

            var submeshVertexEstimate = mesh.SubMeshCount > 0 ? Math.Max(1f, mesh.VertexCount) / mesh.SubMeshCount : Math.Max(1f, mesh.VertexCount);
            var submeshTriangleEstimate = mesh.SubMeshCount > 0 ? meshTriangleCount / mesh.SubMeshCount : meshTriangleCount;
            for (var i = 0; i < mesh.SubMeshCount; i++)
            {
                var subMeshIndex = mesh.SubMeshIndex + i;
                if (!TryGetSubmesh(mdl, subMeshIndex, out var submesh))
                    continue;

                var submeshBones = EnumerateSubmeshBoneIndices(mdl, boneTable, submesh).Distinct().ToArray();
                if (submeshBones.Length == 0)
                    continue;

                var perBoneVertexCoverage = submeshVertexEstimate / submeshBones.Length;
                var perBoneTriangleCoverage = submeshTriangleEstimate / submeshBones.Length;
                foreach (var actualBoneIndex in submeshBones)
                {
                    AddMetric(
                        metrics,
                        mdl.Bones[actualBoneIndex],
                        vertexCoverage: perBoneVertexCoverage * 0.10f,
                        submeshParticipation: 1f,
                        triangleCoverage: perBoneTriangleCoverage * 0.16f);
                }
            }
        }

        return metrics;
    }

    private static Dictionary<string, float> BuildAreaAwareScores(
        IReadOnlyDictionary<string, BoneMetricAccumulator> metrics,
        float floor)
    {
        if (metrics.Count == 0)
            return new Dictionary<string, float>(StringComparer.Ordinal);

        var maxInfluence = metrics.Values.Max(static metric => metric.Influence);
        var maxVertexCoverage = metrics.Values.Max(static metric => metric.VertexCoverage);
        var maxMeshParticipation = metrics.Values.Max(static metric => metric.MeshParticipation);
        var maxSubmeshParticipation = metrics.Values.Max(static metric => metric.SubmeshParticipation);
        var maxTriangleCoverage = metrics.Values.Max(static metric => metric.TriangleCoverage);
        var raw = new Dictionary<string, float>(StringComparer.Ordinal);
        foreach (var (boneName, metric) in metrics)
        {
            var influenceRatio = NormalizeMetric(metric.Influence, maxInfluence);
            var vertexRatio = NormalizeMetric(metric.VertexCoverage, maxVertexCoverage);
            var meshRatio = NormalizeMetric(metric.MeshParticipation, maxMeshParticipation);
            var submeshRatio = NormalizeMetric(metric.SubmeshParticipation, maxSubmeshParticipation);
            var triangleRatio = NormalizeMetric(metric.TriangleCoverage, maxTriangleCoverage);
            raw[boneName] =
                (influenceRatio * 0.42f) +
                (vertexRatio * 0.23f) +
                (triangleRatio * 0.17f) +
                (meshRatio * 0.10f) +
                (submeshRatio * 0.08f);
        }

        return NormalizeScores(raw, floor);
    }

    private static Dictionary<string, float> ApplyClassificationRefinement(IReadOnlyDictionary<string, float> areaScores)
    {
        var refined = new Dictionary<string, float>(StringComparer.Ordinal);
        foreach (var (boneName, areaScore) in areaScores)
        {
            var classification = ClassifyBone(boneName);
            if (classification == BoneImportanceClassification.Ignored)
                continue;

            var multiplier = GetClassificationMultiplier(classification);
            var score = Math.Clamp(areaScore * multiplier, 0f, 1f);
            if (score <= MinimumActiveScore)
                continue;

            refined[boneName] = score;
        }

        return refined;
    }

    private static IReadOnlyDictionary<string, float> MergeScores(IReadOnlyDictionary<string, float> primary, IReadOnlyDictionary<string, float> secondary)
    {
        var merged = new Dictionary<string, float>(StringComparer.Ordinal);
        foreach (var (bone, value) in secondary)
            merged[bone] = value * 0.35f;

        foreach (var (bone, value) in primary)
        {
            var coarse = merged.TryGetValue(bone, out var existing) ? existing : 0f;
            merged[bone] = Math.Clamp((value * 0.85f) + coarse, 0f, 1f);
        }

        return merged;
    }

    private static Dictionary<string, float> NormalizeScores(Dictionary<string, float> rawScores, float floor)
    {
        if (rawScores.Count == 0)
            return new Dictionary<string, float>(StringComparer.Ordinal);

        var max = rawScores.Values.Max();
        if (max <= Epsilon)
            return new Dictionary<string, float>(StringComparer.Ordinal);

        var normalized = new Dictionary<string, float>(StringComparer.Ordinal);
        foreach (var (bone, raw) in rawScores)
        {
            var ratio = Math.Clamp(raw / max, 0f, 1f);
            normalized[bone] = Math.Clamp(floor + ((1f - floor) * MathF.Sqrt(ratio)), floor, 1f);
        }

        return normalized;
    }

    private static IReadOnlyList<string> BuildRefinedSampleValues(
        IReadOnlyDictionary<string, float> finalScores,
        IReadOnlyDictionary<string, float> areaScores)
    {
        if (finalScores.Count == 0)
            return Array.Empty<string>();

        var notable = new[]
        {
            "j_sebo_c",
            "j_kubi",
            "j_sako_l",
            "j_ude_a_l",
            "j_te_l",
            "j_kosi",
            "j_asi_a_l",
            "j_asi_c_l",
            "j_asi_d_l",
        };

        var samples = new List<string>();
        var used = new HashSet<string>(StringComparer.Ordinal);

        foreach (var bone in notable)
        {
            if (!finalScores.ContainsKey(bone))
                continue;

            used.Add(bone);
            samples.Add(BuildSampleLine(bone, finalScores, areaScores));
            if (samples.Count >= 6)
                return samples;
        }

        foreach (var (bone, _) in finalScores
                     .OrderByDescending(static kvp => kvp.Value)
                     .ThenBy(static kvp => kvp.Key, StringComparer.Ordinal))
        {
            if (!used.Add(bone))
                continue;

            samples.Add(BuildSampleLine(bone, finalScores, areaScores));
            if (samples.Count >= 6)
                break;
        }

        return samples;
    }

    private static string BuildSampleLine(
        string boneName,
        IReadOnlyDictionary<string, float> finalScores,
        IReadOnlyDictionary<string, float> areaScores)
    {
        var finalScore = finalScores.TryGetValue(boneName, out var refined) ? refined : 0f;
        var areaScore = areaScores.TryGetValue(boneName, out var area) ? area : finalScore;
        return $"{BoneData.GetBoneDisplayName(boneName)} ({boneName}) {finalScore:0.00} [{GetClassificationLabel(ClassifyBone(boneName))}, area {areaScore:0.00} -> final {finalScore:0.00}]";
    }

    private static string BuildRefinementSummary(IReadOnlyDictionary<string, float> scores)
    {
        if (scores.Count == 0)
            return "Area-aware and classification-aware refinement were not active for this aggregate.";

        var core = 0;
        var secondary = 0;
        var deemphasized = 0;
        foreach (var boneName in scores.Keys)
        {
            switch (ClassifyBone(boneName))
            {
                case BoneImportanceClassification.CoreStructural:
                    core++;
                    break;
                case BoneImportanceClassification.SecondaryLocal:
                    secondary++;
                    break;
                case BoneImportanceClassification.DeemphasizedLocal:
                    deemphasized++;
                    break;
            }
        }

        return $"Area-aware refinement uses influence, vertex coverage, triangle coverage, and mesh/submesh participation. Classification-aware refinement detected {core} core, {secondary} secondary, and {deemphasized} de-emphasized bones in the active map.";
    }

    private static ImportanceCoverageProfile BuildCoverageProfile(IReadOnlyDictionary<string, float> scores)
    {
        if (scores.Count == 0)
            return ImportanceCoverageProfile.Empty;

        var total = scores.Values.Sum();
        if (total <= Epsilon)
            return ImportanceCoverageProfile.Empty;

        var meaningfulBoneCount = 0;
        var coreCount = 0;
        var secondaryCount = 0;
        var deemphasizedCount = 0;
        var coreTotal = 0f;
        var secondaryTotal = 0f;
        var deemphasizedTotal = 0f;
        foreach (var (boneName, score) in scores)
        {
            if (score >= 0.16f)
                meaningfulBoneCount++;

            switch (ClassifyBone(boneName))
            {
                case BoneImportanceClassification.CoreStructural:
                    coreCount++;
                    coreTotal += score;
                    break;
                case BoneImportanceClassification.SecondaryLocal:
                    secondaryCount++;
                    secondaryTotal += score;
                    break;
                case BoneImportanceClassification.DeemphasizedLocal:
                    deemphasizedCount++;
                    deemphasizedTotal += score;
                    break;
            }
        }

        var dominantShare = scores
            .OrderByDescending(static kvp => kvp.Value)
            .Take(3)
            .Sum(static kvp => kvp.Value) / total;
        return new ImportanceCoverageProfile
        {
            MeaningfulBoneCount = meaningfulBoneCount,
            CoreBoneCount = coreCount,
            SecondaryBoneCount = secondaryCount,
            DeemphasizedBoneCount = deemphasizedCount,
            CoreShare = coreTotal / total,
            SecondaryShare = secondaryTotal / total,
            DeemphasizedShare = deemphasizedTotal / total,
            DominantShare = Math.Clamp(dominantShare, 0f, 1f),
        };
    }

    private static SlotConfidenceEvaluation EvaluateSlotConfidence(ModelContribution contribution, bool useStage2)
    {
        var profile = useStage2 && contribution.Cached.Stage2Available
            ? contribution.Cached.SkinProfile
            : contribution.Cached.CoarseProfile;
        var sourceQuality = useStage2 && contribution.Cached.Stage2Available ? 1.00f : 0.74f;
        var baselineTrust = GetSlotBaselineTrust(contribution.Part);
        var expectedBones = GetExpectedMeaningfulBoneCount(contribution.Part);
        var breadthScore = Math.Clamp((float)profile.MeaningfulBoneCount / expectedBones, 0.20f, 1f);
        var structuralScore = Math.Clamp(
            (profile.CoreShare * 0.80f) +
            (profile.SecondaryShare * 0.52f) +
            ((1f - profile.DeemphasizedShare) * 0.18f),
            0.15f,
            1f);
        var localityScore = Math.Clamp(
            1f
            - Math.Clamp((profile.DominantShare - 0.52f) / 0.34f, 0f, 0.50f)
            - Math.Clamp((profile.DeemphasizedShare - 0.48f) / 0.32f, 0f, 0.25f),
            0.20f,
            1f);
        var representationScore = Math.Clamp((breadthScore * 0.65f) + (localityScore * 0.35f), 0.20f, 1f);
        var confidence = Math.Clamp(
            baselineTrust * (
                (sourceQuality * 0.34f) +
                (breadthScore * 0.22f) +
                (structuralScore * 0.28f) +
                (representationScore * 0.16f)),
            0.18f,
            1.05f);
        var effectiveWeight = contribution.AggregateWeight * confidence;
        var reasons = new List<string>
        {
            useStage2 && contribution.Cached.Stage2Available ? "Stage 2 skin-weight source" : "Stage 1 coarse source",
        };

        if (breadthScore >= 0.84f)
            reasons.Add("broad bone coverage");
        else if (breadthScore <= 0.56f)
            reasons.Add("narrow bone coverage");

        if (structuralScore >= 0.72f)
            reasons.Add("strong structural coverage");
        else if (structuralScore <= 0.48f)
            reasons.Add("limited structural coverage");

        if (localityScore <= 0.60f)
            reasons.Add("local/detail-heavy influence");

        if (baselineTrust < 0.80f)
            reasons.Add("extremity baseline trust");

        var adjustment = confidence < baselineTrust * 0.80f
            ? "down-weighted"
            : confidence > baselineTrust * 0.95f
                ? "trusted"
                : "tempered";
        return new SlotConfidenceEvaluation(
            Confidence: confidence,
            EffectiveWeight: effectiveWeight,
            UsesStage2: useStage2 && contribution.Cached.Stage2Available,
            Summary: $"{adjustment}; {string.Join(", ", reasons.Take(3))}");
    }

    private static string BuildConfidenceSummary(IEnumerable<SlotConfidenceEvaluation> evaluations)
    {
        var evaluationList = evaluations.ToList();
        if (evaluationList.Count == 0)
            return "Confidence-weighted slot aggregation was not active.";

        var strong = evaluationList.Count(static evaluation => evaluation.Confidence >= 0.80f);
        var tempered = evaluationList.Count(static evaluation => evaluation.Confidence >= 0.55f && evaluation.Confidence < 0.80f);
        var downWeighted = evaluationList.Count(static evaluation => evaluation.Confidence < 0.55f);
        return $"Confidence-weighted slot aggregation trusted {strong}, tempered {tempered}, and strongly down-weighted {downWeighted} contributing slot{(evaluationList.Count == 1 ? string.Empty : "s")} based on source quality, structural breadth, and local/detail bias.";
    }

    private static int GetExpectedMeaningfulBoneCount(BodyModelPart part)
        => part switch
        {
            BodyModelPart.TopBody => 18,
            BodyModelPart.BottomLegs => 16,
            BodyModelPart.HandsGloves => 10,
            BodyModelPart.FeetShoes => 10,
            _ => 12,
        };

    private static float GetSlotBaselineTrust(BodyModelPart part)
        => part switch
        {
            BodyModelPart.TopBody => 1.00f,
            BodyModelPart.BottomLegs => 0.96f,
            BodyModelPart.HandsGloves => 0.72f,
            BodyModelPart.FeetShoes => 0.70f,
            _ => 0.80f,
        };

    private static BoneImportanceClassification ClassifyBone(string boneName)
    {
        if (!IsRelevantBodyBone(boneName))
            return BoneImportanceClassification.Ignored;

        var family = BoneData.GetBoneFamily(boneName);
        if (IsFingerBone(boneName) || IsToeBone(boneName))
            return BoneImportanceClassification.DeemphasizedLocal;

        if (boneName.StartsWith("iv_", StringComparison.Ordinal) ||
            boneName.StartsWith("ya_", StringComparison.Ordinal))
        {
            return family is BoneData.BoneFamily.Spine or BoneData.BoneFamily.Chest or BoneData.BoneFamily.Groin or BoneData.BoneFamily.Legs or BoneData.BoneFamily.Hands or BoneData.BoneFamily.Feet
                ? BoneImportanceClassification.DeemphasizedLocal
                : BoneImportanceClassification.Ignored;
        }

        return family switch
        {
            BoneData.BoneFamily.Root => BoneImportanceClassification.CoreStructural,
            BoneData.BoneFamily.Spine => boneName is "n_root" or "n_hara" or "j_kosi" or "j_sebo_a" or "j_sebo_b" or "j_sebo_c" or "j_kubi"
                ? BoneImportanceClassification.CoreStructural
                : BoneData.IsDefaultBone(boneName)
                    ? BoneImportanceClassification.SecondaryLocal
                    : BoneImportanceClassification.DeemphasizedLocal,
            BoneData.BoneFamily.Chest => boneName.StartsWith("j_sako_", StringComparison.Ordinal)
                ? BoneImportanceClassification.CoreStructural
                : BoneData.IsDefaultBone(boneName)
                    ? BoneImportanceClassification.SecondaryLocal
                    : BoneImportanceClassification.DeemphasizedLocal,
            BoneData.BoneFamily.Groin => boneName == "j_kosi"
                ? BoneImportanceClassification.CoreStructural
                : BoneData.IsDefaultBone(boneName)
                    ? BoneImportanceClassification.SecondaryLocal
                    : BoneImportanceClassification.DeemphasizedLocal,
            BoneData.BoneFamily.Arms => boneName.StartsWith("n_hkata_", StringComparison.Ordinal) ||
                                         boneName.StartsWith("j_ude_a_", StringComparison.Ordinal) ||
                                         boneName.StartsWith("j_ude_b_", StringComparison.Ordinal)
                ? BoneImportanceClassification.CoreStructural
                : BoneData.IsDefaultBone(boneName)
                    ? BoneImportanceClassification.SecondaryLocal
                    : BoneImportanceClassification.DeemphasizedLocal,
            BoneData.BoneFamily.Hands => boneName.StartsWith("n_hte_", StringComparison.Ordinal) || boneName.StartsWith("j_te_", StringComparison.Ordinal)
                ? BoneImportanceClassification.SecondaryLocal
                : BoneImportanceClassification.DeemphasizedLocal,
            BoneData.BoneFamily.Legs => boneName.StartsWith("j_asi_a_", StringComparison.Ordinal) ||
                                         boneName.StartsWith("j_asi_b_", StringComparison.Ordinal) ||
                                         boneName.StartsWith("j_asi_c_", StringComparison.Ordinal)
                ? BoneImportanceClassification.CoreStructural
                : BoneData.IsDefaultBone(boneName)
                    ? BoneImportanceClassification.SecondaryLocal
                    : BoneImportanceClassification.DeemphasizedLocal,
            BoneData.BoneFamily.Feet => boneName.StartsWith("j_asi_d_", StringComparison.Ordinal)
                ? BoneImportanceClassification.SecondaryLocal
                : BoneImportanceClassification.DeemphasizedLocal,
            BoneData.BoneFamily.Tail => BoneImportanceClassification.DeemphasizedLocal,
            _ => BoneData.IsDefaultBone(boneName)
                ? BoneImportanceClassification.SecondaryLocal
                : BoneImportanceClassification.DeemphasizedLocal,
        };
    }

    private static float GetClassificationMultiplier(BoneImportanceClassification classification)
    {
        var target = classification switch
        {
            BoneImportanceClassification.CoreStructural => 1.10f,
            BoneImportanceClassification.SecondaryLocal => 0.88f,
            BoneImportanceClassification.DeemphasizedLocal => 0.38f,
            _ => 0f,
        };

        return 1f + ((target - 1f) * ClassificationInfluence);
    }

    private static string GetClassificationLabel(BoneImportanceClassification classification)
        => classification switch
        {
            BoneImportanceClassification.CoreStructural => "core structural",
            BoneImportanceClassification.SecondaryLocal => "secondary/local",
            BoneImportanceClassification.DeemphasizedLocal => "de-emphasized local/detail",
            _ => "ignored",
        };

    private static bool IsFingerBone(string boneName)
        => boneName.StartsWith("j_oya_", StringComparison.Ordinal)
           || boneName.StartsWith("j_hito_", StringComparison.Ordinal)
           || boneName.StartsWith("j_naka_", StringComparison.Ordinal)
           || boneName.StartsWith("j_kusu_", StringComparison.Ordinal)
           || boneName.StartsWith("j_ko_", StringComparison.Ordinal);

    private static bool IsToeBone(string boneName)
        => boneName.StartsWith("j_asi_e_", StringComparison.Ordinal)
           || boneName.StartsWith("iv_asi_", StringComparison.Ordinal);

    private static float NormalizeMetric(float value, float max)
        => max <= Epsilon
            ? 0f
            : Math.Clamp(value / max, 0f, 1f);

    private static void AddAmount(Dictionary<string, float> scores, string boneName, float amount)
    {
        if (amount <= 0f)
            return;

        scores[boneName] = scores.TryGetValue(boneName, out var existing)
            ? existing + amount
            : amount;
    }

    private static void AddMetric(
        Dictionary<string, BoneMetricAccumulator> metrics,
        string boneName,
        float influence = 0f,
        float vertexCoverage = 0f,
        float meshParticipation = 0f,
        float submeshParticipation = 0f,
        float triangleCoverage = 0f)
    {
        if (!IsRelevantBodyBone(boneName))
            return;

        if (!metrics.TryGetValue(boneName, out var metric))
        {
            metric = new BoneMetricAccumulator();
            metrics[boneName] = metric;
        }

        metric.Influence += Math.Max(0f, influence);
        metric.VertexCoverage += Math.Max(0f, vertexCoverage);
        metric.MeshParticipation += Math.Max(0f, meshParticipation);
        metric.SubmeshParticipation += Math.Max(0f, submeshParticipation);
        metric.TriangleCoverage += Math.Max(0f, triangleCoverage);
    }

    private List<ResolvedModelReference> ResolveModelReferences(
        Actor actor,
        IReadOnlyList<ModelPathCandidate> candidates,
        List<string> resolutionAttempts)
    {
        var references = new List<ResolvedModelReference>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        TryAppendTrackedResourceReferences(actor, candidates, resolutionAttempts, references, seen);
        TryAppendPenumbraResolvedReferences(actor, candidates, resolutionAttempts, references, seen);
        TryAppendVanillaReferences(candidates, resolutionAttempts, references, seen);

        return references;
    }

    private void TryAppendTrackedResourceReferences(
        Actor actor,
        IReadOnlyList<ModelPathCandidate> candidates,
        List<string> resolutionAttempts,
        ICollection<ResolvedModelReference> references,
        ISet<string> seen)
    {
        if (!_penumbraIpc.TryGetGameObjectResourcePaths(actor.Index.Index, out var resourcePaths) ||
            resourcePaths.Count == 0)
        {
            resolutionAttempts.Add("Active actor resource-path lookup was unavailable, so no live Penumbra resource path could be used.");
            return;
        }

        foreach (var candidate in candidates)
        {
            if (!TryMatchTrackedResource(resourcePaths, candidate, out var resolvedPath, out var matchedGamePath))
                continue;

            AddResolvedReference(
                references,
                seen,
                new ResolvedModelReference(
                    CacheKey: NormalizeCacheKey(resolvedPath),
                    ResolvedPath: resolvedPath,
                    RequestedGamePath: matchedGamePath,
                    ModelIdentity: candidate.ModelIdentity,
                    ResolutionSource: AdvancedBodyScalingBoneImportanceResolutionSource.PenumbraResolvedModel,
                    ResolutionDetail: $"Resolved via active actor resource path from {candidate.Description}.",
                    Part: candidate.Part,
                    PartLabel: candidate.PartLabel,
                    AggregateWeight: candidate.AggregateWeight));
        }
    }

    private void TryAppendPenumbraResolvedReferences(
        Actor actor,
        IReadOnlyList<ModelPathCandidate> candidates,
        List<string> resolutionAttempts,
        ICollection<ResolvedModelReference> references,
        ISet<string> seen)
    {
        var objectIndex = (int)actor.Index.Index;
        foreach (var candidate in candidates)
        {
            if (_penumbraIpc.TryResolveGameObjectPath(candidate.GamePath, objectIndex, out var resolvedPath))
            {
                AddResolvedReference(
                    references,
                    seen,
                new ResolvedModelReference(
                    CacheKey: NormalizeCacheKey(resolvedPath),
                    ResolvedPath: resolvedPath,
                    RequestedGamePath: candidate.GamePath,
                    ModelIdentity: candidate.ModelIdentity,
                    ResolutionSource: AdvancedBodyScalingBoneImportanceResolutionSource.PenumbraResolvedModel,
                    ResolutionDetail: $"Resolved via Penumbra collection path from {candidate.Description}.",
                    Part: candidate.Part,
                    PartLabel: candidate.PartLabel,
                    AggregateWeight: candidate.AggregateWeight));
            }
        }

        if (references.Count == 0)
            resolutionAttempts.Add("Penumbra game-object path resolution did not return a usable body model for the current actor.");
    }

    private void TryAppendVanillaReferences(
        IReadOnlyList<ModelPathCandidate> candidates,
        List<string> resolutionAttempts,
        ICollection<ResolvedModelReference> references,
        ISet<string> seen)
    {
        foreach (var candidate in candidates)
        {
            if (!_dataManager.FileExists(candidate.GamePath))
            {
                resolutionAttempts.Add($"Guessed vanilla model path '{candidate.GamePath}' was not found for {candidate.Description}.");
                continue;
            }

            AddResolvedReference(
                references,
                seen,
                new ResolvedModelReference(
                    CacheKey: NormalizeCacheKey(candidate.GamePath),
                    ResolvedPath: candidate.GamePath,
                    RequestedGamePath: candidate.GamePath,
                    ModelIdentity: candidate.ModelIdentity,
                    ResolutionSource: AdvancedBodyScalingBoneImportanceResolutionSource.VanillaModelPath,
                    ResolutionDetail: $"Resolved via guessed vanilla model path from {candidate.Description}.",
                    Part: candidate.Part,
                    PartLabel: candidate.PartLabel,
                    AggregateWeight: candidate.AggregateWeight));
        }
    }

    private static void AddResolvedReference(
        ICollection<ResolvedModelReference> references,
        ISet<string> seen,
        ResolvedModelReference reference)
    {
        if (string.IsNullOrWhiteSpace(reference.CacheKey) || !seen.Add(reference.CacheKey))
            return;

        references.Add(reference);
    }

    private static bool TryMatchTrackedResource(
        IReadOnlyDictionary<string, HashSet<string>> resourcePaths,
        ModelPathCandidate candidate,
        out string resolvedPath,
        out string matchedGamePath)
    {
        var normalizedCandidate = NormalizeGamePath(candidate.GamePath);
        var trackedPairs = EnumerateTrackedResourcePairs(resourcePaths).ToArray();
        foreach (var (actualPath, gamePath) in trackedPairs)
        {
            if (NormalizeGamePath(gamePath) == normalizedCandidate)
            {
                resolvedPath = actualPath;
                matchedGamePath = gamePath;
                return true;
            }
        }

        foreach (var (actualPath, gamePath) in trackedPairs)
        {
            if (MatchesCandidate(candidate, gamePath))
            {
                resolvedPath = actualPath;
                matchedGamePath = gamePath;
                return true;
            }
        }

        resolvedPath = string.Empty;
        matchedGamePath = string.Empty;
        return false;
    }

    private static IEnumerable<(string ActualPath, string GamePath)> EnumerateTrackedResourcePairs(
        IReadOnlyDictionary<string, HashSet<string>> resourcePaths)
    {
        foreach (var actualPath in resourcePaths.Keys
                     .Where(static path => !string.IsNullOrWhiteSpace(path))
                     .OrderBy(static path => NormalizeCacheKey(path), StringComparer.OrdinalIgnoreCase))
        {
            if (!resourcePaths.TryGetValue(actualPath, out var gamePaths) || gamePaths == null)
                continue;

            foreach (var gamePath in gamePaths
                         .Where(static path => !string.IsNullOrWhiteSpace(path))
                         .OrderBy(static path => NormalizeGamePath(path), StringComparer.OrdinalIgnoreCase))
            {
                yield return (actualPath, gamePath);
            }
        }
    }

    private static bool MatchesCandidate(ModelPathCandidate candidate, string gamePath)
    {
        var normalized = NormalizeGamePath(gamePath);
        return candidate.Kind switch
        {
            ModelPathCandidateKind.ActiveEquipment
                => normalized.Contains($"/chara/equipment/e{candidate.SlotId:D4}/model/", StringComparison.Ordinal)
                    && normalized.EndsWith($"_{candidate.ExpectedSuffix}.mdl", StringComparison.Ordinal),
            ModelPathCandidateKind.CustomizationBody
                => normalized.Contains($"/obj/body/b{candidate.SlotId:D4}/model/", StringComparison.Ordinal)
                    && normalized.EndsWith($"_{candidate.ExpectedSuffix}.mdl", StringComparison.Ordinal),
            _ => false,
        };
    }

    private bool TryReadModelBytes(string resolvedPath, out byte[] data, out string failureReason)
    {
        if (string.IsNullOrWhiteSpace(resolvedPath))
        {
            data = Array.Empty<byte>();
            failureReason = "The resolved model path was empty.";
            return false;
        }

        if (Path.IsPathRooted(resolvedPath))
        {
            if (!File.Exists(resolvedPath))
            {
                data = Array.Empty<byte>();
                failureReason = $"Resolved filesystem path '{resolvedPath}' was not found.";
                return false;
            }

            data = File.ReadAllBytes(resolvedPath);
            if (data.Length == 0)
            {
                failureReason = $"Resolved filesystem path '{resolvedPath}' returned no readable file data.";
                return false;
            }

            failureReason = string.Empty;
            return true;
        }

        if (!_dataManager.FileExists(resolvedPath))
        {
            data = Array.Empty<byte>();
            failureReason = $"Resolved game path '{resolvedPath}' was not found in the game files.";
            return false;
        }

        var file = _dataManager.GetFile(resolvedPath);
        if (file?.Data == null || file.Data.Length == 0)
        {
            data = Array.Empty<byte>();
            failureReason = $"Resolved game path '{resolvedPath}' returned no readable file data.";
            return false;
        }

        data = file.Data;
        failureReason = string.Empty;
        return true;
    }

    private static bool TryBuildModelPartRequests(
        Actor actor,
        out IReadOnlyList<ModelPartRequest> requests,
        out string modelIdentity,
        out string failureReason)
    {
        var model = actor.Model;
        var customize = model.GetCustomize();
        var genderRace = ResolveGenderRace(customize);
        if (!genderRace.IsValid())
        {
            requests = Array.Empty<ModelPartRequest>();
            modelIdentity = string.Empty;
            failureReason = "The actor customize data did not resolve to a supported gender/race body model.";
            return false;
        }

        var bodyArmor = model.GetArmorChanged(HumanSlot.Body);
        var legsArmor = model.GetArmorChanged(HumanSlot.Legs);
        var handsArmor = model.GetArmorChanged(HumanSlot.Hands);
        var feetArmor = model.GetArmorChanged(HumanSlot.Feet);
        var bodyTypeId = (ushort)Math.Max(1, (int)customize.BodyType.Value);
        requests =
        [
            new ModelPartRequest(
                BodyModelPart.TopBody,
                "top/body",
                1.00f,
                BuildTopBodyCandidates(genderRace, bodyArmor, bodyTypeId)),
            new ModelPartRequest(
                BodyModelPart.BottomLegs,
                "bottom/legs",
                0.95f,
                BuildPreferredSlotCandidates(genderRace, legsArmor, EquipSlot.Legs, BodyModelPart.BottomLegs, "bottom/legs", "the active base legs slot model (e0000)", "the active legs equipment model", 0.95f)),
            new ModelPartRequest(
                BodyModelPart.HandsGloves,
                "hands/gloves",
                0.45f,
                BuildPreferredSlotCandidates(genderRace, handsArmor, EquipSlot.Hands, BodyModelPart.HandsGloves, "hands/gloves", "the active base hands slot model (e0000)", "the active hands equipment model", 0.45f)),
            new ModelPartRequest(
                BodyModelPart.FeetShoes,
                "feet/shoes",
                0.45f,
                BuildPreferredSlotCandidates(genderRace, feetArmor, EquipSlot.Feet, BodyModelPart.FeetShoes, "feet/shoes", "the active base feet slot model (e0000)", "the active feet equipment model", 0.45f)),
        ];

        modelIdentity = $"{genderRace.ToRaceCode()} whole-body model context";
        failureReason = string.Empty;
        return true;
    }

    private static IReadOnlyList<ModelPathCandidate> BuildTopBodyCandidates(GenderRace genderRace, CharacterArmor bodyArmor, ushort bodyTypeId)
    {
        var candidates = new List<ModelPathCandidate>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        candidates.AddRange(BuildSlotBaseCandidates(
            genderRace,
            EquipSlot.Body,
            BodyModelPart.TopBody,
            "top/body",
            "the active base body slot model (e0000)",
            1.00f,
            seen));

        candidates.AddRange(BuildEquipmentCandidates(
            genderRace,
            bodyArmor,
            EquipSlot.Body,
            BodyModelPart.TopBody,
            "top/body",
            "the active body equipment model",
            1.00f,
            seen));

        foreach (var race in genderRace.Dependencies().Distinct())
        {
            var customizationPath = GamePaths.Mdl.Customization(race, BodySlot.Body, new PrimaryId(bodyTypeId), CustomizationType.Body);
            AppendCandidate(
                candidates,
                seen,
                new ModelPathCandidate(
                    customizationPath,
                    $"customization body b{bodyTypeId:D4} ({race.ToRaceCode()})",
                    race == genderRace
                        ? "the guessed vanilla customization body model"
                        : $"the guessed vanilla customization body model using fallback race {race.ToRaceCode()}",
                    ModelPathCandidateKind.CustomizationBody,
                    bodyTypeId,
                    race,
                    BodyModelPart.TopBody,
                    "top/body",
                    CustomizationType.Body.ToSuffix(),
                    1.00f));
        }

        return candidates;
    }

    private static IReadOnlyList<ModelPathCandidate> BuildPreferredSlotCandidates(
        GenderRace genderRace,
        CharacterArmor armor,
        EquipSlot slot,
        BodyModelPart part,
        string partLabel,
        string slotBaseDescription,
        string equipmentDescription,
        float aggregateWeight)
    {
        var candidates = new List<ModelPathCandidate>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        candidates.AddRange(BuildSlotBaseCandidates(
            genderRace,
            slot,
            part,
            partLabel,
            slotBaseDescription,
            aggregateWeight,
            seen));

        candidates.AddRange(BuildEquipmentCandidates(
            genderRace,
            armor,
            slot,
            part,
            partLabel,
            equipmentDescription,
            aggregateWeight,
            seen));

        return candidates;
    }

    private static IReadOnlyList<ModelPathCandidate> BuildSlotBaseCandidates(
        GenderRace genderRace,
        EquipSlot slot,
        BodyModelPart part,
        string partLabel,
        string baseDescription,
        float aggregateWeight,
        HashSet<string>? seen = null)
    {
        var candidates = new List<ModelPathCandidate>();
        var localSeen = seen ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var slotBaseArmor = new CharacterArmor((PrimaryId)0, Variant.None, StainIds.None);

        foreach (var race in genderRace.Dependencies().Distinct())
        {
            var equipmentPath = GamePaths.Mdl.Equipment(slotBaseArmor.Set, race, slot);
            AppendCandidate(
                candidates,
                localSeen,
                new ModelPathCandidate(
                    equipmentPath,
                    $"{partLabel} slot-base e0000 ({race.ToRaceCode()})",
                    race == genderRace
                        ? baseDescription
                        : $"{baseDescription} using fallback race {race.ToRaceCode()}",
                    ModelPathCandidateKind.ActiveEquipment,
                    slotBaseArmor.Set.Id,
                    race,
                    part,
                    partLabel,
                    slot.ToSuffix(),
                    aggregateWeight));
        }

        return candidates;
    }

    private static IReadOnlyList<ModelPathCandidate> BuildEquipmentCandidates(
        GenderRace genderRace,
        CharacterArmor armor,
        EquipSlot slot,
        BodyModelPart part,
        string partLabel,
        string baseDescription,
        float aggregateWeight)
        => BuildEquipmentCandidates(genderRace, armor, slot, part, partLabel, baseDescription, aggregateWeight, null);

    private static IReadOnlyList<ModelPathCandidate> BuildEquipmentCandidates(
        GenderRace genderRace,
        CharacterArmor armor,
        EquipSlot slot,
        BodyModelPart part,
        string partLabel,
        string baseDescription,
        float aggregateWeight,
        HashSet<string>? seen)
    {
        var candidates = new List<ModelPathCandidate>();
        var localSeen = seen ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var race in genderRace.Dependencies().Distinct())
        {
            var equipmentPath = GamePaths.Mdl.Equipment(armor.Set, race, slot);
            AppendCandidate(
                candidates,
                localSeen,
                new ModelPathCandidate(
                    equipmentPath,
                    $"{partLabel} equipment e{armor.Set.Id:D4} ({race.ToRaceCode()})",
                    race == genderRace
                        ? baseDescription
                        : $"{baseDescription} using fallback race {race.ToRaceCode()}",
                    ModelPathCandidateKind.ActiveEquipment,
                    armor.Set.Id,
                    race,
                    part,
                    partLabel,
                    slot.ToSuffix(),
                    aggregateWeight));
        }

        return candidates;
    }

    private static void AppendCandidate(
        ICollection<ModelPathCandidate> candidates,
        ISet<string> seen,
        ModelPathCandidate candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate.GamePath) || !seen.Add(candidate.GamePath))
            return;

        candidates.Add(candidate);
    }

    private static GenderRace ResolveGenderRace(CustomizeArray customize)
        => customize.Gender switch
        {
            Gender.Male => customize.Race switch
            {
                Race.Hyur => customize.Clan == SubRace.Highlander ? GenderRace.HighlanderMale : GenderRace.MidlanderMale,
                Race.Elezen => GenderRace.ElezenMale,
                Race.Lalafell => GenderRace.LalafellMale,
                Race.Miqote => GenderRace.MiqoteMale,
                Race.Roegadyn => GenderRace.RoegadynMale,
                Race.AuRa => GenderRace.AuRaMale,
                Race.Hrothgar => GenderRace.HrothgarMale,
                Race.Viera => GenderRace.VieraMale,
                _ => GenderRace.Unknown,
            },
            Gender.Female => customize.Race switch
            {
                Race.Hyur => customize.Clan == SubRace.Highlander ? GenderRace.HighlanderFemale : GenderRace.MidlanderFemale,
                Race.Elezen => GenderRace.ElezenFemale,
                Race.Lalafell => GenderRace.LalafellFemale,
                Race.Miqote => GenderRace.MiqoteFemale,
                Race.Roegadyn => GenderRace.RoegadynFemale,
                Race.AuRa => GenderRace.AuRaFemale,
                Race.Hrothgar => GenderRace.HrothgarFemale,
                Race.Viera => GenderRace.VieraFemale,
                _ => GenderRace.Unknown,
            },
            _ => GenderRace.Unknown,
        };

    private static IEnumerable<int> EnumeratePrimaryMeshIndices(MdlFile mdl)
    {
        if (mdl.LodCount > 0 && mdl.Lods.Length > 0)
        {
            var lod = mdl.Lods[0];
            for (var meshIndex = lod.MeshIndex; meshIndex < lod.MeshIndex + lod.MeshCount; meshIndex++)
                yield return meshIndex;
            yield break;
        }

        for (var i = 0; i < mdl.Meshes.Length; i++)
            yield return i;
    }

    private static bool TryGetMesh(MdlFile mdl, int meshIndex, out Penumbra.GameData.Files.ModelStructs.MeshStruct mesh)
    {
        if (meshIndex >= 0 && meshIndex < mdl.Meshes.Length)
        {
            mesh = mdl.Meshes[meshIndex];
            return true;
        }

        mesh = default;
        return false;
    }

    private static bool TryGetSubmesh(MdlFile mdl, int subMeshIndex, out Lumina.Data.Parsing.MdlStructs.SubmeshStruct submesh)
    {
        if (subMeshIndex >= 0 && subMeshIndex < mdl.SubMeshes.Length)
        {
            submesh = mdl.SubMeshes[subMeshIndex];
            return true;
        }

        submesh = default;
        return false;
    }

    private static bool TryGetBoneTable(MdlFile mdl, ushort tableIndex, out Penumbra.GameData.Files.ModelStructs.BoneTableStruct boneTable)
    {
        if (tableIndex < mdl.BoneTables.Length)
        {
            boneTable = mdl.BoneTables[tableIndex];
            return true;
        }

        boneTable = default;
        return false;
    }

    private static bool TryGetLodForMesh(MdlFile mdl, int meshIndex, out Lumina.Data.Parsing.MdlStructs.LodStruct lod)
    {
        for (var i = 0; i < Math.Min(mdl.LodCount, (byte)mdl.Lods.Length); i++)
        {
            var candidate = mdl.Lods[i];
            if (meshIndex >= candidate.MeshIndex && meshIndex < candidate.MeshIndex + candidate.MeshCount)
            {
                lod = candidate;
                return true;
            }
        }

        lod = default;
        return false;
    }

    private static bool TryResolveVertexDeclaration(MdlFile mdl, int meshIndex, out Lumina.Data.Parsing.MdlStructs.VertexDeclarationStruct declaration)
    {
        if (mdl.VertexDeclarations.Length == 1)
        {
            declaration = mdl.VertexDeclarations[0];
            return true;
        }

        if (meshIndex >= 0 && meshIndex < mdl.VertexDeclarations.Length)
        {
            declaration = mdl.VertexDeclarations[meshIndex];
            return true;
        }

        declaration = default;
        return false;
    }

    private static bool TryResolveBlendElements(Lumina.Data.Parsing.MdlStructs.VertexDeclarationStruct declaration, out BlendElement blendIndices, out BlendElement blendWeights)
    {
        blendIndices = default;
        blendWeights = default;
        foreach (var element in declaration.VertexElements)
        {
            if (element.Usage == (byte)MdlFile.VertexUsage.BlendIndices)
                blendIndices = new BlendElement(element.Stream, element.Offset, (MdlFile.VertexType)element.Type);
            else if (element.Usage == (byte)MdlFile.VertexUsage.BlendWeights)
                blendWeights = new BlendElement(element.Stream, element.Offset, (MdlFile.VertexType)element.Type);
        }

        return blendIndices != default && blendWeights != default;
    }

    private static IEnumerable<int> EnumerateActualBoneIndices(MdlFile mdl, Penumbra.GameData.Files.ModelStructs.BoneTableStruct boneTable)
    {
        var count = Math.Min((int)boneTable.BoneCount, boneTable.BoneIndex.Length);
        for (var i = 0; i < count; i++)
        {
            var actualIndex = boneTable.BoneIndex[i];
            if (actualIndex < mdl.Bones.Length && IsRelevantBodyBone(mdl.Bones[actualIndex]))
                yield return actualIndex;
        }
    }

    private static IEnumerable<int> EnumerateSubmeshBoneIndices(MdlFile mdl, Penumbra.GameData.Files.ModelStructs.BoneTableStruct boneTable, Lumina.Data.Parsing.MdlStructs.SubmeshStruct submesh)
    {
        var end = submesh.BoneStartIndex + submesh.BoneCount;
        for (var i = submesh.BoneStartIndex; i < end; i++)
        {
            if (i < 0 || i >= mdl.SubMeshBoneMap.Length)
                continue;

            var localIndex = mdl.SubMeshBoneMap[i];
            if (localIndex >= boneTable.BoneCount || localIndex >= boneTable.BoneIndex.Length)
                continue;

            var actualIndex = boneTable.BoneIndex[localIndex];
            if (actualIndex < mdl.Bones.Length && IsRelevantBodyBone(mdl.Bones[actualIndex]))
                yield return actualIndex;
        }
    }

    private static bool TryGetVertexStreamBaseOffset(MdlFile mdl, uint lodVertexOffset, Penumbra.GameData.Files.ModelStructs.MeshStruct mesh, byte streamIndex, out int offset)
    {
        offset = 0;
        if (streamIndex >= mesh.VertexStreamCount)
            return false;

        var resolved = (long)lodVertexOffset + mesh.VertexBufferOffset(streamIndex);
        if (resolved < 0 || resolved >= mdl.RemainingData.Length)
            return false;

        offset = (int)resolved;
        return true;
    }

    private static bool TrySliceVertexData(byte[] data, int offset, int size, out ReadOnlySpan<byte> slice)
    {
        if (size <= 0 || offset < 0 || offset + size > data.Length)
        {
            slice = default;
            return false;
        }

        slice = new ReadOnlySpan<byte>(data, offset, size);
        return true;
    }

    private static bool TryReadBlendIndices(ReadOnlySpan<byte> data, MdlFile.VertexType type, Span<int> output, out int count)
    {
        count = GetComponentCount(type);
        if (count == 0)
            return false;

        output.Clear();
        if (type is MdlFile.VertexType.UByte4 or MdlFile.VertexType.NByte4)
        {
            for (var i = 0; i < count; i++)
                output[i] = data[i];
            return true;
        }

        if (type is MdlFile.VertexType.Single1 or MdlFile.VertexType.Single2 or MdlFile.VertexType.Single3 or MdlFile.VertexType.Single4)
        {
            for (var i = 0; i < count; i++)
                output[i] = (int)MathF.Round(BitConverter.ToSingle(data.Slice(i * 4, 4)));
            return true;
        }

        for (var i = 0; i < count; i++)
            output[i] = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(i * 2, 2));
        return true;
    }

    private static bool TryReadBlendWeights(ReadOnlySpan<byte> data, MdlFile.VertexType type, Span<float> output, out int count)
    {
        count = GetComponentCount(type);
        if (count == 0)
            return false;

        output.Clear();
        switch (type)
        {
            case MdlFile.VertexType.UByte4:
            case MdlFile.VertexType.NByte4:
                for (var i = 0; i < count; i++)
                    output[i] = data[i] / 255f;
                return true;
            case MdlFile.VertexType.UShort2:
            case MdlFile.VertexType.UShort4:
                for (var i = 0; i < count; i++)
                    output[i] = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(i * 2, 2)) / 65535f;
                return true;
            case MdlFile.VertexType.Short2:
            case MdlFile.VertexType.Short4:
            case MdlFile.VertexType.NShort2:
            case MdlFile.VertexType.NShort4:
                for (var i = 0; i < count; i++)
                    output[i] = Math.Clamp(BinaryPrimitives.ReadInt16LittleEndian(data.Slice(i * 2, 2)) / 32767f, 0f, 1f);
                return true;
            case MdlFile.VertexType.Half2:
            case MdlFile.VertexType.Half4:
                for (var i = 0; i < count; i++)
                    output[i] = Math.Clamp((float)BitConverter.UInt16BitsToHalf(BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(i * 2, 2))), 0f, 1f);
                return true;
            case MdlFile.VertexType.Single1:
            case MdlFile.VertexType.Single2:
            case MdlFile.VertexType.Single3:
            case MdlFile.VertexType.Single4:
                for (var i = 0; i < count; i++)
                    output[i] = Math.Clamp(BitConverter.ToSingle(data.Slice(i * 4, 4)), 0f, 1f);
                return true;
            default:
                return false;
        }
    }

    private static void NormalizeWeights(Span<float> weights)
    {
        var total = 0f;
        for (var i = 0; i < weights.Length; i++)
        {
            weights[i] = Math.Max(0f, weights[i]);
            total += weights[i];
        }

        if (total <= Epsilon)
            return;

        for (var i = 0; i < weights.Length; i++)
            weights[i] /= total;
    }

    private static int GetComponentCount(MdlFile.VertexType type)
        => type switch
        {
            MdlFile.VertexType.Single1 => 1,
            MdlFile.VertexType.Single2 or MdlFile.VertexType.Short2 or MdlFile.VertexType.NShort2 or MdlFile.VertexType.Half2 or MdlFile.VertexType.UShort2 => 2,
            MdlFile.VertexType.Single3 => 3,
            MdlFile.VertexType.Single4 or MdlFile.VertexType.UByte4 or MdlFile.VertexType.NByte4 or MdlFile.VertexType.Short4 or MdlFile.VertexType.NShort4 or MdlFile.VertexType.Half4 or MdlFile.VertexType.UShort4 => 4,
            _ => 0,
        };

    private static int GetTypeSize(MdlFile.VertexType type)
        => type switch
        {
            MdlFile.VertexType.Single1 => 4,
            MdlFile.VertexType.Single2 => 8,
            MdlFile.VertexType.Single3 => 12,
            MdlFile.VertexType.Single4 => 16,
            MdlFile.VertexType.UByte4 or MdlFile.VertexType.NByte4 => 4,
            MdlFile.VertexType.Short2 or MdlFile.VertexType.NShort2 or MdlFile.VertexType.Half2 or MdlFile.VertexType.UShort2 => 4,
            MdlFile.VertexType.Short4 or MdlFile.VertexType.NShort4 or MdlFile.VertexType.Half4 or MdlFile.VertexType.UShort4 => 8,
            _ => 0,
        };

    private static string NormalizeCacheKey(string path)
        => string.IsNullOrWhiteSpace(path)
            ? string.Empty
            : Path.IsPathRooted(path)
                ? Path.GetFullPath(path)
                : NormalizeGamePath(path);

    private static string NormalizeGamePath(string path)
        => string.IsNullOrWhiteSpace(path)
            ? string.Empty
            : path.Replace('\\', '/').Trim().ToLowerInvariant();

    private static bool IsRelevantBodyBone(string boneName)
    {
        var family = BoneData.GetBoneFamily(boneName);
        return family is BoneData.BoneFamily.Spine
            or BoneData.BoneFamily.Chest
            or BoneData.BoneFamily.Groin
            or BoneData.BoneFamily.Arms
            or BoneData.BoneFamily.Hands
            or BoneData.BoneFamily.Legs
            or BoneData.BoneFamily.Feet
            or BoneData.BoneFamily.Tail;
    }

}
