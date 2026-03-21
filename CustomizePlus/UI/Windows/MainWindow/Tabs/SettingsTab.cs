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
    private readonly PcpService _pcpService;
    private readonly GameObjectService _gameObjectService;
    private Race _neckPresetRace = Race.Elezen;
    private Race _lastDetectedNeckPresetRace = Race.Unknown;
    private bool _followDetectedNeckPresetRace;

    public SettingsTab(
        IDalamudPluginInterface pluginInterface,
        PluginConfiguration configuration,
        ArmatureManager armatureManager,
        HookingService hookingService,
        TemplateEditorManager templateEditorManager,
        CPlusChangeLog changeLog,
        MessageService messageService,
        SupportLogBuilderService supportLogBuilderService,
        PcpService pcpService,
        GameObjectService gameObjectService)
    {
        _pluginInterface = pluginInterface;
        _configuration = configuration;
        _armatureManager = armatureManager;
        _hookingService = hookingService;
        _templateEditorManager = templateEditorManager;
        _changeLog = changeLog;
        _messageService = messageService;
        _supportLogBuilderService = supportLogBuilderService;
        _pcpService = pcpService;
        _gameObjectService = gameObjectService;
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

        DrawEnableRootPositionCheckbox();
        DrawTransitionSpeedSlider();
        DrawSoftScaleLimitsCheckbox();
        DrawAutomaticChildScaleCompensationCheckbox();
        DrawAdvancedBodyScalingSettings();
        DrawDebugModeCheckbox();
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
        var settings = _configuration.AdvancedBodyScalingSettings;
        var isEnabled = settings.Enabled;

        if (CtrlHelper.CheckboxWithTextAndHelp("##advancedbodyscaling", "Advanced body scaling",
                "Enable the advanced body scaling pipeline with influence propagation, smoothing, and guardrails. Runtime only.", ref isEnabled))
        {
            settings.Enabled = isEnabled;
            _configuration.Save();
            _armatureManager.RebindAllArmatures();
        }

        using (var disabled = ImRaii.Disabled(!settings.Enabled))
        {
            var mode = settings.Mode;
            if (ImGui.BeginCombo("Automation mode", mode.ToString()))
            {
                foreach (var value in Enum.GetValues<AdvancedBodyScalingMode>())
                {
                    var selected = value == mode;
                    if (ImGui.Selectable(value.ToString(), selected))
                    {
                        settings.Mode = value;
                        _configuration.Save();
                        _armatureManager.RebindAllArmatures();
                    }

                    if (selected)
                        ImGui.SetItemDefaultFocus();
                }

                ImGui.EndCombo();
            }
            CtrlHelper.AddHoverText("Manual disables automation. Assist is light smoothing. Automatic runs full balancing. Strong is more aggressive.");

            var surfaceBalancing = settings.SurfaceBalancingStrength;
            if (ImGui.SliderFloat("Surface balancing strength", ref surfaceBalancing, 0f, 1f, "%.2f"))
            {
                settings.SurfaceBalancingStrength = surfaceBalancing;
                _configuration.Save();
                _armatureManager.RebindAllArmatures();
            }
            CtrlHelper.AddHoverText("Scales how strongly neighboring bones are smoothed. 0 disables, 1 uses the mode default.");

            var massRedistribution = settings.MassRedistributionStrength;
            if (ImGui.SliderFloat("Mass redistribution strength", ref massRedistribution, 0f, 1f, "%.2f"))
            {
                settings.MassRedistributionStrength = massRedistribution;
                _configuration.Save();
                _armatureManager.RebindAllArmatures();
            }
            CtrlHelper.AddHoverText("Scales how much scale deltas are redistributed across neighboring bones. 0 disables, 1 uses the mode default.");

            var guardrailMode = settings.GuardrailMode;
            if (ImGui.BeginCombo("Proportion guardrail mode", guardrailMode.ToString()))
            {
                foreach (var value in Enum.GetValues<AdvancedBodyScalingGuardrailMode>())
                {
                    var selected = value == guardrailMode;
                    if (ImGui.Selectable(value.ToString(), selected))
                    {
                        settings.GuardrailMode = value;
                        _configuration.Save();
                        _armatureManager.RebindAllArmatures();
                    }

                    if (selected)
                        ImGui.SetItemDefaultFocus();
                }

                ImGui.EndCombo();
            }
            CtrlHelper.AddHoverText("Controls how strict the body proportion guardrails are. Off disables guardrails.");

            var naturalization = settings.NaturalizationStrength;
            if (ImGui.SliderFloat("Naturalization strength", ref naturalization, 0f, 1f, "%.2f"))
            {
                settings.NaturalizationStrength = naturalization;
                _configuration.Save();
                _armatureManager.RebindAllArmatures();
            }
            CtrlHelper.AddHoverText("Blends between your edits and the balanced result. 0 keeps your edits, 1 fully balances.");

            var poseValidation = settings.PoseValidationMode;
            if (ImGui.BeginCombo("Pose-aware validation mode", poseValidation.ToString()))
            {
                foreach (var value in Enum.GetValues<AdvancedBodyScalingPoseValidationMode>())
                {
                    var selected = value == poseValidation;
                    if (ImGui.Selectable(value.ToString(), selected))
                    {
                        settings.PoseValidationMode = value;
                        _configuration.Save();
                        _armatureManager.RebindAllArmatures();
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
                settings.AnimationSafeModeEnabled = animationSafeMode;
                _configuration.Save();
                _armatureManager.RebindAllArmatures();
            }
            CtrlHelper.AddHoverText("Applies a conservative safety preset that reins in propagation, keeps extremities calmer, and strengthens smoothing/guardrails near joints without removing manual control.");

            ImGui.Spacing();
            DrawNeckCompensationSettings(settings);

            ImGui.Spacing();
            DrawPoseSpaceCorrectives(settings);

            ImGui.Spacing();
            DrawAdvancedBodyScalingResets(settings);

            ImGui.Spacing();
            DrawAdvancedBodyScalingRegionProfiles(settings);

            ImGui.Spacing();
            DrawAdvancedBodyScalingExplainability(settings);
        }
    }

    private void DrawNeckCompensationSettings(AdvancedBodyScalingSettings settings)
    {
        ImGui.Text("Neck/Shoulder Compensation");

        var neckLength = settings.NeckLengthCompensation;
        if (ImGui.SliderFloat("Neck length compensation", ref neckLength, 0f, 1f, "%.2f"))
        {
            settings.NeckLengthCompensation = neckLength;
            _configuration.Save();
            _armatureManager.RebindAllArmatures();
        }
        CtrlHelper.AddHoverText("Shortens neck length along its primary axis without shrinking width. 0 disables.");

        var blend = settings.NeckShoulderBlendStrength;
        if (ImGui.SliderFloat("Neck-to-shoulder blend", ref blend, 0f, 1f, "%.2f"))
        {
            settings.NeckShoulderBlendStrength = blend;
            _configuration.Save();
            _armatureManager.RebindAllArmatures();
        }
        CtrlHelper.AddHoverText("Blends the length correction into upper spine and shoulder roots to keep transitions smooth.");

        var clavicleSmoothing = settings.ClavicleShoulderSmoothing;
        if (ImGui.SliderFloat("Clavicle/shoulder bridge smoothing", ref clavicleSmoothing, 0f, 1f, "%.2f"))
        {
            settings.ClavicleShoulderSmoothing = clavicleSmoothing;
            _configuration.Save();
            _armatureManager.RebindAllArmatures();
        }
        CtrlHelper.AddHoverText("Adds extra smoothing across clavicles and shoulder roots to avoid abrupt transitions.");

        if (!ImGui.CollapsingHeader("Race-specific neck presets"))
            return;

        var useRacePresets = settings.UseRaceSpecificNeckCompensation;
        if (ImGui.Checkbox("Enable race-specific presets", ref useRacePresets))
        {
            settings.UseRaceSpecificNeckCompensation = useRacePresets;
            _configuration.Save();
            _armatureManager.RebindAllArmatures();
        }
        CtrlHelper.AddHoverText("When enabled, races with presets override the neck compensation values.");

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
        if (ImGui.SliderFloat("Race neck length compensation", ref raceLength, 0f, 1f, "%.2f"))
        {
            preset = EnsureRacePreset(settings, _neckPresetRace, baseline);
            preset.NeckLengthCompensation = raceLength;
            _configuration.Save();
            _armatureManager.RebindAllArmatures();
        }

        var raceBlend = working.NeckShoulderBlendStrength;
        if (ImGui.SliderFloat("Race neck-to-shoulder blend", ref raceBlend, 0f, 1f, "%.2f"))
        {
            preset = EnsureRacePreset(settings, _neckPresetRace, baseline);
            preset.NeckShoulderBlendStrength = raceBlend;
            _configuration.Save();
            _armatureManager.RebindAllArmatures();
        }

        var raceClavicle = working.ClavicleShoulderSmoothing;
        if (ImGui.SliderFloat("Race clavicle/shoulder smoothing", ref raceClavicle, 0f, 1f, "%.2f"))
        {
            preset = EnsureRacePreset(settings, _neckPresetRace, baseline);
            preset.ClavicleShoulderSmoothing = raceClavicle;
            _configuration.Save();
            _armatureManager.RebindAllArmatures();
        }

        if (ImGui.Button("Restore preset defaults"))
        {
            settings.RaceNeckPresets ??= new Dictionary<Race, AdvancedBodyScalingNeckCompensationPreset>();
            preset = AdvancedBodyScalingNeckCompensationPreset.CreateDefault(_neckPresetRace);
            settings.RaceNeckPresets[_neckPresetRace] = preset;
            hasPreset = true;
            working = preset;
            _configuration.Save();
            _armatureManager.RebindAllArmatures();
        }
        CtrlHelper.AddHoverText(
            "Restore preset defaults = restore the shipped/default preset values for the currently selected race, regardless of your current global settings.");

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
            }
        }
        CtrlHelper.AddHoverText(
            "Clear race preset = remove the explicit preset entry for this race and fall back to the current global neck values in the editor/runtime.");

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
        if (!ImGui.CollapsingHeader("Pose-space correctives"))
            return;

        var poseCorrectives = settings.PoseCorrectives;
        var enabled = poseCorrectives.Enabled;
        if (ImGui.Checkbox("Enable pose-space correctives", ref enabled))
        {
            poseCorrectives.Enabled = enabled;
            _configuration.Save();
            _armatureManager.RebindAllArmatures();
        }
        CtrlHelper.AddHoverText("Adds a broader supported-bone pose-space corrective layer for the major problem regions without depending on unsupported IVCS2 physics-bone control.");

        using (var disabled = ImRaii.Disabled(!poseCorrectives.Enabled))
        {
            var strength = poseCorrectives.Strength;
            if (ImGui.SliderFloat("Global corrective strength", ref strength, 0f, 1f, "%.2f"))
            {
                poseCorrectives.Strength = strength;
                _configuration.Save();
                _armatureManager.RebindAllArmatures();
            }
            CtrlHelper.AddHoverText("Scales the overall strength of pose-space correctives across all supported regions.");

            foreach (var region in AdvancedBodyScalingPoseCorrectiveSystem.GetOrderedRegions())
            {
                var label = AdvancedBodyScalingPoseCorrectiveSystem.GetRegionLabel(region);
                var description = AdvancedBodyScalingPoseCorrectiveSystem.GetRegionDescription(region);
                if (!ImGui.TreeNode($"{label}##PoseCorrectiveRegion{region}"))
                    continue;

                var regionSettings = poseCorrectives.GetRegionSettings(region);
                var regionEnabled = regionSettings.Enabled;
                if (ImGui.Checkbox($"Enable##PoseCorrectiveEnabled{region}", ref regionEnabled))
                {
                    regionSettings.Enabled = regionEnabled;
                    _configuration.Save();
                    _armatureManager.RebindAllArmatures();
                }

                var regionStrength = regionSettings.Strength;
                if (ImGui.SliderFloat($"Strength##PoseCorrectiveStrength{region}", ref regionStrength, 0f, 1f, "%.2f"))
                {
                    regionSettings.Strength = regionStrength;
                    _configuration.Save();
                    _armatureManager.RebindAllArmatures();
                }

                if (ImGui.TreeNode($"Advanced tuning##PoseCorrectiveAdvanced{region}"))
                {
                    var threshold = regionSettings.ActivationThreshold;
                    if (ImGui.SliderFloat($"Activation threshold##PoseCorrectiveThreshold{region}", ref threshold, 0f, 1f, "%.2f"))
                    {
                        regionSettings.ActivationThreshold = threshold;
                        _configuration.Save();
                        _armatureManager.RebindAllArmatures();
                    }

                    var deadzone = regionSettings.ActivationDeadzone;
                    if (ImGui.SliderFloat($"Activation deadzone##PoseCorrectiveDeadzone{region}", ref deadzone, 0f, 0.25f, "%.2f"))
                    {
                        regionSettings.ActivationDeadzone = deadzone;
                        _configuration.Save();
                        _armatureManager.RebindAllArmatures();
                    }

                    var smoothing = regionSettings.Smoothing;
                    if (ImGui.SliderFloat($"Smoothing##PoseCorrectiveSmoothing{region}", ref smoothing, 0f, 1f, "%.2f"))
                    {
                        regionSettings.Smoothing = smoothing;
                        _configuration.Save();
                        _armatureManager.RebindAllArmatures();
                    }

                    var falloff = regionSettings.Falloff;
                    if (ImGui.SliderFloat($"Bridge falloff##PoseCorrectiveFalloff{region}", ref falloff, 0f, 1f, "%.2f"))
                    {
                        regionSettings.Falloff = falloff;
                        _configuration.Save();
                        _armatureManager.RebindAllArmatures();
                    }

                    var maxCorrection = regionSettings.MaxCorrection;
                    if (ImGui.SliderFloat($"Max correction clamp##PoseCorrectiveMax{region}", ref maxCorrection, 0f, 0.10f, "%.3f"))
                    {
                        regionSettings.MaxCorrection = maxCorrection;
                        _configuration.Save();
                        _armatureManager.RebindAllArmatures();
                    }

                    var priority = regionSettings.Priority;
                    if (ImGui.SliderFloat($"Blend priority##PoseCorrectivePriority{region}", ref priority, 0.1f, 1.5f, "%.2f"))
                    {
                        regionSettings.Priority = priority;
                        _configuration.Save();
                        _armatureManager.RebindAllArmatures();
                    }

                    ImGui.TextWrapped(description);
                    ImGui.TreePop();
                }
                else
                {
                    ImGui.TextDisabled(description);
                }

                ImGui.TreePop();
                ImGui.Spacing();
            }
        }

        DrawPoseCorrectiveDebugReadout();
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
            ImGui.TextWrapped($"{region.Label}: driver {region.DriverStrength:0.00}, activation {region.Activation:0.00}, corrective {region.Strength:0.00}. {region.DriverSummary}.");
            ImGui.Indent();
            ImGui.TextDisabled(region.Description);
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
            _ => "Limited corrective-transform fallback",
        };

    private void DrawAdvancedBodyScalingResets(AdvancedBodyScalingSettings settings)
    {
        var defaults = new AdvancedBodyScalingSettings();

        ImGui.Text("Quick resets:");
        if (ImGui.Button("Reset Surface Balancing"))
        {
            settings.SurfaceBalancingStrength = defaults.SurfaceBalancingStrength;
            _configuration.Save();
            _armatureManager.RebindAllArmatures();
        }

        ImGui.SameLine();
        if (ImGui.Button("Reset Naturalization"))
        {
            settings.NaturalizationStrength = defaults.NaturalizationStrength;
            _configuration.Save();
            _armatureManager.RebindAllArmatures();
        }

        ImGui.SameLine();
        if (ImGui.Button("Reset Pose-Aware"))
        {
            settings.PoseValidationMode = defaults.PoseValidationMode;
            _configuration.Save();
            _armatureManager.RebindAllArmatures();
        }

        ImGui.SameLine();
        if (ImGui.Button("Reset All Advanced Scaling"))
        {
            settings.ResetToDefaults();
            _configuration.Save();
            _armatureManager.RebindAllArmatures();
        }
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
            "Pose-space correctives",
            "Neck/shoulder, clavicle/upper chest, and hip/upper thigh under stressful poses.",
            "Detached transitions and harsh region bridges that become more visible when the body is bent, raised, or twisted.",
            settings.PoseCorrectives.Enabled ? "Active" : "Off",
            "This is a limited supported-bone corrective layer. It uses standard pose data and falls back safely when no supported corrective morph path exists.");

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
