// Copyright (c) Customize+.
// Licensed under the MIT license.

using System.Collections.Generic;
using System.Numerics;

namespace CustomizePlus.Core.Data;

public sealed record SemanticBodyGoal(
    string Id,
    string DisplayName,
    string Description,
    IReadOnlyList<SemanticBodyGoalTarget> Targets);

public sealed record SemanticBodyGoalTarget(
    string BoneName,
    Vector3 ScaleDeltaPerUnit);
