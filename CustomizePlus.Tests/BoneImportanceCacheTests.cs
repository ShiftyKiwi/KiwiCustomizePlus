using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using CustomizePlus.Armatures.Services;
using CustomizePlus.Core.Data;
using CustomizePlus.Core.Services;
using CustomizePlus.Interop.Ipc;
using Penumbra.GameData.Enums;
using Xunit;
using Xunit.Abstractions;

namespace CustomizePlus.Tests;

public sealed class BoneImportanceCacheTests(ITestOutputHelper output)
{
    private static readonly Type Service = typeof(AdvancedBodyScalingBoneImportanceService);
    private static readonly Type Manager = typeof(ArmatureManager);

    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void ProbeAndParsedModelUseExactlyTheSameIdentity(bool coarse, bool skin)
    {
        var fixture = new Fixture(coarse, skin);
        Assert.Equal(fixture.ProbeSignature(), fixture.ResolvedSignature());
        Assert.DoesNotContain(":True", fixture.ResolvedSignature());
    }

    [Fact]
    public void ParsedQualityChangesAggregateKeyWithoutChangingReferenceIdentity()
    {
        var skin = new Fixture(true, true);
        var coarse = new Fixture(true, false);
        Assert.Equal(skin.ResolvedSignature(), coarse.ResolvedSignature());
        Assert.NotEqual(skin.AggregateKey(), coarse.AggregateKey());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void ARealChangeInAnySlotChangesBothSignatures(int slot)
    {
        var initial = new Fixture();
        var changed = new Fixture(changedSlot: slot);
        Assert.NotEqual(initial.ResolvedSignature(), changed.ProbeSignature());
        Assert.Equal(changed.ProbeSignature(), changed.ResolvedSignature());
        Assert.NotEqual(initial.AggregateKey(), changed.AggregateKey());
    }

    [Fact]
    public void MissingSlotDoesNotAliasAResolvedSlot()
    {
        var fixture = new Fixture();
        var initial = fixture.ProbeSignature();
        fixture.RemoveSlot(1);
        Assert.NotEqual(initial, fixture.ProbeSignature());
        Assert.Equal(fixture.ProbeSignature(), fixture.ResolvedSignature());
        Assert.Contains("BottomLegs:missing", fixture.ProbeSignature());
    }

    [Fact]
    public void ResolutionSourceAndActorSpecificPathsStayDistinct()
    {
        var vanilla = new Fixture(source: AdvancedBodyScalingBoneImportanceResolutionSource.VanillaModelPath);
        var penumbra = new Fixture();
        var anotherActor = new Fixture(changedSlot: 0);
        Assert.NotEqual(vanilla.ProbeSignature(), penumbra.ProbeSignature());
        Assert.NotEqual(penumbra.ProbeSignature(), anotherActor.ProbeSignature());
    }

    [Fact]
    public void ProbeSkipsOnlyPreviouslyParsedInvalidCandidates()
    {
        var fixture = new Fixture();
        Assert.Contains("0-original", fixture.SelectReferenceWithInvalidCache()!);
        Assert.Contains("1-original", fixture.SelectReferenceWithInvalidCache(0)!);
        Assert.Null(fixture.SelectReferenceWithInvalidCache(0, 1, 2, 3));
    }

    [Fact]
    public void ResourceSnapshotIsLazyAndSharedOnlyWithinOneEvaluation()
    {
        var lookups = 0;
        var firstPaths = Paths("first");
        var secondPaths = Paths("second");
        var first = new BoneImportanceResourcePaths(() => { lookups++; return firstPaths; });
        var second = new BoneImportanceResourcePaths(() => { lookups++; return secondPaths; });
        Assert.Equal(0, lookups);
        for (var slot = 0; slot < 4; slot++)
            Assert.Same(firstPaths, first.Paths);
        Assert.Equal(1, lookups);
        Assert.Same(secondPaths, second.Paths);
        Assert.Equal(2, lookups);
        Assert.NotSame(first.Paths, second.Paths);
    }

    [Fact]
    public void UnavailableSnapshotIsReusedButNextEvaluationCanRecover()
    {
        var lookups = 0;
        var unavailable = new BoneImportanceResourcePaths(() => { lookups++; return new Dictionary<string, HashSet<string>>(); });
        for (var slot = 0; slot < 4; slot++)
            Assert.Empty(unavailable.Paths);
        Assert.Equal(1, lookups);
        var recovered = new BoneImportanceResourcePaths(() => Paths("recovered"));
        Assert.NotEmpty(recovered.Paths);
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public void ProfileOverrideRetainsItsAuthoritativeMeaning(bool? enabledOverride, bool expected)
    {
        var global = new AdvancedBodyScalingSettings { ModelDerivedBoneImportanceEnabled = true };
        var profile = new AdvancedBodyScalingProfileSettings
        {
            UseProfileOverrides = true,
            Overrides = new AdvancedBodyScalingOverrides { ModelDerivedBoneImportanceEnabled = enabledOverride },
        };
        Assert.Equal(expected, profile.Resolve(global, Race.Hyur).ModelDerivedBoneImportanceEnabled);
        Assert.True(global.ModelDerivedBoneImportanceEnabled);
    }

    [Fact]
    public void DisabledBiwNeverNeedsActorOrModelServices()
    {
        var service = (AdvancedBodyScalingBoneImportanceService)RuntimeHelpers.GetUninitializedObject(Service);
        var disabled = new AdvancedBodyScalingSettings { ModelDerivedBoneImportanceEnabled = false };
        Assert.False(service.ProbeActorModelSignature(default, disabled).HasResolvedModelSet);
        Assert.False(service.ResolveForActor(default, disabled).ModelDerivedActive);
    }

    [Fact]
    public void InvalidActorUsesSafeFallback()
    {
        var service = (AdvancedBodyScalingBoneImportanceService)RuntimeHelpers.GetUninitializedObject(Service);
        var enabled = new AdvancedBodyScalingSettings { ModelDerivedBoneImportanceEnabled = true };
        Assert.False(service.ProbeActorModelSignature(default, enabled).HasResolvedModelSet);
        Assert.False(service.ResolveForActor(default, enabled).ModelDerivedActive);
    }

    [Fact]
    public void FullPriorityDoesNotBypassEffectiveDisableAndReducedPolicyRemainsSeparate()
    {
        var settings = new AdvancedBodyScalingSettings { ModelDerivedBoneImportanceEnabled = false, FullBoneImportanceOnSelf = true };
        var service = (AdvancedBodyScalingBoneImportanceService)RuntimeHelpers.GetUninitializedObject(Service);
        Assert.False(service.ResolveForActor(default, settings).ModelDerivedActive);
        Assert.True((bool)Manager.GetMethod("ShouldUseReducedBoneImportance", BindingFlags.Static | BindingFlags.NonPublic)!
            .Invoke(null, [AdvancedBodyScalingBoneImportanceActorTier.Self, false])!);
        Assert.False((bool)Manager.GetMethod("ShouldUseReducedBoneImportance", BindingFlags.Static | BindingFlags.NonPublic)!
            .Invoke(null, [AdvancedBodyScalingBoneImportanceActorTier.NearbyNonProfiled, false])!);
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(1, true)]
    [InlineData(2, false)]
    [InlineData(3, false)]
    [InlineData(4, false)]
    public void ExistingActorEligibilityIsUnchanged(int tierValue, bool expected)
    {
        var tier = (AdvancedBodyScalingBoneImportanceActorTier)tierValue;
        var manager = RuntimeHelpers.GetUninitializedObject(Manager);
        var budgetType = Manager.GetNestedType("BoneImportanceFrameBudgetState", BindingFlags.NonPublic)!;
        var budget = Activator.CreateInstance(budgetType, 14)!;
        var settings = new AdvancedBodyScalingSettings();
        Assert.Equal(expected, (bool)Manager.GetMethod("ShouldActivelyManageBoneImportance", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(manager, [settings, tier, budget])!);
        Assert.Equal(expected, (bool)Manager.GetMethod("IsFullBoneImportanceEligible", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(manager, [settings, tier])!);
    }

    [Fact]
    public void StableIdentityDoesNotRepeatedlyResolveAndReachesStableProbeCadence()
    {
        var fixture = new Fixture();
        var canonical = fixture.ResolvedSignature();
        var state = new AdvancedBodyScalingBoneImportanceRuntimeState { LastConfirmedModelSignature = canonical };
        var manager = RuntimeHelpers.GetUninitializedObject(Manager);
        var interval = Manager.GetMethod("GetBoneImportanceProbeIntervalMs", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var probes = 0;
        var resolves = 0;
        var nextProbe = 0L;
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        for (var now = 0L; now <= 31000; now += 10)
        {
            if (now < nextProbe)
                continue;
            probes++;
            var probe = new AdvancedBodyScalingBoneImportanceProbeResult
            {
                HasResolvedModelSet = true,
                ModelSignature = fixture.ProbeSignature(),
                ModelSignatureChanged = !string.Equals(canonical, fixture.ProbeSignature(), StringComparison.OrdinalIgnoreCase),
            };
            if (EvaluateSignature(state, canonical, probe, now))
                resolves++;
            nextProbe = now + (int)interval.Invoke(manager, [AdvancedBodyScalingBoneImportanceActorTier.Self, state.StableProbeCount])!;
        }
        var allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        output.WriteLine($"31-second production signature/debounce fixture: probes={probes}, resolves={resolves}, managed bytes={allocated}.");
        Assert.Equal(0, resolves);
        Assert.InRange(probes, 27, 32);
        Assert.Equal(24, state.StableProbeCount);
        Assert.Empty(state.PendingModelSignature);
    }

    [Fact]
    public void GenuineChangeStillRequiresExistingConfirmationAndInvalidatesIdentity()
    {
        var initial = new Fixture().ResolvedSignature();
        var changed = new Fixture(changedSlot: 1).ProbeSignature();
        var state = new AdvancedBodyScalingBoneImportanceRuntimeState { LastConfirmedModelSignature = initial };
        var probe = new AdvancedBodyScalingBoneImportanceProbeResult { HasResolvedModelSet = true, ModelSignature = changed, ModelSignatureChanged = true };
        Assert.False(EvaluateSignature(state, initial, probe, 1000));
        Assert.Equal(changed, state.PendingModelSignature);
        Assert.True(EvaluateSignature(state, initial, probe, 1500));
        Assert.Equal(0, state.StableProbeCount);
    }

    private static bool EvaluateSignature(AdvancedBodyScalingBoneImportanceRuntimeState state, string original,
        AdvancedBodyScalingBoneImportanceProbeResult probe, long now)
    {
        object?[] args = [state, AdvancedBodyScalingBoneImportanceActorTier.Self, now, false, true,
            original, probe, GlamourerAppearanceTransitionSnapshot.None, false, false, null];
        return (bool)Manager.GetMethod("EvaluateBoneImportanceSignatureChange", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, args)!;
    }

    private static Dictionary<string, HashSet<string>> Paths(string label) => new()
    {
        [$"C:\\models\\{label}.mdl"] = ["chara/equipment/e0000/model/body_top.mdl"],
    };

    // Reflection exercises the actual private production constructors, rather than a second
    // implementation of their signature formatting or resolution policy.
    private sealed class Fixture
    {
        private static Type Nested(string name) => Service.GetNestedType(name, BindingFlags.NonPublic)!;
        private readonly Array _requests = Array.CreateInstance(Nested("ModelPartRequest"), 4);
        private readonly IDictionary _references = (IDictionary)Activator.CreateInstance(
            typeof(Dictionary<,>).MakeGenericType(Nested("BodyModelPart"), Nested("ResolvedModelReference")))!;
        private readonly IList _contributions = (IList)Activator.CreateInstance(
            typeof(List<>).MakeGenericType(Nested("ModelContribution")))!;

        public Fixture(bool coarse = true, bool skin = true, int changedSlot = -1,
            AdvancedBodyScalingBoneImportanceResolutionSource source = AdvancedBodyScalingBoneImportanceResolutionSource.PenumbraResolvedModel)
        {
            for (var slot = 0; slot < 4; slot++)
            {
                var part = Enum.ToObject(Nested("BodyModelPart"), slot);
                var candidates = Array.CreateInstance(Nested("ModelPathCandidate"), 0);
                _requests.SetValue(Activator.CreateInstance(Nested("ModelPartRequest"), part, part.ToString(), 0.25f, candidates), slot);
                var path = $"C:\\models\\{slot}-{(slot == changedSlot ? "replacement" : "original")}.mdl";
                var reference = Activator.CreateInstance(Nested("ResolvedModelReference"),
                    path, path, path, "model", source, "tracked resource", part, part.ToString(), 0.25f)!;
                _references.Add(part, reference);
                var cached = Activator.CreateInstance(Nested("CachedModelImportance"), true)!;
                Nested("CachedModelImportance").GetProperty("Stage1Available")!.SetValue(cached, coarse);
                Nested("CachedModelImportance").GetProperty("Stage2Available")!.SetValue(cached, skin);
                _contributions.Add(Activator.CreateInstance(Nested("ModelContribution"), part, part.ToString(), 0.25f, reference, cached, true)!);
            }
        }
        public void RemoveSlot(int slot)
        {
            _references.Remove(Enum.ToObject(Nested("BodyModelPart"), slot));
            _contributions.RemoveAt(slot);
        }
        public string? SelectReferenceWithInvalidCache(params int[] invalidSlots)
        {
            var service = new AdvancedBodyScalingBoneImportanceService(null!, null!, null!);
            var cache = (IDictionary)Service.GetField("_modelCache", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(service)!;
            var references = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(Nested("ResolvedModelReference")))!;
            foreach (DictionaryEntry entry in _references)
            {
                references.Add(entry.Value);
                if (!invalidSlots.Contains(Convert.ToInt32(entry.Key)))
                    continue;
                var key = (string)Nested("ResolvedModelReference").GetProperty("CacheKey")!.GetValue(entry.Value)!;
                cache[key] = Activator.CreateInstance(Nested("CachedModelImportance"), true)!;
            }
            var selected = Service.GetMethod("SelectProbedModelReference", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(service, [references]);
            return selected == null ? null : (string)Nested("ResolvedModelReference").GetProperty("CacheKey")!.GetValue(selected)!;
        }
        private string Invoke(string method, params object[] args)
            => (string)Service.GetMethod(method, BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, args)!;
        public string ProbeSignature() => Invoke("BuildReferenceSignature", _requests, _references);
        public string ResolvedSignature() => Invoke("BuildResolvedModelSignature", _requests, _contributions);
        public string AggregateKey() => Invoke("BuildAggregateCacheKey", ResolvedSignature(), _contributions);
    }
}
