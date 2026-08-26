// Copyright (c) Customize+.
// Licensed under the MIT license.

using CustomizePlus.Armatures.Data;
using CustomizePlus.Armatures.Services;
using CustomizePlus.Configuration.Data;
using CustomizePlus.Core.Data;
using CustomizePlus.Core.Helpers;
using CustomizePlus.Core.Services;
using CustomizePlus.Game.Services;
using CustomizePlus.Templates;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.ImGuiNotification;
using Dalamud.Interface.Utility;
using Dalamud.Plugin;
using OtterGui;
using OtterGui.Classes;
using OtterGui.Raii;
using OtterGui.Text;
using OtterGui.Widgets;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using Penumbra.GameData.Interop;
using Penumbra.GameData.Enums;

namespace CustomizePlus.UI.Windows.MainWindow.Tabs;

public class SettingsTab
{
    private const uint DiscordColor = 0xFFDA8972;
    private const uint DonateColor = 0xFF5B5EFF;
    private static readonly AdvancedBodyRegion[] RegionOrder =
    {
        AdvancedBodyRegion.Spine,
        AdvancedBodyRegion.NeckShoulder,
        AdvancedBodyRegion.Chest,
        AdvancedBodyRegion.Pelvis,
        AdvancedBodyRegion.Arms,
        AdvancedBodyRegion.Hands,
        AdvancedBodyRegion.Legs,
        AdvancedBodyRegion.Feet,
        AdvancedBodyRegion.Toes,
        AdvancedBodyRegion.Tail
    };

    private readonly IDalamudPluginInterface _pluginInterface;
    private readonly PluginConfiguration _configuration;
    private readonly ArmatureManager _armatureManager;
    private readonly HookingService _hookingService;
    private readonly TemplateEditorManager _templateEditorManager;
    private readonly CPlusChangeLog _changeLog;
    private readonly MessageService _messageService;
    private readonly SupportLogBuilderService _supportLogBuilderService;
    private readonly ActivityLogService _activityLogService;
    private readonly PcpService _pcpService;
    private readonly GameObjectService _gameObjectService;
#if DEBUG
    private readonly RuntimeEvidenceService _runtimeEvidenceService;
    private string _runtimeEvidenceLabel = "capture";
    private string? _selectedRuntimeEvidencePath;
#endif
    private Race _neckPresetRace = Race.Elezen;
    private Race _lastDetectedNeckPresetRace = Race.Unknown;
    private bool _followDetectedNeckPresetRace;
    private ActivityLogCategory? _activityLogFilter;
    private long? _selectedActivityLogEntryId;

    public SettingsTab(
        IDalamudPluginInterface pluginInterface,
        PluginConfiguration configuration,
        ArmatureManager armatureManager,
        HookingService hookingService,
        TemplateEditorManager templateEditorManager,
        CPlusChangeLog changeLog,
        MessageService messageService,
        SupportLogBuilderService supportLogBuilderService,
        ActivityLogService activityLogService,
        PcpService pcpService,
        GameObjectService gameObjectService
#if DEBUG
        , RuntimeEvidenceService runtimeEvidenceService
#endif
        )
    {
        _pluginInterface = pluginInterface;
        _configuration = configuration;
        _armatureManager = armatureManager;
        _hookingService = hookingService;
        _templateEditorManager = templateEditorManager;
        _changeLog = changeLog;
        _messageService = messageService;
        _supportLogBuilderService = supportLogBuilderService;
        _activityLogService = activityLogService;
        _pcpService = pcpService;
        _gameObjectService = gameObjectService;
#if DEBUG
        _runtimeEvidenceService = runtimeEvidenceService;
#endif
        _followDetectedNeckPresetRace = configuration.UISettings.FollowDetectedNeckPresetRace;
    }

    public void Draw()
    {
        UiHelpers.SetupCommonSizes();
        using var child = ImRaii.Child("MainWindowChild");
        if (!child)
            return;

        DrawGeneralSettings();

        ImGui.NewLine();
        ImGui.NewLine();
        ImGui.NewLine();
        ImGui.NewLine();

        using (var child2 = ImRaii.Child("SettingsChild"))
        {
            DrawProfileApplicationSettings();
            DrawInterface();
            DrawCommands();
            DrawExternal();
            DrawAdvancedSettings();
            DrawActivityLog();
        }

        DrawSupportButtons();
    }

    #region General Settings
    // General Settings
    private void DrawGeneralSettings()
    {
        DrawPluginEnabledCheckbox();
    }

    private void DrawPluginEnabledCheckbox()
    {
        using (var disabled = ImRaii.Disabled(_templateEditorManager.IsEditorActive))
        {
            var isChecked = _configuration.PluginEnabled;

            //users doesn't really need to know what exactly this checkbox does so we just tell them it toggles all profiles
            if (CtrlHelper.CheckboxWithTextAndHelp("##pluginenabled", "Enable Customize+",
                    "Globally enables or disables all plugin functionality.", ref isChecked))
            {
                _configuration.PluginEnabled = isChecked;
                _configuration.Save();
                _hookingService.ReloadHooks();
                _activityLogService.Record(
                    ActivityLogCategory.Settings,
                    "Plugin enabled state changed",
                    $"Customize+ was {(isChecked ? "enabled" : "disabled")}.");
            }
        }
    }
    #endregion

    #region Profile application settings
    private void DrawProfileApplicationSettings()
    {
        var isShouldDraw = ImGui.CollapsingHeader("Profile Application");

        if (!isShouldDraw)
            return;

        DrawApplyInCharacterWindowCheckbox();
        DrawApplyInTryOnCheckbox();
        DrawApplyInCardsCheckbox();
        DrawApplyInInspectCheckbox();
        DrawApplyInLobbyCheckbox();
    }

    private void DrawApplyInCharacterWindowCheckbox()
    {
        var isChecked = _configuration.ProfileApplicationSettings.ApplyInCharacterWindow;

        if (CtrlHelper.CheckboxWithTextAndHelp("##applyincharwindow", "Apply Profiles in Character Window",
                "Apply profile for your character in your main character window, if it is set.", ref isChecked))
        {
            _configuration.ProfileApplicationSettings.ApplyInCharacterWindow = isChecked;
            _configuration.Save();
            _armatureManager.RebindAllArmatures();
        }
    }

    private void DrawApplyInTryOnCheckbox()
    {
        var isChecked = _configuration.ProfileApplicationSettings.ApplyInTryOn;

        if (CtrlHelper.CheckboxWithTextAndHelp("##applyintryon", "Apply Profiles in Try-On Window",
                "Apply profile for your character in your try-on, dye preview or glamour plate window, if it is set.", ref isChecked))
        {
            _configuration.ProfileApplicationSettings.ApplyInTryOn = isChecked;
            _configuration.Save();
            _armatureManager.RebindAllArmatures();
        }
    }

    private void DrawApplyInCardsCheckbox()
    {
        var isChecked = _configuration.ProfileApplicationSettings.ApplyInCards;

        if (CtrlHelper.CheckboxWithTextAndHelp("##applyincards", "Apply Profiles in Adventurer Cards",
                "Apply appropriate profile for the adventurer card you are currently looking at.", ref isChecked))
        {
            _configuration.ProfileApplicationSettings.ApplyInCards = isChecked;
            _configuration.Save();
            _armatureManager.RebindAllArmatures();
        }
    }

    private void DrawApplyInInspectCheckbox()
    {
        var isChecked = _configuration.ProfileApplicationSettings.ApplyInInspect;

        if (CtrlHelper.CheckboxWithTextAndHelp("##applyininspect", "Apply Profiles in Inspect Window",
                "Apply appropriate profile for the character you are currently inspecting.", ref isChecked))
        {
            _configuration.ProfileApplicationSettings.ApplyInInspect = isChecked;
            _configuration.Save();
            _armatureManager.RebindAllArmatures();
        }
    }

    private void DrawApplyInLobbyCheckbox()
    {
        var isChecked = _configuration.ProfileApplicationSettings.ApplyInLobby;

        if (CtrlHelper.CheckboxWithTextAndHelp("##applyinlobby", "Apply Profiles on Character Select Screen",
                "Apply appropriate profile for the character you have currently selected on character select screen during login.", ref isChecked))
        {
            _configuration.ProfileApplicationSettings.ApplyInLobby = isChecked;
            _configuration.Save();
            _armatureManager.RebindAllArmatures();
        }
    }
    #endregion

    #region Chat Commands Settings
    private void DrawCommands()
    {
        var isShouldDraw = ImGui.CollapsingHeader("Chat Commands");

        if (!isShouldDraw)
            return;

        DrawPrintSuccessMessages();
    }

    private void DrawPrintSuccessMessages()
    {
        var isChecked = _configuration.CommandSettings.PrintSuccessMessages;

        if (CtrlHelper.CheckboxWithTextAndHelp("##displaychatcommandconfirms", "Print Successful Command Execution Messages to Chat",
                "Controls whether successful execution of chat commands will be acknowledged by separate chat message or not.", ref isChecked))
        {
            _configuration.CommandSettings.PrintSuccessMessages = isChecked;
            _configuration.Save();
        }
    }
    #endregion

    #region Interface Settings

    private void DrawInterface()
    {
        var isShouldDraw = ImGui.CollapsingHeader("Interface");

        if (!isShouldDraw)
            return;

        DrawOpenWindowAtStart();
        DrawHideWindowInCutscene();
        DrawHideWindowWhenUiHidden();
        DrawHideWindowInGPose();

        UiHelpers.DefaultLineSpace();

        DrawFoldersDefaultOpen();

        UiHelpers.DefaultLineSpace();

        DrawSetPreviewToCurrentCharacterOnLogin();

        UiHelpers.DefaultLineSpace();

        if (Widget.DoubleModifierSelector("Template Deletion Modifier",
            "A modifier you need to hold while clicking the Delete Template button for it to take effect.", 100 * ImGuiHelpers.GlobalScale,
            _configuration.UISettings.DeleteTemplateModifier, v => _configuration.UISettings.DeleteTemplateModifier = v))
            _configuration.Save();
    }

    private void DrawOpenWindowAtStart()
    {
        var isChecked = _configuration.UISettings.OpenWindowAtStart;

        if (CtrlHelper.CheckboxWithTextAndHelp("##openwindowatstart", "Open Customize+ Window at Game Start",
                "Controls whether main Customize+ window will be opened when you launch the game or not.", ref isChecked))
        {
            _configuration.UISettings.OpenWindowAtStart = isChecked;

            _configuration.Save();
        }
    }

    private void DrawHideWindowInCutscene()
    {
        var isChecked = _configuration.UISettings.HideWindowInCutscene;

        if (CtrlHelper.CheckboxWithTextAndHelp("##hidewindowincutscene", "Hide Plugin Windows in Cutscenes",
                "Controls whether any Customize+ windows are hidden during cutscenes or not.", ref isChecked))
        {
            _pluginInterface.UiBuilder.DisableCutsceneUiHide = !isChecked;
            _configuration.UISettings.HideWindowInCutscene = isChecked;

            _configuration.Save();
        }
    }

    private void DrawHideWindowWhenUiHidden()
    {
        var isChecked = _configuration.UISettings.HideWindowWhenUiHidden;

        if (CtrlHelper.CheckboxWithTextAndHelp("##hidewindowwhenuihidden", "Hide Plugin Windows when UI is Hidden",
                "Controls whether any Customize+ windows are hidden when you manually hide the in-game user interface.", ref isChecked))
        {
            _pluginInterface.UiBuilder.DisableUserUiHide = !isChecked;
            _configuration.UISettings.HideWindowWhenUiHidden = isChecked;
            _configuration.Save();
        }
    }

    private void DrawHideWindowInGPose()
    {
        var isChecked = _configuration.UISettings.HideWindowInGPose;

        if (CtrlHelper.CheckboxWithTextAndHelp("##hidewindowingpose", "Hide Plugin Windows in GPose",
                "Controls whether any Customize+ windows are hidden when you enter GPose.", ref isChecked))
        {
            _pluginInterface.UiBuilder.DisableGposeUiHide = !isChecked;
            _configuration.UISettings.HideWindowInGPose = isChecked;
            _configuration.Save();
        }
    }

    private void DrawFoldersDefaultOpen()
    {
        var isChecked = _configuration.UISettings.FoldersDefaultOpen;

        if (CtrlHelper.CheckboxWithTextAndHelp("##foldersdefaultopen", "Open All Folders by Default",
                "Controls whether folders in template and profile lists are open by default or not.", ref isChecked))
        {
            _configuration.UISettings.FoldersDefaultOpen = isChecked;
            _configuration.Save();
        }
    }

    private void DrawSetPreviewToCurrentCharacterOnLogin()
    {
        var isChecked = _configuration.EditorConfiguration.SetPreviewToCurrentCharacterOnLogin;

        if (CtrlHelper.CheckboxWithTextAndHelp("##setpreviewcharaonlogin", "Automatically Set Current Character as Editor Preview Character",
                "Controls whether editor character will be automatically set to the current character during login.", ref isChecked))
        {
            _configuration.EditorConfiguration.SetPreviewToCurrentCharacterOnLogin = isChecked;
            _configuration.Save();
        }
    }

    #endregion

    #region Integrations

    private void DrawExternal()
    {
        var isShouldDraw = ImGui.CollapsingHeader("Integrations");

        if (!isShouldDraw)
            return;

        DrawHandlePCP();
    }

    private void DrawHandlePCP()
    {
        var isChecked = _configuration.IntegrationSettings.PenumbraPCPIntegrationEnabled;

        if (CtrlHelper.CheckboxWithTextAndHelp("##pcpintegrationenabled", "Enable Penumbra PCP integration",
            "Controls whether C+ will add the currently active profile data from an actor to .pcp files upon creation, and construct new profile for said actor upon import.", ref isChecked))
        {
            _configuration.IntegrationSettings.PenumbraPCPIntegrationEnabled = isChecked;
            _pcpService.SetEnabled(isChecked);
            _configuration.Save();
        }
    }

    #endregion

    #region Advanced Settings
    // Advanced Settings
    private void DrawAdvancedSettings()
    {
        var isShouldDraw = ImGui.CollapsingHeader("Advanced");

        if (!isShouldDraw)
            return;

        ImGui.NewLine();
        CtrlHelper.LabelWithIcon(FontAwesomeIcon.ExclamationTriangle,
            "These are advanced settings. Enable them at your own risk.");
        ImGui.NewLine();

        DrawRuntimeAndSafetySettings();
        ImGui.Spacing();
        DrawAdvancedBodyScalingSettings();
        ImGui.Spacing();
        DrawDebugModeCheckbox();
        DrawSkeletonCapabilityManifestDebug();
    }

    private void DrawActivityLog()
    {
        if (!ImGui.CollapsingHeader("Activity Log"))
            return;

        ImGui.TextDisabled($"Current-session local history only. Keeps the last {ActivityLogService.Capacity} meaningful actions; it is not saved, synced, or used for rollback.");

        var filterLabel = _activityLogFilter.HasValue
            ? ActivityLogService.GetCategoryLabel(_activityLogFilter.Value)
            : "All";
        ImGui.SetNextItemWidth(MathF.Min(220 * ImGuiHelpers.GlobalScale, ImGui.GetContentRegionAvail().X));
        if (ImGui.BeginCombo("Category", filterLabel))
        {
            if (ImGui.Selectable("All", !_activityLogFilter.HasValue))
                _activityLogFilter = null;

            foreach (var category in Enum.GetValues<ActivityLogCategory>())
            {
                var selected = _activityLogFilter == category;
                if (ImGui.Selectable(ActivityLogService.GetCategoryLabel(category), selected))
                    _activityLogFilter = category;

                if (selected)
                    ImGui.SetItemDefaultFocus();
            }

            ImGui.EndCombo();
        }

        var entries = _activityLogService.Entries
            .Where(entry => !_activityLogFilter.HasValue || entry.Category == _activityLogFilter.Value)
            .ToList();
        var selectedEntry = entries.FirstOrDefault(entry => entry.Id == _selectedActivityLogEntryId);

        ImGui.SameLine();
        using (ImRaii.Disabled(selectedEntry == null))
        {
            if (ImGui.Button("Copy selected entry") && selectedEntry != null)
            {
                ImGui.SetClipboardText(ActivityLogService.FormatEntry(selectedEntry));
                _messageService.NotificationMessage("Copied activity entry to clipboard.", NotificationType.Success, false);
            }
        }

        ImGui.SameLine();
        using (ImRaii.Disabled(_activityLogService.Entries.Count == 0))
        {
            if (ImGui.Button("Copy activity log"))
            {
                ImGui.SetClipboardText(_activityLogService.BuildClipboardText(_activityLogService.Entries));
                _messageService.NotificationMessage("Copied activity log to clipboard.", NotificationType.Success, false);
            }
        }

        ImGui.SameLine();
        using (ImRaii.Disabled(_activityLogService.Entries.Count == 0))
        {
            if (ImGui.Button("Clear activity log"))
            {
                _activityLogService.Clear();
                _selectedActivityLogEntryId = null;
            }
        }

        if (entries.Count == 0)
        {
            ImGui.TextDisabled("No matching activity has been recorded in this session yet.");
            return;
        }

        var height = MathF.Min(250 * ImGuiHelpers.GlobalScale, MathF.Max(120 * ImGuiHelpers.GlobalScale, entries.Count * ImGui.GetFrameHeightWithSpacing()));
        using var child = ImRaii.Child("ActivityLogEntries", new Vector2(0, height), true);
        if (!child)
            return;

        using var table = ImRaii.Table("ActivityLogTable", 4, ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerV | ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.ScrollY);
        if (!table)
            return;

        ImGui.TableSetupColumn("Time", ImGuiTableColumnFlags.WidthFixed, 78 * ImGuiHelpers.GlobalScale);
        ImGui.TableSetupColumn("Category", ImGuiTableColumnFlags.WidthFixed, 125 * ImGuiHelpers.GlobalScale);
        ImGui.TableSetupColumn("Action", ImGuiTableColumnFlags.WidthFixed, 145 * ImGuiHelpers.GlobalScale);
        ImGui.TableSetupColumn("Summary", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableHeadersRow();

        foreach (var entry in entries)
        {
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            var selected = _selectedActivityLogEntryId == entry.Id;
            if (ImGui.Selectable($"{entry.Timestamp:HH:mm:ss}##ActivityLog{entry.Id}", selected, ImGuiSelectableFlags.SpanAllColumns))
                _selectedActivityLogEntryId = entry.Id;

            ImGui.TableNextColumn();
            ImGui.TextUnformatted(ActivityLogService.GetCategoryLabel(entry.Category));

            ImGui.TableNextColumn();
            ImGui.TextUnformatted(entry.Action);

            ImGui.TableNextColumn();
            ImGuiUtil.TextWrapped(entry.Summary);
            if (!string.IsNullOrWhiteSpace(entry.Detail))
                CtrlHelper.AddHoverText(entry.Detail);
        }
    }

    private void DrawRuntimeAndSafetySettings()
    {
        if (!ImGui.CollapsingHeader("Runtime & Safety", ImGuiTreeNodeFlags.DefaultOpen))
            return;

        ImGui.TextDisabled("General runtime editing, transition, and safety controls.");

        DrawEnableRootPositionCheckbox();
        DrawTransitionSpeedSlider();
        DrawSoftScaleLimitsCheckbox();
        DrawAutomaticChildScaleCompensationCheckbox();
    }

    private void DrawEnableRootPositionCheckbox()
    {
        var isChecked = _configuration.EditorConfiguration.RootPositionEditingEnabled;
        if (CtrlHelper.CheckboxWithTextAndHelp("##rootpos", "Root editing",
                "Enables ability to edit the root bones.", ref isChecked))
        {
            _configuration.EditorConfiguration.RootPositionEditingEnabled = isChecked;
            _configuration.Save();
        }
    }

    private void DrawDebugModeCheckbox()
    {
        var isChecked = _configuration.DebuggingModeEnabled;
        if (CtrlHelper.CheckboxWithTextAndHelp("##debugmode", "Debug mode",
                "Enables debug mode. Requires plugin restart for all features to become properly initialized.", ref isChecked))
        {
            _configuration.DebuggingModeEnabled = isChecked;
            _configuration.Save();
        }
    }

    private void DrawSkeletonCapabilityManifestDebug()
    {
        if (!_configuration.DebuggingModeEnabled || !ImGui.CollapsingHeader("Skeleton Capability Manifest (Debug)"))
            return;

        Armature? armature = null;
        if ((_templateEditorManager.IsEditorActive || _templateEditorManager.IsEditorPaused)
            && TryGetArmatureForCharacter(_templateEditorManager.Character, out var previewArmature))
            armature = previewArmature;
        else if (TryGetArmatureForCharacter(_gameObjectService.GetCurrentPlayerActorIdentifier(), out var selfArmature))
            armature = selfArmature;
        else
            armature = _armatureManager.Armatures.Values.FirstOrDefault(static candidate => candidate.IsBuilt);
        if (armature == null)
        {
            ImGui.TextDisabled("No published live armature is available yet.");
            return;
        }

        var manifest = armature.GetCapabilityManifestSnapshot();
        ImGui.TextDisabled("Diagnostic only. This never changes transform behavior, bone trust, or profile resolution.");
        DrawWrappedDisabledValue("Revision", $"{manifest.Revision}; native binding generation: {armature.NativeBindingGeneration}; binding current: {manifest.BindingCurrent}; stable observations: {manifest.StableObservations}");
        DrawWrappedDisabledValue("Profile binding", $"{armature.Profile}; template binding revision: {armature.TemplateBindingRevision}; resolved transforms: {armature.ResolvedBoneTransforms.Count}; active ModelBones: {armature.ActiveBones.Count}; pending rebind: {armature.IsPendingProfileRebind}");
        DrawWrappedDisabledValue("Revisions", $"armature {armature.ArmatureRevision}; native {armature.NativeBindingGeneration}; actor lifetime {armature.ActorLifetimeGeneration}; manifest {manifest.Revision}; profile resolution {armature.ProfileResolutionRevision}; deformation {armature.DeformationRevision}; diagnostics {armature.DiagnosticsRevision}; reacquisition pending {armature.IsAwaitingActorReacquisitionPublication}");
        DrawWrappedDisabledValue("Structural fingerprint", string.IsNullOrEmpty(manifest.StructuralFingerprint) ? "Unavailable" : manifest.StructuralFingerprint);
        DrawWrappedDisabledValue("Topology", $"{manifest.Topology.TotalBoneCount} bones, {manifest.Topology.PartialBoneCounts.Count} partials, {manifest.Topology.EmptyPartialIndices.Count} optional empty, roots {manifest.Topology.RootCount}, max depth {manifest.Topology.MaxDepth}, valid {manifest.Topology.IsValid}");
        DrawWrappedDisabledValue("Animation compatibility", manifest.AnimationCompatibility.ToString());
        DrawWrappedDisabledValue("Unknown custom bones", manifest.UnknownCustomBoneCount.ToString());

        foreach (var capability in new[]
                 {
                     SkeletonCapability.VanillaCore, SkeletonCapability.IVCS1, SkeletonCapability.IVCS2,
                     SkeletonCapability.YAS, SkeletonCapability.NFLB, SkeletonCapability.Skelomae,
                 })
        {
            var evidence = manifest.CapabilityEvidence.TryGetValue(capability, out var value)
                ? value
                : new SkeletonCapabilityEvidence(SkeletonCapabilityState.Absent, Array.Empty<string>(), Array.Empty<string>());
            ImGui.TextDisabled($"{capability}: {evidence.State}");
        }

        var quality = armature.DeformationQualityDiagnostics;
        var solver = quality.Solver;
        DrawWrappedDisabledValue("Automatic body support", solver.ActiveRegions.Count == 0
            ? "Inactive (no automatic region contribution for the current resolved transforms)."
            : $"regions {string.Join(", ", solver.ActiveRegions)}; primary/support/transition/secondary {solver.PrimaryContributionCount}/{solver.SupportContributionCount}/{solver.TransitionContributionCount}/{solver.SecondaryContributionCount}; bilateral normalizations {solver.BilateralNormalizationCount}; duplicate suppression {solver.DoubleContributionPreventionCount}; clamps {solver.ClampedContributionCount}; fallbacks {solver.FallbackCount}");
        DrawWrappedDisabledValue("Body-shaping quality", $"max bilateral {quality.MaxBilateralDifference:0.000} ({quality.MaxBilateralPair}); max continuity {quality.MaxContinuityDifference:0.000} ({quality.MaxContinuityBoundary}); secondary magnitude {solver.SecondaryContributionMagnitude:0.000}");
        DrawWrappedDisabledValue("Proportional balance", $"enabled {solver.ProportionalBalanceEnabled}; strength {solver.ProportionalBalanceStrength:0.00}; relationships {(solver.CorrectedRelationships.Count == 0 ? "none" : string.Join(", ", solver.CorrectedRelationships))}; max correction {solver.MaximumProportionalCorrection:0.000}; imbalance {solver.MaximumProportionalImbalanceBefore:0.000}->{solver.MaximumProportionalImbalanceAfter:0.000}; skipped explicit/locked {solver.ProportionalSkippedExplicitOrLockedCount}");
        DrawWrappedDisabledValue("Surface smoothness", $"enabled {solver.SurfaceSmoothnessEnabled}; strength {solver.SurfaceSmoothnessStrength:0.00}; affected bones {solver.SurfaceSmoothnessAffectedBoneCount}; regions {(solver.SurfaceSmoothnessRegions.Count == 0 ? "none" : string.Join(", ", solver.SurfaceSmoothnessRegions))}; gradient {solver.MaximumPreSmoothingGradient:0.000}->{solver.MaximumPostSmoothingGradient:0.000}; boundary skips {solver.SurfaceSmoothnessSkippedBoundaryCount}; magnitude error {solver.SurfaceMagnitudePreservationError:0.000}");
        DrawWrappedDisabledValue("Cross-section conditioning", $"enabled {solver.CrossSectionConditioningEnabled}; strength {solver.CrossSectionConditioningStrength:0.00}; affected bones {solver.CrossSectionAffectedBoneCount}; anisotropy {solver.MaximumCrossSectionAnisotropyBefore:0.000}->{solver.MaximumCrossSectionAnisotropyAfter:0.000}; max correction {solver.MaximumCrossSectionCorrection:0.000}; constrained/untrusted skips {solver.CrossSectionSkippedUntrustedOrConstrainedCount}");
        DrawWrappedDisabledValue("Shape fairness", $"enabled {solver.ShapeFairnessEnabled}; strength {solver.ShapeFairnessStrength:0.00}; chains {(solver.ShapeFairnessChains.Count == 0 ? "none" : string.Join(", ", solver.ShapeFairnessChains))}; affected bones {solver.ShapeFairnessAffectedBoneCount}; curvature {solver.MaximumFairnessSecondDifferenceBefore:0.000}->{solver.MaximumFairnessSecondDifferenceAfter:0.000}; max correction {solver.MaximumFairnessCorrection:0.000}; magnitude error {solver.FairnessMagnitudePreservationError:0.000}");
        DrawWrappedDisabledValue("Local volume intent", $"enabled {solver.LocalVolumeIntentEnabled}; strength {solver.LocalVolumeIntentStrength:0.00}; regions {(solver.LocalVolumeIntentRegions.Count == 0 ? "none" : string.Join(", ", solver.LocalVolumeIntentRegions))}; log-volume error {solver.MaximumVolumeErrorBefore:0.000}->{solver.MaximumVolumeErrorAfter:0.000}; max correction {solver.MaximumVolumeAxisCorrection:0.000}; constrained/untrusted skips {solver.LocalVolumeIntentSkippedUntrustedOrConstrainedCount}");
        var jointCorrectives = armature.PoseAwareJointCorrectiveDebugState;
        DrawWrappedDisabledValue("Pose-aware joint correctives", $"enabled {jointCorrectives.Enabled}; active {jointCorrectives.Active}; strength {jointCorrectives.Strength:0.00}; categories {(jointCorrectives.ActiveCategories.Count == 0 ? "none" : string.Join(", ", jointCorrectives.ActiveCategories))}; eligible/corrected joints {jointCorrectives.EligibleJointCount}/{jointCorrectives.CorrectedJointCount}; max weight/correction {jointCorrectives.MaximumPoseWeight:0.000}/{jointCorrectives.MaximumCorrection:0.000}; writes {jointCorrectives.WriteCount}; safety skips {jointCorrectives.SafetySkipCount}; {jointCorrectives.EvaluationMilliseconds:0.000} ms");
        DrawWrappedDisabledValue("Extension automation", $"IVCS2 {solver.AutomatedIvcs2Controls}; YAS {solver.AutomatedYasControls}; NFLB body {solver.AutomatedNflbBodyControls}; Skelomae body {solver.AutomatedSkelomaeBodyControls}; clothing 0; props 0; tongue 0; wings 0");

#if DEBUG
        DrawRuntimeEvidenceDebug(armature);
#endif

        if (ImGui.SmallButton("Copy manifest JSON"))
        {
            try
            {
                ImUtf8.SetClipboardText(manifest.ToDebugJson());
            }
            catch (Exception ex)
            {
                Plugin.Logger.Debug($"Could not copy skeleton capability manifest: {ex.GetType().Name}.");
            }
        }
        CtrlHelper.AddHoverText("Copies the current read-only capability manifest for support diagnostics.");

        if (ImGui.TreeNode("Capability evidence and warnings"))
        {
            foreach (var capability in manifest.CapabilityEvidence.OrderBy(static pair => pair.Key))
            {
                ImGui.TextDisabled($"{capability.Key}: {capability.Value.State}");
                if (capability.Value.Evidence.Count > 0)
                    DrawWrappedDisabledValue("Observed", string.Join(", ", capability.Value.Evidence));
                if (capability.Value.MissingExpected.Count > 0)
                    DrawWrappedDisabledValue("Missing / advisory", string.Join(", ", capability.Value.MissingExpected));
            }

            if (manifest.FamilyCounts.Count > 0)
                DrawWrappedDisabledValue("Family counts", string.Join(", ", manifest.FamilyCounts.OrderBy(static pair => pair.Key).Select(static pair => $"{pair.Key}: {pair.Value}")));
            if (manifest.UnknownCustomBoneNames.Count > 0)
                DrawWrappedDisabledValue("Unknown names", string.Join(", ", manifest.UnknownCustomBoneNames));
            if (manifest.Warnings.Count > 0)
                DrawWrappedDisabledValue("Warnings", string.Join(" | ", manifest.Warnings));
            ImGui.TreePop();
        }

        DrawSelfArmatureLifecycleTraceDebug();
    }

#if DEBUG
    private void DrawRuntimeEvidenceDebug(Armature armature)
    {
        if (!ImGui.TreeNode("Development evidence capture"))
            return;

        ImGui.TextDisabled("Local debug evidence only. Captures never affect profile resolution, transforms, or native writes.");
        ImGui.SetNextItemWidth(Math.Min(260f * ImGuiHelpers.GlobalScale, ImGui.GetContentRegionAvail().X));
        ImGui.InputText("Label", ref _runtimeEvidenceLabel, 64);
        ImGui.SameLine();
        if (ImGui.SmallButton("Capture current"))
            _runtimeEvidenceService.Capture(armature, _runtimeEvidenceLabel);
        CtrlHelper.AddHoverText("Writes a versioned JSON snapshot under the local plugin configuration development-evidence directory.");

        var captures = _runtimeEvidenceService.List();
        ImGui.TextDisabled($"Stored captures: {captures.Count}. Folder: {_runtimeEvidenceService.DirectoryPath}");
        if (captures.Count > 0 && ImGui.BeginTable("runtime-evidence", 3, ImGuiTableFlags.BordersInnerV | ImGuiTableFlags.SizingStretchProp))
        {
            ImGui.TableSetupColumn("Capture");
            ImGui.TableSetupColumn("Compare", ImGuiTableColumnFlags.WidthFixed, 110 * ImGuiHelpers.GlobalScale);
            ImGui.TableSetupColumn("Delete", ImGuiTableColumnFlags.WidthFixed, 60 * ImGuiHelpers.GlobalScale);
            ImGui.TableHeadersRow();
            foreach (var capture in captures.Take(12))
            {
                ImGui.TableNextRow();
                ImGui.TableSetColumnIndex(0);
                ImGui.TextUnformatted($"{capture.Record.Label} ({capture.Record.CapturedAtUtc:yyyy-MM-dd HH:mm:ss} UTC)");
                ImGui.TableSetColumnIndex(1);
                if (ImGui.SmallButton($"Compare##{capture.Path}"))
                    _selectedRuntimeEvidencePath = capture.Path;
                ImGui.TableSetColumnIndex(2);
                if (ImGui.SmallButton($"Delete##{capture.Path}"))
                    _runtimeEvidenceService.Delete(capture.Path);
            }
            ImGui.EndTable();
        }

        var selected = captures.FirstOrDefault(capture => string.Equals(capture.Path, _selectedRuntimeEvidencePath, StringComparison.Ordinal));
        if (selected != null)
        {
            var comparison = _runtimeEvidenceService.Compare(armature, selected);
            DrawWrappedDisabledValue("Latest comparison", comparison.Summary + (comparison.Differences.Count == 0 ? string.Empty : " " + string.Join(" | ", comparison.Differences)));
        }

        ImGui.TreePop();
    }
#endif

    private void DrawSelfArmatureLifecycleTraceDebug()
    {
        if (!ImGui.TreeNode("Self lifecycle trace (Debug)"))
            return;

        ImGui.TextDisabled("Bounded transition-only diagnostics for the local player. Capture a manual snapshot before Glamourer, after the broken state, and after toggling the profile.");
        if (ImGui.SmallButton("Capture self snapshot"))
            _armatureManager.CaptureDebugSelfLifecycleSnapshot();
        CtrlHelper.AddHoverText("Adds one diagnostic snapshot without changing profiles, templates, or transform behavior.");

        ImGui.SameLine();
        if (ImGui.SmallButton("Copy lifecycle trace"))
        {
            try
            {
                ImUtf8.SetClipboardText(_armatureManager.GetDebugSelfLifecycleTraceClipboardText());
            }
            catch (Exception ex)
            {
                Plugin.Logger.Debug($"Could not copy self lifecycle trace: {ex.GetType().Name}.");
            }
        }

        ImGui.SameLine();
        if (ImGui.SmallButton("Clear lifecycle trace"))
            _armatureManager.ClearDebugSelfLifecycleTrace();

        var entries = _armatureManager.GetDebugSelfLifecycleTrace();
        ImGui.TextDisabled($"Recent entries: {entries.Count} / 96");
        if (entries.Count == 0)
        {
            ImGui.TextDisabled("No self lifecycle transitions captured yet. Enable Debug mode, then wait for a self armature or capture a snapshot.");
            ImGui.TreePop();
            return;
        }

        using (var child = ImRaii.Child("SelfLifecycleTraceEntries", new Vector2(0, 230 * ImGuiHelpers.GlobalScale), true))
        {
            foreach (var entry in entries.Reverse())
                ImGui.TextWrapped(entry.ToDisplayLine());
        }

        ImGui.TreePop();
    }

    private void DrawTransitionSpeedSlider()
    {
        var value = _configuration.RuntimeBehaviorSettings.TransformTransitionSharpness;
        ImGui.SetNextItemWidth(260 * ImGuiHelpers.GlobalScale);
        if (ImGui.SliderFloat("Transition speed", ref value,
                CustomizePlus.Core.Data.Constants.MinTransformTransitionSharpness,
                CustomizePlus.Core.Data.Constants.MaxTransformTransitionSharpness,
                "%.1f"))
        {
            _configuration.RuntimeBehaviorSettings.TransformTransitionSharpness = value;
            _configuration.Save();
        }

        CtrlHelper.AddHoverText(
            "Controls how quickly runtime bone edits settle into their target pose. Lower values are softer and slower; higher values are snappier.");
    }

    private void DrawSoftScaleLimitsCheckbox()
    {
        var isChecked = _configuration.RuntimeSafetySettings.SoftScaleLimitsEnabled;
        if (CtrlHelper.CheckboxWithTextAndHelp("##softscalelimits", "Runtime soft scale limits",
                "Applies conservative runtime-only scale guardrails to sensitive bone families to reduce inversion and severe collapse. Saved templates are not modified.", ref isChecked))
        {
            _configuration.RuntimeSafetySettings.SoftScaleLimitsEnabled = isChecked;
            _configuration.Save();
            _armatureManager.RebindAllArmatures();
        }
    }

    private void DrawAutomaticChildScaleCompensationCheckbox()
    {
        var isChecked = _configuration.RuntimeSafetySettings.AutomaticChildScaleCompensationEnabled;
        if (CtrlHelper.CheckboxWithTextAndHelp("##childscalecomp", "Automatic child scale compensation",
                "For sensitive propagated scale chains, dampens descendant scaling and lightly balances volume to reduce harsh collapses. Saved templates are not modified.", ref isChecked))
        {
            _configuration.RuntimeSafetySettings.AutomaticChildScaleCompensationEnabled = isChecked;
            _configuration.Save();
            _armatureManager.RebindAllArmatures();
        }
    }

    private void DrawAdvancedBodyScalingSettings()
    {
        if (!ImGui.CollapsingHeader("Advanced Body Scaling", ImGuiTreeNodeFlags.DefaultOpen))
            return;

        var settings = _configuration.AdvancedBodyScalingSettings;
        var isEnabled = settings.Enabled;

        ImGui.TextDisabled("Automation, balancing, validation, and advanced body-scaling subsystems.");

        if (CtrlHelper.CheckboxWithTextAndHelp("##advancedbodyscaling", "Enable advanced body scaling",
                "Enable the advanced body scaling pipeline with influence propagation, smoothing, and guardrails. Runtime only.", ref isEnabled))
        {
            var previousEnabled = settings.Enabled;
            settings.Enabled = isEnabled;
            _configuration.Save();
            _armatureManager.RebindAllArmatures();
            RecordGlobalAdvancedScalingChange(
                "Advanced Body Scaling",
                GetEnabledStateLabel(previousEnabled),
                GetEnabledStateLabel(isEnabled),
                "enabled");
        }

        using (var disabled = ImRaii.Disabled(!settings.Enabled))
        {
            DrawAdvancedSubsectionLabel("Automation & behavior");

            var mode = settings.Mode;
            if (ImGui.BeginCombo("Automation mode", mode.ToString()))
            {
                foreach (var value in Enum.GetValues<AdvancedBodyScalingMode>())
                {
                    var selected = value == mode;
                    if (ImGui.Selectable(value.ToString(), selected))
                    {
                        var previousMode = settings.Mode;
                        settings.Mode = value;
                        _configuration.Save();
                        _armatureManager.RebindAllArmatures();
                        RecordGlobalAdvancedScalingChange("Automation mode", previousMode.ToString(), value.ToString(), "automation-mode");
                    }

                    if (selected)
                        ImGui.SetItemDefaultFocus();
                }

                ImGui.EndCombo();
            }
            CtrlHelper.AddHoverText("Manual disables automation. Assist is light smoothing. Automatic runs full balancing. Strong is more aggressive.");

            ImGui.Spacing();
            DrawAdvancedSubsectionLabel("Balancing & naturalization");

            var surfaceBalancing = settings.SurfaceBalancingStrength;
            if (ImGui.SliderFloat("Surface balancing strength", ref surfaceBalancing, 0f, 1f, "%.2f"))
            {
                var previousSurfaceBalancing = settings.SurfaceBalancingStrength;
                settings.SurfaceBalancingStrength = surfaceBalancing;
                _configuration.Save();
                _armatureManager.RebindAllArmatures();
                RecordGlobalAdvancedScalingChange("Surface balancing strength", previousSurfaceBalancing, surfaceBalancing, "surface-balancing");
            }
            CtrlHelper.AddHoverText("Scales how strongly neighboring bones are smoothed. 0 disables, 1 uses the mode default.");

            var massRedistribution = settings.MassRedistributionStrength;
            if (ImGui.SliderFloat("Mass redistribution strength", ref massRedistribution, 0f, 1f, "%.2f"))
            {
                var previousMassRedistribution = settings.MassRedistributionStrength;
                settings.MassRedistributionStrength = massRedistribution;
                _configuration.Save();
                _armatureManager.RebindAllArmatures();
                RecordGlobalAdvancedScalingChange("Mass redistribution strength", previousMassRedistribution, massRedistribution, "mass-redistribution");
            }
            CtrlHelper.AddHoverText("Scales how much scale deltas are redistributed across neighboring bones. 0 disables, 1 uses the mode default.");

            var bilateralConsistencyEnabled = settings.BilateralConsistencyEnabled;
            if (ImGui.Checkbox("Enable bilateral consistency", ref bilateralConsistencyEnabled))
            {
                var previous = settings.BilateralConsistencyEnabled;
                settings.BilateralConsistencyEnabled = bilateralConsistencyEnabled;
                _configuration.Save();
                _armatureManager.RebindAllArmatures();
                RecordGlobalAdvancedScalingChange("Bilateral consistency", GetEnabledStateLabel(previous), GetEnabledStateLabel(bilateralConsistencyEnabled), "bilateral-consistency");
            }
            CtrlHelper.AddHoverText("Keeps corresponding left/right automatic shaping adjustments consistent when their authored inputs are equivalent. Intentionally asymmetric template edits are preserved.");

            var proportionalBalanceEnabled = settings.ProportionalBalanceEnabled;
            if (ImGui.Checkbox("Enable proportional balance", ref proportionalBalanceEnabled))
            {
                var previous = settings.ProportionalBalanceEnabled;
                settings.ProportionalBalanceEnabled = proportionalBalanceEnabled;
                _configuration.Save();
                _armatureManager.RebindAllArmatures();
                RecordGlobalAdvancedScalingChange("Proportional balance", GetEnabledStateLabel(previous), GetEnabledStateLabel(proportionalBalanceEnabled), "proportional-balance");
            }
            CtrlHelper.AddHoverText("Adds a small, bounded automatic support adjustment between related body regions. Explicit rows, locks, pinned axes, and deliberate contrasts remain authoritative.");

            using (ImRaii.Disabled(!settings.ProportionalBalanceEnabled))
            {
                var proportionalBalanceStrength = settings.ProportionalBalanceStrength;
                if (ImGui.SliderFloat("Proportional balance strength", ref proportionalBalanceStrength, 0f, 1f, "%.2f"))
                {
                    var previous = settings.ProportionalBalanceStrength;
                    settings.ProportionalBalanceStrength = proportionalBalanceStrength;
                    _configuration.Save();
                    _armatureManager.RebindAllArmatures();
                    RecordGlobalAdvancedScalingChange("Proportional balance strength", previous, proportionalBalanceStrength, "proportional-balance-strength");
                }
            }
            CtrlHelper.AddHoverText("Limits how strongly the automatic support field follows an explicitly requested neighboring region. This is not an ideal-proportions solver.");

            var surfaceSmoothnessEnabled = settings.SurfaceSmoothnessEnabled;
            if (ImGui.Checkbox("Enable surface smoothness", ref surfaceSmoothnessEnabled))
            {
                var previous = settings.SurfaceSmoothnessEnabled;
                settings.SurfaceSmoothnessEnabled = surfaceSmoothnessEnabled;
                _configuration.Save();
                _armatureManager.RebindAllArmatures();
                RecordGlobalAdvancedScalingChange("Surface smoothness", GetEnabledStateLabel(previous), GetEnabledStateLabel(surfaceSmoothnessEnabled), "surface-smoothness");
            }
            CtrlHelper.AddHoverText("Uses one lightweight bone-space smoothing pass over automatically generated body-support transforms. It never writes to explicit, locked, pinned, unknown, clothing, prop, wing, or tongue bones.");

            using (ImRaii.Disabled(!settings.SurfaceSmoothnessEnabled))
            {
                var surfaceSmoothnessStrength = settings.SurfaceSmoothnessStrength;
                if (ImGui.SliderFloat("Surface smoothness strength", ref surfaceSmoothnessStrength, 0f, 1f, "%.2f"))
                {
                    var previous = settings.SurfaceSmoothnessStrength;
                    settings.SurfaceSmoothnessStrength = surfaceSmoothnessStrength;
                    _configuration.Save();
                    _armatureManager.RebindAllArmatures();
                    RecordGlobalAdvancedScalingChange("Surface smoothness strength", previous, surfaceSmoothnessStrength, "surface-smoothness-strength");
                }
            }
            CtrlHelper.AddHoverText("Smooths abrupt automatic deformation gradients across curated body-region edges while preserving each region's automatic deformation magnitude.");

            DrawAdvancedShapeConditioningControl(
                settings,
                "Cross-section conditioning",
                "CrossSectionConditioning",
                "Conditions only automatic scale-axis distortion in log space. Explicit axes, locks, pins, unknown bones, clothing, props, wings, and tongue controls remain unchanged.",
                static value => value.CrossSectionConditioningEnabled,
                static (value, enabled) => value.CrossSectionConditioningEnabled = enabled,
                static value => value.CrossSectionConditioningStrength,
                static (value, strength) => value.CrossSectionConditioningStrength = strength);

            DrawAdvancedShapeConditioningControl(
                settings,
                "Shape fairness",
                "ShapeFairness",
                "Uses one bounded second-difference pass over curated automatic body chains. It reduces accidental kinks without flattening explicit anchors or intentional proportions.",
                static value => value.ShapeFairnessEnabled,
                static (value, enabled) => value.ShapeFairnessEnabled = enabled,
                static value => value.ShapeFairnessStrength,
                static (value, strength) => value.ShapeFairnessStrength = strength);

            DrawAdvancedShapeConditioningControl(
                settings,
                "Local volume intent",
                "LocalVolumeIntent",
                "Preserves requested local enlargement or reduction as a log-volume proxy. It does not force constant volume and only adjusts trusted automatic axes.",
                static value => value.LocalVolumeIntentEnabled,
                static (value, enabled) => value.LocalVolumeIntentEnabled = enabled,
                static value => value.LocalVolumeIntentStrength,
                static (value, strength) => value.LocalVolumeIntentStrength = strength);

            DrawAdvancedShapeConditioningControl(
                settings,
                "Pose-aware joint correctives",
                "PoseAwareJointCorrectives",
                "Adds lightweight, runtime-only scale support around trusted automatic elbow, knee, shoulder, and hip transitions. It never rebuilds static deformation state for animation changes.",
                static value => value.PoseAwareJointCorrectivesEnabled,
                static (value, enabled) => value.PoseAwareJointCorrectivesEnabled = enabled,
                static value => value.PoseAwareJointCorrectivesStrength,
                static (value, strength) => value.PoseAwareJointCorrectivesStrength = strength);

            var naturalization = settings.NaturalizationStrength;
            if (ImGui.SliderFloat("Naturalization strength", ref naturalization, 0f, 1f, "%.2f"))
            {
                var previousNaturalization = settings.NaturalizationStrength;
                settings.NaturalizationStrength = naturalization;
                _configuration.Save();
                _armatureManager.RebindAllArmatures();
                RecordGlobalAdvancedScalingChange("Naturalization strength", previousNaturalization, naturalization, "naturalization");
            }
            CtrlHelper.AddHoverText("Blends between your edits and the balanced result. 0 keeps your edits, 1 fully balances.");

            ImGui.Spacing();
            DrawAdvancedSubsectionLabel("Guardrails & validation");

            var guardrailMode = settings.GuardrailMode;
            if (ImGui.BeginCombo("Proportion guardrail mode", guardrailMode.ToString()))
            {
                foreach (var value in Enum.GetValues<AdvancedBodyScalingGuardrailMode>())
                {
                    var selected = value == guardrailMode;
                    if (ImGui.Selectable(value.ToString(), selected))
                    {
                        var previousGuardrailMode = settings.GuardrailMode;
                        settings.GuardrailMode = value;
                        _configuration.Save();
                        _armatureManager.RebindAllArmatures();
                        RecordGlobalAdvancedScalingChange("Proportion guardrail mode", previousGuardrailMode.ToString(), value.ToString(), "guardrail-mode");
                    }

                    if (selected)
                        ImGui.SetItemDefaultFocus();
                }

                ImGui.EndCombo();
            }
            CtrlHelper.AddHoverText("Controls how strict the body proportion guardrails are. Off disables guardrails.");

            var poseValidation = settings.PoseValidationMode;
            if (ImGui.BeginCombo("Pose-aware validation mode", poseValidation.ToString()))
            {
                foreach (var value in Enum.GetValues<AdvancedBodyScalingPoseValidationMode>())
                {
                    var selected = value == poseValidation;
                    if (ImGui.Selectable(value.ToString(), selected))
                    {
                        var previousPoseValidationMode = settings.PoseValidationMode;
                        settings.PoseValidationMode = value;
                        _configuration.Save();
                        _armatureManager.RebindAllArmatures();
                        RecordGlobalAdvancedScalingChange("Pose-aware validation mode", previousPoseValidationMode.ToString(), value.ToString(), "pose-validation-mode");
                    }

                    if (selected)
                        ImGui.SetItemDefaultFocus();
                }

                ImGui.EndCombo();
            }
            CtrlHelper.AddHoverText("Adds extra pose-aware guardrails to reduce deformation in extreme poses.");

            var animationSafeMode = settings.AnimationSafeModeEnabled;
            if (ImGui.Checkbox("Animation-safe mode", ref animationSafeMode))
            {
                var previousAnimationSafeMode = settings.AnimationSafeModeEnabled;
                settings.AnimationSafeModeEnabled = animationSafeMode;
                _configuration.Save();
                _armatureManager.RebindAllArmatures();
                RecordGlobalAdvancedScalingChange(
                    "Animation-safe mode",
                    GetEnabledStateLabel(previousAnimationSafeMode),
                    GetEnabledStateLabel(animationSafeMode),
                    "animation-safe-mode");
            }
            CtrlHelper.AddHoverText("Biases advanced scaling and RBF pose-space correctives toward safer, more motion-friendly behavior. It increases smoothing near joints, keeps extremities calmer, and makes corrective behavior more conservative without removing manual control.");

            ImGui.Spacing();
            DrawBoneImportanceWeightingSettings(settings);

            ImGui.Spacing();
            DrawNeckCompensationSettings(settings);

            ImGui.Spacing();
            DrawPoseSpaceCorrectives(settings);

            ImGui.Spacing();
            DrawFullIkRetargetingSettings(settings);

            ImGui.Spacing();
            DrawMotionWarpingSettings(settings);

            ImGui.Spacing();
            DrawFullBodyIkSettings(settings);

            ImGui.Spacing();
            DrawAdvancedBodyScalingRegionProfiles(settings);

            ImGui.Spacing();
            DrawAdvancedBodyScalingExplainability(settings);

            ImGui.Spacing();
            DrawAdvancedBodyScalingResets(settings);
        }
    }

    private static void DrawAdvancedSubsectionLabel(string label)
    {
        ImGui.Separator();
        ImGui.TextDisabled(label);
    }

    private void RecordGlobalAdvancedScalingChange(string setting, float previous, float current, string key)
        => RecordGlobalAdvancedScalingChange(setting, previous.ToString("0.00"), current.ToString("0.00"), key);

    private void RecordGlobalAdvancedScalingChange(string setting, string previous, string current, string key)
    {
        if (string.Equals(previous, current, StringComparison.Ordinal))
            return;

        _activityLogService.Record(
            ActivityLogCategory.AdvancedScaling,
            "Global setting changed",
            $"{setting}: {previous} -> {current}.",
            coalesceKey: $"global-advanced-scaling:{key}");
    }

    private static string GetEnabledStateLabel(bool value)
        => value ? "enabled" : "disabled";

    private void DrawBoneImportanceWeightingSettings(AdvancedBodyScalingSettings settings)
    {
        if (!ImGui.CollapsingHeader("Bone Importance Weighting"))
            return;

        ImGui.TextDisabled("Uses supported body model skinning data, when available, to make propagation, smoothing, redistribution, and guardrails more anatomically coherent. It now refines the map with approximate surface coverage and structural bone classification, stays on the existing transform-based path, and falls back safely.");

        var enabled = settings.ModelDerivedBoneImportanceEnabled;
        if (ImGui.Checkbox("Enable model-derived bone importance", ref enabled))
        {
            settings.ModelDerivedBoneImportanceEnabled = enabled;
            _configuration.Save();
            _armatureManager.RebindAllArmatures();
        }
        CtrlHelper.AddHoverText("When enabled, live supported human actors can use resolved body-model data to weight bone importance. If model data is unavailable, the current heuristic behavior is preserved.");

        using var disabled = ImRaii.Disabled(!settings.ModelDerivedBoneImportanceEnabled);

        var preferSkinWeights = settings.PreferTrueSkinWeightImportance;
        if (ImGui.Checkbox("Prefer true skin-weight aggregation", ref preferSkinWeights))
        {
            settings.PreferTrueSkinWeightImportance = preferSkinWeights;
            _configuration.Save();
            _armatureManager.RebindAllArmatures();
        }
        CtrlHelper.AddHoverText("Uses Stage 2 blend-weight aggregation when a supported blend-index/weight stream is available. Otherwise the system falls back to Stage 1 coarse mesh participation.");

        var fullOnSelf = settings.FullBoneImportanceOnSelf;
        if (ImGui.Checkbox("Always run full BIW on self", ref fullOnSelf))
        {
            settings.FullBoneImportanceOnSelf = fullOnSelf;
            _configuration.Save();
            _armatureManager.RebindAllArmatures();
        }
        CtrlHelper.AddHoverText("Self stays the highest-priority BIW actor. When disabled, the crowd-safe policy can still fall back to cached or heuristic behavior.");

        var fullOnProfiledActors = settings.FullBoneImportanceOnProfiledActors;
        if (ImGui.Checkbox("Run full BIW on actors assigned to a profile", ref fullOnProfiledActors))
        {
            settings.FullBoneImportanceOnProfiledActors = fullOnProfiledActors;
            _configuration.Save();
            _armatureManager.RebindAllArmatures();
        }
        CtrlHelper.AddHoverText("Actors with an explicit Customize+ profile stay ahead of crowd-safe downgrades. Default-profile-only actors do not count as explicitly profiled here.");

        var heuristicBlend = settings.BoneImportanceHeuristicBlend;
        if (ImGui.SliderFloat("Model weighting blend", ref heuristicBlend, 0f, 1f, "%.2f"))
        {
            settings.BoneImportanceHeuristicBlend = heuristicBlend;
            _configuration.Save();
            _armatureManager.RebindAllArmatures();
        }
        CtrlHelper.AddHoverText("Blends between the current heuristic behavior and the model-derived bone-importance map. Lower values stay closer to the old behavior; higher values trust the resolved model data more.");

        var liveArmature = _armatureManager.Armatures.Values
            .FirstOrDefault(armature => armature.ActiveAdvancedBodyScalingSettings?.Enabled == true);
        if (liveArmature != null)
        {
            var result = liveArmature.ActiveBoneImportanceResult;
            DrawWrappedDisabledValue("Live source", $"{result.LiveSourceLabel} ({result.StageLabel})");
            DrawWrappedDisabledValue("Live mode", $"{result.AggregateModeLabel} ({result.ContributingPartCount} contributing part{(result.ContributingPartCount == 1 ? string.Empty : "s")})");
            DrawWrappedDisabledValue("Crowd-safe mode", $"{result.VisibleRuntimeModeLabel} on {result.VisibleActorTierLabel} (full eligible {result.VisibleFullQualityEligible}, downgraded {result.VisibleCrowdSafeDowngraded}, stable-throttled {result.VisibleStableThrottled})");
            if (!string.IsNullOrWhiteSpace(result.ResolutionDetail))
                DrawWrappedDisabledValue("Resolution detail", result.ResolutionDetail);
            if (!string.IsNullOrWhiteSpace(result.ModelIdentity))
                DrawWrappedDisabledValue("Live model", $"{result.ModelIdentity} {(result.CacheHit ? "[cache hit]" : "[cache miss / fresh parse]")}");
            if (!string.IsNullOrWhiteSpace(result.RefreshStatus))
                DrawWrappedDisabledValue("Refresh", result.RefreshStatus);
            if (!string.IsNullOrWhiteSpace(result.VisibleRuntimeSummary))
                DrawWrappedDisabledValue("Runtime policy", result.VisibleRuntimeSummary);
            if (!string.IsNullOrWhiteSpace(result.RefinementSummary))
                DrawWrappedDisabledValue("Refinement", result.RefinementSummary);
            if (!string.IsNullOrWhiteSpace(result.ConfidenceSummary))
                DrawWrappedDisabledValue("Slot confidence", result.ConfidenceSummary);
            if (!string.IsNullOrWhiteSpace(result.RequestedGamePath))
                DrawWrappedDisabledValue("Requested game path", result.RequestedGamePath);
            if (!string.IsNullOrWhiteSpace(result.ModelPath))
                DrawWrappedDisabledValue("Resolved model path", result.ModelPath);
            if (result.PartDetails.Count > 0)
            {
                ImGui.TextDisabled("Contributing slots:");
                ImGui.Indent();
                foreach (var part in result.PartDetails.Take(4))
                    DrawWrappedDisabledBulletText(part);
                ImGui.Unindent();
            }
            if (result.MissingPartDetails.Count > 0)
            {
                ImGui.TextDisabled("Missing slots:");
                ImGui.Indent();
                foreach (var missing in result.MissingPartDetails.Take(2))
                    DrawWrappedDisabledBulletText(missing);
                ImGui.Unindent();
            }
            if (!string.IsNullOrWhiteSpace(result.Summary))
                DrawWrappedDisabledValue("Importance source", result.Summary);
            if (result.SampleValues.Count > 0)
            {
                ImGui.TextDisabled("Sample bones:");
                ImGui.Indent();
                foreach (var sample in result.SampleValues.Take(4))
                    DrawWrappedDisabledBulletText(sample);
                ImGui.Unindent();
            }
        }
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

    private void DrawNeckCompensationSettings(AdvancedBodyScalingSettings settings)
    {
        if (!ImGui.CollapsingHeader("Global Neck/Shoulder Baseline", ImGuiTreeNodeFlags.DefaultOpen))
            return;

        ImGui.TextDisabled("These are the default neck/shoulder compensation values used when no race-specific preset overrides them.");

        var neckLength = settings.NeckLengthCompensation;
        if (ImGui.SliderFloat("Neck length compensation", ref neckLength, 0f, 1f, "%.2f"))
        {
            var previousNeckLength = settings.NeckLengthCompensation;
            settings.NeckLengthCompensation = neckLength;
            _configuration.Save();
            _armatureManager.RebindAllArmatures();
            RecordGlobalAdvancedScalingChange("Neck length compensation", previousNeckLength, neckLength, "neck-length");
        }
        CtrlHelper.AddHoverText("Shortens neck length along its primary axis without shrinking width. 0 disables.");

        var blend = settings.NeckShoulderBlendStrength;
        if (ImGui.SliderFloat("Neck-to-shoulder blend", ref blend, 0f, 1f, "%.2f"))
        {
            var previousBlend = settings.NeckShoulderBlendStrength;
            settings.NeckShoulderBlendStrength = blend;
            _configuration.Save();
            _armatureManager.RebindAllArmatures();
            RecordGlobalAdvancedScalingChange("Neck-to-shoulder blend", previousBlend, blend, "neck-shoulder-blend");
        }
        CtrlHelper.AddHoverText("Blends the length correction into upper spine and shoulder roots to keep transitions smooth.");

        var clavicleSmoothing = settings.ClavicleShoulderSmoothing;
        if (ImGui.SliderFloat("Clavicle/shoulder bridge smoothing", ref clavicleSmoothing, 0f, 1f, "%.2f"))
        {
            var previousClavicleSmoothing = settings.ClavicleShoulderSmoothing;
            settings.ClavicleShoulderSmoothing = clavicleSmoothing;
            _configuration.Save();
            _armatureManager.RebindAllArmatures();
            RecordGlobalAdvancedScalingChange("Clavicle/shoulder smoothing", previousClavicleSmoothing, clavicleSmoothing, "clavicle-shoulder-smoothing");
        }
        CtrlHelper.AddHoverText("Adds extra smoothing across clavicles and shoulder roots to avoid abrupt transitions.");

        if (!ImGui.CollapsingHeader("Race-specific neck presets"))
            return;

        var useRacePresets = settings.UseRaceSpecificNeckCompensation;
        if (ImGui.Checkbox("Enable race-specific presets", ref useRacePresets))
        {
            var previousUseRacePresets = settings.UseRaceSpecificNeckCompensation;
            settings.UseRaceSpecificNeckCompensation = useRacePresets;
            _configuration.Save();
            _armatureManager.RebindAllArmatures();
            RecordGlobalAdvancedScalingChange(
                "Enable race-specific presets",
                GetEnabledStateLabel(previousUseRacePresets),
                GetEnabledStateLabel(useRacePresets),
                "race-presets-enabled");
        }
        CtrlHelper.AddHoverText("When enabled, race presets override the global neck/shoulder baseline for the detected actor race when a preset exists.");
        ImGui.TextDisabled("Race-specific presets override the global neck/shoulder baseline for the selected or detected race.");

        using var disabled = ImRaii.Disabled(!settings.UseRaceSpecificNeckCompensation);
        var detectedRace = GetDetectedPresetEditorRace();
        SyncDetectedPresetRace(settings, detectedRace);

        ImGui.TextDisabled($"Detected actor race: {GetRaceLabelOrUnknown(detectedRace)}");
        CtrlHelper.AddHoverText(
            "This is the race currently detected from the active preview actor when available, otherwise your current character.");

        var followDetectedRace = _followDetectedNeckPresetRace;
        if (ImGui.Checkbox("Follow detected actor race", ref followDetectedRace))
        {
            _followDetectedNeckPresetRace = followDetectedRace;
            _configuration.UISettings.FollowDetectedNeckPresetRace = _followDetectedNeckPresetRace;
            _configuration.Save();

            if (_followDetectedNeckPresetRace && TrySetPresetEditorRace(detectedRace) && settings.UseRaceSpecificNeckCompensation)
                _armatureManager.RebindAllArmatures();
        }
        CtrlHelper.AddHoverText(
            "Automatically switches the preset editor to the currently detected actor race. Manual selection stays available when disabled.");

        ImGui.SameLine();
        using (var applyDetectedDisabled = ImRaii.Disabled(detectedRace == Race.Unknown))
        {
            if (ImGui.Button("Use detected race"))
                TrySetPresetEditorRace(detectedRace);
        }

        var raceLabel = GetRaceLabel(_neckPresetRace);
        using (var followDisabled = ImRaii.Disabled(_followDetectedNeckPresetRace && detectedRace != Race.Unknown))
        {
            if (ImGui.BeginCombo("Preset race", raceLabel))
            {
                foreach (var race in Enum.GetValues<Race>())
                {
                    if (race == Race.Unknown)
                        continue;

                    var selected = race == _neckPresetRace;
                    if (ImGui.Selectable(GetRaceLabel(race), selected))
                        _neckPresetRace = race;

                    if (selected)
                        ImGui.SetItemDefaultFocus();
                }

                ImGui.EndCombo();
            }
        }

        ImGui.TextDisabled($"Editor target: {GetNeckPresetEditorTargetLabel(detectedRace)}");
        ImGui.TextDisabled($"Effective source: {GetEffectiveNeckPresetSourceLabel(settings, detectedRace)}");

        var presets = settings.RaceNeckPresets;
        AdvancedBodyScalingNeckCompensationPreset? preset = null;
        if (presets != null)
            presets.TryGetValue(_neckPresetRace, out preset);

        var hasPreset = preset != null;
        var baseline = new AdvancedBodyScalingNeckCompensationPreset
        {
            NeckLengthCompensation = settings.NeckLengthCompensation,
            NeckShoulderBlendStrength = settings.NeckShoulderBlendStrength,
            ClavicleShoulderSmoothing = settings.ClavicleShoulderSmoothing
        };

        var working = hasPreset ? preset! : baseline;

        var raceLength = working.NeckLengthCompensation;
        var previousRaceLength = raceLength;
        if (ImGui.SliderFloat("Race neck length compensation", ref raceLength, 0f, 1f, "%.2f"))
        {
            preset = EnsureRacePreset(settings, _neckPresetRace, baseline);
            preset.NeckLengthCompensation = raceLength;
            _configuration.Save();
            _armatureManager.RebindAllArmatures();
            RecordGlobalAdvancedScalingChange($"{GetRaceLabel(_neckPresetRace)} race neck length compensation", previousRaceLength, raceLength, $"race-{_neckPresetRace}-neck-length");
        }
        CtrlHelper.AddHoverText("Overrides the global neck length compensation baseline for this race preset.");

        var raceBlend = working.NeckShoulderBlendStrength;
        var previousRaceBlend = raceBlend;
        if (ImGui.SliderFloat("Race neck-to-shoulder blend", ref raceBlend, 0f, 1f, "%.2f"))
        {
            preset = EnsureRacePreset(settings, _neckPresetRace, baseline);
            preset.NeckShoulderBlendStrength = raceBlend;
            _configuration.Save();
            _armatureManager.RebindAllArmatures();
            RecordGlobalAdvancedScalingChange($"{GetRaceLabel(_neckPresetRace)} race neck-to-shoulder blend", previousRaceBlend, raceBlend, $"race-{_neckPresetRace}-neck-shoulder-blend");
        }
        CtrlHelper.AddHoverText("Overrides the global neck-to-shoulder blend baseline for this race preset.");

        var raceClavicle = working.ClavicleShoulderSmoothing;
        var previousRaceClavicle = raceClavicle;
        if (ImGui.SliderFloat("Race clavicle/shoulder smoothing", ref raceClavicle, 0f, 1f, "%.2f"))
        {
            preset = EnsureRacePreset(settings, _neckPresetRace, baseline);
            preset.ClavicleShoulderSmoothing = raceClavicle;
            _configuration.Save();
            _armatureManager.RebindAllArmatures();
            RecordGlobalAdvancedScalingChange($"{GetRaceLabel(_neckPresetRace)} race clavicle/shoulder smoothing", previousRaceClavicle, raceClavicle, $"race-{_neckPresetRace}-clavicle-smoothing");
        }
        CtrlHelper.AddHoverText("Overrides the global clavicle/shoulder smoothing baseline for this race preset.");

        if (ImGui.Button("Restore preset defaults"))
        {
            settings.RaceNeckPresets ??= new Dictionary<Race, AdvancedBodyScalingNeckCompensationPreset>();
            preset = AdvancedBodyScalingNeckCompensationPreset.CreateDefault(_neckPresetRace);
            settings.RaceNeckPresets[_neckPresetRace] = preset;
            hasPreset = true;
            working = preset;
            _configuration.Save();
            _armatureManager.RebindAllArmatures();
            _activityLogService.Record(
                ActivityLogCategory.AdvancedScaling,
                "Global race preset restored",
                $"Restored the {GetRaceLabel(_neckPresetRace)} race preset to shipped defaults.");
        }
        CtrlHelper.AddHoverText(
            "Restore preset defaults = restore this race preset to the plugin's shipped default values. This does not copy the current global baseline.");

        ImGui.SameLine();
        using (var clearDisabled = ImRaii.Disabled(!hasPreset))
        {
            if (ImGui.Button("Clear race preset"))
            {
                presets?.Remove(_neckPresetRace);
                hasPreset = false;
                preset = null;
                working = baseline;
                _configuration.Save();
                _armatureManager.RebindAllArmatures();
                _activityLogService.Record(
                    ActivityLogCategory.AdvancedScaling,
                    "Global race preset cleared",
                    $"Cleared the {GetRaceLabel(_neckPresetRace)} race preset; it now uses the global neck/shoulder baseline.");
            }
        }
        CtrlHelper.AddHoverText(
            "Clear race preset = remove the custom override entry for this race. If no race preset remains, runtime falls back to the global neck/shoulder baseline for that race.");

        ImGui.TextDisabled("Restore preset defaults writes the shipped race preset. Clear race preset removes the custom race entry and falls back to the global baseline.");

    }

    private static AdvancedBodyScalingNeckCompensationPreset EnsureRacePreset(
        AdvancedBodyScalingSettings settings,
        Race race,
        AdvancedBodyScalingNeckCompensationPreset baseline)
    {
        settings.RaceNeckPresets ??= new Dictionary<Race, AdvancedBodyScalingNeckCompensationPreset>();

        if (!settings.RaceNeckPresets.TryGetValue(race, out var preset))
        {
            preset = baseline.DeepCopy();
            settings.RaceNeckPresets[race] = preset;
        }

        return preset;
    }

    private static string GetRaceLabel(Race race)
        => race switch
        {
            Race.AuRa => "Au Ra",
            Race.Miqote => "Miqo'te",
            _ => race.ToString()
        };

    private static string GetRaceLabelOrUnknown(Race race)
        => race == Race.Unknown ? "Unknown" : GetRaceLabel(race);

    private string GetNeckPresetEditorTargetLabel(Race detectedRace)
    {
        if (_followDetectedNeckPresetRace)
            return detectedRace == Race.Unknown ? "Follow detected actor race (waiting for actor)" : $"Follow detected actor race ({GetRaceLabel(detectedRace)})";

        return $"Manual preset race ({GetRaceLabel(_neckPresetRace)})";
    }

    private string GetEffectiveNeckPresetSourceLabel(AdvancedBodyScalingSettings settings, Race detectedRace)
    {
        if (!settings.UseRaceSpecificNeckCompensation)
            return "Global baseline";

        if (detectedRace == Race.Unknown)
            return "Waiting for detected actor race";

        var hasPreset = settings.RaceNeckPresets != null && settings.RaceNeckPresets.ContainsKey(detectedRace);
        if (hasPreset)
            return _followDetectedNeckPresetRace
                ? $"Detected actor race preset ({GetRaceLabel(detectedRace)})"
                : $"{GetRaceLabel(detectedRace)} race preset";

        return $"Global baseline ({GetRaceLabel(detectedRace)} has no preset override)";
    }

    private void SyncDetectedPresetRace(AdvancedBodyScalingSettings settings, Race detectedRace)
    {
        if (_lastDetectedNeckPresetRace == detectedRace)
            return;

        _lastDetectedNeckPresetRace = detectedRace;
        if (!_followDetectedNeckPresetRace || !TrySetPresetEditorRace(detectedRace) || !settings.UseRaceSpecificNeckCompensation)
            return;

        _armatureManager.RebindAllArmatures();
    }

    private bool TrySetPresetEditorRace(Race race)
    {
        if (race == Race.Unknown || _neckPresetRace == race)
            return false;

        _neckPresetRace = race;
        return true;
    }

    private Race GetDetectedPresetEditorRace()
    {
        if ((_templateEditorManager.IsEditorActive || _templateEditorManager.IsEditorPaused) &&
            TryGetRaceForCharacter(_templateEditorManager.Character, out var previewRace))
            return previewRace;

        var currentPlayer = _gameObjectService.GetCurrentPlayerActorIdentifier().CreatePermanent();
        if (TryGetRaceForCharacter(currentPlayer, out var resolvedRace))
            return resolvedRace;

        return TryGetActorRace(_gameObjectService.GetLocalPlayerActor(), out var currentRace)
            ? currentRace
            : Race.Unknown;
    }

    private bool TryGetRaceForCharacter(Penumbra.GameData.Actors.ActorIdentifier character, out Race race)
    {
        foreach (var (_, actor) in _gameObjectService.FindActorsByIdentifierIgnoringOwnership(character))
        {
            if (TryGetActorRace(actor, out race))
                return true;
        }

        race = Race.Unknown;
        return false;
    }

    private static unsafe bool TryGetActorRace(Actor actor, out Race race)
    {
        race = Race.Unknown;

        if (!actor || !actor.IsCharacter)
            return false;

        var model = actor.Model;
        if (model && model.IsHuman)
        {
            var modelCustomize = model.GetCustomize();
            race = modelCustomize.Race;
            if (race != Race.Unknown)
                return true;
        }

        var customize = actor.Customize;
        if (customize == null)
            return false;

        race = customize->Race;
        return race != Race.Unknown;
    }


    private void DrawPoseSpaceCorrectives(AdvancedBodyScalingSettings settings)
    {
        if (!ImGui.CollapsingHeader("RBF Pose-Space Correctives"))
            return;

        var poseCorrectives = settings.PoseCorrectives;
        ImGui.TextDisabled("RBF Pose-Space Correctives use pose interpolation across stored sample poses to apply smoother, more natural transform-based corrections in common problem areas. This is a transform-based corrective system, not a mesh morph backend.");

        var enabled = poseCorrectives.Enabled;
        if (ImGui.Checkbox("Enable RBF Pose-Space Correctives", ref enabled))
        {
            poseCorrectives.Enabled = enabled;
            _configuration.Save();
            _armatureManager.RebindAllArmatures();
        }
        CtrlHelper.AddHoverText("Turns the RBF pose-space corrective layer on or off. These corrections stay transform-based, use supported bones only, and interpolate between stored sample poses instead of relying on crude binary thresholds.");

        ImGui.SameLine();
        if (ImGui.Button("Restore corrective defaults"))
        {
            settings.PoseCorrectives = new AdvancedBodyScalingPoseCorrectiveSettings();
            poseCorrectives = settings.PoseCorrectives;
            _configuration.Save();
            _armatureManager.RebindAllArmatures();
        }
        CtrlHelper.AddHoverText("Restores the shipped RBF corrective defaults: global enable, strength, pose-map sharpness, damping, clamp, and every per-region enable, strength, threshold, deadzone, smoothing, sample falloff, max clamp, and blend priority value.");

        using (var disabled = ImRaii.Disabled(!poseCorrectives.Enabled))
        {
            var strength = poseCorrectives.Strength;
            if (ImGui.SliderFloat("Global corrective strength", ref strength, 0f, 1f, "%.2f"))
            {
                poseCorrectives.Strength = strength;
                _configuration.Save();
                _armatureManager.RebindAllArmatures();
            }
            CtrlHelper.AddHoverText("Scales how strongly the RBF pose-space corrective layer participates overall. Per-region strength is layered on top of this baseline.");

            var sharpness = poseCorrectives.PoseMapSharpness;
            if (ImGui.SliderFloat("Pose-map sharpness", ref sharpness, AdvancedBodyScalingPoseCorrectiveTuning.UiPoseMapSharpnessMin, AdvancedBodyScalingPoseCorrectiveTuning.UiPoseMapSharpnessMax, "%.2f"))
            {
                poseCorrectives.PoseMapSharpness = sharpness;
                _configuration.Save();
                _armatureManager.RebindAllArmatures();
            }
            CtrlHelper.AddHoverText("Controls how strongly the nearest stored sample poses dominate the solve. Lower values blend more broadly; higher values feel sharper and more selective.");

            var damping = poseCorrectives.Damping;
            if (ImGui.SliderFloat("Smoothing / damping", ref damping, 0f, 1f, "%.2f"))
            {
                poseCorrectives.Damping = damping;
                _configuration.Save();
                _armatureManager.RebindAllArmatures();
            }
            CtrlHelper.AddHoverText("Damps the interpolated corrective output so small pose changes do not cause abrupt or noisy transform shifts.");

            var maxCorrectionClamp = poseCorrectives.MaxCorrectionClamp;
            if (ImGui.SliderFloat("Max correction clamp", ref maxCorrectionClamp, 0f, AdvancedBodyScalingPoseCorrectiveTuning.UiMaxCorrectionClampMax, "%.3f"))
            {
                poseCorrectives.MaxCorrectionClamp = maxCorrectionClamp;
                _configuration.Save();
                _armatureManager.RebindAllArmatures();
            }
            CtrlHelper.AddHoverText("Global cap on transform-based RBF corrective output. Conservative values are recommended because stronger is not always better.");

            var advisories = AdvancedBodyScalingPoseCorrectiveSystem.GetTuningAdvisories(settings);
            if (advisories.Count > 0)
            {
                ImGui.Spacing();
                ImGui.TextDisabled("Tuning advisories:");
                foreach (var advisory in advisories.Take(4))
                    ImGui.BulletText(advisory);
            }

            foreach (var region in AdvancedBodyScalingPoseCorrectiveSystem.GetOrderedRegions())
            {
                var label = AdvancedBodyScalingPoseCorrectiveSystem.GetRegionLabel(region);
                var description = AdvancedBodyScalingPoseCorrectiveSystem.GetRegionDescription(region);
                if (!ImGui.TreeNode($"{label}##PoseCorrectiveRegion{region}"))
                    continue;

                var regionSettings = poseCorrectives.GetRegionSettings(region);
                if (ImGui.SmallButton($"Restore region defaults##PoseCorrectiveRestore{region}"))
                {
                    poseCorrectives.Regions[region] = AdvancedBodyScalingCorrectiveRegionSettings.CreateDefault(region);
                    regionSettings = poseCorrectives.GetRegionSettings(region);
                    _configuration.Save();
                    _armatureManager.RebindAllArmatures();
                }
                CtrlHelper.AddHoverText($"Restore the shipped defaults for {label}, including enable, strength, threshold, deadzone, damping, sample falloff, max clamp, and blend priority.");

                ImGui.TextDisabled(description);
                ImGui.TextDisabled($"Built-in pose samples: {AdvancedBodyScalingPoseCorrectiveSystem.GetRegionSampleCount(region)}");

                var regionEnabled = regionSettings.Enabled;
                if (ImGui.Checkbox($"Enable##PoseCorrectiveEnabled{region}", ref regionEnabled))
                {
                    regionSettings.Enabled = regionEnabled;
                    _configuration.Save();
                    _armatureManager.RebindAllArmatures();
                }
                CtrlHelper.AddHoverText("Turns this corrective region on or off without changing the other corrective regions.");

                var regionStrength = regionSettings.Strength;
                if (ImGui.SliderFloat($"Strength##PoseCorrectiveStrength{region}", ref regionStrength, 0f, 1f, "%.2f"))
                {
                    regionSettings.Strength = regionStrength;
                    _configuration.Save();
                    _armatureManager.RebindAllArmatures();
                }
                CtrlHelper.AddHoverText("Controls how strongly this corrective region responds compared with the global corrective strength.");

                if (ImGui.TreeNode($"Advanced tuning##PoseCorrectiveAdvanced{region}"))
                {
                    var threshold = regionSettings.ActivationThreshold;
                    if (ImGui.SliderFloat($"Activation threshold##PoseCorrectiveThreshold{region}", ref threshold, 0f, 1f, "%.2f"))
                    {
                        regionSettings.ActivationThreshold = threshold;
                        _configuration.Save();
                        _armatureManager.RebindAllArmatures();
                    }
                    CtrlHelper.AddHoverText("How much pose or continuity stress must be detected before this corrective starts activating.");

                    var deadzone = regionSettings.ActivationDeadzone;
                    if (ImGui.SliderFloat($"Activation deadzone##PoseCorrectiveDeadzone{region}", ref deadzone, 0f, 0.25f, "%.2f"))
                    {
                        regionSettings.ActivationDeadzone = deadzone;
                        _configuration.Save();
                        _armatureManager.RebindAllArmatures();
                    }
                    CtrlHelper.AddHoverText("Ignores tiny fluctuations so the corrective does not flicker on and off from very small pose changes.");

                    var smoothing = regionSettings.Smoothing;
                    if (ImGui.SliderFloat($"Smoothing##PoseCorrectiveSmoothing{region}", ref smoothing, 0f, 1f, "%.2f"))
                    {
                        regionSettings.Smoothing = smoothing;
                        _configuration.Save();
                        _armatureManager.RebindAllArmatures();
                    }
                    CtrlHelper.AddHoverText("Region-level damping layered on top of the global damping value so this area ramps in and out more gradually.");

                    var falloff = regionSettings.Falloff;
                    if (ImGui.SliderFloat($"Sample falloff##PoseCorrectiveFalloff{region}", ref falloff, 0f, 1f, "%.2f"))
                    {
                        regionSettings.Falloff = falloff;
                        _configuration.Save();
                        _armatureManager.RebindAllArmatures();
                    }
                    CtrlHelper.AddHoverText("How broadly nearby RBF pose samples are allowed to contribute in this region instead of concentrating on one very narrow pose.");

                    var maxCorrection = regionSettings.MaxCorrection;
                    if (ImGui.SliderFloat($"Max correction clamp##PoseCorrectiveMax{region}", ref maxCorrection, 0f, 0.10f, "%.3f"))
                    {
                        regionSettings.MaxCorrection = maxCorrection;
                        _configuration.Save();
                        _armatureManager.RebindAllArmatures();
                    }
                    CtrlHelper.AddHoverText("Hard cap on how strong this corrective is allowed to become.");

                    var priority = regionSettings.Priority;
                    if (ImGui.SliderFloat($"Blend priority##PoseCorrectivePriority{region}", ref priority, 0.1f, 1.5f, "%.2f"))
                    {
                        regionSettings.Priority = priority;
                        _configuration.Save();
                        _armatureManager.RebindAllArmatures();
                    }
                    CtrlHelper.AddHoverText("How strongly this corrective participates when multiple corrective regions are active at once.");

                    ImGui.TreePop();
                }

                ImGui.TreePop();
                ImGui.Spacing();
            }
        }

        DrawPoseCorrectiveDebugReadout();
    }

    private void DrawFullIkRetargetingSettings(AdvancedBodyScalingSettings settings)
    {
        if (!ImGui.CollapsingHeader("Full IK Retargeting"))
            return;

        var retarget = settings.FullIkRetargeting;
        ImGui.TextDisabled("Full IK Retargeting adapts animation pose output to changed body proportions before the final Full-Body IK solve. It helps preserve animation intent on scaled bodies and is conservative by default.");

        var enabled = retarget.Enabled;
        if (ImGui.Checkbox("Enable Full IK Retargeting", ref enabled))
        {
            retarget.Enabled = enabled;
            _configuration.Save();
            _armatureManager.RebindAllArmatures();
        }
        CtrlHelper.AddHoverText("Turns the supported-bone retargeting layer on or off. It runs after the RBF pose-space corrective layer and before the final Full-Body IK pass.");

        ImGui.SameLine();
        if (ImGui.Button("Restore retargeting defaults"))
        {
            settings.FullIkRetargeting = new AdvancedBodyScalingFullIkRetargetingSettings();
            retarget = settings.FullIkRetargeting;
            _configuration.Save();
            _armatureManager.RebindAllArmatures();
        }
        CtrlHelper.AddHoverText("Restores the shipped Full IK Retargeting defaults, including global enable/strength, blend and safety values, and every per-chain enable and strength setting.");

        using (var disabled = ImRaii.Disabled(!retarget.Enabled))
        {
            var globalStrength = retarget.GlobalStrength;
            if (ImGui.SliderFloat("Global retargeting strength", ref globalStrength, 0f, AdvancedBodyScalingFullIkRetargetingTuning.UiMaxGlobalStrength, "%.2f"))
            {
                retarget.GlobalStrength = globalStrength;
                _configuration.Save();
                _armatureManager.RebindAllArmatures();
            }
            CtrlHelper.AddHoverText("Scales how strongly retargeting is allowed to adapt the current animation pose overall. Conservative values are recommended; stronger is not always better.");

            var pelvis = retarget.PelvisStrength;
            if (ImGui.SliderFloat("Pelvis / root strength", ref pelvis, 0f, AdvancedBodyScalingFullIkRetargetingTuning.UiMaxPelvisStrength, "%.2f"))
            {
                retarget.PelvisStrength = pelvis;
                _configuration.Save();
                _armatureManager.RebindAllArmatures();
            }
            CtrlHelper.AddHoverText("How strongly retargeting can bias pelvis and root response when leg length and lower-body proportions drift from the authored animation.");

            var spine = retarget.SpineStrength;
            if (ImGui.SliderFloat("Spine strength", ref spine, 0f, AdvancedBodyScalingFullIkRetargetingTuning.UiMaxSpineStrength, "%.2f"))
            {
                retarget.SpineStrength = spine;
                _configuration.Save();
                _armatureManager.RebindAllArmatures();
            }
            CtrlHelper.AddHoverText("How strongly retargeting redistributes torso posture across the supported spine chain.");

            var arm = retarget.ArmStrength;
            if (ImGui.SliderFloat("Arm strength", ref arm, 0f, AdvancedBodyScalingFullIkRetargetingTuning.UiMaxArmStrength, "%.2f"))
            {
                retarget.ArmStrength = arm;
                _configuration.Save();
                _armatureManager.RebindAllArmatures();
            }
            CtrlHelper.AddHoverText("How strongly the supported arm chains are allowed to adapt reach and posture before the final IK pass.");

            var leg = retarget.LegStrength;
            if (ImGui.SliderFloat("Leg strength", ref leg, 0f, AdvancedBodyScalingFullIkRetargetingTuning.UiMaxLegStrength, "%.2f"))
            {
                retarget.LegStrength = leg;
                _configuration.Save();
                _armatureManager.RebindAllArmatures();
            }
            CtrlHelper.AddHoverText("How strongly the supported leg chains are allowed to adapt stride and lower-body posture. Legs are intentionally safer at lower values.");

            var head = retarget.HeadStrength;
            if (ImGui.SliderFloat("Head / neck strength", ref head, 0f, AdvancedBodyScalingFullIkRetargetingTuning.UiMaxHeadStrength, "%.2f"))
            {
                retarget.HeadStrength = head;
                _configuration.Save();
                _armatureManager.RebindAllArmatures();
            }
            CtrlHelper.AddHoverText("How strongly the supported neck/head chain is allowed to preserve head readability after torso and shoulder proportion changes.");

            var reach = retarget.ReachAdaptationStrength;
            if (ImGui.SliderFloat("Reach adaptation strength", ref reach, 0f, AdvancedBodyScalingFullIkRetargetingTuning.UiMaxReachAdaptation, "%.2f"))
            {
                retarget.ReachAdaptationStrength = reach;
                _configuration.Save();
                _armatureManager.RebindAllArmatures();
            }
            CtrlHelper.AddHoverText("How strongly arm-chain reach should adapt to changed shoulder and arm proportions. Stronger values are not always better.");

            var stride = retarget.StrideAdaptationStrength;
            if (ImGui.SliderFloat("Stride adaptation strength", ref stride, 0f, AdvancedBodyScalingFullIkRetargetingTuning.UiMaxStrideAdaptation, "%.2f"))
            {
                retarget.StrideAdaptationStrength = stride;
                _configuration.Save();
                _armatureManager.RebindAllArmatures();
            }
            CtrlHelper.AddHoverText("How strongly leg-chain stride and extension should adapt to changed lower-body proportions.");

            var posture = retarget.PosturePreservationStrength;
            if (ImGui.SliderFloat("Posture preservation strength", ref posture, 0f, AdvancedBodyScalingFullIkRetargetingTuning.UiMaxPosturePreservation, "%.2f"))
            {
                retarget.PosturePreservationStrength = posture;
                _configuration.Save();
                _armatureManager.RebindAllArmatures();
            }
            CtrlHelper.AddHoverText("How strongly spine, pelvis, and head posture should adapt to preserve the original animation read on scaled proportions.");

            var motionSafety = retarget.MotionSafetyBias;
            if (ImGui.SliderFloat("Motion-safety / damping", ref motionSafety, 0.30f, 1f, "%.2f"))
            {
                retarget.MotionSafetyBias = motionSafety;
                _configuration.Save();
                _armatureManager.RebindAllArmatures();
            }
            CtrlHelper.AddHoverText("Adds damping, deadzone, and smoothing pressure so retargeting stays calmer and does not flicker or visibly fight the animation.");

            var blendBias = retarget.BlendBias;
            if (ImGui.SliderFloat("Retargeting blend bias", ref blendBias, 0f, AdvancedBodyScalingFullIkRetargetingTuning.UiMaxBlendBias, "%.2f"))
            {
                retarget.BlendBias = blendBias;
                _configuration.Save();
                _armatureManager.RebindAllArmatures();
            }
            CtrlHelper.AddHoverText("Controls how much the runtime output is allowed to lean toward the retargeted pose instead of the original animation pose.");

            var maxCorrection = retarget.MaxCorrectionClamp;
            if (ImGui.SliderFloat("Max retargeting correction clamp", ref maxCorrection, 0f, AdvancedBodyScalingFullIkRetargetingTuning.UiMaxCorrectionClamp, "%.2f"))
            {
                retarget.MaxCorrectionClamp = maxCorrection;
                _configuration.Save();
                _armatureManager.RebindAllArmatures();
            }
            CtrlHelper.AddHoverText("Hard cap on how much local rotation and translation the retargeting pass is allowed to add before the final IK solve.");

            var advisories = AdvancedBodyScalingFullIkRetargetingSystem.GetTuningAdvisories(settings);
            if (advisories.Count == 0)
            {
                ImGui.TextDisabled("Recommended range: conservative values usually preserve animation intent more cleanly than stronger ones.");
            }
            else
            {
                ImGui.TextColored(new Vector4(0.95f, 0.80f, 0.38f, 1f), "Retargeting advisories:");
                foreach (var advisory in advisories.Take(4))
                    ImGui.BulletText(advisory);
            }

            foreach (var chain in AdvancedBodyScalingFullIkRetargetingSystem.GetOrderedChains())
            {
                var label = AdvancedBodyScalingFullIkRetargetingSystem.GetChainLabel(chain);
                var description = AdvancedBodyScalingFullIkRetargetingSystem.GetChainDescription(chain);
                if (!ImGui.TreeNode($"{label}##FullIkRetargetChain{chain}"))
                    continue;

                var chainSettings = retarget.GetChainSettings(chain);
                if (ImGui.SmallButton($"Restore chain defaults##FullIkRetargetRestore{chain}"))
                {
                    retarget.Chains[chain] = AdvancedBodyScalingFullIkRetargetingChainSettings.CreateDefault(chain);
                    chainSettings = retarget.GetChainSettings(chain);
                    _configuration.Save();
                    _armatureManager.RebindAllArmatures();
                }
                CtrlHelper.AddHoverText($"Restore the shipped defaults for the {label} retargeting chain without changing the rest of the retargeting tuning.");

                ImGui.TextDisabled(description);

                var chainEnabled = chainSettings.Enabled;
                if (ImGui.Checkbox($"Enable##FullIkRetargetEnabled{chain}", ref chainEnabled))
                {
                    chainSettings.Enabled = chainEnabled;
                    _configuration.Save();
                    _armatureManager.RebindAllArmatures();
                }
                CtrlHelper.AddHoverText("Turns this supported retargeting chain on or off without changing the rest of the retargeting system.");

                var chainStrength = chainSettings.Strength;
                if (ImGui.SliderFloat($"Strength##FullIkRetargetStrength{chain}", ref chainStrength, 0f, AdvancedBodyScalingFullIkRetargetingTuning.GetUiMaxChainStrength(chain), "%.2f"))
                {
                    chainSettings.Strength = chainStrength;
                    _configuration.Save();
                    _armatureManager.RebindAllArmatures();
                }
                CtrlHelper.AddHoverText("Scales how strongly this chain participates relative to the global retargeting strength and the matching chain-group strength. Conservative values are recommended.");

                ImGui.TreePop();
                ImGui.Spacing();
            }
        }

        DrawFullIkRetargetingDebugReadout();
    }

    private void DrawMotionWarpingSettings(AdvancedBodyScalingSettings settings)
    {
        if (!ImGui.CollapsingHeader("Motion Warping"))
            return;

        var motion = settings.MotionWarping;
        ImGui.TextDisabled("Motion Warping helps movement fit changed body proportions more naturally. This build currently supports conservative locomotion warping only: stride, direction alignment, and locomotion posture coherence before the final Full-Body IK solve.");

        var enabled = motion.Enabled;
        if (ImGui.Checkbox("Enable Motion Warping", ref enabled))
        {
            motion.Enabled = enabled;
            _configuration.Save();
            _armatureManager.RebindAllArmatures();
        }
        CtrlHelper.AddHoverText("Turns the locomotion-warping layer on or off. It runs after Full IK Retargeting and before the final Full-Body IK pass. True target-window motion warping is not available in this runtime.");

        ImGui.SameLine();
        if (ImGui.Button("Restore warping defaults"))
        {
            settings.MotionWarping = new AdvancedBodyScalingMotionWarpingSettings();
            motion = settings.MotionWarping;
            _configuration.Save();
            _armatureManager.RebindAllArmatures();
        }
        CtrlHelper.AddHoverText("Restores the shipped Motion Warping defaults, including global enable/strength, damping and clamp values, and every per-chain enable and strength setting.");

        ImGui.TextDisabled($"Implementation tier: {AdvancedBodyScalingMotionWarpingSystem.GetImplementationTierLabel()}");
        ImGui.TextDisabled("Target-aware root-motion warping is not currently supported, so there are no target-alignment controls in this build.");

        using (var disabled = ImRaii.Disabled(!motion.Enabled))
        {
            var globalStrength = motion.GlobalStrength;
            if (ImGui.SliderFloat("Global warping strength", ref globalStrength, 0f, AdvancedBodyScalingMotionWarpingTuning.UiMaxGlobalStrength, "%.2f"))
            {
                motion.GlobalStrength = globalStrength;
                _configuration.Save();
                _armatureManager.RebindAllArmatures();
            }
            CtrlHelper.AddHoverText("Scales how strongly locomotion warping is allowed to adapt the current animation pose overall. Conservative values are recommended; stronger values can start to replace the original motion read.");

            var stride = motion.StrideWarpStrength;
            if (ImGui.SliderFloat("Stride warping strength", ref stride, 0f, AdvancedBodyScalingMotionWarpingTuning.UiMaxStrideWarpStrength, "%.2f"))
            {
                motion.StrideWarpStrength = stride;
                _configuration.Save();
                _armatureManager.RebindAllArmatures();
            }
            CtrlHelper.AddHoverText("Biases how strongly supported leg and pelvis chains adapt stride fit from observed locomotion speed and changed leg proportions. Legs are intentionally safer at lower values.");

            var orientation = motion.OrientationWarpStrength;
            if (ImGui.SliderFloat("Orientation warping strength", ref orientation, 0f, AdvancedBodyScalingMotionWarpingTuning.UiMaxOrientationWarpStrength, "%.2f"))
            {
                motion.OrientationWarpStrength = orientation;
                _configuration.Save();
                _armatureManager.RebindAllArmatures();
            }
            CtrlHelper.AddHoverText("Biases how strongly pelvis, spine, and locomotion-facing chains align toward observed movement direction. Stronger values can make movement feel over-steered.");

            var posture = motion.PostureWarpStrength;
            if (ImGui.SliderFloat("Posture / locomotion coherence strength", ref posture, 0f, AdvancedBodyScalingMotionWarpingTuning.UiMaxPostureWarpStrength, "%.2f"))
            {
                motion.PostureWarpStrength = posture;
                _configuration.Save();
                _armatureManager.RebindAllArmatures();
            }
            CtrlHelper.AddHoverText("Biases how strongly pelvis, spine, neck/head, and arm balance respond to locomotion pressure so movement reads more coherent on scaled bodies.");

            var motionSafety = motion.MotionSafetyBias;
            if (ImGui.SliderFloat("Motion-safety / damping", ref motionSafety, 0.30f, 1f, "%.2f"))
            {
                motion.MotionSafetyBias = motionSafety;
                _configuration.Save();
                _armatureManager.RebindAllArmatures();
            }
            CtrlHelper.AddHoverText("Adds damping, deadzone, and hysteresis pressure so locomotion warping stays calm and does not flicker with tiny movement changes. Lower values are riskier.");

            var blendBias = motion.BlendBias;
            if (ImGui.SliderFloat("Warping blend bias", ref blendBias, 0f, AdvancedBodyScalingMotionWarpingTuning.UiMaxBlendBias, "%.2f"))
            {
                motion.BlendBias = blendBias;
                _configuration.Save();
                _armatureManager.RebindAllArmatures();
            }
            CtrlHelper.AddHoverText("Controls how much the runtime output leans toward the warped locomotion pose instead of the original animation pose.");

            var maxCorrection = motion.MaxCorrectionClamp;
            if (ImGui.SliderFloat("Max warp correction clamp", ref maxCorrection, 0f, AdvancedBodyScalingMotionWarpingTuning.UiMaxCorrectionClamp, "%.2f"))
            {
                motion.MaxCorrectionClamp = maxCorrection;
                _configuration.Save();
                _armatureManager.RebindAllArmatures();
            }
            CtrlHelper.AddHoverText("Hard cap on how much local rotation and translation the locomotion-warping pass is allowed to add before the final IK solve. Larger clamps can make unsafe stride or orientation changes more visible.");

            var advisories = AdvancedBodyScalingMotionWarpingSystem.GetTuningAdvisories(settings);
            if (advisories.Count == 0)
            {
                ImGui.TextDisabled("Recommended range: conservative values usually preserve locomotion intent more cleanly than stronger ones.");
            }
            else
            {
                ImGui.TextColored(new Vector4(0.95f, 0.80f, 0.38f, 1f), "Motion-warping advisories:");
                foreach (var advisory in advisories.Take(4))
                    ImGui.BulletText(advisory);
            }

            foreach (var chain in AdvancedBodyScalingMotionWarpingSystem.GetOrderedChains())
            {
                var label = AdvancedBodyScalingMotionWarpingSystem.GetChainLabel(chain);
                var description = AdvancedBodyScalingMotionWarpingSystem.GetChainDescription(chain);
                if (!ImGui.TreeNode($"{label}##MotionWarpChain{chain}"))
                    continue;

                var chainSettings = motion.GetChainSettings(chain);
                if (ImGui.SmallButton($"Restore chain defaults##MotionWarpRestore{chain}"))
                {
                    motion.Chains[chain] = AdvancedBodyScalingMotionWarpingChainSettings.CreateDefault(chain);
                    chainSettings = motion.GetChainSettings(chain);
                    _configuration.Save();
                    _armatureManager.RebindAllArmatures();
                }
                CtrlHelper.AddHoverText($"Restore the shipped defaults for the {label} motion-warping chain without changing the rest of the locomotion-warping tuning.");

                ImGui.TextDisabled(description);

                var chainEnabled = chainSettings.Enabled;
                if (ImGui.Checkbox($"Enable##MotionWarpEnabled{chain}", ref chainEnabled))
                {
                    chainSettings.Enabled = chainEnabled;
                    _configuration.Save();
                    _armatureManager.RebindAllArmatures();
                }
                CtrlHelper.AddHoverText("Turns this supported motion-warping chain on or off without changing the rest of the locomotion-warping system.");

                var chainStrength = chainSettings.Strength;
                if (ImGui.SliderFloat($"Strength##MotionWarpStrength{chain}", ref chainStrength, 0f, AdvancedBodyScalingMotionWarpingTuning.GetUiMaxChainStrength(chain), "%.2f"))
                {
                    chainSettings.Strength = chainStrength;
                    _configuration.Save();
                    _armatureManager.RebindAllArmatures();
                }
                CtrlHelper.AddHoverText("Scales how strongly this chain participates relative to the global locomotion-warping strength and the matching chain-group pressure. Conservative values are recommended.");

                ImGui.TreePop();
                ImGui.Spacing();
            }
        }

        DrawMotionWarpingDebugReadout();
    }

    private void DrawFullBodyIkSettings(AdvancedBodyScalingSettings settings)
    {
        if (!ImGui.CollapsingHeader("Full-Body IK"))
            return;

        var fullBodyIk = settings.FullBodyIk;
        ImGui.TextDisabled("Full-Body IK adds a final whole-body pose solve after scaling and correctives so the body can adapt more coherently to changed proportions. It is conservative by default and works only on supported bone chains.");

        var enabled = fullBodyIk.Enabled;
        if (ImGui.Checkbox("Enable Full-Body IK", ref enabled))
        {
            fullBodyIk.Enabled = enabled;
            _configuration.Save();
            _armatureManager.RebindAllArmatures();
        }
        CtrlHelper.AddHoverText("Turns the final supported-bone full-body IK layer on or off. It runs after Advanced Body Scaling, RBF pose-space correctives, retargeting, and motion warping, then yields back to locks and pinned axes when they limit the solve.");

        ImGui.SameLine();
        if (ImGui.Button("Restore IK defaults"))
        {
            settings.FullBodyIk = new AdvancedBodyScalingFullBodyIkSettings();
            fullBodyIk = settings.FullBodyIk;
            _configuration.Save();
            _armatureManager.RebindAllArmatures();
        }
        CtrlHelper.AddHoverText("Restores the shipped Full-Body IK defaults, including global enable/strength, solver safety values, and every per-chain enable and strength setting.");

        using (var disabled = ImRaii.Disabled(!fullBodyIk.Enabled))
        {
            var globalStrength = fullBodyIk.GlobalStrength;
            if (ImGui.SliderFloat("Global IK strength", ref globalStrength, 0f, AdvancedBodyScalingFullBodyIkTuning.UiMaxGlobalStrength, "%.2f"))
            {
                fullBodyIk.GlobalStrength = globalStrength;
                _configuration.Save();
                _armatureManager.RebindAllArmatures();
            }
            CtrlHelper.AddHoverText("Scales how strongly the final Full-Body IK layer is allowed to adapt the current pose overall. Conservative values are recommended; stronger values are not always better and can make the solve noisier.");

            var iterations = fullBodyIk.IterationCount;
            if (ImGui.SliderInt("Iteration count", ref iterations, 1, 12))
            {
                fullBodyIk.IterationCount = iterations;
                _configuration.Save();
                _armatureManager.RebindAllArmatures();
            }
            CtrlHelper.AddHoverText("Maximum solver iterations for the coordinated chain pass. Higher values can fit the pose more closely, but lower values are usually steadier and cheaper.");

            var tolerance = fullBodyIk.ConvergenceTolerance;
            if (ImGui.SliderFloat("Convergence tolerance", ref tolerance, 0.001f, 0.050f, "%.3f"))
            {
                fullBodyIk.ConvergenceTolerance = tolerance;
                _configuration.Save();
                _armatureManager.RebindAllArmatures();
            }
            CtrlHelper.AddHoverText("How much residual chain error is tolerated before the solve is considered converged. Lower values chase the target longer; higher values stay more conservative.");

            var pelvis = fullBodyIk.PelvisCompensationStrength;
            if (ImGui.SliderFloat("Pelvis compensation strength", ref pelvis, 0f, AdvancedBodyScalingFullBodyIkTuning.UiMaxPelvisStrength, "%.2f"))
            {
                fullBodyIk.PelvisCompensationStrength = pelvis;
                _configuration.Save();
                _armatureManager.RebindAllArmatures();
            }
            CtrlHelper.AddHoverText("How strongly the solver shares leg reach pressure back into the pelvis so planted and extended lower-body poses stay more coherent after scaling. Higher values can overdrive the whole body, so conservative tuning is recommended.");

            var spine = fullBodyIk.SpineRedistributionStrength;
            if (ImGui.SliderFloat("Spine redistribution strength", ref spine, 0f, AdvancedBodyScalingFullBodyIkTuning.UiMaxSpineStrength, "%.2f"))
            {
                fullBodyIk.SpineRedistributionStrength = spine;
                _configuration.Save();
                _armatureManager.RebindAllArmatures();
            }
            CtrlHelper.AddHoverText("How strongly pelvis and limb pressure is redistributed through the supported spine chain instead of collapsing into one joint. Stronger values can amplify torso jitter.");

            var arm = fullBodyIk.ArmStrength;
            if (ImGui.SliderFloat("Arm strength", ref arm, 0f, AdvancedBodyScalingFullBodyIkTuning.UiMaxArmStrength, "%.2f"))
            {
                fullBodyIk.ArmStrength = arm;
                _configuration.Save();
                _armatureManager.RebindAllArmatures();
            }
            CtrlHelper.AddHoverText("How strongly the supported arm chains try to preserve shoulder, elbow, and hand continuity relative to the chest and clavicles. Arms are safer than legs, but stronger is still not always better.");

            var leg = fullBodyIk.LegStrength;
            if (ImGui.SliderFloat("Leg strength", ref leg, 0f, AdvancedBodyScalingFullBodyIkTuning.UiMaxLegStrength, "%.2f"))
            {
                fullBodyIk.LegStrength = leg;
                _configuration.Save();
                _armatureManager.RebindAllArmatures();
            }
            CtrlHelper.AddHoverText("How strongly the supported leg chains try to preserve pelvis-to-foot continuity and planted-feet behavior where practical. Legs are intentionally safer at lower strengths because aggressive values destabilize first here.");

            var head = fullBodyIk.HeadAlignmentStrength;
            if (ImGui.SliderFloat("Head / neck alignment strength", ref head, 0f, AdvancedBodyScalingFullBodyIkTuning.UiMaxHeadStrength, "%.2f"))
            {
                fullBodyIk.HeadAlignmentStrength = head;
                _configuration.Save();
                _armatureManager.RebindAllArmatures();
            }
            CtrlHelper.AddHoverText("How strongly the neck and head are allowed to realign after pelvis, spine, and shoulder compensation.");

            var grounding = fullBodyIk.GroundingBias;
            if (ImGui.SliderFloat("Grounding bias", ref grounding, 0f, AdvancedBodyScalingFullBodyIkTuning.UiMaxGroundingBias, "%.2f"))
            {
                fullBodyIk.GroundingBias = grounding;
                _configuration.Save();
                _armatureManager.RebindAllArmatures();
            }
            CtrlHelper.AddHoverText("Biases leg and pelvis behavior toward planted-feet stability where the supported pose data suggests that is practical. Excessive grounding bias can force unstable leg behavior.");

            var motionSafety = fullBodyIk.MotionSafetyBias;
            if (ImGui.SliderFloat("Motion-safety bias / damping", ref motionSafety, 0.30f, 1f, "%.2f"))
            {
                fullBodyIk.MotionSafetyBias = motionSafety;
                _configuration.Save();
                _armatureManager.RebindAllArmatures();
            }
            CtrlHelper.AddHoverText("Adds damping, deadzone, and smoothing pressure so the solve stays calmer and is less likely to jitter or visibly fight the animation. Lower values are riskier; conservative setups usually keep this fairly high.");

            var maxCorrection = fullBodyIk.MaxCorrectionClamp;
            if (ImGui.SliderFloat("Max IK correction clamp", ref maxCorrection, 0f, AdvancedBodyScalingFullBodyIkTuning.UiMaxCorrectionClamp, "%.2f"))
            {
                fullBodyIk.MaxCorrectionClamp = maxCorrection;
                _configuration.Save();
                _armatureManager.RebindAllArmatures();
            }
            CtrlHelper.AddHoverText("Hard cap on how much local rotation and translation the final Full-Body IK solve is allowed to add on supported chains. Larger clamps can make unsafe corrections much more visible.");

            var advisories = AdvancedBodyScalingFullBodyIkSystem.GetTuningAdvisories(settings);
            if (advisories.Count == 0)
            {
                ImGui.TextDisabled("Recommended range: conservative values usually solve more cleanly than stronger ones.");
            }
            else
            {
                ImGui.TextColored(new Vector4(0.95f, 0.80f, 0.38f, 1f), "Stability advisories:");
                foreach (var advisory in advisories.Take(4))
                    ImGui.BulletText(advisory);
            }

            foreach (var chain in AdvancedBodyScalingFullBodyIkSystem.GetOrderedChains())
            {
                var label = AdvancedBodyScalingFullBodyIkSystem.GetChainLabel(chain);
                var description = AdvancedBodyScalingFullBodyIkSystem.GetChainDescription(chain);
                if (!ImGui.TreeNode($"{label}##FullBodyIkChain{chain}"))
                    continue;

                var chainSettings = fullBodyIk.GetChainSettings(chain);
                if (ImGui.SmallButton($"Restore chain defaults##FullBodyIkRestore{chain}"))
                {
                    fullBodyIk.Chains[chain] = AdvancedBodyScalingFullBodyIkChainSettings.CreateDefault(chain);
                    chainSettings = fullBodyIk.GetChainSettings(chain);
                    _configuration.Save();
                    _armatureManager.RebindAllArmatures();
                }
                CtrlHelper.AddHoverText($"Restore the shipped defaults for the {label} chain without changing the rest of the Full-Body IK tuning.");

                ImGui.TextDisabled(description);

                var chainEnabled = chainSettings.Enabled;
                if (ImGui.Checkbox($"Enable##FullBodyIkEnabled{chain}", ref chainEnabled))
                {
                    chainSettings.Enabled = chainEnabled;
                    _configuration.Save();
                    _armatureManager.RebindAllArmatures();
                }
                CtrlHelper.AddHoverText("Turns this supported chain on or off without changing the rest of the Full-Body IK system.");

                var chainStrength = chainSettings.Strength;
                if (ImGui.SliderFloat($"Strength##FullBodyIkStrength{chain}", ref chainStrength, 0f, AdvancedBodyScalingFullBodyIkTuning.GetUiMaxChainStrength(chain), "%.2f"))
                {
                    chainSettings.Strength = chainStrength;
                    _configuration.Save();
                    _armatureManager.RebindAllArmatures();
                }
                CtrlHelper.AddHoverText("Scales how strongly this chain participates relative to the global Full-Body IK strength and the regional chain group strength. Conservative chain values are recommended; legs and pelvis are deliberately safer at lower strengths.");

                ImGui.TreePop();
                ImGui.Spacing();
            }
        }

        DrawFullBodyIkDebugReadout();
    }

    private void DrawPoseCorrectiveDebugReadout()
    {
        var path = AdvancedBodyScalingPoseCorrectiveSystem.DetectSupportedPath();
        var pathDescription = AdvancedBodyScalingPoseCorrectiveSystem.GetPathDescription(path);
        AdvancedBodyScalingPoseCorrectiveDebugState? debugState = null;

        if (TryGetPoseCorrectiveDebugState(out var liveState) && liveState != null)
        {
            debugState = liveState;
            path = liveState.Path;
            pathDescription = liveState.PathDescription;
        }

        ImGui.TextDisabled($"Runtime path: {GetPoseCorrectivePathLabel(path)}");
        CtrlHelper.AddHoverText(pathDescription);
        ImGui.TextDisabled($"Settings source: {(debugState?.SettingsSourceLabel ?? "Global settings")}");

        if (debugState == null)
        {
            ImGui.TextDisabled("No live armature debug data yet. Activity appears while a supported actor is rendered.");
            return;
        }

        ImGui.TextDisabled($"Enabled: {debugState.Enabled} | Active: {debugState.Active}");
        ImGui.TextDisabled($"Global strength: {debugState.GlobalStrength:0.00} | Sharpness: {debugState.PoseMapSharpness:0.00} | Damping: {debugState.Damping:0.00} | Clamp: {debugState.MaxCorrectionClamp:0.000}");

        if (debugState.Advisories.Count > 0)
        {
            ImGui.TextDisabled("Advisories:");
            foreach (var advisory in debugState.Advisories.Take(4))
                ImGui.BulletText(advisory);
        }

        if (!string.IsNullOrWhiteSpace(debugState.Summary))
            ImGui.TextWrapped(debugState.Summary);

        var historyActiveCount = debugState.ActiveRegions.Count(region => region.PoseHistoryActive);
        var hysteresisCount = debugState.ActiveRegions.Count(region => region.HysteresisHeld);
        var persistenceCount = debugState.ActiveRegions.Count(region => region.DominantSamplePersistenceUsed || region.BroadModeMemoryUsed);
        if (historyActiveCount > 0 || hysteresisCount > 0 || persistenceCount > 0)
        {
            ImGui.TextDisabled($"Transition stabilization: pose history active in {historyActiveCount} region{(historyActiveCount == 1 ? string.Empty : "s")}, hysteresis held {hysteresisCount}, memory-biased sample/mode transitions in {persistenceCount}.");
        }

        if (debugState.ActiveRegions.Count == 0)
        {
            ImGui.TextDisabled("No corrective region is strongly active in the current pose.");
            return;
        }

        ImGui.TextUnformatted("Currently active:");
        foreach (var region in debugState.ActiveRegions.OrderByDescending(entry => entry.Strength))
        {
            ImGui.Bullet();
            ImGui.SameLine();
            ImGui.TextWrapped($"{region.Label}: driver {region.DriverStrength:0.00}, raw {region.RawActivation:0.00}, activation {region.Activation:0.00}, corrective {region.Strength:0.00}, est. risk reduction {region.EstimatedRiskReduction * 100f:0}%, samples {region.InfluenceSampleCount}/{region.SampleCount}.");
            ImGui.Indent();
            if (region.SafetyLimited || region.LocksOrPinsLimited)
            {
                var flags = new List<string>();
                if (region.SafetyLimited)
                    flags.Add("safety-limited");
                if (region.Clamped)
                    flags.Add("clamped");
                if (region.Damped)
                    flags.Add("damped");
                if (region.LocksOrPinsLimited)
                    flags.Add("locks/pins limited");

                if (flags.Count > 0)
                    ImGui.TextDisabled($"State: {string.Join(", ", flags)}");
            }

            ImGui.TextDisabled(region.Description);
            ImGui.TextDisabled(region.ShortlistApplied
                ? $"Nearest-sample shortlist active. {(region.BroadInterpolation ? "Broad interpolation" : "Focused interpolation")} is using {region.InfluenceSampleCount} of {region.SampleCount} samples."
                : $"{(region.BroadInterpolation ? "Broad interpolation" : "Focused interpolation")} is using the full {region.SampleCount}-sample library.");
            if (!string.IsNullOrWhiteSpace(region.AdaptiveSummary))
                ImGui.TextDisabled($"Adaptive tuning: {region.AdaptiveSummary}");
            else
                ImGui.TextDisabled($"Adaptive tuning: {region.AdaptiveMode}, shortlist {region.AdaptiveShortlistFloor}-{region.AdaptiveShortlistMax}, sharpness x{region.AdaptiveSharpnessScale:0.00}, falloff x{region.AdaptiveFalloffScale:0.00}, damping x{region.AdaptiveDampingScale:0.00}.");
            if (region.AdaptiveMeaningfulChange)
                ImGui.TextDisabled("Adaptive solve materially changed shortlist/falloff/damping from the global baseline for this region.");
            if (!string.IsNullOrWhiteSpace(region.TransitionSummary))
                ImGui.TextDisabled($"Transition memory: {region.TransitionSummary}");
            if (!string.IsNullOrWhiteSpace(region.DriverVectorSummary))
                ImGui.TextDisabled($"Driver vector: {region.DriverVectorSummary}");
            if (!string.IsNullOrWhiteSpace(region.SampleSummary))
                ImGui.TextDisabled($"Pose weights: {region.SampleSummary}");
            if (!string.IsNullOrWhiteSpace(region.Summary))
                ImGui.TextDisabled(region.Summary);
            if (region.InfluentialSamples.Count > 0)
            {
                ImGui.TextDisabled("Dominant samples:");
                foreach (var sample in region.InfluentialSamples.Take(3))
                    ImGui.BulletText($"{sample.Name}: {sample.Weight:0.00} @ distance {sample.Distance:0.00} ({sample.Summary})");
            }
            ImGui.Unindent();
        }
    }

    private void DrawFullIkRetargetingDebugReadout()
    {
        AdvancedBodyScalingFullIkRetargetingDebugState? debugState = null;

        if (TryGetFullIkRetargetingDebugState(out var liveState) && liveState != null)
            debugState = liveState;

        ImGui.TextDisabled($"Settings source: {(debugState?.SettingsSourceLabel ?? "Global settings")}");

        if (debugState == null)
        {
            ImGui.TextDisabled("No live armature Full IK Retargeting debug data yet. Activity appears while a supported actor is rendered.");
            return;
        }

        ImGui.TextDisabled($"Enabled: {debugState.Enabled} | Active: {debugState.Active} | Full-Body IK follow-up active: {debugState.FullBodyIkFollowupActive}");
        ImGui.TextDisabled($"Motion safety: {debugState.MotionSafetyBias:0.00} | Blend bias: {debugState.BlendBias:0.00}");
        ImGui.TextDisabled($"Locks/pins limited solve: {debugState.LocksLimited} | Safety limiting: {debugState.SafetyLimited}");
        ImGui.TextDisabled($"Estimated residual risk: {debugState.EstimatedBeforeRisk:0.#} -> {debugState.EstimatedAfterRisk:0.#}");

        if (!string.IsNullOrWhiteSpace(debugState.Summary))
            ImGui.TextWrapped(debugState.Summary);

        if (!string.IsNullOrWhiteSpace(debugState.FullBodyIkFollowupSummary))
            ImGui.TextDisabled($"Full-Body IK follow-up: {debugState.FullBodyIkFollowupSummary}");

        if (debugState.Chains.Count == 0)
        {
            ImGui.TextDisabled("No supported retargeting chain debug data is available yet.");
            return;
        }

        ImGui.TextUnformatted("Chain activity:");
        foreach (var chain in debugState.Chains
                     .OrderByDescending(entry => entry.BlendAmount)
                     .ThenByDescending(entry => entry.Strength)
                     .ThenBy(entry => entry.Label, StringComparer.Ordinal))
        {
            ImGui.Bullet();
            ImGui.SameLine();

            if (!chain.IsValid)
            {
                var skipReason = string.IsNullOrWhiteSpace(chain.SkipReason) ? "Chain unavailable." : chain.SkipReason;
                ImGui.TextWrapped($"{chain.Label}: {skipReason}");
            }
            else if (!chain.IsActive)
            {
                ImGui.TextWrapped($"{chain.Label}: blend {chain.BlendAmount:0.00}, strength {chain.Strength:0.00}. {chain.DriverSummary}.");
            }
            else
            {
                ImGui.TextWrapped($"{chain.Label}: blend {chain.BlendAmount:0.00}, strength {chain.Strength:0.00}, proportion {chain.ProportionDelta:+0.00;-0.00;0.00}, correction {chain.CorrectionMagnitude:0.000}.");
            }

            ImGui.Indent();
            if (chain.LockLimited)
                ImGui.TextDisabled("Locks or pinned axes limited this chain.");

            if (chain.SafetyLimited)
            {
                var flags = new List<string>();
                if (chain.Clamped)
                    flags.Add("clamped");
                if (chain.Rejected)
                    flags.Add("rejected");
                if (chain.Damped)
                    flags.Add("damped");

                if (flags.Count > 0)
                    ImGui.TextDisabled($"Safety state: {string.Join(", ", flags)}");

                if (!string.IsNullOrWhiteSpace(chain.SafetySummary))
                    ImGui.TextDisabled(chain.SafetySummary);
            }

            ImGui.TextDisabled(chain.Description);
            if (!string.IsNullOrWhiteSpace(chain.DriverSummary))
                ImGui.TextDisabled(chain.DriverSummary);
            ImGui.Unindent();
        }
    }

    private void DrawMotionWarpingDebugReadout()
    {
        AdvancedBodyScalingMotionWarpingDebugState? debugState = null;

        if (TryGetMotionWarpingDebugState(out var liveState) && liveState != null)
            debugState = liveState;

        ImGui.TextDisabled($"Settings source: {(debugState?.SettingsSourceLabel ?? "Global settings")} | Tier: {(debugState?.ImplementationTierLabel ?? AdvancedBodyScalingMotionWarpingTuning.ImplementationTierLabel)}");

        if (debugState == null)
        {
            ImGui.TextDisabled("No live armature Motion Warping debug data yet. Activity appears while a supported actor is moving.");
            return;
        }

        ImGui.TextDisabled($"Enabled: {debugState.Enabled} | Active: {debugState.Active} | Full-Body IK follow-up active: {debugState.FullBodyIkFollowupActive}");
        ImGui.TextDisabled($"Locomotion observed: {debugState.LocomotionObserved} | Planar speed: {debugState.PlanarSpeed:0.00} | Locomotion amount: {debugState.LocomotionAmount:0.00}");
        ImGui.TextDisabled($"Motion safety: {debugState.MotionSafetyBias:0.00} | Blend bias: {debugState.BlendBias:0.00}");
        ImGui.TextDisabled($"Locks/pins limited solve: {debugState.LocksLimited} | Safety limiting: {debugState.SafetyLimited}");
        ImGui.TextDisabled($"Estimated residual risk: {debugState.EstimatedBeforeRisk:0.#} -> {debugState.EstimatedAfterRisk:0.#}");

        if (!string.IsNullOrWhiteSpace(debugState.ContextSummary))
            ImGui.TextWrapped(debugState.ContextSummary);

        if (!string.IsNullOrWhiteSpace(debugState.Summary))
            ImGui.TextWrapped(debugState.Summary);

        if (!string.IsNullOrWhiteSpace(debugState.FullBodyIkFollowupSummary))
            ImGui.TextDisabled($"Full-Body IK follow-up: {debugState.FullBodyIkFollowupSummary}");

        if (debugState.Chains.Count == 0)
        {
            ImGui.TextDisabled("No supported motion-warping chain debug data is available yet.");
            return;
        }

        ImGui.TextUnformatted("Chain activity:");
        foreach (var chain in debugState.Chains
                     .OrderByDescending(entry => entry.BlendAmount)
                     .ThenByDescending(entry => entry.Strength)
                     .ThenBy(entry => entry.Label, StringComparer.Ordinal))
        {
            ImGui.Bullet();
            ImGui.SameLine();

            if (!chain.IsValid)
            {
                var skipReason = string.IsNullOrWhiteSpace(chain.SkipReason) ? "Chain unavailable." : chain.SkipReason;
                ImGui.TextWrapped($"{chain.Label}: {skipReason}");
            }
            else if (!chain.IsActive)
            {
                ImGui.TextWrapped($"{chain.Label}: blend {chain.BlendAmount:0.00}, strength {chain.Strength:0.00}. {chain.DriverSummary}.");
            }
            else
            {
                ImGui.TextWrapped($"{chain.Label}: blend {chain.BlendAmount:0.00}, strength {chain.Strength:0.00}, alignment {chain.MovementAlignment:+0.00;-0.00;0.00}, correction {chain.CorrectionMagnitude:0.000}.");
            }

            ImGui.Indent();
            if (chain.LockLimited)
                ImGui.TextDisabled("Locks or pinned axes limited this chain.");

            if (chain.SafetyLimited)
            {
                var flags = new List<string>();
                if (chain.Clamped)
                    flags.Add("clamped");
                if (chain.Rejected)
                    flags.Add("rejected");
                if (chain.Damped)
                    flags.Add("damped");

                if (flags.Count > 0)
                    ImGui.TextDisabled($"Safety state: {string.Join(", ", flags)}");

                if (!string.IsNullOrWhiteSpace(chain.SafetySummary))
                    ImGui.TextDisabled(chain.SafetySummary);
            }

            ImGui.TextDisabled(chain.Description);
            if (!string.IsNullOrWhiteSpace(chain.DriverSummary))
                ImGui.TextDisabled(chain.DriverSummary);
            ImGui.Unindent();
        }
    }

    private void DrawFullBodyIkDebugReadout()
    {
        AdvancedBodyScalingFullBodyIkDebugState? debugState = null;

        if (TryGetFullBodyIkDebugState(out var liveState) && liveState != null)
            debugState = liveState;

        ImGui.TextDisabled($"Settings source: {(debugState?.SettingsSourceLabel ?? "Global settings")}");

        if (debugState == null)
        {
            ImGui.TextDisabled("No live armature Full-Body IK debug data yet. Activity appears while a supported actor is rendered.");
            return;
        }

        ImGui.TextDisabled($"Enabled: {debugState.Enabled} | Active: {debugState.Active}");
        ImGui.TextDisabled($"Iterations used: {debugState.IterationCountUsed} | Tolerance: {debugState.ConvergenceTolerance:0.000}");
        ImGui.TextDisabled($"Converged: {debugState.Converged} | Locks/pins limited solve: {debugState.LocksLimited} | Stability limiting: {debugState.SafetyLimited}");
        ImGui.TextDisabled($"Estimated residual risk: {debugState.EstimatedBeforeRisk:0.#} -> {debugState.EstimatedAfterRisk:0.#} | Max residual: {debugState.MaxResidualError:0.000}");

        if (!string.IsNullOrWhiteSpace(debugState.Summary))
            ImGui.TextWrapped(debugState.Summary);

        if (debugState.Chains.Count == 0)
        {
            ImGui.TextDisabled("No supported chain debug data is available yet.");
            return;
        }

        ImGui.TextUnformatted("Chain activity:");
        foreach (var chain in debugState.Chains
                     .OrderByDescending(entry => entry.Strength)
                     .ThenByDescending(entry => entry.Activation)
                     .ThenBy(entry => entry.Label, StringComparer.Ordinal))
        {
            ImGui.Bullet();
            ImGui.SameLine();

            if (!chain.IsValid)
            {
                var skipReason = string.IsNullOrWhiteSpace(chain.SkipReason) ? "Chain unavailable." : chain.SkipReason;
                ImGui.TextWrapped($"{chain.Label}: {skipReason}");
            }
            else if (!chain.IsSolved)
            {
                ImGui.TextWrapped($"{chain.Label}: activation {chain.Activation:0.00}, strength {chain.Strength:0.00}. {chain.DriverSummary}.");
            }
            else
            {
                ImGui.TextWrapped($"{chain.Label}: activation {chain.Activation:0.00}, strength {chain.Strength:0.00}, correction {chain.CorrectionMagnitude:0.000}, residual {chain.ResidualError:0.000}.");
            }

            ImGui.Indent();
            if (chain.LockLimited)
                ImGui.TextDisabled("Locks or pinned axes limited this chain.");

            if (chain.SafetyLimited)
            {
                var flags = new List<string>();
                if (chain.Clamped)
                    flags.Add("clamped");
                if (chain.Rejected)
                    flags.Add("rejected");
                if (chain.Damped)
                    flags.Add("damped");

                if (flags.Count > 0)
                    ImGui.TextDisabled($"Safety state: {string.Join(", ", flags)}");

                if (!string.IsNullOrWhiteSpace(chain.SafetySummary))
                    ImGui.TextDisabled(chain.SafetySummary);
            }

            ImGui.TextDisabled(chain.Description);
            if (!string.IsNullOrWhiteSpace(chain.DriverSummary))
                ImGui.TextDisabled(chain.DriverSummary);
            ImGui.Unindent();
        }
    }

    private bool TryGetPoseCorrectiveDebugState(out AdvancedBodyScalingPoseCorrectiveDebugState? debugState)
    {
        if ((_templateEditorManager.IsEditorActive || _templateEditorManager.IsEditorPaused) &&
            TryGetArmatureForCharacter(_templateEditorManager.Character, out var previewArmature))
        {
            debugState = previewArmature.PoseCorrectiveDebugState;
            return true;
        }

        var currentPlayer = _gameObjectService.GetCurrentPlayerActorIdentifier().CreatePermanent();
        if (TryGetArmatureForCharacter(currentPlayer, out var currentArmature))
        {
            debugState = currentArmature.PoseCorrectiveDebugState;
            return true;
        }

        debugState = null;
        return false;
    }

    private bool TryGetFullIkRetargetingDebugState(out AdvancedBodyScalingFullIkRetargetingDebugState? debugState)
    {
        if ((_templateEditorManager.IsEditorActive || _templateEditorManager.IsEditorPaused) &&
            TryGetArmatureForCharacter(_templateEditorManager.Character, out var previewArmature))
        {
            debugState = previewArmature.FullIkRetargetingDebugState;
            return true;
        }

        var currentPlayer = _gameObjectService.GetCurrentPlayerActorIdentifier().CreatePermanent();
        if (TryGetArmatureForCharacter(currentPlayer, out var currentArmature))
        {
            debugState = currentArmature.FullIkRetargetingDebugState;
            return true;
        }

        debugState = null;
        return false;
    }

    private bool TryGetMotionWarpingDebugState(out AdvancedBodyScalingMotionWarpingDebugState? debugState)
    {
        if ((_templateEditorManager.IsEditorActive || _templateEditorManager.IsEditorPaused) &&
            TryGetArmatureForCharacter(_templateEditorManager.Character, out var previewArmature))
        {
            debugState = previewArmature.MotionWarpingDebugState;
            return true;
        }

        var currentPlayer = _gameObjectService.GetCurrentPlayerActorIdentifier().CreatePermanent();
        if (TryGetArmatureForCharacter(currentPlayer, out var currentArmature))
        {
            debugState = currentArmature.MotionWarpingDebugState;
            return true;
        }

        debugState = null;
        return false;
    }

    private bool TryGetFullBodyIkDebugState(out AdvancedBodyScalingFullBodyIkDebugState? debugState)
    {
        if ((_templateEditorManager.IsEditorActive || _templateEditorManager.IsEditorPaused) &&
            TryGetArmatureForCharacter(_templateEditorManager.Character, out var previewArmature))
        {
            debugState = previewArmature.FullBodyIkDebugState;
            return true;
        }

        var currentPlayer = _gameObjectService.GetCurrentPlayerActorIdentifier().CreatePermanent();
        if (TryGetArmatureForCharacter(currentPlayer, out var currentArmature))
        {
            debugState = currentArmature.FullBodyIkDebugState;
            return true;
        }

        debugState = null;
        return false;
    }

    private bool TryGetArmatureForCharacter(Penumbra.GameData.Actors.ActorIdentifier character, out Armature armature)
    {
        var permanentCharacter = character.CreatePermanent();
        if (_armatureManager.Armatures.TryGetValue(permanentCharacter, out var foundArmature) && foundArmature != null)
        {
            armature = foundArmature;
            return true;
        }

        foreach (var (identifier, _) in _gameObjectService.FindActorsByIdentifierIgnoringOwnership(character))
        {
            if (_armatureManager.Armatures.TryGetValue(identifier.CreatePermanent(), out foundArmature) && foundArmature != null)
            {
                armature = foundArmature;
                return true;
            }
        }

        armature = null!;
        return false;
    }

    private static string GetPoseCorrectivePathLabel(AdvancedBodyScalingCorrectivePath path)
        => path switch
        {
            AdvancedBodyScalingCorrectivePath.SupportedMorph => "Supported corrective morph path",
            _ => "RBF transform corrective path",
        };

    private void DrawAdvancedShapeConditioningControl(
        AdvancedBodyScalingSettings settings,
        string label,
        string id,
        string help,
        Func<AdvancedBodyScalingSettings, bool> getEnabled,
        Action<AdvancedBodyScalingSettings, bool> setEnabled,
        Func<AdvancedBodyScalingSettings, float> getStrength,
        Action<AdvancedBodyScalingSettings, float> setStrength)
    {
        var enabled = getEnabled(settings);
        if (ImGui.Checkbox($"Enable {label}##{id}", ref enabled))
        {
            var previous = getEnabled(settings);
            setEnabled(settings, enabled);
            _configuration.Save();
            _armatureManager.RebindAllArmatures();
            RecordGlobalAdvancedScalingChange(label, GetEnabledStateLabel(previous), GetEnabledStateLabel(enabled), id);
        }
        CtrlHelper.AddHoverText(help);

        using (ImRaii.Disabled(!getEnabled(settings)))
        {
            var strength = getStrength(settings);
            if (ImGui.SliderFloat($"{label} strength##{id}Strength", ref strength, 0f, 1f, "%.2f"))
            {
                var previous = getStrength(settings);
                setStrength(settings, strength);
                _configuration.Save();
                _armatureManager.RebindAllArmatures();
                RecordGlobalAdvancedScalingChange($"{label} strength", previous, strength, $"{id}-strength");
            }
        }
    }

    private void DrawAdvancedBodyScalingResets(AdvancedBodyScalingSettings settings)
    {
        if (!ImGui.CollapsingHeader("Quick resets"))
            return;

        var defaults = new AdvancedBodyScalingSettings();

        ImGui.TextDisabled("Restore a small part of the advanced stack or reset all advanced scaling back to defaults.");
        using var table = ImRaii.Table("AdvancedBodyScalingQuickResetsTable", 2, ImGuiTableFlags.BordersInnerV | ImGuiTableFlags.SizingStretchProp);
        if (!table)
            return;

        ImGui.TableSetupColumn("AdvancedResetScope", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn("AdvancedResetAction", ImGuiTableColumnFlags.WidthFixed, 230 * ImGuiHelpers.GlobalScale);

        DrawAdvancedResetRow(
            "Balancing & naturalization",
            "Reset Balancing & Naturalization",
            "Restores surface balancing, mass redistribution, bilateral consistency, proportional balance, surface smoothness, shape-conditioning passes, pose-aware joint correctives, and naturalization to shipped defaults. Does not touch guardrail modes, pose-aware validation, BIW, region tuning, neck presets, IK, motion warping, or RBF pose-space correctives.",
            () =>
            {
                settings.SurfaceBalancingStrength = defaults.SurfaceBalancingStrength;
                settings.MassRedistributionStrength = defaults.MassRedistributionStrength;
                settings.BilateralConsistencyEnabled = defaults.BilateralConsistencyEnabled;
                settings.ProportionalBalanceEnabled = defaults.ProportionalBalanceEnabled;
                settings.ProportionalBalanceStrength = defaults.ProportionalBalanceStrength;
                settings.SurfaceSmoothnessEnabled = defaults.SurfaceSmoothnessEnabled;
                settings.SurfaceSmoothnessStrength = defaults.SurfaceSmoothnessStrength;
                settings.CrossSectionConditioningEnabled = defaults.CrossSectionConditioningEnabled;
                settings.CrossSectionConditioningStrength = defaults.CrossSectionConditioningStrength;
                settings.ShapeFairnessEnabled = defaults.ShapeFairnessEnabled;
                settings.ShapeFairnessStrength = defaults.ShapeFairnessStrength;
                settings.LocalVolumeIntentEnabled = defaults.LocalVolumeIntentEnabled;
                settings.LocalVolumeIntentStrength = defaults.LocalVolumeIntentStrength;
                settings.PoseAwareJointCorrectivesEnabled = defaults.PoseAwareJointCorrectivesEnabled;
                settings.PoseAwareJointCorrectivesStrength = defaults.PoseAwareJointCorrectivesStrength;
                settings.NaturalizationStrength = defaults.NaturalizationStrength;
            });

        DrawAdvancedResetRow(
            "Guardrails & validation",
            "Reset Guardrails & Validation",
            "Restores proportion guardrail mode, pose-aware validation mode, and animation-safe mode to shipped defaults. Does not touch balancing strengths, BIW, region tuning, neck presets, IK, motion warping, or pose-space correctives.",
            () =>
            {
                settings.GuardrailMode = defaults.GuardrailMode;
                settings.PoseValidationMode = defaults.PoseValidationMode;
                settings.AnimationSafeModeEnabled = defaults.AnimationSafeModeEnabled;
            });

        DrawAdvancedResetRow(
            "Neck / shoulder baseline",
            "Reset Neck Baseline & Race Presets",
            "Restores only the global neck/shoulder baseline values and race-specific neck preset settings to shipped defaults. Does not touch balancing, guardrails, BIW, region tuning, automation mode, RBF pose-space correctives, IK, motion warping, or other advanced systems.",
            () =>
            {
                settings.NeckLengthCompensation = defaults.NeckLengthCompensation;
                settings.NeckShoulderBlendStrength = defaults.NeckShoulderBlendStrength;
                settings.ClavicleShoulderSmoothing = defaults.ClavicleShoulderSmoothing;
                settings.UseRaceSpecificNeckCompensation = defaults.UseRaceSpecificNeckCompensation;
                settings.RaceNeckPresets = AdvancedBodyScalingNeckCompensationPreset.CreateDefaults();
            });

        DrawAdvancedResetRow(
            "Bone Importance Weighting",
            "Reset Bone Importance",
            "Restores only Bone Importance Weighting settings, including model-derived BIW enablement, true skin-weight preference, self/profile full-BIW eligibility, and model weighting blend.",
            () =>
            {
                settings.ModelDerivedBoneImportanceEnabled = defaults.ModelDerivedBoneImportanceEnabled;
                settings.PreferTrueSkinWeightImportance = defaults.PreferTrueSkinWeightImportance;
                settings.FullBoneImportanceOnSelf = defaults.FullBoneImportanceOnSelf;
                settings.FullBoneImportanceOnProfiledActors = defaults.FullBoneImportanceOnProfiledActors;
                settings.BoneImportanceHeuristicBlend = defaults.BoneImportanceHeuristicBlend;
            });

        DrawAdvancedResetRow(
            "Region Tuning",
            "Reset Region Tuning",
            "Restores only Region Tuning multipliers and region participation toggles. Does not touch global advanced scaling strengths, BIW, RBF pose-space correctives, IK, motion warping, neck presets, or animation-safe mode.",
            () => settings.RegionProfiles = AdvancedBodyScalingRegionProfile.CreateDefaults());

        DrawAdvancedResetRow(
            "Everything advanced",
            "Reset All Advanced Scaling",
            "Restores all Advanced Body Scaling settings to shipped defaults, including balancing, guardrails, BIW, RBF pose-space correctives, IK, motion warping, the global neck/shoulder baseline, race-specific presets, animation-safe mode, and region tuning.",
            settings.ResetToDefaults);
    }

    private void DrawAdvancedResetRow(string label, string buttonLabel, string tooltip, Action reset)
    {
        ImGui.TableNextRow();

        ImGui.TableNextColumn();
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted(label);

        ImGui.TableNextColumn();
        var buttonWidth = ImGui.GetContentRegionAvail().X;
        if (ImGui.Button($"{buttonLabel}##{label}", new Vector2(buttonWidth, 0)))
        {
            reset();
            _configuration.Save();
            _armatureManager.RebindAllArmatures();
            _activityLogService.Record(
                ActivityLogCategory.AdvancedScaling,
                "Quick reset used",
                $"Used '{buttonLabel}'.");
        }
        CtrlHelper.AddHoverText(tooltip);
    }

    private void DrawAdvancedBodyScalingRegionProfiles(AdvancedBodyScalingSettings settings)
    {
        if (!ImGui.CollapsingHeader("Region Tuning"))
            return;

        ImGui.TextDisabled("Adjust how strongly each region participates in propagation, smoothing, and guardrails.");

        foreach (var region in RegionOrder)
        {
            var profile = settings.GetRegionProfile(region);
            if (!ImGui.TreeNode($"{region}##Region{region}"))
                continue;

            var influence = profile.InfluenceMultiplier;
            if (ImGui.SliderFloat("Influence (propagation)", ref influence, 0f, 1f, "%.2f"))
            {
                profile.InfluenceMultiplier = influence;
                _configuration.Save();
                _armatureManager.RebindAllArmatures();
            }
            CtrlHelper.AddHoverText("Scales how strongly this region propagates scale changes to neighbors.");

            var smoothing = profile.SmoothingMultiplier;
            if (ImGui.SliderFloat("Smoothing", ref smoothing, 0f, 1f, "%.2f"))
            {
                profile.SmoothingMultiplier = smoothing;
                _configuration.Save();
                _armatureManager.RebindAllArmatures();
            }
            CtrlHelper.AddHoverText("Scales how strongly surface balancing and curve smoothing affect this region.");

            var guardrail = profile.GuardrailMultiplier;
            if (ImGui.SliderFloat("Guardrail strength", ref guardrail, 0f, 1f, "%.2f"))
            {
                profile.GuardrailMultiplier = guardrail;
                _configuration.Save();
                _armatureManager.RebindAllArmatures();
            }
            CtrlHelper.AddHoverText("Scales the proportion guardrail strength for this region.");

            var mass = profile.MassRedistributionMultiplier;
            if (ImGui.SliderFloat("Mass redistribution", ref mass, 0f, 1f, "%.2f"))
            {
                profile.MassRedistributionMultiplier = mass;
                _configuration.Save();
                _armatureManager.RebindAllArmatures();
            }
            CtrlHelper.AddHoverText("Scales how much mass redistribution affects this region.");

            var pose = profile.PoseValidationMultiplier;
            if (ImGui.SliderFloat("Pose validation", ref pose, 0f, 1f, "%.2f"))
            {
                profile.PoseValidationMultiplier = pose;
                _configuration.Save();
                _armatureManager.RebindAllArmatures();
            }
            CtrlHelper.AddHoverText("Scales how strongly pose-aware corrections affect this region.");

            var naturalization = profile.NaturalizationMultiplier;
            if (ImGui.SliderFloat("Naturalization", ref naturalization, 0f, 1f, "%.2f"))
            {
                profile.NaturalizationMultiplier = naturalization;
                _configuration.Save();
                _armatureManager.RebindAllArmatures();
            }
            CtrlHelper.AddHoverText("Scales how much final results blend toward the balanced output for this region.");

            var allowGuardrails = profile.AllowGuardrails;
            if (ImGui.Checkbox("Allow guardrails", ref allowGuardrails))
            {
                profile.AllowGuardrails = allowGuardrails;
                _configuration.Save();
                _armatureManager.RebindAllArmatures();
            }

            var allowPose = profile.AllowPoseValidation;
            if (ImGui.Checkbox("Allow pose validation", ref allowPose))
            {
                profile.AllowPoseValidation = allowPose;
                _configuration.Save();
                _armatureManager.RebindAllArmatures();
            }

            var allowNaturalization = profile.AllowNaturalization;
            if (ImGui.Checkbox("Allow naturalization", ref allowNaturalization))
            {
                profile.AllowNaturalization = allowNaturalization;
                _configuration.Save();
                _armatureManager.RebindAllArmatures();
            }

            ImGui.TreePop();
            ImGui.Spacing();
        }
    }

    private void DrawAdvancedBodyScalingExplainability(AdvancedBodyScalingSettings settings)
    {
        if (!ImGui.CollapsingHeader("Guardrail & Automation Guide"))
            return;

        ImGui.TextWrapped("Lock excludes the whole row or group from automation. Pins protect only the selected scale axes. Guardrails and pose-aware corrections are automation helpers, not hard locks.");

        using var table = ImRaii.Table("AdvancedBodyScalingGuide", 4, ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerV | ImGuiTableFlags.SizingStretchProp);
        if (!table)
            return;

        ImGui.TableSetupColumn("System", ImGuiTableColumnFlags.WidthFixed, 190 * ImGuiHelpers.GlobalScale);
        ImGui.TableSetupColumn("Focus", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn("Prevents", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn("Status", ImGuiTableColumnFlags.WidthFixed, 140 * ImGuiHelpers.GlobalScale);
        ImGui.TableHeadersRow();

        DrawExplainabilityRow(
            "Row/group lock",
            "Whole bone row, group, or region when locked by the editor.",
            "Stops all automation for that locked scope. Manual edits still decide the final values.",
            "Editor-only",
            "Use this when you want to fully exclude a row or group from advanced scaling.");

        DrawExplainabilityRow(
            "Per-axis pins",
            "Individual X, Y, and Z scale axes on a bone row.",
            "Keeps automation from moving that specific axis while still allowing manual edits and automation on the other axes.",
            "Editor-only",
            "Pins are finer-grained than locks: automation cannot move the pinned axis, but you still can.");

        DrawExplainabilityRow(
            "Surface balancing",
            "Neighboring bones and curve chains in the active region.",
            "Abrupt bone-to-bone scale jumps and harsh surface breaks.",
            GetSliderStatus(settings.Enabled, settings.Mode, settings.SurfaceBalancingStrength),
            "This is the main smoothing pass for adjacent body bones.");

        DrawExplainabilityRow(
            "Mass redistribution",
            "Neighbor chains that share visible body mass.",
            "Single-bone spikes that make one area look over-inflated compared to its neighbors.",
            GetSliderStatus(settings.Enabled, settings.Mode, settings.MassRedistributionStrength),
            "This spreads some scale pressure outward so one bone does not carry the whole silhouette change.");

        DrawExplainabilityRow(
            "Proportion guardrails",
            "Shoulder/waist, hip/waist, thigh/calf, and upper-arm/forearm ratios.",
            "Proportion jumps that start to look detached, abruptly tapered, or unstable in motion.",
            GetModeStatus(settings.Enabled, settings.Mode, settings.GuardrailMode.ToString(), settings.GuardrailMode != AdvancedBodyScalingGuardrailMode.Off),
            "Guardrails are soft correction helpers. They do not replace your edits, but they can pull extreme ratios back toward safer ranges.");

        DrawExplainabilityRow(
            "Pose-aware corrections",
            "Upper-arm/forearm and thigh/calf transitions under stress.",
            "Elbow, knee, and limb taper artifacts that tend to show up more in motion than in a neutral stance.",
            GetModeStatus(settings.Enabled, settings.Mode, settings.PoseValidationMode.ToString(), settings.PoseValidationMode != AdvancedBodyScalingPoseValidationMode.Off),
            "This is a lightweight motion-safety layer, not a full runtime pose solver.");

        DrawExplainabilityRow(
            "Neck/shoulder compensation",
            "Upper spine, neck, clavicles, and shoulder roots.",
            "Long-neck, detached-shoulder, and harsh neck-to-chest bridge shapes.",
            settings.NeckLengthCompensation > 0f || settings.UseRaceSpecificNeckCompensation ? "Active" : "Off",
            "Race presets can override these neck settings for supported races, but they stay on the normal supported scale path.");

        DrawExplainabilityRow(
            "Bone Importance Weighting",
            "Supported body, legs, hands, and feet model slots when model data is available; heuristic fallback otherwise.",
            "Over-trusting tiny/local weighted regions or applying the same smoothing/propagation authority to every bone.",
            settings.ModelDerivedBoneImportanceEnabled
                ? $"Active ({settings.BoneImportanceHeuristicBlend:0.00} blend)"
                : "Off",
            "Bone Importance Weighting refines how strongly bones participate in propagation, smoothing, redistribution, guardrails, curve smoothing, and pose-aware validation. It stays transform-based and falls back safely when model data is unavailable.");

        DrawExplainabilityRow(
            "RBF pose-space correctives",
            "Neck/shoulder, clavicle/upper chest, hip/upper thigh, and other supported transition regions under stressful poses.",
            "Detached transitions and harsh region bridges that become more visible when the body is bent, raised, or twisted.",
            settings.PoseCorrectives.Enabled ? "Active" : "Off",
            "This is a transform-based corrective layer. It interpolates between stored sample poses on supported bones only and falls back safely when no supported corrective morph path exists.");

        DrawExplainabilityRow(
            "Full IK retargeting",
            "Pelvis/root, spine, neck/head, arms, and legs on supported ordinary bones before the final IK pass.",
            "Animation-intent drift on scaled bodies, including reach mismatch, stride mismatch, and posture drift caused by changed proportions.",
            settings.FullIkRetargeting.Enabled ? "Active" : "Off",
            "This is a conservative supported-bone retargeting layer. It adapts pose intent from proportion deltas, yields to locks and pinned axes, and hands the result to Full-Body IK for the final coherence pass.");

        DrawExplainabilityRow(
            "Motion warping",
            "Pelvis/root, spine, neck/head, arms, and legs on supported ordinary bones during observed locomotion.",
            "Stride-length mismatch, movement-direction drift, and locomotion posture imbalance that can remain after retargeting on scaled bodies.",
            settings.MotionWarping.Enabled ? "Active" : "Off",
            "This build supports Tier C locomotion warping only. It derives conservative stride, orientation, and posture pressure from observed movement, ignores unsupported target-based warping, yields to locks and pinned axes, and hands the result to Full-Body IK.");

        DrawExplainabilityRow(
            "Full-body IK",
            "Pelvis/root, spine, neck/head, arms, and legs on supported ordinary bones.",
            "Whole-body pose drift after heavy scaling, including planted-feet mismatch, shoulder/arm disconnects, and pelvis/spine imbalance.",
            settings.FullBodyIk.Enabled ? "Active" : "Off",
            "This is a conservative final supported-bone pose solver. It ignores unsupported custom extras and yields to row locks and pinned axes when they conflict.");

        DrawExplainabilityRow(
            "Animation-safe mode",
            "Whole advanced scaling stack with extra caution near joints and extremities.",
            "Overly aggressive propagation, sharp extremity response, and brittle motion behavior.",
            settings.AnimationSafeModeEnabled ? "On" : "Off",
            "This is a coordinated conservative preset, not a separate scaling system.");

        DrawExplainabilityRow(
            "Region tuning",
            "Per-region multipliers for propagation, smoothing, guardrails, pose validation, and naturalization.",
            "One-size-fits-all behavior when different body regions need different safety or smoothing strengths.",
            HasCustomizedRegionProfiles(settings) ? "Customized" : "Defaults",
            "Region tuning changes how strongly each body area participates without depending on unsupported extra physics bones.");
    }

    private static void DrawExplainabilityRow(string system, string focus, string prevents, string status, string tooltip)
    {
        ImGui.TableNextRow();

        ImGui.TableNextColumn();
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted(system);
        CtrlHelper.AddHoverText(tooltip);

        ImGui.TableNextColumn();
        ImGui.TextWrapped(focus);

        ImGui.TableNextColumn();
        ImGui.TextWrapped(prevents);

        ImGui.TableNextColumn();
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted(status);
    }

    private static string GetSliderStatus(bool enabled, AdvancedBodyScalingMode mode, float strength)
    {
        if (!enabled || mode == AdvancedBodyScalingMode.Manual || strength <= 0f)
            return "Off";

        return strength >= 0.75f ? "Active" : "Light";
    }

    private static string GetModeStatus(bool enabled, AdvancedBodyScalingMode mode, string label, bool active)
    {
        if (!enabled || mode == AdvancedBodyScalingMode.Manual || !active)
            return "Off";

        return label;
    }

    private static bool HasCustomizedRegionProfiles(AdvancedBodyScalingSettings settings)
    {
        var defaults = AdvancedBodyScalingRegionProfile.CreateDefaults();
        foreach (var region in RegionOrder)
        {
            var profile = settings.GetRegionProfile(region);
            var defaultProfile = defaults.TryGetValue(region, out var value) ? value : new AdvancedBodyScalingRegionProfile();
            if (!MatchesRegionProfile(profile, defaultProfile))
                return true;
        }

        return false;
    }

    private static bool MatchesRegionProfile(AdvancedBodyScalingRegionProfile left, AdvancedBodyScalingRegionProfile right)
        => Math.Abs(left.InfluenceMultiplier - right.InfluenceMultiplier) < 0.0001f
           && Math.Abs(left.SmoothingMultiplier - right.SmoothingMultiplier) < 0.0001f
           && Math.Abs(left.GuardrailMultiplier - right.GuardrailMultiplier) < 0.0001f
           && Math.Abs(left.MassRedistributionMultiplier - right.MassRedistributionMultiplier) < 0.0001f
           && Math.Abs(left.PoseValidationMultiplier - right.PoseValidationMultiplier) < 0.0001f
           && Math.Abs(left.NaturalizationMultiplier - right.NaturalizationMultiplier) < 0.0001f
           && left.AllowNaturalization == right.AllowNaturalization
           && left.AllowGuardrails == right.AllowGuardrails
           && left.AllowPoseValidation == right.AllowPoseValidation;

    #endregion

    #region Support Area
    private void DrawSupportButtons()
    {
        var width = ImGui.CalcTextSize("Copy Support Info to Clipboard").X + (ImGui.GetStyle().FramePadding.X * 2);
        var xPos = ImGui.GetWindowWidth() - width;
        // Respect the scroll bar width.
        if (ImGui.GetScrollMaxY() > 0)
            xPos -= ImGui.GetStyle().ScrollbarSize + ImGui.GetStyle().FramePadding.X;

        ImGui.SetCursorPos(new Vector2(xPos, 0));
        DrawUrlButton("Join Discord for Support", "https://discord.gg/KvGJCCnG8t", DiscordColor, width,
            "Join Discord server run by community volunteers who can help you with your questions. Opens https://discord.gg/KvGJCCnG8t in your web browser.");

        ImGui.SetCursorPos(new Vector2(xPos, ImGui.GetFrameHeightWithSpacing()));
        DrawUrlButton("Support developer using Ko-fi", "https://ko-fi.com/risadev", DonateColor, width,
            "Any donations made are voluntary and treated as a token of gratitude for work done on Customize+. Opens https://ko-fi.com/risadev in your web browser.");

        ImGui.SetCursorPos(new Vector2(xPos, 2 * ImGui.GetFrameHeightWithSpacing()));
        if (ImGui.Button("Copy Support Info to Clipboard"))
        {
            var text = _supportLogBuilderService.BuildSupportLog();
            ImGui.SetClipboardText(text);
            _messageService.NotificationMessage($"Copied Support Info to Clipboard.", NotificationType.Success, false);
        }

        ImGui.SetCursorPos(new Vector2(xPos, 3 * ImGui.GetFrameHeightWithSpacing()));
        if (ImGui.Button("Show update history", new Vector2(width, 0)))
            _changeLog.Changelog.ForceOpen = true;
    }

    /// <summary> Draw a button to open some url. </summary>
    private void DrawUrlButton(string text, string url, uint buttonColor, float width, string? description = null)
    {
        using var color = ImRaii.PushColor(ImGuiCol.Button, buttonColor);
        if (ImGui.Button(text, new Vector2(width, 0)))
            try
            {
                var process = new ProcessStartInfo(url)
                {
                    UseShellExecute = true,
                };
                Process.Start(process);
            }
            catch
            {
                _messageService.NotificationMessage($"Unable to open url {url}.", NotificationType.Error, false);
            }

        ImGuiUtil.HoverTooltip(description ?? $"Open {url}");
    }
    #endregion
}
