// Copyright (c) Customize+.
// Licensed under the MIT license.

using System.Collections.Generic;
using System.Numerics;
using CustomizePlus.Core.Data;
using CustomizePlus.Core.Helpers;
using CustomizePlus.Core.Services;
using CustomizePlus.Profiles.Data;
using Newtonsoft.Json.Linq;
using Xunit;

namespace CustomizePlus.Tests;

public class PortableResolvedTemplateBuilderTests
{
    [Fact]
    public void StaticResolvedSnapshotBuildsAnIsolatedNormalTemplate()
    {
        var source = CreateProfile("Muscular Profile");
        var staticTransforms = new Dictionary<string, BoneTransform>
        {
            ["j_sebo_b"] = new()
            {
                Scaling = new Vector3(1.18f, 1.09f, 1.12f),
                LockState = BoneLockState.Locked,
                PinX = true,
            },
            ["j_kami_b"] = new()
            {
                Translation = new Vector3(0.02f, 0f, 0f),
                Scaling = new Vector3(1.04f, 1.02f, 1.03f),
                PinY = true,
            },
        };

        var result = PortableResolvedTemplateBuilder.BuildFromStaticTransforms(source, staticTransforms);

        Assert.True(result.Success);
        var baked = Assert.IsType<Templates.Data.Template>(result.Template);
        Assert.Equal("Muscular Profile [Resolved]", baked.Name.Text);
        Assert.False(baked.IsWriteProtected);
        Assert.Equal(staticTransforms.Count, baked.Bones.Count);
        Assert.Equal(BoneLockState.Locked, baked.Bones["j_sebo_b"].LockState);
        Assert.True(baked.Bones["j_sebo_b"].PinX);
        Assert.False(baked.Bones["j_sebo_b"].PinY);
        Assert.True(baked.Bones["j_kami_b"].PinY);
        Assert.NotSame(staticTransforms["j_sebo_b"], baked.Bones["j_sebo_b"]);

        baked.Bones["j_sebo_b"].Scaling = Vector3.One;
        Assert.Equal(1.18f, staticTransforms["j_sebo_b"].Scaling.X, 3);
    }

    [Fact]
    public void BakedTemplateRoundTripsThroughExistingClipboardFormat()
    {
        var source = CreateProfile("Round Trip");
        var staticTransforms = new Dictionary<string, BoneTransform>
        {
            ["j_sebo_b"] = new()
            {
                Scaling = new Vector3(1.11f, 1.07f, 1.05f),
                LockState = BoneLockState.Locked,
            },
            ["j_ude_a_l"] = new()
            {
                Scaling = new Vector3(1.03f, 1.02f, 1.01f),
                PinZ = true,
            },
        };
        var baked = Assert.IsType<Templates.Data.Template>(
            PortableResolvedTemplateBuilder.BuildFromStaticTransforms(source, staticTransforms).Template);

        var payload = Base64Helper.ExportTemplateToBase64(baked);
        Assert.NotEmpty(payload);
        Assert.Equal(Templates.Data.Template.Version, Base64Helper.ImportFromBase64(payload, out var json));
        var imported = Templates.Data.Template.Load(JObject.Parse(json));

        var disposableProfile = CreateProfile("Disposable");
        disposableProfile.Templates.Add(imported);
        var roundTrip = ProfileTransformResolver.Resolve(disposableProfile).EffectiveTransforms;

        Assert.Equal(staticTransforms.Keys.OrderBy(static key => key), roundTrip.Keys.OrderBy(static key => key));
        Assert.Equal(staticTransforms["j_sebo_b"].Scaling, roundTrip["j_sebo_b"].Scaling);
        Assert.Equal(staticTransforms["j_ude_a_l"].Scaling, roundTrip["j_ude_a_l"].Scaling);
        Assert.Equal(BoneLockState.Locked, roundTrip["j_sebo_b"].LockState);
        Assert.True(roundTrip["j_ude_a_l"].PinZ);
    }

    [Fact]
    public void EmptyStaticSnapshotIsRefusedWithoutMutatingTheSourceProfile()
    {
        var source = CreateProfile("Unchanged Source");
        source.Templates.Add(new Templates.Data.Template());
        var sourceTemplateCount = source.Templates.Count;

        var result = PortableResolvedTemplateBuilder.BuildFromStaticTransforms(source, new Dictionary<string, BoneTransform>());

        Assert.False(result.Success);
        Assert.Null(result.Template);
        Assert.Contains("no portable bone transforms", result.Reason);
        Assert.Equal(sourceTemplateCount, source.Templates.Count);
    }

    private static Profile CreateProfile(string name)
        => new()
        {
            Name = name,
            Enabled = true,
            UniqueId = Guid.NewGuid(),
            Templates = new List<Templates.Data.Template>(),
        };
}
