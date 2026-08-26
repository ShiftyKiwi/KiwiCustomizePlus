// Copyright (c) Customize+.
// Licensed under the MIT license.

using CustomizePlus.Armatures.Data;
using CustomizePlus.Configuration.Data;
using CustomizePlus.Core.Data;
using CustomizePlus.Core.Helpers;
using CustomizePlus.Core.Services;
using CustomizePlus.Game.Services;
using CustomizePlus.GameData.Extensions;
using CustomizePlus.Profiles;
using CustomizePlus.Profiles.Data;
using CustomizePlus.Profiles.Enums;
using CustomizePlus.Templates;
using CustomizePlus.Templates.Data;
using CustomizePlus.UI.Windows.Controls;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Components;
using Dalamud.Interface.Utility;
using OtterGui;
using OtterGui.Log;
using OtterGui.Raii;
using OtterGui.Text;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;

namespace CustomizePlus.UI.Windows.MainWindow.Tabs.Templates;

public class BoneEditorPanel
{
    private const long GroupImportSuccessStatusLifetimeMs = 7000;
    private const long GroupImportFailureStatusLifetimeMs = 12000;
    private const long UnknownWorkbenchStatusLifetimeMs = 12000;

    private readonly TemplateFileSystemSelector _templateFileSystemSelector;
    private readonly TemplateEditorManager _editorManager;
    private readonly PluginConfiguration _configuration;
    private readonly GameObjectService _gameObjectService;
    private readonly ProfileManager _profileManager;
    private readonly ActorAssignmentUi _actorAssignmentUi;
    private readonly PopupSystem _popupSystem;
    private readonly LocalBoneMetadataService _boneMetadataService;
    private readonly BoneExplainabilityService _boneExplainabilityService;
    private readonly SemanticBodyGoalService _semanticBodyGoalService;
    private readonly TemplateManager _templateManager;
    private readonly ActivityLogService _activityLogService;
    private readonly Logger _logger;

    private BoneAttribute _editingAttribute;
    private int _precision;

    private bool _isShowLiveBones;
    private bool _isMirrorModeEnabled;

    private Dictionary<BoneData.BoneFamily, bool> _groupExpandedState = new();

    private bool _openSavePopup;

    private bool _isUnlocked = false;

    private string _boneSearch = string.Empty;

    // A slider transaction retains only the pre-drag managed template snapshot.
    private Dictionary<string, BoneTransform>? _pendingUndoSnapshot = null;
    private bool _commitHistoryAfterWrite;
    private float _initialX, _initialY, _initialZ;
    private Vector3 _initialScale;
    private float _initialChildX, _initialChildY, _initialChildZ;
    private Vector3 _initialChildScale;
    private float _propagateButtonXPos = 0;
    private float _parentRowScreenPosY = 0;

    // favorite bone stuff
    private HashSet<string> _favoriteBones;

    private string? _pendingClipboardText;
    private int _pendingGroupExportCount;
    private string? _pendingImportText;
    private string? _lastGroupImportStatus;
    private string? _unknownWorkbenchStatus;
    private string? _pendingMetadataPackDeleteFileName;
    private string? _pendingMetadataPackDeleteLabel;
    private TemplateHealthReport? _templateHealthReport;
    private bool _lastGroupImportFailed;
    private bool _openMetadataPackDeletePopup;
    private long _lastGroupImportStatusAtMs;
    private long _unknownWorkbenchStatusAtMs;
    private string _unknownBoneSearch = string.Empty;
    private string _templateHealthSearch = string.Empty;
    private bool _templateHealthEditedOnly = true;
    private bool _templateHealthMissingOnly;
    private bool _templateHealthUnknownOnly;
    private bool _templateHealthRiskyOnly;
    private bool _templateHealthAsymmetricOnly;
    private bool _templateHealthLockedPinnedOnly;
    private bool _templateHealthPropagatedOnly;
    private Dictionary<string, float> _semanticGoalValues = new(StringComparer.Ordinal);
    private SemanticBodyGoalPreview? _semanticGoalPreview;
    private TemplateAuthoringOperation? _lastSemanticGoalOperation;
    private Guid _lastSemanticGoalSessionId;
    private string _selectedShapeRecipeId = string.Empty;
    private string? _loadedShapeRecipeName;
    private string? _semanticGoalStatus;
    private string _inspectedBoneName = string.Empty;
    private Guid _compareTemplateId;
    private Guid _compareProfileId;
    private TemplateDiffReport? _templateDiffReport;
    private ProfileDiffReport? _profileDiffReport;
    private SolverPreviewResult? _solverPreview;
    private int _selectedAuthoringRegion;
    private AuthoringRegionScope _selectedAuthoringScope = AuthoringRegionScope.Primary;
    public bool HasChanges => _editorManager.HasChanges;
    public bool IsEditorActive => _editorManager.IsEditorActive;
    public bool IsEditorPaused => _editorManager.IsEditorPaused;
    public bool IsCharacterFound => _editorManager.IsCharacterFound;

    public BoneEditorPanel(
        TemplateFileSystemSelector templateFileSystemSelector,
        TemplateEditorManager editorManager,
        PluginConfiguration configuration,
        GameObjectService gameObjectService,
        ProfileManager profileManager,
        ActorAssignmentUi actorAssignmentUi,
        PopupSystem popupSystem,
        LocalBoneMetadataService boneMetadataService,
        BoneExplainabilityService boneExplainabilityService,
        SemanticBodyGoalService semanticBodyGoalService,
        TemplateManager templateManager,
        ActivityLogService activityLogService,
        Logger logger)
    {
        _templateFileSystemSelector = templateFileSystemSelector;
        _editorManager = editorManager;
        _configuration = configuration;
        _gameObjectService = gameObjectService;
        _profileManager = profileManager;
        _actorAssignmentUi = actorAssignmentUi;
        _popupSystem = popupSystem;
        _boneMetadataService = boneMetadataService;
        _boneExplainabilityService = boneExplainabilityService;
        _semanticBodyGoalService = semanticBodyGoalService;
        _templateManager = templateManager;
        _activityLogService = activityLogService;
        _logger = logger;

        _isShowLiveBones = configuration.EditorConfiguration.ShowLiveBones;
        _isMirrorModeEnabled = configuration.EditorConfiguration.BoneMirroringEnabled;
        _precision = configuration.EditorConfiguration.EditorValuesPrecision;
        _editingAttribute = configuration.EditorConfiguration.EditorMode;
        _favoriteBones = new HashSet<string>(_configuration.EditorConfiguration.FavoriteBones);
        _semanticGoalValues = _semanticBodyGoalService.CreateDefaultGoalValues();
        _selectedShapeRecipeId = _semanticBodyGoalService.Recipes.FirstOrDefault()?.Id ?? string.Empty;
    }

    public bool EnableEditor(Template template)
    {
        if (_editorManager.EnableEditor(template))
        {
            //_editorManager.SetLimitLookupToOwned(_configuration.EditorConfiguration.LimitLookupToOwnedObjects);
            return true;
        }

        return false;
    }

    public bool DisableEditor()
    {
        if (!_editorManager.HasChanges)
            return _editorManager.DisableEditor();

        if (_editorManager.HasChanges && !IsEditorActive)
            throw new Exception("Invalid state in BoneEditorPanel: has changes but editor is not active");

        _openSavePopup = true;

        return false;
    }

    public void Draw()
    {
        _isUnlocked = IsCharacterFound && IsEditorActive && !IsEditorPaused;

        DrawEditorConfirmationPopup();

        ImGui.Separator();

        using (var style = ImRaii.PushStyle(ImGuiStyleVar.ButtonTextAlign, new Vector2(0, 0.5f)))
        {
            string characterText = null!;

            if (_templateFileSystemSelector.IncognitoMode)
                characterText = "Previewing on: incognito active";
            else
                characterText = _editorManager.Character.IsValid ? $"Previewing on: {(_editorManager.Character.Type == Penumbra.GameData.Enums.IdentifierType.Owned ?
                _editorManager.Character.ToNameWithoutOwnerName() : _editorManager.Character.ToString())}" : "No valid character selected";

            ImGuiUtil.PrintIcon(FontAwesomeIcon.User);
            ImGui.SameLine();
            ImGui.Text(characterText);
            ImGui.SameLine();
            ImGuiComponents.HelpMarker("The selected preview character can affect live bone visualization, race-preset evaluation, debug output, and stress-test context.");

            ImGui.Separator();

            var isShouldDraw = ImGui.CollapsingHeader("Change preview character");
            ImGui.SameLine();
            ImGuiComponents.HelpMarker("Choose which character provides context for live bone visualization, race presets, debug output, and stress tests.");

            if (isShouldDraw)
            {
                var width = new Vector2(ImGui.GetContentRegionAvail().X - ImGui.CalcTextSize("Limit to my creatures").X - 68, 0);

                using (var disabled = ImRaii.Disabled(!IsEditorActive || IsEditorPaused))
                {
                    if (!_templateFileSystemSelector.IncognitoMode)
                    {
                        _actorAssignmentUi.DrawWorldCombo(width.X / 2);
                        ImGui.SameLine();
                        _actorAssignmentUi.DrawPlayerInput(width.X / 2);

                        var buttonWidth = new Vector2(165 * ImGuiHelpers.GlobalScale - ImGui.GetStyle().ItemSpacing.X / 2, 0);

                        if (ImGuiUtil.DrawDisabledButton("Apply to player character", buttonWidth, string.Empty, !_actorAssignmentUi.CanSetPlayer))
                            _editorManager.ChangeEditorCharacter(_actorAssignmentUi.PlayerIdentifier);

                        ImGui.SameLine();

                        if (ImGuiUtil.DrawDisabledButton("Apply to retainer", buttonWidth, string.Empty, !_actorAssignmentUi.CanSetRetainer))
                            _editorManager.ChangeEditorCharacter(_actorAssignmentUi.RetainerIdentifier);

                        ImGui.SameLine();

                        if (ImGuiUtil.DrawDisabledButton("Apply to mannequin", buttonWidth, string.Empty, !_actorAssignmentUi.CanSetMannequin))
                            _editorManager.ChangeEditorCharacter(_actorAssignmentUi.MannequinIdentifier);

                        var currentPlayer = _gameObjectService.GetCurrentPlayerActorIdentifier().CreatePermanent();
                        if (ImGuiUtil.DrawDisabledButton("Apply to current character", buttonWidth, string.Empty, !currentPlayer.IsValid))
                            _editorManager.ChangeEditorCharacter(currentPlayer);

                        ImGui.Separator();

                        _actorAssignmentUi.DrawObjectKindCombo(width.X / 2);
                        ImGui.SameLine();
                        _actorAssignmentUi.DrawNpcInput(width.X / 2);

                        if (ImGuiUtil.DrawDisabledButton("Apply to selected NPC", buttonWidth, string.Empty, !_actorAssignmentUi.CanSetNpc))
                            _editorManager.ChangeEditorCharacter(_actorAssignmentUi.NpcIdentifier);
                    }
                    else
                        ImGui.TextUnformatted("Incognito active");
                }
            }

            ImGui.Separator();

            DrawEditorStatusStrip(characterText);
            DrawProfileContextPreviewControl();
            DrawGroupImportStatus();
            DrawTroubleshootingHelper();
            DrawActorHealth();
            DrawBoneExplainabilityInspector();
            DrawCompareAndCompatibilityTools();
            DrawSolverAbPreview();
            DrawRegionBatchTools();
            DrawTemplateHealth();
            DrawSemanticBodyGoals();
            DrawUnknownBoneWorkbench();

            ImGui.Separator();
            DrawBoneEditorToolbar();
            ImGui.Spacing();

            var boneTableHeight = MathF.Max(220 * ImGuiHelpers.GlobalScale, ImGui.GetContentRegionAvail().Y);
            using (var table = ImRaii.Table($"BoneEditorContents", 6, ImGuiTableFlags.BordersOuterH | ImGuiTableFlags.BordersV | ImGuiTableFlags.ScrollY | ImGuiTableFlags.Hideable, new Vector2(0, boneTableHeight)))
            {
                if (!table)
                    return;

                var col1Label = _editingAttribute == BoneAttribute.Rotation ? "Roll" : "X";
                var col2Label = _editingAttribute == BoneAttribute.Rotation ? "Pitch" : "Y";
                var col3Label = _editingAttribute == BoneAttribute.Rotation ? "Yaw" : "Z";
                const string col4Label = "All";

                var controlColumnWidth = GetControlColumnWidth();
                ImGui.TableSetupColumn("Bones", ImGuiTableColumnFlags.NoReorder | ImGuiTableColumnFlags.WidthFixed, controlColumnWidth);

                ImGui.TableSetupColumn($"{col1Label}", ImGuiTableColumnFlags.NoReorder | ImGuiTableColumnFlags.WidthStretch);
                ImGui.TableSetupColumn($"{col2Label}", ImGuiTableColumnFlags.NoReorder | ImGuiTableColumnFlags.WidthStretch);
                ImGui.TableSetupColumn($"{col3Label}", ImGuiTableColumnFlags.NoReorder | ImGuiTableColumnFlags.WidthStretch);
                ImGui.TableSetupColumn($"{col4Label}", ImGuiTableColumnFlags.NoReorder | ImGuiTableColumnFlags.WidthStretch);
                ImGui.TableSetColumnEnabled(4, _editingAttribute == BoneAttribute.Scale);

                ImGui.TableSetupColumn("Name", ImGuiTableColumnFlags.NoReorder | ImGuiTableColumnFlags.WidthStretch);

                ImGui.TableHeadersRow();

                IEnumerable<EditRowParams> relevantModelBones = null!;
                if (_editorManager.IsEditorActive && _editorManager.EditorProfile != null && _editorManager.EditorProfile.Armatures.Count > 0)
                {
                    var currentTemplateBones = _editorManager.CurrentlyEditedTemplate?.Bones;
                    relevantModelBones = _isShowLiveBones && _editorManager.EditorProfile.Armatures.Count > 0
                        ? _editorManager.EditorProfile.Armatures[0]
                            .GetAllBones()
                            .DistinctBy(x => x.BoneName)
                            .Select(x =>
                            {
                                BoneTransform? templateTransform = null;
                                if (currentTemplateBones != null)
                                    currentTemplateBones.TryGetValue(x.BoneName, out templateTransform);

                                var editTransform = templateTransform ?? (_editorManager.ProfileContextPreviewActive ? new BoneTransform() : null);
                                return new EditRowParams(x, editTransform);
                            })
                        : _editorManager.ProfileContextPreviewActive && currentTemplateBones != null
                            ? currentTemplateBones.Select(x => new EditRowParams(x.Key, x.Value))
                            : _editorManager.EditorProfile.Armatures[0].BoneTemplateBinding.Where(x => x.Value.Bones.ContainsKey(x.Key))
                                .Select(x => new EditRowParams(x.Key, x.Value.Bones[x.Key])); //todo: this is awful
                }
                else
                    relevantModelBones = _templateFileSystemSelector.Selected!.Bones.Select(x => new EditRowParams(x.Key, x.Value));

                if (!string.IsNullOrEmpty(_boneSearch))
                {
                    relevantModelBones = relevantModelBones
                        .Where(x => BoneData.MatchesSearch(x.BoneCodeName, _boneSearch) ||
                                    _boneMetadataService.MatchesSearch(x.BoneCodeName, _boneSearch));
                }

                var favoriteRows = relevantModelBones
                    .Where(b => _favoriteBones.Contains(b.BoneCodeName))
                    .OrderBy(b => BoneData.GetBoneRanking(b.BoneCodeName))
                    .ToList();

                var nonFavoriteRows = relevantModelBones
                    .Where(b => !_favoriteBones.Contains(b.BoneCodeName))
                    .ToList();

                var groupedBones = nonFavoriteRows
                    .GroupBy(x => BoneData.GetBoneFamily(x.BoneCodeName));

                if (favoriteRows.Count > 0)
                {
                    const string favoritesHeaderId = "FavoritesHeader";

                    if (!_groupExpandedState.TryGetValue((BoneData.BoneFamily)(-1), out var expanded))
                        _groupExpandedState[(BoneData.BoneFamily)(-1)] = expanded = true;

                    if (expanded)
                        ImGui.TableNextRow(ImGuiTableRowFlags.Headers);
                    else
                        ImGui.TableNextRow();

                    using var id = ImRaii.PushId(favoritesHeaderId);
                    ImGui.TableNextColumn();
                    CtrlHelper.ArrowToggle($"##{favoritesHeaderId}", ref expanded);
                    ImGui.SameLine();
                    CtrlHelper.StaticLabel("Favorites");

                    if (expanded)
                    {
                        ImGui.TableNextRow();
                        foreach (var erp in favoriteRows)
                        {
                            var family = BoneData.GetBoneFamily(erp.BoneCodeName);
                            CompleteBoneEditor(family, erp);
                        }
                    }

                    _groupExpandedState[(BoneData.BoneFamily)(-1)] = expanded;
                }

                foreach (var boneGroup in groupedBones.OrderBy(x => (int)x.Key))
                {
                    if (!string.IsNullOrEmpty(_pendingImportText))
                    {
                        ClearGroupImportStatus();
                        try
                        {
                            var importedBones = Base64Helper.ImportEditedBonesFromBase64(_pendingImportText, out var importError);
                            if (importedBones != null)
                            {
                                using (_activityLogService.SuppressTemplateBoneEditEvents())
                                {
                                    foreach (var boneData in importedBones)
                                    {
                                        _editorManager.ModifyBoneTransform(
                                            boneData.BoneCodeName,
                                            new BoneTransform
                                            {
                                                Translation = boneData.Translation,
                                                Rotation = boneData.Rotation,
                                                Scaling = boneData.Scaling,
                                                ChildScaling = boneData.ChildScaling,
                                                ChildScalingIndependent = boneData.ChildScalingIndependent,
                                                PropagateTranslation = boneData.PropagateTranslation,
                                                PropagateRotation = boneData.PropagateRotation,
                                                PropagateScale = boneData.PropagateScale,
                                                PropagationFalloff = boneData.PropagationFalloff,
                                                LockState = boneData.LockState,
                                                PinX = boneData.PinX,
                                                PinY = boneData.PinY,
                                                PinZ = boneData.PinZ
                                            }
                                        );
                                    }
                                }

                                SetGroupImportStatus(
                                    $"Imported {importedBones.Count} grouped bone transform{(importedBones.Count == 1 ? string.Empty : "s")} from clipboard.",
                                    failed: false);
                                _activityLogService.Record(
                                    ActivityLogCategory.ImportExport,
                                    "Grouped import",
                                    $"Imported {importedBones.Count} grouped bone transform{(importedBones.Count == 1 ? string.Empty : "s")} from the clipboard.");
                                _logger.Information(_lastGroupImportStatus);
                            }
                            else
                            {
                                SetGroupImportStatus(importError, failed: true);
                                _activityLogService.Record(
                                    ActivityLogCategory.ImportExport,
                                    "Grouped import failed",
                                    "Could not import grouped bone transforms from the clipboard.",
                                    _lastGroupImportStatus);
                                _logger.Warning($"Group import failed: {_lastGroupImportStatus}");
                                _popupSystem.ShowPopup(PopupSystem.Messages.ClipboardDataUnsupported);
                            }
                        }
                        catch (Exception ex)
                        {
                            SetGroupImportStatus($"Unexpected group import error: {ex.Message}", failed: true);
                            _activityLogService.Record(
                                ActivityLogCategory.ImportExport,
                                "Grouped import failed",
                                "An unexpected error occurred while importing grouped bone transforms.",
                                ex.Message);
                            _logger.Error($"Error while importing grouped bone transforms: {ex}");
                            _popupSystem.ShowPopup(PopupSystem.Messages.ActionError);
                        }
                        finally
                        {
                            _pendingImportText = null;
                        }
                    }

                    //Hide root bone if it's not enabled in settings or if we are in rotation mode
                    if (boneGroup.Key == BoneData.BoneFamily.Root &&
                        (!_configuration.EditorConfiguration.RootPositionEditingEnabled ||
                            _editingAttribute == BoneAttribute.Rotation))
                        continue;

                    //create a dropdown entry for the family if one doesn't already exist
                    //mind that it'll only be rendered if bones exist to fill it
                    if (!_groupExpandedState.TryGetValue(boneGroup.Key, out var expanded))
                    {
                        _groupExpandedState[boneGroup.Key] = false;
                        expanded = false;
                    }

                    if (expanded)
                    {
                        //paint the row in header colors if it's expanded
                        ImGui.TableNextRow(ImGuiTableRowFlags.Headers);
                    }
                    else
                    {
                        ImGui.TableNextRow();
                    }

                    using var id = ImRaii.PushId(boneGroup.Key.ToString());
                    ImGui.TableNextColumn();

                    CtrlHelper.ArrowToggle($"##{boneGroup.Key}", ref expanded);
                    ImGui.SameLine();
                    CtrlHelper.StaticLabel(boneGroup.Key.ToString());
                    if (BoneData.DisplayableFamilies.TryGetValue(boneGroup.Key, out var tip) && tip != null)
                        CtrlHelper.AddHoverText(tip);

                    // sigma
                    var rowMin = ImGui.GetItemRectMin();
                    var rowMax = new Vector2(ImGui.GetContentRegionAvail().X + rowMin.X, ImGui.GetItemRectMax().Y);

                    if (ImGui.IsMouseHoveringRect(rowMin, rowMax) && ImGui.IsMouseClicked(ImGuiMouseButton.Right))
                    {
                        ImGui.OpenPopup($"GroupContext##{boneGroup.Key}");
                    }

                    if (ImGui.BeginPopup($"GroupContext##{boneGroup.Key}"))
                    {
                        using (var disabled = ImRaii.Disabled(!_isUnlocked))
                        {
                            if (ImGui.MenuItem("Copy Group"))
                            {
                                try
                                {
                                    var editedBones = boneGroup
                                        .Where(b => b.Transform != null && b.Transform.IsEdited())
                                        .Select(b => (b.BoneCodeName, b.Transform))
                                        .ToList();

                                    if (editedBones.Count > 0)
                                    {
                                        _pendingClipboardText = Base64Helper.ExportEditedBonesToBase64(editedBones);
                                        _pendingGroupExportCount = editedBones.Count;
                                    }
                                }
                                catch (Exception)
                                {
                                    _activityLogService.Record(
                                        ActivityLogCategory.ImportExport,
                                        "Grouped export failed",
                                        "Could not copy grouped bone transforms to the clipboard.");
                                    _popupSystem.ShowPopup(PopupSystem.Messages.ActionError);
                                }
                            }

                            if (ImGui.MenuItem("Import Group"))
                            {
                                var clipboardText = ImUtf8.GetClipboardText();
                                if (!string.IsNullOrEmpty(clipboardText))
                                {
                                    ClearGroupImportStatus();
                                    _pendingImportText = clipboardText;
                                }
                            }
                        }

                        ImGui.EndPopup();
                    }

                    if (expanded)
                    {
                        ImGui.TableNextRow();
                        foreach (var erp in boneGroup.OrderBy(x => BoneData.GetBoneRanking(x.BoneCodeName)))
                        {
                            CompleteBoneEditor(boneGroup.Key, erp);
                        }
                    }

                    _groupExpandedState[boneGroup.Key] = expanded;
                }
            }
        }

        if (!string.IsNullOrEmpty(_pendingClipboardText))
        {
            try
            {
                ImUtf8.SetClipboardText(_pendingClipboardText);
                _activityLogService.Record(
                    ActivityLogCategory.ImportExport,
                    "Grouped export",
                    $"Copied {_pendingGroupExportCount} grouped bone transform{(_pendingGroupExportCount == 1 ? string.Empty : "s")} to the clipboard.");
                _logger.Debug("Copied grouped bone transforms to clipboard.");
            }
            catch (Exception)
            {
                _activityLogService.Record(
                    ActivityLogCategory.ImportExport,
                    "Grouped export failed",
                    "Could not copy grouped bone transforms to the clipboard.");
                _logger.Debug("Could not copy grouped bone transforms to clipboard.");
            }
            _pendingClipboardText = null;
            _pendingGroupExportCount = 0;
        }

    }

    private void DrawEditorConfirmationPopup()
    {
        if (_openSavePopup)
        {
            ImGui.OpenPopup("SavePopup");
            _openSavePopup = false;
        }

        var viewportSize = ImGui.GetWindowViewport().Size;
        var scale = ImGuiHelpers.GlobalScale;
        var style = ImGui.GetStyle();
        var popupWidth = MathF.Min(520 * scale, MathF.Max(1, viewportSize.X - 48 * scale));
        ImGui.SetNextWindowSize(new Vector2(popupWidth, 0));
        ImGui.SetNextWindowPos(viewportSize / 2, ImGuiCond.Always, new Vector2(0.5f));
        using var popup = ImRaii.Popup("SavePopup", ImGuiWindowFlags.Modal);
        if (!popup)
            return;

        ImGuiUtil.TextWrapped("You have unsaved changes in current template, what would you like to do?");
        ImGui.Spacing();

        var buttonWidth = (ImGui.GetContentRegionAvail().X - style.ItemSpacing.X) / 2;
        var buttonSize = new Vector2(buttonWidth, 0);

        var ExitedEditor = false;

        if (ImGui.Button("Save", buttonSize))
        {
            _editorManager.SaveChangesAndDisableEditor();
            ExitedEditor = true;
            ImGui.CloseCurrentPopup();
        }

        ImGui.SameLine();
        if (ImGui.Button("Save as a copy", buttonSize))
        {
            _editorManager.SaveChangesAndDisableEditor(true);
            ExitedEditor = true;
            ImGui.CloseCurrentPopup();
        }

        if (ImGui.Button("Do not save", buttonSize))
        {
            _editorManager.DisableEditor();
            ExitedEditor = true;
            ImGui.CloseCurrentPopup();
        }

        ImGui.SameLine();
        if (ImGui.Button("Keep editing", buttonSize))
        {
            ImGui.CloseCurrentPopup();
        }

        if (ExitedEditor)
        {
        }
    }

    private Armature? GetPrimaryEditorArmature()
    {
        return _editorManager.IsEditorActive && _editorManager.EditorProfile?.Armatures.Count > 0
            ? _editorManager.EditorProfile.Armatures[0]
            : null;
    }

    private void DrawBoneEditorToolbar()
    {
        using var table = ImRaii.Table("BoneEditorToolbar", 3, ImGuiTableFlags.SizingStretchProp);
        if (!table)
            return;

        ImGui.TableSetupColumn("EditMode", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn("Filter", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn("History", ImGuiTableColumnFlags.WidthFixed, 115 * ImGuiHelpers.GlobalScale);

        ImGui.TableNextRow();

        ImGui.TableNextColumn();
        ImGui.AlignTextToFramePadding();
        ImGui.TextDisabled("Edit mode:");
        ImGui.SameLine();

        var modeChanged = false;
        if (ImGui.RadioButton("Position", _editingAttribute == BoneAttribute.Position))
        {
            _editingAttribute = BoneAttribute.Position;
            modeChanged = true;
        }
        CtrlHelper.AddHoverText("Position is the highest-risk gameplay mode because offsets can expose hierarchy and animation artifacts.\nUse it sparingly, and prefer Scale for first-pass body shaping.");

        ImGui.SameLine();
        if (ImGui.RadioButton("Rotation", _editingAttribute == BoneAttribute.Rotation))
        {
            _editingAttribute = BoneAttribute.Rotation;
            modeChanged = true;
        }
        CtrlHelper.AddHoverText("Rotation is useful for posing, expression, and special effects.\nIt is usually not the best first-pass body-shaping mode because animation can amplify rotations.");

        ImGui.SameLine();
        if (ImGui.RadioButton("Scale", _editingAttribute == BoneAttribute.Scale))
        {
            _editingAttribute = BoneAttribute.Scale;
            modeChanged = true;
        }
        CtrlHelper.AddHoverText("Scale is the primary body-scaling mode and usually the safest starting point for proportional shape edits.");

        if (modeChanged)
        {
            _configuration.EditorConfiguration.EditorMode = _editingAttribute;
            _configuration.Save();
        }

        ImGui.TableNextColumn();
        ImGui.AlignTextToFramePadding();
        ImGui.TextDisabled("Filter:");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
        ImGui.InputTextWithHint("##BoneSearch", "Search bones...", ref _boneSearch, 64);
        CtrlHelper.AddHoverText("Search by bone name, code name, family, or body terms like shoulders, waist, hips, chest, thigh, calf, wrist, or glute.");

        ImGui.TableNextColumn();
        ImGui.AlignTextToFramePadding();
        ImGui.TextDisabled("History:");
        ImGui.SameLine();
        ImGui.BeginDisabled(_editorManager.EditHistory.UndoCount == 0);
        if (ImGuiComponents.IconButton("##UndoBone", FontAwesomeIcon.Undo))
            _editorManager.TryUndoEdit();
        ImGui.EndDisabled();
        CtrlHelper.AddHoverText("Undo");

        ImGui.SameLine();
        ImGui.BeginDisabled(_editorManager.EditHistory.RedoCount == 0);
        if (ImGuiComponents.IconButton("##RedoBone", FontAwesomeIcon.Redo))
            _editorManager.TryRedoEdit();
        ImGui.EndDisabled();
        CtrlHelper.AddHoverText("Redo");

        ImGui.SameLine();
        ImGui.TextDisabled($"{_editorManager.EditHistory.UndoCount}");
        CtrlHelper.AddHoverText(_editorManager.EditHistory.RecentLabels.Count == 0
            ? "No session-local edit history yet."
            : string.Join(Environment.NewLine, _editorManager.EditHistory.RecentLabels));

        ImGui.TableNextRow();

        ImGui.TableNextColumn();
        using (var disabled = ImRaii.Disabled(!_isUnlocked))
        {
            ImGui.AlignTextToFramePadding();
            ImGui.TextDisabled("View:");
            ImGui.SameLine();
            if (CtrlHelper.Checkbox("Show Live Bones", ref _isShowLiveBones))
            {
                _configuration.EditorConfiguration.ShowLiveBones = _isShowLiveBones;
                _configuration.Save();
            }
            CtrlHelper.AddHoverText("If selected, present for editing all bones found in the game data,\nelse show only bones for which the profile already contains edits.");

            ImGui.SameLine();
            ImGui.BeginDisabled(!_isShowLiveBones);
            if (CtrlHelper.Checkbox("Mirror Mode", ref _isMirrorModeEnabled))
            {
                _configuration.EditorConfiguration.BoneMirroringEnabled = _isMirrorModeEnabled;
                _configuration.Save();
            }
            CtrlHelper.AddHoverText("Bone changes will be reflected from left to right and vice versa.");
            ImGui.EndDisabled();
        }

        ImGui.TableNextColumn();
        ImGui.AlignTextToFramePadding();
        ImGui.TextDisabled("Precision:");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(MathF.Min(220 * ImGuiHelpers.GlobalScale, ImGui.GetContentRegionAvail().X));
        if (ImGui.SliderInt("##Precision", ref _precision, 0, 6, $"{_precision} Place{(_precision == 1 ? "" : "s")}"))
        {
            _configuration.EditorConfiguration.EditorValuesPrecision = _precision;
            _configuration.Save();
        }
        CtrlHelper.AddHoverText("Level of precision to display while editing values.");
    }

    private void DrawEditorStatusStrip(string characterText)
    {
        var armature = GetPrimaryEditorArmature();
        var liveBoneNames = armature?.GetAllBones()
            .Select(b => b.BoneName)
            .Distinct(StringComparer.Ordinal)
            .ToArray() ?? Array.Empty<string>();

        var unknownBoneCount = liveBoneNames.Count(b => BoneData.GetBoneFamily(b) == BoneData.BoneFamily.Unknown);
        var ivcsBoneCount = liveBoneNames.Count(BoneData.IsIVCSCompatibleBone);
        var supportClass = GetSupportClass(liveBoneNames.Length, unknownBoneCount, ivcsBoneCount);
        var advancedStatus = GetAdvancedScalingStatus(armature);
        var characterArmatureStatus = !IsCharacterFound
            ? "No preview actor"
            : armature?.IsBuilt == true
                ? "Actor found / armature ready"
                : "Actor found / waiting for skeleton";
        var liveBonesStatus = liveBoneNames.Length > 0
            ? $"{liveBoneNames.Length} detected"
            : armature?.IsBuilt == true
                ? "Waiting for live bones"
                : "Waiting for armature";
        var previewText = characterText.StartsWith("Previewing on: ", StringComparison.Ordinal)
            ? characterText["Previewing on: ".Length..]
            : characterText;

        ImGui.Spacing();
        ImGui.TextDisabled("Editor status");
        ImGui.SameLine();
        ImGuiComponents.HelpMarker("A compact summary of the active preview context. Unknown/custom bones remain manual and experimental by default.");

        using var table = ImRaii.Table("TemplateEditorStatusStrip", 4, ImGuiTableFlags.BordersInnerV | ImGuiTableFlags.SizingStretchSame);
        if (!table)
            return;

        for (var i = 0; i < 4; i++)
            ImGui.TableSetupColumn($"Status{i}", ImGuiTableColumnFlags.WidthStretch);

        ImGui.TableNextRow();
        DrawStatusCell("Preview context", previewText, IsCharacterFound ? Constants.Colors.Active : Constants.Colors.Warning);
        DrawStatusCell("Character / armature", characterArmatureStatus,
            IsCharacterFound && armature?.IsBuilt == true ? Constants.Colors.Active : Constants.Colors.Warning);
        DrawStatusCell("Live bones", liveBonesStatus,
            liveBoneNames.Length > 0 ? Constants.Colors.Active : Constants.Colors.Warning);
        DrawStatusCell("Skeleton class", supportClass, unknownBoneCount > 0 ? Constants.Colors.Warning : Constants.Colors.Info);

        ImGui.TableNextRow();
        DrawStatusCell("Live display", _isShowLiveBones ? "Shown" : "Edited bones only", _isShowLiveBones ? Constants.Colors.Active : Constants.Colors.Normal);
        DrawStatusCell("Mirror mode", _isMirrorModeEnabled ? "Enabled" : "Disabled", _isMirrorModeEnabled ? Constants.Colors.Active : Constants.Colors.Normal);
        DrawStatusCell("Unknown bones", unknownBoneCount > 0 ? $"{unknownBoneCount} manual/experimental" : "None detected",
            unknownBoneCount > 0 ? Constants.Colors.Warning : Constants.Colors.Active);
        DrawStatusCell("Advanced scaling", advancedStatus,
            advancedStatus is "Off" or "Waiting for armature" ? Constants.Colors.Normal : Constants.Colors.Info);
    }

    private static void DrawStatusCell(string label, string value, Vector4 color)
    {
        ImGui.TableNextColumn();
        ImGui.TextDisabled(label);
        ImGui.PushStyleColor(ImGuiCol.Text, color);
        ImGuiUtil.TextWrapped(value);
        ImGui.PopStyleColor();
    }

    private void DrawProfileContextPreviewControl()
    {
        var enabled = _configuration.EditorConfiguration.PreviewWithProfileContext;
        var contextProfile = ResolveProfileContextPreviewProfile(enabled, out var unavailableReason);
        _editorManager.RefreshProfileContextPreview(enabled, contextProfile, unavailableReason);

        ImGui.AlignTextToFramePadding();
        if (ImGui.Checkbox("Preview with profile context", ref enabled))
        {
            _configuration.EditorConfiguration.PreviewWithProfileContext = enabled;
            _configuration.Save();
            contextProfile = ResolveProfileContextPreviewProfile(enabled, out unavailableReason);
            _editorManager.RefreshProfileContextPreview(enabled, contextProfile, unavailableReason);
        }
        CtrlHelper.AddHoverText("Shows other active templates from the preview actor's active profile while editing this template. Other templates are visual context only; edits are saved only to the current template.");

        ImGui.SameLine();
        var active = _editorManager.ProfileContextPreviewActive;
        var color = !enabled
            ? Constants.Colors.Normal
            : active
                ? Constants.Colors.Active
                : Constants.Colors.Warning;
        ImGui.TextDisabled("Profile context:");
        ImGui.SameLine();
        ImGui.PushStyleColor(ImGuiCol.Text, color);
        ImGui.TextUnformatted(_editorManager.ProfileContextPreviewStatus);
        ImGui.PopStyleColor();

        if (enabled)
            CtrlHelper.AddHoverText(active
                ? "The preview actor is using the active profile's enabled template order, with the edited template replaced by the temporary editable copy."
                : unavailableReason);
    }

    private Profile? ResolveProfileContextPreviewProfile(bool enabled, out string unavailableReason)
    {
        unavailableReason = "Off";
        if (!enabled)
            return null;

        if (!IsEditorActive)
        {
            unavailableReason = "editor is not active";
            return null;
        }

        var previewActor = _editorManager.Character;
        if (!previewActor.IsValid)
        {
            unavailableReason = "no preview actor";
            return null;
        }

        var activeProfile = _profileManager.GetEnabledProfilesByActor(previewActor)
            .FirstOrDefault(profile => profile.ProfileType != ProfileType.Editor);
        if (activeProfile == null)
        {
            unavailableReason = "no active profile for preview actor";
            return null;
        }

        var assignedTemplate = activeProfile.Templates.FirstOrDefault(template => template.UniqueId == _editorManager.CurrentlyEditedTemplateId);
        if (assignedTemplate == null)
        {
            unavailableReason = "selected template is not assigned to the preview actor's active profile";
            return null;
        }

        if (activeProfile.DisabledTemplates.Contains(assignedTemplate.UniqueId))
        {
            unavailableReason = "selected template is disabled in the preview actor's active profile";
            return null;
        }

        var enabledContextTemplateCount = activeProfile.Templates.Count(template =>
            template.UniqueId != assignedTemplate.UniqueId &&
            !activeProfile.DisabledTemplates.Contains(template.UniqueId));
        if (enabledContextTemplateCount <= 0)
        {
            unavailableReason = "no other enabled templates are assigned to the preview actor's active profile";
            return null;
        }

        unavailableReason = string.Empty;
        return activeProfile;
    }

    private static string GetSupportClass(int liveBoneCount, int unknownBoneCount, int ivcsBoneCount)
    {
        if (liveBoneCount <= 0)
            return "Waiting for live bones";

        return (unknownBoneCount, ivcsBoneCount) switch
        {
            (> 0, > 0) => "IVCS/modded + unknown",
            (> 0, _) => "Unknown/custom",
            (_, > 0) => "IVCS/modded",
            _ => "Known/vanilla"
        };
    }

    private static string GetAdvancedScalingStatus(Armature? armature)
    {
        var settings = armature?.ActiveAdvancedBodyScalingSettings;
        if (settings == null)
            return "Waiting for armature";

        if (!settings.Enabled)
            return "Off";

        return settings.Mode == AdvancedBodyScalingMode.Manual
            ? "Manual"
            : settings.Mode.ToString();
    }

    private void DrawGroupImportStatus()
    {
        if (string.IsNullOrWhiteSpace(_lastGroupImportStatus))
            return;

        var lifetimeMs = _lastGroupImportFailed
            ? GroupImportFailureStatusLifetimeMs
            : GroupImportSuccessStatusLifetimeMs;
        if (Environment.TickCount64 - _lastGroupImportStatusAtMs > lifetimeMs)
        {
            ClearGroupImportStatus();
            return;
        }

        ImGui.PushStyleColor(ImGuiCol.Text, _lastGroupImportFailed ? Constants.Colors.Warning : Constants.Colors.Active);
        ImGuiUtil.TextWrapped(_lastGroupImportFailed
            ? $"Group import failed: {_lastGroupImportStatus}"
            : _lastGroupImportStatus);
        ImGui.PopStyleColor();
    }

    private void SetGroupImportStatus(string? status, bool failed)
    {
        _lastGroupImportStatus = string.IsNullOrWhiteSpace(status) ? "Unknown group import result." : status;
        _lastGroupImportFailed = failed;
        _lastGroupImportStatusAtMs = Environment.TickCount64;
    }

    private void ClearGroupImportStatus()
    {
        _lastGroupImportStatus = null;
        _lastGroupImportFailed = false;
        _lastGroupImportStatusAtMs = 0;
    }

    private void DrawTroubleshootingHelper()
    {
        if (!ImGui.CollapsingHeader("Why didn't this move?"))
            return;

        var armature = GetPrimaryEditorArmature();
        var liveBoneNames = armature?.GetAllBones()
            .Select(b => b.BoneName)
            .Distinct(StringComparer.Ordinal)
            .ToHashSet(StringComparer.Ordinal) ?? new HashSet<string>(StringComparer.Ordinal);
        var templateBones = _editorManager.CurrentlyEditedTemplate?.Bones ?? _templateFileSystemSelector.Selected?.Bones;
        var lockedRows = templateBones?.Count(b => b.Value.LockState != BoneLockState.Unlocked) ?? 0;
        var pinnedAxes = templateBones?.Sum(b => (b.Value.PinX ? 1 : 0) + (b.Value.PinY ? 1 : 0) + (b.Value.PinZ ? 1 : 0)) ?? 0;
        var editedBones = templateBones?.Where(b => b.Value.IsEdited()).ToList() ?? new List<KeyValuePair<string, BoneTransform>>();
        var missingEditedBones = liveBoneNames.Count > 0
            ? editedBones.Count(b => !liveBoneNames.Contains(b.Key))
            : 0;
        var unknownBoneCount = liveBoneNames.Count(b => BoneData.GetBoneFamily(b) == BoneData.BoneFamily.Unknown);
        var ivcsBoneCount = liveBoneNames.Count(BoneData.IsIVCSCompatibleBone);

        ImGuiUtil.TextWrapped("Quick local checks for cases where a bone edit is visible in the table but does not visibly affect the actor:");

        if (!IsEditorActive)
            DrawWrappedBullet("The template editor is not active, so no live preview armature is currently being edited.");
        if (IsEditorPaused)
            DrawWrappedBullet("The editor is paused by the current game state. Resume a compatible state before testing movement.");
        if (!IsCharacterFound)
            DrawWrappedBullet("No active preview actor was found. Pick a visible actor or apply the editor to your current character.");
        if (armature?.IsBuilt != true)
            DrawWrappedBullet("No live armature is ready yet. Redraw, change preview actor, or wait for the actor skeleton to finish loading.");
        if (armature?.IsSkeletonBindingCurrent == false)
            DrawWrappedBullet("The live skeleton binding is being refreshed. Native writes are safely paused until the current topology is validated.");
        if (armature?.Profile.Enabled == false)
            DrawWrappedBullet("The active profile is disabled, so its template transforms are dormant.");
        if (armature?.GetCapabilityManifestSnapshot().CapabilityEvidence.Values.Any(static evidence => evidence.State is SkeletonCapabilityState.Partial or SkeletonCapabilityState.Ambiguous) == true)
            DrawWrappedBullet("One or more skeleton capabilities are partial or ambiguous. Capability-gated template content and automatic support may remain dormant until the required live controls are present.");
        if (missingEditedBones > 0)
            DrawWrappedBullet($"{missingEditedBones} edited bone{(missingEditedBones == 1 ? " is" : "s are")} not present on the current live skeleton.");
        if (unknownBoneCount > 0)
            DrawWrappedBullet($"{unknownBoneCount} unknown/custom bone{(unknownBoneCount == 1 ? " is" : "s are")} visible. Unknown bones are manual/experimental and are not trusted for automation by default.");
        if (ivcsBoneCount > 0)
            DrawWrappedBullet($"{ivcsBoneCount} IVCS/modded bone{(ivcsBoneCount == 1 ? " is" : "s are")} visible. These require compatible skeleton, body, and clothing weights to move reliably.");
        if (lockedRows > 0)
            DrawWrappedBullet($"{lockedRows} row lock{(lockedRows == 1 ? " is" : "s are")} active. Locked rows block automation and analyzer fixes.");
        if (pinnedAxes > 0)
            DrawWrappedBullet($"{pinnedAxes} pinned axis value{(pinnedAxes == 1 ? " is" : "s are")} active. Pins protect individual scale axes from automation.");
        if (armature?.DeformationQualityDiagnostics.Solver.FallbackCount > 0)
            DrawWrappedBullet($"{armature.DeformationQualityDiagnostics.Solver.FallbackCount} automatic support contribution(s) were skipped because live capability, trust, or model-influence evidence was unavailable. Explicit template edits are not discarded for this reason.");
        if (armature?.DeformationQualityDiagnostics.Solver.DoubleContributionPreventionCount > 0)
            DrawWrappedBullet("Automatic support at a shared region boundary was blended to prevent duplicate structural and secondary contributions from over-amplifying the same area.");
        if (armature?.DeformationQualityDiagnostics.Solver.ClampedContributionCount > 0)
            DrawWrappedBullet("An automatic contribution was rejected by finite-value/clamp safety. This does not change the saved template row.");
        if (editedBones.Count == 0)
            DrawWrappedBullet("This template has no effective bone edits yet. Identity/default transforms will not visibly move anything.");

        DrawWrappedBullet("If clothing does not move, the mesh may not be weighted to that bone even when the body is.");
        DrawWrappedBullet("Some helper, face, or GPose-oriented bones may be unreliable outside supported contexts. The plugin can expose them, but it cannot remove game-engine limits.");
        DrawWrappedBullet("Propagation only affects child bones when the propagation icon is enabled for the current edit mode.");
    }

    private void DrawActorHealth()
    {
        if (!ImGui.CollapsingHeader("Actor Health"))
            return;

        var armature = GetPrimaryEditorArmature();
        if (armature == null)
        {
            ImGui.TextDisabled("Waiting for a preview armature before actor health can be evaluated.");
            return;
        }

        var profile = armature.Profile;
        if (profile == null)
        {
            ImGui.TextDisabled("Waiting for an active profile before actor health can be evaluated.");
            return;
        }

        var applicability = ProfileTransformResolver.Resolve(profile, armature.GetCapabilityManifestSnapshot()).TemplateApplicability;
        var native = armature.GetDebugNativeWriteDiagnostics();
        var report = ActorHealthReport.Evaluate(new ActorHealthInput(
            HasProfile: true,
            ProfileEnabled: profile.Enabled,
            BindingCurrent: armature.IsSkeletonBindingCurrent,
            AppearanceTransitionPending: armature.IsAwaitingAppearanceContextRebind,
            NativeReacquisitionPending: armature.AppearanceEpochState.Contains("reacquisition", StringComparison.OrdinalIgnoreCase),
            DormantTemplateCount: applicability.Count(static item => item.Enabled && !item.Active),
            StaleWrites: native.SkippedStaleBinding,
            UnsafeWrites: native.SkippedUnsafeTransform,
            BindingIssue: armature.LastSkeletonBindingIssue));
        var color = report.State switch
        {
            ActorHealthState.Healthy => Constants.Colors.Active,
            ActorHealthState.TemporarilyWaiting => Constants.Colors.Warning,
            ActorHealthState.LimitedCompatibility => Constants.Colors.Warning,
            _ => Constants.Colors.Error,
        };
        ImGui.PushStyleColor(ImGuiCol.Text, color);
        ImGui.TextUnformatted(report.State.ToString());
        ImGui.PopStyleColor();
        ImGuiUtil.TextWrapped(report.Summary);
        foreach (var detail in report.Details)
            DrawWrappedBullet(detail);
        foreach (var layer in armature.GetOptionalLayerHealthSnapshot().Where(static item => item.HasFailure))
        {
            var recovery = layer.Recovered ? "recovered; informational" : "currently being contained";
            DrawWrappedBullet($"Optional layer {layer.Layer}: last failure {layer.MostRecentFailureType}; {layer.RepeatedFailureCount} occurrence(s) in the recent window; {recovery}.");
        }
        ImGui.TextDisabled($"Binding: {(armature.IsSkeletonBindingCurrent ? "current" : "waiting")}; body-shaping revision {armature.DeformationRevision}; BIW: {armature.ActiveBoneImportanceResult.SourceLabel}.");
    }

    private void DrawBoneExplainabilityInspector()
    {
        if (!ImGui.CollapsingHeader("Bone Explainability"))
            return;

        ImGuiUtil.TextWrapped("Explain a published bone transform or why an automatic layer intentionally did not change it. Inspection is read-only and never traces every frame.");
        ImGui.SetNextItemWidth(MathF.Min(340 * ImGuiHelpers.GlobalScale, ImGui.GetContentRegionAvail().X));
        ImGui.InputTextWithHint("##BoneExplainName", "Bone code name, for example j_ude_a_l", ref _inspectedBoneName, 96);
        if (string.IsNullOrWhiteSpace(_inspectedBoneName) && !string.IsNullOrWhiteSpace(_boneSearch))
            ImGui.TextDisabled("Tip: enter a bone code, or use the row/Unknown Workbench Inspect action.");
        if (string.IsNullOrWhiteSpace(_inspectedBoneName))
            return;

        var report = _boneExplainabilityService.Explain(GetPrimaryEditorArmature(), _editorManager.CurrentlyEditedTemplate, _inspectedBoneName.Trim());
        ImGui.TextUnformatted($"{report.DisplayName} ({report.CanonicalName})");
        ImGui.TextDisabled($"Origin: {report.Metadata.Origin}; Role: {report.Metadata.Role}; Trust: {report.Metadata.Trust}; Live: {(report.IsLive ? "yes" : "no")}");
        ImGuiUtil.TextWrapped(report.Summary);
        if (report.Reasons.Count > 0)
        {
            ImGui.TextDisabled("Why it may not move automatically:");
            foreach (var reason in report.Reasons)
                DrawWrappedBullet(DescribeReason(reason));
        }
        if (ImGui.TreeNode("Detailed transform stages"))
        {
            foreach (var stage in report.Stages.Where(static stage => stage.IsActive))
            {
                var value = stage.Kind == BoneTransformStageKind.Factor
                    ? $"{stage.Value.X:0.000}"
                    : $"{stage.Value.X:0.000}, {stage.Value.Y:0.000}, {stage.Value.Z:0.000}";
                ImGui.TextUnformatted($"{stage.Name}: {value} - {stage.Detail}");
            }
            ImGui.TextDisabled($"Parent live/curated: {report.LiveParent ?? "none"} / {report.CuratedParent ?? "none"}; Mirror: {report.Mirror ?? "none"}; BIW: {(report.BoneImportance?.ToString("0.00") ?? "n/a")}");
            ImGui.TreePop();
        }
    }

    private void DrawCompareAndCompatibilityTools()
    {
        if (!ImGui.CollapsingHeader("Compare / Compatibility Preview"))
            return;

        var edited = _editorManager.CurrentlyEditedTemplate;
        if (edited == null)
        {
            ImGui.TextDisabled("Start bone editing to compare the temporary editable template.");
            return;
        }

        var candidates = _templateManager.Templates.Where(template => template.UniqueId != _editorManager.CurrentlyEditedTemplateId).OrderBy(template => template.Name.Text, StringComparer.Ordinal).ToArray();
        if (candidates.Length > 0)
        {
            var selected = candidates.FirstOrDefault(template => template.UniqueId == _compareTemplateId) ?? candidates[0];
            _compareTemplateId = selected.UniqueId;
            ImGui.SetNextItemWidth(MathF.Min(360 * ImGuiHelpers.GlobalScale, ImGui.GetContentRegionAvail().X));
            if (ImGui.BeginCombo("Compare template", selected.Name.Text))
            {
                foreach (var template in candidates)
                {
                    if (ImGui.Selectable(template.Name.Text, template.UniqueId == _compareTemplateId))
                        _compareTemplateId = template.UniqueId;
                }
                ImGui.EndCombo();
            }
            if (ImGui.Button("Build template diff"))
                _templateDiffReport = TemplateDiffService.Compare(selected, edited);
            ImGui.SameLine();
            if (ImGui.Button("Copy changed source -> edited") && _templateDiffReport != null)
            {
                var state = TemplateDiffService.CopyFrom(_editorManager.CaptureCurrentTemplateState(),
                    _templateDiffReport.Rows.Where(static row => row.Kind is TemplateDiffKind.Changed or TemplateDiffKind.OnlyLeft), true, true, true, true, true);
                _editorManager.BeginEditTransaction("Apply template diff selection");
                _editorManager.ReplaceEditedTemplateState(state);
                _editorManager.CommitEditTransaction();
            }
            CtrlHelper.AddHoverText("Copies changed source rows into the currently edited temporary template. The change is undoable and does not modify the source template.");
            if (_templateDiffReport != null)
            {
                ImGui.TextDisabled($"Shared {_templateDiffReport.SharedCount}; changed {_templateDiffReport.ChangedCount}; source-only {_templateDiffReport.OnlyLeftCount}; edited-only {_templateDiffReport.OnlyRightCount}.");
                foreach (var (family, delta) in _templateDiffReport.RegionScaleDeltas.OrderBy(static item => item.Key.ToString(), StringComparer.Ordinal))
                    ImGui.TextDisabled($"{family}: transform scale delta {delta.X:0.000}, {delta.Y:0.000}, {delta.Z:0.000}");
            }
        }
        else
            ImGui.TextDisabled("Create another saved template to enable template-to-template comparison.");

        var armature = GetPrimaryEditorArmature();
        if (armature != null && ImGui.TreeNode("Compatibility preview"))
        {
            var report = CompatibilityPreviewService.Preview(armature.Profile, armature.GetCapabilityManifestSnapshot());
            ImGui.TextUnformatted(report.IsSafePartialCompatibility ? "Safe Partial Compatibility" : "Compatibility preview");
            ImGui.TextDisabled($"Authored {report.TotalAuthoredEntries}; directly present {report.DirectlyPresentEntries}; dormant {report.DormantEntries}; known but absent {report.KnownButAbsentEntries}; manual {report.ManualOnlyEntries}; excluded {report.ExcludedEntries}; unknown {report.UnknownEntries}; unavailable {report.UnavailableEntries}.");
            foreach (var row in report.Rows)
                ImGui.TextUnformatted($"{row.TemplateName}: {(row.Active ? "active" : "dormant")} - {row.Reason}; present {row.DirectlyPresent}, absent {row.KnownButAbsent}, manual {row.ManualOnly}, excluded {row.ExcludedFromAutomation}, unknown {row.Unknown}.");
            ImGui.TreePop();
        }

        if (armature != null && ImGui.TreeNode("Profile-to-profile diff"))
        {
            var profiles = _profileManager.Profiles
                .Where(profile => profile.UniqueId != armature.Profile?.UniqueId)
                .OrderBy(profile => profile.Name.Text, StringComparer.Ordinal)
                .ToArray();
            if (profiles.Length == 0 || armature.Profile == null)
                ImGui.TextDisabled("Assign another profile to enable a semantic profile comparison.");
            else
            {
                var selected = profiles.FirstOrDefault(profile => profile.UniqueId == _compareProfileId) ?? profiles[0];
                _compareProfileId = selected.UniqueId;
                ImGui.SetNextItemWidth(MathF.Min(360 * ImGuiHelpers.GlobalScale, ImGui.GetContentRegionAvail().X));
                if (ImGui.BeginCombo("Compare profile", selected.Name.Text))
                {
                    foreach (var profile in profiles)
                    {
                        if (ImGui.Selectable(profile.Name.Text, profile.UniqueId == _compareProfileId))
                            _compareProfileId = profile.UniqueId;
                    }
                    ImGui.EndCombo();
                }

                if (ImGui.Button("Build profile diff"))
                    _profileDiffReport = ProfileDiffService.Compare(selected, armature.Profile);
                if (_profileDiffReport != null)
                {
                    ImGui.TextDisabled($"Assignments {_profileDiffReport.Templates.Count}; priority changed: {_profileDiffReport.PriorityChanged}; advanced overrides changed: {_profileDiffReport.AdvancedOverridesChanged}.");
                    foreach (var row in _profileDiffReport.Templates.Where(static row => !row.ExistsLeft || !row.ExistsRight || row.EnabledLeft != row.EnabledRight || MathF.Abs(row.WeightLeft - row.WeightRight) > 0.0001f || row.RequirementLeft != row.RequirementRight).Take(12))
                        ImGui.TextUnformatted($"{row.TemplateName}: source {(row.EnabledLeft ? "on" : "off")} {row.WeightLeft:0.00} / active {(row.EnabledRight ? "on" : "off")} {row.WeightRight:0.00}");
                }
            }
            ImGui.TreePop();
        }
    }

    private void DrawSolverAbPreview()
    {
        if (!ImGui.CollapsingHeader("Solver A/B Preview"))
            return;

        var armature = GetPrimaryEditorArmature();
        ImGuiUtil.TextWrapped("Runs the current resolver and Advanced Body Shaping conditioning stack on copied managed transforms. It does not install a live override, save settings, replace a profile, or write native transforms.");
        using (ImRaii.Disabled(armature == null))
        {
            if (ImGui.Button("Compare current vs Naturalization Off"))
            {
                _solverPreview = armature == null
                    ? SolverPreviewResult.Unavailable("Waiting for a preview armature.")
                    : SolverPreviewService.CompareCurrentToNaturalizationOff(armature.Profile, armature.GetCapabilityManifestSnapshot(), armature.ActiveAdvancedBodyScalingSettings, armature.ActiveBoneImportanceResult, armature.GetAllBones().Select(static bone => bone.BoneName));
            }
        }
        if (_solverPreview == null)
            return;
        ImGui.TextDisabled($"{_solverPreview.Mode}; changed automatic targets {_solverPreview.ChangedBoneCount}; saved state mutated: no.");
        foreach (var row in _solverPreview.Rows.Take(12))
            ImGui.TextUnformatted($"{row.BoneName}: current {row.CurrentScale.X:0.000} / baseline {row.BaselineScale.X:0.000} / delta {row.Delta.X:0.000}");
    }

    private void DrawRegionBatchTools()
    {
        if (!ImGui.CollapsingHeader("Region / Batch Editing"))
            return;

        var template = _editorManager.CurrentlyEditedTemplate;
        if (template == null)
        {
            ImGui.TextDisabled("Start bone editing to use region tools.");
            return;
        }

        _selectedAuthoringRegion = Math.Clamp(_selectedAuthoringRegion, 0, RegionBatchEditService.Regions.Count - 1);
        var region = RegionBatchEditService.Regions[_selectedAuthoringRegion];
        if (ImGui.BeginCombo("Region", region.Name))
        {
            for (var index = 0; index < RegionBatchEditService.Regions.Count; index++)
            {
                if (ImGui.Selectable(RegionBatchEditService.Regions[index].Name, index == _selectedAuthoringRegion))
                    _selectedAuthoringRegion = index;
            }
            ImGui.EndCombo();
        }
        if (ImGui.BeginCombo("Scope", _selectedAuthoringScope.ToString()))
        {
            foreach (var scope in Enum.GetValues<AuthoringRegionScope>())
            {
                if (ImGui.Selectable(scope.ToString(), scope == _selectedAuthoringScope))
                    _selectedAuthoringScope = scope;
            }
            ImGui.EndCombo();
        }
        var live = GetPrimaryEditorArmature()?.GetAllBones().Select(static bone => bone.BoneName);
        var bones = RegionBatchEditService.GetEligibleBones(region, _selectedAuthoringScope, live);
        ImGui.TextDisabled($"{bones.Count} eligible curated body bone(s); clothing, props, appendages, and unknown/manual bones are excluded.");
        var multiplier = 1.05f;
        ImGui.SetNextItemWidth(120 * ImGuiHelpers.GlobalScale);
        ImGui.DragFloat("Uniform scale", ref multiplier, 0.005f, 0.5f, 1.5f, "%.3f");
        if (ImGui.Button("Scale region"))
        {
            var state = RegionBatchEditService.Scale(_editorManager.CaptureCurrentTemplateState(), bones, new Vector3(multiplier), out var skipped);
            _editorManager.BeginEditTransaction($"Scale {region.Name}");
            _editorManager.ReplaceEditedTemplateState(state);
            _editorManager.CommitEditTransaction();
            _semanticGoalStatus = $"Scaled {bones.Count - skipped} {region.Name} bone(s); skipped {skipped} locked row(s).";
        }
        ImGui.SameLine();
        if (ImGui.Button("Mirror Left -> Right"))
        {
            var state = RegionBatchEditService.Mirror(_editorManager.CaptureCurrentTemplateState(), bones, true, true, true, true, true, out var skipped);
            _editorManager.BeginEditTransaction($"Mirror {region.Name} left to right");
            _editorManager.ReplaceEditedTemplateState(state);
            _editorManager.CommitEditTransaction();
            _semanticGoalStatus = $"Mirrored trusted {region.Name} rows; skipped {skipped} row(s) without curated mirror metadata.";
        }
        ImGui.SameLine();
        if (ImGui.Button("Reset region"))
        {
            var state = _editorManager.CaptureCurrentTemplateState();
            foreach (var bone in bones)
                state.Remove(bone);
            _editorManager.BeginEditTransaction($"Reset {region.Name}");
            _editorManager.ReplaceEditedTemplateState(state);
            _editorManager.CommitEditTransaction();
        }
    }

    private static string DescribeReason(BoneExplainabilityReasonCode reason)
        => reason switch
        {
            BoneExplainabilityReasonCode.ClothingExcluded => "Role: Clothing rig. Automatic body deformation deliberately stops before clothing controls.",
            BoneExplainabilityReasonCode.PropExcluded => "Role: prop/gear rig. Automatic body deformation deliberately stops before prop controls.",
            BoneExplainabilityReasonCode.UnknownCustom => "Origin: unknown/custom. The control remains manual/experimental until curated support exists.",
            BoneExplainabilityReasonCode.ManualOnly => "Automation: manual-only. No automatic propagation or deformation is granted.",
            BoneExplainabilityReasonCode.AxisLocked => "The active template locks this bone, so automatic changes are blocked.",
            BoneExplainabilityReasonCode.AxisPinned => "One or more scale axes are pinned by the active template.",
            BoneExplainabilityReasonCode.BindingNotCurrent => "The live skeleton binding is not yet validated; safety blocks native writes until it is current.",
            BoneExplainabilityReasonCode.AppearanceTransitionPending => "Appearance transition is still settling. The active profile remains intact while safe reacquisition completes.",
            BoneExplainabilityReasonCode.CapabilityMissing => "The required skeleton capability is not present on this live target, so this compatibility-dependent control stays dormant.",
            BoneExplainabilityReasonCode.CompatibilityDormant => "The assigned template is stored safely but dormant until its profile compatibility requirement is met.",
            BoneExplainabilityReasonCode.NativeSafetyBlocked => "A current native-safety check blocked a write while the live binding is invalid. The transform was not applied unsafely.",
            BoneExplainabilityReasonCode.BIWAttenuated => "Model influence is low, so optional automatic correction is conservatively attenuated.",
            BoneExplainabilityReasonCode.NoModelInfluence => "No meaningful model influence was available for this bone.",
            BoneExplainabilityReasonCode.SolverDisabled => "Advanced Body Scaling is disabled or in Manual mode for this actor.",
            BoneExplainabilityReasonCode.ExplicitAuthority => "An explicit template row is authoritative; automatic receivers do not overwrite it.",
            _ => reason.ToString(),
        };

    private void DrawTemplateHealth()
    {
        if (!ImGui.CollapsingHeader("Template Health / Delta Details"))
            return;

        var templateBones = _editorManager.CurrentlyEditedTemplate?.Bones
                            ?? _templateFileSystemSelector.Selected?.Bones
                            ?? new Dictionary<string, BoneTransform>();
        var armature = GetPrimaryEditorArmature();
        var liveBoneNames = armature?.GetAllBones()
            .Select(b => b.BoneName)
            .Distinct(StringComparer.Ordinal)
            .ToHashSet(StringComparer.Ordinal) ?? new HashSet<string>(StringComparer.Ordinal);
        var signature = BuildTemplateHealthSignature(templateBones, liveBoneNames);

        if (_templateHealthReport == null || _templateHealthReport.Signature != signature)
            _templateHealthReport = BuildTemplateHealthReport(templateBones, liveBoneNames, signature);

        ImGuiUtil.TextWrapped("Read-only details for the current template and preview skeleton. This is diagnostic context only; it does not apply fixes or modify template data.");
        ImGui.TextDisabled("Missing-live and asymmetry checks use the current preview actor. Metadata remains advisory and does not grant mirroring, parenting, propagation, BIW, guardrail, or automation trust.");

        if (ImGui.Button("Refresh Template Health"))
            _templateHealthReport = BuildTemplateHealthReport(templateBones, liveBoneNames, signature);
        CtrlHelper.AddHoverText("Rebuilds the read-only health report from the current template, live preview skeleton, and local metadata notes.");

        ImGui.SameLine();
        if (ImGui.Button("Clear health filters"))
        {
            _templateHealthSearch = string.Empty;
            _templateHealthEditedOnly = true;
            _templateHealthMissingOnly = false;
            _templateHealthUnknownOnly = false;
            _templateHealthRiskyOnly = false;
            _templateHealthAsymmetricOnly = false;
            _templateHealthLockedPinnedOnly = false;
            _templateHealthPropagatedOnly = false;
        }

        var report = _templateHealthReport;
        if (report == null)
            return;

        DrawTemplateHealthSummary(report.Summary);
        DrawProportionDashboard(report.ProportionDashboard);
        DrawTemplateHealthFilters();

        var rows = report.Rows
            .Where(PassesTemplateHealthFilters)
            .OrderByDescending(r => r.IsEdited)
            .ThenByDescending(r => r.IsRisky)
            .ThenByDescending(r => r.IsUnknown)
            .ThenBy(r => BoneData.GetBoneRanking(r.BoneName))
            .ThenBy(r => r.BoneName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        ImGui.TextDisabled($"Showing {rows.Count} of {report.Rows.Count} delta row{(report.Rows.Count == 1 ? string.Empty : "s")}. Scroll the table for long reports.");

        var tableHeight = GetHelperScrollHeight(rows.Count, 260, 150);
        using (var table = ImRaii.Table("TemplateHealthDeltaTable", 9, ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerV | ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.ScrollY, new Vector2(0, tableHeight)))
        {
            if (!table)
                return;

            ImGui.TableSetupColumn("Bone", ImGuiTableColumnFlags.WidthFixed, 180 * ImGuiHelpers.GlobalScale);
            ImGui.TableSetupColumn("Family", ImGuiTableColumnFlags.WidthFixed, 95 * ImGuiHelpers.GlobalScale);
            ImGui.TableSetupColumn("Support", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupColumn("Template", ImGuiTableColumnFlags.WidthFixed, 85 * ImGuiHelpers.GlobalScale);
            ImGui.TableSetupColumn("Live", ImGuiTableColumnFlags.WidthFixed, 65 * ImGuiHelpers.GlobalScale);
            ImGui.TableSetupColumn("Deltas", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupColumn("Protect", ImGuiTableColumnFlags.WidthFixed, 105 * ImGuiHelpers.GlobalScale);
            ImGui.TableSetupColumn("Prop", ImGuiTableColumnFlags.WidthFixed, 85 * ImGuiHelpers.GlobalScale);
            ImGui.TableSetupColumn("Notes", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableHeadersRow();

            foreach (var row in rows.Take(80))
            {
                ImGui.TableNextRow();

                ImGui.TableNextColumn();
                ImGui.TextUnformatted(row.DisplayName);
                if (!string.Equals(row.DisplayName, row.BoneName, StringComparison.Ordinal))
                    ImGui.TextDisabled(row.BoneName);

                ImGui.TableNextColumn();
                ImGui.TextUnformatted(row.Family);

                ImGui.TableNextColumn();
                ImGuiUtil.TextWrapped(row.SupportLabel);

                ImGui.TableNextColumn();
                ImGui.TextUnformatted(row.IsEdited
                    ? "Edited"
                    : row.InTemplate
                        ? "Stored"
                        : "Live only");

                ImGui.TableNextColumn();
                ImGui.TextUnformatted(row.InLiveSkeleton ? "Yes" : liveBoneNames.Count == 0 ? "Waiting" : "No");

                ImGui.TableNextColumn();
                ImGuiUtil.TextWrapped($"P {FormatDelta(row.PositionDelta)} / R {FormatDelta(row.RotationDelta)} / S {FormatDelta(row.ScaleDelta)} / C {FormatDelta(row.ChildScaleDelta)}");

                ImGui.TableNextColumn();
                ImGui.TextUnformatted(row.ProtectionSummary);

                ImGui.TableNextColumn();
                ImGui.TextUnformatted(row.PropagationSummary);

                ImGui.TableNextColumn();
                ImGuiUtil.TextWrapped(row.Note);
            }
        }

        if (rows.Count > 80)
            ImGui.TextDisabled($"...and {rows.Count - 80} more row{(rows.Count - 80 == 1 ? string.Empty : "s")}. Use filters to narrow the list.");
    }

    private void DrawSemanticBodyGoals()
    {
        if (!ImGui.CollapsingHeader("Semantic Body Goals"))
            return;

        ImGuiUtil.TextWrapped("Semantic Body Goals are authoring helpers. They preview ordinary bone transform edits on known supported bones. They do not inspect meshes, do not auto-fix templates, and may not match your artistic intent.");
        ImGui.TextDisabled("These presets are starting points, not corrections. Review the preview and fine-tune manually.");
        ImGui.TextDisabled("Scale-only MVP: known built-in default bones only. Unknown/custom, metadata-trusted, and modded/IVCS bones are not used.");

        DrawShapeRecipeSelector();
        DrawSemanticGoalSliders();
        DrawSemanticGoalActions();

        if (!string.IsNullOrWhiteSpace(_semanticGoalStatus))
            ImGuiUtil.TextWrapped(_semanticGoalStatus);

        DrawSemanticGoalPreviewRows();
    }

    private void DrawShapeRecipeSelector()
    {
        var selectedRecipe = _semanticBodyGoalService.Recipes.FirstOrDefault(recipe => recipe.Id == _selectedShapeRecipeId)
                             ?? _semanticBodyGoalService.Recipes.FirstOrDefault();
        if (selectedRecipe != null && string.IsNullOrWhiteSpace(_selectedShapeRecipeId))
            _selectedShapeRecipeId = selectedRecipe.Id;

        ImGui.AlignTextToFramePadding();
        ImGui.TextDisabled("Recipe:");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(MathF.Min(280 * ImGuiHelpers.GlobalScale, ImGui.GetContentRegionAvail().X));
        if (ImGui.BeginCombo("##ShapeRecipeSelector", selectedRecipe?.DisplayName ?? "No recipes"))
        {
            foreach (var recipe in _semanticBodyGoalService.Recipes)
            {
                var selected = recipe.Id == _selectedShapeRecipeId;
                if (ImGui.Selectable(recipe.DisplayName, selected))
                    _selectedShapeRecipeId = recipe.Id;
                CtrlHelper.AddHoverText(recipe.Description);

                if (selected)
                    ImGui.SetItemDefaultFocus();
            }

            ImGui.EndCombo();
        }
        CtrlHelper.AddHoverText("Recipes only populate semantic sliders. They do not write template data until you preview and apply.");

        ImGui.SameLine();
        using (ImRaii.Disabled(selectedRecipe == null))
        {
            if (ImGui.Button("Load recipe values") && selectedRecipe != null)
            {
                _semanticGoalValues = _semanticBodyGoalService.CreateRecipeGoalValues(selectedRecipe);
                _loadedShapeRecipeName = selectedRecipe.DisplayName;
                _semanticGoalPreview = null;
                _semanticGoalStatus = $"Loaded '{selectedRecipe.DisplayName}' slider values. Click Preview Goals before applying.";
            }
        }
        CtrlHelper.AddHoverText("Loads this recipe into the semantic sliders. This is local UI state only and does not modify the template.");

        ImGui.SameLine();
        if (ImGui.Button("Reset sliders"))
        {
            _semanticGoalValues = _semanticBodyGoalService.CreateDefaultGoalValues();
            _loadedShapeRecipeName = null;
            _semanticGoalPreview = null;
            _semanticGoalStatus = "Reset semantic goal sliders.";
        }
        CtrlHelper.AddHoverText("Returns all semantic goal sliders to zero and clears the current preview.");
    }

    private void DrawSemanticGoalSliders()
    {
        using var table = ImRaii.Table("SemanticGoalSliders", 2, ImGuiTableFlags.BordersInnerV | ImGuiTableFlags.SizingStretchProp);
        if (!table)
            return;

        ImGui.TableSetupColumn("Goal", ImGuiTableColumnFlags.WidthFixed, 210 * ImGuiHelpers.GlobalScale);
        ImGui.TableSetupColumn("Value", ImGuiTableColumnFlags.WidthStretch);

        foreach (var goal in _semanticBodyGoalService.Goals)
        {
            if (!_semanticGoalValues.TryGetValue(goal.Id, out var value))
                value = 0f;

            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.AlignTextToFramePadding();
            ImGui.TextUnformatted(goal.DisplayName);
            CtrlHelper.AddHoverText(goal.Description);

            ImGui.TableNextColumn();
            ImGui.SetNextItemWidth(-1);
            if (ImGui.SliderFloat($"##SemanticGoal{goal.Id}", ref value, -1f, 1f, "%.2f"))
            {
                _semanticGoalValues[goal.Id] = Math.Clamp(value, -1f, 1f);
                _loadedShapeRecipeName = null;
                _semanticGoalPreview = null;
                _semanticGoalStatus = "Slider changed. Click Preview Goals to inspect the new output.";
            }
        }
    }

    private void DrawSemanticGoalActions()
    {
        var canPreview = IsEditorActive && !IsEditorPaused && _semanticGoalValues.Any(kvp => MathF.Abs(kvp.Value) > 0.0005f);
        var previewIsStale = IsSemanticGoalPreviewStale();
        using (ImRaii.Disabled(!canPreview))
        {
            if (ImGui.Button("Preview Goals"))
            {
                _semanticGoalPreview = BuildSemanticGoalPreview();
                _semanticGoalStatus = _semanticGoalPreview.Rows.Count == 0
                    ? "No semantic goals are active."
                    : $"Preview built: {_semanticGoalPreview.PreviewedChangeCount} change{(_semanticGoalPreview.PreviewedChangeCount == 1 ? string.Empty : "s")} previewed, {_semanticGoalPreview.BlockedChangeCount} blocked/skipped.";
            }
        }
        CtrlHelper.AddHoverText("Builds a read-only preview of ordinary scale edits. No template data is changed.");

        ImGui.SameLine();
        using (ImRaii.Disabled(_semanticGoalPreview == null))
        {
            if (ImGui.Button("Clear Preview"))
            {
                _semanticGoalPreview = null;
                _semanticGoalStatus = "Cleared semantic goal preview.";
            }
        }
        CtrlHelper.AddHoverText("Discards the current preview without changing the template.");

        ImGui.SameLine();
        var canApply = IsEditorActive && !IsEditorPaused && _semanticGoalPreview?.HasPreviewableChanges == true && !previewIsStale;
        using (ImRaii.Disabled(!canApply))
        {
            if (ImGui.Button("Apply Goals"))
                ApplySemanticGoalPreview();
        }
        CtrlHelper.AddHoverText("Writes the previewed final scales as ordinary BoneTransform edits through the existing editor path. This is the only semantic action that changes the template.");

        ImGui.SameLine();
        var canRevert = CanRevertSemanticGoals();
        using (ImRaii.Disabled(!canRevert))
        {
            if (ImGui.Button("Revert Goals"))
                RevertSemanticGoals();
        }
        CtrlHelper.AddHoverText("Reverts only the last applied Goals rows if they are unchanged. Otherwise use Undo/Redo.");

        if (!IsEditorActive || IsEditorPaused)
            ImGui.TextDisabled("Start bone editing and wait for the editor to be active before previewing or applying semantic goals.");
        else if (previewIsStale)
            ImGui.TextDisabled("Preview is stale. Rebuild Preview Goals before applying.");
    }

    private SemanticBodyGoalPreview BuildSemanticGoalPreview()
    {
        var templateBones = _editorManager.CurrentlyEditedTemplate?.Bones
                            ?? _templateFileSystemSelector.Selected?.Bones
                            ?? new Dictionary<string, BoneTransform>();
        var liveBoneNames = GetPrimaryEditorArmature()?.GetAllBones()
            .Select(b => b.BoneName)
            .Distinct(StringComparer.Ordinal)
            .ToHashSet(StringComparer.Ordinal) ?? new HashSet<string>(StringComparer.Ordinal);

        var signature = BuildSemanticGoalPreviewSignature(templateBones, liveBoneNames);
        return _semanticBodyGoalService.BuildPreview(_semanticGoalValues, templateBones, liveBoneNames, signature);
    }

    private void ApplySemanticGoalPreview()
    {
        var preview = _semanticGoalPreview;
        if (preview?.HasPreviewableChanges != true)
            return;

        if (IsSemanticGoalPreviewStale())
        {
            _semanticGoalStatus = "Preview is stale. Rebuild Preview Goals before applying.";
            return;
        }

        var operation = TemplateAuthoringOperation.Create(
            "Apply Semantic Body Goals",
            _editorManager.CaptureCurrentTemplateState(),
            preview.FinalTransforms);
        if (operation == null)
        {
            _semanticGoalStatus = "Apply did not change any template rows. Rebuild the preview if the template changed.";
            return;
        }

        using (_activityLogService.SuppressTemplateBoneEditEvents())
        {
            if (!_editorManager.TryApplyAuthoringOperation(operation))
            {
                _semanticGoalStatus = "Apply did not change any template rows. Rebuild the preview if the template changed.";
                return;
            }
        }

        _lastSemanticGoalOperation = operation;
        _lastSemanticGoalSessionId = _editorManager.EditorSessionId;
        _commitHistoryAfterWrite = false;
        _templateHealthReport = null;
        _semanticGoalPreview = null;
        _semanticGoalStatus = $"Applied semantic goals to {preview.FinalTransforms.Count} bone row{(preview.FinalTransforms.Count == 1 ? string.Empty : "s")}. Undo/Revert Goals is available; rebuild the preview before intentionally applying another relative adjustment.";
        var recipeSuffix = string.IsNullOrWhiteSpace(_loadedShapeRecipeName) ? string.Empty : $" from '{_loadedShapeRecipeName}'";
        _activityLogService.Record(
            ActivityLogCategory.SemanticGoals,
            "Applied goals",
            $"Applied semantic goals{recipeSuffix} to {preview.FinalTransforms.Count} bone row{(preview.FinalTransforms.Count == 1 ? string.Empty : "s")}.");
    }

    private bool CanRevertSemanticGoals()
        => IsEditorActive
            && !IsEditorPaused
            && _lastSemanticGoalOperation != null
            && _lastSemanticGoalSessionId == _editorManager.EditorSessionId
            && _lastSemanticGoalOperation.TryCreateRevert(
                _editorManager.CaptureCurrentTemplateState(),
                "Revert Semantic Body Goals",
                out _);

    private void RevertSemanticGoals()
    {
        if (_lastSemanticGoalOperation == null)
            return;

        using (_activityLogService.SuppressTemplateBoneEditEvents())
        {
            if (!_editorManager.TryRevertAuthoringOperation(_lastSemanticGoalOperation, "Revert Semantic Body Goals"))
            {
                _semanticGoalStatus = "Revert Goals is unavailable because an affected row changed. Use the Bone Editor Undo/Redo history instead.";
                return;
            }
        }

        _lastSemanticGoalOperation = null;
        _lastSemanticGoalSessionId = Guid.Empty;
        _commitHistoryAfterWrite = false;
        _templateHealthReport = null;
        _semanticGoalStatus = "Reverted the last applied semantic goals without changing unrelated rows.";
        _activityLogService.Record(
            ActivityLogCategory.SemanticGoals,
            "Reverted goals",
            "Reverted the last applied semantic goals without changing unrelated rows.");
    }

    private bool IsSemanticGoalPreviewStale()
    {
        var preview = _semanticGoalPreview;
        if (preview == null)
            return false;

        var templateBones = _editorManager.CurrentlyEditedTemplate?.Bones
                            ?? _templateFileSystemSelector.Selected?.Bones
                            ?? new Dictionary<string, BoneTransform>();
        var liveBoneNames = GetPrimaryEditorArmature()?.GetAllBones()
            .Select(b => b.BoneName)
            .Distinct(StringComparer.Ordinal)
            .ToHashSet(StringComparer.Ordinal) ?? new HashSet<string>(StringComparer.Ordinal);

        return preview.Signature != BuildSemanticGoalPreviewSignature(templateBones, liveBoneNames);
    }

    private int BuildSemanticGoalPreviewSignature(
        IReadOnlyDictionary<string, BoneTransform> templateBones,
        IReadOnlySet<string> liveBoneNames)
    {
        var hash = new HashCode();
        hash.Add(_selectedShapeRecipeId, StringComparer.Ordinal);

        foreach (var goal in _semanticBodyGoalService.Goals.OrderBy(goal => goal.Id, StringComparer.Ordinal))
        {
            hash.Add(goal.Id, StringComparer.Ordinal);
            hash.Add(_semanticGoalValues.TryGetValue(goal.Id, out var value) ? MathF.Round(value, 4) : 0f);
        }

        foreach (var boneName in GetSemanticGoalTargetBoneNames())
        {
            hash.Add(boneName, StringComparer.Ordinal);
            hash.Add(liveBoneNames.Contains(boneName));

            if (!templateBones.TryGetValue(boneName, out var transform) || transform == null)
            {
                hash.Add("missing", StringComparer.Ordinal);
                continue;
            }

            hash.Add(transform.Scaling);
            hash.Add(transform.ChildScaling);
            hash.Add(transform.ChildScalingIndependent);
            hash.Add(transform.PropagateScale);
            hash.Add(transform.PropagationFalloff);
            hash.Add(transform.LockState);
            hash.Add(transform.PinX);
            hash.Add(transform.PinY);
            hash.Add(transform.PinZ);
        }

        return hash.ToHashCode();
    }

    private IReadOnlyList<string> GetSemanticGoalTargetBoneNames()
        => _semanticBodyGoalService.Goals
            .SelectMany(goal => goal.Targets)
            .Select(target => target.BoneName)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

    private void DrawSemanticGoalPreviewRows()
    {
        var preview = _semanticGoalPreview;
        if (preview == null)
            return;

        if (!ImGui.TreeNodeEx($"Preview Rows ({preview.Rows.Count})", ImGuiTreeNodeFlags.DefaultOpen))
            return;

        ImGui.TextDisabled("Output is ordinary scale-only BoneTransform edits. Scroll this preview if it has many rows.");
        var previewHeight = MathF.Min(260 * ImGuiHelpers.GlobalScale, MathF.Max(140 * ImGuiHelpers.GlobalScale, ImGui.GetTextLineHeightWithSpacing() * 9f));
        using (var child = ImRaii.Child("SemanticGoalPreviewRowsScroll", new Vector2(0, previewHeight), true))
        {
            if (child)
                DrawSemanticGoalPreviewRowsTable(preview);
        }

        ImGui.TreePop();
    }

    private void DrawSemanticGoalPreviewRowsTable(SemanticBodyGoalPreview preview)
    {
        using var table = ImRaii.Table("SemanticGoalPreviewRows", 7, ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerV | ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.ScrollY);
        if (!table)
            return;

        ImGui.TableSetupColumn("Goal", ImGuiTableColumnFlags.WidthFixed, 145 * ImGuiHelpers.GlobalScale);
        ImGui.TableSetupColumn("Bone", ImGuiTableColumnFlags.WidthFixed, 155 * ImGuiHelpers.GlobalScale);
        ImGui.TableSetupColumn("Display", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn("Before", ImGuiTableColumnFlags.WidthFixed, 115 * ImGuiHelpers.GlobalScale);
        ImGui.TableSetupColumn("After", ImGuiTableColumnFlags.WidthFixed, 115 * ImGuiHelpers.GlobalScale);
        ImGui.TableSetupColumn("Delta", ImGuiTableColumnFlags.WidthFixed, 105 * ImGuiHelpers.GlobalScale);
        ImGui.TableSetupColumn("Status", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableHeadersRow();

        foreach (var row in preview.Rows.Take(100))
        {
            ImGui.TableNextRow();

            ImGui.TableNextColumn();
            ImGui.TextUnformatted(row.GoalName);

            ImGui.TableNextColumn();
            ImGui.TextUnformatted(row.BoneName);

            ImGui.TableNextColumn();
            ImGui.TextUnformatted(row.DisplayName);

            ImGui.TableNextColumn();
            ImGui.TextUnformatted(FormatSemanticVector(row.BeforeScale));

            ImGui.TableNextColumn();
            ImGui.TextUnformatted(FormatSemanticVector(row.AfterScale));

            ImGui.TableNextColumn();
            ImGui.TextUnformatted(FormatSemanticVector(row.Delta, true));

            ImGui.TableNextColumn();
            ImGui.PushStyleColor(ImGuiCol.Text, row.IsSkipped ? Constants.Colors.Warning : Constants.Colors.Active);
            ImGuiUtil.TextWrapped(row.Reason);
            ImGui.PopStyleColor();
        }

        if (preview.Rows.Count > 100)
            ImGui.TextDisabled($"...and {preview.Rows.Count - 100} more preview row{(preview.Rows.Count - 100 == 1 ? string.Empty : "s")}.");
    }

    private static string FormatSemanticVector(Vector3 value, bool showSign = false)
        => showSign
            ? $"{value.X:+0.###;-0.###;0}, {value.Y:+0.###;-0.###;0}, {value.Z:+0.###;-0.###;0}"
            : $"{value.X:0.###}, {value.Y:0.###}, {value.Z:0.###}";

    private void DrawTemplateHealthSummary(TemplateHealthSummary summary)
    {
        using var table = ImRaii.Table("TemplateHealthSummary", 4, ImGuiTableFlags.BordersInnerV | ImGuiTableFlags.SizingStretchSame);
        if (!table)
            return;

        for (var i = 0; i < 4; i++)
            ImGui.TableSetupColumn($"HealthSummary{i}", ImGuiTableColumnFlags.WidthStretch);

        ImGui.TableNextRow();
        DrawStatusCell("Edited bones", summary.EditedBoneCount.ToString(), summary.EditedBoneCount > 0 ? Constants.Colors.Info : Constants.Colors.Normal);
        DrawStatusCell("Missing live", summary.MissingEditedBoneCount.ToString(), summary.MissingEditedBoneCount > 0 ? Constants.Colors.Warning : Constants.Colors.Active);
        DrawStatusCell("Unknown edited", summary.UnknownEditedBoneCount.ToString(), summary.UnknownEditedBoneCount > 0 ? Constants.Colors.Warning : Constants.Colors.Active);
        DrawStatusCell("Risky rows", summary.RiskyEditedBoneCount.ToString(), summary.RiskyEditedBoneCount > 0 ? Constants.Colors.Warning : Constants.Colors.Active);

        ImGui.TableNextRow();
        DrawStatusCell("Locked rows", summary.LockedRowCount.ToString(), summary.LockedRowCount > 0 ? Constants.Colors.Info : Constants.Colors.Normal);
        DrawStatusCell("Pinned axes", summary.PinnedAxisCount.ToString(), summary.PinnedAxisCount > 0 ? Constants.Colors.Info : Constants.Colors.Normal);
        DrawStatusCell("Propagated", summary.PropagationCount.ToString(), summary.PropagationCount > 0 ? Constants.Colors.Info : Constants.Colors.Normal);
        DrawStatusCell("Asymmetric", summary.LeftRightAsymmetryCount.ToString(), summary.LeftRightAsymmetryCount > 0 ? Constants.Colors.Warning : Constants.Colors.Active);

        ImGui.TableNextRow();
        DrawStatusCell("Live bones", summary.LiveBoneCount.ToString(), summary.LiveBoneCount > 0 ? Constants.Colors.Active : Constants.Colors.Warning);
        DrawStatusCell("Metadata packs", summary.MetadataPackCount.ToString(), summary.MetadataPackCount > 0 ? Constants.Colors.Info : Constants.Colors.Normal);
        DrawStatusCell("Metadata entries", summary.MetadataEntryCount.ToString(), summary.MetadataEntryCount > 0 ? Constants.Colors.Info : Constants.Colors.Normal);
        DrawStatusCell("Mode", "Read-only", Constants.Colors.Active);
    }

    private void DrawProportionDashboard(ProportionDashboardReport dashboard)
    {
        if (!ImGui.TreeNode("Proportion Dashboard"))
            return;

        try
        {
            ImGuiUtil.TextWrapped("This dashboard uses bone transform ratios, not true mesh measurements. It is an advisory styling/debug tool.");
            ImGui.TextDisabled($"Overall: {dashboard.OverallStatus}. {dashboard.Summary}");

            var tableHeight = GetHelperScrollHeight(dashboard.Items.Count, 190, 110, 7);
            using (var table = ImRaii.Table("ProportionDashboardTable", 4, ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerV | ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.ScrollY, new Vector2(0, tableHeight)))
            {
                if (!table)
                    return;

                ImGui.TableSetupColumn("Signal", ImGuiTableColumnFlags.WidthFixed, 190 * ImGuiHelpers.GlobalScale);
                ImGui.TableSetupColumn("Value", ImGuiTableColumnFlags.WidthFixed, 85 * ImGuiHelpers.GlobalScale);
                ImGui.TableSetupColumn("Status", ImGuiTableColumnFlags.WidthFixed, 90 * ImGuiHelpers.GlobalScale);
                ImGui.TableSetupColumn("Note", ImGuiTableColumnFlags.WidthStretch);
                ImGui.TableHeadersRow();

                foreach (var item in dashboard.Items)
                {
                    ImGui.TableNextRow();
                    ImGui.TableNextColumn();
                    ImGui.TextUnformatted(item.Label);

                    ImGui.TableNextColumn();
                    ImGui.TextUnformatted(item.Value);

                    ImGui.TableNextColumn();
                    ImGui.PushStyleColor(ImGuiCol.Text, GetProportionSeverityColor(item.Severity));
                    ImGui.TextUnformatted(item.Status);
                    ImGui.PopStyleColor();

                    ImGui.TableNextColumn();
                    ImGuiUtil.TextWrapped(item.Note);
                }
            }
        }
        finally
        {
            ImGui.TreePop();
        }
    }

    private void DrawTemplateHealthFilters()
    {
        ImGui.SetNextItemWidth(MathF.Min(320 * ImGuiHelpers.GlobalScale, ImGui.GetContentRegionAvail().X));
        ImGui.InputTextWithHint("##TemplateHealthSearch", "Filter delta rows...", ref _templateHealthSearch, 64);
        CtrlHelper.AddHoverText("Filters by code name, display name, family, support label, notes, or metadata aliases for unknown/custom bones.");

        CtrlHelper.Checkbox("Edited only", ref _templateHealthEditedOnly);
        ImGui.SameLine();
        CtrlHelper.Checkbox("Missing live", ref _templateHealthMissingOnly);
        ImGui.SameLine();
        CtrlHelper.Checkbox("Unknown/custom", ref _templateHealthUnknownOnly);
        ImGui.SameLine();
        CtrlHelper.Checkbox("Risky/modded", ref _templateHealthRiskyOnly);
        ImGui.SameLine();
        CtrlHelper.Checkbox("Asymmetric", ref _templateHealthAsymmetricOnly);
        ImGui.SameLine();
        CtrlHelper.Checkbox("Locked/pinned", ref _templateHealthLockedPinnedOnly);
        ImGui.SameLine();
        CtrlHelper.Checkbox("Propagated", ref _templateHealthPropagatedOnly);
    }

    private bool PassesTemplateHealthFilters(TemplateHealthDeltaRow row)
    {
        if (_templateHealthEditedOnly && !row.IsEdited)
            return false;
        if (_templateHealthMissingOnly && !row.IsMissingLiveBone)
            return false;
        if (_templateHealthUnknownOnly && !row.IsUnknown)
            return false;
        if (_templateHealthRiskyOnly && !row.IsRisky)
            return false;
        if (_templateHealthAsymmetricOnly && !row.IsAsymmetric)
            return false;
        if (_templateHealthLockedPinnedOnly && !row.IsLockedOrPinned)
            return false;
        if (_templateHealthPropagatedOnly && !row.IsPropagated)
            return false;
        if (string.IsNullOrWhiteSpace(_templateHealthSearch))
            return true;

        var query = _templateHealthSearch.Trim();
        return row.BoneName.Contains(query, StringComparison.OrdinalIgnoreCase)
               || row.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase)
               || row.Family.Contains(query, StringComparison.OrdinalIgnoreCase)
               || row.SupportLabel.Contains(query, StringComparison.OrdinalIgnoreCase)
               || row.Note.Contains(query, StringComparison.OrdinalIgnoreCase)
               || _boneMetadataService.MatchesSearch(row.BoneName, query);
    }

    private TemplateHealthReport BuildTemplateHealthReport(
        IReadOnlyDictionary<string, BoneTransform> templateBones,
        IReadOnlySet<string> liveBoneNames,
        int signature)
    {
        var allNames = templateBones.Keys
            .Union(liveBoneNames.Where(name => BoneData.GetBoneFamily(name) == BoneData.BoneFamily.Unknown), StringComparer.Ordinal)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(BoneData.GetBoneRanking)
            .ThenBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var rows = allNames.Select(name => BuildTemplateHealthRow(name, templateBones, liveBoneNames)).ToList();
        var editedRows = rows.Where(row => row.IsEdited).ToList();
        var asymmetricPairs = rows
            .Where(row => row.IsAsymmetric && !string.IsNullOrWhiteSpace(row.MirrorBoneName))
            .Select(row => string.Compare(row.BoneName, row.MirrorBoneName, StringComparison.Ordinal) <= 0
                ? $"{row.BoneName}|{row.MirrorBoneName}"
                : $"{row.MirrorBoneName}|{row.BoneName}")
            .Distinct(StringComparer.Ordinal)
            .Count();

        var summary = new TemplateHealthSummary(
            editedRows.Count,
            editedRows.Count(row => row.IsMissingLiveBone),
            editedRows.Count(row => row.IsUnknown),
            editedRows.Count(row => row.IsRisky),
            rows.Count(row => row.LockState != BoneLockState.Unlocked),
            rows.Sum(row => row.PinnedAxisCount),
            rows.Count(row => row.IsPropagated),
            asymmetricPairs,
            liveBoneNames.Count,
            _boneMetadataService.LoadedPackCount,
            _boneMetadataService.LoadedEntryCount);

        return new TemplateHealthReport(summary, BuildProportionDashboard(templateBones, rows), rows, signature);
    }

    private TemplateHealthDeltaRow BuildTemplateHealthRow(
        string boneName,
        IReadOnlyDictionary<string, BoneTransform> templateBones,
        IReadOnlySet<string> liveBoneNames)
    {
        templateBones.TryGetValue(boneName, out var transform);
        var inTemplate = transform != null;
        var isEdited = transform?.IsEdited(true) == true;
        var liveKnown = liveBoneNames.Count > 0;
        var inLiveSkeleton = liveKnown && liveBoneNames.Contains(boneName);
        var isMissingLiveBone = liveKnown && isEdited && !inLiveSkeleton;
        var family = BoneData.GetBoneFamily(boneName);
        var isUnknown = family == BoneData.BoneFamily.Unknown;
        var isIvcs = BoneData.IsIVCSCompatibleBone(boneName);
        _boneMetadataService.TryGetEntry(boneName, out var metadata);

        var positionDelta = transform == null ? 0f : VectorMagnitude(transform.Translation);
        var rotationDelta = transform == null ? 0f : VectorMagnitude(transform.Rotation);
        var scaleDelta = transform == null ? 0f : MaxAxisDelta(transform.Scaling, Vector3.One);
        var childScaleDelta = transform == null || !transform.ChildScalingIndependent ? 0f : MaxAxisDelta(transform.ChildScaling, Vector3.One);
        var pinnedAxisCount = transform == null ? 0 : (transform.PinX ? 1 : 0) + (transform.PinY ? 1 : 0) + (transform.PinZ ? 1 : 0);
        var isPropagated = transform != null && (transform.PropagateTranslation || transform.PropagateRotation || transform.PropagateScale);
        var isMotionRisk = positionDelta > 0.20f || rotationDelta > 12f;

        var mirrorBoneName = BoneData.GetBoneMirror(boneName);
        var mirrorDelta = GetBuiltInMirrorDelta(boneName, mirrorBoneName, transform, templateBones, out var mirrorEdited);
        var isAsymmetric = mirrorDelta > 0.035f || (mirrorEdited.HasValue && mirrorEdited.Value != isEdited);

        var supportLabel = GetTemplateHealthSupportLabel(boneName, isUnknown, isIvcs, metadata);
        var isRisky = isMissingLiveBone
                      || isUnknown
                      || isMotionRisk
                      || string.Equals(metadata?.SupportClass, "Risky", StringComparison.OrdinalIgnoreCase)
                      || (isUnknown && string.Equals(metadata?.SupportClass, "ManualOnly", StringComparison.OrdinalIgnoreCase));

        var note = BuildTemplateHealthNote(
            metadata,
            isMissingLiveBone,
            isUnknown,
            isIvcs,
            isMotionRisk,
            mirrorDelta,
            mirrorBoneName);

        return new TemplateHealthDeltaRow(
            boneName,
            _boneMetadataService.GetDisplayName(boneName),
            family.ToString(),
            supportLabel,
            inTemplate,
            inLiveSkeleton,
            isEdited,
            isMissingLiveBone,
            isUnknown,
            isIvcs,
            isRisky,
            isAsymmetric,
            transform?.LockState ?? BoneLockState.Unlocked,
            pinnedAxisCount,
            isPropagated,
            positionDelta,
            rotationDelta,
            scaleDelta,
            childScaleDelta,
            mirrorBoneName,
            mirrorDelta,
            FormatProtectionSummary(transform, pinnedAxisCount),
            FormatPropagationSummary(transform),
            note);
    }

    private static TemplateHealthReport BuildEmptyTemplateHealthReport(int signature)
        => new(new TemplateHealthSummary(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0), new ProportionDashboardReport("Balanced", "No template data was available.", []), [], signature);

    private int BuildTemplateHealthSignature(
        IReadOnlyDictionary<string, BoneTransform> templateBones,
        IReadOnlySet<string> liveBoneNames)
    {
        var hash = new HashCode();
        foreach (var bone in templateBones.OrderBy(kvp => kvp.Key, StringComparer.Ordinal))
        {
            hash.Add(bone.Key, StringComparer.Ordinal);
            hash.Add(bone.Value.Translation);
            hash.Add(bone.Value.Rotation);
            hash.Add(bone.Value.Scaling);
            hash.Add(bone.Value.ChildScaling);
            hash.Add(bone.Value.ChildScalingIndependent);
            hash.Add(bone.Value.PropagateTranslation);
            hash.Add(bone.Value.PropagateRotation);
            hash.Add(bone.Value.PropagateScale);
            hash.Add(bone.Value.PropagationFalloff);
            hash.Add(bone.Value.LockState);
            hash.Add(bone.Value.PinX);
            hash.Add(bone.Value.PinY);
            hash.Add(bone.Value.PinZ);
        }

        foreach (var boneName in liveBoneNames.OrderBy(name => name, StringComparer.Ordinal))
            hash.Add(boneName, StringComparer.Ordinal);

        hash.Add(_boneMetadataService.LoadedPackCount);
        hash.Add(_boneMetadataService.LoadedEntryCount);
        return hash.ToHashCode();
    }

    private static float? GetBuiltInMirrorDelta(
        string boneName,
        string? mirrorBoneName,
        BoneTransform? transform,
        IReadOnlyDictionary<string, BoneTransform> templateBones,
        out bool? mirrorEdited)
    {
        mirrorEdited = null;
        if (string.IsNullOrWhiteSpace(mirrorBoneName) ||
            BoneData.GetBoneFamily(boneName) == BoneData.BoneFamily.Unknown ||
            BoneData.GetBoneFamily(mirrorBoneName) == BoneData.BoneFamily.Unknown)
        {
            return null;
        }

        templateBones.TryGetValue(mirrorBoneName, out var mirrorTransform);
        mirrorEdited = mirrorTransform?.IsEdited(true) == true;
        if (transform == null && mirrorTransform == null)
            return 0f;

        transform ??= new BoneTransform();
        mirrorTransform ??= new BoneTransform();

        return MathF.Max(
            MathF.Max(MaxAxisDelta(transform.Scaling, mirrorTransform.Scaling), MaxAxisDelta(transform.ChildScaling, mirrorTransform.ChildScaling)),
            MathF.Max(
                MathF.Abs(VectorMagnitude(transform.Translation) - VectorMagnitude(mirrorTransform.Translation)),
                MathF.Abs(VectorMagnitude(transform.Rotation) - VectorMagnitude(mirrorTransform.Rotation))));
    }

    private static string GetTemplateHealthSupportLabel(
        string boneName,
        bool isUnknown,
        bool isIvcs,
        LocalBoneMetadataEntry? metadata)
    {
        if (isUnknown)
            return metadata?.SupportLabel ?? "Unknown/custom / ManualOnly";

        if (isIvcs)
            return "Built-in IVCS/modded";

        return BoneData.GetBoneFamily(boneName) == BoneData.BoneFamily.Equipment
            ? "Built-in equipment/helper"
            : "Built-in supported";
    }

    private static string BuildTemplateHealthNote(
        LocalBoneMetadataEntry? metadata,
        bool isMissingLiveBone,
        bool isUnknown,
        bool isIvcs,
        bool isMotionRisk,
        float? mirrorDelta,
        string? mirrorBoneName)
    {
        var notes = new List<string>();
        if (isMissingLiveBone)
            notes.Add("Edited but missing from the current live preview skeleton.");
        if (isUnknown)
            notes.Add(metadata?.EffectiveRiskNote ?? "Unknown/custom bone; manual/experimental only.");
        else if (isIvcs)
            notes.Add("Built-in IVCS/modded bone; check compatible body and clothing weights.");
        if (isMotionRisk)
            notes.Add("Large position/rotation edit may be motion-risky.");
        if (mirrorDelta > 0.035f && !string.IsNullOrWhiteSpace(mirrorBoneName))
            notes.Add($"Built-in mirror delta vs {mirrorBoneName}: {mirrorDelta.Value:0.###}.");
        if (!string.IsNullOrWhiteSpace(metadata?.PackName))
            notes.Add($"Metadata pack: {metadata.PackName}.");

        return notes.Count == 0 ? "No notable advisory flags." : string.Join(" ", notes);
    }

    private static ProportionDashboardReport BuildProportionDashboard(
        IReadOnlyDictionary<string, BoneTransform> templateBones,
        IReadOnlyList<TemplateHealthDeltaRow> rows)
    {
        var items = new List<ProportionDashboardItem>
        {
            BuildRatioItem(
                "Shoulder-to-waist",
                AverageKnownScale(templateBones, "n_hkata_l", "n_hkata_r", "j_sako_l", "j_sako_r"),
                AverageKnownScale(templateBones, "j_kosi", "j_sebo_a"),
                "Broad upper-frame transform ratio compared with waist/spine support."),
            BuildRatioItem(
                "Hip-to-waist",
                AverageKnownScale(templateBones, "j_asi_a_l", "j_asi_a_r", "iv_shiri_l", "iv_shiri_r"),
                AverageKnownScale(templateBones, "j_kosi", "j_sebo_a"),
                "Lower-frame transform ratio compared with waist/spine support."),
            BuildRatioItem(
                "Thigh-to-calf",
                AverageKnownScale(templateBones, "j_asi_a_l", "j_asi_a_r"),
                AverageKnownScale(templateBones, "j_asi_c_l", "j_asi_c_r"),
                "Leg taper signal from upper-leg to lower-leg scale transforms."),
            BuildRatioItem(
                "Upper-arm-to-forearm",
                AverageKnownScale(templateBones, "j_ude_a_l", "j_ude_a_r"),
                AverageKnownScale(templateBones, "j_ude_b_l", "j_ude_b_r"),
                "Arm taper signal from upper-arm to forearm scale transforms."),
            BuildPairDeltaItem(
                "Chest L/R delta",
                PairScaleDelta(templateBones, "j_mune_l", "j_mune_r"),
                "Left/right chest scale difference from known built-in bones."),
            BuildPairDeltaItem(
                "Arm L/R delta",
                PairScaleDelta(templateBones, "j_ude_a_l", "j_ude_a_r"),
                "Left/right upper-arm scale difference from known built-in bones."),
            BuildPairDeltaItem(
                "Leg L/R delta",
                PairScaleDelta(templateBones, "j_asi_a_l", "j_asi_a_r"),
                "Left/right upper-leg scale difference from known built-in bones."),
            BuildCountItem(
                "Extreme scale outliers",
                rows.Count(row => row.ScaleDelta > 0.35f || row.ChildScaleDelta > 0.35f),
                "Known/edited rows with large scale or child-scale deltas."),
            BuildCountItem(
                "Motion-risky edits",
                rows.Count(row => row.IsEdited && (row.PositionDelta > 0.20f || row.RotationDelta > 12f)),
                "Position or rotation edits large enough to deserve animation/pose review.")
        };

        var unknownEdited = rows.Count(row => row.IsEdited && row.IsUnknown);
        if (unknownEdited > 0)
        {
            items.Add(new ProportionDashboardItem(
                "Unknown bone caution",
                unknownEdited.ToString(),
                "Review",
                ProportionSeverity.Review,
                "Unknown/custom edited bones may affect perceived proportions, but this dashboard does not trust them for ratio calculations."));
        }

        var highestSeverity = items.Count == 0 ? ProportionSeverity.Balanced : items.Max(item => item.Severity);
        var summary = highestSeverity switch
        {
            ProportionSeverity.Balanced => "No strong transform-ratio flags detected.",
            ProportionSeverity.Mild => "A few mild transform-ratio differences are present.",
            ProportionSeverity.Strong => "One or more transform-ratio signals are strong enough to review.",
            ProportionSeverity.Extreme => "Extreme transform-ratio or outlier signals were detected.",
            _ => "Review advisory notes before treating the ratios as meaningful."
        };

        return new ProportionDashboardReport(highestSeverity.ToString(), summary, items);
    }

    private static ProportionDashboardItem BuildRatioItem(string label, float numerator, float denominator, string note)
    {
        var ratio = denominator <= 0.0001f ? 1f : numerator / denominator;
        var severity = ClassifyRatioSeverity(ratio);
        return new ProportionDashboardItem(label, ratio.ToString("0.00"), SeverityLabel(severity), severity, note);
    }

    private static ProportionDashboardItem BuildPairDeltaItem(string label, float delta, string note)
    {
        var severity = ClassifyDeltaSeverity(delta);
        return new ProportionDashboardItem(label, delta.ToString("0.###"), SeverityLabel(severity), severity, note);
    }

    private static ProportionDashboardItem BuildCountItem(string label, int count, string note)
    {
        var severity = count switch
        {
            0 => ProportionSeverity.Balanced,
            <= 2 => ProportionSeverity.Mild,
            <= 5 => ProportionSeverity.Strong,
            _ => ProportionSeverity.Extreme
        };
        return new ProportionDashboardItem(label, count.ToString(), SeverityLabel(severity), severity, note);
    }

    private static float AverageKnownScale(IReadOnlyDictionary<string, BoneTransform> templateBones, params string[] boneNames)
    {
        if (boneNames.Length == 0)
            return 1f;

        var sum = 0f;
        foreach (var boneName in boneNames)
            sum += GetKnownUniformScale(templateBones, boneName);

        return sum / boneNames.Length;
    }

    private static float PairScaleDelta(IReadOnlyDictionary<string, BoneTransform> templateBones, string leftBone, string rightBone)
        => MathF.Abs(GetKnownUniformScale(templateBones, leftBone) - GetKnownUniformScale(templateBones, rightBone));

    private static float GetKnownUniformScale(IReadOnlyDictionary<string, BoneTransform> templateBones, string boneName)
    {
        if (BoneData.GetBoneFamily(boneName) == BoneData.BoneFamily.Unknown)
            return 1f;

        return templateBones.TryGetValue(boneName, out var transform)
            ? (transform.Scaling.X + transform.Scaling.Y + transform.Scaling.Z) / 3f
            : 1f;
    }

    private static ProportionSeverity ClassifyRatioSeverity(float ratio)
    {
        var delta = MathF.Abs(ratio - 1f);
        return delta switch
        {
            < 0.06f => ProportionSeverity.Balanced,
            < 0.14f => ProportionSeverity.Mild,
            < 0.28f => ProportionSeverity.Strong,
            _ => ProportionSeverity.Extreme
        };
    }

    private static ProportionSeverity ClassifyDeltaSeverity(float delta)
        => delta switch
        {
            < 0.035f => ProportionSeverity.Balanced,
            < 0.09f => ProportionSeverity.Mild,
            < 0.18f => ProportionSeverity.Strong,
            _ => ProportionSeverity.Extreme
        };

    private static string SeverityLabel(ProportionSeverity severity)
        => severity switch
        {
            ProportionSeverity.Balanced => "Balanced",
            ProportionSeverity.Mild => "Mild",
            ProportionSeverity.Strong => "Strong",
            ProportionSeverity.Extreme => "Extreme",
            _ => "Review"
        };

    private static Vector4 GetProportionSeverityColor(ProportionSeverity severity)
        => severity switch
        {
            ProportionSeverity.Balanced => Constants.Colors.Active,
            ProportionSeverity.Mild => Constants.Colors.Info,
            ProportionSeverity.Strong => Constants.Colors.Warning,
            ProportionSeverity.Extreme => Constants.Colors.Warning,
            _ => Constants.Colors.Warning
        };

    private static string FormatProtectionSummary(BoneTransform? transform, int pinnedAxisCount)
    {
        if (transform == null)
            return "-";

        var pins = pinnedAxisCount == 0 ? string.Empty : $" / {pinnedAxisCount} pin{(pinnedAxisCount == 1 ? string.Empty : "s")}";
        return transform.LockState == BoneLockState.Unlocked && pinnedAxisCount == 0
            ? "-"
            : $"{transform.LockState}{pins}";
    }

    private static string FormatPropagationSummary(BoneTransform? transform)
    {
        if (transform == null || !(transform.PropagateTranslation || transform.PropagateRotation || transform.PropagateScale))
            return "-";

        var flags = string.Concat(
            transform.PropagateTranslation ? "P" : string.Empty,
            transform.PropagateRotation ? "R" : string.Empty,
            transform.PropagateScale ? "S" : string.Empty);
        return $"{flags} {transform.PropagationFalloff:0.##}";
    }

    private static string FormatDelta(float value)
        => MathF.Abs(value) < 0.0005f ? "-" : value.ToString("0.###");

    private static float VectorMagnitude(Vector3 value)
        => MathF.Sqrt((value.X * value.X) + (value.Y * value.Y) + (value.Z * value.Z));

    private static float MaxAxisDelta(Vector3 value, Vector3 baseline)
        => MathF.Max(
            MathF.Abs(value.X - baseline.X),
            MathF.Max(MathF.Abs(value.Y - baseline.Y), MathF.Abs(value.Z - baseline.Z)));

    private void DrawUnknownBoneWorkbench()
    {
        var armature = GetPrimaryEditorArmature();
        var liveBoneNames = armature?.GetAllBones()
            .Select(b => b.BoneName)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(BoneData.GetBoneRanking)
            .ThenBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray() ?? Array.Empty<string>();

        var unknownBones = liveBoneNames
            .Where(name => BoneData.GetBoneFamily(name) == BoneData.BoneFamily.Unknown)
            .ToArray();

        if (!ImGui.CollapsingHeader($"Unknown Bone Workbench ({unknownBones.Length})"))
            return;

        ImGui.PushStyleColor(ImGuiCol.Text, Constants.Colors.Warning);
        ImGuiUtil.TextWrapped("Unknown/custom bones are manual and experimental. Local metadata can improve labels, search aliases, and notes, but it does not make these bones trusted for mirroring, propagation safety, parent changes, guardrails, BIW, or advanced automation.");
        ImGui.PopStyleColor();

        ImGui.TextDisabled($"Metadata folder: {_boneMetadataService.MetadataDirectory}");
        ImGui.TextDisabled($"Metadata packs: {_boneMetadataService.LoadedPackCount}/{_boneMetadataService.TotalPackCount} loaded, {_boneMetadataService.LoadedEntryCount} entr{(_boneMetadataService.LoadedEntryCount == 1 ? "y" : "ies")} available, {_boneMetadataService.IgnoredEntryCount} ignored.");

        if (ImGui.Button("Reload metadata packs"))
        {
            _boneMetadataService.Reload();
            SetUnknownWorkbenchStatus("Reloaded local bone metadata packs.");
            _templateHealthReport = null;
            _activityLogService.Record(
                ActivityLogCategory.Metadata,
                "Packs reloaded",
                "Reloaded local bone metadata packs.");
        }
        CtrlHelper.AddHoverText("Reloads local JSON metadata packs from the bone_metadata folder. This only affects editor display, search, and explanations.");

        ImGui.SameLine();
        if (ImGui.Button("Copy metadata folder path"))
            TryCopyUnknownWorkbenchText(_boneMetadataService.MetadataDirectory, "Copied local bone metadata folder path to clipboard.");
        CtrlHelper.AddHoverText("Copies the local bone_metadata folder path so you can inspect or back up metadata packs outside the plugin.");

        ImGui.SameLine();
        using (ImRaii.Disabled(unknownBones.Length == 0))
        {
            if (ImGui.Button("Copy unknown bone names"))
                TryCopyUnknownWorkbenchText(
                    string.Join(Environment.NewLine, unknownBones),
                    $"Copied {unknownBones.Length} unknown bone name{(unknownBones.Length == 1 ? string.Empty : "s")} to clipboard.");
        }
        CtrlHelper.AddHoverText("Copies the currently detected unknown/custom live bone code names to the clipboard.");

        ImGui.SameLine();
        using (ImRaii.Disabled(unknownBones.Length == 0 || armature == null))
        {
            if (ImGui.Button("Copy evidence JSON") && armature != null)
            {
                var evidence = unknownBones.Select(name => BuildUnknownBoneEvidence(armature, name));
                TryCopyUnknownWorkbenchText(
                    _boneMetadataService.CreateEvidenceExport(armature.GetCapabilityManifestSnapshot(), evidence),
                    "Copied compact unknown-bone evidence JSON to clipboard.");
            }
        }
        CtrlHelper.AddHoverText("Exports observed topology facts and clearly labelled candidate metadata. Exported data remains advisory and manual-only by default.");

        ImGui.SameLine();
        using (ImRaii.Disabled(unknownBones.Length == 0))
        {
            if (ImGui.Button("Copy starter metadata draft"))
                TryCopyUnknownWorkbenchText(
                    _boneMetadataService.CreateStarterPackDraft(unknownBones),
                    "Copied a starter local metadata pack draft to clipboard.");
        }
        CtrlHelper.AddHoverText("Copies a schemaVersion 1 metadata pack draft for the detected unknown/custom bones. Draft entries remain manual-only.");

        ImGui.SameLine();
        using (ImRaii.Disabled(unknownBones.Length == 0))
        {
            if (ImGui.Button("Save starter draft"))
            {
                try
                {
                    var path = _boneMetadataService.SaveStarterPackDraft(unknownBones);
                    _boneMetadataService.Reload();
                    SetUnknownWorkbenchStatus($"Saved starter metadata draft: {path}");
                    _templateHealthReport = null;
                    _activityLogService.Record(
                        ActivityLogCategory.Metadata,
                        "Starter pack saved",
                        "Saved a local starter metadata pack.",
                        Path.GetFileName(path));
                }
                catch (Exception ex)
                {
                    SetUnknownWorkbenchStatus($"Could not save starter metadata draft: {ex.Message}");
                    _activityLogService.Record(
                        ActivityLogCategory.Metadata,
                        "Starter pack save failed",
                        "Could not save a local starter metadata pack.",
                        ex.Message);
                    _logger.Error($"Could not save starter metadata draft: {ex}");
                    _popupSystem.ShowPopup(PopupSystem.Messages.ActionError);
                }
            }
        }
        CtrlHelper.AddHoverText("Writes a starter JSON metadata pack into the local bone_metadata folder and reloads metadata packs.");

        ClearExpiredUnknownWorkbenchStatus();
        if (!string.IsNullOrWhiteSpace(_unknownWorkbenchStatus))
            ImGuiUtil.TextWrapped(_unknownWorkbenchStatus);

        DrawMetadataPackStatus();
        DrawMetadataPackDeleteConfirmationPopup();

        ImGui.SetNextItemWidth(MathF.Min(320 * ImGuiHelpers.GlobalScale, ImGui.GetContentRegionAvail().X));
        ImGui.InputTextWithHint("##UnknownBoneSearch", "Filter unknown bones...", ref _unknownBoneSearch, 64);
        CtrlHelper.AddHoverText("Filters by code name, metadata display name, support class, family, notes, or aliases.");

        var templateBones = _editorManager.CurrentlyEditedTemplate?.Bones ?? _templateFileSystemSelector.Selected?.Bones;
        var rows = unknownBones
            .Where(name => string.IsNullOrWhiteSpace(_unknownBoneSearch) ||
                           name.Contains(_unknownBoneSearch.Trim(), StringComparison.OrdinalIgnoreCase) ||
                           _boneMetadataService.MatchesSearch(name, _unknownBoneSearch))
            .Select(name =>
            {
                BoneTransform? transform = null;
                templateBones?.TryGetValue(name, out transform);
                var inTemplate = transform != null;
                var edited = transform?.IsEdited(true) == true;
                _boneMetadataService.TryGetEntry(name, out var metadata);
                return new UnknownBoneWorkbenchRow(
                    name,
                    _boneMetadataService.GetDisplayName(name),
                    _boneMetadataService.GetSupportLabel(name),
                    inTemplate,
                    edited,
                    metadata?.PackName,
                    _boneMetadataService.GetRiskNote(name));
            })
            .ToList();

        if (unknownBones.Length == 0)
        {
            ImGui.TextDisabled(armature?.IsBuilt == true
                ? "No unknown/custom live bones are currently detected for the preview actor."
                : "Waiting for a live preview armature before unknown/custom bones can be listed.");
            return;
        }

        var tableHeight = GetHelperScrollHeight(rows.Count, 260, 140);
        using var table = ImRaii.Table("UnknownBoneWorkbenchTable", 6, ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerV | ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.ScrollY, new Vector2(0, tableHeight));
        if (!table)
            return;

        ImGui.TableSetupColumn("Bone code", ImGuiTableColumnFlags.WidthFixed, 190 * ImGuiHelpers.GlobalScale);
        ImGui.TableSetupColumn("Label", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn("Support", ImGuiTableColumnFlags.WidthFixed, 180 * ImGuiHelpers.GlobalScale);
        ImGui.TableSetupColumn("Template", ImGuiTableColumnFlags.WidthFixed, 120 * ImGuiHelpers.GlobalScale);
        ImGui.TableSetupColumn("Notes", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn("Action", ImGuiTableColumnFlags.WidthFixed, 60 * ImGuiHelpers.GlobalScale);
        ImGui.TableHeadersRow();

        foreach (var row in rows)
        {
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(row.BoneName);

            ImGui.TableNextColumn();
            ImGui.TextUnformatted(row.DisplayName);

            ImGui.TableNextColumn();
            ImGui.TextWrapped(row.SupportLabel);

            ImGui.TableNextColumn();
            ImGui.TextUnformatted(row.Edited
                ? "Edited"
                : row.InTemplate
                    ? "In template"
                    : "Live only");

            ImGui.TableNextColumn();
            ImGuiUtil.TextWrapped(string.IsNullOrWhiteSpace(row.PackName)
                ? row.RiskNote
                : $"{row.RiskNote} Pack: {row.PackName}.");

            ImGui.TableNextColumn();
            using (var id = ImRaii.PushId($"UnknownInspect{row.BoneName}"))
            {
                if (ImGui.SmallButton("Inspect"))
                    _inspectedBoneName = row.BoneName;
            }
        }

    }

    private UnknownBoneEvidenceRecord BuildUnknownBoneEvidence(Armature armature, string boneName)
    {
        armature.TryGetPublishedBone(boneName, out var bone);
        _boneMetadataService.TryGetEntry(boneName, out var entry);
        var depth = 0;
        for (var parent = bone?.ParentBone; parent != null && depth < 128; parent = parent.ParentBone)
            depth++;
        var mirrorCandidate = BoneData.GetBoneMirror(boneName);
        if (mirrorCandidate == null && boneName.EndsWith("_l", StringComparison.Ordinal))
            mirrorCandidate = boneName[..^2] + "_r";
        else if (mirrorCandidate == null && boneName.EndsWith("_r", StringComparison.Ordinal))
            mirrorCandidate = boneName[..^2] + "_l";
        var metadata = BoneData.GetMetadata(boneName);
        float? importance = armature.ActiveBoneImportanceResult.Scores.TryGetValue(boneName, out var score) ? score : null;
        return new UnknownBoneEvidenceRecord(
            boneName,
            bone?.ParentBone?.BoneName,
            bone?.ChildBones.Select(static child => child.BoneName).OrderBy(static name => name, StringComparer.Ordinal).ToArray() ?? Array.Empty<string>(),
            depth,
            mirrorCandidate,
            importance,
            ObservationCount: armature.GetCapabilityManifestSnapshot().StableObservations,
            ParentageStable: armature.IsSkeletonBindingCurrent,
            metadata.Origin,
            metadata.Role,
            metadata.Trust,
            entry?.RiskNotes);
    }

    private void DrawMetadataPackStatus()
    {
        if (!ImGui.TreeNode("Metadata pack status"))
            return;

        try
        {
            if (_boneMetadataService.PackStatuses.Count == 0)
            {
                ImGui.TextDisabled("No local metadata packs found yet. Add schemaVersion 1 JSON files to the bone_metadata folder.");
                return;
            }

            var tableHeight = GetHelperScrollHeight(_boneMetadataService.PackStatuses.Count, 170, 90, 5);
            using (var table = ImRaii.Table("MetadataPackStatusTable", 4, ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerV | ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.ScrollY, new Vector2(0, tableHeight)))
            {
                if (!table)
                    return;

                ImGui.TableSetupColumn("Pack", ImGuiTableColumnFlags.WidthStretch);
                ImGui.TableSetupColumn("Result", ImGuiTableColumnFlags.WidthFixed, 95 * ImGuiHelpers.GlobalScale);
                ImGui.TableSetupColumn("Messages", ImGuiTableColumnFlags.WidthStretch);
                ImGui.TableSetupColumn("Action", ImGuiTableColumnFlags.WidthFixed, 75 * ImGuiHelpers.GlobalScale);
                ImGui.TableHeadersRow();

                foreach (var status in _boneMetadataService.PackStatuses)
                {
                    ImGui.TableNextRow();

                    ImGui.TableNextColumn();
                    ImGui.TextUnformatted(status.PackName);
                    ImGui.TextDisabled(status.FileName);

                    ImGui.TableNextColumn();
                    ImGui.TextUnformatted(status.Loaded ? $"Loaded {status.EntryCount}" : "Failed");
                    if (status.IgnoredEntryCount > 0)
                        ImGui.TextDisabled($"Ignored {status.IgnoredEntryCount}");

                    ImGui.TableNextColumn();
                    if (!string.IsNullOrWhiteSpace(status.MessageText))
                        ImGuiUtil.TextWrapped(status.MessageText);
                    else
                        ImGui.TextDisabled("No issues.");

                    ImGui.TableNextColumn();
                    using (ImRaii.Disabled(!_boneMetadataService.CanDeletePackFile(status.FileName)))
                    {
                        using var id = ImRaii.PushId($"MetadataPackDelete{status.FileName}");
                        if (ImGui.SmallButton("Delete"))
                        {
                            _pendingMetadataPackDeleteFileName = status.FileName;
                            _pendingMetadataPackDeleteLabel = string.IsNullOrWhiteSpace(status.PackName)
                                ? status.FileName
                                : $"{status.PackName} ({status.FileName})";
                            _openMetadataPackDeletePopup = true;
                        }
                    }
                    CtrlHelper.AddHoverText("Deletes this local JSON metadata pack file from the bone_metadata folder after confirmation.");
                }
            }
        }
        finally
        {
            ImGui.TreePop();
        }
    }

    private void DrawMetadataPackDeleteConfirmationPopup()
    {
        if (_openMetadataPackDeletePopup)
        {
            ImGui.OpenPopup("DeleteMetadataPackPopup");
            _openMetadataPackDeletePopup = false;
        }

        var viewportSize = ImGui.GetWindowViewport().Size;
        var scale = ImGuiHelpers.GlobalScale;
        var popupWidth = MathF.Min(520 * scale, MathF.Max(1, viewportSize.X - 48 * scale));
        ImGui.SetNextWindowSize(new Vector2(popupWidth, 0));
        ImGui.SetNextWindowPos(viewportSize / 2, ImGuiCond.Always, new Vector2(0.5f));
        using var popup = ImRaii.Popup("DeleteMetadataPackPopup", ImGuiWindowFlags.Modal);
        if (!popup)
            return;

        var fileName = _pendingMetadataPackDeleteFileName;
        ImGuiUtil.TextWrapped($"Delete local metadata pack '{_pendingMetadataPackDeleteLabel ?? fileName ?? "unknown"}'?");
        ImGuiUtil.TextWrapped("This deletes the JSON metadata pack file from disk. It does not delete built-in bone data, templates, profiles, or plugin files.");
        ImGui.Spacing();

        var style = ImGui.GetStyle();
        var buttonWidth = (ImGui.GetContentRegionAvail().X - style.ItemSpacing.X) / 2;
        var buttonSize = new Vector2(buttonWidth, 0);
        using (ImRaii.Disabled(string.IsNullOrWhiteSpace(fileName)))
        {
            if (ImGui.Button("Delete metadata pack", buttonSize) && !string.IsNullOrWhiteSpace(fileName))
            {
                if (_boneMetadataService.TryDeletePackFile(fileName, out var message))
                {
                    _templateHealthReport = null;
                    _activityLogService.Record(
                        ActivityLogCategory.Metadata,
                        "Pack deleted",
                        "Deleted a local metadata pack.",
                        fileName);
                }
                else
                {
                    _activityLogService.Record(
                        ActivityLogCategory.Metadata,
                        "Pack deletion failed",
                        "Could not delete a local metadata pack.",
                        message);
                    _popupSystem.ShowPopup(PopupSystem.Messages.ActionError);
                }

                SetUnknownWorkbenchStatus(message);
                _pendingMetadataPackDeleteFileName = null;
                _pendingMetadataPackDeleteLabel = null;
                ImGui.CloseCurrentPopup();
            }
        }

        ImGui.SameLine();
        if (ImGui.Button("Cancel", buttonSize))
        {
            _pendingMetadataPackDeleteFileName = null;
            _pendingMetadataPackDeleteLabel = null;
            ImGui.CloseCurrentPopup();
        }
    }

    private void TryCopyUnknownWorkbenchText(string text, string successStatus)
    {
        try
        {
            ImUtf8.SetClipboardText(text);
            SetUnknownWorkbenchStatus(successStatus);
            _popupSystem.ShowPopup(PopupSystem.Messages.IPCCopiedToClipboard);
        }
        catch (Exception ex)
        {
            SetUnknownWorkbenchStatus($"Could not copy Unknown Bone Workbench text: {ex.Message}");
            _logger.Error($"Could not copy Unknown Bone Workbench text: {ex}");
            _popupSystem.ShowPopup(PopupSystem.Messages.ActionError);
        }
    }

    private void SetUnknownWorkbenchStatus(string message)
    {
        _unknownWorkbenchStatus = message;
        _unknownWorkbenchStatusAtMs = Environment.TickCount64;
    }

    private void ClearExpiredUnknownWorkbenchStatus()
    {
        if (string.IsNullOrWhiteSpace(_unknownWorkbenchStatus))
            return;

        if (Environment.TickCount64 - _unknownWorkbenchStatusAtMs <= UnknownWorkbenchStatusLifetimeMs)
            return;

        _unknownWorkbenchStatus = null;
    }

    private static void DrawWrappedBullet(string text)
    {
        ImGui.Bullet();
        ImGui.SameLine();
        ImGuiUtil.TextWrapped(text);
    }

    private static float GetHelperScrollHeight(int rowCount, float maxUnscaled, float minUnscaled, int visibleRows = 8)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var headerAndPadding = 2.5f * ImGui.GetTextLineHeightWithSpacing();
        var desiredRows = MathF.Max(1, MathF.Min(rowCount, visibleRows));
        var desiredHeight = headerAndPadding + desiredRows * ImGui.GetTextLineHeightWithSpacing();
        return MathF.Min(maxUnscaled * scale, MathF.Max(minUnscaled * scale, desiredHeight));
    }

    #region ImGui helper functions

    private bool ResetBoneButton(EditRowParams bone)
    {
        var output = ImGuiComponents.IconButton($"##Reset{bone.BoneCodeName}", FontAwesomeIcon.Recycle);
        CtrlHelper.AddHoverText(
            $"Reset '{BoneData.GetBoneDisplayName(bone.BoneCodeName)}' to default {_editingAttribute} values");

        if (output)
        {
            _editorManager.BeginEditTransaction($"Reset {BoneData.GetBoneDisplayName(bone.BoneCodeName)}");
            _editorManager.ResetBoneAttributeChanges(bone.BoneCodeName, _editingAttribute);
            if (_isMirrorModeEnabled && bone.Basis?.TwinBone != null) //todo: put it inside manager
                _editorManager.ResetBoneAttributeChanges(bone.Basis.TwinBone.BoneName, _editingAttribute);
            _editorManager.CommitEditTransaction();
        }

        return output;
    }

    private bool RevertBoneButton(EditRowParams bone)
    {
        var output = ImGuiComponents.IconButton($"##Revert{bone.BoneCodeName}", FontAwesomeIcon.ArrowCircleLeft);
        CtrlHelper.AddHoverText(
            $"Revert '{BoneData.GetBoneDisplayName(bone.BoneCodeName)}' to last saved {_editingAttribute} values");

        if (output)
        {
            _editorManager.BeginEditTransaction($"Revert {BoneData.GetBoneDisplayName(bone.BoneCodeName)}");
            _editorManager.RevertBoneAttributeChanges(bone.BoneCodeName, _editingAttribute);
            if (_isMirrorModeEnabled && bone.Basis?.TwinBone != null) //todo: put it inside manager
                _editorManager.RevertBoneAttributeChanges(bone.Basis.TwinBone.BoneName, _editingAttribute);
            _editorManager.CommitEditTransaction();
        }

        return output;
    }

    private bool PropagateCheckbox(EditRowParams bone, ref bool enabled)
    {
        const FontAwesomeIcon icon = FontAwesomeIcon.Link;
        var id = $"##Propagate{bone.BoneCodeName}";

        if (enabled)
            ImGui.PushStyleColor(ImGuiCol.Text, Constants.Colors.Active);

        var output = ImGuiComponents.IconButton(id, icon);
        CtrlHelper.AddHoverText(
            $"Apply '{BoneData.GetBoneDisplayName(bone.BoneCodeName)}' transformations to its child bones");

        if (enabled)
            ImGui.PopStyleColor();

        if (output)
            enabled = !enabled;

        return output;
    }

    private bool FavoriteButton(EditRowParams bone)
    {
        var isFavorite = _favoriteBones.Contains(bone.BoneCodeName);

        const FontAwesomeIcon icon = FontAwesomeIcon.Star;
        var id = $"##Favorite{bone.BoneCodeName}";

        if (isFavorite)
            ImGui.PushStyleColor(ImGuiCol.Text, Constants.Colors.Favorite);

        var output = ImGuiComponents.IconButton(id, icon);

        if (isFavorite)
            ImGui.PopStyleColor();

        CtrlHelper.AddHoverText(
            $"Toggle favorite on '{BoneData.GetBoneDisplayName(bone.BoneCodeName)}' bone");

        if (output)
        {
            if (isFavorite)
                _favoriteBones.Remove(bone.BoneCodeName);
            else
                _favoriteBones.Add(bone.BoneCodeName);

            _configuration.EditorConfiguration.FavoriteBones = _favoriteBones.ToHashSet();
            _configuration.Save();
        }

        return isFavorite;
    }

    private bool LockStateButton(EditRowParams bone, ref BoneLockState lockState)
    {
        var id = $"##LockState{bone.BoneCodeName}";
        var icon = lockState switch
        {
            BoneLockState.Locked => FontAwesomeIcon.Lock,
            BoneLockState.Priority => FontAwesomeIcon.ExclamationTriangle,
            _ => FontAwesomeIcon.LockOpen
        };

        if (lockState == BoneLockState.Locked)
            ImGui.PushStyleColor(ImGuiCol.Text, Constants.Colors.Warning);
        else if (lockState == BoneLockState.Priority)
            ImGui.PushStyleColor(ImGuiCol.Text, Constants.Colors.Info);

        var output = ImGuiComponents.IconButton(id, icon);

        if (lockState == BoneLockState.Locked || lockState == BoneLockState.Priority)
            ImGui.PopStyleColor();

        var tooltip = lockState switch
        {
            BoneLockState.Locked => "Locked: automatic systems cannot modify this bone.\r\nPins are redundant while the whole row is locked.",
            BoneLockState.Priority => "Priority: influences neighbors but is not modified.\r\nPins are redundant while the whole row is protected.",
            _ => "Unlocked: automatic systems can modify this bone.\r\nUse Pin X/Y/Z to protect only selected scale axes."
        };
        CtrlHelper.AddHoverText(tooltip);

        if (output)
        {
            lockState = lockState switch
            {
                BoneLockState.Unlocked => BoneLockState.Locked,
                BoneLockState.Locked => BoneLockState.Priority,
                _ => BoneLockState.Unlocked
            };
        }

        return output;
    }

    private bool PinAxisButton(EditRowParams bone, char axis, ref bool pinned, bool disableBecauseLocked)
    {
        if (pinned)
            ImGui.PushStyleColor(ImGuiCol.Text, Constants.Colors.Info);

        using var disabled = ImRaii.Disabled(disableBecauseLocked);
        var output = ImGui.Button($"{axis}##Pin{axis}{bone.BoneCodeName}", new Vector2(CtrlHelper.IconButtonWidth * 0.8f, 0f));

        if (pinned)
            ImGui.PopStyleColor();

        var tooltip = disableBecauseLocked
            ? $"Pin {axis}: prevents automation from changing the {axis} scale axis on this bone.\r\nThe whole-row lock already blocks all automation here."
            : $"Pin {axis}: prevents automation from changing the {axis} scale axis on this bone.\r\nManual edits are still allowed.\r\nLock protects the whole row; pins protect only selected axes.";
        CtrlHelper.AddHoverText(tooltip);

        if (output)
            pinned = !pinned;

        return output;
    }

    private bool FullBoneSlider(string label, ref Vector3 value)
    {
        var velocity = _editingAttribute == BoneAttribute.Rotation ? 0.1f : 0.001f;
        var minValue = _editingAttribute == BoneAttribute.Rotation ? -360.0f : -10.0f;
        var maxValue = _editingAttribute == BoneAttribute.Rotation ? 360.0f : 10.0f;

        var temp = _editingAttribute switch
        {
            BoneAttribute.Position => 0.0f,
            BoneAttribute.Rotation => 0.0f,
            _ => value.X == value.Y && value.Y == value.Z ? value.X : 1.0f
        };


        ImGui.PushItemWidth(ImGui.GetColumnWidth());
        if (ImGui.DragFloat(label, ref temp, velocity, minValue, maxValue, $"%.{_precision}f"))
        {
            value = new Vector3(temp, temp, temp);
            return true;

        }

        return false;
    }

    private bool SingleValueSlider(string label, ref float value)
    {
        var velocity = _editingAttribute == BoneAttribute.Rotation ? 0.1f : 0.001f;
        var minValue = _editingAttribute == BoneAttribute.Rotation ? -360.0f : -10.0f;
        var maxValue = _editingAttribute == BoneAttribute.Rotation ? 360.0f : 10.0f;

        ImGui.PushItemWidth(ImGui.GetColumnWidth());
        var temp = value;
        if (ImGui.DragFloat(label, ref temp, velocity, minValue, maxValue, $"%.{_precision}f"))
        {
            value = temp;
            return true;
        }

        return false;
    }

    private void CompleteBoneEditor(BoneData.BoneFamily boneFamily, EditRowParams bone)
    {
        var codename = bone.BoneCodeName;
        var displayName = _boneMetadataService.GetDisplayName(codename);
        var transform = new BoneTransform(bone.Transform);

        var newVector = _editingAttribute switch
        {
            BoneAttribute.Position => transform.Translation,
            BoneAttribute.Rotation => transform.Rotation,
            _ => transform.Scaling
        };

        var propagationEnabled = _editingAttribute switch
        {
            BoneAttribute.Position => transform.PropagateTranslation,
            BoneAttribute.Rotation => transform.PropagateRotation,
            _ => transform.PropagateScale
        };
        var lockState = transform.LockState;
        var pinX = transform.PinX;
        var pinY = transform.PinY;
        var pinZ = transform.PinZ;

        bool valueChanged = false;

        bool isFavorite = false;

        using var id = ImRaii.PushId(codename);
        ImGui.TableNextColumn();
        _parentRowScreenPosY = ImGui.GetCursorScreenPos().Y;
        using (var disabled = ImRaii.Disabled(!_isUnlocked))
        {
            ImGui.Dummy(new Vector2(CtrlHelper.IconButtonWidth * 0.75f, 0));
            ImGui.SameLine();
            ResetBoneButton(bone);
            ImGui.SameLine();
            RevertBoneButton(bone);
            ImGui.SameLine();

            _propagateButtonXPos = ImGui.GetCursorPosX();
            if (PropagateCheckbox(bone, ref propagationEnabled))
            {
                SaveStateForUndo(CaptureCurrentState());
                valueChanged = true;
            }

            ImGui.SameLine();
            if (LockStateButton(bone, ref lockState))
            {
                SaveStateForUndo(CaptureCurrentState());
                transform.LockState = lockState;
                valueChanged = true;
            }

            ImGui.SameLine();
            isFavorite = FavoriteButton(bone);

            if (_editingAttribute == BoneAttribute.Scale)
            {
                var pinsDisabled = lockState != BoneLockState.Unlocked;
                bool pinsChanged = false;

                ImGui.SameLine();
                ImGui.Dummy(new Vector2(CtrlHelper.IconButtonWidth * 0.25f, 0f));
                ImGui.SameLine();
                if (PinAxisButton(bone, 'X', ref pinX, pinsDisabled))
                    pinsChanged = true;

                ImGui.SameLine();
                if (PinAxisButton(bone, 'Y', ref pinY, pinsDisabled))
                    pinsChanged = true;

                ImGui.SameLine();
                if (PinAxisButton(bone, 'Z', ref pinZ, pinsDisabled))
                    pinsChanged = true;

                if (pinsChanged)
                {
                    SaveStateForUndo(CaptureCurrentState());
                    transform.PinX = pinX;
                    transform.PinY = pinY;
                    transform.PinZ = pinZ;
                    valueChanged = true;
                }
            }

            // adjusted logic, should only snapshot if there is a change in the value.
            // change da X
            ImGui.TableNextColumn();
            float tempX = newVector.X;
            if (ImGui.IsItemActivated())
            {
                _initialX = tempX;
                if (_pendingUndoSnapshot == null)
                    _pendingUndoSnapshot = CaptureCurrentState();
            }
            if (SingleValueSlider($"##{displayName}-X", ref tempX))
            {
                newVector.X = tempX;
                valueChanged = true;
            }
            if (ImGui.IsItemDeactivatedAfterEdit())
            {
                FinalizePendingEditTransaction(_initialX != newVector.X);
            }

            // change da Y
            ImGui.TableNextColumn();
            float tempY = newVector.Y;
            if (ImGui.IsItemActivated())
            {
                _initialY = tempY;
                if (_pendingUndoSnapshot == null)
                    _pendingUndoSnapshot = CaptureCurrentState();
            }
            if (SingleValueSlider($"##{displayName}-Y", ref tempY))
            {
                newVector.Y = tempY;
                valueChanged = true;
            }
            if (ImGui.IsItemDeactivatedAfterEdit())
            {
                FinalizePendingEditTransaction(_initialY != newVector.Y);
            }

            // change da Z
            ImGui.TableNextColumn();
            float tempZ = newVector.Z;
            if (ImGui.IsItemActivated())
            {
                _initialZ = tempZ;
                if (_pendingUndoSnapshot == null)
                    _pendingUndoSnapshot = CaptureCurrentState();
            }
            if (SingleValueSlider($"##{displayName}-Z", ref tempZ))
            {
                newVector.Z = tempZ;
                valueChanged = true;
            }
            if (ImGui.IsItemDeactivatedAfterEdit())
            {
                FinalizePendingEditTransaction(_initialZ != newVector.Z);
            }

            // scale
            if (_editingAttribute != BoneAttribute.Scale)
                ImGui.BeginDisabled();

            ImGui.TableNextColumn();
            Vector3 tempScale = newVector;
            if (ImGui.IsItemActivated())
            {
                _initialScale = tempScale;
                if (_pendingUndoSnapshot == null)
                    _pendingUndoSnapshot = CaptureCurrentState();
            }
            if (FullBoneSlider($"##{displayName}-All", ref tempScale))
            {
                newVector = tempScale;
                valueChanged = true;
            }
            if (ImGui.IsItemDeactivatedAfterEdit())
            {
                FinalizePendingEditTransaction(_initialScale != newVector);
            }

            if (_editingAttribute != BoneAttribute.Scale)
                ImGui.EndDisabled();
        }

        ImGui.TableNextColumn();
        var isKnownModdedBone = BoneData.IsIVCSCompatibleBone(codename);
        var isUnknownBone = boneFamily == BoneData.BoneFamily.Unknown;
        if ((isKnownModdedBone || isUnknownBone) && !codename.StartsWith("j_f_"))
        {
            ImGui.PushStyleColor(ImGuiCol.Text, Constants.Colors.Warning);
            ImGuiUtil.PrintIcon(FontAwesomeIcon.Wrench);
            ImGui.PopStyleColor();
            CtrlHelper.AddHoverText(isUnknownBone
                ? $"Unknown/custom bone detected.\r\nThe plugin shows this bone for manual experimentation, but it is not trusted for mirroring, propagation safety, guardrails, BIW, or advanced automation by default.\r\nMovement depends on the active skeleton and whether the body or clothing mesh is weighted to this bone.\r\n\r\nMetadata note: {_boneMetadataService.GetRiskNote(codename)}"
                : "Known IVCS/modded skeleton bone.\r\nThis needs a compatible skeleton, body, and clothing weights designed for the bone.\r\nEven compatible outfits may not support every modded bone, so test body and clothing behavior separately.");
            ImGui.SameLine();
        }

        CtrlHelper.StaticLabel(!isFavorite ? displayName : $"{displayName} ({boneFamily})", CtrlHelper.TextAlignment.Left,
            isKnownModdedBone ? $"(IVCS Compatible) {codename}" : codename);
        ImGui.SameLine();
        if (ImGui.SmallButton("Why?"))
            _inspectedBoneName = codename;
        CtrlHelper.AddHoverText("Inspect why this bone moved, or why automatic shaping was safely skipped.");

        if (valueChanged)
        {
            transform.UpdateAttribute(_editingAttribute, newVector, propagationEnabled);
            _editorManager.ModifyBoneTransform(codename, transform);

            if (_isMirrorModeEnabled && BoneData.HasAutomationTrust(codename, BoneAutomationTrust.MirrorSafe) && bone.Basis?.TwinBone != null)
            {
                _editorManager.ModifyBoneTransform(
                    bone.Basis.TwinBone.BoneName,
                    BoneData.IsIVCSCompatibleBone(codename)
                        ? transform.GetSpecialReflection()
                        : transform.GetStandardReflection()
                );
            }

            if (_commitHistoryAfterWrite)
            {
                _editorManager.CommitEditTransaction();
                _commitHistoryAfterWrite = false;
            }
        }

        ImGui.TableNextRow();

        if (_editingAttribute == BoneAttribute.Scale && propagationEnabled)
            RenderChildScalingRow(bone, transform);

        if (propagationEnabled)
            RenderPropagationFalloffRow(bone, transform);
    }

    private void RenderChildScalingRow(EditRowParams bone, BoneTransform transform)
    {
        var codename = bone.BoneCodeName;
        var displayName = bone.BoneDisplayName;

        bool isChildScaleIndependent = transform.ChildScalingIndependent;
        bool childScaleChanged = false;
        var childScale = isChildScaleIndependent ? transform.ChildScaling : transform.Scaling;

        using var id = ImRaii.PushId($"{codename}_childscale");

        ImGui.TableNextColumn();
        
        ImGui.SetCursorPosX(_propagateButtonXPos);

        using (var disabled = ImRaii.Disabled(!_isUnlocked))
        {
            var wasLinked = !isChildScaleIndependent;

            if (wasLinked)
                ImGui.PushStyleColor(ImGuiCol.Text, Constants.Colors.Active);

            if (ImGuiComponents.IconButton($"##ChildLink{codename}", FontAwesomeIcon.Link))
            {
                SaveStateForUndo(CaptureCurrentState());

                isChildScaleIndependent = !isChildScaleIndependent;
                if (isChildScaleIndependent)
                {
                    childScale = transform.Scaling;
                }
                else
                {
                    transform.ChildScaling = Vector3.One;
                }
                transform.ChildScalingIndependent = isChildScaleIndependent;
                childScaleChanged = true;
            }

            if (wasLinked)
                ImGui.PopStyleColor();

            if (!isChildScaleIndependent)
                ImGui.PushStyleColor(ImGuiCol.Text, Constants.Colors.Active);

            CtrlHelper.AddHoverText(
                $"Link '{BoneData.GetBoneDisplayName(codename)}' child bone scaling to parent scaling");

            if (!isChildScaleIndependent)
                ImGui.PopStyleColor();
        }

        // Draws a bracket between the two rows.
        var drawList = ImGui.GetWindowDrawList();
        var bracketColor = ImGui.GetColorU32(ImGuiCol.TextDisabled);
        var lineThickness = 2.0f;

        var rowHeight = ImGui.GetFrameHeight();
        var bracketWidth = CtrlHelper.IconButtonWidth * 0.3f;

        var availWidth = ImGui.GetContentRegionAvail().X;
        var cursorScreenPos = ImGui.GetCursorScreenPos();
        var rightEdgeX = cursorScreenPos.X + availWidth - bracketWidth;

        var parentRowCenterY = _parentRowScreenPosY + rowHeight * 0.5f;
        var childRowCenterY = cursorScreenPos.Y + rowHeight * 0.5f;
        var bracketCenterY = (parentRowCenterY + childRowCenterY) * 0.5f;

        var topY = parentRowCenterY;
        var bottomY = bracketCenterY;
        var heightThird = (topY - bottomY) / 3;
        var topRightM = new Vector2(rightEdgeX + bracketWidth - 1, topY);
        var topLeft = new Vector2(rightEdgeX, topY);
        var bottomLeft = new Vector2(rightEdgeX, bottomY);
        var bottomLeftM = new Vector2(rightEdgeX - 1, bottomY); // Just works
        var bottomRight = new Vector2(rightEdgeX + bracketWidth, bottomY);

        drawList.AddLine(topRightM, topLeft, bracketColor, lineThickness);   // Top
        if (!isChildScaleIndependent)
        {
            drawList.AddLine(topLeft, bottomLeft, bracketColor, lineThickness); // Middle
        }
        else
        {
            var gapStart = new Vector2(rightEdgeX, topY - heightThird);
            var gapEnd = new Vector2(rightEdgeX, topY - 2 * heightThird);
            drawList.AddLine(topLeft, gapStart, bracketColor, lineThickness);
            drawList.AddLine(gapEnd, bottomLeft, bracketColor, lineThickness);
        }
        drawList.AddLine(bottomLeftM, bottomRight, bracketColor, lineThickness); // Bottom

        using (var disabled = ImRaii.Disabled(!_isUnlocked || !isChildScaleIndependent))
        {
            ImGui.TableNextColumn();
            float tempChildX = childScale.X;
            if (ImGui.IsItemActivated())
            {
                _initialChildX = tempChildX;
                if (_pendingUndoSnapshot == null)
                    _pendingUndoSnapshot = CaptureCurrentState();
            }
            if (SingleValueSlider($"##child-{displayName}-X", ref tempChildX))
            {
                childScale.X = tempChildX;
                childScaleChanged = true;
            }
            if (ImGui.IsItemDeactivatedAfterEdit())
            {
                FinalizePendingEditTransaction(_initialChildX != childScale.X);
            }

            ImGui.TableNextColumn();
            float tempChildY = childScale.Y;
            if (ImGui.IsItemActivated())
            {
                _initialChildY = tempChildY;
                if (_pendingUndoSnapshot == null)
                    _pendingUndoSnapshot = CaptureCurrentState();
            }
            if (SingleValueSlider($"##child-{displayName}-Y", ref tempChildY))
            {
                childScale.Y = tempChildY;
                childScaleChanged = true;
            }
            if (ImGui.IsItemDeactivatedAfterEdit())
            {
                FinalizePendingEditTransaction(_initialChildY != childScale.Y);
            }

            ImGui.TableNextColumn();
            float tempChildZ = childScale.Z;
            if (ImGui.IsItemActivated())
            {
                _initialChildZ = tempChildZ;
                if (_pendingUndoSnapshot == null)
                    _pendingUndoSnapshot = CaptureCurrentState();
            }
            if (SingleValueSlider($"##child-{displayName}-Z", ref tempChildZ))
            {
                childScale.Z = tempChildZ;
                childScaleChanged = true;
            }
            if (ImGui.IsItemDeactivatedAfterEdit())
            {
                FinalizePendingEditTransaction(_initialChildZ != childScale.Z);
            }

            ImGui.TableNextColumn();
            if (ImGui.IsItemActivated())
            {
                _initialChildScale = childScale;
                if (_pendingUndoSnapshot == null)
                    _pendingUndoSnapshot = CaptureCurrentState();
            }
            if (FullBoneSlider($"##child-{displayName}-All", ref childScale))
                childScaleChanged = true;
            if (ImGui.IsItemDeactivatedAfterEdit())
            {
                FinalizePendingEditTransaction(_initialChildScale != childScale);
            }
        }

        ImGui.TableNextColumn();
        CtrlHelper.StaticLabel($"{displayName} - Child Bones", CtrlHelper.TextAlignment.Left, "Scale applied to child bones");

        if (childScaleChanged)
        {
            transform.ChildScaling = childScale;
            _editorManager.ModifyBoneTransform(codename, transform);

            if (_isMirrorModeEnabled && BoneData.HasAutomationTrust(codename, BoneAutomationTrust.MirrorSafe) && bone.Basis?.TwinBone != null)
            {
                _editorManager.ModifyBoneTransform(
                    bone.Basis.TwinBone.BoneName,
                    BoneData.IsIVCSCompatibleBone(codename)
                        ? transform.GetSpecialReflection()
                        : transform.GetStandardReflection()
                );
            }

            if (_commitHistoryAfterWrite)
            {
                _editorManager.CommitEditTransaction();
                _commitHistoryAfterWrite = false;
            }
        }

        ImGui.TableNextRow();
    }

    private void RenderPropagationFalloffRow(EditRowParams bone, BoneTransform transform)
    {
        var codename = bone.BoneCodeName;
        var displayName = bone.BoneDisplayName;
        var propagationFalloff = transform.PropagationFalloff;
        bool falloffChanged = false;

        using var id = ImRaii.PushId($"{codename}_falloff");

        ImGui.TableNextColumn();
        ImGui.SetCursorPosX(_propagateButtonXPos);
        ImGui.Dummy(new Vector2(CtrlHelper.IconButtonWidth * 0.75f, 0));

        using (var disabled = ImRaii.Disabled(!_isUnlocked))
        {
            ImGui.TableNextColumn();
            ImGui.SetNextItemWidth(-1);
            if (ImGui.DragFloat("##PropagationFalloff", ref propagationFalloff, 0.01f, 0f, 1f, "%.2f"))
            {
                propagationFalloff = Math.Clamp(propagationFalloff, 0f, 1f);
                falloffChanged = true;
            }

            if (ImGui.IsItemActivated() && _pendingUndoSnapshot == null)
                _pendingUndoSnapshot = CaptureCurrentState();

            if (ImGui.IsItemDeactivatedAfterEdit())
                FinalizePendingEditTransaction(falloffChanged);

            ImGui.TableNextColumn();
            ImGui.TableNextColumn();
            ImGui.TableNextColumn();
        }

        ImGui.TableNextColumn();
        CtrlHelper.StaticLabel($"{displayName} - Propagation Falloff", CtrlHelper.TextAlignment.Left,
            "Each descendant keeps this fraction of the propagated change for each step away from the edited bone.");

        if (falloffChanged)
        {
            transform.PropagationFalloff = propagationFalloff;
            _editorManager.ModifyBoneTransform(codename, transform);

            if (_isMirrorModeEnabled && BoneData.HasAutomationTrust(codename, BoneAutomationTrust.MirrorSafe) && bone.Basis?.TwinBone != null)
            {
                _editorManager.ModifyBoneTransform(
                    bone.Basis.TwinBone.BoneName,
                    BoneData.IsIVCSCompatibleBone(codename)
                        ? transform.GetSpecialReflection()
                        : transform.GetStandardReflection()
                );
            }

            if (_commitHistoryAfterWrite)
            {
                _editorManager.CommitEditTransaction();
                _commitHistoryAfterWrite = false;
            }
        }

        ImGui.TableNextRow();
    }

    private Dictionary<string, BoneTransform> CaptureCurrentState()
    {
        _editorManager.BeginEditTransaction("Edit bone transform");
        return _editorManager.CaptureCurrentTemplateState();
    }

    private void SaveStateForUndo(Dictionary<string, BoneTransform> snapshot)
    {
        // The snapshot begins a named editor transaction. The matching commit occurs
        // after the normal TemplateManager mutation, so slider drags remain one entry.
        _editorManager.BeginEditTransaction("Edit bone transform");
        _commitHistoryAfterWrite = true;
    }

    private void FinalizePendingEditTransaction(bool changed)
    {
        if (_pendingUndoSnapshot == null)
            return;

        if (changed)
            SaveStateForUndo(_pendingUndoSnapshot);
        else
            _editorManager.CancelEditTransaction();

        _pendingUndoSnapshot = null;
    }

    private void RestoreState(Dictionary<string, BoneTransform> state)
    {
        _editorManager.ReplaceEditedTemplateState(state);
    }

    private float GetControlColumnWidth()
    {
        var iconWidth = CtrlHelper.IconButtonWidth;
        var spacing = ImGui.GetStyle().ItemSpacing.X;

        if (_editingAttribute == BoneAttribute.Scale)
        {
            var fixedWidths = (0.75f * iconWidth) + (5f * iconWidth) + (0.25f * iconWidth) + (3f * iconWidth * 0.8f);
            var spacingWidth = 9f * spacing;
            return fixedWidths + spacingWidth + (iconWidth * 0.75f);
        }

        var compactWidths = (0.75f * iconWidth) + (5f * iconWidth);
        var compactSpacing = 5f * spacing;
        return compactWidths + compactSpacing + (iconWidth * 0.5f);
    }

    #endregion
}

/// <summary>
/// Simple structure for representing arguments to the editor table.
/// Can be constructed with or without access to a live armature.
/// </summary>
internal struct EditRowParams
{
    public string BoneCodeName;
    public string BoneDisplayName => BoneData.GetBoneDisplayName(BoneCodeName);
    public BoneTransform Transform;
    public ModelBone? Basis = null;

    public EditRowParams(ModelBone mb, BoneTransform? overrideTransform = null)
    {
        BoneCodeName = mb.BoneName;
        Transform = overrideTransform != null
            ? new BoneTransform(overrideTransform)
            : mb.CustomizedTransform ?? new BoneTransform();
        Basis = mb;
    }

    public EditRowParams(string codename, BoneTransform tr)
    {
        BoneCodeName = codename;
        Transform = tr;
        Basis = null;
    }
}

internal sealed record UnknownBoneWorkbenchRow(
    string BoneName,
    string DisplayName,
    string SupportLabel,
    bool InTemplate,
    bool Edited,
    string? PackName,
    string RiskNote);

internal sealed record TemplateHealthReport(
    TemplateHealthSummary Summary,
    ProportionDashboardReport ProportionDashboard,
    IReadOnlyList<TemplateHealthDeltaRow> Rows,
    int Signature);

internal sealed record TemplateHealthSummary(
    int EditedBoneCount,
    int MissingEditedBoneCount,
    int UnknownEditedBoneCount,
    int RiskyEditedBoneCount,
    int LockedRowCount,
    int PinnedAxisCount,
    int PropagationCount,
    int LeftRightAsymmetryCount,
    int LiveBoneCount,
    int MetadataPackCount,
    int MetadataEntryCount);

internal sealed record TemplateHealthDeltaRow(
    string BoneName,
    string DisplayName,
    string Family,
    string SupportLabel,
    bool InTemplate,
    bool InLiveSkeleton,
    bool IsEdited,
    bool IsMissingLiveBone,
    bool IsUnknown,
    bool IsIvcs,
    bool IsRisky,
    bool IsAsymmetric,
    BoneLockState LockState,
    int PinnedAxisCount,
    bool IsPropagated,
    float PositionDelta,
    float RotationDelta,
    float ScaleDelta,
    float ChildScaleDelta,
    string? MirrorBoneName,
    float? MirrorDelta,
    string ProtectionSummary,
    string PropagationSummary,
    string Note)
{
    public bool IsLockedOrPinned => LockState != BoneLockState.Unlocked || PinnedAxisCount > 0;
}

internal sealed record ProportionDashboardReport(
    string OverallStatus,
    string Summary,
    IReadOnlyList<ProportionDashboardItem> Items);

internal sealed record ProportionDashboardItem(
    string Label,
    string Value,
    string Status,
    ProportionSeverity Severity,
    string Note);

internal enum ProportionSeverity
{
    Balanced = 0,
    Mild = 1,
    Strong = 2,
    Extreme = 3,
    Review = 4
}
