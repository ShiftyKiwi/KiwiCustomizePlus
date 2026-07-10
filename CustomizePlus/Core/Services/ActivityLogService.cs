// Copyright (c) Customize+.
// Licensed under the MIT license.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using CustomizePlus.Core.Data;
using CustomizePlus.Profiles.Data;
using CustomizePlus.Profiles.Events;
using CustomizePlus.Templates.Data;
using CustomizePlus.Templates.Events;
using Penumbra.GameData.Enums;

namespace CustomizePlus.Core.Services;

/// <summary>
/// Keeps a small, local-only history of completed UI actions for the current plugin session.
/// It never participates in transform resolution, persistence, IPC, or synchronization.
/// </summary>
public sealed class ActivityLogService : IDisposable
{
    public const int Capacity = 50;

    private const int CoalesceWindowMilliseconds = 1500;

    private sealed record StoredEntry(ActivityLogEntry Entry, string? CoalesceKey);
    private sealed record ActivityDescription(string Summary, string? Detail, string CoalesceKey);

    private readonly TemplateChanged _templateChanged;
    private readonly ProfileChanged _profileChanged;
    private readonly List<StoredEntry> _entries = new(Capacity);
    private long _nextId;
    private int _suppressTemplateBoneEvents;

    public ActivityLogService(TemplateChanged templateChanged, ProfileChanged profileChanged)
    {
        _templateChanged = templateChanged;
        _profileChanged = profileChanged;
        _templateChanged.Subscribe(OnTemplateChanged, TemplateChanged.Priority.ActivityLog);
        _profileChanged.Subscribe(OnProfileChanged, ProfileChanged.Priority.ActivityLog);
    }

    public IReadOnlyList<ActivityLogEntry> Entries
        => _entries.Select(entry => entry.Entry).ToArray();

    public void Dispose()
    {
        _templateChanged.Unsubscribe(OnTemplateChanged);
        _profileChanged.Unsubscribe(OnProfileChanged);
    }

    public void Clear()
        => _entries.Clear();

    public void Record(
        ActivityLogCategory category,
        string action,
        string summary,
        string? detail = null,
        string? coalesceKey = null)
    {
        if (string.IsNullOrWhiteSpace(action) || string.IsNullOrWhiteSpace(summary))
            return;

        var now = DateTimeOffset.Now;
        if (!string.IsNullOrWhiteSpace(coalesceKey))
        {
            var existingIndex = _entries.FindIndex(entry =>
                string.Equals(entry.CoalesceKey, coalesceKey, StringComparison.Ordinal) &&
                (now - entry.Entry.Timestamp).TotalMilliseconds <= CoalesceWindowMilliseconds);
            if (existingIndex >= 0)
            {
                var existing = _entries[existingIndex].Entry;
                _entries[existingIndex] = new StoredEntry(
                    new ActivityLogEntry(existing.Id, now, category, action, summary, detail),
                    coalesceKey);
                return;
            }
        }

        _entries.Insert(0, new StoredEntry(
            new ActivityLogEntry(++_nextId, now, category, action, summary, detail),
            coalesceKey));
        if (_entries.Count > Capacity)
            _entries.RemoveRange(Capacity, _entries.Count - Capacity);
    }

    public IDisposable SuppressTemplateBoneEditEvents()
    {
        _suppressTemplateBoneEvents++;
        return new TemplateBoneEventSuppression(this);
    }

    public static string FormatEntry(ActivityLogEntry entry)
    {
        var detail = string.IsNullOrWhiteSpace(entry.Detail) ? string.Empty : $" {entry.Detail}";
        return $"[{entry.Timestamp:HH:mm:ss}] {GetCategoryLabel(entry.Category)} | {entry.Action}: {entry.Summary}{detail}";
    }

    public string BuildClipboardText(IEnumerable<ActivityLogEntry> entries)
        => string.Join(Environment.NewLine, entries
            .OrderBy(entry => entry.Timestamp)
            .Select(FormatEntry));

    public static string GetCategoryLabel(ActivityLogCategory category)
        => category switch
        {
            ActivityLogCategory.AdvancedScaling => "Advanced Scaling",
            ActivityLogCategory.SemanticGoals => "Semantic Goals",
            ActivityLogCategory.ImportExport => "Import / Export",
            _ => category.ToString(),
        };

    private void OnTemplateChanged(TemplateChanged.Type type, Template? template, object? data)
    {
        if (template == null)
            return;

        var templateName = GetTemplateLabel(template);
        switch (type)
        {
            case TemplateChanged.Type.Created:
                Record(ActivityLogCategory.Templates, "Created", $"Created template '{templateName}'.");
                break;
            case TemplateChanged.Type.Deleted:
                Record(ActivityLogCategory.Templates, "Deleted", $"Deleted template '{templateName}'.");
                break;
            case TemplateChanged.Type.Renamed:
                Record(ActivityLogCategory.Templates, "Renamed", $"Renamed template to '{templateName}'.", data as string);
                break;
            case TemplateChanged.Type.NewBone:
            case TemplateChanged.Type.UpdatedBone:
            case TemplateChanged.Type.DeletedBone:
                if (_suppressTemplateBoneEvents == 0)
                {
                    var boneName = data as string;
                    Record(
                        ActivityLogCategory.Templates,
                        "Edited bone transforms",
                        $"Updated bone rows in '{templateName}'.",
                        string.IsNullOrWhiteSpace(boneName) ? null : $"Latest row: {boneName}.",
                        $"template-bones:{template.UniqueId}");
                }
                break;
        }
    }

    private void OnProfileChanged(ProfileChanged.Type type, Profile? profile, object? data)
    {
        if (profile == null)
            return;

        var profileName = GetProfileLabel(profile);
        switch (type)
        {
            case ProfileChanged.Type.Created:
                Record(ActivityLogCategory.Profiles, "Created", $"Created profile '{profileName}'.");
                break;
            case ProfileChanged.Type.Deleted:
                Record(ActivityLogCategory.Profiles, "Deleted", $"Deleted profile '{profileName}'.");
                break;
            case ProfileChanged.Type.Renamed:
                Record(ActivityLogCategory.Profiles, "Renamed", $"Renamed profile to '{profileName}'.", data as string);
                break;
            case ProfileChanged.Type.Toggled:
            {
                var enabled = data is bool enabledValue && enabledValue;
                Record(ActivityLogCategory.Profiles, "Enabled state changed", $"Profile '{profileName}' was {(enabled ? "enabled" : "disabled")}.");
                break;
            }
            case ProfileChanged.Type.PriorityChanged:
                Record(ActivityLogCategory.Profiles, "Priority changed", $"Updated priority for profile '{profileName}'.");
                break;
            case ProfileChanged.Type.AddedCharacter:
                Record(ActivityLogCategory.Profiles, "Character assigned", $"Assigned a character to profile '{profileName}'.");
                break;
            case ProfileChanged.Type.RemovedCharacter:
                Record(ActivityLogCategory.Profiles, "Character removed", $"Removed a character assignment from profile '{profileName}'.");
                break;
            case ProfileChanged.Type.AddedTemplate:
                Record(ActivityLogCategory.Profiles, "Template assigned", $"Added a template to profile '{profileName}'.");
                break;
            case ProfileChanged.Type.RemovedTemplate:
                Record(ActivityLogCategory.Profiles, "Template removed", $"Removed a template from profile '{profileName}'.");
                break;
            case ProfileChanged.Type.EnabledTemplate:
            case ProfileChanged.Type.DisabledTemplate:
                Record(ActivityLogCategory.Profiles, "Template state changed", $"Updated an assigned template in profile '{profileName}'.");
                break;
            case ProfileChanged.Type.ChangedTemplate:
            case ProfileChanged.Type.MovedTemplate:
            case ProfileChanged.Type.TemplateWeightChanged:
                Record(ActivityLogCategory.Profiles, "Template stack changed", $"Updated template order or weight in profile '{profileName}'.");
                break;
            case ProfileChanged.Type.AdvancedBodyScalingSettingsChanged:
                // Profile-scoped Advanced Scaling edits belong with their global counterparts,
                // while the Profiles category remains focused on profile management actions.
                if (data is AdvancedBodyScalingProfileOverrideChange change &&
                    TryDescribeAdvancedBodyScalingChange(change, profile.UniqueId, out var description))
                {
                    Record(
                        ActivityLogCategory.AdvancedScaling,
                        "Profile override changed",
                        $"{description.Summary} for profile '{profileName}'.",
                        description.Detail,
                        description.CoalesceKey);
                }
                else
                {
                    Record(
                        ActivityLogCategory.AdvancedScaling,
                        "Profile overrides changed",
                        $"Updated Advanced Body Scaling overrides for profile '{profileName}'.",
                        coalesceKey: $"profile-advanced-scaling:{profile.UniqueId}:other");
                }
                break;
        }
    }

    private static bool TryDescribeAdvancedBodyScalingChange(
        AdvancedBodyScalingProfileOverrideChange change,
        Guid profileId,
        out ActivityDescription description)
    {
        if (change.Previous.UseProfileOverrides != change.Current.UseProfileOverrides)
        {
            var mode = change.Current.UseProfileOverrides ? "enabled" : "disabled";
            description = new ActivityDescription(
                $"Use Profile Overrides: {mode}",
                $"Use Profile Overrides changed from {(change.Previous.UseProfileOverrides ? "enabled" : "disabled")} to {mode}.",
                $"profile-advanced-scaling:{profileId}:use-profile-overrides");
            return true;
        }

        if (TryDescribeScalarOverrideChange(change.Previous.Overrides, change.Current.Overrides, profileId, out description))
            return true;

        if (TryDescribeRacePresetChange(change.Previous.Overrides, change.Current.Overrides, profileId, out description))
            return true;

        description = null!;
        return false;
    }

    private static bool TryDescribeScalarOverrideChange(
        AdvancedBodyScalingOverrides previous,
        AdvancedBodyScalingOverrides current,
        Guid profileId,
        out ActivityDescription description)
    {
        foreach (var property in typeof(AdvancedBodyScalingOverrides).GetProperties(BindingFlags.Instance | BindingFlags.Public))
        {
            if (IsDictionary(property.PropertyType))
                continue;

            var before = property.GetValue(previous);
            var after = property.GetValue(current);
            if (Equals(before, after))
                continue;

            var label = GetOverrideLabel(property.Name);
            description = new ActivityDescription(
                $"{label}: {FormatOverrideValue(before)} -> {FormatOverrideValue(after)}",
                null,
                $"profile-advanced-scaling:{profileId}:{property.Name}");
            return true;
        }

        description = null!;
        return false;
    }

    private static bool TryDescribeRacePresetChange(
        AdvancedBodyScalingOverrides previous,
        AdvancedBodyScalingOverrides current,
        Guid profileId,
        out ActivityDescription description)
    {
        var previousPresets = previous.RaceNeckPresetOverrides;
        var currentPresets = current.RaceNeckPresetOverrides;
        if (ReferenceEquals(previousPresets, currentPresets))
        {
            description = null!;
            return false;
        }

        if (previousPresets == null || currentPresets == null)
        {
            description = new ActivityDescription(
                currentPresets == null ? "Profile race presets now inherit global settings" : "Profile-local race presets enabled",
                null,
                $"profile-advanced-scaling:{profileId}:race-preset-group");
            return true;
        }

        foreach (var race in previousPresets.Keys.Union(currentPresets.Keys).OrderBy(race => race))
        {
            var hasBefore = previousPresets.TryGetValue(race, out var before);
            var hasAfter = currentPresets.TryGetValue(race, out var after);
            var raceLabel = GetRaceLabel(race);
            if (!hasBefore || !hasAfter)
            {
                description = new ActivityDescription(
                    $"{raceLabel} race preset {(hasAfter ? "added" : "removed")}",
                    null,
                    $"profile-advanced-scaling:{profileId}:race-preset:{race}");
                return true;
            }

            if (!AreEqual(before!.NeckLengthCompensation, after!.NeckLengthCompensation))
            {
                description = CreateRacePresetDescription(profileId, race, "neck length compensation", before.NeckLengthCompensation, after.NeckLengthCompensation);
                return true;
            }

            if (!AreEqual(before.NeckShoulderBlendStrength, after.NeckShoulderBlendStrength))
            {
                description = CreateRacePresetDescription(profileId, race, "neck-to-shoulder blend", before.NeckShoulderBlendStrength, after.NeckShoulderBlendStrength);
                return true;
            }

            if (!AreEqual(before.ClavicleShoulderSmoothing, after.ClavicleShoulderSmoothing))
            {
                description = CreateRacePresetDescription(profileId, race, "clavicle/shoulder smoothing", before.ClavicleShoulderSmoothing, after.ClavicleShoulderSmoothing);
                return true;
            }
        }

        description = null!;
        return false;
    }

    private static ActivityDescription CreateRacePresetDescription(Guid profileId, Race race, string field, float before, float after)
    {
        var raceLabel = GetRaceLabel(race);
        return new ActivityDescription(
            $"{raceLabel} race {field}: {before.ToString("0.00", CultureInfo.InvariantCulture)} -> {after.ToString("0.00", CultureInfo.InvariantCulture)}",
            null,
            $"profile-advanced-scaling:{profileId}:race-preset:{race}:{field}");
    }

    private static bool IsDictionary(Type type)
        => type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Dictionary<,>);

    private static bool AreEqual(float left, float right)
        => MathF.Abs(left - right) <= 0.0001f;

    private static string GetOverrideLabel(string propertyName)
        => propertyName switch
        {
            nameof(AdvancedBodyScalingOverrides.Enabled) => "Advanced Body Scaling",
            nameof(AdvancedBodyScalingOverrides.Mode) => "Automation mode",
            nameof(AdvancedBodyScalingOverrides.AnimationSafeModeEnabled) => "Animation-safe mode",
            nameof(AdvancedBodyScalingOverrides.ModelDerivedBoneImportanceEnabled) => "Model-derived BIW",
            nameof(AdvancedBodyScalingOverrides.PreferTrueSkinWeightImportance) => "Prefer skin weights",
            nameof(AdvancedBodyScalingOverrides.BoneImportanceHeuristicBlend) => "Model weighting blend",
            nameof(AdvancedBodyScalingOverrides.UseRaceSpecificNeckCompensation) => "Profile race-specific presets",
            nameof(AdvancedBodyScalingOverrides.NeckLengthCompensation) => "Neck length compensation",
            nameof(AdvancedBodyScalingOverrides.NeckShoulderBlendStrength) => "Neck-to-shoulder blend",
            nameof(AdvancedBodyScalingOverrides.ClavicleShoulderSmoothing) => "Clavicle/shoulder smoothing",
            _ => Humanize(propertyName),
        };

    private static string FormatOverrideValue(object? value)
        => value switch
        {
            null => "inherit global",
            bool boolean => boolean ? "enabled" : "disabled",
            float number => number.ToString("0.00", CultureInfo.InvariantCulture),
            double number => number.ToString("0.00", CultureInfo.InvariantCulture),
            Enum enumeration => Humanize(enumeration.ToString()),
            _ => value.ToString() ?? "unknown",
        };

    private static string GetRaceLabel(Race race)
        => race switch
        {
            Race.AuRa => "Au Ra",
            Race.Miqote => "Miqo'te",
            _ => race.ToString(),
        };

    private static string Humanize(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var characters = new List<char>(value.Length + 8);
        for (var index = 0; index < value.Length; ++index)
        {
            var current = value[index];
            if (index > 0 && char.IsUpper(current) && !char.IsUpper(value[index - 1]))
                characters.Add(' ');

            characters.Add(current);
        }

        return new string(characters.ToArray());
    }

    private static string GetTemplateLabel(Template template)
        => string.IsNullOrWhiteSpace(template.Name.Text) ? "Unnamed template" : template.Name.Text;

    private static string GetProfileLabel(Profile profile)
        => string.IsNullOrWhiteSpace(profile.Name.Text) ? "Unnamed profile" : profile.Name.Text;

    private sealed class TemplateBoneEventSuppression(ActivityLogService owner) : IDisposable
    {
        private ActivityLogService? _owner = owner;

        public void Dispose()
        {
            if (_owner == null)
                return;

            _owner._suppressTemplateBoneEvents = Math.Max(0, _owner._suppressTemplateBoneEvents - 1);
            _owner = null;
        }
    }
}
