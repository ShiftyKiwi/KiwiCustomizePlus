// Copyright (c) Customize+.
// Licensed under the MIT license.

using System.Collections.Generic;

namespace CustomizePlus.Armatures.Data;

/// <summary>Describes the safe, context-dependent choice of one actor object from an actor group.</summary>
internal readonly record struct ArmatureActorSelectionCandidate(
    bool IsGPoseOrCutscene,
    bool IsValid,
    bool HasCharacterBase,
    bool HasSkeleton,
    bool MatchesExpectedIdentifier)
{
    public bool IsBindable
        => IsValid && HasCharacterBase && HasSkeleton && MatchesExpectedIdentifier;
}

/// <summary>
/// Character Select identities are established by AgentLobby before the preview actor can derive
/// a normal ActorIdentifier. The caller must therefore supply only that authoritative lobby group.
/// </summary>
internal readonly record struct ArmatureLobbyActorSelectionCandidate(
    bool IsValid,
    bool HasCharacterBase,
    bool HasSkeleton)
{
    public bool IsBindable
        => IsValid && HasCharacterBase && HasSkeleton;
}

internal readonly record struct ArmatureActorSelectionResult(int Index, string Reason)
{
    public bool IsSelected
        => Index >= 0;

    public static ArmatureActorSelectionResult Selected(int index)
        => new(index, "selected");

    public static ArmatureActorSelectionResult Rejected(string reason)
        => new(-1, reason);
}

#if DEBUG
/// <summary>Read-only candidate state exposed through the development bridge for cutscene diagnosis.</summary>
internal readonly record struct ArmatureActorSelectionDebugCandidate(
    int ObjectIndex,
    bool IsGPoseOrCutscene,
    bool IsValid,
    bool HasCharacterBase,
    bool HasSkeleton,
    bool MatchesExpectedIdentifier);

/// <summary>Compares the active runtime selection policy with a cutscene-aware candidate selection.</summary>
internal sealed record ArmatureActorSelectionDebugSnapshot(
    string Actor,
    bool GroupFound,
    bool IsInGPose,
    int GroupObjectCount,
    int CurrentSelectedObjectIndex,
    string CurrentSelectionReason,
    int CutsceneAwareSelectedObjectIndex,
    string CutsceneAwareSelectionReason,
    IReadOnlyList<ArmatureActorSelectionDebugCandidate> Candidates);

/// <summary>Read-only raw slot evidence for a cutscene that has not yet produced an armature.</summary>
internal sealed record ArmatureCutsceneActorDebugSnapshot(
    int ObjectIndex,
    int CutsceneParentIndex,
    string GameObjectId,
    string Identifier,
    bool IsValid,
    bool IsCharacter,
    bool IsPlayer,
    bool HasCharacterBase,
    bool HasSkeleton);

internal sealed record ArmatureCutsceneSelectionDebugSnapshot(
    ArmatureCutsceneActorDebugSnapshot PlayerSlot,
    IReadOnlyList<ArmatureCutsceneActorDebugSnapshot> CutsceneSlots);

/// <summary>Read-only Character Select evidence derived from the lobby's authoritative actor groups.</summary>
internal sealed record ArmatureLobbySelectionDebugSnapshot(
    bool IsInLobby,
    bool ApplyProfilesInLobby,
    IReadOnlyList<ArmatureLobbyActorDebugSnapshot> Actors);

internal sealed record ArmatureLobbyActorDebugSnapshot(
    string ExpectedIdentifier,
    int ObjectIndex,
    string ObjectKind,
    bool IsValid,
    bool IsCharacter,
    bool HasCharacterBase,
    bool HasSkeleton,
    bool IsGPoseOrCutscene,
    string DerivedIdentifier,
    bool MatchesExpectedIdentifier,
    string SelectionReason,
    int SelectedObjectIndex,
    string ProfileName,
    string ProfileId,
    IReadOnlyList<ArmatureLobbyTemplateDebugSnapshot> Templates,
    bool ArmatureExists,
    bool BindingCurrent,
    long SkeletonRevision,
    int ResolvedTransformCount,
    string BindingIssue);

internal sealed record ArmatureLobbyTemplateDebugSnapshot(
    string Name,
    bool Enabled,
    float Weight,
    int SavedTransformCount);
#endif

/// <summary>
/// Keeps ordinary actor ordering intact while making GPose/cutscene replacement selection explicit,
/// validated, and order-independent.
/// </summary>
internal static class ArmatureActorSelection
{
    /// <summary>
    /// GPose and ordinary quest cutscenes both require a validated replacement selection when an
    /// actor group contains a cutscene copy. Normal world groups retain their first-object path.
    /// </summary>
    public static bool RequiresValidatedSelection(bool isInGPose, bool hasCutsceneActor)
        => isInGPose || hasCutsceneActor;

    public static ArmatureActorSelectionResult Select(IReadOnlyList<ArmatureActorSelectionCandidate> candidates, bool isInGPose)
    {
        if (candidates.Count == 0)
            return ArmatureActorSelectionResult.Rejected("no actor objects were available");

        // Normal-world behavior is intentionally unchanged: ActorObjectManager's first object
        // remains the selected instance and the normal binding validators decide readiness.
        if (!isInGPose)
            return ArmatureActorSelectionResult.Selected(0);

        var hasGPoseCandidate = false;
        var selectedIndex = -1;
        var selectedCount = 0;
        for (var index = 0; index < candidates.Count; ++index)
        {
            var candidate = candidates[index];
            if (!candidate.IsGPoseOrCutscene)
                continue;

            hasGPoseCandidate = true;
            if (!candidate.IsBindable)
                continue;

            selectedIndex = index;
            selectedCount++;
        }

        if (hasGPoseCandidate)
        {
            return selectedCount switch
            {
                1 => ArmatureActorSelectionResult.Selected(selectedIndex),
                0 => ArmatureActorSelectionResult.Rejected("no bindable GPose/cutscene actor copy was available"),
                _ => ArmatureActorSelectionResult.Rejected("multiple bindable GPose/cutscene actor copies were available"),
            };
        }

        // Some existing GPose paths, including the local player, retain one live non-cutscene
        // instance. Preserve that supported path only when it is uniquely bindable.
        selectedIndex = -1;
        selectedCount = 0;
        for (var index = 0; index < candidates.Count; ++index)
        {
            if (!candidates[index].IsBindable)
                continue;

            selectedIndex = index;
            selectedCount++;
        }

        return selectedCount switch
        {
            1 => ArmatureActorSelectionResult.Selected(selectedIndex),
            0 => ArmatureActorSelectionResult.Rejected("no uniquely bindable actor was available during GPose"),
            _ => ArmatureActorSelectionResult.Rejected("multiple bindable non-GPose actor copies were available during GPose"),
        };
    }

    /// <summary>
    /// Accepts a lobby preview only when ActorObjectManager's AgentLobby-derived group provides
    /// exactly one currently bindable actor. It never attempts a runtime identifier fallback.
    /// </summary>
    public static ArmatureActorSelectionResult SelectLobby(IReadOnlyList<ArmatureLobbyActorSelectionCandidate> candidates)
    {
        if (candidates.Count != 1)
            return ArmatureActorSelectionResult.Rejected("lobby actor group did not contain exactly one authoritative preview actor");

        return candidates[0].IsBindable
            ? ArmatureActorSelectionResult.Selected(0)
            : ArmatureActorSelectionResult.Rejected("authoritative lobby preview actor was not currently bindable");
    }
}
