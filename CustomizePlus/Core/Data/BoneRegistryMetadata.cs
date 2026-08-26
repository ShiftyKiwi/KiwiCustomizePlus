// Copyright (c) Customize+.
// Licensed under the MIT license.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace CustomizePlus.Core.Data;

public enum BoneOrigin
{
    Vanilla,
    IVCS1,
    IVCS2,
    YAS,
    NFLB,
    Skelomae,
    UnknownCustom,
}

public enum BoneFunctionalRole
{
    StructuralAnatomical,
    BodyExtension,
    PhysicsSimulation,
    PhysicsControlOverride,
    ClothingRig,
    PropRig,
    ArticulatedAppendage,
    ArticulatedBodyFeature,
    FacePrimary,
    FaceHelper,
    GearAttachment,
    ConditionalExtra,
    AnimationHelper,
    Unknown,
}

[Flags]
public enum BoneAvailability
{
    Gameplay = 1 << 0,
    GPoseOrCutsceneOnly = 1 << 1,
    RaceConditional = 1 << 2,
    GearConditional = 1 << 3,
    ModelConditional = 1 << 4,
    SkeletonCapabilityConditional = 1 << 5,
    ConditionalExtra = 1 << 6,
}

[Flags]
public enum BoneAutomationTrust
{
    ManualOnly = 0,
    MirrorSafe = 1 << 0,
    PropagationSafe = 1 << 1,
    TemplateSafe = 1 << 2,
    SemanticSafe = 1 << 3,
    AdvancedCorrectiveSafe = 1 << 4,
}

[Flags]
public enum BoneAnimationCompatibility
{
    None = 0,
    VanillaBaseline = 1 << 0,
    IVCS1Portable = 1 << 1,
    NFLBExtended = 1 << 2,
    UnknownCustom = 1 << 3,
}

public enum BoneScalingInheritanceMode
{
    None,
    Identity,
    SwapXY,
}

public readonly record struct BoneScalingInheritance(string? SourceBone, BoneScalingInheritanceMode Mode)
{
    public static BoneScalingInheritance None => new(null, BoneScalingInheritanceMode.None);
}

public readonly record struct BoneMetadata(
    BoneOrigin Origin,
    BoneFunctionalRole Role,
    BoneAvailability Availability,
    BoneAutomationTrust Trust,
    BoneAnimationCompatibility AnimationCompatibility,
    BoneScalingInheritance ScalingInheritance,
    string? ExpectedParent = null)
{
    public bool HasTrust(BoneAutomationTrust trust) => (Trust & trust) == trust;

    public static BoneMetadata Unknown => new(
        BoneOrigin.UnknownCustom,
        BoneFunctionalRole.Unknown,
        BoneAvailability.ModelConditional,
        BoneAutomationTrust.ManualOnly,
        BoneAnimationCompatibility.UnknownCustom,
        BoneScalingInheritance.None);
}

[Flags]
public enum SkeletonCapability
{
    None = 0,
    VanillaCore = 1 << 0,
    IVCS1 = 1 << 1,
    IVCS2 = 1 << 2,
    YAS = 1 << 3,
    NFLB = 1 << 4,
    Skelomae = 1 << 5,
}

public enum SkeletonCapabilityState
{
    Absent,
    Partial,
    Present,
    Ambiguous,
}

/// <summary>
/// A managed-only description of one published bone. It deliberately contains no native pointers.
/// </summary>
public readonly record struct ObservedSkeletonBone(int PartialIndex, int BoneIndex, string Name, int ParentBoneIndex);

public sealed record SkeletonTopologySummary(
    int TotalBoneCount,
    int UniqueBoneNameCount,
    int RootCount,
    int EdgeCount,
    int MaxDepth,
    IReadOnlyList<int> PartialBoneCounts,
    IReadOnlyList<int> EmptyPartialIndices,
    bool IsValid);

public sealed record SkeletonCapabilityEvidence(
    SkeletonCapabilityState State,
    IReadOnlyList<string> Evidence,
    IReadOnlyList<string> MissingExpected);

/// <summary>
/// Immutable, diagnostic-only capability snapshot derived from a published live skeleton.
/// It never changes transform, trust, parentage, or profile resolution behavior.
/// </summary>
public sealed record SkeletonCapabilityManifest
{
    public static readonly SkeletonCapabilityManifest Unavailable = new(
        SkeletonCapability.None,
        0,
        0,
        string.Empty,
        0,
        false,
        new SkeletonTopologySummary(0, 0, 0, 0, 0, Array.Empty<int>(), Array.Empty<int>(), false),
        new ReadOnlyDictionary<SkeletonCapability, SkeletonCapabilityEvidence>(new Dictionary<SkeletonCapability, SkeletonCapabilityEvidence>()),
        BoneAnimationCompatibility.None,
        new ReadOnlyDictionary<string, int>(new Dictionary<string, int>(StringComparer.Ordinal)),
        Array.Empty<string>(),
        Array.Empty<string>());

    public SkeletonCapabilityManifest(SkeletonCapability capabilities, int unknownCustomBoneCount)
        : this(
            capabilities,
            unknownCustomBoneCount,
            0,
            string.Empty,
            0,
            false,
            new SkeletonTopologySummary(0, 0, 0, 0, 0, Array.Empty<int>(), Array.Empty<int>(), false),
            new ReadOnlyDictionary<SkeletonCapability, SkeletonCapabilityEvidence>(new Dictionary<SkeletonCapability, SkeletonCapabilityEvidence>()),
            BoneAnimationCompatibility.None,
            new ReadOnlyDictionary<string, int>(new Dictionary<string, int>(StringComparer.Ordinal)),
            Array.Empty<string>(),
            Array.Empty<string>())
    { }

    public SkeletonCapabilityManifest(
        SkeletonCapability capabilities,
        int unknownCustomBoneCount,
        long revision,
        string structuralFingerprint,
        int stableObservations,
        bool bindingCurrent,
        SkeletonTopologySummary topology,
        IReadOnlyDictionary<SkeletonCapability, SkeletonCapabilityEvidence> capabilityEvidence,
        BoneAnimationCompatibility animationCompatibility,
        IReadOnlyDictionary<string, int> familyCounts,
        IReadOnlyList<string> unknownCustomBoneNames,
        IReadOnlyList<string> warnings,
        IReadOnlyList<string>? observedBoneNames = null)
    {
        Capabilities = capabilities;
        UnknownCustomBoneCount = unknownCustomBoneCount;
        Revision = revision;
        StructuralFingerprint = structuralFingerprint;
        StableObservations = stableObservations;
        BindingCurrent = bindingCurrent;
        Topology = topology;
        CapabilityEvidence = new ReadOnlyDictionary<SkeletonCapability, SkeletonCapabilityEvidence>(capabilityEvidence.ToDictionary(static pair => pair.Key, static pair => pair.Value));
        AnimationCompatibility = animationCompatibility;
        FamilyCounts = new ReadOnlyDictionary<string, int>(familyCounts.ToDictionary(static pair => pair.Key, static pair => pair.Value, StringComparer.Ordinal));
        UnknownCustomBoneNames = unknownCustomBoneNames.OrderBy(static name => name, StringComparer.Ordinal).ToArray();
        Warnings = warnings.OrderBy(static warning => warning, StringComparer.Ordinal).ToArray();
        ObservedBoneNames = (observedBoneNames ?? Array.Empty<string>()).OrderBy(static name => name, StringComparer.Ordinal).ToArray();
    }

    public SkeletonCapability Capabilities { get; }
    public int UnknownCustomBoneCount { get; }
    public long Revision { get; }
    public string StructuralFingerprint { get; }
    public int StableObservations { get; }
    public bool BindingCurrent { get; }
    public SkeletonTopologySummary Topology { get; }
    public IReadOnlyDictionary<SkeletonCapability, SkeletonCapabilityEvidence> CapabilityEvidence { get; }
    public BoneAnimationCompatibility AnimationCompatibility { get; }
    public IReadOnlyDictionary<string, int> FamilyCounts { get; }
    public IReadOnlyList<string> UnknownCustomBoneNames { get; }
    public IReadOnlyList<string> Warnings { get; }
    /// <summary>Exact immutable names from the last validated live topology publication.</summary>
    public IReadOnlyList<string> ObservedBoneNames { get; }

    public bool ContainsObservedBone(string boneName)
        => ObservedBoneNames.Contains(boneName, StringComparer.Ordinal);

    public bool Has(SkeletonCapability capability) => (Capabilities & capability) == capability;

    public SkeletonCapabilityState GetState(SkeletonCapability capability)
        => CapabilityEvidence.TryGetValue(capability, out var evidence) ? evidence.State : SkeletonCapabilityState.Absent;

    public SkeletonCapabilityManifest WithBindingState(bool bindingCurrent)
        => bindingCurrent == BindingCurrent
            ? this
            : new SkeletonCapabilityManifest(Capabilities, UnknownCustomBoneCount, Revision, StructuralFingerprint, StableObservations, bindingCurrent,
                Topology, CapabilityEvidence, AnimationCompatibility, FamilyCounts, UnknownCustomBoneNames, Warnings, ObservedBoneNames);

    public string ToDebugJson()
        => JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
}

/// <summary>
/// Builds a deterministic diagnostic manifest from the validated, managed armature snapshot.
/// This evaluator has no native reads and no authority over transform behavior.
/// </summary>
public static class SkeletonCapabilityManifestEvaluator
{
    private static readonly SkeletonCapability[] CapabilityOrder =
    {
        SkeletonCapability.VanillaCore,
        SkeletonCapability.IVCS1,
        SkeletonCapability.IVCS2,
        SkeletonCapability.YAS,
        SkeletonCapability.NFLB,
        SkeletonCapability.Skelomae,
    };

    private static readonly string[] VanillaCoreMarkers =
    {
        "n_root", "j_kosi", "j_sebo_a", "j_ude_a_l", "j_ude_a_r", "j_asi_a_l", "j_asi_a_r",
    };

    private static readonly string[] YasMarkers =
    {
        "ya_fukubu_phys", "ya_daitai_phys_l", "ya_daitai_phys_r", "ya_shiri_phys_l", "ya_shiri_phys_r",
    };

    private static readonly string[] SkelomaeMarkers =
    {
        "belly_sebo_a", "belly_kosi", "forebreas_l", "forebreas_r", "butt_left", "butt_right",
    };

    public static SkeletonCapabilityManifest Evaluate(
        IEnumerable<ObservedSkeletonBone> observedBones,
        IReadOnlyList<int> partialBoneCounts,
        long revision,
        int stableObservations,
        bool bindingCurrent,
        Func<string, BoneMetadata> resolveMetadata,
        Func<string, string> canonicalize)
    {
        var observed = observedBones
            .Where(static bone => !string.IsNullOrWhiteSpace(bone.Name) && bone.PartialIndex >= 0 && bone.BoneIndex >= 0)
            .Select(bone => bone with { Name = canonicalize(bone.Name) })
            .OrderBy(static bone => bone.PartialIndex)
            .ThenBy(static bone => bone.BoneIndex)
            .ToArray();
        var names = new HashSet<string>(observed.Select(static bone => bone.Name), StringComparer.Ordinal);
        var byPartial = observed.GroupBy(static bone => bone.PartialIndex)
            .ToDictionary(static group => group.Key, static group => group.ToDictionary(static bone => bone.BoneIndex));
        var warnings = new List<string>();
        var topology = BuildTopology(observed, partialBoneCounts, byPartial, warnings);
        var parentNames = BuildParentNames(observed, byPartial);

        var metadataByName = observed
            .Select(static bone => bone.Name)
            .Distinct(StringComparer.Ordinal)
            .ToDictionary(static name => name, resolveMetadata, StringComparer.Ordinal);
        var animation = metadataByName.Values.Aggregate(BoneAnimationCompatibility.None, static (current, metadata) => current | metadata.AnimationCompatibility);
        var unknown = metadataByName.Where(static pair => pair.Value.Origin == BoneOrigin.UnknownCustom).Select(static pair => pair.Key).OrderBy(static name => name, StringComparer.Ordinal).ToArray();
        var familyCounts = BuildFamilyCounts(observed, metadataByName);
        AddParentWarnings(observed, metadataByName, parentNames, canonicalize, warnings);

        var evidence = new Dictionary<SkeletonCapability, SkeletonCapabilityEvidence>();
        evidence[SkeletonCapability.VanillaCore] = BuildVanillaEvidence(names);
        evidence[SkeletonCapability.IVCS1] = BuildIvcs1Evidence(names, metadataByName);
        evidence[SkeletonCapability.IVCS2] = BuildIvcs2Evidence(names);
        evidence[SkeletonCapability.YAS] = BuildExactEvidence(names, YasMarkers, 3, "YAS");
        evidence[SkeletonCapability.NFLB] = BuildNflbEvidence(observed, metadataByName);
        evidence[SkeletonCapability.Skelomae] = BuildSkelomaeEvidence(names, evidence[SkeletonCapability.IVCS1].State);

        foreach (var pair in evidence.Where(static pair => pair.Value.State == SkeletonCapabilityState.Partial))
            warnings.Add($"{pair.Key} capability evidence is partial.");
        if (unknown.Length > 0)
            warnings.Add($"Observed {unknown.Length} unknown/custom control(s); they remain manual-only.");
        var nonEmptyPartialCount = partialBoneCounts.Count(static count => count > 0);
        if (topology.RootCount != nonEmptyPartialCount)
            warnings.Add($"Observed {topology.RootCount} roots across {nonEmptyPartialCount} populated partial(s).");

        var capabilities = CapabilityOrder.Where(capability => evidence[capability].State != SkeletonCapabilityState.Absent)
            .Aggregate(SkeletonCapability.None, static (current, capability) => current | capability);
        if (!topology.IsValid)
            warnings.Add("Published skeleton topology was incomplete; capability evidence is diagnostic only.");

        return new SkeletonCapabilityManifest(
            capabilities,
            unknown.Length,
            revision,
            ComputeStructuralFingerprint(observed, partialBoneCounts, byPartial),
            stableObservations,
            bindingCurrent,
            topology,
            evidence,
            animation,
            familyCounts,
            unknown,
            warnings,
            names.OrderBy(static name => name, StringComparer.Ordinal).ToArray());
    }

    public static string ComputeStructuralFingerprint(IEnumerable<ObservedSkeletonBone> observedBones, IReadOnlyList<int> partialBoneCounts)
    {
        var observed = observedBones.Where(static bone => !bone.Name.EndsWith("_ex", StringComparison.OrdinalIgnoreCase))
            .OrderBy(static bone => bone.PartialIndex)
            .ThenBy(static bone => bone.BoneIndex)
            .ToArray();
        var byPartial = observed.GroupBy(static bone => bone.PartialIndex)
            .ToDictionary(static group => group.Key, static group => group.ToDictionary(static bone => bone.BoneIndex));
        return ComputeStructuralFingerprint(observed, partialBoneCounts, byPartial);
    }

    private static SkeletonTopologySummary BuildTopology(
        IReadOnlyList<ObservedSkeletonBone> observed,
        IReadOnlyList<int> partialBoneCounts,
        IReadOnlyDictionary<int, Dictionary<int, ObservedSkeletonBone>> byPartial,
        ICollection<string> warnings)
    {
        var roots = 0;
        var edges = 0;
        var maxDepth = 0;
        var valid = true;
        foreach (var bone in observed)
        {
            if (!byPartial.TryGetValue(bone.PartialIndex, out var partial))
            {
                valid = false;
                continue;
            }

            if (bone.ParentBoneIndex < 0)
            {
                roots++;
                continue;
            }

            if (!partial.ContainsKey(bone.ParentBoneIndex))
            {
                valid = false;
                warnings.Add($"Partial {bone.PartialIndex} has an unresolved parent index for {bone.Name}.");
                continue;
            }

            edges++;
        }

        foreach (var partial in byPartial.Values)
        {
            foreach (var bone in partial.Values)
            {
                var visited = new HashSet<int>();
                var depth = 0;
                var current = bone;
                while (current.ParentBoneIndex >= 0)
                {
                    if (!visited.Add(current.BoneIndex) || !partial.TryGetValue(current.ParentBoneIndex, out current))
                    {
                        valid = false;
                        break;
                    }

                    depth++;
                }

                maxDepth = Math.Max(maxDepth, depth);
            }
        }

        var emptyPartials = partialBoneCounts.Select((count, index) => (count, index))
            .Where(static pair => pair.count == 0).Select(static pair => pair.index).ToArray();
        return new SkeletonTopologySummary(observed.Count, observed.Select(static bone => bone.Name).Distinct(StringComparer.Ordinal).Count(), roots, edges, maxDepth,
            partialBoneCounts.ToArray(), emptyPartials, valid && observed.Count > 0 && roots > 0);
    }

    private static Dictionary<(int PartialIndex, int BoneIndex), string> BuildParentNames(
        IEnumerable<ObservedSkeletonBone> observed,
        IReadOnlyDictionary<int, Dictionary<int, ObservedSkeletonBone>> byPartial)
    {
        var result = new Dictionary<(int, int), string>();
        foreach (var bone in observed)
        {
            if (bone.ParentBoneIndex < 0)
                continue;

            if (byPartial.TryGetValue(bone.PartialIndex, out var partial) && partial.TryGetValue(bone.ParentBoneIndex, out var parent))
                result[(bone.PartialIndex, bone.BoneIndex)] = parent.Name;
        }

        return result;
    }

    private static Dictionary<string, int> BuildFamilyCounts(IReadOnlyList<ObservedSkeletonBone> observed, IReadOnlyDictionary<string, BoneMetadata> metadataByName)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["conditional-ex"] = observed.Count(static bone => bone.Name.EndsWith("_ex", StringComparison.OrdinalIgnoreCase)),
        };
        foreach (var bone in observed)
        {
            var metadata = metadataByName[bone.Name];
            var family = metadata.Origin switch
            {
                BoneOrigin.NFLB when metadata.Role == BoneFunctionalRole.ClothingRig => "nflb.clothing",
                BoneOrigin.NFLB when metadata.Role == BoneFunctionalRole.PropRig => "nflb.props",
                BoneOrigin.NFLB when metadata.Role == BoneFunctionalRole.BodyExtension => "nflb.body",
                BoneOrigin.NFLB => "nflb.unclassified",
                BoneOrigin.YAS => "yas.physics",
                BoneOrigin.Skelomae when metadata.Role == BoneFunctionalRole.ArticulatedBodyFeature => "skelomae.tongue",
                BoneOrigin.Skelomae when metadata.Role == BoneFunctionalRole.ArticulatedAppendage => "skelomae.wings",
                BoneOrigin.Skelomae => "skelomae.body",
                _ => string.Empty,
            };
            if (!string.IsNullOrEmpty(family))
                counts[family] = counts.GetValueOrDefault(family) + 1;
        }

        counts["nflb.total"] = observed.Count(static bone => bone.Name.StartsWith("nf_", StringComparison.Ordinal));
        return counts;
    }

    private static void AddParentWarnings(
        IEnumerable<ObservedSkeletonBone> observed,
        IReadOnlyDictionary<string, BoneMetadata> metadataByName,
        IReadOnlyDictionary<(int PartialIndex, int BoneIndex), string> parentNames,
        Func<string, string> canonicalize,
        ICollection<string> warnings)
    {
        foreach (var bone in observed)
        {
            var expectedParent = metadataByName[bone.Name].ExpectedParent;
            if (string.IsNullOrEmpty(expectedParent))
                continue;

            if (!parentNames.TryGetValue((bone.PartialIndex, bone.BoneIndex), out var actualParent)
                || !string.Equals(canonicalize(expectedParent), actualParent, StringComparison.Ordinal))
                warnings.Add($"Parent advisory: {bone.Name} expected {expectedParent}, observed {actualParent ?? "<root/missing>"}.");
        }
    }

    private static SkeletonCapabilityEvidence BuildVanillaEvidence(IReadOnlySet<string> names)
    {
        var present = VanillaCoreMarkers.Where(names.Contains).ToArray();
        var state = names.Contains("n_root") && names.Contains("j_kosi") && names.Any(static name => name.StartsWith("j_sebo_", StringComparison.Ordinal)) && present.Length >= 5
            ? SkeletonCapabilityState.Present
            : present.Length > 0 ? SkeletonCapabilityState.Partial : SkeletonCapabilityState.Absent;
        return new SkeletonCapabilityEvidence(state, present, VanillaCoreMarkers.Except(present, StringComparer.Ordinal).ToArray());
    }

    private static SkeletonCapabilityEvidence BuildIvcs1Evidence(IReadOnlySet<string> names, IReadOnlyDictionary<string, BoneMetadata> metadataByName)
    {
        var ivcs1 = metadataByName.Where(static pair => pair.Value.Origin == BoneOrigin.IVCS1).Select(static pair => pair.Key).OrderBy(static name => name, StringComparer.Ordinal).ToArray();
        var regions = new[]
        {
            ivcs1.Any(static name => name is "iv_nitoukin_l" or "iv_nitoukin_r"),
            ivcs1.Count(static name => name.StartsWith("iv_hito_c_", StringComparison.Ordinal) || name.StartsWith("iv_naka_c_", StringComparison.Ordinal) || name.StartsWith("iv_kusu_c_", StringComparison.Ordinal) || name.StartsWith("iv_ko_c_", StringComparison.Ordinal)) >= 2,
            ivcs1.Count(static name => name.StartsWith("iv_asi_", StringComparison.Ordinal)) >= 4,
            ivcs1.Count(static name => name.StartsWith("iv_ochinko_", StringComparison.Ordinal) || name.StartsWith("iv_inshin_", StringComparison.Ordinal) || name is "iv_omanko" or "iv_kuritto") >= 3,
        };
        var regionCount = regions.Count(static region => region);
        var state = ivcs1.Length >= 10 && regionCount >= 3 ? SkeletonCapabilityState.Present : ivcs1.Length > 0 ? SkeletonCapabilityState.Partial : SkeletonCapabilityState.Absent;
        var missing = new List<string>();
        if (!regions[0]) missing.Add("arm extension");
        if (!regions[1]) missing.Add("hand controls");
        if (!regions[2]) missing.Add("toe controls");
        if (!regions[3]) missing.Add("pelvic controls");
        missing.Insert(0, $"curated controls: {ivcs1.Length}/49");
        return new SkeletonCapabilityEvidence(state, ivcs1, missing);
    }

    private static SkeletonCapabilityEvidence BuildIvcs2Evidence(IReadOnlySet<string> names)
    {
        var groups = new Dictionary<string, Func<string, bool>>(StringComparer.Ordinal)
        {
            ["chest"] = static name => name.StartsWith("iv_kyokin_phys_", StringComparison.Ordinal),
            ["abdomen"] = static name => name.StartsWith("iv_fukubu_phys", StringComparison.Ordinal),
            ["thigh"] = static name => name.StartsWith("iv_daitai_phys_", StringComparison.Ordinal),
            ["pelvic"] = static name => name.StartsWith("iv_kintama_phys_", StringComparison.Ordinal),
            ["appendage"] = static name => name.StartsWith("iv_funyachin_phy_", StringComparison.Ordinal),
        };
        var presentGroups = groups.Where(pair => names.Any(pair.Value)).Select(static pair => pair.Key).ToArray();
        var evidence = names.Where(name => groups.Values.Any(match => match(name))).OrderBy(static name => name, StringComparer.Ordinal).ToArray();
        var state = presentGroups.Length >= 3 ? SkeletonCapabilityState.Present : presentGroups.Length > 0 ? SkeletonCapabilityState.Partial : SkeletonCapabilityState.Absent;
        return new SkeletonCapabilityEvidence(state, evidence, groups.Keys.Except(presentGroups, StringComparer.Ordinal).ToArray());
    }

    private static SkeletonCapabilityEvidence BuildExactEvidence(IReadOnlySet<string> names, IReadOnlyCollection<string> expected, int presentThreshold, string label)
    {
        var present = expected.Where(names.Contains).ToArray();
        var state = present.Length == expected.Count ? SkeletonCapabilityState.Present : present.Length >= presentThreshold ? SkeletonCapabilityState.Partial : present.Length > 0 ? SkeletonCapabilityState.Partial : SkeletonCapabilityState.Absent;
        return new SkeletonCapabilityEvidence(state, present, expected.Except(present, StringComparer.Ordinal).Select(name => $"{label}: {name}").ToArray());
    }

    private static SkeletonCapabilityEvidence BuildNflbEvidence(IReadOnlyList<ObservedSkeletonBone> observed, IReadOnlyDictionary<string, BoneMetadata> metadataByName)
    {
        var nflb = observed.Where(static bone => bone.Name.StartsWith("nf_", StringComparison.Ordinal)).Select(static bone => bone.Name).Distinct(StringComparer.Ordinal).OrderBy(static name => name, StringComparer.Ordinal).ToArray();
        var roles = nflb.Select(name => metadataByName[name].Role).Distinct().Count();
        var state = nflb.Length >= 8 && roles >= 2 ? SkeletonCapabilityState.Present : nflb.Length > 0 ? SkeletonCapabilityState.Partial : SkeletonCapabilityState.Absent;
        return new SkeletonCapabilityEvidence(state, nflb, new[] { $"observed: {nflb.Length} controls across {roles} role families" });
    }

    private static SkeletonCapabilityEvidence BuildSkelomaeEvidence(IReadOnlySet<string> names, SkeletonCapabilityState ivcs1State)
    {
        var present = SkelomaeMarkers.Where(names.Contains).ToArray();
        var hasIvcs1Evidence = ivcs1State is SkeletonCapabilityState.Present or SkeletonCapabilityState.Partial;
        var state = present.Length >= 3 && hasIvcs1Evidence ? SkeletonCapabilityState.Present : present.Length >= 2 ? SkeletonCapabilityState.Partial : SkeletonCapabilityState.Absent;
        var missing = SkelomaeMarkers.Except(present, StringComparer.Ordinal).ToList();
        if (!hasIvcs1Evidence)
            missing.Add("IVCS1 companion evidence");
        return new SkeletonCapabilityEvidence(state, present, missing);
    }

    private static string ComputeStructuralFingerprint(
        IReadOnlyList<ObservedSkeletonBone> observed,
        IReadOnlyList<int> partialBoneCounts,
        IReadOnlyDictionary<int, Dictionary<int, ObservedSkeletonBone>> byPartial)
    {
        var rows = new List<string>();
        for (var partial = 0; partial < partialBoneCounts.Count; ++partial)
            rows.Add($"partial:{partial}:count:{partialBoneCounts[partial]}");
        foreach (var bone in observed.Where(static bone => !bone.Name.EndsWith("_ex", StringComparison.OrdinalIgnoreCase)))
        {
            var parent = bone.ParentBoneIndex < 0
                ? "<root>"
                : byPartial.TryGetValue(bone.PartialIndex, out var partial) && partial.TryGetValue(bone.ParentBoneIndex, out var parentBone)
                    ? parentBone.Name
                    : $"<invalid:{bone.ParentBoneIndex}>";
            rows.Add($"bone:{bone.PartialIndex}:{bone.Name}@{parent}");
        }

        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\n", rows.OrderBy(static row => row, StringComparer.Ordinal))));
        return Convert.ToHexString(bytes);
    }
}

/// <summary>
/// Curated semantic metadata. It is advisory only: live armature parentage and presence remain authoritative.
/// </summary>
internal static class CuratedBoneRegistry
{
    private static readonly Dictionary<string, BoneMetadata> Entries = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, string> Aliases = new(StringComparer.Ordinal)
    {
        ["iv_kurrito"] = "iv_kuritto",
    };

    static CuratedBoneRegistry()
    {
        AddIvcs1();
        AddIvcs2();
        AddYas();
        AddNflbKnownControls();
        AddSkelomae();
    }

    public static string Canonicalize(string boneName)
        => Aliases.TryGetValue(boneName, out var canonical) ? canonical : boneName;

    public static bool TryGet(string boneName, out BoneMetadata metadata)
        => Entries.TryGetValue(Canonicalize(boneName), out metadata);

    public static bool TryGetAliasTarget(string boneName, out string canonical)
        => Aliases.TryGetValue(boneName, out canonical!);

    public static IReadOnlyDictionary<string, string> KnownAliases => Aliases;

    public static BoneMetadata InferKnownExtension(string boneName)
    {
        if (TryGet(boneName, out var metadata))
            return metadata;

        if (!boneName.StartsWith("nf_", StringComparison.Ordinal))
            return BoneMetadata.Unknown;

        var role = boneName.StartsWith("nf_shrt_", StringComparison.Ordinal)
                   || boneName.StartsWith("nf_bra_", StringComparison.Ordinal)
                   || boneName.StartsWith("nf_pant_", StringComparison.Ordinal)
                   || boneName.StartsWith("nf_pnty_", StringComparison.Ordinal)
                   || boneName.StartsWith("nf_glasses", StringComparison.Ordinal)
                   || boneName.StartsWith("nf_sho_", StringComparison.Ordinal)
                   || boneName.StartsWith("nf_skrt_", StringComparison.Ordinal)
                   || boneName.StartsWith("nf_hlm_", StringComparison.Ordinal)
                   || boneName.StartsWith("nf_glv_", StringComparison.Ordinal)
            ? BoneFunctionalRole.ClothingRig
            : boneName.Contains("prop", StringComparison.Ordinal)
                ? BoneFunctionalRole.PropRig
                : BoneFunctionalRole.Unknown;

        // Prefix recognition is descriptive only. It never grants semantic, propagation, or corrective trust.
        return new BoneMetadata(
            BoneOrigin.NFLB,
            role,
            BoneAvailability.SkeletonCapabilityConditional | BoneAvailability.ModelConditional,
            BoneAutomationTrust.ManualOnly,
            BoneAnimationCompatibility.NFLBExtended,
            BoneScalingInheritance.None);
    }

    public static SkeletonCapabilityManifest EvaluateCapabilities(IEnumerable<string> liveBoneNames, Func<string, BoneMetadata> resolveMetadata)
    {
        var bones = liveBoneNames.Where(static name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.Ordinal)
            .Select((name, index) => new ObservedSkeletonBone(0, index, name, index == 0 ? -1 : 0))
            .ToArray();
        return SkeletonCapabilityManifestEvaluator.Evaluate(
            bones,
            new[] { bones.Length },
            revision: 0,
            stableObservations: 0,
            bindingCurrent: true,
            resolveMetadata: resolveMetadata,
            canonicalize: Canonicalize);
    }

    private static void AddIvcs1()
    {
        var structural = new BoneMetadata(BoneOrigin.IVCS1, BoneFunctionalRole.BodyExtension, BoneAvailability.SkeletonCapabilityConditional, BoneAutomationTrust.TemplateSafe | BoneAutomationTrust.MirrorSafe, BoneAnimationCompatibility.IVCS1Portable, BoneScalingInheritance.None);
        Add(structural, "iv_hito_c_l", "iv_hito_c_r", "iv_naka_c_l", "iv_naka_c_r", "iv_kusu_c_l", "iv_kusu_c_r", "iv_ko_c_l", "iv_ko_c_r");
        Add(structural with { ScalingInheritance = new BoneScalingInheritance("j_ude_a_l", BoneScalingInheritanceMode.SwapXY) }, "iv_nitoukin_l");
        Add(structural with { ScalingInheritance = new BoneScalingInheritance("j_ude_a_r", BoneScalingInheritanceMode.SwapXY) }, "iv_nitoukin_r");
        Add(structural with { ScalingInheritance = new BoneScalingInheritance("j_kosi", BoneScalingInheritanceMode.SwapXY) }, "iv_shiri_l", "iv_shiri_r");
        foreach (var side in new[] { "l", "r" })
        foreach (var toe in new[] { "oya", "hito", "naka", "kusu", "ko" })
            Add(structural with { ExpectedParent = $"iv_asi_{toe}_a_{side}" }, $"iv_asi_{toe}_b_{side}");
        foreach (var side in new[] { "l", "r" })
        foreach (var toe in new[] { "oya", "hito", "naka", "kusu", "ko" })
            Add(structural with { ExpectedParent = "j_asi_e_" + side }, $"iv_asi_{toe}_a_{side}");

        var breastOverride = structural with
        {
            Role = BoneFunctionalRole.PhysicsControlOverride,
            Trust = structural.Trust | BoneAutomationTrust.AdvancedCorrectiveSafe,
            ScalingInheritance = new BoneScalingInheritance("j_mune_l", BoneScalingInheritanceMode.Identity)
        };
        Add(breastOverride with { ScalingInheritance = new BoneScalingInheritance("j_mune_l", BoneScalingInheritanceMode.Identity) }, "iv_c_mune_l");
        Add(breastOverride with { ScalingInheritance = new BoneScalingInheritance("j_mune_r", BoneScalingInheritanceMode.Identity) }, "iv_c_mune_r");

        var pelvic = structural with { Role = BoneFunctionalRole.BodyExtension, ScalingInheritance = new BoneScalingInheritance("j_kosi", BoneScalingInheritanceMode.SwapXY) };
        Add(pelvic, "iv_kougan_l", "iv_kougan_r", "iv_ochinko_a", "iv_ochinko_b", "iv_ochinko_c", "iv_ochinko_d", "iv_ochinko_e", "iv_ochinko_f", "iv_omanko", "iv_kuritto", "iv_inshin_l", "iv_inshin_r", "iv_koumon", "iv_koumon_l", "iv_koumon_r");
    }

    private static void AddIvcs2()
    {
        var physics = new BoneMetadata(BoneOrigin.IVCS2, BoneFunctionalRole.PhysicsSimulation, BoneAvailability.SkeletonCapabilityConditional | BoneAvailability.ModelConditional, BoneAutomationTrust.TemplateSafe | BoneAutomationTrust.AdvancedCorrectiveSafe, BoneAnimationCompatibility.None, BoneScalingInheritance.None);
        Add(physics, "iv_kyokin_phys_l", "iv_kyokin_phys_r", "iv_fukubu_phys_l", "iv_fukubu_phys_r", "iv_kintama_phys_l", "iv_kintama_phys_r", "iv_funyachin_phy_a", "iv_funyachin_phy_b", "iv_funyachin_phy_c", "iv_funyachin_phy_d");
        Add(physics with { ScalingInheritance = new BoneScalingInheritance("j_sebo_a", BoneScalingInheritanceMode.SwapXY) }, "iv_fukubu_phys");
        Add(physics with { ScalingInheritance = new BoneScalingInheritance("j_asi_a_l", BoneScalingInheritanceMode.Identity) }, "iv_daitai_phys_l");
        Add(physics with { ScalingInheritance = new BoneScalingInheritance("j_asi_a_r", BoneScalingInheritanceMode.Identity) }, "iv_daitai_phys_r");
    }

    private static void AddYas()
    {
        var yas = new BoneMetadata(BoneOrigin.YAS, BoneFunctionalRole.PhysicsSimulation, BoneAvailability.SkeletonCapabilityConditional | BoneAvailability.ModelConditional, BoneAutomationTrust.TemplateSafe | BoneAutomationTrust.AdvancedCorrectiveSafe, BoneAnimationCompatibility.None, BoneScalingInheritance.None);
        Add(yas with { ScalingInheritance = new BoneScalingInheritance("j_kosi", BoneScalingInheritanceMode.SwapXY), ExpectedParent = "j_kosi" }, "ya_fukubu_phys", "ya_shiri_phys_l", "ya_shiri_phys_r");
        Add(yas with { ScalingInheritance = new BoneScalingInheritance("j_asi_a_l", BoneScalingInheritanceMode.SwapXY), ExpectedParent = "j_asi_a_l" }, "ya_daitai_phys_l");
        Add(yas with { ScalingInheritance = new BoneScalingInheritance("j_asi_a_r", BoneScalingInheritanceMode.SwapXY), ExpectedParent = "j_asi_a_r" }, "ya_daitai_phys_r");
    }

    private static void AddNflbKnownControls()
    {
        var body = new BoneMetadata(BoneOrigin.NFLB, BoneFunctionalRole.BodyExtension, BoneAvailability.SkeletonCapabilityConditional | BoneAvailability.ModelConditional, BoneAutomationTrust.TemplateSafe | BoneAutomationTrust.AdvancedCorrectiveSafe, BoneAnimationCompatibility.NFLBExtended, BoneScalingInheritance.None);
        Add(body with { ExpectedParent = "j_kosi" }, "nf_bulge_a");
        Add(body with { ExpectedParent = "iv_c_mune_l" }, "nf_nipple_l");
        Add(body with { ExpectedParent = "iv_c_mune_r" }, "nf_nipple_r");
        Add(body with { ExpectedParent = "iv_kuritto" }, "nf_clitoris");
        Add(body with { ExpectedParent = "iv_inshin_l" }, "nf_labia_inner_l", "nf_labia_outer_l");
        Add(body with { ExpectedParent = "iv_inshin_r" }, "nf_labia_inner_r", "nf_labia_outer_r");
        Add(body with { ExpectedParent = "iv_daitai_phys_l" }, "nf_iv_daitai_phys_l");
        Add(body with { ExpectedParent = "iv_daitai_phys_r" }, "nf_iv_daitai_phys_r");
        Add(body with { ExpectedParent = "iv_shiri_l" }, "nf_iv_shiri_l");
        Add(body with { ExpectedParent = "iv_shiri_r" }, "nf_iv_shiri_r");
        Add(body with { ExpectedParent = "iv_kintama_phys_l" }, "nf_iv_kintama_phys_l");
        Add(body with { ExpectedParent = "iv_kintama_phys_r" }, "nf_iv_kintama_phys_r");
        Add(body with { ExpectedParent = "iv_funyachin_phy_a" }, "nf_iv_funyachin_phy_a");
        Add(body with { ExpectedParent = "iv_funyachin_phy_b" }, "nf_iv_funyachin_phy_b");
        Add(body with { ExpectedParent = "iv_funyachin_phy_c" }, "nf_iv_funyachin_phy_c");
        Add(body with { ExpectedParent = "iv_funyachin_phy_d" }, "nf_iv_funyachin_phy_d");
    }

    private static void AddSkelomae()
    {
        var body = new BoneMetadata(BoneOrigin.Skelomae, BoneFunctionalRole.BodyExtension, BoneAvailability.SkeletonCapabilityConditional | BoneAvailability.ModelConditional, BoneAutomationTrust.TemplateSafe | BoneAutomationTrust.AdvancedCorrectiveSafe, BoneAnimationCompatibility.IVCS1Portable, BoneScalingInheritance.None);
        Add(body, "butt_left", "butt_right", "thigh_l", "thigh_r", "belly_sebo_a", "belly_kosi", "forebreas_l", "forebreas_r");
        var tongue = body with { Role = BoneFunctionalRole.ArticulatedBodyFeature, Trust = BoneAutomationTrust.TemplateSafe };
        Add(tongue, "tongue_a", "tongue_b", "tongue_c", "tongue_d", "tongue_e");
        var wings = body with { Role = BoneFunctionalRole.ArticulatedAppendage, Trust = BoneAutomationTrust.TemplateSafe };
        Add(wings, "mkl_wingbase_l", "mkl_wingarm_a_l", "mkl_wingarm_b_l", "mkl_wingarm_c_l", "mkl_wingarm_d_l", "mkl_wingbase_r", "mkl_wingarm_a_r", "mkl_wingarm_b_r", "mkl_wingarm_c_r", "mkl_wingarm_d_r");
    }

    private static void Add(BoneMetadata metadata, params string[] names)
    {
        foreach (var name in names)
            Entries.Add(name, metadata);
    }
}
