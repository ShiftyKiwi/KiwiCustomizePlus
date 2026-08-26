using System.Collections.Generic;
using System.Linq;
using CustomizePlus.Core.Data;
using Xunit;

namespace CustomizePlus.Tests;

public class BoneRegistryTests
{
    [Fact]
    public void Registry_HasNoStaticIntegrityIssues()
        => Assert.Empty(BoneData.ValidateRegistry());

    [Fact]
    public void Registry_UsesCanonicalClitorisAlias()
    {
        Assert.Equal("iv_kuritto", BoneData.GetCanonicalBoneName("iv_kurrito"));
        Assert.Equal(BoneOrigin.IVCS1, BoneData.GetMetadata("iv_kurrito").Origin);
    }

    [Fact]
    public void Ivcs1Catalogue_HasAllFortyNineCuratedControls()
    {
        var names = new List<string>
        {
            "iv_hito_c_l", "iv_hito_c_r", "iv_naka_c_l", "iv_naka_c_r", "iv_kusu_c_l", "iv_kusu_c_r", "iv_ko_c_l", "iv_ko_c_r",
            "iv_nitoukin_l", "iv_nitoukin_r", "iv_c_mune_l", "iv_c_mune_r", "iv_shiri_l", "iv_shiri_r",
            "iv_kougan_l", "iv_kougan_r", "iv_ochinko_a", "iv_ochinko_b", "iv_ochinko_c", "iv_ochinko_d", "iv_ochinko_e", "iv_ochinko_f", "iv_omanko", "iv_kuritto", "iv_inshin_l", "iv_inshin_r", "iv_koumon", "iv_koumon_l", "iv_koumon_r",
        };
        foreach (var side in new[] { "l", "r" })
        foreach (var toe in new[] { "oya", "hito", "naka", "kusu", "ko" })
        {
            names.Add($"iv_asi_{toe}_a_{side}");
            names.Add($"iv_asi_{toe}_b_{side}");
        }

        Assert.Equal(49, names.Count);
        Assert.All(names, name => Assert.Equal(BoneOrigin.IVCS1, BoneData.GetMetadata(name).Origin));
        Assert.Equal(BoneFunctionalRole.PhysicsControlOverride, BoneData.GetMetadata("iv_c_mune_l").Role);
        Assert.Equal("iv_asi_naka_a_l", BoneData.GetMetadata("iv_asi_naka_b_l").ExpectedParent);
    }

    [Fact]
    public void Ivcs2Catalogue_HasThirteenPhysicsControls()
    {
        var names = new[]
        {
            "iv_kyokin_phys_l", "iv_kyokin_phys_r", "iv_fukubu_phys", "iv_fukubu_phys_l", "iv_fukubu_phys_r",
            "iv_daitai_phys_l", "iv_daitai_phys_r", "iv_kintama_phys_l", "iv_kintama_phys_r",
            "iv_funyachin_phy_a", "iv_funyachin_phy_b", "iv_funyachin_phy_c", "iv_funyachin_phy_d",
        };

        Assert.Equal(13, names.Length);
        Assert.All(names, name =>
        {
            var metadata = BoneData.GetMetadata(name);
            Assert.Equal(BoneOrigin.IVCS2, metadata.Origin);
            Assert.Equal(BoneFunctionalRole.PhysicsSimulation, metadata.Role);
            Assert.False(metadata.HasTrust(BoneAutomationTrust.SemanticSafe));
        });
    }

    [Fact]
    public void YasControls_HaveCuratedScalingInheritance()
    {
        Assert.Equal(new BoneScalingInheritance("j_kosi", BoneScalingInheritanceMode.SwapXY), BoneData.GetMetadata("ya_fukubu_phys").ScalingInheritance);
        Assert.Equal(new BoneScalingInheritance("j_asi_a_l", BoneScalingInheritanceMode.SwapXY), BoneData.GetMetadata("ya_daitai_phys_l").ScalingInheritance);
        Assert.Equal(new BoneScalingInheritance("j_asi_a_r", BoneScalingInheritanceMode.SwapXY), BoneData.GetMetadata("ya_daitai_phys_r").ScalingInheritance);
    }

    [Fact]
    public void CapabilityDetection_SupportsComposedStacksWithoutTrustingUnknowns()
    {
        var names = new List<string>
        {
            "n_root", "j_kosi", "j_sebo_a",
            "iv_nitoukin_l", "iv_nitoukin_r", "iv_hito_c_l", "iv_naka_c_r", "iv_asi_oya_a_l", "iv_asi_oya_b_l", "iv_asi_hito_a_r", "iv_asi_hito_b_r", "iv_omanko", "iv_kuritto", "iv_inshin_l",
            "iv_kyokin_phys_l", "iv_fukubu_phys", "iv_daitai_phys_l",
            "ya_fukubu_phys", "ya_daitai_phys_l", "ya_shiri_phys_l",
            "nf_shrt_a", "nf_shrt_b", "nf_glv_a", "nf_glv_b", "nf_handprop_a_l", "nf_handprop_b_l", "nf_bulge_a", "nf_nipple_l",
            "belly_sebo_a", "belly_kosi", "forebreas_l",
            "future_custom_control",
        };

        var manifest = BoneData.EvaluateCapabilities(names);
        Assert.True(manifest.Has(SkeletonCapability.VanillaCore));
        Assert.True(manifest.Has(SkeletonCapability.IVCS1));
        Assert.True(manifest.Has(SkeletonCapability.IVCS2));
        Assert.True(manifest.Has(SkeletonCapability.YAS));
        Assert.True(manifest.Has(SkeletonCapability.NFLB));
        Assert.True(manifest.Has(SkeletonCapability.Skelomae));
        Assert.Equal(1, manifest.UnknownCustomBoneCount);
    }

    [Fact]
    public void NflbClothingAndProps_RemainManualOnly()
    {
        Assert.Equal(BoneFunctionalRole.ClothingRig, BoneData.GetMetadata("nf_shrt_example").Role);
        Assert.Equal(BoneFunctionalRole.PropRig, BoneData.GetMetadata("nf_handprop_a_l").Role);
        Assert.False(BoneData.HasAutomationTrust("nf_shrt_example", BoneAutomationTrust.PropagationSafe));
        Assert.False(BoneData.HasAutomationTrust("nf_handprop_a_l", BoneAutomationTrust.SemanticSafe));
    }

    [Fact]
    public void NflbDirectBodyControls_AreCuratedWhileUnlistedPrefixBonesRemainManual()
    {
        var curated = new[]
        {
            "nf_bulge_a", "nf_nipple_l", "nf_nipple_r", "nf_clitoris",
            "nf_labia_inner_l", "nf_labia_inner_r", "nf_labia_outer_l", "nf_labia_outer_r",
            "nf_iv_daitai_phys_l", "nf_iv_daitai_phys_r", "nf_iv_shiri_l", "nf_iv_shiri_r",
            "nf_iv_kintama_phys_l", "nf_iv_kintama_phys_r", "nf_iv_funyachin_phy_a", "nf_iv_funyachin_phy_b",
            "nf_iv_funyachin_phy_c", "nf_iv_funyachin_phy_d",
        };

        Assert.Equal(18, curated.Length);
        Assert.All(curated, name =>
        {
            var metadata = BoneData.GetMetadata(name);
            Assert.Equal(BoneOrigin.NFLB, metadata.Origin);
            Assert.Equal(BoneFunctionalRole.BodyExtension, metadata.Role);
            Assert.True(metadata.HasTrust(BoneAutomationTrust.AdvancedCorrectiveSafe));
        });

        var unlisted = BoneData.GetMetadata("nf_future_custom_body_control");
        Assert.Equal(BoneOrigin.NFLB, unlisted.Origin);
        Assert.Equal(BoneFunctionalRole.Unknown, unlisted.Role);
        Assert.Equal(BoneAutomationTrust.ManualOnly, unlisted.Trust);
    }
}
