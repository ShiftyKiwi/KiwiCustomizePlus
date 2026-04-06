// Copyright (c) Customize+.
// Licensed under the MIT license.

using System;
using System.Collections.Generic;

namespace CustomizePlus.Core.Data;

internal sealed class AdvancedBodyScalingDebugReport
{
    public IReadOnlyList<IReadOnlyList<string>> ActiveCurveChains { get; set; } = Array.Empty<IReadOnlyList<string>>();
    public Dictionary<string, float> InitialScales { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, float> AfterPropagation { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, float> AfterSurfaceBalancing { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, float> AfterMassRedistribution { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, float> AfterGuardrails { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, float> AfterCurveSmoothing { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, float> AfterPoseValidation { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, float> FinalScales { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, float> PropagationDeltas { get; } = new(StringComparer.Ordinal);
    public List<GuardrailCorrection> GuardrailCorrections { get; } = new();
    public bool BoneImportanceEnabled { get; set; }
    public bool BoneImportancePreferTrueSkinWeights { get; set; }
    public float BoneImportanceHeuristicBlend { get; set; }
    public bool BoneImportanceCacheHit { get; set; }
    public bool BoneImportanceFallbackUsed { get; set; }
    public bool BoneImportanceInfluencedPropagation { get; set; }
    public bool BoneImportanceInfluencedSmoothing { get; set; }
    public bool BoneImportanceInfluencedGuardrails { get; set; }
    public bool BoneImportanceMultiModelAggregate { get; set; }
    public int BoneImportanceContributingPartCount { get; set; }
    public string BoneImportanceSource { get; set; } = "heuristic fallback";
    public string BoneImportanceStage { get; set; } = "heuristic fallback";
    public string BoneImportanceResolution { get; set; } = "heuristic fallback";
    public string BoneImportanceAggregateMode { get; set; } = "single-model";
    public string BoneImportanceRequestedModelPath { get; set; } = string.Empty;
    public string BoneImportanceModelIdentity { get; set; } = string.Empty;
    public string BoneImportanceModelSignature { get; set; } = string.Empty;
    public string BoneImportanceModelPath { get; set; } = string.Empty;
    public string BoneImportanceResolutionDetail { get; set; } = string.Empty;
    public string BoneImportanceResolutionTrace { get; set; } = string.Empty;
    public string BoneImportanceRefreshStatus { get; set; } = string.Empty;
    public bool BoneImportanceSignatureChanged { get; set; }
    public bool BoneImportanceAreaAwareRefinementActive { get; set; }
    public bool BoneImportanceClassificationRefinementActive { get; set; }
    public bool BoneImportanceConfidenceWeightedAggregationActive { get; set; }
    public string BoneImportanceRefinementSummary { get; set; } = string.Empty;
    public string BoneImportanceConfidenceSummary { get; set; } = string.Empty;
    public string BoneImportanceRuntimeMode { get; set; } = "skipped";
    public string BoneImportanceActorTier { get; set; } = "other actor";
    public bool BoneImportanceFullQualityEligible { get; set; }
    public bool BoneImportanceCrowdSafeDowngraded { get; set; }
    public bool BoneImportanceStableThrottled { get; set; }
    public string BoneImportanceRuntimeSummary { get; set; } = string.Empty;
    public string BoneImportanceSummary { get; set; } = string.Empty;
    public List<string> BoneImportanceSamples { get; } = new();
    public List<string> BoneImportancePartDetails { get; } = new();
    public List<string> BoneImportanceMissingPartDetails { get; } = new();
    public List<AdvancedBodyScalingCorrectiveDebugRegionState> EstimatedPoseCorrectives { get; } = new();
    public List<AdvancedBodyScalingFullIkRetargetingEstimate> EstimatedRetargeting { get; } = new();
    public List<AdvancedBodyScalingMotionWarpingEstimate> EstimatedMotionWarping { get; } = new();
    public List<AdvancedBodyScalingFullBodyIkEstimate> EstimatedFullBodyIk { get; } = new();

    public sealed class GuardrailCorrection
    {
        public string Description { get; init; } = string.Empty;
        public float BeforeRatio { get; init; }
        public float AfterRatio { get; init; }
        public IReadOnlyList<string> NumeratorBones { get; init; } = Array.Empty<string>();
        public IReadOnlyList<string> DenominatorBones { get; init; } = Array.Empty<string>();
    }

    public static void CopyScales(Dictionary<string, float> source, Dictionary<string, float> target)
    {
        target.Clear();
        foreach (var kvp in source)
            target[kvp.Key] = kvp.Value;
    }
}
