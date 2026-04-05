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
