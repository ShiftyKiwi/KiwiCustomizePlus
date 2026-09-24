// Copyright (c) Customize+.
// Licensed under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using CustomizePlus.Armatures.Data;
using CustomizePlus.Armatures.Services;
using CustomizePlus.Core.Data;
using CustomizePlus.Core.Helpers;
using CustomizePlus.Profiles;
using CustomizePlus.Profiles.Data;
using Dalamud.Bindings.ImGui;
using OtterGui.Text;

namespace CustomizePlus.Core.Services;

/// <summary>
/// The sole production action for copying a profile's stable, resolved static
/// shape as a normal template. UI and development tooling must call this class
/// instead of rebuilding or serializing an export independently.
/// </summary>
public sealed class ResolvedTemplateExportService
{
    public const string ActionId = "resolved-static-template-export";

    private readonly ProfileManager? _profileManager;
    private readonly ArmatureManager? _armatureManager;
    private readonly PortableResolvedTemplateBuilder? _builder;
    private readonly Action<string> _writeClipboard;

    public ResolvedTemplateExportService(
        ProfileManager profileManager,
        ArmatureManager armatureManager,
        PortableResolvedTemplateBuilder builder,
        ResolvedTemplateClipboard clipboard)
    {
        _profileManager = profileManager;
        _armatureManager = armatureManager;
        _builder = builder;
        _writeClipboard = clipboard.SetText;
    }

    // The test constructor exercises the same core action with a recording clipboard writer.
    internal ResolvedTemplateExportService(Action<string> writeClipboard)
        => _writeClipboard = writeClipboard;

    public ResolvedTemplateExportResult CopyResolvedTemplateToClipboard(Guid profileId)
    {
        var profile = _profileManager?.Profiles.FirstOrDefault(candidate => candidate.UniqueId == profileId);
        if (profile == null)
            return ResolvedTemplateExportResult.Failed(profileId, string.Empty, "The requested profile is no longer available for resolved-template export.");

        if (_armatureManager == null || _builder == null)
            return ResolvedTemplateExportResult.Failed(profileId, profile.Name.Text, "Resolved-template export is not available in this runtime.");

        return CopyResolvedTemplateToClipboard(profile, _armatureManager.Armatures.Values, _builder.TryBuild);
    }

    internal ResolvedTemplateExportResult CopyResolvedTemplateToClipboard(
        Profile profile,
        IEnumerable<Armature> armatures,
        Func<Profile, IEnumerable<Armature>, PortableResolvedTemplateBuildResult>? buildOverride = null)
    {
        var armatureSnapshot = armatures.ToArray();
        var build = buildOverride?.Invoke(profile, armatureSnapshot)
            ?? _builder?.TryBuild(profile, armatureSnapshot)
            ?? PortableResolvedTemplateBuildResult.Unavailable("Resolved-template export is not available in this runtime.");
        if (!build.Success || build.Template == null)
            return ResolvedTemplateExportResult.Failed(profile.UniqueId, profile.Name.Text, build.Reason);

        var payload = Base64Helper.ExportTemplateToBase64(build.Template);
        if (string.IsNullOrEmpty(payload))
            return ResolvedTemplateExportResult.Failed(profile.UniqueId, profile.Name.Text, "Could not serialize the resolved shape as a template.");

        try
        {
            _writeClipboard(payload);
        }
        catch (Exception ex)
        {
            return ResolvedTemplateExportResult.Failed(profile.UniqueId, profile.Name.Text,
                $"Could not write the resolved template to the clipboard: {ex.Message}");
        }

        var matchingArmature = armatureSnapshot.FirstOrDefault(armature => armature.Profile.UniqueId == profile.UniqueId);
        var transforms = build.Template.Bones;
        return ResolvedTemplateExportResult.Succeeded(
            profile.UniqueId,
            profile.Name.Text,
            matchingArmature?.ActorIdentifier.ToString() ?? string.Empty,
            matchingArmature?.SkeletonRevision ?? 0,
            matchingArmature?.DeformationRevision ?? 0,
            transforms.Count,
            transforms.Values.Count(static transform => transform.LockState != BoneLockState.Unlocked),
            transforms.Values.Count(static transform => transform.HasPinnedScaleAxes()),
            payload.Length,
            ComputeSha256(payload),
            ComputeTransformSetSha256(transforms));
    }

    internal static string ComputeSha256(string value)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    internal static string ComputeTransformSetSha256(IReadOnlyDictionary<string, BoneTransform> transforms)
    {
        var builder = new StringBuilder();
        foreach (var (boneName, transform) in transforms.OrderBy(static pair => pair.Key, StringComparer.Ordinal))
        {
            builder.Append(boneName).Append('|');
            AppendVectorBits(builder, transform.Translation);
            AppendVectorBits(builder, transform.Rotation);
            AppendVectorBits(builder, transform.Scaling);
            AppendVectorBits(builder, transform.ChildScaling);
            builder.Append(BitConverter.SingleToInt32Bits(transform.PropagationFalloff)).Append('|')
                .Append((int)transform.LockState).Append('|')
                .Append(transform.PinX).Append('|').Append(transform.PinY).Append('|').Append(transform.PinZ).Append('|')
                .Append(transform.PropagateTranslation).Append('|').Append(transform.PropagateRotation).Append('|').Append(transform.PropagateScale).Append('|')
                .Append(transform.ChildScalingIndependent).Append('\n');
        }

        return ComputeSha256(builder.ToString());
    }

    private static void AppendVectorBits(StringBuilder builder, System.Numerics.Vector3 vector)
        => builder.Append(BitConverter.SingleToInt32Bits(vector.X)).Append('|')
            .Append(BitConverter.SingleToInt32Bits(vector.Y)).Append('|')
            .Append(BitConverter.SingleToInt32Bits(vector.Z)).Append('|');
}

/// <summary>Production clipboard boundary with a replaceable in-memory test seam.</summary>
public class ResolvedTemplateClipboard
{
    public virtual void SetText(string text)
        => ImUtf8.SetClipboardText(text);

    public virtual string GetText()
        => ImGui.GetClipboardText();
}

public sealed record ResolvedTemplateExportResult(
    bool Success,
    string FailureReason,
    Guid ProfileId,
    string ProfileName,
    string ActorIdentifier,
    long SkeletonRevision,
    long DeformationRevision,
    int TransformCount,
    int LockedRowCount,
    int PinnedRowCount,
    int SerializedLength,
    string SerializedSha256,
    string StaticTransformSha256)
{
    public static ResolvedTemplateExportResult Failed(Guid profileId, string profileName, string reason)
        => new(false, reason, profileId, profileName, string.Empty, 0, 0, 0, 0, 0, 0, string.Empty, string.Empty);

    public static ResolvedTemplateExportResult Succeeded(
        Guid profileId,
        string profileName,
        string actorIdentifier,
        long skeletonRevision,
        long deformationRevision,
        int transformCount,
        int lockedRowCount,
        int pinnedRowCount,
        int serializedLength,
        string serializedSha256,
        string staticTransformSha256)
        => new(true, string.Empty, profileId, profileName, actorIdentifier, skeletonRevision, deformationRevision,
            transformCount, lockedRowCount, pinnedRowCount, serializedLength, serializedSha256, staticTransformSha256);
}
