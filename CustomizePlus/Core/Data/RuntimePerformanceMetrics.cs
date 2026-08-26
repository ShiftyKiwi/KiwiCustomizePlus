// Copyright (c) Customize+.
// Licensed under the MIT license.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace CustomizePlus.Core.Data;

/// <summary>
/// Small single-threaded rolling timing store for coarse runtime stages.
/// It is intentionally sampled only at work boundaries, never by a render/UI query.
/// </summary>
internal sealed class RuntimePerformanceMetrics
{
    private readonly Dictionary<string, MutableTiming> _timings = new(StringComparer.Ordinal);

    public long Start() => Stopwatch.GetTimestamp();

    public void Record(string stage, long startTimestamp)
    {
        var elapsedMs = (Stopwatch.GetTimestamp() - startTimestamp) * 1000d / Stopwatch.Frequency;
        RecordMilliseconds(stage, elapsedMs);
    }

    public void RecordMilliseconds(string stage, double elapsedMs)
    {
        if (!double.IsFinite(elapsedMs) || elapsedMs < 0d)
            return;

        if (!_timings.TryGetValue(stage, out var timing))
            _timings[stage] = timing = new MutableTiming();
        timing.Record(elapsedMs);
    }

    public IReadOnlyList<RuntimeTimingSummary> Snapshot()
        => _timings.OrderBy(static pair => pair.Key, StringComparer.Ordinal)
            .Select(static pair => pair.Value.Freeze(pair.Key))
            .ToArray();

    private sealed class MutableTiming
    {
        private long _count;
        private double _total;
        private double _latest;
        private double _max;

        public void Record(double milliseconds)
        {
            _count++;
            _total += milliseconds;
            _latest = milliseconds;
            _max = Math.Max(_max, milliseconds);
        }

        public RuntimeTimingSummary Freeze(string stage)
            => new(stage, _count, _latest, _count == 0 ? 0d : _total / _count, _max);
    }
}

internal sealed record RuntimeTimingSummary(
    string Stage,
    long Samples,
    double LatestMilliseconds,
    double AverageMilliseconds,
    double MaxMilliseconds);

/// <summary>
/// Bounded current-state diagnostics for optional runtime layers. This intentionally retains
/// only the most recent failure window; it is not a per-frame error history.
/// </summary>
internal sealed class OptionalLayerHealthState
{
    private string _lastFailureType = string.Empty;
    private long _lastFailureAtMs;
    private int _failureCountInWindow;
    private bool _recovered;

    public void RecordFailure(Exception exception)
    {
        var now = Environment.TickCount64;
        if (now - _lastFailureAtMs > 30_000)
            _failureCountInWindow = 0;
        _lastFailureType = exception.GetType().Name;
        _lastFailureAtMs = now;
        _failureCountInWindow++;
        _recovered = false;
    }

    public void RecordSuccess()
    {
        if (_lastFailureAtMs > 0)
            _recovered = true;
    }

    public OptionalLayerHealthSnapshot Freeze(string layer)
        => new(layer, _lastFailureType, _lastFailureAtMs, _failureCountInWindow, _recovered);
}

internal sealed record OptionalLayerHealthSnapshot(
    string Layer,
    string MostRecentFailureType,
    long LastFailureAtMs,
    int RepeatedFailureCount,
    bool Recovered)
{
    public bool HasFailure => LastFailureAtMs > 0;
}
