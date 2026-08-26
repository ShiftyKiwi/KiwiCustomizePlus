// Copyright (c) Customize+.
// Licensed under the MIT license.

using System.Collections.Generic;
using System.Linq;

namespace CustomizePlus.Core.Data;

/// <summary>
/// Validates the parent-index topology required before a live skeleton snapshot is published.
/// </summary>
internal static class SkeletonTopologyValidator
{
    /// <summary>
    /// Empty partials are valid optional draw-object slots. A skeleton publisher must still reject an all-empty candidate.
    /// </summary>
    public static bool HasValidOptionalPartialTopologies(IReadOnlyList<IReadOnlyList<int>> partialParents)
        => partialParents.All(static parents => parents.Count == 0 || HasValidTopology(parents));

    public static bool HasValidTopology(IReadOnlyList<int> parents)
    {
        if (parents.Count == 0)
            return false;

        var rootCount = 0;
        for (var index = 0; index < parents.Count; ++index)
        {
            var parent = parents[index];
            if (parent < -1 || parent >= parents.Count || parent == index)
                return false;

            if (parent < 0)
                rootCount++;
        }

        if (rootCount == 0)
            return false;

        var states = new byte[parents.Count];
        for (var start = 0; start < parents.Count; ++start)
        {
            if (states[start] != 0)
                continue;

            var current = start;
            while (current >= 0)
            {
                if (states[current] == 1)
                    return false;

                if (states[current] == 2)
                    break;

                states[current] = 1;
                current = parents[current];
            }

            current = start;
            while (current >= 0 && states[current] == 1)
            {
                states[current] = 2;
                current = parents[current];
            }
        }

        return true;
    }
}
