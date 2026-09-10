using System.Numerics;
using CustomizePlus.Api.Data;
using CustomizePlus.Armatures.Data;
using CustomizePlus.Core.Data;
using CustomizePlus.Core.Helpers;
using CustomizePlus.Core.Services;
using CustomizePlus.Templates.Data;
using CustomizePlus.UI.Windows.MainWindow.Tabs.Templates;
using Newtonsoft.Json.Linq;
using Xunit;

namespace CustomizePlus.Tests;

public class TemplateLockPinIntegrityTests
{
    public static IEnumerable<object[]> ProtectionStates()
    {
        foreach (var lockState in Enum.GetValues<BoneLockState>())
        {
            for (var mask = 0; mask < 8; ++mask)
            {
                yield return new object[]
                {
                    lockState,
                    (mask & 1) != 0,
                    (mask & 2) != 0,
                    (mask & 4) != 0,
                };
            }
        }
    }

    [Theory]
    [MemberData(nameof(ProtectionStates))]
    public void TemplateJsonRoundTrip_PreservesEveryProtectionState(
        BoneLockState lockState,
        bool pinX,
        bool pinY,
        bool pinZ)
    {
        var original = new Template
        {
            Bones = new Dictionary<string, BoneTransform>(StringComparer.Ordinal)
            {
                ["j_kosi"] = Transform(lockState, pinX, pinY, pinZ),
            },
        };

        var reloaded = Template.Load(original.JsonSerialize());

        AssertProtectionEqual(original.Bones["j_kosi"], reloaded.Bones["j_kosi"]);
    }

    [Fact]
    public void ProtectionTransitions_CommitAndReloadExactly()
    {
        var working = new BoneTransform();
        var expectedStates = new[]
        {
            new BoneTransform { PinX = true },
            new BoneTransform(),
            new BoneTransform { LockState = BoneLockState.Locked },
            new BoneTransform(),
            new BoneTransform { LockState = BoneLockState.Locked, PinX = true },
            new BoneTransform { PinX = true },
            new BoneTransform { LockState = BoneLockState.Locked, PinX = true, PinY = true, PinZ = true },
            new BoneTransform { PinX = true, PinY = true, PinZ = true },
            new BoneTransform { PinY = true },
            new BoneTransform { PinZ = true },
            new BoneTransform { PinZ = true },
            new BoneTransform(),
        };

        foreach (var expected in expectedStates)
        {
            working.UpdateToMatch(expected);
            var template = new Template
            {
                Bones = new Dictionary<string, BoneTransform>(StringComparer.Ordinal)
                {
                    ["j_kosi"] = working.DeepCopy(),
                },
            };

            var reloaded = Template.Load(template.JsonSerialize());
            AssertProtectionEqual(expected, reloaded.Bones["j_kosi"]);
        }
    }

    [Fact]
    public void DuplicateTemplateAndUnrelatedRows_OwnIndependentProtectionState()
    {
        var original = new Template
        {
            Bones = new Dictionary<string, BoneTransform>(StringComparer.Ordinal)
            {
                ["j_kosi"] = Transform(BoneLockState.Unlocked, true, false, false),
                ["j_hara"] = Transform(BoneLockState.Unlocked, false, false, false),
                ["j_sebo_b"] = Transform(BoneLockState.Unlocked, false, false, false),
            },
        };
        var duplicate = new Template(original);

        Assert.False(ReferenceEquals(original.Bones["j_kosi"], duplicate.Bones["j_kosi"]));
        duplicate.Bones["j_kosi"].PinX = false;
        duplicate.Bones["j_kosi"].LockState = BoneLockState.Locked;
        duplicate.Bones["j_hara"].PinZ = true;

        AssertProtectionEqual(Transform(BoneLockState.Unlocked, true, false, false), original.Bones["j_kosi"]);
        AssertProtectionEqual(Transform(BoneLockState.Unlocked, false, false, false), original.Bones["j_hara"]);
        AssertProtectionEqual(Transform(BoneLockState.Unlocked, false, false, false), original.Bones["j_sebo_b"]);

        var reloaded = Template.Load(duplicate.JsonSerialize());
        AssertProtectionEqual(Transform(BoneLockState.Locked, false, false, false), reloaded.Bones["j_kosi"]);
        AssertProtectionEqual(Transform(BoneLockState.Unlocked, false, false, true), reloaded.Bones["j_hara"]);
        AssertProtectionEqual(Transform(BoneLockState.Unlocked, false, false, false), reloaded.Bones["j_sebo_b"]);
    }

    [Fact]
    public void GroupClipboard_RoundTripsLockOnlyAndAxisPins()
    {
        var lockOnly = Transform(BoneLockState.Locked, false, false, false);
        var pins = Transform(BoneLockState.Unlocked, true, false, true);

        var payload = Base64Helper.ExportEditedBonesToBase64(new[]
        {
            ("j_kosi", lockOnly),
            ("j_hara", pins),
        });
        var imported = Base64Helper.ImportEditedBonesFromBase64(payload, out var error);

        Assert.True(string.IsNullOrEmpty(error));
        var data = Assert.IsType<List<BoneTransformData>>(imported);
        Assert.Equal(2, data.Count);
        Assert.Equal(BoneLockState.Locked, data.Single(static bone => bone.BoneCodeName == "j_kosi").LockState);
        var pinned = data.Single(static bone => bone.BoneCodeName == "j_hara");
        Assert.True(pinned.PinX);
        Assert.False(pinned.PinY);
        Assert.True(pinned.PinZ);
    }

    [Fact]
    public void GroupClipboardSelection_IncludesLockOnlyRowsAndExcludesDefaults()
    {
        Assert.True(BoneEditorPanel.ShouldIncludeGroupExport(Transform(BoneLockState.Locked, false, false, false)));
        Assert.True(BoneEditorPanel.ShouldIncludeGroupExport(Transform(BoneLockState.Unlocked, true, false, false)));
        Assert.False(BoneEditorPanel.ShouldIncludeGroupExport(new BoneTransform()));
        Assert.False(BoneEditorPanel.ShouldIncludeGroupExport(null));
    }

    [Theory]
    [InlineData(BoneLockState.Locked)]
    [InlineData(BoneLockState.Priority)]
    public void RuntimeBinding_RetainsProtectionOnlyRows(BoneLockState lockState)
    {
        var protectionOnly = Transform(lockState, false, false, false);

        var resolution = ProfileTransformResolver.ResolveContributions(new[]
        {
            new ProfileTransformResolver.TemplateContribution(
                Guid.NewGuid(),
                new Dictionary<string, BoneTransform>(StringComparer.Ordinal) { ["j_kosi"] = protectionOnly },
                true,
                1f),
        });

        Assert.True(resolution.EffectiveTransforms.ContainsKey("j_kosi"));
        Assert.True(Armature.ShouldRetainResolvedTransform(resolution.EffectiveTransforms["j_kosi"]));
    }

    [Fact]
    public void ProfileStack_CombinesProtectionStatesConservativelyWithoutMutatingSources()
    {
        var pinnedSource = Transform(BoneLockState.Unlocked, true, false, false);
        var lockedSource = Transform(BoneLockState.Locked, false, false, true);
        var resolution = ProfileTransformResolver.ResolveContributions(new[]
        {
            new ProfileTransformResolver.TemplateContribution(
                Guid.NewGuid(),
                new Dictionary<string, BoneTransform>(StringComparer.Ordinal) { ["j_kosi"] = pinnedSource },
                true,
                0.5f),
            new ProfileTransformResolver.TemplateContribution(
                Guid.NewGuid(),
                new Dictionary<string, BoneTransform>(StringComparer.Ordinal) { ["j_kosi"] = lockedSource },
                true,
                0.5f),
        });

        AssertProtectionEqual(Transform(BoneLockState.Unlocked, true, false, false), pinnedSource);
        AssertProtectionEqual(Transform(BoneLockState.Locked, false, false, true), lockedSource);
        AssertProtectionEqual(Transform(BoneLockState.Locked, true, false, true), resolution.EffectiveTransforms["j_kosi"]);
    }

    [Fact]
    public void ProtectionChanges_InvalidateTheArmatureTransformSignature()
    {
        var baseline = new Dictionary<string, BoneTransform>(StringComparer.Ordinal)
        {
            ["j_kosi"] = new BoneTransform(),
        };
        var baselineSignature = Armature.ComputeTransformSignature(baseline, 7);

        foreach (var transform in new[]
        {
            Transform(BoneLockState.Locked, false, false, false),
            Transform(BoneLockState.Priority, false, false, false),
            Transform(BoneLockState.Unlocked, true, false, false),
            Transform(BoneLockState.Unlocked, false, true, false),
            Transform(BoneLockState.Unlocked, false, false, true),
        })
        {
            transform.Scaling = Vector3.One;
            var candidate = new Dictionary<string, BoneTransform>(StringComparer.Ordinal) { ["j_kosi"] = transform };
            Assert.NotEqual(baselineSignature, Armature.ComputeTransformSignature(candidate, 7));
        }
    }

    [Fact]
    public void IpcProfileRoundTrip_PreservesLockAndPins()
    {
        var ipc = new IPCCharacterProfile
        {
            Bones = new Dictionary<string, IPCBoneTransform>(StringComparer.Ordinal)
            {
                ["j_kosi"] = new IPCBoneTransform
                {
                    LockState = BoneLockState.Priority,
                    PinX = true,
                    PinY = false,
                    PinZ = true,
                },
            },
        };

        var (_, template) = IPCCharacterProfile.ToFullProfile(ipc);
        var restored = IPCCharacterProfile.FromFullProfile(new Profiles.Data.Profile
        {
            Templates = new List<Template> { template },
        });

        Assert.Equal(BoneLockState.Priority, template.Bones["j_kosi"].LockState);
        Assert.True(template.Bones["j_kosi"].PinX);
        Assert.False(template.Bones["j_kosi"].PinY);
        Assert.True(template.Bones["j_kosi"].PinZ);
        Assert.Equal(BoneLockState.Priority, restored.Bones["j_kosi"].LockState);
        Assert.True(restored.Bones["j_kosi"].PinX);
        Assert.False(restored.Bones["j_kosi"].PinY);
        Assert.True(restored.Bones["j_kosi"].PinZ);
    }

    [Fact]
    public void OldTemplateWithoutProtectionFields_LoadsAsUnlockedAndUnpinned()
    {
        var template = new Template
        {
            Bones = new Dictionary<string, BoneTransform>(StringComparer.Ordinal)
            {
                ["j_kosi"] = new BoneTransform(),
            },
        };
        var json = template.JsonSerialize();
        var bone = (JObject)json["Bones"]!["j_kosi"]!;
        bone.Remove("LockState");
        bone.Remove("PinX");
        bone.Remove("PinY");
        bone.Remove("PinZ");

        var reloaded = Template.Load(json);

        AssertProtectionEqual(new BoneTransform(), reloaded.Bones["j_kosi"]);
    }

    [Fact]
    public void EditHistory_RoundTripsLockAndPinsWithoutCrossRowMutation()
    {
        var before = new Dictionary<string, BoneTransform>(StringComparer.Ordinal)
        {
            ["j_kosi"] = new BoneTransform(),
            ["j_hara"] = new BoneTransform(),
        };
        var after = AuthoringTooling.CloneTransforms(before);
        after["j_kosi"].LockState = BoneLockState.Locked;
        after["j_hara"].PinY = true;
        var history = new TemplateEditHistory();

        history.Record("Protection edit", before, after);

        Assert.True(history.TryUndo(out var undone));
        AssertProtectionEqual(new BoneTransform(), undone["j_kosi"]);
        AssertProtectionEqual(new BoneTransform(), undone["j_hara"]);
        Assert.True(history.TryRedo(out var redone));
        AssertProtectionEqual(Transform(BoneLockState.Locked, false, false, false), redone["j_kosi"]);
        AssertProtectionEqual(Transform(BoneLockState.Unlocked, false, true, false), redone["j_hara"]);
    }

    private static BoneTransform Transform(BoneLockState lockState, bool pinX, bool pinY, bool pinZ)
        => new()
        {
            Scaling = new Vector3(1.15f, 0.90f, 1.05f),
            LockState = lockState,
            PinX = pinX,
            PinY = pinY,
            PinZ = pinZ,
        };

    private static void AssertProtectionEqual(BoneTransform expected, BoneTransform actual)
    {
        Assert.Equal(expected.LockState, actual.LockState);
        Assert.Equal(expected.PinX, actual.PinX);
        Assert.Equal(expected.PinY, actual.PinY);
        Assert.Equal(expected.PinZ, actual.PinZ);
    }
}
