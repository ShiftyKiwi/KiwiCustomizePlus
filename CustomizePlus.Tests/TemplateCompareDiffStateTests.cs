using System.Numerics;
using CustomizePlus.Core.Data;
using CustomizePlus.Templates.Data;
using Xunit;

namespace CustomizePlus.Tests;

public sealed class TemplateCompareDiffStateTests
{
    [Fact]
    public void InitialComparison_UsesTemplateDiffServiceAndDoesNotMutateEitherInput()
    {
        var target = Template(("j_kosi", 1f));
        var working = Template(("j_kosi", 1.2f));
        var targetBefore = AuthoringTooling.CloneTransforms(target.Bones);
        var workingBefore = AuthoringTooling.CloneTransforms(working.Bones);
        var state = new TemplateCompareDiffState();
        TemplateDiffReport? built = null;

        state.Build(Guid.NewGuid(), 0, target, working, (left, right) =>
        {
            built = TemplateDiffService.Compare(left, right);
            return built;
        });

        Assert.Same(built, state.CurrentReport);
        Assert.Equal(1, state.CurrentReport!.ChangedCount);
        Assert.False(state.IsStale);
        AssertTransformsEqual(targetBefore, target.Bones);
        AssertTransformsEqual(workingBefore, working.Bones);
    }

    [Theory]
    [InlineData(TemplateChange.Transform)]
    [InlineData(TemplateChange.Lock)]
    [InlineData(TemplateChange.Pin)]
    public void WorkingCopyChanges_InvalidateThePriorReportAtTheNextRevision(TemplateChange change)
    {
        var target = Template(("j_kosi", 1f));
        var working = Clone(target);
        var state = BuildState(target, working, out var session, out _);

        switch (change)
        {
            case TemplateChange.Transform:
                working.Bones["j_kosi"].Scaling = new Vector3(1.2f);
                break;
            case TemplateChange.Lock:
                working.Bones["j_kosi"].LockState = BoneLockState.Locked;
                break;
            case TemplateChange.Pin:
                working.Bones["j_kosi"].PinY = true;
                break;
        }

        state.Synchronize(session, 1, target);

        Assert.Null(state.CurrentReport);
        Assert.True(state.IsStale);
    }

    [Fact]
    public void UndoRedoAndCheckpointRestore_RevisionsCannotReuseThePriorReport()
    {
        var target = Template(("j_kosi", 1f));
        var baseline = Clone(target);
        var changed = Clone(target);
        changed.Bones["j_kosi"].Scaling = new Vector3(1.2f);
        var history = new TemplateEditHistory();
        history.Record("Scale pelvis", baseline.Bones, changed.Bones);
        var checkpoints = new TemplateEditorCheckpointStore();
        var checkpoint = checkpoints.Capture("Baseline", baseline.Bones);
        var state = BuildState(target, changed, out var session, out _);

        Assert.True(history.TryUndo(out var undone));
        state.Synchronize(session, 1, target);
        Assert.Null(state.CurrentReport);
        state.Build(session, 1, target, TemplateFromState(undone), TemplateDiffService.Compare);

        Assert.True(history.TryRedo(out var redone));
        state.Synchronize(session, 2, target);
        Assert.Null(state.CurrentReport);
        state.Build(session, 2, target, TemplateFromState(redone), TemplateDiffService.Compare);

        Assert.True(checkpoints.TryGetState(checkpoint.Id, out var restored));
        state.Synchronize(session, 3, target);

        Assert.Null(state.CurrentReport);
        Assert.True(state.IsStale);
        AssertTransformsEqual(baseline.Bones, restored);
    }

    [Fact]
    public void TargetSelectionAndTargetContentChanges_InvalidateThePriorReport()
    {
        var target = Template(("j_kosi", 1f));
        var replacement = Template(("j_kosi", 1.1f));
        var working = Template(("j_kosi", 1.2f));
        var state = BuildState(target, working, out var session, out _);

        state.Synchronize(session, 0, replacement);
        Assert.Null(state.CurrentReport);
        Assert.True(state.IsStale);

        state.Build(session, 0, target, working, TemplateDiffService.Compare);
        target.Bones["j_kosi"].Scaling = new Vector3(1.05f);
        state.Synchronize(session, 0, target);

        Assert.Null(state.CurrentReport);
        Assert.True(state.IsStale);
    }

    [Fact]
    public void SessionReplacementAndCloseReopen_ClearPriorReports()
    {
        var target = Template(("j_kosi", 1f));
        var working = Template(("j_kosi", 1.2f));
        var state = BuildState(target, working, out var session, out _);

        state.Synchronize(Guid.Empty, 0, target);
        Assert.Null(state.CurrentReport);
        Assert.True(state.IsStale);

        var reopenedSession = Guid.NewGuid();
        state.Synchronize(reopenedSession, 0, target);

        Assert.Null(state.CurrentReport);
        Assert.False(state.IsStale);
        Assert.NotEqual(session, reopenedSession);
    }

    [Fact]
    public void UnchangedInputs_DoNotRecomputeTheCachedReport()
    {
        var target = Template(("j_kosi", 1f));
        var working = Template(("j_kosi", 1.2f));
        var state = new TemplateCompareDiffState();
        var session = Guid.NewGuid();
        var comparisons = 0;

        state.Build(session, 7, target, working, (left, right) =>
        {
            comparisons++;
            return TemplateDiffService.Compare(left, right);
        });
        state.Synchronize(session, 7, target);
        state.Synchronize(session, 7, target);

        Assert.Equal(1, comparisons);
        Assert.NotNull(state.CurrentReport);
        Assert.False(state.IsStale);
    }

    private static TemplateCompareDiffState BuildState(Template target, Template working, out Guid session, out TemplateDiffReport report)
    {
        session = Guid.NewGuid();
        var state = new TemplateCompareDiffState();
        state.Build(session, 0, target, working, TemplateDiffService.Compare);
        report = state.CurrentReport!;
        return state;
    }

    private static Template Template(params (string Bone, float Scale)[] values)
    {
        var template = new Template();
        foreach (var (bone, scale) in values)
            template.Bones[bone] = new BoneTransform { Scaling = new Vector3(scale) };
        return template;
    }

    private static Template Clone(Template source)
    {
        var clone = new Template();
        foreach (var (bone, transform) in source.Bones)
            clone.Bones[bone] = transform.DeepCopy();
        return clone;
    }

    private static Template TemplateFromState(IReadOnlyDictionary<string, BoneTransform> state)
    {
        var template = new Template();
        foreach (var (bone, transform) in state)
            template.Bones[bone] = transform.DeepCopy();
        return template;
    }

    private static void AssertTransformsEqual(IReadOnlyDictionary<string, BoneTransform> expected, IReadOnlyDictionary<string, BoneTransform> actual)
    {
        Assert.Equal(expected.Count, actual.Count);
        foreach (var (bone, transform) in expected)
        {
            Assert.True(actual.TryGetValue(bone, out var current));
            Assert.True(AuthoringTooling.TransformEquals(transform, current));
        }
    }

    public enum TemplateChange
    {
        Transform,
        Lock,
        Pin,
    }
}
