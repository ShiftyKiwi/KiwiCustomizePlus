using CustomizePlus.Core.Data;
using Xunit;

namespace CustomizePlus.Tests;

public class SkeletonCapabilityManifestTests
{
    [Fact]
    public void Manifest_IsDeterministicAcrossInputOrder()
    {
        var ordered = Build("n_root", "j_kosi", "j_sebo_a", "j_ude_a_l", "j_ude_a_r", "j_asi_a_l", "j_asi_a_r");
        var reversed = ordered.Reverse().ToArray();

        var first = BoneData.EvaluateCapabilityManifest(ordered, new[] { ordered.Length }, 1, 1, true);
        var second = BoneData.EvaluateCapabilityManifest(reversed, new[] { ordered.Length }, 1, 1, true);

        Assert.Equal(first.StructuralFingerprint, second.StructuralFingerprint);
        Assert.Equal(SkeletonCapabilityState.Present, first.GetState(SkeletonCapability.VanillaCore));
    }

    [Fact]
    public void Manifest_IgnoresConditionalExControlsInStructuralFingerprint()
    {
        var baseline = Build("n_root", "j_kosi", "j_sebo_a", "gear_control_ex");
        var replacement = Build("n_root", "j_kosi", "j_sebo_a", "different_control_ex");

        var first = BoneData.EvaluateCapabilityManifest(baseline, new[] { baseline.Length }, 1, 1, true);
        var second = BoneData.EvaluateCapabilityManifest(replacement, new[] { replacement.Length }, 1, 1, true);

        Assert.Equal(first.StructuralFingerprint, second.StructuralFingerprint);
        Assert.Equal(1, first.FamilyCounts["conditional-ex"]);
    }

    [Fact]
    public void Manifest_ChangesFingerprintWhenTopologyChanges()
    {
        var baseline = new[]
        {
            new ObservedSkeletonBone(0, 0, "n_root", -1),
            new ObservedSkeletonBone(0, 1, "j_kosi", 0),
        };
        var changed = new[]
        {
            new ObservedSkeletonBone(0, 0, "n_root", -1),
            new ObservedSkeletonBone(0, 1, "j_kosi", -1),
        };

        var first = BoneData.EvaluateCapabilityManifest(baseline, new[] { 2 }, 1, 1, true);
        var second = BoneData.EvaluateCapabilityManifest(changed, new[] { 2 }, 2, 2, true);

        Assert.NotEqual(first.StructuralFingerprint, second.StructuralFingerprint);
        Assert.Equal(2, second.Revision);
        Assert.Equal(2, second.StableObservations);
    }

    [Fact]
    public void Manifest_RecognizesPartialAndPresentCapabilityEvidence()
    {
        var partial = Build("n_root", "j_kosi", "iv_nitoukin_l");
        var full = Build(
            "n_root", "j_kosi", "j_sebo_a", "j_ude_a_l", "j_ude_a_r", "j_asi_a_l", "j_asi_a_r",
            "iv_nitoukin_l", "iv_nitoukin_r", "iv_hito_c_l", "iv_naka_c_r", "iv_asi_oya_a_l", "iv_asi_oya_b_l", "iv_asi_hito_a_r", "iv_asi_hito_b_r", "iv_omanko", "iv_kuritto", "iv_inshin_l");

        Assert.Equal(SkeletonCapabilityState.Partial, BoneData.EvaluateCapabilityManifest(partial, new[] { partial.Length }, 1, 1, true).GetState(SkeletonCapability.IVCS1));
        Assert.Equal(SkeletonCapabilityState.Present, BoneData.EvaluateCapabilityManifest(full, new[] { full.Length }, 1, 1, true).GetState(SkeletonCapability.IVCS1));
    }

    [Theory]
    [InlineData("j_sebo_b")]
    [InlineData("j_sebo_c")]
    public void Manifest_AcceptsObservedIvcs2ParentVariantsAsAdvisory(string chestParent)
    {
        var bones = new[]
        {
            new ObservedSkeletonBone(0, 0, "n_root", -1),
            new ObservedSkeletonBone(0, 1, chestParent, 0),
            new ObservedSkeletonBone(0, 2, "iv_kyokin_phys_l", 1),
            new ObservedSkeletonBone(0, 3, "iv_fukubu_phys", 0),
            new ObservedSkeletonBone(0, 4, "iv_daitai_phys_l", 0),
        };

        var manifest = BoneData.EvaluateCapabilityManifest(bones, new[] { bones.Length }, 1, 1, true);

        Assert.Equal(SkeletonCapabilityState.Present, manifest.GetState(SkeletonCapability.IVCS2));
        Assert.DoesNotContain(manifest.Warnings, warning => warning.Contains("iv_kyokin_phys_l", StringComparison.Ordinal));
    }

    [Fact]
    public void Manifest_ReportsUnknownCustomBonesWithoutGrantingTrust()
    {
        var bones = Build("n_root", "j_kosi", "future_custom_control");
        var manifest = BoneData.EvaluateCapabilityManifest(bones, new[] { bones.Length }, 1, 1, true);

        Assert.Equal(new[] { "future_custom_control" }, manifest.UnknownCustomBoneNames);
        Assert.False(BoneData.HasAutomationTrust("future_custom_control", BoneAutomationTrust.TemplateSafe));
    }

    [Fact]
    public void Manifest_ComposesYasNflbAndSkelomaeEvidenceWithFamilyCounts()
    {
        var bones = Build(
            "n_root", "j_kosi", "j_sebo_a", "j_ude_a_l", "j_ude_a_r", "j_asi_a_l", "j_asi_a_r",
            "iv_nitoukin_l", "iv_nitoukin_r", "iv_hito_c_l", "iv_naka_c_r", "iv_asi_oya_a_l", "iv_asi_oya_b_l", "iv_asi_hito_a_r", "iv_asi_hito_b_r", "iv_omanko", "iv_kuritto", "iv_inshin_l",
            "iv_kyokin_phys_l", "iv_fukubu_phys", "iv_daitai_phys_l",
            "ya_fukubu_phys", "ya_daitai_phys_l", "ya_daitai_phys_r", "ya_shiri_phys_l", "ya_shiri_phys_r",
            "nf_shrt_a", "nf_shrt_b", "nf_glv_a", "nf_glv_b", "nf_handprop_a_l", "nf_handprop_b_l", "nf_bulge_a", "nf_nipple_l",
            "belly_sebo_a", "belly_kosi", "forebreas_l", "gear_control_ex");

        var manifest = BoneData.EvaluateCapabilityManifest(bones, new[] { bones.Length }, 1, 1, true);

        Assert.Equal(SkeletonCapabilityState.Present, manifest.GetState(SkeletonCapability.YAS));
        Assert.Equal(SkeletonCapabilityState.Present, manifest.GetState(SkeletonCapability.NFLB));
        Assert.Equal(SkeletonCapabilityState.Present, manifest.GetState(SkeletonCapability.Skelomae));
        // Both short and glove controls are intentionally classified as clothing rigs.
        Assert.Equal(4, manifest.FamilyCounts["nflb.clothing"]);
        Assert.Equal(2, manifest.FamilyCounts["nflb.props"]);
        Assert.Equal(2, manifest.FamilyCounts["nflb.body"]);
        Assert.Equal(0, manifest.FamilyCounts.GetValueOrDefault("nflb.unclassified"));
        Assert.Equal(1, manifest.FamilyCounts["conditional-ex"]);
    }

    [Fact]
    public void Manifest_DoesNotInventSkelomaeFromGenericThigh()
    {
        var bones = Build("n_root", "j_kosi", "thigh_l");
        var manifest = BoneData.EvaluateCapabilityManifest(bones, new[] { bones.Length }, 1, 1, true);

        Assert.Equal(SkeletonCapabilityState.Absent, manifest.GetState(SkeletonCapability.Skelomae));
    }

    [Fact]
    public void TopologyValidator_AllowsEmptyOptionalPartialsButRejectsMalformedPopulatedPartial()
    {
        IReadOnlyList<int>[] optionalEmpty = { new[] { -1, 0 }, Array.Empty<int>() };
        IReadOnlyList<int>[] malformed = { new[] { -1, 2 }, Array.Empty<int>() };

        Assert.True(SkeletonTopologyValidator.HasValidOptionalPartialTopologies(optionalEmpty));
        Assert.False(SkeletonTopologyValidator.HasValidOptionalPartialTopologies(malformed));

        var manifest = BoneData.EvaluateCapabilityManifest(Build("n_root", "j_kosi"), new[] { 2, 0 }, 1, 1, true);
        Assert.True(manifest.Topology.IsValid);
        Assert.Equal(new[] { 1 }, manifest.Topology.EmptyPartialIndices);
    }

    private static ObservedSkeletonBone[] Build(params string[] names)
        => names.Select((name, index) => new ObservedSkeletonBone(0, index, name, index == 0 ? -1 : 0)).ToArray();
}
