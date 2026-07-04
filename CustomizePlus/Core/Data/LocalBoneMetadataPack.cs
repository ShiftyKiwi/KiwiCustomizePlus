// Copyright (c) Customize+.
// Licensed under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;

namespace CustomizePlus.Core.Data;

[Serializable]
public sealed class LocalBoneMetadataPack
{
    public int SchemaVersion { get; set; }
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
}

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
