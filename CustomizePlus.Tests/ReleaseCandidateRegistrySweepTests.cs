using System.Numerics;
using CustomizePlus.Core.Data;
using Xunit;
using Xunit.Abstractions;

namespace CustomizePlus.Tests;

/// <summary>
/// Data-driven release-candidate checks. New registry rows and static receiver entries
/// are included automatically through the production registry and solver definitions.
/// </summary>
public sealed class ReleaseCandidateRegistrySweepTests
{
    private readonly ITestOutputHelper _output;

    public ReleaseCandidateRegistrySweepTests(ITestOutputHelper output)
        => _output = output;

    [Fact]
    public void EveryRegisteredBone_HasCanonicalFiniteAndSaneCuratedMetadata()
    {
        var names = BoneData.GetBoneCodenames().OrderBy(static name => name, StringComparer.Ordinal).ToArray();
        Assert.NotEmpty(names);
        Assert.Equal(names.Length, names.Distinct(StringComparer.Ordinal).Count());

        foreach (var name in names)
        {
            Assert.False(string.IsNullOrWhiteSpace(name));
            Assert.Equal(name, BoneData.GetCanonicalBoneName(name));

            var metadata = BoneData.GetMetadata(name);
            Assert.True(Enum.IsDefined(metadata.Origin));
            Assert.True(Enum.IsDefined(metadata.Role));
            Assert.NotEqual((BoneAvailability)0, metadata.Availability);
            Assert.True(Enum.IsDefined(metadata.ScalingInheritance.Mode));

            if (metadata.ScalingInheritance.SourceBone != null)
            {
                Assert.NotEqual(name, metadata.ScalingInheritance.SourceBone);
                Assert.Contains(metadata.ScalingInheritance.SourceBone, names);
                Assert.NotEqual(BoneScalingInheritanceMode.None, metadata.ScalingInheritance.Mode);
            }
            else
            {
                Assert.Equal(BoneScalingInheritanceMode.None, metadata.ScalingInheritance.Mode);
            }

            if (metadata.ExpectedParent != null)
                Assert.NotEqual(name, metadata.ExpectedParent);

            if (metadata.Origin == BoneOrigin.UnknownCustom)
                Assert.Equal(BoneAutomationTrust.ManualOnly, metadata.Trust);
        }

        Assert.Empty(BoneData.ValidateRegistry());
        WriteOriginCoverage(names);
    }

    [Fact]
    public void EveryAliasAndTrustedMirror_IsBidirectionalAndUnambiguous()
    {
        var names = BoneData.GetBoneCodenames().ToHashSet(StringComparer.Ordinal);
        foreach (var (alias, canonical) in CuratedBoneRegistry.KnownAliases)
        {
            Assert.NotEqual(alias, canonical);
            Assert.Contains(canonical, names);
            Assert.Equal(canonical, BoneData.GetCanonicalBoneName(alias));
            Assert.Equal(canonical, BoneData.GetCanonicalBoneName(canonical));
        }

        var mirrorPairs = 0;
        foreach (var name in names)
        {
            var mirror = BoneData.GetBoneMirror(name);
            if (mirror == null)
                continue;

            Assert.NotEqual(name, mirror);
            Assert.Contains(mirror, names);
            Assert.Equal(name, BoneData.GetBoneMirror(mirror));
            if (BoneData.GetAutomationMirror(name) != null)
            {
                Assert.Equal(mirror, BoneData.GetAutomationMirror(name));
                Assert.Equal(name, BoneData.GetAutomationMirror(mirror));
            }

            if (string.CompareOrdinal(name, mirror) < 0)
                mirrorPairs++;
        }

        _output.WriteLine($"Aliases={CuratedBoneRegistry.KnownAliases.Count}; trusted mirror pairs={mirrorPairs}.");
    }

    [Fact]
    public void ConfiguredAutomaticReceivers_ExerciseOnlyTrustedAndCapabilitySupportedBones()
    {
        var receivers = AdvancedBodyScalingDeformationSolver.GetConfiguredAutomaticReceiversForValidation();
        Assert.NotEmpty(receivers);

        var fullManifest = CreateManifest(
            SkeletonCapability.VanillaCore | SkeletonCapability.IVCS1 | SkeletonCapability.IVCS2 |
            SkeletonCapability.YAS | SkeletonCapability.NFLB | SkeletonCapability.Skelomae);
        var allNames = BoneData.GetBoneCodenames().ToHashSet(StringComparer.Ordinal);
        var expected = receivers
            .Where(name => IsAutomaticMetadata(BoneData.GetMetadata(name)))
            .OrderBy(static name => name, StringComparer.Ordinal)
            .ToArray();

        var field = CreateDrivenField(receivers, allNames);
        var liveNames = field.Keys.ToHashSet(StringComparer.Ordinal);
        var diagnostics = AdvancedBodyScalingDeformationSolver.Apply(
            field,
            new HashSet<string>(StringComparer.Ordinal),
            liveNames,
            fullManifest,
            null,
            new AdvancedBodyScalingSettings());
        var applied = diagnostics.ContributionSources
            .Where(static pair => (pair.Value & DeformationContributionSource.AutomaticSupport) != 0)
            .Select(static pair => pair.Key)
            .OrderBy(static name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(expected, applied);
        foreach (var name in applied)
        {
            Assert.True(IsAutomaticMetadata(BoneData.GetMetadata(name)));
            Assert.True(TransformSafety.IsFinite(field[name].Scaling));
            Assert.InRange(field[name].Scaling.X, 0.70f, 1.45f);
            Assert.InRange(field[name].Scaling.Y, 0.70f, 1.45f);
            Assert.InRange(field[name].Scaling.Z, 0.70f, 1.45f);
        }

        foreach (var capability in new[]
                 {
                     SkeletonCapability.IVCS1, SkeletonCapability.IVCS2, SkeletonCapability.YAS,
                     SkeletonCapability.NFLB, SkeletonCapability.Skelomae,
                 })
        {
            var origin = OriginFor(capability);
            var capabilityReceivers = expected.Where(name => BoneData.GetMetadata(name).Origin == origin).ToArray();
            if (capabilityReceivers.Length == 0)
                continue;

            var missingCapability = CreateManifest(fullManifest.Capabilities & ~capability);
            var capabilityField = CreateDrivenField(receivers, allNames);
            var capabilityDiagnostics = AdvancedBodyScalingDeformationSolver.Apply(
                capabilityField,
                new HashSet<string>(StringComparer.Ordinal),
                capabilityField.Keys.ToHashSet(StringComparer.Ordinal),
                missingCapability,
                null,
                new AdvancedBodyScalingSettings());
            var capabilityApplied = capabilityDiagnostics.ContributionSources
                .Where(static pair => (pair.Value & DeformationContributionSource.AutomaticSupport) != 0)
                .Select(static pair => pair.Key)
                .ToHashSet(StringComparer.Ordinal);

            Assert.DoesNotContain(capabilityReceivers, capabilityApplied.Contains);
        }

        _output.WriteLine($"Configured automatic receivers exercised={applied.Length}/{expected.Length}; excluded configured receivers={receivers.Count - expected.Length}.");
    }

    [Fact]
    public void RegisteredExcludedRoles_RemainInertInTheProductionStaticSolve()
    {
        var names = BoneData.GetBoneCodenames().ToHashSet(StringComparer.Ordinal);
        var excluded = names.Where(name => !IsAutomaticMetadata(BoneData.GetMetadata(name))).ToHashSet(StringComparer.Ordinal);
        var field = CreateDrivenField(AdvancedBodyScalingDeformationSolver.GetConfiguredAutomaticReceiversForValidation(), names);
        var before = field.ToDictionary(static pair => pair.Key, static pair => pair.Value.Scaling, StringComparer.Ordinal);

        var diagnostics = AdvancedBodyScalingDeformationSolver.Apply(
            field,
            new HashSet<string>(StringComparer.Ordinal),
            field.Keys.ToHashSet(StringComparer.Ordinal),
            CreateManifest(SkeletonCapability.VanillaCore | SkeletonCapability.IVCS1 | SkeletonCapability.IVCS2 | SkeletonCapability.YAS | SkeletonCapability.NFLB | SkeletonCapability.Skelomae),
            null,
            new AdvancedBodyScalingSettings());

        var automatic = diagnostics.ContributionSources
            .Where(static pair => (pair.Value & DeformationContributionSource.AutomaticSupport) != 0)
            .Select(static pair => pair.Key)
            .ToHashSet(StringComparer.Ordinal);
        Assert.DoesNotContain(automatic, excluded.Contains);

        foreach (var name in excluded)
            Assert.Equal(before[name], field[name].Scaling);

        _output.WriteLine($"Manual/excluded registry rows proven inert={excluded.Count}; static automatic output={automatic.Count}.");
    }

    private static Dictionary<string, BoneTransform> CreateDrivenField(IEnumerable<string> receivers, IEnumerable<string> names)
    {
        var field = names.ToDictionary(static name => name, static _ => new BoneTransform(), StringComparer.Ordinal);
        foreach (var name in new[]
                 {
                     "j_mune_l", "j_mune_r", "j_sebo_b", "n_hkata_l", "n_hkata_r", "j_sako_l", "j_sako_r",
                     "j_ude_a_l", "j_ude_a_r", "j_ude_b_l", "j_ude_b_r", "j_sebo_a", "j_kosi",
                     "j_asi_a_l", "j_asi_a_r", "j_asi_b_l", "j_asi_b_r", "j_kubi",
                 })
        {
            if (field.TryGetValue(name, out var transform))
                transform.Scaling = new Vector3(1.24f, 1.16f, 1.21f);
        }

        // Ensure future configured receivers not currently in the base table are part of the synthetic live field.
        foreach (var receiver in receivers)
            field.TryAdd(receiver, new BoneTransform());
        return field;
    }

    private static SkeletonCapabilityManifest CreateManifest(SkeletonCapability capabilities)
    {
        var evidence = Enum.GetValues<SkeletonCapability>()
            .Where(static capability => capability != SkeletonCapability.None)
            .ToDictionary(
                static capability => capability,
                capability => new SkeletonCapabilityEvidence(
                    (capabilities & capability) != 0 ? SkeletonCapabilityState.Present : SkeletonCapabilityState.Absent,
                    Array.Empty<string>(),
                    Array.Empty<string>()));
        return new SkeletonCapabilityManifest(
            capabilities,
            0,
            1,
            "release-candidate-registry-sweep",
            2,
            true,
            new SkeletonTopologySummary(1, 1, 1, 0, 0, new[] { 1 }, Array.Empty<int>(), true),
            evidence,
            BoneAnimationCompatibility.VanillaBaseline,
            new Dictionary<string, int>(),
            Array.Empty<string>(),
            Array.Empty<string>());
    }

    private static bool IsAutomaticMetadata(BoneMetadata metadata)
        => metadata.HasTrust(BoneAutomationTrust.AdvancedCorrectiveSafe)
           && metadata.Role is not (BoneFunctionalRole.ClothingRig
               or BoneFunctionalRole.PropRig
               or BoneFunctionalRole.ArticulatedAppendage
               or BoneFunctionalRole.ArticulatedBodyFeature
               or BoneFunctionalRole.Unknown);

    private static BoneOrigin OriginFor(SkeletonCapability capability)
        => capability switch
        {
            SkeletonCapability.IVCS1 => BoneOrigin.IVCS1,
            SkeletonCapability.IVCS2 => BoneOrigin.IVCS2,
            SkeletonCapability.YAS => BoneOrigin.YAS,
            SkeletonCapability.NFLB => BoneOrigin.NFLB,
            SkeletonCapability.Skelomae => BoneOrigin.Skelomae,
            _ => throw new ArgumentOutOfRangeException(nameof(capability), capability, null),
        };

    private void WriteOriginCoverage(IEnumerable<string> names)
    {
        var summary = names
            .GroupBy(static name => BoneData.GetMetadata(name).Origin)
            .OrderBy(static group => group.Key)
            .Select(group => $"{group.Key}={group.Count()}");
        _output.WriteLine($"Registered bones validated={names.Count()}/{names.Count()}; {string.Join(", ", summary)}.");
    }
}
