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

        _boneEditor.Draw();
    }

    private void DrawBodyAnalyzer()
    {
        if (_selector.Selected == null)
            return;

        var show = ImGui.CollapsingHeader("Body Analyzer");
        if (!show)
            return;

        if (ImGui.Button("Analyze Template"))
        {
            _analysisResult = AdvancedBodyScalingPipeline.Analyze(_selector.Selected.Bones, _configuration.AdvancedBodyScalingSettings);
            _showFixPreview = false;
        }

        var analysisResult = _analysisResult;
        if (analysisResult == null)
            return;

        ImGui.Spacing();
        ImGui.Text($"Surface Smoothness: {analysisResult.SurfaceSmoothness}%");
        ImGui.Text($"Proportion Balance: {analysisResult.ProportionBalance}%");
        ImGui.Text($"Symmetry: {analysisResult.Symmetry}%");

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

        ImGui.Spacing();
        var hasFixes = analysisResult.SuggestedFixes.Count > 0;
        using (var disabled = ImRaii.Disabled(!hasFixes))
        {
            if (ImGui.Button(_showFixPreview ? "Hide Fix Preview" : "Preview Fix"))
                _showFixPreview = !_showFixPreview;
        }

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

        ImGui.SameLine();
        var canRevertFix = CanRevertAnalyzerFix(_selector.Selected!);
        using (var disabled = ImRaii.Disabled(!canRevertFix))
        {
            if (ImGui.Button("Revert Fix"))
                RevertAnalyzerFixes(_selector.Selected!);
        }

        ImGui.SameLine();
        if (ImGui.Button("Ignore"))
        {
            _analysisResult = null;
            _showFixPreview = false;
        }

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

        var settings = _configuration.AdvancedBodyScalingSettings;
        if (!settings.Enabled || settings.Mode == AdvancedBodyScalingMode.Manual)
            ImGui.TextDisabled("Advanced scaling is disabled or in Manual mode. Preview will be a no-op.");

        if (ImGui.Button("Preview Advanced Scaling"))
            BuildAdvancedScalingPreview(_selector.Selected);

        ImGui.SameLine();
        using (ImRaii.Disabled(_advancedPreview == null))
        {
            if (ImGui.Button("Clear Preview"))
                ClearAdvancedScalingPreview();
        }

        ImGui.SameLine();
        var canApplyPreview = _advancedPreview != null && !_boneEditor.IsEditorActive && !(_selector.Selected?.IsWriteProtected ?? true);
        using (ImRaii.Disabled(!canApplyPreview))
        {
            if (ImGui.Button("Apply Preview"))
                ApplyAdvancedScalingPreview(_selector.Selected!);
        }

        ImGui.SameLine();
        var canRevertAppliedPreview = CanRevertAppliedAdvancedPreview(_selector.Selected!);
        using (ImRaii.Disabled(!canRevertAppliedPreview))
        {
            if (ImGui.Button("Revert Applied Preview"))
                RevertAppliedAdvancedScalingPreview(_selector.Selected!);
        }

        if (_advancedPreview == null)
            return;

        ImGui.Spacing();
        ImGui.Text($"Preview changes: {_advancedPreview.Count} bone(s)");

        if (ImGui.Button(_showAdvancedPreview ? "Hide Preview Details" : "Show Preview Details"))
            _showAdvancedPreview = !_showAdvancedPreview;

        ImGui.SameLine();
        if (ImGui.Checkbox("Show Debug Details", ref _showAdvancedDebug))
        {
            if (!_showAdvancedDebug)
                _advancedDebug = null;
        }

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
            DrawAdvancedScalingDebug(_advancedDebug);
    }

    private void BuildAdvancedScalingPreview(Template template)
    {
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

    private static void DrawAdvancedScalingDebug(AdvancedBodyScalingDebugReport debug)
    {
        ImGui.Spacing();
        if (ImGui.CollapsingHeader("Debug Details"))
        {
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
                {
                    ImGui.Text($"{entry.Description}: {entry.BeforeRatio:0.##} -> {entry.AfterRatio:0.##}");
                }
            }
        }
    }

    private void ApplyAnalyzerFixes(Template template, IReadOnlyDictionary<string, BoneTransform> fixes)
    {
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


    private void SelectorSelectionChanged(Template? oldSelection, Template? newSelection, in TemplateFileSystemSelector.TemplateState state)
    {
        _analysisResult = null;
        _showFixPreview = false;
        ClearAdvancedScalingPreview();

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
