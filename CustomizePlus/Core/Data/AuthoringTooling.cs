// Copyright (c) Customize+.
// Licensed under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using CustomizePlus.Profiles.Data;
using CustomizePlus.Templates.Data;

namespace CustomizePlus.Core.Data;

/// <summary>
/// Pure, on-demand authoring helpers.  These types deliberately do not retain
/// armatures or perform native access; UI callers supply managed snapshots.
/// </summary>
internal static class AuthoringTooling
{
    internal static Dictionary<string, BoneTransform> CloneTransforms(IReadOnlyDictionary<string, BoneTransform> transforms)
        => transforms.ToDictionary(static pair => pair.Key, static pair => pair.Value.DeepCopy(), StringComparer.Ordinal);

    internal static bool TransformEquals(BoneTransform? left, BoneTransform? right)
    {
        if (ReferenceEquals(left, right))
            return true;
        if (left == null || right == null)
            return false;

        return left.Translation == right.Translation
            && left.Rotation == right.Rotation
            && left.Scaling == right.Scaling
            && left.ChildScaling == right.ChildScaling
            && left.PropagateTranslation == right.PropagateTranslation
            && left.PropagateRotation == right.PropagateRotation
            && left.PropagateScale == right.PropagateScale
            && left.ChildScalingIndependent == right.ChildScalingIndependent
            && left.PropagationFalloff == right.PropagationFalloff
            && left.LockState == right.LockState
            && left.PinX == right.PinX
            && left.PinY == right.PinY
            && left.PinZ == right.PinZ;
    }
}

/// <summary>
/// A narrow, managed authoring delta.  It intentionally knows nothing about saved
/// templates or live skeletons: callers apply it through TemplateEditorManager.
/// </summary>
internal sealed class TemplateAuthoringOperation
{
    public string Label { get; }
    public IReadOnlyDictionary<string, BoneTransform?> Before { get; }
    public IReadOnlyDictionary<string, BoneTransform> After { get; }

    private TemplateAuthoringOperation(
        string label,
        IReadOnlyDictionary<string, BoneTransform?> before,
        IReadOnlyDictionary<string, BoneTransform> after)
    {
        Label = label;
        Before = before;
        After = after;
    }

    public static TemplateAuthoringOperation? Create(
        string label,
        IReadOnlyDictionary<string, BoneTransform> current,
        IReadOnlyDictionary<string, BoneTransform> requested)
    {
        var before = new Dictionary<string, BoneTransform?>(StringComparer.Ordinal);
        var after = new Dictionary<string, BoneTransform>(StringComparer.Ordinal);
        foreach (var (boneName, requestedTransform) in requested)
        {
            current.TryGetValue(boneName, out var existing);
            var desired = requestedTransform.DeepCopy();
            if (existing == null && !desired.IsEdited(true))
                continue;
            if (AuthoringTooling.TransformEquals(existing, desired))
                continue;

            before[boneName] = existing?.DeepCopy();
            after[boneName] = desired;
        }

        return after.Count == 0 ? null : new TemplateAuthoringOperation(label, before, after);
    }

    public static TemplateAuthoringOperation? CreateScaleOperation(
        string label,
        IReadOnlyDictionary<string, BoneTransform> current,
        IReadOnlyDictionary<string, BoneTransform> requested)
    {
        var scaleOnly = new Dictionary<string, BoneTransform>(StringComparer.Ordinal);
        foreach (var (boneName, requestedTransform) in requested)
        {
            var desired = current.TryGetValue(boneName, out var existing)
                ? existing.DeepCopy()
                : new BoneTransform();
            desired.Scaling = desired.ApplyScalePins(requestedTransform.Scaling);
            scaleOnly[boneName] = desired;
        }

        return Create(label, current, scaleOnly);
    }

    public bool CanApply(IReadOnlyDictionary<string, BoneTransform> current)
        => Before.All(pair => MatchesBefore(current, pair.Key, pair.Value));

    /// <summary>
    /// A revert is only safe while every row still exactly matches the applied
    /// result.  Unrelated rows are deliberately ignored.
    /// </summary>
    public bool TryCreateRevert(IReadOnlyDictionary<string, BoneTransform> current, string label, out TemplateAuthoringOperation? revert)
    {
        revert = null;
        if (!After.All(pair => MatchesAppliedResult(current, pair.Key, pair.Value)))
            return false;

        var requested = Before.ToDictionary(
            static pair => pair.Key,
            static pair => pair.Value?.DeepCopy() ?? new BoneTransform(),
            StringComparer.Ordinal);
        revert = Create(label, current, requested);
        return revert != null;
    }

    private static bool MatchesBefore(
        IReadOnlyDictionary<string, BoneTransform> current,
        string boneName,
        BoneTransform? expected)
    {
        if (expected == null)
            return !current.ContainsKey(boneName);

        return current.TryGetValue(boneName, out var existing)
            && AuthoringTooling.TransformEquals(expected, existing);
    }

    private static bool MatchesAppliedResult(
        IReadOnlyDictionary<string, BoneTransform> current,
        string boneName,
        BoneTransform expected)
    {
        // TemplateManager removes unedited default transforms after a write.
        if (!expected.IsEdited(true))
            return !current.ContainsKey(boneName);

        return current.TryGetValue(boneName, out var existing)
            && AuthoringTooling.TransformEquals(expected, existing);
    }
}

/// <summary>Small pure helpers for choosing the authoritative authoring state.</summary>
internal static class TemplateAuthoringState
{
    public static IReadOnlyDictionary<string, BoneTransform> SelectBones(
        IReadOnlyDictionary<string, BoneTransform> savedBones,
        IReadOnlyDictionary<string, BoneTransform>? workingBones,
        bool editorActive)
        => editorActive && workingBones != null ? workingBones : savedBones;

    public static bool IsStale(long sourceRevision, long currentRevision, bool editorActive)
        => editorActive && sourceRevision != currentRevision;
}

internal enum TemplateDiffKind
{
    Shared,
    Changed,
    OnlyLeft,
    OnlyRight,
}

internal sealed record TemplateDiffRow(
    string BoneName,
    TemplateDiffKind Kind,
    BoneTransform? Left,
    BoneTransform? Right,
    Vector3 TranslationDelta,
    Vector3 RotationDelta,
    Vector3 ScalingDelta,
    bool LockChanged,
    bool PinsChanged,
    BoneData.BoneFamily Family,
    BoneOrigin Origin,
    BoneAutomationTrust Trust);

internal sealed record TemplateDiffReport(
    IReadOnlyList<TemplateDiffRow> Rows,
    int SharedCount,
    int ChangedCount,
    int OnlyLeftCount,
    int OnlyRightCount)
{
    public IReadOnlyDictionary<BoneData.BoneFamily, Vector3> RegionScaleDeltas
        => Rows.Where(static row => row.Kind == TemplateDiffKind.Changed)
            .GroupBy(static row => row.Family)
            .ToDictionary(
                static group => group.Key,
                static group => group.Aggregate(Vector3.Zero, static (sum, row) => sum + row.ScalingDelta) / Math.Max(1, group.Count()));
}

internal static class TemplateDiffService
{
    public static TemplateDiffReport Compare(Template left, Template right)
    {
        var rows = new List<TemplateDiffRow>();
        foreach (var bone in left.Bones.Keys.Union(right.Bones.Keys, StringComparer.Ordinal).OrderBy(static name => name, StringComparer.Ordinal))
        {
            left.Bones.TryGetValue(bone, out var a);
            right.Bones.TryGetValue(bone, out var b);
            var kind = a == null ? TemplateDiffKind.OnlyRight
                : b == null ? TemplateDiffKind.OnlyLeft
                : AuthoringTooling.TransformEquals(a, b) ? TemplateDiffKind.Shared
                : TemplateDiffKind.Changed;
            var metadata = BoneData.GetMetadata(bone);
            rows.Add(new TemplateDiffRow(
                bone,
                kind,
                a?.DeepCopy(),
                b?.DeepCopy(),
                a == null || b == null ? Vector3.Zero : b.Translation - a.Translation,
                a == null || b == null ? Vector3.Zero : b.Rotation - a.Rotation,
                a == null || b == null ? Vector3.Zero : b.Scaling - a.Scaling,
                a != null && b != null && a.LockState != b.LockState,
                a != null && b != null && (a.PinX != b.PinX || a.PinY != b.PinY || a.PinZ != b.PinZ),
                BoneData.GetBoneFamily(bone),
                metadata.Origin,
                metadata.Trust));
        }

        return new TemplateDiffReport(
            rows,
            rows.Count(static row => row.Kind == TemplateDiffKind.Shared),
            rows.Count(static row => row.Kind == TemplateDiffKind.Changed),
            rows.Count(static row => row.Kind == TemplateDiffKind.OnlyLeft),
            rows.Count(static row => row.Kind == TemplateDiffKind.OnlyRight));
    }

    public static Dictionary<string, BoneTransform> CopyFrom(
        IReadOnlyDictionary<string, BoneTransform> target,
        IEnumerable<TemplateDiffRow> rows,
        bool leftToRight,
        bool copyPosition,
        bool copyRotation,
        bool copyScale,
        bool copyLocksAndPins)
    {
        var result = AuthoringTooling.CloneTransforms(target);
        foreach (var row in rows)
        {
            var source = leftToRight ? row.Left : row.Right;
            if (source == null)
                continue;

            result.TryGetValue(row.BoneName, out var existing);
            var update = existing?.DeepCopy() ?? new BoneTransform();
            if (copyPosition)
                update.Translation = source.Translation;
            if (copyRotation)
                update.Rotation = source.Rotation;
            if (copyScale)
            {
                update.Scaling = source.Scaling;
                update.ChildScaling = source.ChildScaling;
                update.ChildScalingIndependent = source.ChildScalingIndependent;
                update.PropagateScale = source.PropagateScale;
                update.PropagationFalloff = source.PropagationFalloff;
            }
            if (copyLocksAndPins)
            {
                update.LockState = source.LockState;
                update.PinX = source.PinX;
                update.PinY = source.PinY;
                update.PinZ = source.PinZ;
            }
            result[row.BoneName] = update;
        }
        return result;
    }
}

internal sealed record ProfileDiffTemplateRow(
    Guid TemplateId,
    string TemplateName,
    bool ExistsLeft,
    bool ExistsRight,
    bool EnabledLeft,
    bool EnabledRight,
    float WeightLeft,
    float WeightRight,
    TemplateCompatibilityRequirement RequirementLeft,
    TemplateCompatibilityRequirement RequirementRight);

internal sealed record ProfileDiffReport(IReadOnlyList<ProfileDiffTemplateRow> Templates, bool PriorityChanged, bool AdvancedOverridesChanged);

internal static class ProfileDiffService
{
    public static ProfileDiffReport Compare(Profile left, Profile right)
    {
        var leftById = left.Templates.ToDictionary(static template => template.UniqueId);
        var rightById = right.Templates.ToDictionary(static template => template.UniqueId);
        var rows = leftById.Keys.Union(rightById.Keys)
            .OrderBy(static id => id)
            .Select(id =>
            {
                var hasLeft = leftById.TryGetValue(id, out var a);
                var hasRight = rightById.TryGetValue(id, out var b);
                return new ProfileDiffTemplateRow(
                    id,
                    a?.Name.Text ?? b?.Name.Text ?? id.ToString(),
                    hasLeft,
                    hasRight,
                    hasLeft && !left.DisabledTemplates.Contains(id),
                    hasRight && !right.DisabledTemplates.Contains(id),
                    hasLeft ? left.GetTemplateWeight(id) : 0f,
                    hasRight ? right.GetTemplateWeight(id) : 0f,
                    hasLeft ? left.GetTemplateCompatibilityRequirement(id) : TemplateCompatibilityRequirement.Always,
                    hasRight ? right.GetTemplateCompatibilityRequirement(id) : TemplateCompatibilityRequirement.Always);
            }).ToArray();
        return new ProfileDiffReport(rows, left.Priority != right.Priority,
            !Equals(left.AdvancedBodyScalingOverrides, right.AdvancedBodyScalingOverrides));
    }
}

internal sealed record CompatibilityPreviewRow(
    Guid TemplateId,
    string TemplateName,
    bool Enabled,
    bool Active,
    string Reason,
    int AuthoredEntries,
    int DirectlyPresent,
    int DormantDueToCapability,
    int KnownButAbsent,
    int ManualOnly,
    int ExcludedFromAutomation,
    int Unknown,
    int Unavailable);

internal sealed record CompatibilityPreviewReport(
    IReadOnlyList<CompatibilityPreviewRow> Rows,
    int TotalAuthoredEntries,
    int ActiveEntries,
    int DormantEntries,
    int DirectlyPresentEntries,
    int KnownButAbsentEntries,
    int ManualOnlyEntries,
    int ExcludedEntries,
    int UnknownEntries,
    int UnavailableEntries)
{
    public bool IsSafePartialCompatibility => DormantEntries > 0 && ActiveEntries > 0;
}

internal static class CompatibilityPreviewService
{
    public static CompatibilityPreviewReport Preview(Profile profile, SkeletonCapabilityManifest manifest)
    {
        // Use the production resolver; this is deliberately a dry-run over an immutable manifest.
        var resolution = ProfileTransformResolver.Resolve(profile, manifest);
        var rows = new List<CompatibilityPreviewRow>();
        foreach (var applicability in resolution.TemplateApplicability)
        {
            var template = profile.Templates.First(template => template.UniqueId == applicability.TemplateId);
            var supported = 0;
            var dormant = 0;
            var absent = 0;
            var manual = 0;
            var excluded = 0;
            var unknown = 0;
            var unavailable = 0;
            foreach (var bone in template.Bones.Keys)
            {
                var metadata = BoneData.GetMetadata(bone);
                if (!manifest.BindingCurrent)
                    unavailable++;
                else if (metadata.Origin == BoneOrigin.UnknownCustom || manifest.UnknownCustomBoneNames.Contains(bone, StringComparer.Ordinal))
                    unknown++;
                else if (metadata.Role is BoneFunctionalRole.ClothingRig or BoneFunctionalRole.PropRig or BoneFunctionalRole.GearAttachment or BoneFunctionalRole.ArticulatedAppendage)
                    excluded++;
                else if (metadata.Trust == BoneAutomationTrust.ManualOnly)
                    manual++;
                else if (applicability.Enabled && !applicability.Active)
                    dormant++;
                else if (!manifest.ContainsObservedBone(bone))
                    absent++;
                else
                    supported++;
            }
            rows.Add(new CompatibilityPreviewRow(applicability.TemplateId, applicability.TemplateName, applicability.Enabled,
                applicability.Active, applicability.Reason, template.Bones.Count, supported, dormant, absent, manual, excluded, unknown, unavailable));
        }

        return new CompatibilityPreviewReport(rows,
            rows.Sum(static row => row.AuthoredEntries),
            rows.Where(static row => row.Active).Sum(static row => row.AuthoredEntries),
            rows.Where(static row => !row.Active).Sum(static row => row.AuthoredEntries),
            rows.Sum(static row => row.DirectlyPresent), rows.Sum(static row => row.KnownButAbsent), rows.Sum(static row => row.ManualOnly),
            rows.Sum(static row => row.ExcludedFromAutomation), rows.Sum(static row => row.Unknown), rows.Sum(static row => row.Unavailable));
    }
}

internal enum ActorHealthState
{
    Healthy,
    TemporarilyWaiting,
    LimitedCompatibility,
    NeedsAttention,
}

internal sealed record ActorHealthInput(
    bool HasProfile,
    bool ProfileEnabled,
    bool BindingCurrent,
    bool AppearanceTransitionPending,
    bool NativeReacquisitionPending,
    int DormantTemplateCount,
    long StaleWrites,
    long UnsafeWrites,
    string BindingIssue);

internal sealed record ActorHealthReport(ActorHealthState State, string Summary, IReadOnlyList<string> Details)
{
    public static ActorHealthReport Evaluate(ActorHealthInput input)
    {
        if (!input.HasProfile || !input.ProfileEnabled)
            return new(ActorHealthState.NeedsAttention, "No active Customize+ profile is assigned.", ["Assign or enable a profile before expecting a template to apply."]);
        if (input.AppearanceTransitionPending || input.NativeReacquisitionPending)
            return new(ActorHealthState.TemporarilyWaiting, "Scaling is temporarily paused while this actor's appearance settles.", ["The profile remains active. No action is required."]);
        // Native-write counters are cumulative diagnostics. Only an invalid current binding is a
        // current health failure; historical blocked writes stay visible as informational detail.
        if (!input.BindingCurrent)
            return new(ActorHealthState.NeedsAttention, "The current skeleton binding is not safe to write.", [string.IsNullOrWhiteSpace(input.BindingIssue) ? "Waiting for a valid binding." : input.BindingIssue]);
        if (input.DormantTemplateCount > 0)
            return new(ActorHealthState.LimitedCompatibility, "Safe partial compatibility is active.", [$"{input.DormantTemplateCount} template assignment(s) are dormant until their required capability is available."]);
        var historicalSafetyEvents = new List<string>();
        if (input.StaleWrites > 0)
            historicalSafetyEvents.Add("A previous stale write was blocked safely.");
        if (input.UnsafeWrites > 0)
            historicalSafetyEvents.Add("A previous unsafe write was blocked safely.");
        return new(ActorHealthState.Healthy, "Profile, skeleton binding, and runtime safety are healthy.", historicalSafetyEvents);
    }
}

internal enum AuthoringRegionScope
{
    Primary,
    Support,
    Transition,
    TrustedSecondary,
}

internal sealed record AuthoringRegion(string Name, IReadOnlyList<string> Primary, IReadOnlyList<string> Support, IReadOnlyList<string> Transition, IReadOnlyList<string> Secondary)
{
    public IEnumerable<string> GetBones(AuthoringRegionScope scope) => scope switch
    {
        AuthoringRegionScope.Primary => Primary,
        AuthoringRegionScope.Support => Support,
        AuthoringRegionScope.Transition => Transition,
        _ => Secondary,
    };
}

internal static class RegionBatchEditService
{
    // Shared curated, anatomical scope. Generic operations intentionally exclude clothing, props, appendages and unknown bones.
    public static readonly IReadOnlyList<AuthoringRegion> Regions =
    [
        new("Chest", ["j_mune_l", "j_mune_r", "j_sebo_b"], ["j_sebo_c"], ["j_sako_l", "j_sako_r", "n_hkata_l", "n_hkata_r"], ["iv_kyokin_phys_l", "iv_kyokin_phys_r"]),
        new("Upper Arms", ["j_ude_a_l", "j_ude_a_r"], ["iv_nitoukin_l", "iv_nitoukin_r"], ["j_ude_b_l", "j_ude_b_r"], Array.Empty<string>()),
        new("Waist / Pelvis", ["j_kosi"], ["j_sebo_a"], ["j_asi_a_l", "j_asi_a_r"], ["iv_shiri_l", "iv_shiri_r"]),
        new("Thighs", ["j_asi_a_l", "j_asi_a_r"], ["j_asi_b_l", "j_asi_b_r"], ["j_asi_c_l", "j_asi_c_r"], ["iv_daitai_phys_l", "iv_daitai_phys_r"]),
        new("Neck / Shoulders", ["j_kubi", "j_sebo_c"], ["j_sako_l", "j_sako_r"], ["n_hkata_l", "n_hkata_r"], Array.Empty<string>()),
    ];

    public static IReadOnlyList<string> GetEligibleBones(AuthoringRegion region, AuthoringRegionScope scope, IEnumerable<string>? liveBones = null, bool includeManual = false)
    {
        var live = liveBones == null ? null : new HashSet<string>(liveBones, StringComparer.Ordinal);
        return region.GetBones(scope)
            .Where(bone => live == null || live.Contains(bone))
            .Where(bone => IsEligible(bone, includeManual))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    public static Dictionary<string, BoneTransform> Scale(
        IReadOnlyDictionary<string, BoneTransform> source,
        IEnumerable<string> bones,
        Vector3 multiplier,
        out int skippedLocked)
    {
        var result = AuthoringTooling.CloneTransforms(source);
        skippedLocked = 0;
        foreach (var bone in bones)
        {
            result.TryGetValue(bone, out var transform);
            transform ??= new BoneTransform();
            if (transform.LockState == BoneLockState.Locked)
            {
                skippedLocked++;
                continue;
            }

            var scale = transform.Scaling * multiplier;
            transform.Scaling = transform.ApplyScalePins(scale);
            result[bone] = transform;
        }
        return result;
    }

    public static Dictionary<string, BoneTransform> Mirror(
        IReadOnlyDictionary<string, BoneTransform> source,
        IEnumerable<string> bones,
        bool leftToRight,
        bool copyPosition,
        bool copyRotation,
        bool copyScale,
        bool copyLocksPins,
        out int skipped)
    {
        var result = AuthoringTooling.CloneTransforms(source);
        skipped = 0;
        foreach (var bone in bones)
        {
            var mirror = BoneData.GetAutomationMirror(bone);
            var isLeft = bone.EndsWith("_l", StringComparison.Ordinal);
            if (mirror == null || leftToRight != isLeft || !source.TryGetValue(bone, out var transform))
            {
                skipped++;
                continue;
            }

            var update = result.GetValueOrDefault(mirror)?.DeepCopy() ?? new BoneTransform();
            var reflected = BoneData.GetMetadata(bone).Origin is BoneOrigin.IVCS1 or BoneOrigin.IVCS2
                ? transform.GetSpecialReflection()
                : transform.GetStandardReflection();
            if (copyPosition) update.Translation = reflected.Translation;
            if (copyRotation) update.Rotation = reflected.Rotation;
            if (copyScale) update.Scaling = update.ApplyScalePins(reflected.Scaling);
            if (copyLocksPins)
            {
                update.LockState = reflected.LockState;
                update.PinX = reflected.PinX;
                update.PinY = reflected.PinY;
                update.PinZ = reflected.PinZ;
            }
            result[mirror] = update;
        }
        return result;
    }

    private static bool IsEligible(string bone, bool includeManual)
    {
        var metadata = BoneData.GetMetadata(bone);
        if (metadata.Origin == BoneOrigin.UnknownCustom)
            return includeManual;
        if (metadata.Role is BoneFunctionalRole.ClothingRig or BoneFunctionalRole.PropRig or BoneFunctionalRole.ArticulatedAppendage or BoneFunctionalRole.GearAttachment)
            return false;
        return includeManual || metadata.HasTrust(BoneAutomationTrust.TemplateSafe);
    }
}

internal sealed record TemplateEditTransaction(string Label, IReadOnlyDictionary<string, BoneTransform> Before, IReadOnlyDictionary<string, BoneTransform> After);

/// <summary>Bounded, session-local snapshots. It stores normal model states, not files or live skeleton references.</summary>
internal sealed class TemplateEditHistory
{
    private const int MaximumEntries = 50;
    private readonly LinkedList<TemplateEditTransaction> _undo = new();
    private readonly LinkedList<TemplateEditTransaction> _redo = new();

    public int UndoCount => _undo.Count;
    public int RedoCount => _redo.Count;
    public string LatestLabel => _undo.Last?.Value.Label ?? string.Empty;
    public IReadOnlyList<string> RecentLabels => _undo.Reverse().Take(8).Select(static entry => entry.Label).ToArray();

    public void Clear()
    {
        _undo.Clear();
        _redo.Clear();
    }

    public void Record(string label, IReadOnlyDictionary<string, BoneTransform> before, IReadOnlyDictionary<string, BoneTransform> after)
    {
        if (SameState(before, after))
            return;
        _undo.AddLast(new TemplateEditTransaction(label, AuthoringTooling.CloneTransforms(before), AuthoringTooling.CloneTransforms(after)));
        while (_undo.Count > MaximumEntries)
            _undo.RemoveFirst();
        _redo.Clear();
    }

    public bool TryUndo(out IReadOnlyDictionary<string, BoneTransform> state)
    {
        if (_undo.Last is not { } entry)
        {
            state = new Dictionary<string, BoneTransform>();
            return false;
        }
        _undo.RemoveLast();
        _redo.AddLast(entry.Value);
        state = AuthoringTooling.CloneTransforms(entry.Value.Before);
        return true;
    }

    public bool TryRedo(out IReadOnlyDictionary<string, BoneTransform> state)
    {
        if (_redo.Last is not { } entry)
        {
            state = new Dictionary<string, BoneTransform>();
            return false;
        }
        _redo.RemoveLast();
        _undo.AddLast(entry.Value);
        state = AuthoringTooling.CloneTransforms(entry.Value.After);
        return true;
    }

    private static bool SameState(IReadOnlyDictionary<string, BoneTransform> left, IReadOnlyDictionary<string, BoneTransform> right)
        => left.Count == right.Count
            && left.All(pair => right.TryGetValue(pair.Key, out var other) && AuthoringTooling.TransformEquals(pair.Value, other));
}
