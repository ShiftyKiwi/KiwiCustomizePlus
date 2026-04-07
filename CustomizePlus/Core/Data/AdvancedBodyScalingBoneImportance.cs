// Copyright (c) Customize+.
// Licensed under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;

namespace CustomizePlus.Core.Data;

internal enum AdvancedBodyScalingBoneImportanceSource
{
    HeuristicFallback = 0,
    CoarseParticipation = 1,
    ModelWeights = 2,
    MixedAggregate = 3,
}

internal enum AdvancedBodyScalingBoneImportanceResolutionSource
{
    HeuristicFallback = 0,
    PenumbraResolvedModel = 1,
    VanillaModelPath = 2,
    PenumbraResolvedAggregate = 3,
    VanillaAggregate = 4,
}

internal enum AdvancedBodyScalingBoneImportanceRuntimeMode
{
    Full = 0,
    Reduced = 1,
    Cached = 2,
    Skipped = 3,
}

internal enum AdvancedBodyScalingBoneImportanceActorTier
{
    Self = 0,
    ProfiledActor = 1,
    TargetOrFocus = 2,
    NearbyNonProfiled = 3,
    Other = 4,
}

internal sealed class AdvancedBodyScalingBoneImportanceProbeResult
{
    public static AdvancedBodyScalingBoneImportanceProbeResult CreateFallback(
        string reason,
        string modelSignature = "",
        bool signatureChanged = false)
        => new()
        {
            HasResolvedModelSet = false,
            ModelSignature = modelSignature,
            ModelSignatureChanged = signatureChanged,
            Summary = string.IsNullOrWhiteSpace(reason)
                ? "No usable model-signature probe data was available."
                : reason,
        };

    public bool HasResolvedModelSet { get; init; }
    public string ModelSignature { get; init; } = string.Empty;
    public bool ModelSignatureChanged { get; init; }
    public string Summary { get; init; } = string.Empty;
}

internal sealed class AdvancedBodyScalingBoneImportanceRuntimeState
{
    public string LastProbedModelSignature { get; set; } = string.Empty;
    public string LastConfirmedModelSignature { get; set; } = string.Empty;
    public string PendingModelSignature { get; set; } = string.Empty;
    public long PendingModelSignatureAtMs { get; set; }
    public int PendingModelSignatureProbeCount { get; set; }
    public long PendingModelSignatureSettleHoldUntilMs { get; set; }
    public long LastProbeAtMs { get; set; }
    public long LastResolveAtMs { get; set; }
    public int StableProbeCount { get; set; }
    public AdvancedBodyScalingBoneImportanceRuntimeMode LastMode { get; set; } = AdvancedBodyScalingBoneImportanceRuntimeMode.Skipped;
    public bool HasVisibleRuntimeState { get; set; }
    public string VisibleStateKey { get; set; } = string.Empty;
    public string VisibleRuntimeModeLabel { get; set; } = "skipped";
    public string VisibleActorTierLabel { get; set; } = "other actor";
    public bool VisibleFullQualityEligible { get; set; }
    public bool VisibleCrowdSafeDowngraded { get; set; }
    public bool VisibleStableThrottled { get; set; }
    public string VisibleRuntimeSummary { get; set; } = string.Empty;
    public string PendingVisibleStateKey { get; set; } = string.Empty;
    public long PendingVisibleStateAtMs { get; set; }
}

internal sealed class AdvancedBodyScalingBoneImportanceResult
{
    private static readonly IReadOnlyDictionary<string, float> EmptyScores = new Dictionary<string, float>(StringComparer.Ordinal);
    private static readonly IReadOnlyList<string> EmptySamples = Array.Empty<string>();
    private static readonly IReadOnlyList<string> EmptyDetails = Array.Empty<string>();

    public static AdvancedBodyScalingBoneImportanceResult CreateFallback(
        string reason,
        bool enabled,
        bool preferSkinWeights,
        float heuristicBlend,
        string modelSignature = "",
        string modelIdentity = "",
        string requestedGamePath = "",
        string resolvedModelPath = "",
        string resolutionDetail = "",
        string resolutionTrace = "",
        bool modelSignatureChanged = false,
        string refreshStatus = "",
        IReadOnlyList<string>? partDetails = null,
        IReadOnlyList<string>? missingPartDetails = null)
        => new()
        {
            Enabled = enabled,
            PreferTrueSkinWeightImportance = preferSkinWeights,
            HeuristicBlend = heuristicBlend,
            Source = AdvancedBodyScalingBoneImportanceSource.HeuristicFallback,
            ResolutionSource = AdvancedBodyScalingBoneImportanceResolutionSource.HeuristicFallback,
            ModelSignature = modelSignature,
            ModelSignatureChanged = modelSignatureChanged,
            ModelIdentity = modelIdentity,
            RequestedGamePath = requestedGamePath,
            ModelPath = resolvedModelPath,
            ResolutionDetail = resolutionDetail,
            ResolutionTrace = resolutionTrace,
            RefreshStatus = refreshStatus,
            AreaAwareRefinementActive = false,
            ClassificationRefinementActive = false,
            ConfidenceWeightedAggregationActive = false,
            RefinementSummary = string.Empty,
            ConfidenceSummary = string.Empty,
            RuntimeMode = AdvancedBodyScalingBoneImportanceRuntimeMode.Skipped,
            ActorTier = AdvancedBodyScalingBoneImportanceActorTier.Other,
            FullQualityEligible = false,
            CrowdSafeDowngraded = false,
            StableThrottled = false,
            RuntimeSummary = string.Empty,
            Summary = string.IsNullOrWhiteSpace(reason)
                ? "Using heuristic fallback."
                : $"Using heuristic fallback. {reason}",
            FallbackReason = reason ?? string.Empty,
            Scores = EmptyScores,
            SampleValues = EmptySamples,
            PartDetails = partDetails ?? EmptyDetails,
            MissingPartDetails = missingPartDetails ?? EmptyDetails,
        };

    public bool Enabled { get; init; }
    public bool PreferTrueSkinWeightImportance { get; init; }
    public float HeuristicBlend { get; init; }
    public AdvancedBodyScalingBoneImportanceSource Source { get; init; }
    public AdvancedBodyScalingBoneImportanceResolutionSource ResolutionSource { get; init; }
    public string ModelSignature { get; init; } = string.Empty;
    public bool ModelSignatureChanged { get; init; }
    public string ModelIdentity { get; init; } = string.Empty;
    public string RequestedGamePath { get; init; } = string.Empty;
    public string ModelPath { get; init; } = string.Empty;
    public string ResolutionDetail { get; init; } = string.Empty;
    public string ResolutionTrace { get; init; } = string.Empty;
    public string RefreshStatus { get; init; } = string.Empty;
    public bool CacheHit { get; init; }
    public bool Stage1Available { get; init; }
    public bool Stage2Available { get; init; }
    public bool MultiModelAggregate { get; init; }
    public int ContributingPartCount { get; init; }
    public bool AreaAwareRefinementActive { get; init; }
    public bool ClassificationRefinementActive { get; init; }
    public bool ConfidenceWeightedAggregationActive { get; init; }
    public string RefinementSummary { get; init; } = string.Empty;
    public string ConfidenceSummary { get; init; } = string.Empty;
    public AdvancedBodyScalingBoneImportanceRuntimeMode RuntimeMode { get; set; } = AdvancedBodyScalingBoneImportanceRuntimeMode.Full;
    public AdvancedBodyScalingBoneImportanceActorTier ActorTier { get; set; } = AdvancedBodyScalingBoneImportanceActorTier.Other;
    public bool FullQualityEligible { get; set; }
    public bool CrowdSafeDowngraded { get; set; }
    public bool StableThrottled { get; set; }
    public string RuntimeSummary { get; set; } = string.Empty;
    public bool UseVisibleRuntimeState { get; set; }
    public string DisplayRuntimeModeLabel { get; set; } = string.Empty;
    public string DisplayActorTierLabel { get; set; } = string.Empty;
    public bool DisplayFullQualityEligible { get; set; }
    public bool DisplayCrowdSafeDowngraded { get; set; }
    public bool DisplayStableThrottled { get; set; }
    public string DisplayRuntimeSummary { get; set; } = string.Empty;
    public string Summary { get; init; } = string.Empty;
    public string FallbackReason { get; init; } = string.Empty;
    public IReadOnlyDictionary<string, float> Scores { get; init; } = EmptyScores;
    public IReadOnlyList<string> SampleValues { get; init; } = EmptySamples;
    public IReadOnlyList<string> PartDetails { get; init; } = EmptyDetails;
    public IReadOnlyList<string> MissingPartDetails { get; init; } = EmptyDetails;

    public bool ModelDerivedActive
        => Source != AdvancedBodyScalingBoneImportanceSource.HeuristicFallback && Scores.Count > 0;

    public string SourceLabel
        => Source switch
        {
            AdvancedBodyScalingBoneImportanceSource.MixedAggregate => "mixed aggregate",
            AdvancedBodyScalingBoneImportanceSource.ModelWeights => "model weights",
            AdvancedBodyScalingBoneImportanceSource.CoarseParticipation => "coarse participation",
            _ => "heuristic fallback",
        };

    public string ResolutionLabel
        => ResolutionSource switch
        {
            AdvancedBodyScalingBoneImportanceResolutionSource.PenumbraResolvedAggregate => "Penumbra-resolved aggregate",
            AdvancedBodyScalingBoneImportanceResolutionSource.VanillaAggregate => "vanilla aggregate",
            AdvancedBodyScalingBoneImportanceResolutionSource.PenumbraResolvedModel => "Penumbra-resolved model",
            AdvancedBodyScalingBoneImportanceResolutionSource.VanillaModelPath => "vanilla model path",
            _ => "heuristic fallback",
        };

    public string LiveSourceLabel
        => Source switch
        {
            AdvancedBodyScalingBoneImportanceSource.MixedAggregate => $"mixed aggregate ({ResolutionLabel})",
            AdvancedBodyScalingBoneImportanceSource.ModelWeights => $"model weights ({ResolutionLabel})",
            AdvancedBodyScalingBoneImportanceSource.CoarseParticipation => $"coarse participation ({ResolutionLabel})",
            _ => "heuristic fallback",
        };

    public string StageLabel
        => Source switch
        {
            AdvancedBodyScalingBoneImportanceSource.MixedAggregate => "Mixed Stage 1 + Stage 2 aggregate",
            AdvancedBodyScalingBoneImportanceSource.ModelWeights => "Stage 2 skin-weight aggregation",
            AdvancedBodyScalingBoneImportanceSource.CoarseParticipation => "Stage 1 coarse participation",
            _ => "heuristic fallback",
        };

    public string AggregateModeLabel
        => MultiModelAggregate ? "multi-model aggregate" : "single-model";

    public string RuntimeModeLabel
    {
        get
        {
            if (RuntimeMode == AdvancedBodyScalingBoneImportanceRuntimeMode.Cached && StableThrottled)
                return "cached-frozen";

            if (RuntimeMode == AdvancedBodyScalingBoneImportanceRuntimeMode.Skipped)
            {
                if (Source == AdvancedBodyScalingBoneImportanceSource.HeuristicFallback && CrowdSafeDowngraded)
                    return "hard-skipped";

                if (Source == AdvancedBodyScalingBoneImportanceSource.HeuristicFallback)
                    return "heuristic fallback";

                return "skipped";
            }

            return RuntimeMode switch
            {
                AdvancedBodyScalingBoneImportanceRuntimeMode.Reduced => "reduced/coarse",
                AdvancedBodyScalingBoneImportanceRuntimeMode.Cached => "cached",
                _ => "full",
            };
        }
    }

    public string ActorTierLabel
        => ActorTier switch
        {
            AdvancedBodyScalingBoneImportanceActorTier.Self => "self",
            AdvancedBodyScalingBoneImportanceActorTier.ProfiledActor => "profiled actor",
            AdvancedBodyScalingBoneImportanceActorTier.TargetOrFocus => "target/focus",
            AdvancedBodyScalingBoneImportanceActorTier.NearbyNonProfiled => "nearby non-profiled",
            _ => "other actor",
        };

    public string VisibleRuntimeModeLabel
        => UseVisibleRuntimeState && !string.IsNullOrWhiteSpace(DisplayRuntimeModeLabel)
            ? DisplayRuntimeModeLabel
            : RuntimeModeLabel;

    public string VisibleActorTierLabel
        => UseVisibleRuntimeState && !string.IsNullOrWhiteSpace(DisplayActorTierLabel)
            ? DisplayActorTierLabel
            : ActorTierLabel;

    public bool VisibleFullQualityEligible
        => UseVisibleRuntimeState ? DisplayFullQualityEligible : FullQualityEligible;

    public bool VisibleCrowdSafeDowngraded
        => UseVisibleRuntimeState ? DisplayCrowdSafeDowngraded : CrowdSafeDowngraded;

    public bool VisibleStableThrottled
        => UseVisibleRuntimeState ? DisplayStableThrottled : StableThrottled;

    public string VisibleRuntimeSummary
        => UseVisibleRuntimeState && !string.IsNullOrWhiteSpace(DisplayRuntimeSummary)
            ? DisplayRuntimeSummary
            : RuntimeSummary;

    public static IReadOnlyList<string> BuildSampleValues(IReadOnlyDictionary<string, float> scores)
    {
        if (scores.Count == 0)
            return EmptySamples;

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

        var used = new HashSet<string>(StringComparer.Ordinal);
        var samples = new List<string>();

        foreach (var bone in notable)
        {
            if (!scores.TryGetValue(bone, out var value))
                continue;

            used.Add(bone);
            samples.Add($"{BoneData.GetBoneDisplayName(bone)} ({bone}) {value:0.00}");
            if (samples.Count >= 6)
                return samples;
        }

        foreach (var (bone, value) in scores
                     .OrderByDescending(kvp => kvp.Value)
                     .ThenBy(kvp => kvp.Key, StringComparer.Ordinal))
        {
            if (!used.Add(bone))
                continue;

            samples.Add($"{BoneData.GetBoneDisplayName(bone)} ({bone}) {value:0.00}");
            if (samples.Count >= 6)
                break;
        }

        return samples;
    }
}
