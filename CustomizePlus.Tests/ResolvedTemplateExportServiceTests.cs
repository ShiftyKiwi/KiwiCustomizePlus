// Copyright (c) Customize+.
// Licensed under the MIT license.

using System;
using System.Collections.Generic;
using System.Numerics;
using CustomizePlus.Armatures.Data;
using CustomizePlus.Core.Data;
using CustomizePlus.Core.Services;
using CustomizePlus.Profiles.Data;
using Xunit;

namespace CustomizePlus.Tests;

public class ResolvedTemplateExportServiceTests
{
    [Fact]
    public void SharedActionBuildsOnceAndWritesItsExactPayload()
    {
        var source = CreateProfile("Shared Action");
        var transforms = CreateTransforms();
        var builderCalls = 0;
        var clipboardPayload = string.Empty;
        var service = new ResolvedTemplateExportService(text => clipboardPayload = text);

        var result = service.CopyResolvedTemplateToClipboard(source, Array.Empty<Armature>(), (_, _) =>
        {
            builderCalls++;
            return PortableResolvedTemplateBuilder.BuildFromStaticTransforms(source, transforms);
        });

        Assert.True(result.Success);
        Assert.Equal(1, builderCalls);
        Assert.Equal(result.SerializedLength, clipboardPayload.Length);
        Assert.Equal(result.SerializedSha256, ResolvedTemplateExportService.ComputeSha256(clipboardPayload));
        Assert.Equal(2, result.TransformCount);
        Assert.Equal(1, result.LockedRowCount);
        Assert.Equal(1, result.PinnedRowCount);
    }

    [Fact]
    public void RefusalNeverReplacesClipboardContents()
    {
        var source = CreateProfile("Refusal");
        var builderCalls = 0;
        var clipboardPayload = "sentinel clipboard payload";
        var service = new ResolvedTemplateExportService(text => clipboardPayload = text);

        var result = service.CopyResolvedTemplateToClipboard(source, Array.Empty<Armature>(), (_, _) =>
        {
            builderCalls++;
            return PortableResolvedTemplateBuildResult.Unavailable("Stable armature required.");
        });

        Assert.False(result.Success);
        Assert.Equal("Stable armature required.", result.FailureReason);
        Assert.Equal(1, builderCalls);
        Assert.Equal("sentinel clipboard payload", clipboardPayload);
    }

    [Fact]
    public void RepeatedExportsKeepTheStaticShapeDigestStable()
    {
        var source = CreateProfile("Stable Digest");
        var transforms = CreateTransforms();
        var payloads = new List<string>();
        var service = new ResolvedTemplateExportService(payloads.Add);

        var first = service.CopyResolvedTemplateToClipboard(source, Array.Empty<Armature>(),
            (_, _) => PortableResolvedTemplateBuilder.BuildFromStaticTransforms(source, transforms));
        var second = service.CopyResolvedTemplateToClipboard(source, Array.Empty<Armature>(),
            (_, _) => PortableResolvedTemplateBuilder.BuildFromStaticTransforms(source, transforms));

        Assert.True(first.Success);
        Assert.True(second.Success);
        Assert.Equal(first.StaticTransformSha256, second.StaticTransformSha256);
        Assert.Equal(first.SerializedSha256, ResolvedTemplateExportService.ComputeSha256(payloads[0]));
        Assert.Equal(second.SerializedSha256, ResolvedTemplateExportService.ComputeSha256(payloads[1]));
        Assert.Equal(2, payloads.Count);
    }

    private static Profile CreateProfile(string name)
        => new()
        {
            Name = name,
            Enabled = true,
            UniqueId = Guid.NewGuid(),
        };

    private static Dictionary<string, BoneTransform> CreateTransforms()
        => new()
        {
            ["j_sebo_b"] = new()
            {
                Scaling = new Vector3(1.18f, 1.09f, 1.12f),
                LockState = BoneLockState.Locked,
            },
            ["j_ude_a_l"] = new()
            {
                Scaling = new Vector3(1.03f, 1.02f, 1.01f),
                PinY = true,
            },
        };
}
