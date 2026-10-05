using CustomizePlus.Templates.Data;
using System;
using System.Collections.Generic;

namespace CustomizePlus.Core.Data;

/// <summary>
/// Session-local validity state for the manual Template Compare report.
/// The report remains opt-in, but cannot be displayed or applied after either
/// side of the comparison changes.
/// </summary>
internal sealed class TemplateCompareDiffState
{
    private TemplateCompareDiffCache? _cache;
    private Guid _editorSessionId;

    public TemplateDiffReport? CurrentReport => _cache?.Report;
    public bool IsStale { get; private set; }

    public void BeginSession(Guid editorSessionId)
    {
        if (_editorSessionId == editorSessionId)
            return;

        _editorSessionId = editorSessionId;
        Invalidate();
    }

    public void Build(
        Guid editorSessionId,
        long editorRevision,
        Template comparisonTarget,
        Template workingCopy,
        Func<Template, Template, TemplateDiffReport> compare)
    {
        BeginSession(editorSessionId);
        if (editorSessionId == Guid.Empty)
            return;

        _cache = new TemplateCompareDiffCache(
            editorSessionId,
            editorRevision,
            comparisonTarget.UniqueId,
            AuthoringTooling.CloneTransforms(comparisonTarget.Bones),
            compare(comparisonTarget, workingCopy));
        IsStale = false;
    }

    public void Synchronize(Guid editorSessionId, long editorRevision, Template comparisonTarget)
    {
        BeginSession(editorSessionId);
        if (_cache == null)
            return;

        if (_cache.IsCurrent(editorSessionId, editorRevision, comparisonTarget))
            return;

        Invalidate();
    }

    public void Clear()
    {
        _cache = null;
        IsStale = false;
    }

    public void Invalidate()
    {
        var hadReport = _cache != null;
        _cache = null;
        IsStale = hadReport;
    }

    private sealed class TemplateCompareDiffCache(
        Guid editorSessionId,
        long editorRevision,
        Guid comparisonTargetId,
        Dictionary<string, BoneTransform> comparisonTargetBones,
        TemplateDiffReport report)
    {
        public TemplateDiffReport Report { get; } = report;

        public bool IsCurrent(Guid currentSessionId, long currentRevision, Template comparisonTarget)
            => editorSessionId == currentSessionId
                && editorRevision == currentRevision
                && comparisonTargetId == comparisonTarget.UniqueId
                && Matches(comparisonTargetBones, comparisonTarget.Bones);

        private static bool Matches(
            IReadOnlyDictionary<string, BoneTransform> expected,
            IReadOnlyDictionary<string, BoneTransform> current)
        {
            if (expected.Count != current.Count)
                return false;

            foreach (var (boneName, expectedTransform) in expected)
            {
                if (!current.TryGetValue(boneName, out var currentTransform)
                    || !AuthoringTooling.TransformEquals(expectedTransform, currentTransform))
                    return false;
            }

            return true;
        }
    }
}
