using System;
using System.Collections.Generic;
using System.Linq;

namespace CustomizePlus.Core.Data;

/// <summary>Session-local presentation state for filtering editor rows against a checkpoint.</summary>
internal sealed class LiveCheckpointDiffFilterState
{
    private readonly HashSet<string> _changedBoneNames = new(StringComparer.Ordinal);
    private CheckpointDiffCacheKey? _cachedKey;
    private TemplateDiffReport? _cachedReport;
    private Guid _editorSessionId;

    public Guid SelectedCheckpointId { get; private set; }
    public bool IsFilterEnabled { get; private set; }
    public bool IsComparisonRequested { get; private set; }
    public TemplateDiffReport? CurrentReport => _cachedReport;
    public int ChangedBoneCount => _changedBoneNames.Count;

    public void BeginSession(Guid editorSessionId)
    {
        if (_editorSessionId == editorSessionId)
            return;

        _editorSessionId = editorSessionId;
        Clear();
    }

    public void SelectCheckpoint(Guid checkpointId)
    {
        if (SelectedCheckpointId == checkpointId)
            return;

        SelectedCheckpointId = checkpointId;
        IsComparisonRequested = IsFilterEnabled && checkpointId != Guid.Empty;
        if (checkpointId == Guid.Empty)
            IsFilterEnabled = false;
        ClearCachedReport();
    }

    public void RequestComparison()
    {
        if (SelectedCheckpointId != Guid.Empty)
            IsComparisonRequested = true;
    }

    public void SetFilterEnabled(bool enabled)
    {
        IsFilterEnabled = enabled && SelectedCheckpointId != Guid.Empty;
        if (IsFilterEnabled)
            IsComparisonRequested = true;
    }

    public bool ClearIfSelectedCheckpointIsMissing(IEnumerable<TemplateEditorCheckpoint> checkpoints)
    {
        if (SelectedCheckpointId == Guid.Empty || checkpoints.Any(checkpoint => checkpoint.Id == SelectedCheckpointId))
            return false;

        Clear();
        return true;
    }

    public void Clear()
    {
        SelectedCheckpointId = Guid.Empty;
        IsFilterEnabled = false;
        IsComparisonRequested = false;
        ClearCachedReport();
    }

    public void Synchronize(long editorRevision, Func<Guid, TemplateDiffReport?> compareCheckpoint)
    {
        if (!IsComparisonRequested || SelectedCheckpointId == Guid.Empty || _editorSessionId == Guid.Empty)
        {
            ClearCachedReport();
            return;
        }

        var key = new CheckpointDiffCacheKey(_editorSessionId, SelectedCheckpointId, editorRevision);
        if (_cachedKey == key)
            return;

        var report = compareCheckpoint(SelectedCheckpointId);
        if (report == null)
        {
            Clear();
            return;
        }

        _cachedKey = key;
        _cachedReport = report;
        _changedBoneNames.Clear();
        foreach (var row in report.Rows.Where(static row => row.Kind != TemplateDiffKind.Shared))
            _changedBoneNames.Add(row.BoneName);
    }

    public bool IncludesBone(string boneName)
        => IsFilterEnabled && _cachedReport != null && _changedBoneNames.Contains(boneName);

    private void ClearCachedReport()
    {
        _cachedKey = null;
        _cachedReport = null;
        _changedBoneNames.Clear();
    }

    private readonly record struct CheckpointDiffCacheKey(Guid EditorSessionId, Guid CheckpointId, long EditorRevision);
}
