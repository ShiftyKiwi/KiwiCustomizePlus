// Copyright (c) Customize+.
// Licensed under the MIT license.

using Dalamud.Interface;
using Dalamud.Bindings.ImGui;
using OtterGui;
using OtterGui.Classes;
using OtterGui.Raii;
using OtterGui.Text;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using CustomizePlus.Templates;
using CustomizePlus.Configuration.Data;
using CustomizePlus.Core.Helpers;
using CustomizePlus.Templates.Data;
using OtterGui.Log;
using CustomizePlus.Templates.Events;
using CustomizePlus.Core.Data;
using CustomizePlus.Core.Extensions;
using Dalamud.Interface.ImGuiNotification;
using Dalamud.Interface.Components;

namespace CustomizePlus.UI.Windows.MainWindow.Tabs.Templates;

public class TemplatePanel : IDisposable
{
    private readonly TemplateFileSystemSelector _selector;
    private readonly TemplateManager _manager;
    private readonly BoneEditorPanel _boneEditor;
    private readonly PluginConfiguration _configuration;
    private readonly MessageService _messageService;
    private readonly PopupSystem _popupSystem;
    private readonly Logger _logger;

    private readonly TemplateEditorEvent _editorEvent;

    private string? _newName;
    private Template? _changedTemplate;
    private BodyAnalysisResult? _analysisResult;
    private bool _showFixPreview;
    private Guid _lastAnalyzerFixTemplateId = Guid.Empty;
    private Dictionary<string, BoneTransform?>? _lastAnalyzerFixSnapshot;
    private Guid _lastAppliedAdvancedPreviewTemplateId = Guid.Empty;
    private Dictionary<string, BoneTransform?>? _lastAppliedAdvancedPreviewSnapshot;
    private IReadOnlyDictionary<string, BoneTransform>? _advancedPreview;
    private AdvancedBodyScalingDebugReport? _advancedDebug;
    private AdvancedBodyScalingStressTestReport? _stressTestReport;
    private bool _showAdvancedPreview;
    private bool _showAdvancedDebug;

    /// <summary>
    /// Set to true if we received OnEditorEvent EditorEnableRequested and waiting for selector value to be changed.
    /// </summary>
    private bool _isEditorEnablePending = false;

    private string SelectionName
        => _selector.Selected == null ? "No Selection" : _selector.IncognitoMode ? _selector.Selected.Incognito : _selector.Selected.Name.Text;

    public TemplatePanel(
        TemplateFileSystemSelector selector,
        TemplateManager manager,
        BoneEditorPanel boneEditor,
        PluginConfiguration configuration,
        MessageService messageService,
        PopupSystem popupSystem,
        Logger logger,
        TemplateEditorEvent editorEvent)
    {
        _selector = selector;
        _manager = manager;
        _boneEditor = boneEditor;
        _configuration = configuration;
        _messageService = messageService;
        _popupSystem = popupSystem;
        _logger = logger;

        _editorEvent = editorEvent;

        _editorEvent.Subscribe(OnEditorEvent, TemplateEditorEvent.Priority.TemplatePanel);

        _selector.SelectionChanged += SelectorSelectionChanged;
    }

    public void Draw()
    {
        using var group = ImRaii.Group();
        if (_selector.SelectedPaths.Count > 1)
        {
            DrawMultiSelection();
        }
        else
        {
            DrawHeader();
            DrawPanel();
        }
    }

    public void Dispose()
    {
        _editorEvent.Unsubscribe(OnEditorEvent);
    }

    private HeaderDrawer.Button LockButton()
        => _selector.Selected == null
            ? HeaderDrawer.Button.Invisible
            : _selector.Selected.IsWriteProtected
                ? new HeaderDrawer.Button
                {
                    Description = "Make this template editable.",
                    Icon = FontAwesomeIcon.Lock,
                    OnClick = () => _manager.SetWriteProtection(_selector.Selected!, false),
                    Disabled = _boneEditor.IsEditorActive
                }
                : new HeaderDrawer.Button
                {
                    Description = "Write-protect this template.",
                    Icon = FontAwesomeIcon.LockOpen,
                    OnClick = () => _manager.SetWriteProtection(_selector.Selected!, true),
                    Disabled = _boneEditor.IsEditorActive
                };

    private HeaderDrawer.Button ExportToClipboardButton()
        => new()
        {
            Description = "Copy the current template to your clipboard.",
            Icon = FontAwesomeIcon.Copy,
            OnClick = ExportToClipboard,
            Visible = _selector.Selected != null,
            Disabled = _boneEditor.IsEditorActive
        };

    private void DrawHeader()
        => HeaderDrawer.Draw(SelectionName, 0, ImGui.GetColorU32(ImGuiCol.FrameBg),
            1, ExportToClipboardButton(), LockButton(),
            HeaderDrawer.Button.IncognitoButton(_selector.IncognitoMode, v => _selector.IncognitoMode = v));

    private void DrawMultiSelection()
    {
        if (_selector.SelectedPaths.Count == 0)
            return;

        var sizeType = ImGui.GetFrameHeight();
        var availableSizePercent = (ImGui.GetContentRegionAvail().X - sizeType - 4 * ImGui.GetStyle().CellPadding.X) / 100;
        var sizeMods = availableSizePercent * 35;
        var sizeFolders = availableSizePercent * 65;

        ImGui.NewLine();
        ImGui.TextUnformatted("Currently Selected Templates");
        ImGui.Separator();
        using var table = ImRaii.Table("templates", 3, ImGuiTableFlags.RowBg);
        ImGui.TableSetupColumn("btn", ImGuiTableColumnFlags.WidthFixed, sizeType);
        ImGui.TableSetupColumn("name", ImGuiTableColumnFlags.WidthFixed, sizeMods);
        ImGui.TableSetupColumn("path", ImGuiTableColumnFlags.WidthFixed, sizeFolders);

        var i = 0;
        foreach (var (fullName, path) in _selector.SelectedPaths.Select(p => (p.FullName(), p))
                     .OrderBy(p => p.Item1, StringComparer.OrdinalIgnoreCase))
        {
            using var id = ImRaii.PushId(i++);
            ImGui.TableNextColumn();
            var icon = (path is TemplateFileSystem.Leaf ? FontAwesomeIcon.FileCircleMinus : FontAwesomeIcon.FolderMinus).ToIconString();
            if (ImGuiUtil.DrawDisabledButton(icon, new Vector2(sizeType), "Remove from selection.", false, true))
                _selector.RemovePathFromMultiSelection(path);

            ImGui.TableNextColumn();
            ImGui.AlignTextToFramePadding();
            ImGui.TextUnformatted(path is TemplateFileSystem.Leaf l ? _selector.IncognitoMode ? l.Value.Incognito : l.Value.Name.Text : string.Empty);

            ImGui.TableNextColumn();
            ImGui.AlignTextToFramePadding();
            ImGui.TextUnformatted(_selector.IncognitoMode ? "Incognito is active" : fullName);
        }
    }

    private void DrawPanel()
    {
        using var child = ImRaii.Child("##Panel", -Vector2.One, true);
        if (!child || _selector.Selected == null)
            return;

        using (var disabled = ImRaii.Disabled(_selector.Selected?.IsWriteProtected ?? true))
        {
            DrawBasicSettings();
            DrawEditorToggle();
        }

        DrawBodyAnalyzer();
        DrawAdvancedScalingPreview();
        DrawPoseStressTest();

        _boneEditor.Draw();
    }

    private void DrawBodyAnalyzer()
    {
        if (_selector.Selected == null)
            return;

        var show = ImGui.CollapsingHeader("Body Analyzer");
        if (!show)
            return;

        ImGui.TextDisabled("Analyzes the current template for smoothing, proportional balance, symmetry, and common transition issues. These scores are guidance, not hard quality rules.");
        ImGui.Spacing();

        if (ImGui.Button("Analyze Template"))
        {
            _analysisResult = AdvancedBodyScalingPipeline.Analyze(_selector.Selected.Bones, _configuration.AdvancedBodyScalingSettings);
            _showFixPreview = false;
        }
        ImGuiUtil.HoverTooltip("Run a heuristic analysis of the current template and list suggested smoothing, balance, symmetry, and transition fixes.");

        var analysisResult = _analysisResult;
        if (analysisResult == null)
            return;

        ImGui.Spacing();
        ImGui.Text($"Surface Smoothness: {analysisResult.SurfaceSmoothness}%");
        ImGui.Text($"Proportion Balance: {analysisResult.ProportionBalance}%");
        ImGui.Text($"Symmetry: {analysisResult.Symmetry}%");
        ImGui.TextDisabled("Heuristic guidance metrics, not absolute quality grades.");

        ImGui.Spacing();
        if (analysisResult.Issues.Count == 0)
        {
            ImGui.Text("Issues detected: none");
        }
        else
        {
            ImGui.Text("Issues detected:");
            foreach (var issue in analysisResult.Issues)
                ImGui.Text($"- {issue}");
        }

        if (!string.IsNullOrWhiteSpace(analysisResult.PoseCorrectiveSummary))
        {
            ImGui.Spacing();
            ImGui.TextUnformatted("RBF pose-space corrective outlook:");
            ImGui.SameLine();
            ImGuiComponents.HelpMarker("Estimate based on the current advanced-scaling settings, built-in RBF sample poses, and detected continuity risk patterns.");
            ImGui.TextWrapped(analysisResult.PoseCorrectiveSummary);
            foreach (var hint in analysisResult.PoseCorrectiveHints)
                ImGui.BulletText(hint);
        }

        if (!string.IsNullOrWhiteSpace(analysisResult.RetargetingSummary))
        {
            ImGui.Spacing();
            ImGui.TextUnformatted("Full IK retargeting outlook:");
            ImGui.SameLine();
            ImGuiComponents.HelpMarker("Estimate based on the current advanced-scaling settings, supported major chains, and detected proportion drift before the final Full-Body IK solve.");
            ImGui.TextWrapped(analysisResult.RetargetingSummary);
            foreach (var hint in analysisResult.RetargetingHints)
                ImGui.BulletText(hint);
        }

        if (!string.IsNullOrWhiteSpace(analysisResult.MotionWarpingSummary))
        {
            ImGui.Spacing();
            ImGui.TextUnformatted("Motion warping outlook:");
            ImGui.SameLine();
            ImGuiComponents.HelpMarker("Estimate based on the current advanced-scaling settings, supported major chains, and expected locomotion mismatch after retargeting. This build supports locomotion warping only, not target-based warping.");
            ImGui.TextWrapped(analysisResult.MotionWarpingSummary);
            foreach (var hint in analysisResult.MotionWarpingHints)
                ImGui.BulletText(hint);
        }

        if (!string.IsNullOrWhiteSpace(analysisResult.FullBodyIkSummary))
        {
            ImGui.Spacing();
            ImGui.TextUnformatted("Full-body IK outlook:");
            ImGui.SameLine();
            ImGuiComponents.HelpMarker("Estimate based on the current advanced-scaling settings, supported major chains, and detected risk patterns after retargeting and motion warping.");
            ImGui.TextWrapped(analysisResult.FullBodyIkSummary);
            foreach (var hint in analysisResult.FullBodyIkHints)
                ImGui.BulletText(hint);
        }

        ImGui.Spacing();
        var hasFixes = analysisResult.SuggestedFixes.Count > 0;
        using (var disabled = ImRaii.Disabled(!hasFixes))
        {
            if (ImGui.Button(_showFixPreview ? "Hide Fix Preview" : "Preview Fix"))
                _showFixPreview = !_showFixPreview;
        }
        ImGuiUtil.HoverTooltip(
            _showFixPreview
                ? "Hide the temporary suggested-fix preview without changing the template."
                : "Show the suggested analyzer changes without committing them to the template.",
            ImGuiHoveredFlags.AllowWhenDisabled);

        ImGui.SameLine();
        var canApplyFix = hasFixes && !_boneEditor.IsEditorActive && !(_selector.Selected?.IsWriteProtected ?? true);
        using (var disabled = ImRaii.Disabled(!canApplyFix))
        {
            if (ImGui.Button("Apply Fix"))
            {
                ApplyAnalyzerFixes(_selector.Selected!, analysisResult.SuggestedFixes);
                _analysisResult = null;
                _showFixPreview = false;
            }
        }
        ImGuiUtil.HoverTooltip("Commit the suggested analyzer fixes into the template.", ImGuiHoveredFlags.AllowWhenDisabled);

        ImGui.SameLine();
        var canRevertFix = CanRevertAnalyzerFix(_selector.Selected!);
        using (var disabled = ImRaii.Disabled(!canRevertFix))
        {
            if (ImGui.Button("Revert Fix"))
                RevertAnalyzerFixes(_selector.Selected!);
        }
        ImGuiUtil.HoverTooltip("Restore the last pre-fix template state, if available.", ImGuiHoveredFlags.AllowWhenDisabled);

        ImGui.SameLine();
        if (ImGui.Button("Ignore"))
        {
            _analysisResult = null;
            _showFixPreview = false;
        }
        ImGuiUtil.HoverTooltip("Dismiss this analysis result without applying its suggested fixes.");

        if (_showFixPreview && hasFixes)
        {
            ImGui.Spacing();
            ImGui.Text("Suggested changes:");

            var count = 0;
            var maxRows = 40;
            foreach (var kvp in analysisResult.SuggestedFixes.OrderBy(x => BoneData.GetBoneRanking(x.Key)))
            {
                if (count >= maxRows)
                {
                    ImGui.Text($"...and {analysisResult.SuggestedFixes.Count - maxRows} more");
                    break;
                }

                var fromScale = _selector.Selected!.Bones.TryGetValue(kvp.Key, out var existing)
                    ? existing.Scaling
                    : Vector3.One;

                var toScale = kvp.Value.Scaling;
                var fromUniform = GetUniformScale(fromScale);
                var toUniform = GetUniformScale(toScale);
                ImGui.Text($"{BoneData.GetBoneDisplayName(kvp.Key)}: {fromUniform:0.##} -> {toUniform:0.##}");
                count++;
            }
        }
    }

    private void DrawAdvancedScalingPreview()
    {
        if (_selector.Selected == null)
            return;

        if (!ImGui.CollapsingHeader("Advanced Scaling Preview"))
            return;

        ImGui.TextDisabled("Previews the current advanced-scaling result on this template without saving until Apply Preview is used.");
        ImGui.Spacing();

        var settings = _configuration.AdvancedBodyScalingSettings;
        if (!settings.Enabled || settings.Mode == AdvancedBodyScalingMode.Manual)
            ImGui.TextDisabled("Advanced scaling is disabled or in Manual mode, so previewing will not change this template.");

        if (ImGui.Button("Preview Advanced Scaling"))
            BuildAdvancedScalingPreview(_selector.Selected);
        ImGuiUtil.HoverTooltip("Generate a temporary advanced-scaling preview for this template without saving it.");

        ImGui.SameLine();
        using (ImRaii.Disabled(_advancedPreview == null))
        {
            if (ImGui.Button("Clear Preview"))
                ClearAdvancedScalingPreview();
        }
        ImGuiUtil.HoverTooltip("Discard the current temporary preview.", ImGuiHoveredFlags.AllowWhenDisabled);

        ImGui.SameLine();
        var canApplyPreview = _advancedPreview != null && !_boneEditor.IsEditorActive && !(_selector.Selected?.IsWriteProtected ?? true);
        using (ImRaii.Disabled(!canApplyPreview))
        {
            if (ImGui.Button("Apply Preview"))
                ApplyAdvancedScalingPreview(_selector.Selected!);
        }
        ImGuiUtil.HoverTooltip("Commit the current preview result into the template.", ImGuiHoveredFlags.AllowWhenDisabled);

        ImGui.SameLine();
        var canRevertAppliedPreview = CanRevertAppliedAdvancedPreview(_selector.Selected!);
        using (ImRaii.Disabled(!canRevertAppliedPreview))
        {
            if (ImGui.Button("Revert Applied Preview"))
                RevertAppliedAdvancedScalingPreview(_selector.Selected!);
        }
        ImGuiUtil.HoverTooltip("Restore the template to the last pre-apply state, if available.", ImGuiHoveredFlags.AllowWhenDisabled);

        if (_advancedPreview == null)
            return;

        ImGui.Spacing();
        ImGui.Text($"Preview changes: {_advancedPreview.Count} bone(s)");

        if (ImGui.Button(_showAdvancedPreview ? "Hide Preview Details" : "Show Preview Details"))
            _showAdvancedPreview = !_showAdvancedPreview;
        ImGuiUtil.HoverTooltip(
            _showAdvancedPreview
                ? "Hide the list of temporary per-bone preview changes."
                : "Show the temporary per-bone changes in this preview.");

        ImGui.SameLine();
        if (ImGui.Checkbox("Show Debug Details", ref _showAdvancedDebug))
        {
            if (!_showAdvancedDebug)
                _advancedDebug = null;
        }
        ImGuiUtil.HoverTooltip("Show debug output for this preview, including guardrails plus estimated RBF corrective, retargeting, motion-warping, and Full-Body IK activity.");

        if (_showAdvancedPreview)
        {
            ImGui.Spacing();
            var count = 0;
            var maxRows = 40;
            foreach (var kvp in _advancedPreview.OrderBy(x => BoneData.GetBoneRanking(x.Key)))
            {
                if (count >= maxRows)
                {
                    ImGui.Text($"...and {_advancedPreview.Count - maxRows} more");
                    break;
                }

                var fromScale = _selector.Selected!.Bones.TryGetValue(kvp.Key, out var existing)
                    ? existing.Scaling
                    : Vector3.One;

                var toScale = kvp.Value.Scaling;
                var fromUniform = GetUniformScale(fromScale);
                var toUniform = GetUniformScale(toScale);
                ImGui.Text($"{BoneData.GetBoneDisplayName(kvp.Key)}: {fromUniform:0.##} -> {toUniform:0.##}");
                count++;
            }
        }

        if (_showAdvancedDebug && _advancedDebug != null)
            DrawAdvancedScalingDebug(_advancedDebug, _configuration.AdvancedBodyScalingSettings);
    }

    private void DrawPoseStressTest()
    {
        if (_selector.Selected == null)
            return;

        if (!ImGui.CollapsingHeader("Pose Stress Test"))
            return;

        if (ImGui.Button("Run Stress Test"))
            BuildPoseStressTest(_selector.Selected);

        ImGui.SameLine();
        using (ImRaii.Disabled(_stressTestReport == null))
        {
            if (ImGui.Button("Clear Stress Test"))
                ClearPoseStressTest();
        }

        if (_stressTestReport == null)
        {
            ImGui.TextDisabled("Runs lightweight pose-risk checks against the current template, current automation output, or the active preview result.");
            return;
        }

        ImGui.Spacing();
        ImGui.Text($"Evaluation target: {_stressTestReport.SourceLabel}");
        ImGui.TextUnformatted("Overall animation risk:");
        DrawRiskProgression(
            _stressTestReport.BaseOverallRisk,
            _stressTestReport.BaseOverallScore,
            _stressTestReport.CorrectiveOverallRisk,
            _stressTestReport.CorrectiveOverallScore,
            _stressTestReport.RetargetingOverallRisk,
            _stressTestReport.RetargetingOverallScore,
            _stressTestReport.MotionWarpingOverallRisk,
            _stressTestReport.MotionWarpingOverallScore,
            _stressTestReport.OverallRisk,
            _stressTestReport.OverallScore);
        ImGui.TextDisabled("Base -> after RBF pose-space correctives -> after full IK retargeting -> after motion warping -> after full-body IK");
        ImGui.TextWrapped(_stressTestReport.Summary);
        if (_stressTestReport.CorrectiveAdvisories.Count > 0)
        {
            ImGui.TextUnformatted("RBF corrective advisories:");
            foreach (var advisory in _stressTestReport.CorrectiveAdvisories.Take(4))
                ImGui.BulletText(advisory);
        }
        if (_stressTestReport.RetargetingAdvisories.Count > 0)
        {
            ImGui.TextUnformatted("Retargeting advisories:");
            foreach (var advisory in _stressTestReport.RetargetingAdvisories.Take(4))
                ImGui.BulletText(advisory);
        }
        if (_stressTestReport.MotionWarpingAdvisories.Count > 0)
        {
            ImGui.TextUnformatted("Motion-warping advisories:");
            foreach (var advisory in _stressTestReport.MotionWarpingAdvisories.Take(4))
                ImGui.BulletText(advisory);
        }
        if (_stressTestReport.FullBodyIkAdvisories.Count > 0)
        {
            ImGui.TextUnformatted("IK tuning advisories:");
            foreach (var advisory in _stressTestReport.FullBodyIkAdvisories.Take(4))
                ImGui.BulletText(advisory);
        }

        if (_stressTestReport.RegionSummary.Count > 0)
        {
            ImGui.Spacing();
            ImGui.TextUnformatted("Region hot spots:");
            foreach (var region in _stressTestReport.RegionSummary.Take(3))
            {
                ImGui.Bullet();
                ImGui.SameLine();
                ImGui.TextUnformatted(region.RegionName);
                ImGui.SameLine();
                DrawRiskProgression(
                    region.BaseRiskLevel,
                    region.BaseScore,
                    region.CorrectiveOnlyRiskLevel,
                    region.CorrectiveOnlyScore,
                    region.RetargetingRiskLevel,
                    region.RetargetingScore,
                    region.MotionWarpingRiskLevel,
                    region.MotionWarpingScore,
                    region.RiskLevel,
                    region.Score);
                ImGui.TextWrapped(region.Reasons.FirstOrDefault() ?? "No major issue detected.");
                if (region.CorrectiveIntensity > 0.05f)
                    ImGui.TextDisabled(region.CorrectiveSummary);
                if (region.RetargetingIntensity > 0.05f)
                    ImGui.TextDisabled(region.RetargetingSummary);
                if (region.MotionWarpingIntensity > 0.05f)
                    ImGui.TextDisabled(region.MotionWarpingSummary);
                if (region.FullBodyIkIntensity > 0.05f)
                    ImGui.TextDisabled(region.FullBodyIkSummary);
            }
        }

        ImGui.Spacing();
        foreach (var pose in _stressTestReport.Poses)
        {
            if (!ImGui.TreeNode($"{pose.Name}##Stress{pose.Name}"))
                continue;

            DrawRiskProgression(
                pose.BaseRiskLevel,
                pose.BaseScore,
                pose.CorrectiveOnlyRiskLevel,
                pose.CorrectiveOnlyScore,
                pose.RetargetingRiskLevel,
                pose.RetargetingScore,
                pose.MotionWarpingRiskLevel,
                pose.MotionWarpingScore,
                pose.RiskLevel,
                pose.Score);
            ImGui.TextDisabled("Base -> after RBF pose-space correctives -> after full IK retargeting -> after motion warping -> after full-body IK");
            ImGui.TextWrapped(pose.Description);

            foreach (var region in pose.Regions)
            {
                ImGui.Spacing();
                ImGui.TextUnformatted(region.RegionName);
                ImGui.SameLine();
                DrawRiskProgression(
                    region.BaseRiskLevel,
                    region.BaseScore,
                    region.CorrectiveOnlyRiskLevel,
                    region.CorrectiveOnlyScore,
                    region.RetargetingRiskLevel,
                    region.RetargetingScore,
                    region.MotionWarpingRiskLevel,
                    region.MotionWarpingScore,
                    region.RiskLevel,
                    region.Score);

                if (region.Reasons.Count == 0)
                {
                    ImGui.TextDisabled("No strong instability heuristic fired for this region in this pose.");
                }
                else
                {
                    foreach (var reason in region.Reasons)
                        ImGui.BulletText(reason);
                }

                if (region.CorrectiveIntensity > 0.05f)
                    ImGui.TextDisabled(region.CorrectiveSummary);

                if (region.RetargetingIntensity > 0.05f)
                    ImGui.TextDisabled(region.RetargetingSummary);

                if (region.MotionWarpingIntensity > 0.05f)
                    ImGui.TextDisabled(region.MotionWarpingSummary);

                if (region.FullBodyIkIntensity > 0.05f)
                    ImGui.TextDisabled(region.FullBodyIkSummary);
            }

            ImGui.TreePop();
        }
    }

    private void BuildPoseStressTest(Template template)
    {
        var input = BuildStressTestInput(template, out var sourceLabel);
        _stressTestReport = AdvancedBodyScalingStressTestHarness.Run(input, _configuration.AdvancedBodyScalingSettings, sourceLabel);
    }

    private IReadOnlyDictionary<string, BoneTransform> BuildStressTestInput(Template template, out string sourceLabel)
    {
        if (_advancedPreview != null && _advancedPreview.Count > 0)
        {
            var merged = template.Bones.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.DeepCopy(), StringComparer.Ordinal);
            foreach (var kvp in _advancedPreview)
            {
                var transform = merged.TryGetValue(kvp.Key, out var existing)
                    ? existing.DeepCopy()
                    : new BoneTransform();

                transform.Scaling = kvp.Value.Scaling;
                merged[kvp.Key] = transform;
            }

            sourceLabel = "Current preview result (not yet applied)";
            return merged;
        }

        var settings = _configuration.AdvancedBodyScalingSettings;
        if (settings.Enabled && settings.Mode != AdvancedBodyScalingMode.Manual)
        {
            sourceLabel = settings.AnimationSafeModeEnabled
                ? "Current advanced scaling output (animation-safe mode)"
                : "Current advanced scaling output";
            return AdvancedBodyScalingPipeline.Apply(template.Bones, settings);
        }

        sourceLabel = "Current template values";
        return template.Bones.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.DeepCopy(), StringComparer.Ordinal);
    }

    private void ClearPoseStressTest()
        => _stressTestReport = null;

    private void BuildAdvancedScalingPreview(Template template)
    {
        ClearPoseStressTest();

        var debug = new AdvancedBodyScalingDebugReport();
        var preview = AdvancedBodyScalingPipeline.Apply(template.Bones, _configuration.AdvancedBodyScalingSettings, debug);

        var changes = new Dictionary<string, BoneTransform>(StringComparer.Ordinal);
        foreach (var kvp in preview)
        {
            var fromScale = template.Bones.TryGetValue(kvp.Key, out var existing)
                ? existing.Scaling
                : Vector3.One;

            if (!fromScale.IsApproximately(kvp.Value.Scaling, 0.0005f))
                changes[kvp.Key] = kvp.Value.DeepCopy();
        }

        _advancedPreview = changes;
        _advancedDebug = debug;
        _showAdvancedPreview = true;

        if (changes.Count == 0)
            _messageService.NotificationMessage("Advanced scaling preview produced no changes.", NotificationType.Info, false);
    }

    private void ApplyAdvancedScalingPreview(Template template)
    {
        if (_advancedPreview == null)
            return;

        _lastAppliedAdvancedPreviewTemplateId = template.UniqueId;
        _lastAppliedAdvancedPreviewSnapshot = _advancedPreview.ToDictionary(
            kvp => kvp.Key,
            kvp => template.Bones.TryGetValue(kvp.Key, out var existing)
                ? existing.DeepCopy()
                : null,
            StringComparer.Ordinal);

        foreach (var kvp in _advancedPreview)
        {
            var transform = template.Bones.TryGetValue(kvp.Key, out var existing)
                ? new BoneTransform(existing)
                : new BoneTransform();

            transform.Scaling = transform.ApplyScalePins(kvp.Value.Scaling);
            _manager.ModifyBoneTransform(template, kvp.Key, transform);
        }

        _manager.QueueSave(template);
        _messageService.NotificationMessage("Applied advanced scaling preview to template.", NotificationType.Success, false);
        ClearAdvancedScalingPreview();
    }

    private void ClearAdvancedScalingPreview()
    {
        ClearPoseStressTest();
        _advancedPreview = null;
        _advancedDebug = null;
        _showAdvancedPreview = false;
        _showAdvancedDebug = false;
    }

    private bool CanRevertAppliedAdvancedPreview(Template template)
        => _lastAppliedAdvancedPreviewSnapshot != null
            && _lastAppliedAdvancedPreviewTemplateId == template.UniqueId
            && !_boneEditor.IsEditorActive
            && !template.IsWriteProtected;

    private void RevertAppliedAdvancedScalingPreview(Template template)
    {
        if (_lastAppliedAdvancedPreviewSnapshot == null || _lastAppliedAdvancedPreviewTemplateId != template.UniqueId)
            return;

        ClearPoseStressTest();

        foreach (var kvp in _lastAppliedAdvancedPreviewSnapshot)
        {
            var transform = kvp.Value?.DeepCopy() ?? new BoneTransform();
            _manager.ModifyBoneTransform(template, kvp.Key, transform);
        }

        _manager.QueueSave(template);
        _lastAppliedAdvancedPreviewSnapshot = null;
        _lastAppliedAdvancedPreviewTemplateId = Guid.Empty;
        _messageService.NotificationMessage("Reverted the last applied advanced scaling preview.", NotificationType.Success, false);
    }

    private static void DrawAdvancedScalingDebug(AdvancedBodyScalingDebugReport debug, AdvancedBodyScalingSettings settings)
    {
        ImGui.Spacing();
        if (ImGui.CollapsingHeader("Debug Details"))
        {
            var poseAwareCorrections = debug.GuardrailCorrections.Count(entry => entry.Description.StartsWith("Pose-aware", StringComparison.Ordinal));
            var guardrailCorrections = debug.GuardrailCorrections.Count - poseAwareCorrections;

            ImGui.Text("Automation activity:");
            ImGui.BulletText($"Guardrails triggered in last preview: {guardrailCorrections}");
            ImGui.BulletText($"Pose-aware corrections triggered in last preview: {poseAwareCorrections}");

            ImGui.Spacing();
            ImGui.Text("Bone importance weighting:");
            DrawWrappedBulletValue("Source", $"{debug.BoneImportanceSource} ({debug.BoneImportanceStage})");
            DrawWrappedBulletValue("Resolution", debug.BoneImportanceResolution);
            DrawWrappedBulletValue("Aggregate mode", $"{debug.BoneImportanceAggregateMode} ({debug.BoneImportanceContributingPartCount} contributing part{(debug.BoneImportanceContributingPartCount == 1 ? string.Empty : "s")})");
            DrawWrappedBulletValue("Crowd-safe mode", $"{debug.BoneImportanceRuntimeMode} on {debug.BoneImportanceActorTier} (full eligible {debug.BoneImportanceFullQualityEligible}, downgraded {debug.BoneImportanceCrowdSafeDowngraded}, stable-throttled {debug.BoneImportanceStableThrottled})");
            DrawWrappedBulletValue("Cache", debug.BoneImportanceCacheHit ? "hit" : "miss / not cached");
            DrawWrappedBulletValue("Refresh", debug.BoneImportanceRefreshStatus);
            DrawWrappedBulletValue("Blend bias", $"{debug.BoneImportanceHeuristicBlend:0.00}");
            DrawWrappedBulletValue("Refinements", $"{(debug.BoneImportanceAreaAwareRefinementActive ? "area-aware on" : "area-aware off")}, {(debug.BoneImportanceClassificationRefinementActive ? "classification-aware on" : "classification-aware off")}, {(debug.BoneImportanceConfidenceWeightedAggregationActive ? "confidence-weighted aggregation on" : "confidence-weighted aggregation off")}");
            if (!string.IsNullOrWhiteSpace(debug.BoneImportanceConfidenceSummary))
                DrawWrappedBulletValue("Slot confidence", debug.BoneImportanceConfidenceSummary);
            if (!string.IsNullOrWhiteSpace(debug.BoneImportanceRuntimeSummary))
                DrawWrappedBulletValue("Runtime policy", debug.BoneImportanceRuntimeSummary);
            DrawWrappedBulletText(debug.BoneImportanceFallbackUsed
                ? "Pipeline fell back to the current heuristic behavior for this preview."
                : "Model-derived importance influenced propagation, smoothing, and guardrails in this preview.");
            if (!string.IsNullOrWhiteSpace(debug.BoneImportanceModelIdentity))
                DrawWrappedDisabledValue("Resolved model", debug.BoneImportanceModelIdentity);
            if (!string.IsNullOrWhiteSpace(debug.BoneImportanceResolutionDetail))
                DrawWrappedDisabledValue("Resolution detail", debug.BoneImportanceResolutionDetail);
            if (!string.IsNullOrWhiteSpace(debug.BoneImportanceRequestedModelPath))
                DrawWrappedDisabledValue("Requested game path", debug.BoneImportanceRequestedModelPath);
            if (!string.IsNullOrWhiteSpace(debug.BoneImportanceModelPath))
                DrawWrappedDisabledValue("Resolved model path", debug.BoneImportanceModelPath);
            if (!string.IsNullOrWhiteSpace(debug.BoneImportanceSummary))
                DrawWrappedDisabledValue("Importance source", debug.BoneImportanceSummary);
            if (!string.IsNullOrWhiteSpace(debug.BoneImportanceRefinementSummary))
                DrawWrappedDisabledValue("Refinement detail", debug.BoneImportanceRefinementSummary);
            if (!string.IsNullOrWhiteSpace(debug.BoneImportanceResolutionTrace) &&
                !string.Equals(debug.BoneImportanceResolutionTrace, debug.BoneImportanceSummary, StringComparison.Ordinal))
                DrawWrappedDisabledValue("Resolution trace", debug.BoneImportanceResolutionTrace);
            if (debug.BoneImportancePartDetails.Count > 0)
            {
                ImGui.TextDisabled("Contributing slots:");
                ImGui.Indent();
                foreach (var part in debug.BoneImportancePartDetails.Take(6))
                    DrawWrappedDisabledBulletText(part);
                ImGui.Unindent();
            }
            if (debug.BoneImportanceMissingPartDetails.Count > 0)
            {
                ImGui.TextDisabled("Missing slots:");
                ImGui.Indent();
                foreach (var missing in debug.BoneImportanceMissingPartDetails.Take(4))
                    DrawWrappedDisabledBulletText(missing);
                ImGui.Unindent();
            }
            foreach (var sample in debug.BoneImportanceSamples.Take(6))
                DrawWrappedBulletText(sample);

            ImGui.Spacing();
            ImGui.Text("Estimated RBF pose-space correctives:");
            ImGui.TextDisabled("Static preview shows adaptive RBF behavior, but live pose-history hysteresis only appears in the runtime armature debug readout.");
            if (debug.EstimatedPoseCorrectives.Count == 0)
            {
                ImGui.Text("None");
            }
            else
            {
                foreach (var entry in debug.EstimatedPoseCorrectives.Take(8))
                {
                    ImGui.Bullet();
                    ImGui.SameLine();
                    ImGui.TextWrapped($"{entry.Label}: driver {entry.DriverStrength:0.00}, activation {entry.Activation:0.00}, corrective {entry.Strength:0.00}, est. risk reduction {entry.EstimatedRiskReduction * 100f:0}%, samples {entry.InfluenceSampleCount}/{entry.SampleCount}.");
                    ImGui.Indent();
                    ImGui.TextDisabled($"{entry.DriverSummary}. {entry.Description}");
                    ImGui.TextDisabled(entry.ShortlistApplied
                        ? $"Nearest-sample shortlist active. {(entry.BroadInterpolation ? "Broad interpolation" : "Focused interpolation")} is using {entry.InfluenceSampleCount} of {entry.SampleCount} samples."
                        : $"{(entry.BroadInterpolation ? "Broad interpolation" : "Focused interpolation")} is using the full {entry.SampleCount}-sample library.");
                    if (!string.IsNullOrWhiteSpace(entry.AdaptiveSummary))
                        ImGui.TextDisabled($"Adaptive tuning: {entry.AdaptiveSummary}");
                    else
                        ImGui.TextDisabled($"Adaptive tuning: {entry.AdaptiveMode}, shortlist {entry.AdaptiveShortlistFloor}-{entry.AdaptiveShortlistMax}, sharpness x{entry.AdaptiveSharpnessScale:0.00}, falloff x{entry.AdaptiveFalloffScale:0.00}, damping x{entry.AdaptiveDampingScale:0.00}.");
                    if (entry.AdaptiveMeaningfulChange)
                        ImGui.TextDisabled("Adaptive solve materially changed shortlist/falloff/damping for this region.");
                    if (!string.IsNullOrWhiteSpace(entry.DriverVectorSummary))
                        ImGui.TextDisabled($"Driver vector: {entry.DriverVectorSummary}");
                    if (!string.IsNullOrWhiteSpace(entry.SampleSummary))
                        ImGui.TextDisabled($"Pose weights: {entry.SampleSummary}");
                    ImGui.Unindent();
                }
            }

            var correctiveAdvisories = AdvancedBodyScalingPoseCorrectiveSystem.GetTuningAdvisories(settings);
            if (correctiveAdvisories.Count > 0)
            {
                ImGui.Spacing();
                ImGui.Text("RBF corrective advisories:");
                foreach (var advisory in correctiveAdvisories.Take(4))
                    ImGui.BulletText(advisory);
            }

            ImGui.Spacing();
            ImGui.Text("Estimated full IK retargeting:");
            if (debug.EstimatedRetargeting.Count == 0)
            {
                ImGui.Text("None");
            }
            else
            {
                foreach (var entry in debug.EstimatedRetargeting.Take(8))
                {
                    ImGui.Bullet();
                    ImGui.SameLine();
                    if (!entry.IsValid)
                    {
                        var reason = string.IsNullOrWhiteSpace(entry.SkipReason) ? "Chain unavailable." : entry.SkipReason;
                        ImGui.TextWrapped($"{entry.Label}: {reason}");
                    }
                    else
                    {
                        ImGui.TextWrapped($"{entry.Label}: blend {entry.BlendAmount:0.00}, strength {entry.Strength:0.00}, est. risk {entry.EstimatedBeforeRisk:0.#} -> {entry.EstimatedAfterRisk:0.#}.");
                    }

                    ImGui.Indent();
                    ImGui.TextDisabled($"{entry.DriverSummary}. {entry.Description}");
                    ImGui.Unindent();
                }
            }

            ImGui.Spacing();
            ImGui.Text($"Estimated motion warping ({AdvancedBodyScalingMotionWarpingSystem.GetImplementationTierLabel()}):");
            if (debug.EstimatedMotionWarping.Count == 0)
            {
                ImGui.Text("None");
            }
            else
            {
                foreach (var entry in debug.EstimatedMotionWarping.Take(8))
                {
                    ImGui.Bullet();
                    ImGui.SameLine();
                    if (!entry.IsValid)
                    {
                        var reason = string.IsNullOrWhiteSpace(entry.SkipReason) ? "Chain unavailable." : entry.SkipReason;
                        ImGui.TextWrapped($"{entry.Label}: {reason}");
                    }
                    else
                    {
                        ImGui.TextWrapped($"{entry.Label}: blend {entry.BlendAmount:0.00}, strength {entry.Strength:0.00}, est. risk {entry.EstimatedBeforeRisk:0.#} -> {entry.EstimatedAfterRisk:0.#}.");
                    }

                    ImGui.Indent();
                    ImGui.TextDisabled($"{entry.DriverSummary}. {entry.Description}");
                    ImGui.Unindent();
                }
            }

            ImGui.Spacing();
            ImGui.Text("Estimated full-body IK:");
            if (debug.EstimatedFullBodyIk.Count == 0)
            {
                ImGui.Text("None");
            }
            else
            {
                foreach (var entry in debug.EstimatedFullBodyIk.Take(8))
                {
                    ImGui.Bullet();
                    ImGui.SameLine();
                    if (!entry.IsValid)
                    {
                        var reason = string.IsNullOrWhiteSpace(entry.SkipReason) ? "Chain unavailable." : entry.SkipReason;
                        ImGui.TextWrapped($"{entry.Label}: {reason}");
                    }
                    else
                    {
                        ImGui.TextWrapped($"{entry.Label}: activation {entry.Activation:0.00}, solve {entry.Strength:0.00}, est. risk {entry.EstimatedBeforeRisk:0.#} -> {entry.EstimatedAfterRisk:0.#}.");
                    }

                    ImGui.Indent();
                    ImGui.TextDisabled($"{entry.DriverSummary}. {entry.Description}");
                    ImGui.Unindent();
                }
            }

            var retargetAdvisories = AdvancedBodyScalingFullIkRetargetingSystem.GetTuningAdvisories(settings);
            if (retargetAdvisories.Count > 0)
            {
                ImGui.Spacing();
                ImGui.Text("Retargeting advisories:");
                foreach (var advisory in retargetAdvisories.Take(4))
                    ImGui.BulletText(advisory);
            }

            var motionAdvisories = AdvancedBodyScalingMotionWarpingSystem.GetTuningAdvisories(settings);
            if (motionAdvisories.Count > 0)
            {
                ImGui.Spacing();
                ImGui.Text("Motion-warping advisories:");
                foreach (var advisory in motionAdvisories.Take(4))
                    ImGui.BulletText(advisory);
            }

            var advisories = AdvancedBodyScalingFullBodyIkSystem.GetTuningAdvisories(settings);
            if (advisories.Count > 0)
            {
                ImGui.Spacing();
                ImGui.Text("IK tuning advisories:");
                foreach (var advisory in advisories.Take(4))
                    ImGui.BulletText(advisory);
            }

            ImGui.Spacing();
            ImGui.Text("Active curve chains:");
            var chainCount = 0;
            foreach (var chain in debug.ActiveCurveChains)
            {
                if (chainCount >= 12)
                {
                    ImGui.Text($"...and {debug.ActiveCurveChains.Count - 12} more");
                    break;
                }

                ImGui.Text($"- {string.Join(" -> ", chain)}");
                chainCount++;
            }

            ImGui.Spacing();
            ImGui.Text("Propagation deltas (top 20):");
            foreach (var (bone, delta) in debug.PropagationDeltas
                         .OrderByDescending(kvp => MathF.Abs(kvp.Value))
                         .Take(20))
            {
                ImGui.Text($"{BoneData.GetBoneDisplayName(bone)}: {delta:+0.###;-0.###;0.###}");
            }

            ImGui.Spacing();
            ImGui.Text("Pre/Post balancing (top 20 changes):");
            foreach (var bone in debug.FinalScales
                         .OrderByDescending(kvp => MathF.Abs(kvp.Value - GetValueOrDefault(debug.InitialScales, kvp.Key, 1f)))
                         .Select(kvp => kvp.Key)
                         .Take(20))
            {
                var initial = GetValueOrDefault(debug.InitialScales, bone, 1f);
                var guardrail = GetValueOrDefault(debug.AfterGuardrails, bone, initial);
                var final = GetValueOrDefault(debug.FinalScales, bone, initial);
                ImGui.Text($"{BoneData.GetBoneDisplayName(bone)}: {initial:0.##} -> {guardrail:0.##} -> {final:0.##}");
            }

            ImGui.Spacing();
            ImGui.Text("Guardrail corrections:");
            if (debug.GuardrailCorrections.Count == 0)
            {
                ImGui.Text("None");
            }
            else
            {
                foreach (var entry in debug.GuardrailCorrections.Take(20))
                    ImGui.Text($"{entry.Description}: {entry.BeforeRatio:0.##} -> {entry.AfterRatio:0.##}");
            }
        }
    }

    private static void DrawWrappedBulletValue(string label, string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return;

        DrawWrappedBulletText($"{label}: {value}");
    }

    private static void DrawWrappedBulletText(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return;

        ImGui.Bullet();
        ImGui.SameLine();
        DrawWrappedTextWithColor(value, ImGui.GetStyle().Colors[(int)ImGuiCol.Text]);
    }

    private static void DrawWrappedDisabledValue(string label, string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return;

        ImGui.TextDisabled($"{label}:");
        ImGui.Indent();
        DrawWrappedTextWithColor(value, ImGui.GetStyle().Colors[(int)ImGuiCol.TextDisabled]);
        ImGui.Unindent();
    }

    private static void DrawWrappedDisabledBulletText(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return;

        ImGui.Bullet();
        ImGui.SameLine();
        DrawWrappedTextWithColor(value, ImGui.GetStyle().Colors[(int)ImGuiCol.TextDisabled]);
    }

    private static void DrawWrappedTextWithColor(string value, Vector4 color)
    {
        if (string.IsNullOrWhiteSpace(value))
            return;

        ImGui.PushStyleColor(ImGuiCol.Text, color);
        ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + ImGui.GetContentRegionAvail().X);
        ImGui.TextUnformatted(value);
        ImGui.PopTextWrapPos();
        ImGui.PopStyleColor();
    }

    private static void DrawRiskTransition(
        AdvancedBodyScalingRiskLevel beforeRisk,
        int beforeScore,
        AdvancedBodyScalingRiskLevel afterRisk,
        int afterScore)
    {
        DrawRiskBadge(beforeRisk, beforeScore);
        if (beforeRisk == afterRisk && beforeScore == afterScore)
            return;

        ImGui.SameLine();
        ImGui.TextUnformatted("->");
        ImGui.SameLine();
        DrawRiskBadge(afterRisk, afterScore);
    }

    private static void DrawRiskProgression(
        AdvancedBodyScalingRiskLevel baseRisk,
        int baseScore,
        AdvancedBodyScalingRiskLevel correctiveRisk,
        int correctiveScore,
        AdvancedBodyScalingRiskLevel retargetingRisk,
        int retargetingScore,
        AdvancedBodyScalingRiskLevel motionWarpingRisk,
        int motionWarpingScore,
        AdvancedBodyScalingRiskLevel finalRisk,
        int finalScore)
    {
        DrawRiskBadge(baseRisk, baseScore);

        ImGui.SameLine();
        ImGui.TextUnformatted("->");
        ImGui.SameLine();
        DrawRiskBadge(correctiveRisk, correctiveScore);

        if (correctiveRisk != retargetingRisk || correctiveScore != retargetingScore)
        {
            ImGui.SameLine();
            ImGui.TextUnformatted("->");
            ImGui.SameLine();
            DrawRiskBadge(retargetingRisk, retargetingScore);
        }

        if (retargetingRisk != motionWarpingRisk || retargetingScore != motionWarpingScore)
        {
            ImGui.SameLine();
            ImGui.TextUnformatted("->");
            ImGui.SameLine();
            DrawRiskBadge(motionWarpingRisk, motionWarpingScore);
        }

        if (motionWarpingRisk == finalRisk && motionWarpingScore == finalScore)
            return;

        ImGui.SameLine();
        ImGui.TextUnformatted("->");
        ImGui.SameLine();
        DrawRiskBadge(finalRisk, finalScore);
    }

    private void ApplyAnalyzerFixes(Template template, IReadOnlyDictionary<string, BoneTransform> fixes)
    {
        ClearPoseStressTest();
        _lastAnalyzerFixTemplateId = template.UniqueId;
        _lastAnalyzerFixSnapshot = fixes.ToDictionary(
            kvp => kvp.Key,
            kvp => template.Bones.TryGetValue(kvp.Key, out var existing)
                ? existing.DeepCopy()
                : null,
            StringComparer.Ordinal);

        foreach (var kvp in fixes)
        {
            var transform = template.Bones.TryGetValue(kvp.Key, out var existing)
                ? new BoneTransform(existing)
                : new BoneTransform();

            transform.Scaling = transform.ApplyScalePins(kvp.Value.Scaling);
            _manager.ModifyBoneTransform(template, kvp.Key, transform);
        }

        _manager.QueueSave(template);
        _messageService.NotificationMessage("Applied body analyzer fixes to template.", NotificationType.Success, false);
    }

    private bool CanRevertAnalyzerFix(Template template)
        => _lastAnalyzerFixSnapshot != null
            && _lastAnalyzerFixTemplateId == template.UniqueId
            && !_boneEditor.IsEditorActive
            && !template.IsWriteProtected;

    private void RevertAnalyzerFixes(Template template)
    {
        if (_lastAnalyzerFixSnapshot == null || _lastAnalyzerFixTemplateId != template.UniqueId)
            return;

        ClearPoseStressTest();

        foreach (var kvp in _lastAnalyzerFixSnapshot)
        {
            var transform = kvp.Value?.DeepCopy() ?? new BoneTransform();
            _manager.ModifyBoneTransform(template, kvp.Key, transform);
        }

        _manager.QueueSave(template);
        _lastAnalyzerFixSnapshot = null;
        _lastAnalyzerFixTemplateId = Guid.Empty;
        _messageService.NotificationMessage("Reverted the last body analyzer fix.", NotificationType.Success, false);
    }

    private static float GetUniformScale(Vector3 scale)
    {
        var x = MathF.Abs(scale.X);
        var y = MathF.Abs(scale.Y);
        var z = MathF.Abs(scale.Z);
        return (x + y + z) / 3f;
    }

    private void DrawEditorToggle()
    {
        (bool isEditorAllowed, bool isEditorActive) = CanToggleEditor();

        if (ImGuiUtil.DrawDisabledButton($"{(_boneEditor.IsEditorActive ? "Finish" : "Start")} bone editing", Vector2.Zero,
            "Toggle the bone editor for this template", !isEditorAllowed))
        {
            if (!isEditorActive)
                _boneEditor.EnableEditor(_selector.Selected!);
            else
                _boneEditor.DisableEditor();
        }
    }

    private (bool isEditorAllowed, bool isEditorActive) CanToggleEditor()
    {
        return ((!_selector.Selected?.IsWriteProtected ?? false) || _configuration.PluginEnabled, _boneEditor.IsEditorActive);
    }

    private void DrawBasicSettings()
    {
        using (var style = ImRaii.PushStyle(ImGuiStyleVar.ButtonTextAlign, new Vector2(0, 0.5f)))
        {
            using (var table = ImRaii.Table("BasicSettings", 2))
            {
                ImGui.TableSetupColumn("BasicCol1", ImGuiTableColumnFlags.WidthFixed, ImGui.CalcTextSize("lorem ipsum dolor").X);
                ImGui.TableSetupColumn("BasicCol2", ImGuiTableColumnFlags.WidthStretch);

                ImGuiUtil.DrawFrameColumn("Template Name");
                ImGui.TableNextColumn();
                var width = new Vector2(ImGui.GetContentRegionAvail().X, 0);
                var name = _newName ?? _selector.Selected!.Name;
                ImGui.SetNextItemWidth(width.X);

                if (!_selector.IncognitoMode)
                {
                    if (ImGui.InputText("##Name", ref name, 128))
                    {
                        _newName = name;
                        _changedTemplate = _selector.Selected;
                    }

                    if (ImGui.IsItemDeactivatedAfterEdit() && _changedTemplate != null)
                    {
                        _manager.Rename(_changedTemplate, name);
                        _newName = null;
                        _changedTemplate = null;
                    }
                }
                else
                    ImGui.TextUnformatted(_selector.Selected!.Incognito);
            }
        }
    }

    private void ExportToClipboard()
    {
        try
        {
            ImUtf8.SetClipboardText(Base64Helper.ExportTemplateToBase64(_selector.Selected!));
            _popupSystem.ShowPopup(PopupSystem.Messages.ClipboardDataNotLongTerm);
        }
        catch (Exception ex)
        {
            _logger.Error($"Could not copy data from template {_selector.Selected!.UniqueId} to clipboard: {ex}");
            _popupSystem.ShowPopup(PopupSystem.Messages.ActionError);
        }
    }


    private static void DrawRiskBadge(AdvancedBodyScalingRiskLevel risk, int score)
    {
        using var color = ImRaii.PushColor(ImGuiCol.Text, GetRiskColor(risk));
        ImGui.TextUnformatted($"{risk} ({score})");
    }

    private static uint GetRiskColor(AdvancedBodyScalingRiskLevel risk)
        => risk switch
        {
            AdvancedBodyScalingRiskLevel.High => ImGui.GetColorU32(new Vector4(0.95f, 0.45f, 0.35f, 1f)),
            AdvancedBodyScalingRiskLevel.Moderate => ImGui.GetColorU32(new Vector4(0.95f, 0.78f, 0.30f, 1f)),
            _ => ImGui.GetColorU32(new Vector4(0.55f, 0.90f, 0.55f, 1f)),
        };

    private void SelectorSelectionChanged(Template? oldSelection, Template? newSelection, in TemplateFileSystemSelector.TemplateState state)
    {
        _analysisResult = null;
        _showFixPreview = false;
        ClearAdvancedScalingPreview();
        ClearPoseStressTest();

        if (!_isEditorEnablePending)
            return;

        _isEditorEnablePending = false;

        _boneEditor.EnableEditor(_selector.Selected!);
    }

    private void OnEditorEvent(TemplateEditorEvent.Type type, Template? template)
    {
        if (type != TemplateEditorEvent.Type.EditorEnableRequestedStage2)
            return;

        if(template == null)
            return;

        (bool isEditorAllowed, bool isEditorActive) = CanToggleEditor();

        if (!isEditorAllowed || isEditorActive)
            return;

        if(_selector.Selected != template)
        {
            _selector.SelectByValue(template);

            _isEditorEnablePending = true;
        }
        else
            _boneEditor.EnableEditor(_selector.Selected!);
    }

    private static float GetValueOrDefault(IReadOnlyDictionary<string, float> dict, string key, float fallback)
        => dict.TryGetValue(key, out var value) ? value : fallback;
}
