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
