// Copyright (c) Customize+.
// Licensed under the MIT license.

#if DEBUG
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using CustomizePlus.Core.Data;

namespace CustomizePlus.Armatures.Data;

/// <summary>
/// A bounded Debug-only fixture that drives the normal RBF corrective evaluator without
/// editing a saved template. It intentionally uses only unbound, trusted default bones.
/// </summary>
internal sealed class DebugPoseCorrectiveValidationSession
{
    private const int TargetCycleCount = 25;
    private const int FramesPerDrivenPhase = 8;
    private const int FramesPerNeutralReturnPhase = 16;
    private const float ActiveScaleDeltaThreshold = 0.0005f;
    // Float quaternion normalization can produce about 0.04 degrees of angular rounding
    // for an unchanged Havok pose; this stays well below a perceptible corrective delta.
    private const float NeutralReturnRotationToleranceDegrees = 0.05f;
    // The fixture must use a real automatic receiver, never an authored primary bone.
    private static readonly FixtureCandidate[] Candidates =
    [
        new(AdvancedBodyScalingCorrectiveRegion.HipUpperThigh, "n_hara", 0.80f, [0.72f, 0.42f, 0.28f, 0.56f, 0.62f]),
        new(AdvancedBodyScalingCorrectiveRegion.ShoulderUpperArm, "j_ude_a_l", 1.08f, [0.88f, 0.18f, 0.12f, 0.14f, 0.58f]),
        new(AdvancedBodyScalingCorrectiveRegion.ShoulderUpperArm, "j_ude_a_r", 1.08f, [0.88f, 0.18f, 0.12f, 0.14f, 0.58f]),
        new(AdvancedBodyScalingCorrectiveRegion.ElbowForearm, "j_ude_a_l", 1.08f, [0.74f, 0.66f, 0.70f, 0.28f, 0.22f]),
        new(AdvancedBodyScalingCorrectiveRegion.ElbowForearm, "j_ude_a_r", 1.08f, [0.74f, 0.66f, 0.70f, 0.28f, 0.22f]),
        new(AdvancedBodyScalingCorrectiveRegion.WaistHips, "n_hara", 1.08f, [0.62f, 0.52f, 0.70f, 0.58f]),
    ];

    private readonly Dictionary<ValidationPhase, Dictionary<string, DebugPoseCorrectiveNativeSample>> _samples = [];
    private readonly Dictionary<ValidationPhase, TimingWindow> _evaluationTimings = [];
    private readonly string[] _diagnosticBones;
    private readonly BoneTransform _syntheticInputTransform;
    private readonly BoneTransform _nativeBaselineTransform = new();
    private readonly AdvancedBodyScalingCorrectiveRegion _region;
    private readonly float[] _neutralDrivers;
    private readonly float[] _activeDrivers;
    private readonly float[] _intermediateDrivers;
    private readonly long _startedAtMs = Environment.TickCount64;
    private ValidationPhase _phase = ValidationPhase.Baseline;
    private int _framesInPhase;
    private int _completedCycles;
    private int _nativeApplicationCount;
    private float _maximumActiveScaleDelta;
    private float _maximumIntermediateScaleDelta;
    private float _maximumReturnTransitionScaleDelta;
    private float _maximumPostCycleTranslationDelta;
    private float _maximumPostCycleRotationDegrees;
    private float _maximumPostCycleScaleDelta;
    private float _currentFrameTranslationDelta;
    private float _currentFrameRotationDegrees;
    private float _currentFrameScaleDelta;
    private string _completionDetail = "Diagnostic fixture is queued for its first normal RBF evaluation.";

    private DebugPoseCorrectiveValidationSession(FixtureCandidate candidate)
    {
        _diagnosticBones = [candidate.BoneName];
        _region = candidate.Region;
        _syntheticInputTransform = new BoneTransform { Scaling = new Vector3(candidate.InputScale) };
        _activeDrivers = candidate.ActiveDrivers;
        _neutralDrivers = new float[candidate.ActiveDrivers.Length];
        _intermediateDrivers = candidate.ActiveDrivers.Select(static value => value * 0.5f).ToArray();
    }

    public bool IsComplete { get; private set; }

    public bool TryGetSyntheticTransform(string boneName, out BoneTransform transform)
    {
        if (!IsComplete && _diagnosticBones.Contains(boneName, StringComparer.Ordinal))
        {
            transform = _syntheticInputTransform;
            return true;
        }

        transform = null!;
        return false;
    }

    public bool TryGetDriverOverride(AdvancedBodyScalingCorrectiveRegion region, int expectedCount, out IReadOnlyList<float> drivers)
    {
        if (!IsComplete && region == _region && expectedCount == _activeDrivers.Length)
        {
            drivers = DriversForPhase(_phase);
            return true;
        }

        drivers = Array.Empty<float>();
        return false;
    }

    public IReadOnlyList<string> GetTargetBoneNames()
        => IsComplete ? Array.Empty<string>() : _diagnosticBones;

    public bool IsDrivenRegion(AdvancedBodyScalingCorrectiveRegion region)
        => !IsComplete && region == _region;

    public void FilterScaleMultipliers(Dictionary<string, Vector3> scaleMultipliers)
    {
        if (IsComplete)
            return;

        foreach (var boneName in scaleMultipliers.Keys.Where(name => !_diagnosticBones.Contains(name, StringComparer.Ordinal)).ToArray())
            scaleMultipliers.Remove(boneName);
    }

    public bool TryGetNativeBaselineTransform(string boneName, out BoneTransform transform)
    {
        if (!IsComplete && _diagnosticBones.Contains(boneName, StringComparer.Ordinal))
        {
            transform = _nativeBaselineTransform;
            return true;
        }

        transform = null!;
        return false;
    }

    public void RecordNativeApplication(
        string boneName,
        Vector3 beforeTranslation,
        Quaternion beforeRotation,
        Vector3 beforeScale,
        Vector3 afterTranslation,
        Quaternion afterRotation,
        Vector3 afterScale,
        Vector3 correctiveScale)
    {
        if (IsComplete || !_diagnosticBones.Contains(boneName, StringComparer.Ordinal))
            return;

        _nativeApplicationCount++;
        var delta = MaxAxisDelta(beforeScale, afterScale);
        var translationDelta = Vector3.Distance(beforeTranslation, afterTranslation);
        var rotationDegrees = QuaternionAngleDegrees(beforeRotation, afterRotation);
        var sample = new DebugPoseCorrectiveNativeSample(
            boneName,
            Vector3Snapshot.From(beforeTranslation),
            QuaternionSnapshot.From(beforeRotation),
            Vector3Snapshot.From(beforeScale),
            Vector3Snapshot.From(afterTranslation),
            QuaternionSnapshot.From(afterRotation),
            Vector3Snapshot.From(afterScale),
            Vector3Snapshot.From(correctiveScale),
            translationDelta,
            rotationDegrees,
            delta);
        var phaseSamples = GetPhaseSamples(_phase);
        if (!phaseSamples.TryGetValue(boneName, out var existing)
            || _phase == ValidationPhase.NeutralReturn
            || (_phase is ValidationPhase.Intermediate or ValidationPhase.Active && sample.ScaleDelta > existing.ScaleDelta))
        {
            phaseSamples[boneName] = sample;
        }

        switch (_phase)
        {
            case ValidationPhase.Active:
                _maximumActiveScaleDelta = Math.Max(_maximumActiveScaleDelta, delta);
                break;
            case ValidationPhase.Intermediate:
            case ValidationPhase.IntermediateReturn:
                _maximumIntermediateScaleDelta = Math.Max(_maximumIntermediateScaleDelta, delta);
                break;
            case ValidationPhase.NeutralReturn:
                _maximumReturnTransitionScaleDelta = Math.Max(_maximumReturnTransitionScaleDelta, delta);
                _currentFrameTranslationDelta = Math.Max(_currentFrameTranslationDelta, translationDelta);
                _currentFrameRotationDegrees = Math.Max(_currentFrameRotationDegrees, rotationDegrees);
                _currentFrameScaleDelta = Math.Max(_currentFrameScaleDelta, delta);
                break;
        }
    }

    public void RecordEvaluationMilliseconds(double elapsedMilliseconds)
    {
        if (IsComplete || !double.IsFinite(elapsedMilliseconds) || elapsedMilliseconds < 0d)
            return;

        if (!_evaluationTimings.TryGetValue(_phase, out var timing))
            _evaluationTimings[_phase] = timing = new TimingWindow();
        timing.Record(elapsedMilliseconds);
    }

    public void CompleteFrame()
    {
        if (IsComplete)
            return;

        _framesInPhase++;
        var phaseComplete = _framesInPhase >= FramesRequired(_phase);
        if (_phase == ValidationPhase.NeutralReturn && phaseComplete)
        {
            _maximumPostCycleTranslationDelta = Math.Max(_maximumPostCycleTranslationDelta, _currentFrameTranslationDelta);
            _maximumPostCycleRotationDegrees = Math.Max(_maximumPostCycleRotationDegrees, _currentFrameRotationDegrees);
            _maximumPostCycleScaleDelta = Math.Max(_maximumPostCycleScaleDelta, _currentFrameScaleDelta);
        }

        _currentFrameTranslationDelta = 0f;
        _currentFrameRotationDegrees = 0f;
        _currentFrameScaleDelta = 0f;
        if (!phaseComplete)
            return;

        _framesInPhase = 0;
        if (_phase != ValidationPhase.NeutralReturn)
        {
            _phase++;
            return;
        }

        _completedCycles++;
        if (_completedCycles >= TargetCycleCount)
        {
            IsComplete = true;
            _completionDetail = _maximumActiveScaleDelta > ActiveScaleDeltaThreshold && NeutralReturnWithinTolerance
                ? "Completed bounded A-B-C-B-A RBF fixture; the active phase produced a native scale correction and the neutral return removed it."
                : "Completed bounded A-B-C-B-A RBF fixture, but the active correction or final neutral return did not meet the diagnostic tolerance.";
            return;
        }

        _phase = ValidationPhase.Baseline;
    }

    public DebugPoseCorrectiveValidationSnapshot Snapshot()
    {
        var samples = _samples
            .OrderBy(static pair => pair.Key)
            .SelectMany(pair => pair.Value.Values.OrderBy(static sample => sample.BoneName, StringComparer.Ordinal)
                .Select(sample => sample with { Phase = pair.Key.ToString() }))
            .ToArray();
        var timings = _evaluationTimings
            .OrderBy(static pair => pair.Key)
            .Select(static pair => pair.Value.Snapshot(pair.Key.ToString()))
            .ToArray();
        return new DebugPoseCorrectiveValidationSnapshot(
            IsComplete ? "completed" : "running",
            _completionDetail,
            _phase.ToString(),
            _completedCycles,
            TargetCycleCount,
            _framesInPhase,
            FramesRequired(_phase),
            _nativeApplicationCount,
            _maximumActiveScaleDelta,
            _maximumIntermediateScaleDelta,
            _maximumReturnTransitionScaleDelta,
            _maximumPostCycleTranslationDelta,
            _maximumPostCycleRotationDegrees,
            _maximumPostCycleScaleDelta,
            _maximumActiveScaleDelta > ActiveScaleDeltaThreshold,
            NeutralReturnWithinTolerance,
            Environment.TickCount64 - _startedAtMs,
            _diagnosticBones,
            samples,
            timings);
    }

    public static bool TryCreate(Armature armature, AdvancedBodyScalingSettings settings, out DebugPoseCorrectiveValidationSession? session, out string reason)
    {
        session = null;
        if (!armature.IsBuilt || !armature.IsSkeletonBindingCurrent)
        {
            reason = "The player armature does not have a current validated skeleton binding.";
            return false;
        }

        if (!settings.Enabled || settings.Mode == AdvancedBodyScalingMode.Manual || !settings.PoseCorrectives.Enabled || settings.PoseCorrectives.Strength <= 0f)
        {
            reason = "Advanced Body Scaling or RBF pose-space correctives are disabled for the player profile.";
            return false;
        }

        var neckSettings = settings.PoseCorrectives.GetRegionSettings(AdvancedBodyScalingCorrectiveRegion.NeckShoulder);
        if (!neckSettings.Enabled || neckSettings.Strength <= 0f)
        {
            reason = "The Neck / Shoulder RBF region is disabled for the player profile.";
            return false;
        }

        var rejectedCandidates = new List<string>();
        foreach (var candidate in Candidates)
        {
            var boneName = candidate.BoneName;
            if (!armature.TryGetPublishedBone(boneName, out var bone))
            {
                rejectedCandidates.Add($"{boneName}: unavailable");
                continue;
            }

            var metadata = BoneData.GetMetadata(boneName);
            if (!metadata.HasTrust(BoneAutomationTrust.AdvancedCorrectiveSafe)
                || metadata.Role is BoneFunctionalRole.ClothingRig or BoneFunctionalRole.PropRig or BoneFunctionalRole.GearAttachment or BoneFunctionalRole.ArticulatedAppendage or BoneFunctionalRole.Unknown)
            {
                rejectedCandidates.Add($"{boneName}: not corrective-safe");
                continue;
            }

            // AppliedTransform is the current resolved runtime output and may exist for an
            // otherwise automatic bone. Only authored/customized state is user authority.
            if (armature.IsExplicitTemplateTransform(boneName) || bone.CustomizedTransform != null)
            {
                rejectedCandidates.Add($"{boneName}: authored");
                continue;
            }

            session = new DebugPoseCorrectiveValidationSession(candidate);
            reason = $"Queued a bounded 25-cycle {candidate.Region} RBF validation fixture using unbound trusted receiver {boneName}.";
            return true;
        }

        reason = $"No unbound trusted automatic RBF receiver is available for the Debug fixture ({string.Join(", ", rejectedCandidates)}); no temporary input was installed.";
        return false;
    }

    private Dictionary<string, DebugPoseCorrectiveNativeSample> GetPhaseSamples(ValidationPhase phase)
    {
        if (!_samples.TryGetValue(phase, out var samples))
        {
            samples = new Dictionary<string, DebugPoseCorrectiveNativeSample>(StringComparer.Ordinal);
            _samples[phase] = samples;
        }

        return samples;
    }

    private IReadOnlyList<float> DriversForPhase(ValidationPhase phase)
        => phase switch
        {
            ValidationPhase.Baseline or ValidationPhase.NeutralReturn => _neutralDrivers,
            ValidationPhase.Intermediate or ValidationPhase.IntermediateReturn => _intermediateDrivers,
            ValidationPhase.Active => _activeDrivers,
            _ => _neutralDrivers,
        };

    private static int FramesRequired(ValidationPhase phase)
        => phase == ValidationPhase.NeutralReturn ? FramesPerNeutralReturnPhase : FramesPerDrivenPhase;

    private static float MaxAxisDelta(Vector3 before, Vector3 after)
        => Math.Max(Math.Abs(after.X - before.X), Math.Max(Math.Abs(after.Y - before.Y), Math.Abs(after.Z - before.Z)));

    private bool NeutralReturnWithinTolerance
        => _maximumPostCycleTranslationDelta <= 0.0001f
        && _maximumPostCycleRotationDegrees <= NeutralReturnRotationToleranceDegrees
        && _maximumPostCycleScaleDelta <= ActiveScaleDeltaThreshold;

    private static float QuaternionAngleDegrees(Quaternion before, Quaternion after)
    {
        if (before.LengthSquared() <= 0.000001f || after.LengthSquared() <= 0.000001f)
            return float.PositiveInfinity;

        var normalizedBefore = Quaternion.Normalize(before);
        var normalizedAfter = Quaternion.Normalize(after);
        var dot = Math.Clamp(Math.Abs(Quaternion.Dot(normalizedBefore, normalizedAfter)), 0f, 1f);
        return 2f * MathF.Acos(dot) * 180f / MathF.PI;
    }

    private enum ValidationPhase
    {
        Baseline,
        Intermediate,
        Active,
        IntermediateReturn,
        NeutralReturn,
    }

    private sealed record FixtureCandidate(
        AdvancedBodyScalingCorrectiveRegion Region,
        string BoneName,
        float InputScale,
        float[] ActiveDrivers);

    private sealed class TimingWindow
    {
        private long _count;
        private double _total;
        private double _max;

        public void Record(double milliseconds)
        {
            _count++;
            _total += milliseconds;
            _max = Math.Max(_max, milliseconds);
        }

        public DebugPoseCorrectiveTimingSnapshot Snapshot(string phase)
            => new(phase, _count, _count == 0 ? 0d : _total / _count, _max);
    }
}

internal sealed record DebugPoseCorrectiveValidationSnapshot(
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
    IReadOnlyList<DebugPoseCorrectiveNativeSample> Samples,
    IReadOnlyList<DebugPoseCorrectiveTimingSnapshot> EvaluationTimings)
{
    public static DebugPoseCorrectiveValidationSnapshot Idle { get; } = new(
        "idle", "No Debug RBF validation has been requested.", "none", 0, 25, 0, 0, 0, 0f, 0f, 0f, 0f, 0f, 0f, false, true, 0L, Array.Empty<string>(), Array.Empty<DebugPoseCorrectiveNativeSample>(), Array.Empty<DebugPoseCorrectiveTimingSnapshot>());
}

internal sealed record DebugPoseCorrectiveNativeSample(
    string BoneName,
    Vector3Snapshot BeforeTranslation,
    QuaternionSnapshot BeforeRotation,
    Vector3Snapshot BeforeScale,
    Vector3Snapshot AfterTranslation,
    QuaternionSnapshot AfterRotation,
    Vector3Snapshot AfterScale,
    Vector3Snapshot CorrectiveScale,
    float TranslationDelta,
    float RotationDegreesDelta,
    float ScaleDelta,
    string Phase = "");

internal sealed record DebugPoseCorrectiveTimingSnapshot(
    string Phase,
    long Samples,
    double AverageMilliseconds,
    double MaximumMilliseconds);

internal sealed record Vector3Snapshot(float X, float Y, float Z)
{
    public static Vector3Snapshot From(Vector3 value) => new(value.X, value.Y, value.Z);
}

internal sealed record QuaternionSnapshot(float X, float Y, float Z, float W)
{
    public static QuaternionSnapshot From(Quaternion value) => new(value.X, value.Y, value.Z, value.W);
}
#endif
