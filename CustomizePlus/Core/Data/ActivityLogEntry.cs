// Copyright (c) Customize+.
// Licensed under the MIT license.

using System;

namespace CustomizePlus.Core.Data;

public enum ActivityLogCategory
{
    Settings,
    AdvancedScaling,
    Templates,
    Profiles,
    Metadata,
    SemanticGoals,
    ImportExport,
}

/// <summary>
/// A local, current-session record of a completed user-facing action.
/// </summary>
public sealed record ActivityLogEntry(
    long Id,
    DateTimeOffset Timestamp,
    ActivityLogCategory Category,
    string Action,
    string Summary,
    string? Detail);
