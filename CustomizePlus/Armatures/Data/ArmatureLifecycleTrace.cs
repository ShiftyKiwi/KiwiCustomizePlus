// Copyright (c) Customize+.
// Licensed under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace CustomizePlus.Armatures.Data;

/// <summary>
/// Outcome of a primary ModelBone native write. This is diagnostic-only and never changes write behavior.
/// </summary>
internal enum NativeTransformWriteOutcome
{
    Accepted,
    SkippedMissingBone,
    SkippedStaleBinding,
    SkippedPoseNotInSync,
    SkippedUnsafeTransform,
    SkippedUnsupportedFrame,
}

/// <summary>
/// A compact snapshot of self-only native transform writes since the current debug capture began.
/// </summary>
internal readonly record struct ArmatureNativeWriteDiagnostics(
    long Attempted,
    long Accepted,
    long SkippedMissingBone,
    long SkippedStaleBinding,
    long SkippedPoseNotInSync,
    long SkippedUnsafeTransform,
    int ActiveTargetBoneCount)
{
    public static ArmatureNativeWriteDiagnostics Empty => new(0, 0, 0, 0, 0, 0, 0);

    public string ToCompactString()
        => $"attempted {Attempted}, accepted {Accepted}, stale {SkippedStaleBinding}, missing {SkippedMissingBone}, not-in-sync {SkippedPoseNotInSync}, unsafe {SkippedUnsafeTransform}, active targets {ActiveTargetBoneCount}";
}

/// <summary>
/// Last observed root-scale handoff. This is diagnostic-only and never changes application behavior.
/// </summary>
internal readonly record struct RootScaleApplicationDiagnostics(
    long Attempts,
    long Applied,
    bool RootScaleModified,
    bool ActorEligible,
    Vector3 ObservedBefore,
    Vector3 Requested,
    Vector3 ObservedAfter)
{
    public static RootScaleApplicationDiagnostics Empty => new(0, 0, false, false, Vector3.One, Vector3.One, Vector3.One);
}

/// <summary>
/// One state-changing observation of the self armature lifecycle. Pointer values are debug evidence only.
/// </summary>
internal sealed record ArmatureLifecycleTraceEntry(
    long Sequence,
    long TimestampMs,
    long Frame,
    string Reason,
    string ActorIdentity,
    string ModelIdentity,
    string NativeIdentity,
    string StructuralFingerprint,
    long SkeletonRevision,
    long NativeBindingGeneration,
    bool IsBuilt,
    bool BindingCurrent,
    string PendingPublication,
    int PendingObservations,
    string ProfileIdentity,
    string TemplateSummary,
    int ResolvedTransformCount,
    int BoundModelBoneCount,
    int ActiveModelBoneCount,
    long TemplateBindingRevision,
    bool PendingProfileRebind,
    string BoneImportanceSignature,
    string GlamourerTransition,
    ArmatureNativeWriteDiagnostics NativeWrites,
    string StateKey)
{
    public string ToDisplayLine()
        => $"#{Sequence} t={TimestampMs} f={Frame} {Reason}: {ModelIdentity}; rev={SkeletonRevision}/native={NativeBindingGeneration}; built={IsBuilt}; binding={BindingCurrent}; resolved={ResolvedTransformCount}; bound={BoundModelBoneCount}; active={ActiveModelBoneCount}; writes [{NativeWrites.ToCompactString()}]";

    public string ToSupportLine()
        => $"#{Sequence} t={TimestampMs} f={Frame} reason={Reason}; actor={ActorIdentity}; model={ModelIdentity}; native={NativeIdentity}; fingerprint={StructuralFingerprint}; revision={SkeletonRevision}; native-generation={NativeBindingGeneration}; built={IsBuilt}; binding-current={BindingCurrent}; pending-publication={PendingPublication} ({PendingObservations}); profile={ProfileIdentity}; templates={TemplateSummary}; resolved={ResolvedTransformCount}; bound={BoundModelBoneCount}; active={ActiveModelBoneCount}; template-binding-revision={TemplateBindingRevision}; pending-profile-rebind={PendingProfileRebind}; BIW={BoneImportanceSignature}; Glamourer={GlamourerTransition}; writes={NativeWrites.ToCompactString()}";
}

/// <summary>
/// Debug-only-in-practice bounded trace. Release builds keep this as a no-op surface so UI/support callers stay simple.
/// </summary>
internal sealed class ArmatureLifecycleTrace
{
    private const int Capacity = 96;
    private readonly Queue<ArmatureLifecycleTraceEntry> _entries = new();
    private string _lastStateKey = string.Empty;
    private long _nextSequence;

    public IReadOnlyList<ArmatureLifecycleTraceEntry> Snapshot()
        => _entries.ToArray();

    public void Clear()
    {
        _entries.Clear();
        _lastStateKey = string.Empty;
    }

    public void Record(ArmatureLifecycleTraceEntry entry, bool force)
    {
#if !DEBUG
        return;
#else
        if (!force && string.Equals(entry.StateKey, _lastStateKey, StringComparison.Ordinal))
            return;

        _lastStateKey = entry.StateKey;
        entry = entry with { Sequence = ++_nextSequence };
        if (_entries.Count == Capacity)
            _entries.Dequeue();

        _entries.Enqueue(entry);
#endif
    }

    public string BuildClipboardText()
        => string.Join(Environment.NewLine, Snapshot().Select(static entry => entry.ToSupportLine()));
}
