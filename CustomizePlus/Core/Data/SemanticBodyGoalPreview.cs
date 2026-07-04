// Copyright (c) Customize+.
// Licensed under the MIT license.

using System.Collections.Generic;
using System.Numerics;

namespace CustomizePlus.Core.Data;

public sealed record SemanticBodyGoalPreview(
    IReadOnlyList<SemanticBodyGoalPreviewRow> Rows,
    IReadOnlyDictionary<string, BoneTransform> FinalTransforms,
    int PreviewedChangeCount,
    int BlockedChangeCount,
    int Signature)
{
    public bool HasPreviewableChanges => PreviewedChangeCount > 0 && FinalTransforms.Count > 0;
}

public sealed record SemanticBodyGoalPreviewRow(
    string GoalName,
    string BoneName,
    string DisplayName,
    Vector3 BeforeScale,
    Vector3 AfterScale,
    Vector3 Delta,
    bool IsSkipped,
    string Reason);
