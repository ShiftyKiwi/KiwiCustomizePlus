using CustomizePlus.Armatures.Services;
using CustomizePlus.Configuration.Data;
using CustomizePlus.Core.Data;
using CustomizePlus.Core.Helpers;
using CustomizePlus.Core.Services;
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
using System.Diagnostics;
using System.Numerics;

namespace CustomizePlus.UI.Windows.MainWindow.Tabs;

public class SettingsTab
{
    private const uint DiscordColor = 0xFFDA8972;
    private const uint DonateColor = 0xFF5B5EFF;
    private static readonly AdvancedBodyRegion[] RegionOrder =
    {
        AdvancedBodyRegion.Spine,
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

    public SettingsTab(
        IDalamudPluginInterface pluginInterface,
        PluginConfiguration configuration,
        ArmatureManager armatureManager,
        HookingService hookingService,
        TemplateEditorManager templateEditorManager,
        CPlusChangeLog changeLog,
        MessageService messageService,
        SupportLogBuilderService supportLogBuilderService,
        PcpService pcpService)
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

            ImGui.Spacing();
            DrawAdvancedBodyScalingResets(settings);

            ImGui.Spacing();
            DrawAdvancedBodyScalingRegionProfiles(settings);
        }
    }

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

    #endregion

    #region Support Area
    private void DrawSupportButtons()
    {
        var width = ImGui.CalcTextSize("Copy Support Info to Clipboard").X + ImGui.GetStyle().FramePadding.X * 2;
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
