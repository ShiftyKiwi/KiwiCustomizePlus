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
            CtrlHelper.AddHoverText("Biases advanced scaling and pose-space correctives toward safer, more motion-friendly behavior. It increases smoothing near joints, keeps extremities calmer, and makes corrective behavior more conservative without removing manual control.");

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
            DrawAdvancedBodyScalingResets(settings);

            ImGui.Spacing();
            DrawAdvancedBodyScalingRegionProfiles(settings);

            ImGui.Spacing();
            DrawAdvancedBodyScalingExplainability(settings);
        }
    }

    private void DrawNeckCompensationSettings(AdvancedBodyScalingSettings settings)
    {
        ImGui.Text("Global Neck/Shoulder Baseline");
        ImGui.TextDisabled("These are the default neck/shoulder compensation values used when no race-specific preset overrides them.");

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
        if (ImGui.SliderFloat("Race neck length compensation", ref raceLength, 0f, 1f, "%.2f"))
        {
            preset = EnsureRacePreset(settings, _neckPresetRace, baseline);
            preset.NeckLengthCompensation = raceLength;
            _configuration.Save();
            _armatureManager.RebindAllArmatures();
        }
        CtrlHelper.AddHoverText("Overrides the global neck length compensation baseline for this race preset.");

        var raceBlend = working.NeckShoulderBlendStrength;
        if (ImGui.SliderFloat("Race neck-to-shoulder blend", ref raceBlend, 0f, 1f, "%.2f"))
        {
            preset = EnsureRacePreset(settings, _neckPresetRace, baseline);
            preset.NeckShoulderBlendStrength = raceBlend;
            _configuration.Save();
            _armatureManager.RebindAllArmatures();
        }
        CtrlHelper.AddHoverText("Overrides the global neck-to-shoulder blend baseline for this race preset.");

        var raceClavicle = working.ClavicleShoulderSmoothing;
        if (ImGui.SliderFloat("Race clavicle/shoulder smoothing", ref raceClavicle, 0f, 1f, "%.2f"))
        {
            preset = EnsureRacePreset(settings, _neckPresetRace, baseline);
            preset.ClavicleShoulderSmoothing = raceClavicle;
            _configuration.Save();
            _armatureManager.RebindAllArmatures();
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
        if (!ImGui.CollapsingHeader("Pose-space correctives"))
            return;

        var poseCorrectives = settings.PoseCorrectives;
        ImGui.TextDisabled("Pose-space correctives add small pose-driven corrections in common problem areas to improve transitions during stressed poses. They are conservative by default and do not replace manual control.");

        var enabled = poseCorrectives.Enabled;
        if (ImGui.Checkbox("Enable pose-space correctives", ref enabled))
        {
            poseCorrectives.Enabled = enabled;
            _configuration.Save();
            _armatureManager.RebindAllArmatures();
        }
        CtrlHelper.AddHoverText("Turns the pose-space corrective layer on or off. These corrections are small, pose-driven adjustments for common transition problems and do not replace manual control.");

        ImGui.SameLine();
        if (ImGui.Button("Restore corrective defaults"))
        {
            settings.PoseCorrectives = new AdvancedBodyScalingPoseCorrectiveSettings();
            poseCorrectives = settings.PoseCorrectives;
            _configuration.Save();
            _armatureManager.RebindAllArmatures();
        }
        CtrlHelper.AddHoverText("Restores the shipped pose-space corrective defaults: global enable/strength plus every per-region enable, strength, threshold, deadzone, smoothing, falloff, max clamp, and blend priority value.");

        using (var disabled = ImRaii.Disabled(!poseCorrectives.Enabled))
        {
            var strength = poseCorrectives.Strength;
            if (ImGui.SliderFloat("Global corrective strength", ref strength, 0f, 1f, "%.2f"))
            {
                poseCorrectives.Strength = strength;
                _configuration.Save();
                _armatureManager.RebindAllArmatures();
            }
            CtrlHelper.AddHoverText("Scales how strongly pose-space correctives participate overall. Per-region strength is layered on top of this baseline.");

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
                CtrlHelper.AddHoverText($"Restore the shipped defaults for {label}, including enable, strength, threshold, deadzone, smoothing, falloff, max clamp, and blend priority.");

                ImGui.TextDisabled(description);

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
                    CtrlHelper.AddHoverText("How gradually the corrective ramps in and out instead of changing abruptly.");

                    var falloff = regionSettings.Falloff;
                    if (ImGui.SliderFloat($"Bridge falloff##PoseCorrectiveFalloff{region}", ref falloff, 0f, 1f, "%.2f"))
                    {
                        regionSettings.Falloff = falloff;
                        _configuration.Save();
                        _armatureManager.RebindAllArmatures();
                    }
                    CtrlHelper.AddHoverText("How broadly the corrective spreads across the transition area instead of concentrating in one spot.");

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
        CtrlHelper.AddHoverText("Turns the supported-bone retargeting layer on or off. It runs after pose-space correctives and before the final Full-Body IK pass.");

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
        CtrlHelper.AddHoverText("Turns the final supported-bone full-body IK layer on or off. It runs after Advanced Body Scaling and pose-space correctives, then yields back to locks and pinned axes when they limit the solve.");

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
        CtrlHelper.AddHoverText("Restores only Surface balancing strength. It does not touch pose-space correctives, Full IK retargeting, Motion Warping, Full-Body IK, the global neck/shoulder baseline, race-specific presets, or animation-safe mode.");

        ImGui.SameLine();
        if (ImGui.Button("Reset Naturalization"))
        {
            settings.NaturalizationStrength = defaults.NaturalizationStrength;
            _configuration.Save();
            _armatureManager.RebindAllArmatures();
        }
        CtrlHelper.AddHoverText("Restores only Naturalization strength. It does not touch pose-space correctives, Full IK retargeting, Motion Warping, Full-Body IK, the global neck/shoulder baseline, race-specific presets, or animation-safe mode.");

        ImGui.SameLine();
        if (ImGui.Button("Reset Pose-Aware"))
        {
            settings.PoseValidationMode = defaults.PoseValidationMode;
            _configuration.Save();
            _armatureManager.RebindAllArmatures();
        }
        CtrlHelper.AddHoverText("Restores only Pose-aware validation mode. It does not touch pose-space correctives, Full IK retargeting, Motion Warping, Full-Body IK, the global neck/shoulder baseline, race-specific presets, or animation-safe mode.");

        ImGui.SameLine();
        if (ImGui.Button("Reset All Advanced Scaling"))
        {
            settings.ResetToDefaults();
            _configuration.Save();
            _armatureManager.RebindAllArmatures();
        }
        CtrlHelper.AddHoverText("Restores all Advanced Body Scaling settings to shipped defaults, including pose-space correctives, Full IK retargeting, Motion Warping, Full-Body IK, the global neck/shoulder baseline, race-specific presets, animation-safe mode, and region tuning.");
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
