using System.Numerics;
using CustomizePlus.Core.Data;
using Xunit;

namespace CustomizePlus.Tests;

public sealed class LiveCheckpointDiffFilterStateTests
{
    [Fact]
    public void NoSelectedCheckpoint_LeavesFilteringDisabledAndDoesNotCompare()
    {
        var filter = new LiveCheckpointDiffFilterState();
        filter.BeginSession(Guid.NewGuid());
        var comparisons = 0;

        filter.SetFilterEnabled(true);
        filter.Synchronize(0, _ =>
        {
            comparisons++;
            return null;
        });

        Assert.False(filter.IsFilterEnabled);
        Assert.False(filter.IncludesBone("j_kosi"));
        Assert.Equal(0, comparisons);
    }

    [Fact]
    public void IdenticalCheckpointAndWorkingCopy_ProducesAnEmptyChangedSet()
    {
        var checkpoint = State(("j_kosi", 1f));
        var filter = CreateFilter(out var checkpointId);

        filter.SetFilterEnabled(true);
        filter.Synchronize(0, _ => TemplateDiffService.Compare(checkpoint, Clone(checkpoint)));

        Assert.NotNull(filter.CurrentReport);
        Assert.Equal(0, filter.ChangedBoneCount);
        Assert.False(filter.IncludesBone("j_kosi"));
        Assert.Equal(checkpointId, filter.SelectedCheckpointId);
    }

    [Fact]
    public void TransformLockAndPinDifferences_AreAllIncludedByTheAuthoritativeDiff()
    {
        var checkpoint = State(("j_kosi", 1f), ("j_hara", 1f), ("j_ude_a_l", 1f), ("j_asi_a_l", 1f));
        var current = Clone(checkpoint);
        current["j_kosi"].Scaling = new Vector3(1.2f);
        current["j_hara"].LockState = BoneLockState.Locked;
        current["j_ude_a_l"].PinY = true;
        current.Remove("j_asi_a_l");

        var filter = CreateFilter(out _);
        filter.SetFilterEnabled(true);
        filter.Synchronize(0, _ => TemplateDiffService.Compare(checkpoint, current));

        Assert.Equal(4, filter.ChangedBoneCount);
        Assert.True(filter.IncludesBone("j_kosi"));
        Assert.True(filter.IncludesBone("j_hara"));
        Assert.True(filter.IncludesBone("j_ude_a_l"));
        Assert.True(filter.IncludesBone("j_asi_a_l"));
        Assert.False(filter.IncludesBone("j_mune_l"));
    }

    [Fact]
    public void FilterComposition_IntersectsChangedRowsWithExistingSearchResults()
    {
        var checkpoint = State(("j_sako_l", 1f), ("j_sako_r", 1f), ("j_hara", 1f));
        var current = Clone(checkpoint);
        current["j_sako_l"].Scaling = new Vector3(1.1f);
        current["j_sako_r"].Scaling = new Vector3(1.1f);
        var filter = CreateFilter(out _);
        filter.SetFilterEnabled(true);
        filter.Synchronize(0, _ => TemplateDiffService.Compare(checkpoint, current));

        var visible = current.Keys
            .Where(filter.IncludesBone)
            .Where(static bone => bone.EndsWith("_l", StringComparison.Ordinal))
            .ToArray();

        Assert.Equal(new[] { "j_sako_l" }, visible);
    }

    [Fact]
    public void RevisionChanges_RefreshTheCachedDiffInsteadOfReusingStaleRows()
    {
        var checkpoint = State(("j_kosi", 1f), ("j_hara", 1f));
        var current = Clone(checkpoint);
        var filter = CreateFilter(out _);
        var comparisons = 0;
        filter.SetFilterEnabled(true);

        filter.Synchronize(0, _ =>
        {
            comparisons++;
            return TemplateDiffService.Compare(checkpoint, current);
        });
        Assert.Equal(0, filter.ChangedBoneCount);

        current["j_kosi"].Scaling = new Vector3(1.2f);
        filter.Synchronize(0, _ =>
        {
            comparisons++;
            return TemplateDiffService.Compare(checkpoint, current);
        });
        Assert.Equal(1, comparisons);
        Assert.Equal(0, filter.ChangedBoneCount);

        filter.Synchronize(1, _ =>
        {
            comparisons++;
            return TemplateDiffService.Compare(checkpoint, current);
        });
        Assert.Equal(2, comparisons);
        Assert.True(filter.IncludesBone("j_kosi"));
    }

    [Fact]
    public void UndoRedoAndCheckpointRestore_FollowTheCurrentWorkingState()
    {
        var checkpoint = State(("j_kosi", 1f));
        var edited = Clone(checkpoint);
        edited["j_kosi"].Scaling = new Vector3(1.2f);
        var history = new TemplateEditHistory();
        history.Record("Edit pelvis", checkpoint, edited);
        var current = Clone(edited);
        var filter = CreateFilter(out _);
        filter.SetFilterEnabled(true);

        filter.Synchronize(1, _ => TemplateDiffService.Compare(checkpoint, current));
        Assert.True(filter.IncludesBone("j_kosi"));

        Assert.True(history.TryUndo(out var undone));
        current = Clone(undone);
        filter.Synchronize(2, _ => TemplateDiffService.Compare(checkpoint, current));
        Assert.Equal(0, filter.ChangedBoneCount);

        Assert.True(history.TryRedo(out var redone));
        current = Clone(redone);
        filter.Synchronize(3, _ => TemplateDiffService.Compare(checkpoint, current));
        Assert.True(filter.IncludesBone("j_kosi"));

        current = Clone(checkpoint);
        filter.Synchronize(4, _ => TemplateDiffService.Compare(checkpoint, current));
        Assert.Equal(0, filter.ChangedBoneCount);

        current = Clone(edited);
        filter.Synchronize(5, _ => TemplateDiffService.Compare(checkpoint, current));
        Assert.True(filter.IncludesBone("j_kosi"));
    }

    [Fact]
    public void SessionReplacementAndCheckpointDeletion_ClearTheActiveFilter()
    {
        var store = new TemplateEditorCheckpointStore();
        var checkpoint = store.Capture("A", State(("j_kosi", 1f)));
        var filter = new LiveCheckpointDiffFilterState();
        filter.BeginSession(Guid.NewGuid());
        filter.SelectCheckpoint(checkpoint.Id);
        filter.SetFilterEnabled(true);

        Assert.True(store.Remove(checkpoint.Id));
        Assert.True(filter.ClearIfSelectedCheckpointIsMissing(store.Items));
        Assert.False(filter.IsFilterEnabled);
        Assert.Equal(Guid.Empty, filter.SelectedCheckpointId);

        filter.SelectCheckpoint(Guid.NewGuid());
        filter.SetFilterEnabled(true);
        filter.BeginSession(Guid.NewGuid());
        Assert.False(filter.IsFilterEnabled);
        Assert.Equal(Guid.Empty, filter.SelectedCheckpointId);
    }

    [Fact]
    public void CheckpointIdentity_UsesTheStableIdWhenDisplayNamesCollide()
    {
        var store = new TemplateEditorCheckpointStore();
        var baseline = State(("j_kosi", 1f));
        var alternate = State(("j_kosi", 1.2f));
        var first = store.Capture("Same name", baseline);
        var second = store.Capture("Same name", alternate);
        var current = Clone(baseline);
        var filter = new LiveCheckpointDiffFilterState();
        filter.BeginSession(Guid.NewGuid());
        filter.SelectCheckpoint(second.Id);
        filter.SetFilterEnabled(true);

        filter.Synchronize(0, checkpointId => store.TryGetState(checkpointId, out var state)
            ? TemplateDiffService.Compare(state, current)
            : null);

        Assert.True(filter.IncludesBone("j_kosi"));

        filter.SelectCheckpoint(first.Id);
        filter.Synchronize(0, checkpointId => store.TryGetState(checkpointId, out var state)
            ? TemplateDiffService.Compare(state, current)
            : null);

        Assert.Equal(0, filter.ChangedBoneCount);
    }

    [Fact]
    public void PresentationState_DoesNotMutateCheckpointOrWorkingTransforms()
    {
        var checkpoint = State(("j_kosi", 1f));
        var working = Clone(checkpoint);
        working["j_kosi"].Scaling = new Vector3(1.2f);
        var checkpointBefore = Clone(checkpoint);
        var workingBefore = Clone(working);
        var filter = CreateFilter(out _);

        filter.RequestComparison();
        filter.SetFilterEnabled(true);
        filter.Synchronize(0, _ => TemplateDiffService.Compare(checkpoint, working));
        filter.SetFilterEnabled(false);

        Assert.True(AuthoringTooling.TransformEquals(checkpointBefore["j_kosi"], checkpoint["j_kosi"]));
        Assert.True(AuthoringTooling.TransformEquals(workingBefore["j_kosi"], working["j_kosi"]));
    }

    private static LiveCheckpointDiffFilterState CreateFilter(out Guid checkpointId)
    {
        checkpointId = Guid.NewGuid();
        var filter = new LiveCheckpointDiffFilterState();
        filter.BeginSession(Guid.NewGuid());
        filter.SelectCheckpoint(checkpointId);
        return filter;
    }

    private static Dictionary<string, BoneTransform> State(params (string Bone, float Scale)[] values)
        => values.ToDictionary(
            static value => value.Bone,
            static value => new BoneTransform { Scaling = new Vector3(value.Scale) },
            StringComparer.Ordinal);

    private static Dictionary<string, BoneTransform> Clone(IReadOnlyDictionary<string, BoneTransform> source)
        => AuthoringTooling.CloneTransforms(source);
}
