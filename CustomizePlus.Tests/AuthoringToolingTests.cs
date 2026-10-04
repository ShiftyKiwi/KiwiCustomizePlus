using System.Numerics;
using CustomizePlus.Core.Data;
using CustomizePlus.Profiles.Data;
using CustomizePlus.Templates.Data;
using Xunit;

namespace CustomizePlus.Tests;

public class AuthoringToolingTests
{
    [Fact]
    public void TemplateDiff_CopiesOnlySelectedSourceRows()
    {
        var source = TemplateWith(("j_ude_a_l", 1.20f), ("j_kosi", 1.10f));
        var target = TemplateWith(("j_ude_a_l", 1.05f), ("j_asi_a_l", 1.15f));
        var report = TemplateDiffService.Compare(source, target);

        Assert.Equal(1, report.ChangedCount);
        Assert.Equal(1, report.OnlyLeftCount);
        Assert.Equal(1, report.OnlyRightCount);

        var copied = TemplateDiffService.CopyFrom(target.Bones, report.Rows.Where(row => row.BoneName == "j_ude_a_l"), true, false, false, true, true);
        Assert.Equal(1.20f, copied["j_ude_a_l"].Scaling.X);
        Assert.Equal(1.15f, copied["j_asi_a_l"].Scaling.X);
    }

    [Fact]
    public void ProfileDiff_ReportsAssignmentWeightAndEnableState()
    {
        var template = TemplateWith(("j_kosi", 1.1f));
        var left = new Profile { Priority = 1 };
        left.Templates.Add(template);
        left.SetTemplateWeight(template.UniqueId, 1f);
        var right = new Profile { Priority = 2 };
        right.Templates.Add(template);
        right.DisabledTemplates.Add(template.UniqueId);
        right.SetTemplateWeight(template.UniqueId, 0.5f);

        var report = ProfileDiffService.Compare(left, right);

        Assert.True(report.PriorityChanged);
        var row = Assert.Single(report.Templates);
        Assert.True(row.EnabledLeft);
        Assert.False(row.EnabledRight);
        Assert.Equal(0.5f, row.WeightRight);
    }

    [Fact]
    public void CompatibilityPreview_UsesProductionRequirementEvaluation()
    {
        var template = TemplateWith(("j_kosi", 1.1f));
        var profile = new Profile();
        profile.Templates.Add(template);
        profile.SetTemplateCompatibilityRequirement(template.UniqueId, new TemplateCompatibilityRequirement(SkeletonCapability.IVCS2));

        var report = CompatibilityPreviewService.Preview(profile, SkeletonCapabilityManifest.Unavailable);

        var row = Assert.Single(report.Rows);
        Assert.False(row.Active);
        Assert.Equal(1, report.DormantEntries);
    }

    [Fact]
    public void CompatibilityPreview_KnownButAbsentBoneIsNotDirectlyPresent()
    {
        var template = TemplateWith(("j_ude_a_l", 1.1f));
        var profile = new Profile();
        profile.Templates.Add(template);
        var manifest = new SkeletonCapabilityManifest(
            SkeletonCapability.VanillaCore,
            0,
            1,
            "test",
            2,
            true,
            new SkeletonTopologySummary(1, 1, 1, 0, 0, new[] { 1 }, Array.Empty<int>(), true),
            new Dictionary<SkeletonCapability, SkeletonCapabilityEvidence>(),
            BoneAnimationCompatibility.VanillaBaseline,
            new Dictionary<string, int>(),
            Array.Empty<string>(),
            Array.Empty<string>(),
            new[] { "j_kosi" });

        var report = CompatibilityPreviewService.Preview(profile, manifest);

        Assert.Equal(0, report.DirectlyPresentEntries);
        Assert.Equal(1, report.KnownButAbsentEntries);
        Assert.Equal(1, Assert.Single(report.Rows).KnownButAbsent);
    }

    [Fact]
    public void ActorHealth_TreatsAppearanceTransitionAsTemporaryWaiting()
    {
        var report = ActorHealthReport.Evaluate(new ActorHealthInput(true, true, false, true, false, 0, 0, 0, "none"));

        Assert.Equal(ActorHealthState.TemporarilyWaiting, report.State);
        Assert.DoesNotContain("error", report.Summary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ActorHealth_TreatsHistoricalBlockedWritesAsInformationalAfterRecovery()
    {
        var report = ActorHealthReport.Evaluate(new ActorHealthInput(true, true, true, false, false, 0, 4, 2, "none"));

        Assert.Equal(ActorHealthState.Healthy, report.State);
        Assert.Contains(report.Details, detail => detail.Contains("previous stale write", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(report.Details, detail => detail.Contains("previous unsafe write", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void RegionTools_ExcludeUnknownAndUseCuratedMirrorOnly()
    {
        var chest = RegionBatchEditService.Regions.First(region => region.Name == "Chest");
        var bones = RegionBatchEditService.GetEligibleBones(chest, AuthoringRegionScope.Primary);
        Assert.Contains("j_mune_l", bones);
        Assert.DoesNotContain("nf_shrt_unknown", bones);

        var source = new Dictionary<string, BoneTransform>
        {
            ["j_mune_l"] = new() { Scaling = new Vector3(1.2f) },
            ["j_mune_r"] = new() { Scaling = Vector3.One },
        };
        var mirrored = RegionBatchEditService.Mirror(source, ["j_mune_l"], true, false, false, true, false, out var skipped);
        Assert.Equal(0, skipped);
        Assert.Equal(1.2f, mirrored["j_mune_r"].Scaling.X);
    }

    [Fact]
    public void EditHistory_IsBoundedAndRoundTripsExactTemplateStates()
    {
        var history = new TemplateEditHistory();
        var before = new Dictionary<string, BoneTransform> { ["j_kosi"] = new() { Scaling = Vector3.One } };
        var after = new Dictionary<string, BoneTransform> { ["j_kosi"] = new() { Scaling = new Vector3(1.2f) } };
        history.Record("Scale pelvis", before, after);

        Assert.True(history.TryUndo(out var undone));
        Assert.Equal(1f, undone["j_kosi"].Scaling.X);
        Assert.True(history.TryRedo(out var redone));
        Assert.Equal(1.2f, redone["j_kosi"].Scaling.X);

        for (var index = 0; index < 60; index++)
            history.Record($"Edit {index}", before, new Dictionary<string, BoneTransform> { ["j_kosi"] = new() { Scaling = new Vector3(1f + index / 100f) } });
        Assert.Equal(50, history.UndoCount);
    }

    [Fact]
    public void Checkpoints_DeepCopyEveryMutableTransformFieldAndRemainIndependent()
    {
        var source = new Dictionary<string, BoneTransform>(StringComparer.Ordinal)
        {
            ["j_kosi"] = new BoneTransform
            {
                Translation = new Vector3(0.1f, -0.2f, 0.3f),
                Rotation = new Vector3(0.4f, -0.5f, 0.6f),
                Scaling = new Vector3(1.1f, 0.9f, 1.2f),
                ChildScaling = new Vector3(0.8f, 1.3f, 0.7f),
                ChildScalingIndependent = true,
                PropagateTranslation = true,
                PropagateRotation = true,
                PropagateScale = true,
                PropagationFalloff = 0.37f,
                LockState = BoneLockState.Priority,
                PinX = true,
                PinY = false,
                PinZ = true,
            },
            ["j_hara"] = new BoneTransform { LockState = BoneLockState.Locked, PinY = true },
        };
        var expected = AuthoringTooling.CloneTransforms(source);
        var checkpoints = new TemplateEditorCheckpointStore();

        var checkpoint = checkpoints.Capture("A", source);
        source["j_kosi"].Scaling = new Vector3(1.7f);
        source["j_hara"].PinY = false;

        Assert.True(checkpoints.TryGetState(checkpoint.Id, out var firstRestore));
        Assert.True(AuthoringTooling.TransformEquals(expected["j_kosi"], firstRestore["j_kosi"]));
        Assert.True(AuthoringTooling.TransformEquals(expected["j_hara"], firstRestore["j_hara"]));
        Assert.False(ReferenceEquals(expected["j_kosi"], firstRestore["j_kosi"]));

        firstRestore["j_kosi"].PropagationFalloff = 0.91f;
        Assert.True(checkpoints.TryGetState(checkpoint.Id, out var secondRestore));
        Assert.True(AuthoringTooling.TransformEquals(expected["j_kosi"], secondRestore["j_kosi"]));
    }

    [Fact]
    public void Checkpoints_CompareRestoreHistoryAndDeletionUseIndependentStates()
    {
        var checkpoints = new TemplateEditorCheckpointStore();
        var checkpointAState = new Dictionary<string, BoneTransform>(StringComparer.Ordinal)
        {
            ["j_kosi"] = new BoneTransform { Scaling = new Vector3(1.1f), LockState = BoneLockState.Locked, PinX = true },
            ["j_hara"] = new BoneTransform { ChildScaling = new Vector3(0.9f), ChildScalingIndependent = true, PropagateScale = true, PropagationFalloff = 0.6f },
        };
        var checkpointA = checkpoints.Capture("A", checkpointAState);
        var current = AuthoringTooling.CloneTransforms(checkpointAState);
        current["j_kosi"].Scaling = new Vector3(1.3f, 0.8f, 1.15f);
        current["j_kosi"].PinX = false;
        current["j_hara"].PropagationFalloff = 0.2f;
        var checkpointB = checkpoints.Capture("B", current);
        current["j_hara"].LockState = BoneLockState.Priority;

        Assert.True(checkpoints.TryGetState(checkpointA.Id, out var restoreA));
        var diff = TemplateDiffService.Compare(restoreA, current);
        Assert.Equal(2, diff.ChangedCount);
        Assert.Contains(diff.Rows, static row => row.LockChanged || row.PinsChanged);
        Assert.Contains(diff.Rows, static row => row.ScalingDelta != Vector3.Zero);

        var history = new TemplateEditHistory();
        history.Record("Restore checkpoint 'A'", current, restoreA);
        Assert.True(history.TryUndo(out var undone));
        Assert.True(AuthoringTooling.TransformEquals(current["j_kosi"], undone["j_kosi"]));
        Assert.True(history.TryRedo(out var redone));
        Assert.True(AuthoringTooling.TransformEquals(restoreA["j_hara"], redone["j_hara"]));

        Assert.True(checkpoints.Remove(checkpointA.Id));
        Assert.Single(checkpoints.Items);
        Assert.Equal(checkpointB.Id, checkpoints.Items[0].Id);
        checkpoints.Clear();
        Assert.Empty(checkpoints.Items);
    }

    private static Template TemplateWith(params (string Bone, float Scale)[] transforms)
    {
        var template = new Template();
        foreach (var (bone, scale) in transforms)
            template.Bones[bone] = new BoneTransform { Scaling = new Vector3(scale) };
        return template;
    }
}
