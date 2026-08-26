// Copyright (c) Customize+.
// Licensed under the MIT license.

using System;
using System.Linq;
using CustomizePlus.Core.Data;

namespace CustomizePlus.Profiles.Data;

/// <summary>
/// A conservative, compositional requirement for one profile template assignment.
/// Missing persisted data is always treated as <see cref="Always"/> for backward compatibility.
/// </summary>
public readonly record struct TemplateCompatibilityRequirement(SkeletonCapability RequiredAll)
{
    public static TemplateCompatibilityRequirement Always => new(SkeletonCapability.None);

    public bool IsAlways => RequiredAll == SkeletonCapability.None;

    public TemplateCompatibilityEvaluation Evaluate(SkeletonCapabilityManifest manifest)
    {
        if (IsAlways)
            return TemplateCompatibilityEvaluation.Active("Always");

        var requiredAll = RequiredAll;
        var missing = Enum.GetValues<SkeletonCapability>()
            .Where(capability => capability != SkeletonCapability.None && requiredAll.HasFlag(capability))
            .Where(capability => manifest.GetState(capability) != SkeletonCapabilityState.Present)
            .ToArray();
        if (missing.Length == 0)
            return TemplateCompatibilityEvaluation.Active(ToDisplayString());

        var reason = string.Join(", ", missing.Select(capability => $"{capability} is {manifest.GetState(capability)}"));
        return TemplateCompatibilityEvaluation.Dormant($"Requires {ToDisplayString()}: {reason}.");
    }

    public string ToDisplayString()
    {
        if (IsAlways)
            return "Always";

        var requiredAll = RequiredAll;
        return string.Join(" + ", Enum.GetValues<SkeletonCapability>()
            .Where(capability => capability != SkeletonCapability.None && requiredAll.HasFlag(capability)));
    }
}

public readonly record struct TemplateCompatibilityEvaluation(bool IsActive, string Reason)
{
    public static TemplateCompatibilityEvaluation Active(string reason) => new(true, reason);

    public static TemplateCompatibilityEvaluation Dormant(string reason) => new(false, reason);
}
