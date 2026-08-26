// Copyright (c) Customize+.
// Licensed under the MIT license.

#if DEBUG
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using CustomizePlus.Armatures.Data;
using CustomizePlus.Core.Data;
using CustomizePlus.Core.Helpers;
using Dalamud.Plugin;

namespace CustomizePlus.Core.Services;

/// <summary>
/// Development-only local evidence capture. Captures are diagnostic records and
/// are never consumed as runtime state or transform authority.
/// </summary>
public sealed class RuntimeEvidenceService
{
    private const int SchemaVersion = 1;
    private readonly string _directory;
    private readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };
    private EvidenceComparison? _latestComparison;
    private int _knownCount;

    public RuntimeEvidenceService(IDalamudPluginInterface pluginInterface)
    {
        _directory = Path.Combine(pluginInterface.GetPluginConfigDirectory(), "development-evidence");
        Directory.CreateDirectory(_directory);
        _knownCount = Directory.EnumerateFiles(_directory, "*.json", SearchOption.TopDirectoryOnly).Count();
    }

    internal string DirectoryPath => _directory;
    internal EvidenceComparison? LatestComparison => _latestComparison;

    internal RuntimeEvidenceRecord Capture(Armature armature, string? label, string? note = null)
    {
        var record = CreateRecord(armature, label, note);
        var filename = $"{record.CapturedAtUtc:yyyyMMdd-HHmmss}-{SanitizeFileName(record.Label)}.json";
        File.WriteAllText(Path.Combine(_directory, filename), JsonSerializer.Serialize(record, _jsonOptions));
        _knownCount++;
        return record;
    }

    internal IReadOnlyList<EvidenceFile> List()
    {
        if (!Directory.Exists(_directory))
            return Array.Empty<EvidenceFile>();

        var items = Directory.EnumerateFiles(_directory, "*.json", SearchOption.TopDirectoryOnly)
            .Select(TryRead)
            .Where(static item => item != null)
            .Cast<EvidenceFile>()
            .OrderByDescending(static item => item.Record.CapturedAtUtc)
            .ToArray();
        _knownCount = items.Length;
        return items;
    }

    internal bool Delete(string path)
    {
        if (!Path.GetFullPath(path).StartsWith(Path.GetFullPath(_directory), StringComparison.OrdinalIgnoreCase) || !File.Exists(path))
            return false;
        File.Delete(path);
        _knownCount = Math.Max(0, _knownCount - 1);
        return true;
    }

    internal EvidenceComparison Compare(Armature current, EvidenceFile baseline)
        => _latestComparison = Compare(CreateRecord(current, "current"), baseline.Record);

    internal EvidenceComparison Compare(EvidenceFile current, EvidenceFile baseline)
        => _latestComparison = Compare(current.Record, baseline.Record);

    internal RuntimeEvidenceSummary BuildSummary(Armature? armature)
        => new(_knownCount, _latestComparison?.Summary ?? "No comparison run.", armature?.GetCapabilityManifestSnapshot().StructuralFingerprint ?? string.Empty);

    private EvidenceFile? TryRead(string path)
    {
        try
        {
            var record = JsonSerializer.Deserialize<RuntimeEvidenceRecord>(File.ReadAllText(path));
            return record == null || record.SchemaVersion != SchemaVersion ? null : new EvidenceFile(path, record);
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static RuntimeEvidenceRecord CreateRecord(Armature armature, string? label, string? note = null)
    {
        var manifest = armature.GetCapabilityManifestSnapshot();
        var diagnostics = armature.DeformationQualityDiagnostics;
        var extensions = armature.GetAllBones().Select(static bone => BoneData.GetMetadata(bone.BoneName)).ToArray();
        var applicability = ProfileTransformResolver.Resolve(armature.Profile, manifest).TemplateApplicability;
        return new RuntimeEvidenceRecord(
            SchemaVersion,
            VersionHelper.Version.ToString(),
            string.IsNullOrWhiteSpace(label) ? "capture" : label.Trim(),
            note?.Trim() ?? string.Empty,
            DateTimeOffset.UtcNow,
            armature.ActorIdentifier.ToString(),
            manifest.StructuralFingerprint,
            armature.SkeletonRevision,
            armature.NativeBindingGeneration,
            armature.ProfileResolutionRevision,
            armature.DeformationRevision,
            armature.DiagnosticsRevision,
            manifest.Revision,
            manifest.CapabilityEvidence.ToDictionary(static pair => pair.Key.ToString(), static pair => pair.Value.State.ToString(), StringComparer.Ordinal),
            applicability.Count(static item => item.Active),
            applicability.Count(static item => item.Enabled && !item.Active),
            armature.ResolvedBoneTransforms.Count,
            armature.BoundModelBoneCount,
            armature.ActiveBones.Count,
            diagnostics.Solver.ActiveRegions,
            diagnostics.Solver.PrimaryContributionCount,
            diagnostics.Solver.SupportContributionCount,
            diagnostics.Solver.TransitionContributionCount,
            diagnostics.Solver.SecondaryContributionCount,
            diagnostics.Solver.SecondaryContributionMagnitude,
            diagnostics.Solver.ClampedContributionCount,
            diagnostics.Solver.FallbackCount,
            diagnostics.MaxBilateralDifference,
            diagnostics.MaxContinuityDifference,
            diagnostics.Solver.AutomatedNflbBodyControls,
            diagnostics.Solver.AutomatedSkelomaeBodyControls,
            armature.ActiveBoneImportanceResult.SourceLabel,
            armature.ActiveBoneImportanceResult.ModelSignature ?? string.Empty,
            armature.GetDebugNativeWriteDiagnostics(),
            armature.PerformanceMetrics.Snapshot(),
            extensions.Count(static metadata => metadata.Origin == BoneOrigin.NFLB && metadata.Role == BoneFunctionalRole.BodyExtension),
            extensions.Count(static metadata => metadata.Origin == BoneOrigin.Skelomae && metadata.Role == BoneFunctionalRole.BodyExtension));
    }

    private static EvidenceComparison Compare(RuntimeEvidenceRecord current, RuntimeEvidenceRecord baseline)
    {
        var differences = new List<string>();
        AddDifference(differences, "Structural fingerprint", baseline.StructuralFingerprint, current.StructuralFingerprint);
        AddDifference(differences, "Capabilities", string.Join(',', baseline.Capabilities.OrderBy(static pair => pair.Key)), string.Join(',', current.Capabilities.OrderBy(static pair => pair.Key)));
        AddDifference(differences, "Active assignments", baseline.ActiveAssignments, current.ActiveAssignments);
        AddDifference(differences, "Dormant assignments", baseline.DormantAssignments, current.DormantAssignments);
        AddDifference(differences, "Resolved transforms", baseline.ResolvedTransformCount, current.ResolvedTransformCount);
        AddDifference(differences, "Solver secondary count", baseline.SecondaryContributionCount, current.SecondaryContributionCount);
        AddDifference(differences, "NFLB automatic count", baseline.AutomatedNflbBodyControls, current.AutomatedNflbBodyControls);
        AddDifference(differences, "Skelomae automatic count", baseline.AutomatedSkelomaeBodyControls, current.AutomatedSkelomaeBodyControls);
        AddDifference(differences, "Stale native write skips", baseline.NativeWrites.SkippedStaleBinding, current.NativeWrites.SkippedStaleBinding);
        AddDifference(differences, "Unsafe native write skips", baseline.NativeWrites.SkippedUnsafeTransform, current.NativeWrites.SkippedUnsafeTransform);
        if (MathF.Abs(baseline.MaxBilateralDifference - current.MaxBilateralDifference) > 0.01f)
            differences.Add($"Bilateral difference: {baseline.MaxBilateralDifference:0.000} -> {current.MaxBilateralDifference:0.000}");
        if (MathF.Abs(baseline.MaxContinuityDifference - current.MaxContinuityDifference) > 0.01f)
            differences.Add($"Continuity difference: {baseline.MaxContinuityDifference:0.000} -> {current.MaxContinuityDifference:0.000}");
        return new EvidenceComparison(baseline.Label, current.Label, differences, differences.Count == 0 ? "No material differences." : $"{differences.Count} material difference(s).");
    }

    private static void AddDifference<T>(ICollection<string> differences, string label, T before, T after)
        where T : IEquatable<T>
    {
        if (!before.Equals(after))
            differences.Add($"{label}: {before} -> {after}");
    }

    private static string SanitizeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var safe = new string(value.Select(character => invalid.Contains(character) ? '-' : character).ToArray()).Trim();
        return string.IsNullOrWhiteSpace(safe) ? "capture" : safe[..Math.Min(safe.Length, 48)];
    }
}

internal sealed record EvidenceFile(string Path, RuntimeEvidenceRecord Record);
internal sealed record RuntimeEvidenceSummary(int Count, string LatestComparison, string CurrentStructuralFingerprint);
internal sealed record EvidenceComparison(string BaselineLabel, string CurrentLabel, IReadOnlyList<string> Differences, string Summary);
internal sealed record RuntimeEvidenceRecord(
    int SchemaVersion,
    string BuildIdentity,
    string Label,
    string Note,
    DateTimeOffset CapturedAtUtc,
    string ActorSummary,
    string StructuralFingerprint,
    long ArmatureRevision,
    long NativeBindingGeneration,
    long ProfileResolutionRevision,
    long DeformationRevision,
    long DiagnosticsRevision,
    long ManifestRevision,
    IReadOnlyDictionary<string, string> Capabilities,
    int ActiveAssignments,
    int DormantAssignments,
    int ResolvedTransformCount,
    int BoundModelBoneCount,
    int ActiveModelBoneCount,
    IReadOnlyList<string> ActiveRegions,
    int PrimaryContributionCount,
    int SupportContributionCount,
    int TransitionContributionCount,
    int SecondaryContributionCount,
    float SecondaryContributionMagnitude,
    int ClampedContributionCount,
    int FallbackCount,
    float MaxBilateralDifference,
    float MaxContinuityDifference,
    int AutomatedNflbBodyControls,
    int AutomatedSkelomaeBodyControls,
    string BoneImportanceSource,
    string BoneImportanceSignature,
    ArmatureNativeWriteDiagnostics NativeWrites,
    IReadOnlyList<RuntimeTimingSummary> Timings,
    int AvailableNflbBodyControls,
    int AvailableSkelomaeBodyControls);
#endif
