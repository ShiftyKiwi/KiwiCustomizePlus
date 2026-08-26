// Copyright (c) Customize+.
// Licensed under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;

namespace CustomizePlus.Core.Data;

/// <summary>
/// Local pack authority is intentionally advisory. Even a locally trusted pack
/// cannot bypass live topology, native safety, or grant runtime automation.
/// </summary>
public enum LocalBoneMetadataTrust
{
    Informational,
    ManualExtension,
    LocallyTrusted,
}

[Serializable]
public sealed class LocalBoneMetadataPack
{
    public int SchemaVersion { get; set; }
    public string? PackId { get; set; }
    public string? PackVersion { get; set; }
    public string? PackName { get; set; }
    public string? PackAuthor { get; set; }
    public string? Source { get; set; }
    public string? Description { get; set; }
    public List<LocalBoneMetadataPackEntry> Entries { get; set; } = new();
}

[Serializable]
public sealed class LocalBoneMetadataPackEntry
{
    public string? BoneName { get; set; }
    public string? CodeName { get; set; }
    public string? DisplayName { get; set; }
    public string? Family { get; set; }
    public List<string> Aliases { get; set; } = new();
    public string? SupportClass { get; set; }
    public string? RiskNotes { get; set; }
    public bool? ManualOnly { get; set; }
    public bool? AllowSearchAlias { get; set; }
    public string? MirrorPartner { get; set; }
    public string? ParentOverride { get; set; }
    public string? CandidateOrigin { get; set; }
    public string? CandidateFunctionalRole { get; set; }
    public string? CandidateBodyRegion { get; set; }
    public string? CandidateCapability { get; set; }
    public string? CandidateAxisMapping { get; set; }
    public string? CandidateScalingInheritance { get; set; }
    public string? CandidateAutomationTrust { get; set; }
    public string? TrustLevel { get; set; }

    public string? EffectiveBoneName
        => !string.IsNullOrWhiteSpace(BoneName)
            ? BoneName
            : CodeName;
}

public sealed record LocalBoneMetadataEntry(
    string BoneName,
    string? DisplayName,
    string? Family,
    IReadOnlyList<string> Aliases,
    string SupportClass,
    string? RiskNotes,
    bool ManualOnly,
    bool AllowSearchAlias,
    string? MirrorPartner,
    string? ParentOverride,
    string? CandidateOrigin,
    string? CandidateFunctionalRole,
    string? CandidateBodyRegion,
    string? CandidateCapability,
    string? CandidateAxisMapping,
    string? CandidateScalingInheritance,
    string? CandidateAutomationTrust,
    LocalBoneMetadataTrust TrustLevel,
    string PackName,
    string PackFile)
{
    public string SupportLabel
        => string.IsNullOrWhiteSpace(Family)
            ? SupportClass
            : $"{Family} / {SupportClass}";

    public string EffectiveRiskNote
        => !string.IsNullOrWhiteSpace(RiskNotes)
            ? RiskNotes!
            : ManualOnly
                ? "Manual/experimental only. Not trusted for mirroring, propagation safety, guardrails, BIW, or advanced automation by default."
                : "Metadata is advisory only in this build. It does not grant automation, mirroring, propagation, parent, guardrail, or BIW trust.";

    public string AuthorityLabel => TrustLevel switch
    {
        LocalBoneMetadataTrust.Informational => "Observed / informational",
        LocalBoneMetadataTrust.ManualExtension => "Candidate / manual extension",
        _ => "Locally trusted for authoring notes only",
    };
}

public sealed record UnknownBoneEvidenceRecord(
    string BoneName,
    string? LiveParent,
    IReadOnlyList<string> LiveChildren,
    int TopologyDepth,
    string? MirrorCandidate,
    float? ModelInfluence,
    int ObservationCount,
    bool ParentageStable,
    BoneOrigin Origin,
    BoneFunctionalRole Role,
    BoneAutomationTrust Trust,
    string? CandidateNotes);

public sealed record UnknownBoneEvidenceExport(
    int SchemaVersion,
    string StructuralFingerprint,
    SkeletonCapability Capabilities,
    IReadOnlyList<UnknownBoneEvidenceRecord> Bones,
    string GeneratedBy);

public sealed record LocalBoneMetadataPackStatus(
    string FileName,
    string PackName,
    bool Loaded,
    int EntryCount,
    int IgnoredEntryCount,
    IReadOnlyList<string> Messages)
{
    public string Summary
    {
        get
        {
            var status = Loaded ? $"loaded {EntryCount}" : "not loaded";
            var ignored = IgnoredEntryCount > 0 ? $", ignored {IgnoredEntryCount}" : string.Empty;
            return $"{PackName} ({FileName}): {status}{ignored}";
        }
    }

    public string MessageText
        => Messages.Count == 0 ? string.Empty : string.Join("; ", Messages.Where(m => !string.IsNullOrWhiteSpace(m)));
}
