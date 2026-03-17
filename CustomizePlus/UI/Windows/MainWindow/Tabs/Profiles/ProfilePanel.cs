// Copyright (c) Customize+.
// Licensed under the MIT license.

using Dalamud.Interface;
using Dalamud.Interface.Utility;
using Dalamud.Bindings.ImGui;
using OtterGui;
using OtterGui.Raii;
using OtterGui.Extensions;
using OtterGui.Log;
using OtterGui.Text;
using System;
using System.Linq;
using System.Numerics;
using CustomizePlus.Profiles;
using CustomizePlus.Configuration.Data;
using CustomizePlus.Profiles.Data;
using CustomizePlus.UI.Windows.Controls;
using CustomizePlus.Templates;
using CustomizePlus.Core.Data;
using CustomizePlus.Core.Helpers;
using CustomizePlus.Templates.Events;
using Penumbra.GameData.Actors;
using CustomizePlus.GameData.Extensions;
using Dalamud.Interface.Components;

namespace CustomizePlus.UI.Windows.MainWindow.Tabs.Profiles;

public class ProfilePanel
{
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

    private readonly ProfileFileSystemSelector _selector;
    private readonly ProfileManager _manager;
    private readonly PluginConfiguration _configuration;
    private readonly TemplateCombo _templateCombo;
    private readonly TemplateEditorManager _templateEditorManager;
    private readonly ActorAssignmentUi _actorAssignmentUi;
    private readonly ActorManager _actorManager;
    private readonly TemplateEditorEvent _templateEditorEvent;
    private readonly PopupSystem _popupSystem;
    private readonly Logger _logger;

    private string? _newName;
    private int? _newPriority;
    private Profile? _changedProfile;

    private Action? _endAction;

    private int _dragIndex = -1;

    private string SelectionName
        => _selector.Selected == null ? "No Selection" : _selector.IncognitoMode ? _selector.Selected.Incognito : _selector.Selected.Name.Text;

    public ProfilePanel(
        ProfileFileSystemSelector selector,
        ProfileManager manager,
        PluginConfiguration configuration,
        TemplateCombo templateCombo,
        TemplateEditorManager templateEditorManager,
        ActorAssignmentUi actorAssignmentUi,
        ActorManager actorManager,
        TemplateEditorEvent templateEditorEvent,
        PopupSystem popupSystem,
        Logger logger)
    {
        _selector = selector;
        _manager = manager;
        _configuration = configuration;
        _templateCombo = templateCombo;
        _templateEditorManager = templateEditorManager;
        _actorAssignmentUi = actorAssignmentUi;
        _actorManager = actorManager;
        _templateEditorEvent = templateEditorEvent;
        _popupSystem = popupSystem;
        _logger = logger;
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

    private HeaderDrawer.Button LockButton()
        => _selector.Selected == null
            ? HeaderDrawer.Button.Invisible
            : _selector.Selected.IsWriteProtected
                ? new HeaderDrawer.Button
                {
                    Description = "Make this profile editable.",
                    Icon = FontAwesomeIcon.Lock,
                    OnClick = () => _manager.SetWriteProtection(_selector.Selected!, false)
                }
                : new HeaderDrawer.Button
                {
                    Description = "Write-protect this profile.",
                    Icon = FontAwesomeIcon.LockOpen,
                    OnClick = () => _manager.SetWriteProtection(_selector.Selected!, true)
                };

    private HeaderDrawer.Button ExportToClipboardButton()
         => _selector.Selected == null
        ? HeaderDrawer.Button.Invisible
        :new HeaderDrawer.Button {
            Description = "Copy the current profile combined into one template to your clipboard.",
            Icon = FontAwesomeIcon.Copy,
            OnClick = ExportToClipboard,
            Visible = _selector.Selected != null,
            Disabled = false
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
        ImGui.TextUnformatted("Currently Selected Profiles");
        ImGui.Separator();
        using var table = ImRaii.Table("profile", 3, ImGuiTableFlags.RowBg);
        ImGui.TableSetupColumn("btn", ImGuiTableColumnFlags.WidthFixed, sizeType);
        ImGui.TableSetupColumn("name", ImGuiTableColumnFlags.WidthFixed, sizeMods);
        ImGui.TableSetupColumn("path", ImGuiTableColumnFlags.WidthFixed, sizeFolders);

        var i = 0;
        foreach (var (fullName, path) in _selector.SelectedPaths.Select(p => (p.FullName(), p))
                     .OrderBy(p => p.Item1, StringComparer.OrdinalIgnoreCase))
        {
            using var id = ImRaii.PushId(i++);
            ImGui.TableNextColumn();
            var icon = (path is ProfileFileSystem.Leaf ? FontAwesomeIcon.FileCircleMinus : FontAwesomeIcon.FolderMinus).ToIconString();
            if (ImGuiUtil.DrawDisabledButton(icon, new Vector2(sizeType), "Remove from selection.", false, true))
                _selector.RemovePathFromMultiSelection(path);

            ImGui.TableNextColumn();
            ImGui.AlignTextToFramePadding();
            ImGui.TextUnformatted(path is ProfileFileSystem.Leaf l ? _selector.IncognitoMode ? l.Value.Incognito : l.Value.Name.Text : string.Empty);

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

        DrawEnabledSetting();

        ImGui.Separator();

        using (var disabled = ImRaii.Disabled(_selector.Selected?.IsWriteProtected ?? true))
        {
            DrawBasicSettings();

            ImGui.Separator();

            DrawAdvancedBodyScalingOverrides();

            ImGui.Separator();

            var isShouldDraw = ImGui.CollapsingHeader("Add character");

            if (isShouldDraw)
                DrawAddCharactersArea();

            ImGui.Separator();

            DrawCharacterListArea();

            ImGui.Separator();

            DrawTemplateArea();
        }
    }

    private void DrawEnabledSetting()
    {
        var spacing = ImGui.GetStyle().ItemInnerSpacing with { X = ImGui.GetStyle().ItemSpacing.X, Y = ImGui.GetStyle().ItemSpacing.Y };

        using (var style = ImRaii.PushStyle(ImGuiStyleVar.ItemSpacing, spacing))
        {
            var enabled = _selector.Selected?.Enabled ?? false;
            using (ImRaii.Disabled(_templateEditorManager.IsEditorActive || _templateEditorManager.IsEditorPaused))
            {
                if (ImGui.Checkbox("##Enabled", ref enabled))
                    _manager.SetEnabled(_selector.Selected!, enabled);
                ImGuiUtil.LabeledHelpMarker("Enabled",
                    "Whether the templates in this profile should be applied at all.");
            }
        }
    }

    private void DrawBasicSettings()
    {
        using (var style = ImRaii.PushStyle(ImGuiStyleVar.ButtonTextAlign, new Vector2(0, 0.5f)))
        {
            using (var table = ImRaii.Table("BasicSettings", 2))
            {
                ImGui.TableSetupColumn("BasicCol1", ImGuiTableColumnFlags.WidthFixed, ImGui.CalcTextSize("lorem ipsum dolor").X);
                ImGui.TableSetupColumn("BasicCol2", ImGuiTableColumnFlags.WidthStretch);

                ImGuiUtil.DrawFrameColumn("Profile Name");
                ImGui.TableNextColumn();
                var width = new Vector2(ImGui.GetContentRegionAvail().X, 0);
                var name = _newName ?? _selector.Selected!.Name;
                ImGui.SetNextItemWidth(width.X);

                if (!_selector.IncognitoMode)
                {
                    if (ImGui.InputText("##ProfileName", ref name, 128))
                    {
                        _newName = name;
                        _changedProfile = _selector.Selected;
                    }

                    if (ImGui.IsItemDeactivatedAfterEdit() && _changedProfile != null)
                    {
                        _manager.Rename(_changedProfile, name);
                        _newName = null;
                        _changedProfile = null;
                    }
                }
                else
                    ImGui.TextUnformatted(_selector.Selected!.Incognito);

                ImGui.TableNextRow();

                ImGuiUtil.DrawFrameColumn("Priority");
                ImGui.TableNextColumn();

                var priority = _newPriority ?? _selector.Selected!.Priority;

                ImGui.SetNextItemWidth(50);
                if (ImGui.InputInt("##Priority", ref priority, 0, 0))
                {
                    _newPriority = priority;
                    _changedProfile = _selector.Selected;
                }

                if (ImGui.IsItemDeactivatedAfterEdit() && _changedProfile != null)
                {
                    _manager.SetPriority(_changedProfile, priority);
                    _newPriority = null;
                    _changedProfile = null;
                }

                ImGuiComponents.HelpMarker("Profiles with a higher number here take precedence before profiles with a lower number.\n" +
                    "That means if two or more profiles affect same character, profile with higher priority will be applied to that character.");
            }
        }
    }

    private void DrawAdvancedBodyScalingOverrides()
    {
        if (_selector.Selected == null)
            return;

        var profile = _selector.Selected;
        var globalSettings = _configuration.AdvancedBodyScalingSettings;
        var settingColumnWidth = 190 * ImGuiHelpers.GlobalScale;
        var overrideColumnWidth = MathF.Max(
            80 * ImGuiHelpers.GlobalScale,
            ImGui.CalcTextSize("Override").X + (ImGui.GetStyle().FramePadding.X * 2));
        var overrideTableWidth = new Vector2(ImGui.GetContentRegionAvail().X, 0);

        if (!ImGui.CollapsingHeader("Advanced Body Scaling (Profile)"))
            return;

        var useOverrides = profile.AdvancedBodyScalingOverrides.UseProfileOverrides;
        if (ImGui.RadioButton("Use Global Settings", !useOverrides))
            _manager.UpdateAdvancedBodyScalingOverrides(profile, settings => settings.UseProfileOverrides = false);

        ImGui.SameLine();
        if (ImGui.RadioButton("Use Profile Overrides", useOverrides))
            _manager.UpdateAdvancedBodyScalingOverrides(profile, settings => settings.UseProfileOverrides = true);

        if (!profile.AdvancedBodyScalingOverrides.UseProfileOverrides)
        {
            ImGui.TextDisabled("Inheriting global advanced body scaling settings.");
            return;
        }

        void ToggleOverride(Action<AdvancedBodyScalingOverrides> update)
            => _manager.UpdateAdvancedBodyScalingOverrides(profile, settings => update(settings.Overrides));

        void UpdateRegionOverride(AdvancedBodyRegion region, Action<AdvancedBodyScalingRegionProfileOverrides> update)
            => _manager.UpdateAdvancedBodyScalingOverrides(profile, settings =>
            {
                var overrides = settings.Overrides;
                if (!overrides.RegionOverrides.TryGetValue(region, out var regionOverride))
                {
                    regionOverride = new AdvancedBodyScalingRegionProfileOverrides();
                    overrides.RegionOverrides[region] = regionOverride;
                }

                update(regionOverride);

                if (regionOverride.IsEmpty)
                    overrides.RegionOverrides.Remove(region);
            });

        var overrides = profile.AdvancedBodyScalingOverrides.Overrides;
        using (var table = ImRaii.Table("ProfileAdvancedBodyScaling", 3, ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollX, overrideTableWidth))
        {
            if (!table)
                return;

            ImGui.TableSetupColumn("Setting", ImGuiTableColumnFlags.WidthFixed, settingColumnWidth);
            ImGui.TableSetupColumn("Value", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupColumn("Override", ImGuiTableColumnFlags.WidthFixed, overrideColumnWidth);
            ImGui.TableSetupScrollFreeze(0, 1);
            ImGui.TableHeadersRow();

            // Enabled
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.AlignTextToFramePadding();
            ImGui.TextUnformatted("Advanced body scaling");
            ImGui.TableNextColumn();
            if (overrides.Enabled.HasValue)
            {
                var enabled = overrides.Enabled.Value;
                if (ImGui.Checkbox("##ProfileAdvScalingEnabled", ref enabled))
                    ToggleOverride(o => o.Enabled = enabled);
            }
            else
            {
                var enabled = globalSettings.Enabled;
                using (ImRaii.Disabled())
                    ImGui.Checkbox("##ProfileAdvScalingEnabled", ref enabled);
            }
            CtrlHelper.AddHoverText("Enable or disable advanced body scaling for this profile.");
            ImGui.TableNextColumn();
            var enabledOverride = overrides.Enabled.HasValue;
            if (ImGui.Checkbox("##ProfileAdvScalingEnabledOverride", ref enabledOverride))
                ToggleOverride(o => o.Enabled = enabledOverride ? globalSettings.Enabled : null);

            // Automation mode
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.AlignTextToFramePadding();
            ImGui.TextUnformatted("Automation mode");
            ImGui.TableNextColumn();
            if (overrides.Mode.HasValue)
            {
                var mode = overrides.Mode ?? globalSettings.Mode;
                ImGui.SetNextItemWidth(-1);
                if (ImGui.BeginCombo("##ProfileAdvScalingMode", mode.ToString()))
                {
                    foreach (var value in Enum.GetValues<AdvancedBodyScalingMode>())
                    {
                        var selected = value == mode;
                        if (ImGui.Selectable(value.ToString(), selected))
                            ToggleOverride(o => o.Mode = value);

                        if (selected)
                            ImGui.SetItemDefaultFocus();
                    }
                    ImGui.EndCombo();
                }
            }
            else
            {
                ImGui.TextDisabled($"Global: {globalSettings.Mode}");
            }
            CtrlHelper.AddHoverText("Manual disables automation. Assist is light smoothing. Automatic runs full balancing. Strong is more aggressive.");
            ImGui.TableNextColumn();
            var modeOverride = overrides.Mode.HasValue;
            if (ImGui.Checkbox("##ProfileAdvScalingModeOverride", ref modeOverride))
                ToggleOverride(o => o.Mode = modeOverride ? globalSettings.Mode : null);

            // Surface balancing strength
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.AlignTextToFramePadding();
            ImGui.TextUnformatted("Surface balancing strength");
            ImGui.TableNextColumn();
            if (overrides.SurfaceBalancingStrength.HasValue)
            {
                var value = overrides.SurfaceBalancingStrength.Value;
                ImGui.SetNextItemWidth(-1);
                if (ImGui.SliderFloat("##ProfileAdvScalingSurface", ref value, 0f, 1f, "%.2f"))
                    ToggleOverride(o => o.SurfaceBalancingStrength = value);
            }
            else
            {
                var value = globalSettings.SurfaceBalancingStrength;
                using (ImRaii.Disabled())
                {
                    ImGui.SetNextItemWidth(-1);
                    ImGui.SliderFloat("##ProfileAdvScalingSurface", ref value, 0f, 1f, "%.2f");
                }
            }
            CtrlHelper.AddHoverText("Scales how strongly neighboring bones are smoothed. 0 disables, 1 uses the mode default.");
            ImGui.TableNextColumn();
            var surfaceOverride = overrides.SurfaceBalancingStrength.HasValue;
            if (ImGui.Checkbox("##ProfileAdvScalingSurfaceOverride", ref surfaceOverride))
                ToggleOverride(o => o.SurfaceBalancingStrength = surfaceOverride ? globalSettings.SurfaceBalancingStrength : null);

            // Mass redistribution strength
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.AlignTextToFramePadding();
            ImGui.TextUnformatted("Mass redistribution strength");
            ImGui.TableNextColumn();
            if (overrides.MassRedistributionStrength.HasValue)
            {
                var value = overrides.MassRedistributionStrength.Value;
                ImGui.SetNextItemWidth(-1);
                if (ImGui.SliderFloat("##ProfileAdvScalingMass", ref value, 0f, 1f, "%.2f"))
                    ToggleOverride(o => o.MassRedistributionStrength = value);
            }
            else
            {
                var value = globalSettings.MassRedistributionStrength;
                using (ImRaii.Disabled())
                {
                    ImGui.SetNextItemWidth(-1);
                    ImGui.SliderFloat("##ProfileAdvScalingMass", ref value, 0f, 1f, "%.2f");
                }
            }
            CtrlHelper.AddHoverText("Scales how much scale deltas are redistributed across neighboring bones. 0 disables, 1 uses the mode default.");
            ImGui.TableNextColumn();
            var massOverride = overrides.MassRedistributionStrength.HasValue;
            if (ImGui.Checkbox("##ProfileAdvScalingMassOverride", ref massOverride))
                ToggleOverride(o => o.MassRedistributionStrength = massOverride ? globalSettings.MassRedistributionStrength : null);

            // Proportion guardrail mode
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.AlignTextToFramePadding();
            ImGui.TextUnformatted("Proportion guardrail mode");
            ImGui.TableNextColumn();
            if (overrides.GuardrailMode.HasValue)
            {
                var mode = overrides.GuardrailMode ?? globalSettings.GuardrailMode;
                ImGui.SetNextItemWidth(-1);
                if (ImGui.BeginCombo("##ProfileAdvScalingGuardrail", mode.ToString()))
                {
                    foreach (var value in Enum.GetValues<AdvancedBodyScalingGuardrailMode>())
                    {
                        var selected = value == mode;
                        if (ImGui.Selectable(value.ToString(), selected))
                            ToggleOverride(o => o.GuardrailMode = value);

                        if (selected)
                            ImGui.SetItemDefaultFocus();
                    }
                    ImGui.EndCombo();
                }
            }
            else
            {
                ImGui.TextDisabled($"Global: {globalSettings.GuardrailMode}");
            }
            CtrlHelper.AddHoverText("Controls how strict the body proportion guardrails are. Off disables guardrails.");
            ImGui.TableNextColumn();
            var guardrailOverride = overrides.GuardrailMode.HasValue;
            if (ImGui.Checkbox("##ProfileAdvScalingGuardrailOverride", ref guardrailOverride))
                ToggleOverride(o => o.GuardrailMode = guardrailOverride ? globalSettings.GuardrailMode : null);

            // Naturalization strength
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.AlignTextToFramePadding();
            ImGui.TextUnformatted("Naturalization strength");
            ImGui.TableNextColumn();
            if (overrides.NaturalizationStrength.HasValue)
            {
                var value = overrides.NaturalizationStrength.Value;
                ImGui.SetNextItemWidth(-1);
                if (ImGui.SliderFloat("##ProfileAdvScalingNaturalization", ref value, 0f, 1f, "%.2f"))
                    ToggleOverride(o => o.NaturalizationStrength = value);
            }
            else
            {
                var value = globalSettings.NaturalizationStrength;
                using (ImRaii.Disabled())
                {
                    ImGui.SetNextItemWidth(-1);
                    ImGui.SliderFloat("##ProfileAdvScalingNaturalization", ref value, 0f, 1f, "%.2f");
                }
            }
            CtrlHelper.AddHoverText("Blends between your edits and the balanced result. 0 keeps your edits, 1 fully balances.");
            ImGui.TableNextColumn();
            var naturalizationOverride = overrides.NaturalizationStrength.HasValue;
            if (ImGui.Checkbox("##ProfileAdvScalingNaturalizationOverride", ref naturalizationOverride))
                ToggleOverride(o => o.NaturalizationStrength = naturalizationOverride ? globalSettings.NaturalizationStrength : null);

            // Pose-aware validation mode
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.AlignTextToFramePadding();
            ImGui.TextUnformatted("Pose-aware validation mode");
            ImGui.TableNextColumn();
            if (overrides.PoseValidationMode.HasValue)
            {
                var mode = overrides.PoseValidationMode ?? globalSettings.PoseValidationMode;
                ImGui.SetNextItemWidth(-1);
                if (ImGui.BeginCombo("##ProfileAdvScalingPose", mode.ToString()))
                {
                    foreach (var value in Enum.GetValues<AdvancedBodyScalingPoseValidationMode>())
                    {
                        var selected = value == mode;
                        if (ImGui.Selectable(value.ToString(), selected))
                            ToggleOverride(o => o.PoseValidationMode = value);

                        if (selected)
                            ImGui.SetItemDefaultFocus();
                    }
                    ImGui.EndCombo();
                }
            }
            else
            {
                ImGui.TextDisabled($"Global: {globalSettings.PoseValidationMode}");
            }
            CtrlHelper.AddHoverText("Adds extra pose-aware guardrails to reduce deformation in extreme poses.");
            ImGui.TableNextColumn();
            var poseOverride = overrides.PoseValidationMode.HasValue;
            if (ImGui.Checkbox("##ProfileAdvScalingPoseOverride", ref poseOverride))
                ToggleOverride(o => o.PoseValidationMode = poseOverride ? globalSettings.PoseValidationMode : null);

            // Neck length compensation
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.AlignTextToFramePadding();
            ImGui.TextUnformatted("Neck length compensation");
            ImGui.TableNextColumn();
            if (overrides.NeckLengthCompensation.HasValue)
            {
                var value = overrides.NeckLengthCompensation.Value;
                ImGui.SetNextItemWidth(-1);
                if (ImGui.SliderFloat("##ProfileAdvScalingNeckLength", ref value, 0f, 1f, "%.2f"))
                    ToggleOverride(o => o.NeckLengthCompensation = value);
            }
            else
            {
                var value = globalSettings.NeckLengthCompensation;
                using (ImRaii.Disabled())
                {
                    ImGui.SetNextItemWidth(-1);
                    ImGui.SliderFloat("##ProfileAdvScalingNeckLength", ref value, 0f, 1f, "%.2f");
                }
            }
            CtrlHelper.AddHoverText("Shortens neck length along its primary axis without shrinking width.");
            ImGui.TableNextColumn();
            var neckLengthOverride = overrides.NeckLengthCompensation.HasValue;
            if (ImGui.Checkbox("##ProfileAdvScalingNeckLengthOverride", ref neckLengthOverride))
                ToggleOverride(o => o.NeckLengthCompensation = neckLengthOverride ? globalSettings.NeckLengthCompensation : null);

            // Neck-to-shoulder blend
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.AlignTextToFramePadding();
            ImGui.TextUnformatted("Neck-to-shoulder blend");
            ImGui.TableNextColumn();
            if (overrides.NeckShoulderBlendStrength.HasValue)
            {
                var value = overrides.NeckShoulderBlendStrength.Value;
                ImGui.SetNextItemWidth(-1);
                if (ImGui.SliderFloat("##ProfileAdvScalingNeckBlend", ref value, 0f, 1f, "%.2f"))
                    ToggleOverride(o => o.NeckShoulderBlendStrength = value);
            }
            else
            {
                var value = globalSettings.NeckShoulderBlendStrength;
                using (ImRaii.Disabled())
                {
                    ImGui.SetNextItemWidth(-1);
                    ImGui.SliderFloat("##ProfileAdvScalingNeckBlend", ref value, 0f, 1f, "%.2f");
                }
            }
            CtrlHelper.AddHoverText("Blends the length correction into upper spine and shoulder roots.");
            ImGui.TableNextColumn();
            var neckBlendOverride = overrides.NeckShoulderBlendStrength.HasValue;
            if (ImGui.Checkbox("##ProfileAdvScalingNeckBlendOverride", ref neckBlendOverride))
                ToggleOverride(o => o.NeckShoulderBlendStrength = neckBlendOverride ? globalSettings.NeckShoulderBlendStrength : null);

            // Clavicle/shoulder bridge smoothing
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.AlignTextToFramePadding();
            ImGui.TextUnformatted("Clavicle/shoulder smoothing");
            ImGui.TableNextColumn();
            if (overrides.ClavicleShoulderSmoothing.HasValue)
            {
                var value = overrides.ClavicleShoulderSmoothing.Value;
                ImGui.SetNextItemWidth(-1);
                if (ImGui.SliderFloat("##ProfileAdvScalingClavicleSmoothing", ref value, 0f, 1f, "%.2f"))
                    ToggleOverride(o => o.ClavicleShoulderSmoothing = value);
            }
            else
            {
                var value = globalSettings.ClavicleShoulderSmoothing;
                using (ImRaii.Disabled())
                {
                    ImGui.SetNextItemWidth(-1);
                    ImGui.SliderFloat("##ProfileAdvScalingClavicleSmoothing", ref value, 0f, 1f, "%.2f");
                }
            }
            CtrlHelper.AddHoverText("Adds extra smoothing across clavicles and shoulder roots.");
            ImGui.TableNextColumn();
            var clavicleOverride = overrides.ClavicleShoulderSmoothing.HasValue;
            if (ImGui.Checkbox("##ProfileAdvScalingClavicleSmoothingOverride", ref clavicleOverride))
                ToggleOverride(o => o.ClavicleShoulderSmoothing = clavicleOverride ? globalSettings.ClavicleShoulderSmoothing : null);
        }

        ImGui.Spacing();
        if (!ImGui.CollapsingHeader("Region Tuning Overrides"))
            return;

        ImGui.TextDisabled("Override per-region tuning settings. Disabled fields inherit the global region tuning.");

        foreach (var region in RegionOrder)
        {
            var globalProfile = globalSettings.GetRegionProfile(region);
            overrides.RegionOverrides.TryGetValue(region, out var regionOverride);

            if (!ImGui.TreeNode($"{region}##ProfileRegion{region}"))
                continue;

            using var regionTable = ImRaii.Table(
                $"ProfileRegionOverrides_{region}",
                3,
                ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollX,
                new Vector2(ImGui.GetContentRegionAvail().X, 0));
            if (regionTable)
            {
                ImGui.TableSetupColumn("Setting", ImGuiTableColumnFlags.WidthFixed, settingColumnWidth);
                ImGui.TableSetupColumn("Value", ImGuiTableColumnFlags.WidthStretch);
                ImGui.TableSetupColumn("Override", ImGuiTableColumnFlags.WidthFixed, overrideColumnWidth);
                ImGui.TableSetupScrollFreeze(0, 1);
                ImGui.TableHeadersRow();

                void DrawRegionFloatOverride(
                    string label,
                    string idSuffix,
                    float globalValue,
                    float? overrideValue,
                    Action<AdvancedBodyScalingRegionProfileOverrides, float?> setter)
                {
                    ImGui.TableNextRow();
                    ImGui.TableNextColumn();
                    ImGui.AlignTextToFramePadding();
                    ImGui.TextUnformatted(label);
                    ImGui.TableNextColumn();

                    if (overrideValue.HasValue)
                    {
                        var value = overrideValue.Value;
                        ImGui.SetNextItemWidth(-1);
                        if (ImGui.SliderFloat($"##{idSuffix}_{region}", ref value, 0f, 1f, "%.2f"))
                            UpdateRegionOverride(region, o => setter(o, value));
                    }
                    else
                    {
                        var value = globalValue;
                        using (ImRaii.Disabled())
                        {
                            ImGui.SetNextItemWidth(-1);
                            ImGui.SliderFloat($"##{idSuffix}_{region}", ref value, 0f, 1f, "%.2f");
                        }
                    }

                    ImGui.TableNextColumn();
                    var enabled = overrideValue.HasValue;
                    if (ImGui.Checkbox($"##{idSuffix}_{region}_Override", ref enabled))
                        UpdateRegionOverride(region, o => setter(o, enabled ? globalValue : null));
                }

                void DrawRegionBoolOverride(
                    string label,
                    string idSuffix,
                    bool globalValue,
                    bool? overrideValue,
                    Action<AdvancedBodyScalingRegionProfileOverrides, bool?> setter)
                {
                    ImGui.TableNextRow();
                    ImGui.TableNextColumn();
                    ImGui.AlignTextToFramePadding();
                    ImGui.TextUnformatted(label);
                    ImGui.TableNextColumn();

                    if (overrideValue.HasValue)
                    {
                        var value = overrideValue.Value;
                        if (ImGui.Checkbox($"##{idSuffix}_{region}", ref value))
                            UpdateRegionOverride(region, o => setter(o, value));
                    }
                    else
                    {
                        var value = globalValue;
                        using (ImRaii.Disabled())
                            ImGui.Checkbox($"##{idSuffix}_{region}", ref value);
                    }

                    ImGui.TableNextColumn();
                    var enabled = overrideValue.HasValue;
                    if (ImGui.Checkbox($"##{idSuffix}_{region}_Override", ref enabled))
                        UpdateRegionOverride(region, o => setter(o, enabled ? globalValue : null));
                }

                DrawRegionFloatOverride("Influence (propagation)", "Influence", globalProfile.InfluenceMultiplier, regionOverride?.InfluenceMultiplier,
                    (o, v) => o.InfluenceMultiplier = v);
                DrawRegionFloatOverride("Smoothing", "Smoothing", globalProfile.SmoothingMultiplier, regionOverride?.SmoothingMultiplier,
                    (o, v) => o.SmoothingMultiplier = v);
                DrawRegionFloatOverride("Guardrail strength", "Guardrail", globalProfile.GuardrailMultiplier, regionOverride?.GuardrailMultiplier,
                    (o, v) => o.GuardrailMultiplier = v);
                DrawRegionFloatOverride("Mass redistribution", "Mass", globalProfile.MassRedistributionMultiplier, regionOverride?.MassRedistributionMultiplier,
                    (o, v) => o.MassRedistributionMultiplier = v);
                DrawRegionFloatOverride("Pose validation", "Pose", globalProfile.PoseValidationMultiplier, regionOverride?.PoseValidationMultiplier,
                    (o, v) => o.PoseValidationMultiplier = v);
                DrawRegionFloatOverride("Naturalization", "Naturalization", globalProfile.NaturalizationMultiplier, regionOverride?.NaturalizationMultiplier,
                    (o, v) => o.NaturalizationMultiplier = v);

                DrawRegionBoolOverride("Allow guardrails", "AllowGuardrails", globalProfile.AllowGuardrails, regionOverride?.AllowGuardrails,
                    (o, v) => o.AllowGuardrails = v);
                DrawRegionBoolOverride("Allow pose validation", "AllowPose", globalProfile.AllowPoseValidation, regionOverride?.AllowPoseValidation,
                    (o, v) => o.AllowPoseValidation = v);
                DrawRegionBoolOverride("Allow naturalization", "AllowNatural", globalProfile.AllowNaturalization, regionOverride?.AllowNaturalization,
                    (o, v) => o.AllowNaturalization = v);
            }

            ImGui.TreePop();
            ImGui.Spacing();
        }
    }

     private void ExportToClipboard()
    {
        try
        {
            ImUtf8.SetClipboardText(Base64Helper.ExportProfileToBase64(_selector.Selected!));
            _popupSystem.ShowPopup(PopupSystem.Messages.ClipboardDataNotLongTerm);
        }
        catch (Exception ex)
        {
            _logger.Error($"Could not copy data from profile {_selector.Selected!.UniqueId} to clipboard: {ex}");
            _popupSystem.ShowPopup(PopupSystem.Messages.ActionError);
        }
    }

    private void DrawAddCharactersArea()
    {
        using (var style = ImRaii.PushStyle(ImGuiStyleVar.ButtonTextAlign, new Vector2(0, 0.5f)))
        {
            var width = new Vector2(
                ImGui.GetContentRegionAvail().X - ImGui.CalcTextSize("Limit to my creatures").X - 68,
                0);

            ImGui.SetNextItemWidth(width.X);

            bool appliesToMultiple = _manager.DefaultProfile == _selector.Selected || _manager.DefaultLocalPlayerProfile == _selector.Selected;
            using (ImRaii.Disabled(appliesToMultiple))
            {
                _actorAssignmentUi.DrawWorldCombo(width.X / 2);
                ImGui.SameLine();
                _actorAssignmentUi.DrawPlayerInput(width.X / 2);

                var buttonWidth = new Vector2(
                    (165 * ImGuiHelpers.GlobalScale) - (ImGui.GetStyle().ItemSpacing.X / 2),
                    0);

                if (ImGuiUtil.DrawDisabledButton("Apply to player character", buttonWidth, string.Empty, !_actorAssignmentUi.CanSetPlayer))
                    _manager.AddCharacter(_selector.Selected!, _actorAssignmentUi.PlayerIdentifier);

                ImGui.SameLine();

                if (ImGuiUtil.DrawDisabledButton("Apply to retainer", buttonWidth, string.Empty, !_actorAssignmentUi.CanSetRetainer))
                    _manager.AddCharacter(_selector.Selected!, _actorAssignmentUi.RetainerIdentifier);

                ImGui.SameLine();

                if (ImGuiUtil.DrawDisabledButton("Apply to mannequin", buttonWidth, string.Empty, !_actorAssignmentUi.CanSetMannequin))
                    _manager.AddCharacter(_selector.Selected!, _actorAssignmentUi.MannequinIdentifier);

                var currentPlayer = _actorManager.GetCurrentPlayer().CreatePermanent();
                if (ImGuiUtil.DrawDisabledButton("Apply to current character", buttonWidth, string.Empty, !currentPlayer.IsValid))
                    _manager.AddCharacter(_selector.Selected!, currentPlayer);

                ImGui.Separator();

                _actorAssignmentUi.DrawObjectKindCombo(width.X / 2);
                ImGui.SameLine();
                _actorAssignmentUi.DrawNpcInput(width.X / 2);

                if (ImGuiUtil.DrawDisabledButton("Apply to selected NPC", buttonWidth, string.Empty, !_actorAssignmentUi.CanSetNpc))
                    _manager.AddCharacter(_selector.Selected!, _actorAssignmentUi.NpcIdentifier);
            }
        }
    }

    private void DrawCharacterListArea()
    {
        var isDefaultLP = _manager.DefaultLocalPlayerProfile == _selector.Selected;
        var isDefaultLPOrCurrentProfilesEnabled = (_manager.DefaultLocalPlayerProfile?.Enabled ?? false) || (_selector.Selected?.Enabled ?? false);
        using (ImRaii.Disabled(isDefaultLPOrCurrentProfilesEnabled))
        {
            if (ImGui.Checkbox("##DefaultLocalPlayerProfile", ref isDefaultLP))
                _manager.SetDefaultLocalPlayerProfile(isDefaultLP ? _selector.Selected! : null);
            ImGuiUtil.LabeledHelpMarker("Apply to any character you are logged in with",
                "Whether the templates in this profile should be applied to any character you are currently logged in with.\r\nTakes priority over the next option for said character.\r\nThis setting cannot be applied to multiple profiles.");
        }
        if (isDefaultLPOrCurrentProfilesEnabled)
        {
            ImGui.SameLine();
            ImGui.PushStyleColor(ImGuiCol.Text, Constants.Colors.Warning);
            ImGuiUtil.PrintIcon(FontAwesomeIcon.ExclamationTriangle);
            ImGui.PopStyleColor();
            ImGuiUtil.HoverTooltip("Can only be changed when both currently selected and profile where this checkbox is checked are disabled.");
        }

        ImGui.SameLine();
        using(ImRaii.Disabled(true))
            ImGui.Button("##splitter", new Vector2(1, ImGui.GetFrameHeight()));
        ImGui.SameLine();

        var isDefault = _manager.DefaultProfile == _selector.Selected;
        var isDefaultOrCurrentProfilesEnabled = (_manager.DefaultProfile?.Enabled ?? false) || (_selector.Selected?.Enabled ?? false);
        using (ImRaii.Disabled(isDefaultOrCurrentProfilesEnabled))
        {
            if (ImGui.Checkbox("##DefaultProfile", ref isDefault))
                _manager.SetDefaultProfile(isDefault ? _selector.Selected! : null);
            ImGuiUtil.LabeledHelpMarker("Apply to all players and retainers",
                "Whether the templates in this profile are applied to all players and retainers without a specific profile.\r\nThis setting cannot be applied to multiple profiles.");
        }
        if (isDefaultOrCurrentProfilesEnabled)
        {
            ImGui.SameLine();
            ImGui.PushStyleColor(ImGuiCol.Text, Constants.Colors.Warning);
            ImGuiUtil.PrintIcon(FontAwesomeIcon.ExclamationTriangle);
            ImGui.PopStyleColor();
            ImGuiUtil.HoverTooltip("Can only be changed when both currently selected and profile where this checkbox is checked are disabled.");
        }
        bool appliesToMultiple = _manager.DefaultProfile == _selector.Selected || _manager.DefaultLocalPlayerProfile == _selector.Selected;

        ImGui.Separator();

        using var dis = ImRaii.Disabled(appliesToMultiple);
        using var table = ImRaii.Table("CharacterTable", 2, ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollX | ImGuiTableFlags.ScrollY, new Vector2(ImGui.GetContentRegionAvail().X, 200));
        if (!table)
            return;

        ImGui.TableSetupColumn("##charaDel", ImGuiTableColumnFlags.WidthFixed, ImGui.GetFrameHeight());
        ImGui.TableSetupColumn("Character", ImGuiTableColumnFlags.WidthFixed, 320 * ImGuiHelpers.GlobalScale);
        ImGui.TableHeadersRow();

        if (appliesToMultiple)
        {
            ImGui.TableNextColumn();
            ImGui.TableNextColumn();
            ImGui.AlignTextToFramePadding();
            ImGui.TextUnformatted("Applies to multiple targets");
            return;
        }

        //warn: .ToList() might be performance critical at some point
        //the copying via ToList is done because manipulations with .Templates list result in "Collection was modified" exception here
        var charas = _selector.Selected!.Characters.WithIndex().ToList();

        if (charas.Count == 0)
        {
            ImGui.TableNextColumn();
            ImGui.TableNextColumn();
            ImGui.AlignTextToFramePadding();
            ImGui.TextUnformatted("No characters are associated with this profile");
        }

        foreach (var (character, idx) in charas)
        {
            using var id = ImRaii.PushId(idx);
            ImGui.TableNextColumn();
            var keyValid = _configuration.UISettings.DeleteTemplateModifier.IsActive();
            var tt = keyValid
                ? "Remove this character from the profile."
                : $"Remove this character from the profile.\nHold {_configuration.UISettings.DeleteTemplateModifier} to remove.";

            if (ImGuiUtil.DrawDisabledButton(FontAwesomeIcon.Trash.ToIconString(), new Vector2(ImGui.GetFrameHeight()), tt, !keyValid, true))
                _endAction = () => _manager.DeleteCharacter(_selector.Selected!, character);
            ImGui.TableNextColumn();
            ImGui.AlignTextToFramePadding();
            ImGui.TextUnformatted(!_selector.IncognitoMode ? $"{character.ToNameWithoutOwnerName()}{character.TypeToString()}" : "Incognito");

            var profiles = _manager.GetEnabledProfilesByActor(character).ToList();
            if (profiles.Count > 1)
            {
                //todo: make helper
                ImGui.SameLine();
                if (profiles.Any(x => x.IsTemporary))
                {
                    ImGui.PushStyleColor(ImGuiCol.Text, Constants.Colors.Error);
                    ImGuiUtil.PrintIcon(FontAwesomeIcon.Lock);
                }
                else if (profiles[0] != _selector.Selected!)
                {
                    ImGui.PushStyleColor(ImGuiCol.Text, Constants.Colors.Warning);
                    ImGuiUtil.PrintIcon(FontAwesomeIcon.ExclamationTriangle);
                }
                else
                {
                    ImGui.PushStyleColor(ImGuiCol.Text, Constants.Colors.Info);
                    ImGuiUtil.PrintIcon(FontAwesomeIcon.Star);
                }

                ImGui.PopStyleColor();

                if (profiles.Any(x => x.IsTemporary))
                    ImGuiUtil.HoverTooltip("This character is being affected by temporary profile set by external plugin. This profile will not be applied!");
                else
                    ImGuiUtil.HoverTooltip(profiles[0] != _selector.Selected! ? "Several profiles are trying to affect this character. This profile will not be applied!" :
                        "Several profiles are trying to affect this character. This profile is being applied.");
            }
        }

        _endAction?.Invoke();
        _endAction = null;
    }

    private void DrawTemplateArea()
    {
        using var table = ImRaii.Table("TemplateTable", 6, ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollX | ImGuiTableFlags.ScrollY);
        if (!table)
            return;

        ImGui.TableSetupColumn("##del", ImGuiTableColumnFlags.WidthFixed, ImGui.GetFrameHeight());
        ImGui.TableSetupColumn("##Index", ImGuiTableColumnFlags.WidthFixed, 30 * ImGuiHelpers.GlobalScale);
        ImGui.TableSetupColumn("##Enabled", ImGuiTableColumnFlags.WidthFixed, 30 * ImGuiHelpers.GlobalScale);

        ImGui.TableSetupColumn("Template", ImGuiTableColumnFlags.WidthFixed, 220 * ImGuiHelpers.GlobalScale);
        ImGui.TableSetupColumn("Weight", ImGuiTableColumnFlags.WidthFixed, 120 * ImGuiHelpers.GlobalScale);

        ImGui.TableSetupColumn("##editbtn", ImGuiTableColumnFlags.WidthFixed, 120 * ImGuiHelpers.GlobalScale);

        ImGui.TableHeadersRow();

        //warn: .ToList() might be performance critical at some point
        //the copying via ToList is done because manipulations with .Templates list result in "Collection was modified" exception here
        foreach (var (template, idx) in _selector.Selected!.Templates.WithIndex().ToList())
        {
            using var id = ImRaii.PushId(idx);
            ImGui.TableNextColumn();
            var keyValid = _configuration.UISettings.DeleteTemplateModifier.IsActive();
            var tt = keyValid
                ? "Remove this template from the profile."
                : $"Remove this template from the profile.\nHold {_configuration.UISettings.DeleteTemplateModifier} to remove.";

            if (ImGuiUtil.DrawDisabledButton(FontAwesomeIcon.Trash.ToIconString(), new Vector2(ImGui.GetFrameHeight()), tt, !keyValid, true))
                _endAction = () => _manager.DeleteTemplate(_selector.Selected!, idx);
            ImGui.TableNextColumn();
            ImGui.Selectable($"#{idx + 1:D2}");
            DrawDragDrop(_selector.Selected!, idx);

            ImGui.TableNextColumn();
            var enabled = !_selector.Selected!.DisabledTemplates.Contains(template.UniqueId);
            if (ImGui.Checkbox("##EnableCheckbox", ref enabled))
                _manager.ToggleTemplate(_selector.Selected!, idx);
            ImGuiUtil.HoverTooltip("Whether this template is applied to the profile.");

            ImGui.TableNextColumn();

            _templateCombo.Draw(_selector.Selected!, template, idx);

            DrawDragDrop(_selector.Selected!, idx);

            ImGui.TableNextColumn();
            var weightPercent = _selector.Selected!.GetTemplateWeight(template.UniqueId) * 100f;
            ImGui.SetNextItemWidth(-1);
            if (ImGui.SliderFloat("##Weight", ref weightPercent, 0f, 100f, "%.0f%%"))
                _endAction = () => _manager.SetTemplateWeight(_selector.Selected!, idx, weightPercent / 100f);
            ImGuiUtil.HoverTooltip("Blend weight for this template when multiple templates affect the same bone.");

            ImGui.TableNextColumn();

            var disabledCondition = _templateEditorManager.IsEditorActive || template.IsWriteProtected;

            if (ImGuiUtil.DrawDisabledButton(FontAwesomeIcon.Edit.ToIconString(), new Vector2(ImGui.GetFrameHeight()), "Open this template in the template editor.", disabledCondition, true))
                _templateEditorEvent.Invoke(TemplateEditorEvent.Type.EditorEnableRequested, template);

            if (disabledCondition)
            {
                //todo: make helper
                ImGui.SameLine();
                ImGui.PushStyleColor(ImGuiCol.Text, Constants.Colors.Warning);
                ImGuiUtil.PrintIcon(FontAwesomeIcon.ExclamationTriangle);
                ImGui.PopStyleColor();
                ImGuiUtil.HoverTooltip("This template cannot be edited because it is either write protected or you are already editing one of the templates.");
            }
        }

        ImGui.TableNextRow();
        ImGui.TableSetColumnIndex(2);
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted("New");
        ImGui.TableSetColumnIndex(3);
        _templateCombo.Draw(_selector.Selected!, null, -1);

        _endAction?.Invoke();
        _endAction = null;
    }

    private void DrawDragDrop(Profile profile, int index)
    {
        const string dragDropLabel = "TemplateDragDrop";
        using (var target = ImRaii.DragDropTarget())
        {
            if (target.Success && ImGuiUtil.IsDropping(dragDropLabel))
            {
                if (_dragIndex >= 0)
                {
                    var idx = _dragIndex;
                    _endAction = () => _manager.MoveTemplate(profile, idx, index);
                }

                _dragIndex = -1;
            }
        }

        using (var source = ImRaii.DragDropSource())
        {
            if (source)
            {
                ImGui.TextUnformatted($"Moving template #{index + 1:D2}...");
                if (ImGui.SetDragDropPayload(dragDropLabel, null, 0))
                {
                    _dragIndex = index;
                }
            }
        }
    }

    private void UpdateIdentifiers()
    {

    }
}
