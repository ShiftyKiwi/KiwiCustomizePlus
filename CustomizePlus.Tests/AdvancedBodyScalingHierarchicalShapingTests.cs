using System.Numerics;
using CustomizePlus.Core.Data;
using Xunit;

namespace CustomizePlus.Tests;

public class AdvancedBodyScalingHierarchicalShapingTests
{
    [Fact]
    public void Disabled_LeavesResolvedTransformsExact()
    {
        var transforms = CreateTorsoField();
        var before = Clone(transforms);

        var diagnostics = AdvancedBodyScalingHierarchicalShapingSystem.Apply(
            transforms,
            new HashSet<string>(StringComparer.Ordinal) { "j_sebo_b" },
            Live(transforms),
            CreateManifest(),
            new AdvancedBodyScalingSettings { HierarchicalShapingEnabled = false });

        Assert.False(diagnostics.Enabled);
        Assert.Empty(diagnostics.ContributionScaleDeltas);
        AssertTransformsEqual(before, transforms);
    }

    [Fact]
    public void ProfileOverrides_InheritAndResolveBothHierarchicalFlags()
    {
        var baseline = new AdvancedBodyScalingSettings
        {
            HierarchicalShapingEnabled = false,
            HierarchicalAuthoredRelaxationEnabled = false,
        };
        var overrides = new AdvancedBodyScalingOverrides
        {
            HierarchicalShapingEnabled = true,
            HierarchicalAuthoredRelaxationEnabled = true,
        };

        var resolved = overrides.MergeOnto(baseline);

        Assert.False(baseline.HierarchicalShapingEnabled);
        Assert.False(baseline.HierarchicalAuthoredRelaxationEnabled);
        Assert.True(resolved.HierarchicalShapingEnabled);
        Assert.True(resolved.HierarchicalAuthoredRelaxationEnabled);
        Assert.True(overrides.DeepCopy().HierarchicalShapingEnabled);
        Assert.True(overrides.DeepCopy().HierarchicalAuthoredRelaxationEnabled);
    }

    [Fact]
    public void StrictMode_PreservesExplicitRowsAndUsesAutomaticTransition()
    {
        var transforms = CreateTorsoField();
        var before = Clone(transforms);
        var explicitRows = new HashSet<string>(transforms.Keys, StringComparer.Ordinal);
        explicitRows.Remove("j_sebo_c");

        var diagnostics = AdvancedBodyScalingHierarchicalShapingSystem.Apply(
            transforms,
            explicitRows,
            Live(transforms),
            CreateManifest(),
            new AdvancedBodyScalingSettings { HierarchicalShapingEnabled = true });

        Assert.Contains(diagnostics.Regions, region => region.Region == "chest -> shoulder -> upper arm" && region.Status == HierarchicalShapingRegionStatus.Applied);
        Assert.True(diagnostics.ContributionScaleDeltas.ContainsKey("j_sebo_c"));
        foreach (var bone in explicitRows)
            Assert.Equal(before[bone].Scaling, transforms[bone].Scaling);
    }

    [Fact]
    public void AuthoredRelaxation_OnlyActivatesWhenExplicitlyEnabled()
    {
        var strict = CreateTorsoField();
        var relaxed = Clone(strict);
        var explicitRows = new HashSet<string>(strict.Keys, StringComparer.Ordinal);

        var strictDiagnostics = AdvancedBodyScalingHierarchicalShapingSystem.Apply(
            strict, explicitRows, Live(strict), CreateManifest(),
            new AdvancedBodyScalingSettings { HierarchicalShapingEnabled = true, HierarchicalAuthoredRelaxationEnabled = false });
        var relaxedDiagnostics = AdvancedBodyScalingHierarchicalShapingSystem.Apply(
            relaxed, explicitRows, Live(relaxed), CreateManifest(),
            new AdvancedBodyScalingSettings { HierarchicalShapingEnabled = true, HierarchicalAuthoredRelaxationEnabled = true });

        Assert.Empty(strictDiagnostics.ContributionScaleDeltas);
        Assert.True(relaxedDiagnostics.ContributionScaleDeltas.Count > 0);
        Assert.NotEqual(strict["j_sebo_c"].Scaling, relaxed["j_sebo_c"].Scaling);
    }

    [Fact]
    public void LocksAndPins_AreHardExclusionsEvenWithRelaxation()
    {
        var transforms = CreateTorsoField();
        transforms["j_sebo_c"].LockState = BoneLockState.Locked;
        transforms["j_sako_l"].PinX = true;
        transforms["j_sako_r"].PinX = true;
        var before = Clone(transforms);

        var diagnostics = AdvancedBodyScalingHierarchicalShapingSystem.Apply(
            transforms,
            new HashSet<string>(transforms.Keys, StringComparer.Ordinal),
            Live(transforms),
            CreateManifest(),
            new AdvancedBodyScalingSettings { HierarchicalShapingEnabled = true, HierarchicalAuthoredRelaxationEnabled = true });

        Assert.DoesNotContain("j_sebo_c", diagnostics.ContributionScaleDeltas.Keys);
        Assert.DoesNotContain("j_sako_l", diagnostics.ContributionScaleDeltas.Keys);
        Assert.DoesNotContain("j_sako_r", diagnostics.ContributionScaleDeltas.Keys);
        Assert.Equal(before["j_sebo_c"].Scaling, transforms["j_sebo_c"].Scaling);
        Assert.Equal(before["j_sako_l"].Scaling, transforms["j_sako_l"].Scaling);
        Assert.Equal(before["j_sako_r"].Scaling, transforms["j_sako_r"].Scaling);
    }

    [Fact]
    public void DeliberateBilateralAsymmetry_IsNotNormalizedOrRelaxed()
    {
        var transforms = CreateTorsoField();
        transforms["j_sako_r"].Scaling = new Vector3(1.30f);
        var before = Clone(transforms);

        var diagnostics = AdvancedBodyScalingHierarchicalShapingSystem.Apply(
            transforms,
            new HashSet<string>(transforms.Keys, StringComparer.Ordinal),
            Live(transforms),
            CreateManifest(),
            new AdvancedBodyScalingSettings { HierarchicalShapingEnabled = true, HierarchicalAuthoredRelaxationEnabled = true });

        Assert.Contains(diagnostics.Regions, region => region.Region == "chest -> shoulder -> upper arm" && region.Status == HierarchicalShapingRegionStatus.PreservedAsymmetry);
        AssertTransformsEqual(before, transforms);
    }

    [Fact]
    public void SymmetricAutomaticReceivers_ReceiveIdenticalBilateralCorrections()
    {
        var transforms = CreateTorsoField();
        var before = Clone(transforms);
        var explicitRows = new HashSet<string>(transforms.Keys, StringComparer.Ordinal);
        explicitRows.Remove("j_sako_l");
        explicitRows.Remove("j_sako_r");

        var diagnostics = AdvancedBodyScalingHierarchicalShapingSystem.Apply(
            transforms, explicitRows, Live(transforms), CreateManifest(),
            new AdvancedBodyScalingSettings { HierarchicalShapingEnabled = true });

        Assert.True(diagnostics.ContributionScaleDeltas.ContainsKey("j_sako_l"));
        Assert.True(diagnostics.ContributionScaleDeltas.ContainsKey("j_sako_r"));
        Assert.Equal(transforms["j_sako_l"].Scaling, transforms["j_sako_r"].Scaling);
        Assert.NotEqual(before["j_sako_l"].Scaling, transforms["j_sako_l"].Scaling);
    }

    [Fact]
    public void UnknownAndUnsupportedInputs_AreNeverTouchedAndBlockTheirRequiredChain()
    {
        var transforms = CreateTorsoField();
        transforms["custom_manual_control"] = Transform(1.37f);
        var before = Clone(transforms);
        var live = Live(transforms);
        live.Remove("j_ude_a_r");

        var diagnostics = AdvancedBodyScalingHierarchicalShapingSystem.Apply(
            transforms,
            new HashSet<string>(StringComparer.Ordinal) { "j_sebo_b" },
            live,
            CreateManifest(),
            new AdvancedBodyScalingSettings { HierarchicalShapingEnabled = true });

        Assert.Equal(before["custom_manual_control"].Scaling, transforms["custom_manual_control"].Scaling);
        Assert.DoesNotContain("custom_manual_control", diagnostics.ContributionScaleDeltas.Keys);
        Assert.Contains(diagnostics.Regions, region => region.Region == "chest -> shoulder -> upper arm" && region.Status == HierarchicalShapingRegionStatus.Unavailable);
        AssertTransformsEqual(before, transforms);
    }

    [Fact]
    public void EveryAppliedCorrection_IsBoundedToOnePercentAndImprovesContinuity()
    {
        var transforms = CreateTorsoField();
        var before = Clone(transforms);
        var explicitRows = new HashSet<string>(transforms.Keys, StringComparer.Ordinal);
        explicitRows.Remove("j_sebo_c");

        var diagnostics = AdvancedBodyScalingHierarchicalShapingSystem.Apply(
            transforms, explicitRows, Live(transforms), CreateManifest(),
            new AdvancedBodyScalingSettings { HierarchicalShapingEnabled = true });

        Assert.NotEmpty(diagnostics.ContributionScaleDeltas);
        foreach (var bone in diagnostics.ContributionScaleDeltas.Keys)
        {
            var ratio = transforms[bone].Scaling.X / before[bone].Scaling.X;
            Assert.InRange(ratio, 1f / 1.01f, 1.01f);
        }
        Assert.All(
            diagnostics.Regions.Where(region => region.Status == HierarchicalShapingRegionStatus.Applied),
            region => Assert.True(region.ContinuityErrorAfter < region.ContinuityErrorBefore));
    }

    [Fact]
    public void FreshEquivalentInputs_AreDeterministicAndUnavailableChainsRefuse()
    {
        var first = CreateTorsoField();
        var second = Clone(first);
        var explicitRows = new HashSet<string>(first.Keys, StringComparer.Ordinal);
        explicitRows.Remove("j_sebo_c");

        var firstDiagnostics = AdvancedBodyScalingHierarchicalShapingSystem.Apply(
            first, explicitRows, Live(first), CreateManifest(), new AdvancedBodyScalingSettings { HierarchicalShapingEnabled = true });
        var secondDiagnostics = AdvancedBodyScalingHierarchicalShapingSystem.Apply(
            second, explicitRows, Live(second), CreateManifest(), new AdvancedBodyScalingSettings { HierarchicalShapingEnabled = true });

        AssertTransformsEqual(first, second);
        Assert.Equal(firstDiagnostics.ContributionScaleDeltas, secondDiagnostics.ContributionScaleDeltas);
        Assert.Contains(firstDiagnostics.Regions, region => region.Region == "ribs -> abdomen -> waist" && region.Status == HierarchicalShapingRegionStatus.Unavailable);
        Assert.Contains(firstDiagnostics.Regions, region => region.Region == "pelvis -> thigh -> knee -> calf" && region.Status == HierarchicalShapingRegionStatus.Unavailable);
    }

    [Fact]
    public void FullBodyFixture_AdmitsAllValidatedFamiliesAndKeepsActorsIsolated()
    {
        var active = CreateFullBodyField();
        var untouchedActor = Clone(active);
        var explicitRows = new HashSet<string>(StringComparer.Ordinal)
        {
            "j_sebo_b", "j_kosi", "j_asi_c_l", "j_asi_c_r",
        };

        var diagnostics = AdvancedBodyScalingHierarchicalShapingSystem.Apply(
            active, explicitRows, Live(active), CreateManifest(),
            new AdvancedBodyScalingSettings { HierarchicalShapingEnabled = true });

        Assert.Equal(3, diagnostics.AppliedRegionCount);
        Assert.Contains("j_sebo_c", diagnostics.ContributionScaleDeltas.Keys);
        Assert.Contains("j_sebo_a", diagnostics.ContributionScaleDeltas.Keys);
        Assert.True(diagnostics.ContributionScaleDeltas.Keys.Any(static bone => bone.StartsWith("j_asi_", StringComparison.Ordinal)));
        AssertTransformsEqual(CreateFullBodyField(), untouchedActor);
    }

    [Fact]
    public void TogglingOff_UsesFreshResolvedInputWithoutDerivedCarryover()
    {
        var baseline = CreateTorsoField();
        var enabled = Clone(baseline);
        var disabled = Clone(baseline);
        var explicitRows = new HashSet<string>(baseline.Keys, StringComparer.Ordinal);
        explicitRows.Remove("j_sebo_c");

        var enabledDiagnostics = AdvancedBodyScalingHierarchicalShapingSystem.Apply(
            enabled, explicitRows, Live(enabled), CreateManifest(), new AdvancedBodyScalingSettings { HierarchicalShapingEnabled = true });
        var disabledDiagnostics = AdvancedBodyScalingHierarchicalShapingSystem.Apply(
            disabled, explicitRows, Live(disabled), CreateManifest(), new AdvancedBodyScalingSettings { HierarchicalShapingEnabled = false });

        Assert.NotEmpty(enabledDiagnostics.ContributionScaleDeltas);
        Assert.Empty(disabledDiagnostics.ContributionScaleDeltas);
        AssertTransformsEqual(baseline, disabled);
    }

    [Fact]
    public void CachedReplay_MatchesFreshSolveAndFailsAtomicallyWhenAnyGateChanges()
    {
        var baseline = CreateFullBodyField();
        var solved = Clone(baseline);
        var explicitRows = new HashSet<string>(solved.Keys, StringComparer.Ordinal);
        var settings = new AdvancedBodyScalingSettings
        {
            HierarchicalShapingEnabled = true,
            HierarchicalAuthoredRelaxationEnabled = true,
        };
        var diagnostics = AdvancedBodyScalingHierarchicalShapingSystem.Apply(
            solved, explicitRows, Live(solved), CreateManifest(), settings);

        Assert.True(diagnostics.LogScaleCorrections.Count >= 2);

        var replay = Clone(baseline);
        var replayed = AdvancedBodyScalingHierarchicalShapingSystem.TryApplyCached(
            replay, explicitRows, Live(replay), CreateManifest(), settings, diagnostics, out var replayDiagnostics);

        Assert.True(replayed);
        Assert.True(replayDiagnostics.CacheHit);
        AssertScalesApproximatelyEqual(solved, replay);

        var blockedReplay = Clone(baseline);
        var blockedBone = diagnostics.LogScaleCorrections.Keys.Skip(1).First();
        blockedReplay[blockedBone].PinY = true;
        var beforeBlockedReplay = Clone(blockedReplay);
        var replayBlocked = AdvancedBodyScalingHierarchicalShapingSystem.TryApplyCached(
            blockedReplay, explicitRows, Live(blockedReplay), CreateManifest(), settings, diagnostics, out _);

        Assert.False(replayBlocked);
        AssertTransformsEqual(beforeBlockedReplay, blockedReplay);
    }

    private static Dictionary<string, BoneTransform> CreateTorsoField()
        => new(StringComparer.Ordinal)
        {
            ["j_sebo_b"] = Transform(1.60f),
            ["j_sebo_c"] = Transform(1.08f),
            ["j_sako_l"] = Transform(1.00f),
            ["j_sako_r"] = Transform(1.00f),
            ["n_hkata_l"] = Transform(1.00f),
            ["n_hkata_r"] = Transform(1.00f),
            ["j_ude_a_l"] = Transform(1.00f),
            ["j_ude_a_r"] = Transform(1.00f),
        };

    private static Dictionary<string, BoneTransform> CreateFullBodyField()
        => new(StringComparer.Ordinal)
        {
            ["j_sebo_b"] = Transform(1.60f),
            ["j_sebo_c"] = Transform(1.08f),
            ["j_sako_l"] = Transform(1.00f),
            ["j_sako_r"] = Transform(1.00f),
            ["n_hkata_l"] = Transform(1.00f),
            ["n_hkata_r"] = Transform(1.00f),
            ["j_ude_a_l"] = Transform(1.00f),
            ["j_ude_a_r"] = Transform(1.00f),
            ["j_sebo_a"] = Transform(1.00f),
            ["j_kosi"] = Transform(0.90f),
            ["j_asi_a_l"] = Transform(1.00f),
            ["j_asi_a_r"] = Transform(1.00f),
            ["j_asi_b_l"] = Transform(1.00f),
            ["j_asi_b_r"] = Transform(1.00f),
            ["j_asi_c_l"] = Transform(1.30f),
            ["j_asi_c_r"] = Transform(1.30f),
        };

    private static BoneTransform Transform(float scale)
        => new() { Scaling = new Vector3(scale) };

    private static HashSet<string> Live(IReadOnlyDictionary<string, BoneTransform> transforms)
        => new(transforms.Keys, StringComparer.Ordinal);

    private static Dictionary<string, BoneTransform> Clone(IReadOnlyDictionary<string, BoneTransform> source)
        => source.ToDictionary(pair => pair.Key, pair => new BoneTransform(pair.Value), StringComparer.Ordinal);

    private static void AssertTransformsEqual(IReadOnlyDictionary<string, BoneTransform> expected, IReadOnlyDictionary<string, BoneTransform> actual)
    {
        Assert.Equal(expected.Keys.OrderBy(static key => key), actual.Keys.OrderBy(static key => key));
        foreach (var (bone, transform) in expected)
            Assert.Equal(transform.Scaling, actual[bone].Scaling);
    }

    private static void AssertScalesApproximatelyEqual(IReadOnlyDictionary<string, BoneTransform> expected, IReadOnlyDictionary<string, BoneTransform> actual)
    {
        Assert.Equal(expected.Keys.OrderBy(static key => key), actual.Keys.OrderBy(static key => key));
        foreach (var (bone, transform) in expected)
        {
            Assert.InRange(actual[bone].Scaling.X, transform.Scaling.X - 0.000001f, transform.Scaling.X + 0.000001f);
            Assert.InRange(actual[bone].Scaling.Y, transform.Scaling.Y - 0.000001f, transform.Scaling.Y + 0.000001f);
            Assert.InRange(actual[bone].Scaling.Z, transform.Scaling.Z - 0.000001f, transform.Scaling.Z + 0.000001f);
        }
    }

    private static SkeletonCapabilityManifest CreateManifest()
        => new(
            SkeletonCapability.VanillaCore,
            0,
            1,
            "test",
            2,
            true,
            new SkeletonTopologySummary(1, 1, 1, 0, 0, new[] { 1 }, Array.Empty<int>(), true),
            new Dictionary<SkeletonCapability, SkeletonCapabilityEvidence>
            {
                [SkeletonCapability.VanillaCore] = new(SkeletonCapabilityState.Present, Array.Empty<string>(), Array.Empty<string>()),
            },
            BoneAnimationCompatibility.None,
            new Dictionary<string, int>(),
            Array.Empty<string>(),
            Array.Empty<string>());
}
