// Copyright (c) Customize+.
// Licensed under the MIT license.

using System.Collections.Generic;

namespace CustomizePlus.Core.Data;

public sealed record ShapeRecipe(
    string Id,
    string DisplayName,
    string Description,
    IReadOnlyDictionary<string, float> GoalValues);
