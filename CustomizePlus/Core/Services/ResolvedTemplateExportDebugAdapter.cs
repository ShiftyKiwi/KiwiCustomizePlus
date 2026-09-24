// Copyright (c) Customize+.
// Licensed under the MIT license.

#if DEBUG
using System;
using System.Linq;
using CustomizePlus.Armatures.Data;
using CustomizePlus.Armatures.Services;
using CustomizePlus.Core.Data;
using CustomizePlus.Core.Helpers;
using CustomizePlus.Profiles;
using CustomizePlus.Profiles.Data;
using CustomizePlus.Profiles.Enums;
using CustomizePlus.Templates;
using CustomizePlus.Templates.Data;
using CustomizePlus.UI.Windows.MainWindow.Tabs.Profiles;
using Newtonsoft.Json.Linq;

namespace CustomizePlus.Core.Services;

/// <summary>
/// Reflection-friendly development surface for the resolved-template export.
/// This class deliberately has no Conduit dependency: external development
/// tools may call it, but every export still enters the production service.
/// </summary>
public sealed class ResolvedTemplateExportDebugAdapter : IDisposable
{
    private readonly ProfileFileSystemSelector _selector;
    private readonly ResolvedTemplateExportService _exportService;
    private readonly ResolvedTemplateClipboard _clipboard;
    private readonly ArmatureManager _armatureManager;
    private ResolvedTemplateExportResult _lastExport = ResolvedTemplateExportResult.Failed(Guid.Empty, string.Empty, "No resolved-template export has been requested.");

    public static ResolvedTemplateExportDebugAdapter? Instance { get; private set; }

    public ResolvedTemplateExportDebugAdapter(
        ProfileFileSystemSelector selector,
        ResolvedTemplateExportService exportService,
        ResolvedTemplateClipboard clipboard,
        ArmatureManager armatureManager)
    {
        _selector = selector;
        _exportService = exportService;
        _clipboard = clipboard;
        _armatureManager = armatureManager;
        Instance = this;
    }

    public string SharedActionId
        => ResolvedTemplateExportService.ActionId;

    public ResolvedTemplateExportDebugProfile GetSelectedProfile()
    {
        var selected = _selector.Selected;
        if (selected == null)
            return ResolvedTemplateExportDebugProfile.Unavailable("No Profile-tab selection is available.");

        var armature = _armatureManager.Armatures.Values.SingleOrDefault(candidate => candidate.Profile.UniqueId == selected.UniqueId);
        return new ResolvedTemplateExportDebugProfile(
            true,
            string.Empty,
            selected.UniqueId,
            selected.Name.Text,
            selected.Templates.Count,
            armature?.ActorIdentifier.ToString() ?? string.Empty,
            armature?.ActiveAdvancedBodyScalingSettings?.Enabled == true,
            armature?.ActiveAdvancedBodyScalingSettings?.HierarchicalShapingEnabled == true,
            armature?.ActiveAdvancedBodyScalingSettings?.HierarchicalAuthoredRelaxationEnabled == true,
            armature?.IsBuilt == true && armature.IsSkeletonBindingCurrent && !armature.IsAwaitingAppearanceContextRebind);
    }

    public ResolvedTemplateExportResult InvokeResolvedTemplateCopy()
    {
        var selected = _selector.Selected;
        _lastExport = selected == null
            ? ResolvedTemplateExportResult.Failed(Guid.Empty, string.Empty, "Select a profile in the Profiles tab before exporting its resolved shape.")
            : _exportService.CopyResolvedTemplateToClipboard(selected.UniqueId);
        return _lastExport;
    }

    public ResolvedTemplateExportResult InvokeMissingProfileRefusal()
    {
        _lastExport = _exportService.CopyResolvedTemplateToClipboard(Guid.Empty);
        return _lastExport;
    }

    public ResolvedTemplateExportResult GetLastExportResult()
        => _lastExport;

    public string GetLastSerializedPayloadHash()
        => _lastExport.SerializedSha256;

    public int GetLastSerializedPayloadLength()
        => _lastExport.SerializedLength;

    /// <summary>Returns only the current clipboard payload digest for refusal-path verification.</summary>
    public ResolvedTemplateExportDebugClipboardPayload GetClipboardPayload()
    {
        var clipboardText = _clipboard.GetText() ?? string.Empty;
        return new ResolvedTemplateExportDebugClipboardPayload(
            clipboardText.Length,
            ResolvedTemplateExportService.ComputeSha256(clipboardText));
    }

    public ResolvedTemplateExportDebugClipboardCheck CompareLastExportToClipboard()
    {
        if (!_lastExport.Success)
            return ResolvedTemplateExportDebugClipboardCheck.Unavailable("No successful export is available for clipboard comparison.");

        var clipboardText = _clipboard.GetText() ?? string.Empty;
        var observedHash = ResolvedTemplateExportService.ComputeSha256(clipboardText);
        return new ResolvedTemplateExportDebugClipboardCheck(
            true,
            string.Empty,
            _lastExport.SerializedSha256,
            observedHash,
            _lastExport.SerializedLength,
            clipboardText.Length,
            string.Equals(_lastExport.SerializedSha256, observedHash, StringComparison.Ordinal));
    }

    public ResolvedTemplateExportDebugParsedTemplate ParseLastExport()
    {
        var clipboard = CompareLastExportToClipboard();
        if (!clipboard.Success || !clipboard.MatchesExpectedPayload)
            return ResolvedTemplateExportDebugParsedTemplate.Unavailable(clipboard.FailureReason.Length > 0
                ? clipboard.FailureReason
                : "The clipboard no longer contains the payload produced by the last resolved-template export.");

        try
        {
            var version = Base64Helper.ImportFromBase64(_clipboard.GetText(), out var json);
            if (version != Template.Version)
                return ResolvedTemplateExportDebugParsedTemplate.Unavailable($"Clipboard template version {version} does not match the supported version {Template.Version}.");

            var template = Template.Load(JObject.Parse(json));
            return new ResolvedTemplateExportDebugParsedTemplate(
                true,
                string.Empty,
                template.Bones.Count,
                template.Bones.Values.Count(static transform => transform.LockState != BoneLockState.Unlocked),
                template.Bones.Values.Count(static transform => transform.HasPinnedScaleAxes()),
                ResolvedTemplateExportService.ComputeTransformSetSha256(template.Bones));
        }
        catch (Exception ex)
        {
            return ResolvedTemplateExportDebugParsedTemplate.Unavailable($"Could not parse the last resolved-template clipboard payload: {ex.Message}");
        }
    }

    public ResolvedTemplateExportDebugReceiverComparison CompareLastExportWithDisposableReceiver()
    {
        var parsed = ParseLastExport();
        if (!parsed.Success)
            return ResolvedTemplateExportDebugReceiverComparison.Unavailable(parsed.FailureReason);

        var armatures = _armatureManager.Armatures.Values
            .Where(candidate => candidate.Profile.UniqueId == _lastExport.ProfileId)
            .ToArray();
        if (armatures.Length != 1)
            return ResolvedTemplateExportDebugReceiverComparison.Unavailable(
                $"Expected exactly one armature for the exported profile, found {armatures.Length}.");

        var armature = armatures[0];
        if (!armature.IsBuilt || !armature.IsSkeletonBindingCurrent || armature.IsAwaitingAppearanceContextRebind)
            return ResolvedTemplateExportDebugReceiverComparison.Unavailable("The source armature is no longer in a stable validated state.");

        try
        {
            var version = Base64Helper.ImportFromBase64(_clipboard.GetText(), out var json);
            if (version != Template.Version)
                return ResolvedTemplateExportDebugReceiverComparison.Unavailable("The clipboard payload could not be imported using the normal template format.");

            var imported = Template.Load(JObject.Parse(json));
            var receiver = new Profile
            {
                Name = "[Debug] Resolved Template Receiver",
                Enabled = true,
                ProfileType = ProfileType.Temporary,
                AdvancedBodyScalingOverrides = new AdvancedBodyScalingProfileSettings
                {
                    UseProfileOverrides = true,
                    Overrides = new AdvancedBodyScalingOverrides { Enabled = false },
                },
            };
            receiver.Templates.Add(imported);

            var sourceStatic = armature.ResolvedBoneTransforms;
            var receiverTransforms = ProfileTransformResolver.Resolve(receiver, armature.GetCapabilityManifestSnapshot()).EffectiveTransforms;
            var sourceDigest = ResolvedTemplateExportService.ComputeTransformSetSha256(sourceStatic);
            var receiverDigest = ResolvedTemplateExportService.ComputeTransformSetSha256(receiverTransforms);
            return new ResolvedTemplateExportDebugReceiverComparison(
                true,
                string.Empty,
                receiver.AdvancedBodyScalingOverrides.UseProfileOverrides && receiver.AdvancedBodyScalingOverrides.Overrides.Enabled == false,
                sourceStatic.Count,
                receiverTransforms.Count,
                sourceDigest,
                receiverDigest,
                sourceStatic.Count == receiverTransforms.Count && string.Equals(sourceDigest, receiverDigest, StringComparison.Ordinal));
        }
        catch (Exception ex)
        {
            return ResolvedTemplateExportDebugReceiverComparison.Unavailable($"Could not compare the disposable receiver: {ex.Message}");
        }
    }

    public void ClearLastExport()
        => _lastExport = ResolvedTemplateExportResult.Failed(Guid.Empty, string.Empty, "No resolved-template export has been requested.");

    public void Dispose()
    {
        if (ReferenceEquals(Instance, this))
            Instance = null;
    }
}

public sealed record ResolvedTemplateExportDebugProfile(
    bool Success,
    string FailureReason,
    Guid ProfileId,
    string ProfileName,
    int TemplateCount,
    string ActorIdentifier,
    bool AdvancedBodyScalingEnabled,
    bool HierarchicalShapingEnabled,
    bool HierarchicalAuthoredRelaxationEnabled,
    bool BindingStable)
{
    public static ResolvedTemplateExportDebugProfile Unavailable(string reason)
        => new(false, reason, Guid.Empty, string.Empty, 0, string.Empty, false, false, false, false);
}

public sealed record ResolvedTemplateExportDebugClipboardCheck(
    bool Success,
    string FailureReason,
    string ExpectedSha256,
    string ObservedSha256,
    int ExpectedLength,
    int ObservedLength,
    bool MatchesExpectedPayload)
{
    public static ResolvedTemplateExportDebugClipboardCheck Unavailable(string reason)
        => new(false, reason, string.Empty, string.Empty, 0, 0, false);
}

public sealed record ResolvedTemplateExportDebugClipboardPayload(
    int Length,
    string Sha256);

public sealed record ResolvedTemplateExportDebugParsedTemplate(
    bool Success,
    string FailureReason,
    int TransformCount,
    int LockedRowCount,
    int PinnedRowCount,
    string StaticTransformSha256)
{
    public static ResolvedTemplateExportDebugParsedTemplate Unavailable(string reason)
        => new(false, reason, 0, 0, 0, string.Empty);
}

public sealed record ResolvedTemplateExportDebugReceiverComparison(
    bool Success,
    string FailureReason,
    bool AdvancedBodyScalingExplicitlyOff,
    int SourceTransformCount,
    int ReceiverTransformCount,
    string SourceStaticTransformSha256,
    string ReceiverTransformSha256,
    bool IsExact)
{
    public static ResolvedTemplateExportDebugReceiverComparison Unavailable(string reason)
        => new(false, reason, false, 0, 0, string.Empty, string.Empty, false);
}
#endif
