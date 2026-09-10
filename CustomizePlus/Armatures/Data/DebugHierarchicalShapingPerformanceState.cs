// Copyright (c) Customize+.
// Licensed under the MIT license.

#if DEBUG
using System;
using System.Collections.Generic;
using System.Linq;
using CustomizePlus.Core.Data;

namespace CustomizePlus.Armatures.Data;

/// <summary>
/// Debug-only, per-armature metrics for the rebuild-time Hierarchical Shaping pass.
/// It is intentionally populated only at binding rebuild boundaries, never per frame.
/// </summary>
internal sealed class DebugHierarchicalShapingPerformanceState
{
    private const int MaximumRetainedSolveSamples = 256;

    private readonly Queue<double> _solveMilliseconds = new();
    private readonly Queue<double> _combinedRebuildMilliseconds = new();

    public long SolveCount { get; private set; }
    public long CacheHitCount { get; private set; }
    public long CacheMissCount { get; private set; }
    public long NoAdmittedRegionCount { get; private set; }
    public bool LastRebuildWasCacheHit { get; private set; }
    public double LastSolveMilliseconds { get; private set; }
    public double TotalSolveMilliseconds { get; private set; }

    public void ResetMeasurements()
    {
        _solveMilliseconds.Clear();
        _combinedRebuildMilliseconds.Clear();
        SolveCount = 0;
        CacheHitCount = 0;
        CacheMissCount = 0;
        NoAdmittedRegionCount = 0;
        LastRebuildWasCacheHit = false;
        LastSolveMilliseconds = 0d;
        TotalSolveMilliseconds = 0d;
    }

    public void RecordCacheHit()
    {
        CacheHitCount++;
        LastRebuildWasCacheHit = true;
    }

    public void RecordSolve(double elapsedMilliseconds, HierarchicalShapingDiagnostics diagnostics)
    {
        CacheMissCount++;
        SolveCount++;
        LastRebuildWasCacheHit = false;

        if (diagnostics.Regions.All(static region => region.EligibleBoneCount == 0))
            NoAdmittedRegionCount++;

        if (!double.IsFinite(elapsedMilliseconds) || elapsedMilliseconds < 0d)
            return;

        LastSolveMilliseconds = elapsedMilliseconds;
        TotalSolveMilliseconds += elapsedMilliseconds;
        _solveMilliseconds.Enqueue(elapsedMilliseconds);
        while (_solveMilliseconds.Count > MaximumRetainedSolveSamples)
            _solveMilliseconds.Dequeue();
    }

    public void RecordCombinedRebuild(double elapsedMilliseconds)
    {
        if (!double.IsFinite(elapsedMilliseconds) || elapsedMilliseconds < 0d)
            return;

        _combinedRebuildMilliseconds.Enqueue(elapsedMilliseconds);
        while (_combinedRebuildMilliseconds.Count > MaximumRetainedSolveSamples)
            _combinedRebuildMilliseconds.Dequeue();
    }

    public DebugHierarchicalShapingPerformanceSnapshot Snapshot(
        bool effectiveEnabled,
        bool authoredRelaxationEnabled,
        HierarchicalShapingDiagnostics diagnostics,
        bool cacheEntryAvailable)
    {
        var samples = _solveMilliseconds.OrderBy(static value => value).ToArray();
        var combinedSamples = _combinedRebuildMilliseconds.OrderBy(static value => value).ToArray();
        return new DebugHierarchicalShapingPerformanceSnapshot(
            effectiveEnabled,
            authoredRelaxationEnabled,
            diagnostics.AppliedRegionCount,
            diagnostics.Regions.Count(static region => region.EligibleBoneCount > 0),
            diagnostics.CorrectedBoneCount,
            GetMaximumRelativeCorrectionPercent(diagnostics),
            cacheEntryAvailable,
            LastRebuildWasCacheHit,
            SolveCount,
            CacheHitCount,
            CacheMissCount,
            NoAdmittedRegionCount,
            LastSolveMilliseconds,
            GetPercentile(samples, 0.50d),
            GetPercentile(samples, 0.95d),
            samples.Length == 0 ? 0d : samples[^1],
            TotalSolveMilliseconds,
            samples,
            combinedSamples.Length,
            GetPercentile(combinedSamples, 0.50d),
            GetPercentile(combinedSamples, 0.95d),
            combinedSamples.Length == 0 ? 0d : combinedSamples[^1],
            combinedSamples.Sum(),
            combinedSamples);
    }

    private static float GetMaximumRelativeCorrectionPercent(HierarchicalShapingDiagnostics diagnostics)
        => diagnostics.LogScaleCorrections.Count == 0
            ? 0f
            : diagnostics.LogScaleCorrections.Values
                .Select(static correction => MathF.Abs(MathF.Exp(correction) - 1f) * 100f)
                .Max();

    private static double GetPercentile(IReadOnlyList<double> sorted, double percentile)
    {
        if (sorted.Count == 0)
            return 0d;

        var index = Math.Clamp((int)Math.Ceiling(sorted.Count * percentile) - 1, 0, sorted.Count - 1);
        return sorted[index];
    }
}

internal sealed record DebugHierarchicalShapingPerformanceSnapshot(
    bool EffectiveEnabled,
    bool AuthoredRelaxationEnabled,
    int AppliedRegionCount,
    int AdmittedRegionCount,
    int CorrectedBoneCount,
    float MaximumRelativeCorrectionPercent,
    bool CacheEntryAvailable,
    bool LastRebuildWasCacheHit,
    long SolveCount,
    long CacheHitCount,
    long CacheMissCount,
    long NoAdmittedRegionCount,
    double LastSolveMilliseconds,
    double MedianSolveMilliseconds,
    double P95SolveMilliseconds,
    double MaximumSolveMilliseconds,
    double TotalSolveMilliseconds,
    IReadOnlyList<double> SolveSamples,
    long CombinedRebuildCount,
    double MedianCombinedRebuildMilliseconds,
    double P95CombinedRebuildMilliseconds,
    double MaximumCombinedRebuildMilliseconds,
    double TotalCombinedRebuildMilliseconds,
    IReadOnlyList<double> CombinedRebuildSamples);
#endif
