using System.Numerics;
using CustomizePlus.Core.Data;
using CustomizePlus.Core.Services;
using Xunit;

namespace CustomizePlus.Tests;

public class TemplateAuthoringOperationTests
{
    [Fact]
    public void AuthoringState_UsesWorkingCopyOnlyWhileEditorIsActive()
    {
        var saved = State(("j_kosi", 1.0f));
        var working = State(("j_kosi", 1.25f));

        Assert.Equal(1.0f, TemplateAuthoringState.SelectBones(saved, working, false)["j_kosi"].Scaling.X);
        Assert.Equal(1.25f, TemplateAuthoringState.SelectBones(saved, working, true)["j_kosi"].Scaling.X);
        Assert.True(TemplateAuthoringState.IsStale(4, 5, true));
        Assert.False(TemplateAuthoringState.IsStale(4, 5, false));
    }

    [Fact]
    public void ToolRevert_PreservesAnUnrelatedLaterEdit()
    {
        var before = State(("j_kosi", 1.0f), ("j_ude_a_l", 1.0f));
        var requested = State(("j_kosi", 1.2f));
        var operation = Assert.IsType<TemplateAuthoringOperation>(TemplateAuthoringOperation.Create("Apply test", before, requested));
        Assert.True(operation.CanApply(before));

        var after = Clone(before);
        foreach (var (bone, transform) in operation.After)
            after[bone] = transform.DeepCopy();
        after["j_ude_a_l"].Scaling = new Vector3(1.35f);

        Assert.True(operation.TryCreateRevert(after, "Revert test", out var revert));
        var revertOperation = Assert.IsType<TemplateAuthoringOperation>(revert);
        foreach (var (bone, transform) in revertOperation.After)
            after[bone] = transform.DeepCopy();

        Assert.Equal(1.0f, after["j_kosi"].Scaling.X);
        Assert.Equal(1.35f, after["j_ude_a_l"].Scaling.X);
    }

    [Fact]
    public void ToolRevert_RejectsAChangedAffectedRow()
    {
        var before = State(("j_kosi", 1.0f));
        var operation = Assert.IsType<TemplateAuthoringOperation>(TemplateAuthoringOperation.Create("Apply test", before, State(("j_kosi", 1.2f))));
        var changed = State(("j_kosi", 1.3f));

        Assert.False(operation.TryCreateRevert(changed, "Revert test", out _));
    }

    [Fact]
    public void ToolRevert_AcceptsAResultThatNormalizedToAnAbsentRow()
    {
        var before = State(("j_kosi", 1.2f));
        var operation = Assert.IsType<TemplateAuthoringOperation>(TemplateAuthoringOperation.Create(
            "Apply normalization",
            before,
            new Dictionary<string, BoneTransform> { ["j_kosi"] = new BoneTransform() }));
        var after = new Dictionary<string, BoneTransform>(StringComparer.Ordinal);

        Assert.True(operation.TryCreateRevert(after, "Revert normalization", out var revert));
        var revertOperation = Assert.IsType<TemplateAuthoringOperation>(revert);
        Assert.Equal(1.2f, revertOperation.After["j_kosi"].Scaling.X);
    }

    [Fact]
    public void ToolApply_IsDeterministicAndHistoryRoundTrips()
    {
        var before = State(("j_kosi", 1.0f));
        var operation = Assert.IsType<TemplateAuthoringOperation>(TemplateAuthoringOperation.Create("Apply Analyzer", before, State(("j_kosi", 1.2f))));
        var after = Clone(before);
        foreach (var (bone, transform) in operation.After)
            after[bone] = transform.DeepCopy();

        Assert.Null(TemplateAuthoringOperation.Create("Apply Analyzer", after, operation.After));

        var history = new TemplateEditHistory();
        history.Record(operation.Label, before, after);
        Assert.True(history.TryUndo(out var undone));
        Assert.Equal(1.0f, undone["j_kosi"].Scaling.X);
        Assert.True(history.TryRedo(out var redone));
        Assert.Equal(1.2f, redone["j_kosi"].Scaling.X);
    }

    [Fact]
    public void ScaleOperation_PreservesPinnedAxes()
    {
        var current = State(("j_kosi", 1.1f));
        current["j_kosi"].PinX = true;
        var request = State(("j_kosi", 1.3f));

        var operation = Assert.IsType<TemplateAuthoringOperation>(TemplateAuthoringOperation.CreateScaleOperation("Apply preview", current, request));

        Assert.Equal(1.1f, operation.After["j_kosi"].Scaling.X);
        Assert.Equal(1.3f, operation.After["j_kosi"].Scaling.Y);
        Assert.Equal(1.3f, operation.After["j_kosi"].Scaling.Z);
    }

    [Fact]
    public void SemanticRecipeAndPreview_DoNotMutateInputUntilOperationIsApplied()
    {
        var service = new SemanticBodyGoalService();
        var source = State(("j_mune_l", 1.0f), ("j_mune_r", 1.0f));
        var sourceBefore = Clone(source);
        var recipe = Assert.IsType<ShapeRecipe>(service.Recipes.FirstOrDefault());

        var values = service.CreateRecipeGoalValues(recipe);
        var preview = service.BuildPreview(values, source, new HashSet<string>(source.Keys), 1);

        Assert.Equal(sourceBefore["j_mune_l"].Scaling, source["j_mune_l"].Scaling);
        Assert.Equal(sourceBefore["j_mune_r"].Scaling, source["j_mune_r"].Scaling);
        Assert.False(ReferenceEquals(source, preview.FinalTransforms));
    }

    private static Dictionary<string, BoneTransform> State(params (string Bone, float Scale)[] values)
        => values.ToDictionary(
            static value => value.Bone,
            static value => new BoneTransform { Scaling = new Vector3(value.Scale) },
            StringComparer.Ordinal);

    private static Dictionary<string, BoneTransform> Clone(IReadOnlyDictionary<string, BoneTransform> state)
        => state.ToDictionary(static pair => pair.Key, static pair => pair.Value.DeepCopy(), StringComparer.Ordinal);
}
