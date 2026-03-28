// Copyright (c) Customize+.
// Licensed under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using CustomizePlus.Armatures.Data;
using CustomizePlus.Core.Extensions;
using FFXIVClientStructs.FFXIV.Client.Graphics.Scene;

namespace CustomizePlus.Core.Data;

internal static unsafe class AdvancedBodyScalingFullIkRetargetingSystem
{
    private const float Epsilon = 0.0001f;

    private sealed record ChainDefinition(
        AdvancedBodyScalingFullBodyIkChain Chain,
        string Label,
        string Description,
        string[] RequiredBones,
        string[][] OptionalTailBones);

    private sealed class BoneSnapshot
    {
        public required string Name { get; init; }
        public required ModelBone Bone { get; init; }
        public required Vector3 Position { get; init; }
        public required Quaternion Rotation { get; init; }
        public required Vector3 Scale { get; init; }
        public BoneTransform? AppliedTransform { get; init; }
    }

    private sealed class ResolvedChain
    {
        public required ChainDefinition Definition { get; init; }
        public required IReadOnlyList<BoneSnapshot> Bones { get; init; }
        public required float[] ActualSegmentLengths { get; init; }
        public required float[] EffectiveSegmentLengths { get; init; }
        public required float Activation { get; init; }
        public required float Strength { get; init; }
        public required float BlendAmount { get; init; }
        public required float ProportionDelta { get; init; }
        public required float ReachDelta { get; init; }
        public required float StrideDelta { get; init; }
        public required float PostureDelta { get; init; }
        public required bool HasScalePins { get; init; }
        public required bool LockLimited { get; init; }
        public string SkipReason { get; init; } = string.Empty;
        public bool IsValid => Bones.Count >= 2 && string.IsNullOrEmpty(SkipReason);
        public float ActualLength => ActualSegmentLengths.Sum();
        public float EffectiveLength => EffectiveSegmentLengths.Sum();
    }

    private sealed class RuntimeSolveResult
    {
        public required Dictionary<string, BoneTransform> TargetCorrections { get; init; }
        public required IReadOnlyList<AdvancedBodyScalingFullIkRetargetingChainDebugState> DebugChains { get; init; }
        public required float EstimatedBeforeRisk { get; init; }
        public required float EstimatedAfterRisk { get; init; }
        public required string Summary { get; init; }
        public required bool LocksLimited { get; init; }
        public required bool SafetyLimited { get; init; }
    }

    private sealed class SafetyAssessment
    {
        public bool Clamped { get; set; }
        public bool Rejected { get; set; }
        public bool Damped { get; set; }
        public bool SafetyLimited => Clamped || Rejected || Damped;
        public string Summary { get; set; } = string.Empty;
    }

    private sealed class ChainSolveResult
    {
        public required Vector3[] Positions { get; init; }
        public required float ResidualError { get; init; }
        public required SafetyAssessment Safety { get; init; }
    }

    private static readonly ChainDefinition[] Definitions =
    {
        new(
            AdvancedBodyScalingFullBodyIkChain.PelvisRoot,
            "Pelvis / Root",
            "Adapts the pelvis anchor and center-of-mass response so lower-body motion better matches changed leg and torso proportions.",
            new[] { "j_kosi", "j_sebo_a" },
            Array.Empty<string[]>()),
        new(
            AdvancedBodyScalingFullBodyIkChain.Spine,
            "Spine",
            "Redistributes torso posture through the supported spine chain so body intent reads more coherently on scaled proportions.",
            new[] { "j_kosi", "j_sebo_a", "j_sebo_b", "j_sebo_c" },
            Array.Empty<string[]>()),
        new(
            AdvancedBodyScalingFullBodyIkChain.NeckHead,
            "Neck / Head",
            "Preserves head readability and neck posture under changed torso and shoulder proportions.",
            new[] { "j_sebo_c", "j_kubi", "j_kao" },
            Array.Empty<string[]>()),
        new(
            AdvancedBodyScalingFullBodyIkChain.LeftArm,
            "Left Arm",
            "Adapts left-arm reach and chain posture to changed shoulder and arm proportions without replacing the source animation.",
            new[] { "j_sako_l", "j_ude_a_l", "j_ude_b_l" },
            new[]
            {
                new[] { "n_hte_l" },
                new[] { "j_te_l" },
            }),
        new(
            AdvancedBodyScalingFullBodyIkChain.RightArm,
            "Right Arm",
            "Adapts right-arm reach and chain posture to changed shoulder and arm proportions without replacing the source animation.",
            new[] { "j_sako_r", "j_ude_a_r", "j_ude_b_r" },
            new[]
            {
                new[] { "n_hte_r" },
                new[] { "j_te_r" },
            }),
        new(
            AdvancedBodyScalingFullBodyIkChain.LeftLeg,
            "Left Leg",
            "Adapts left-leg stride and lower-body posture so scaled leg chains read closer to the original animation intent.",
            new[] { "j_kosi", "j_asi_a_l", "j_asi_b_l", "j_asi_c_l", "j_asi_d_l" },
            Array.Empty<string[]>()),
        new(
            AdvancedBodyScalingFullBodyIkChain.RightLeg,
            "Right Leg",
            "Adapts right-leg stride and lower-body posture so scaled leg chains read closer to the original animation intent.",
            new[] { "j_kosi", "j_asi_a_r", "j_asi_b_r", "j_asi_c_r", "j_asi_d_r" },
            Array.Empty<string[]>()),
    };

    private static readonly IReadOnlyDictionary<AdvancedBodyScalingFullBodyIkChain, ChainDefinition> DefinitionMap =
        Definitions.ToDictionary(definition => definition.Chain);

    private static readonly Dictionary<AdvancedBodyScalingCorrectiveRegion, AdvancedBodyScalingFullBodyIkChain[]> RegionChainMap = new()
    {
        [AdvancedBodyScalingCorrectiveRegion.NeckShoulder] = new[]
        {
            AdvancedBodyScalingFullBodyIkChain.Spine,
            AdvancedBodyScalingFullBodyIkChain.NeckHead,
        },
        [AdvancedBodyScalingCorrectiveRegion.ClavicleUpperChest] = new[]
        {
            AdvancedBodyScalingFullBodyIkChain.Spine,
            AdvancedBodyScalingFullBodyIkChain.LeftArm,
            AdvancedBodyScalingFullBodyIkChain.RightArm,
        },
        [AdvancedBodyScalingCorrectiveRegion.ShoulderUpperArm] = new[]
        {
            AdvancedBodyScalingFullBodyIkChain.LeftArm,
            AdvancedBodyScalingFullBodyIkChain.RightArm,
        },
        [AdvancedBodyScalingCorrectiveRegion.ElbowForearm] = new[]
        {
            AdvancedBodyScalingFullBodyIkChain.LeftArm,
            AdvancedBodyScalingFullBodyIkChain.RightArm,
        },
        [AdvancedBodyScalingCorrectiveRegion.WaistHips] = new[]
        {
            AdvancedBodyScalingFullBodyIkChain.PelvisRoot,
            AdvancedBodyScalingFullBodyIkChain.Spine,
            AdvancedBodyScalingFullBodyIkChain.LeftLeg,
            AdvancedBodyScalingFullBodyIkChain.RightLeg,
        },
        [AdvancedBodyScalingCorrectiveRegion.HipUpperThigh] = new[]
        {
            AdvancedBodyScalingFullBodyIkChain.PelvisRoot,
            AdvancedBodyScalingFullBodyIkChain.LeftLeg,
            AdvancedBodyScalingFullBodyIkChain.RightLeg,
        },
        [AdvancedBodyScalingCorrectiveRegion.ThighKneeCalf] = new[]
        {
            AdvancedBodyScalingFullBodyIkChain.LeftLeg,
            AdvancedBodyScalingFullBodyIkChain.RightLeg,
        },
    };

    public static IReadOnlyList<AdvancedBodyScalingFullBodyIkChain> GetOrderedChains()
        => Definitions.Select(definition => definition.Chain).ToArray();

    public static string GetChainLabel(AdvancedBodyScalingFullBodyIkChain chain)
        => DefinitionMap.TryGetValue(chain, out var definition) ? definition.Label : chain.ToString();

    public static string GetChainDescription(AdvancedBodyScalingFullBodyIkChain chain)
        => DefinitionMap.TryGetValue(chain, out var definition) ? definition.Description : string.Empty;

    public static IReadOnlyList<string> GetTuningAdvisories(AdvancedBodyScalingSettings settings)
    {
        var advisories = new List<string>();
        if (!settings.Enabled || settings.Mode == AdvancedBodyScalingMode.Manual || !settings.FullIkRetargeting.Enabled)
            return advisories;

        var retarget = settings.FullIkRetargeting;
        if (retarget.GlobalStrength > AdvancedBodyScalingFullIkRetargetingTuning.RecommendedGlobalStrengthMax)
            advisories.Add("Global retargeting strength exceeds the recommended range and may start to replace the original animation too strongly.");
        if (retarget.PelvisStrength > AdvancedBodyScalingFullIkRetargetingTuning.RecommendedPelvisStrengthMax)
            advisories.Add("Pelvis/root retargeting is above the recommended range and can overdrive whole-body posture.");
        if (retarget.SpineStrength > AdvancedBodyScalingFullIkRetargetingTuning.RecommendedSpineStrengthMax)
            advisories.Add("Spine retargeting is above the recommended range and may make torso posture feel artificial.");
        if (retarget.ArmStrength > AdvancedBodyScalingFullIkRetargetingTuning.RecommendedArmStrengthMax)
            advisories.Add("Arm retargeting is above the recommended range and may over-correct reach.");
        if (retarget.LegStrength > AdvancedBodyScalingFullIkRetargetingTuning.RecommendedLegStrengthMax)
            advisories.Add("Leg retargeting is above the recommended range and may exaggerate stride changes.");
        if (retarget.HeadStrength > AdvancedBodyScalingFullIkRetargetingTuning.RecommendedHeadStrengthMax)
            advisories.Add("Head/neck retargeting is above the recommended range and may make head motion read too detached.");
        if (retarget.ReachAdaptationStrength > AdvancedBodyScalingFullIkRetargetingTuning.RecommendedReachAdaptationMax)
            advisories.Add("Reach adaptation exceeds the recommended range and may replace the arm animation intent too aggressively.");
        if (retarget.StrideAdaptationStrength > AdvancedBodyScalingFullIkRetargetingTuning.RecommendedStrideAdaptationMax)
            advisories.Add("Stride adaptation exceeds the recommended range and may distort lower-body motion.");
        if (retarget.PosturePreservationStrength > AdvancedBodyScalingFullIkRetargetingTuning.RecommendedPosturePreservationMax)
            advisories.Add("Posture preservation exceeds the recommended range and may amplify torso biasing.");
        if (retarget.MotionSafetyBias < AdvancedBodyScalingFullIkRetargetingTuning.RecommendedMotionSafetyBiasMin)
            advisories.Add("Retarget motion-safety bias is below the recommended range; damping and deadzone protection may be too weak.");
        if (retarget.BlendBias > AdvancedBodyScalingFullIkRetargetingTuning.RecommendedBlendBiasMax)
            advisories.Add("Retarget blend bias exceeds the recommended range and may make the source animation feel replaced.");
        if (retarget.MaxCorrectionClamp > AdvancedBodyScalingFullIkRetargetingTuning.RecommendedMaxCorrectionClampMax)
            advisories.Add("Max retarget correction clamp exceeds the recommended range and may allow visibly implausible corrections.");

        foreach (var chain in GetOrderedChains())
        {
            var chainSettings = retarget.GetChainSettings(chain);
            if (chainSettings.Strength <= AdvancedBodyScalingFullIkRetargetingTuning.GetRecommendedChainStrengthMax(chain))
                continue;

            advisories.Add($"{GetChainLabel(chain)} retargeting strength exceeds the recommended range and may adapt less predictably.");
        }

        return advisories;
    }

    public static void EvaluateAndApply(
        Armature armature,
        CharacterBase* cBase,
        AdvancedBodyScalingSettings settings,
        bool profileOverridesActive,
        float deltaSeconds,
        Dictionary<string, BoneTransform> smoothedCorrections,
        AdvancedBodyScalingFullIkRetargetingDebugState debugState)
    {
        var retarget = settings.FullIkRetargeting;
        debugState.Reset(retarget.Enabled, profileOverridesActive, retarget.MotionSafetyBias, retarget.BlendBias);

        if (cBase == null || !settings.Enabled || settings.Mode == AdvancedBodyScalingMode.Manual || !retarget.Enabled || retarget.GlobalStrength <= 0f)
        {
            smoothedCorrections.Clear();
            debugState.FinalizeState(false, false, false, 0f, 0f, "Full IK retargeting is disabled.");
            return;
        }

        var snapshot = BuildLiveSnapshot(armature, cBase);
        if (snapshot.Count == 0)
        {
            smoothedCorrections.Clear();
            debugState.FinalizeState(false, false, false, 0f, 0f, "No supported live retargeting chain data was available.");
            return;
        }

        var runtimeSolve = SolveRuntime(armature, snapshot, settings);
        var temporalStabilityActive = UpdateSmoothedCorrections(smoothedCorrections, runtimeSolve.TargetCorrections, deltaSeconds, retarget.MotionSafetyBias);
        ApplyCorrections(cBase, armature, smoothedCorrections);
        var summary = temporalStabilityActive
            ? $"{runtimeSolve.Summary} Temporal smoothing softened small frame-to-frame retargeting changes."
            : runtimeSolve.Summary;

        debugState.Reset(retarget.Enabled, profileOverridesActive, retarget.MotionSafetyBias, retarget.BlendBias);
        debugState.Chains.AddRange(runtimeSolve.DebugChains);
        debugState.FinalizeState(
            smoothedCorrections.Count > 0,
            runtimeSolve.LocksLimited,
            runtimeSolve.SafetyLimited || temporalStabilityActive,
            runtimeSolve.EstimatedBeforeRisk,
            runtimeSolve.EstimatedAfterRisk,
            summary);
    }

    public static IReadOnlyList<AdvancedBodyScalingFullIkRetargetingEstimate> EstimateStaticSupport(
        IReadOnlyDictionary<string, BoneTransform> transforms,
        AdvancedBodyScalingSettings settings)
    {
        var output = new List<AdvancedBodyScalingFullIkRetargetingEstimate>();
        if (!settings.Enabled || settings.Mode == AdvancedBodyScalingMode.Manual || !settings.FullIkRetargeting.Enabled || settings.FullIkRetargeting.GlobalStrength <= 0f)
            return output;

        foreach (var definition in Definitions)
        {
            var chainSettings = settings.FullIkRetargeting.GetChainSettings(definition.Chain);
            var strength = ComputeChainStrength(settings.FullIkRetargeting, definition.Chain, chainSettings);
            if (!chainSettings.Enabled || strength <= 0.001f)
            {
                output.Add(new AdvancedBodyScalingFullIkRetargetingEstimate
                {
                    Chain = definition.Chain,
                    Label = definition.Label,
                    IsValid = false,
                    IsActive = false,
                    DriverSummary = "Chain disabled",
                    Description = definition.Description,
                    SkipReason = "This chain is disabled by the current Full IK retargeting tuning.",
                });
                continue;
            }

            var scales = ResolveStaticChainScales(transforms, definition);
            if (scales.Count < definition.RequiredBones.Length)
            {
                output.Add(new AdvancedBodyScalingFullIkRetargetingEstimate
                {
                    Chain = definition.Chain,
                    Label = definition.Label,
                    IsValid = false,
                    IsActive = false,
                    DriverSummary = "Unsupported chain data",
                    Description = definition.Description,
                    SkipReason = "One or more supported bones for this chain are missing in the current template/profile data.",
                });
                continue;
            }

            var averageScale = scales.Average();
            var proportionDelta = averageScale - 1f;
            var continuity = AverageNeighborContinuity(scales);
            var balanceDelta = ComputeBalanceDelta(scales, scales);
            var activation = ComputeActivation(MathF.Abs(proportionDelta) + (continuity * 0.65f) + (MathF.Abs(balanceDelta) * 0.45f), settings.FullIkRetargeting.MotionSafetyBias);
            var blendAmount = ComputeBlendAmount(settings.FullIkRetargeting, strength, activation);
            var reachDelta = proportionDelta * settings.FullIkRetargeting.ReachAdaptationStrength;
            var strideDelta = proportionDelta * settings.FullIkRetargeting.StrideAdaptationStrength;
            var postureDelta = ((proportionDelta * 0.45f) + (balanceDelta * 0.85f)) * settings.FullIkRetargeting.PosturePreservationStrength;
            var beforeRisk = Math.Clamp((MathF.Abs(proportionDelta) * 84f) + (continuity * 48f) + (MathF.Abs(balanceDelta) * 42f), 0f, 100f);
            var reductionFraction = EstimateRiskReduction(settings.FullIkRetargeting, blendAmount);
            output.Add(new AdvancedBodyScalingFullIkRetargetingEstimate
            {
                Chain = definition.Chain,
                Label = definition.Label,
                IsValid = true,
                IsActive = blendAmount > 0.01f,
                Strength = strength,
                BlendAmount = blendAmount,
                ProportionDelta = proportionDelta,
                ReachDelta = reachDelta,
                StrideDelta = strideDelta,
                PostureDelta = postureDelta,
                EstimatedRiskReduction = reductionFraction,
                EstimatedBeforeRisk = beforeRisk,
                EstimatedAfterRisk = beforeRisk * (1f - reductionFraction),
                DriverSummary = BuildStaticDriverSummary(proportionDelta, continuity, balanceDelta),
                Description = definition.Description,
            });
        }

        return output
            .OrderByDescending(entry => entry.BlendAmount)
            .ThenByDescending(entry => entry.Strength)
            .ToList();
    }

    public static AdvancedBodyScalingFullIkRetargetingRegionEstimate EstimateRegionRiskReduction(
        IReadOnlyDictionary<string, BoneTransform> transforms,
        AdvancedBodyScalingSettings settings,
        AdvancedBodyScalingCorrectiveRegion region,
        float poseWeight)
    {
        const string defaultSummary = "No strong full IK retargeting response is expected for this region in this pose.";
        if (!RegionChainMap.TryGetValue(region, out var chains))
        {
            return new AdvancedBodyScalingFullIkRetargetingRegionEstimate
            {
                Summary = defaultSummary,
            };
        }

        var estimates = EstimateStaticSupport(transforms, settings)
            .ToDictionary(estimate => estimate.Chain, estimate => estimate);

        var relevant = chains
            .Where(chain => estimates.TryGetValue(chain, out var estimate) && estimate.IsValid && estimate.BlendAmount > 0.01f)
            .Select(chain => estimates[chain])
            .OrderByDescending(estimate => estimate.BlendAmount)
            .ToList();

        if (relevant.Count == 0)
        {
            return new AdvancedBodyScalingFullIkRetargetingRegionEstimate
            {
                Summary = defaultSummary,
            };
        }

        var blendedReduction = Math.Clamp(relevant.Average(estimate => estimate.EstimatedRiskReduction) * Math.Clamp(poseWeight, 0f, 1f), 0f, 0.28f);
        var top = relevant.Take(2).Select(estimate => estimate.Label).ToList();
        return new AdvancedBodyScalingFullIkRetargetingRegionEstimate
        {
            EstimatedRiskReduction = blendedReduction,
            Strength = relevant.Max(estimate => estimate.Strength),
            ChainLabels = top,
            Summary = $"Estimated retargeting activity {relevant.Max(estimate => estimate.BlendAmount):0.00} via {string.Join(" and ", top)}, trimming about {(blendedReduction * 100f):0}% of this region's residual risk before the final IK pass.",
        };
    }

    private static IReadOnlyDictionary<string, BoneSnapshot> BuildLiveSnapshot(Armature armature, CharacterBase* cBase)
    {
        var snapshot = new Dictionary<string, BoneSnapshot>(StringComparer.Ordinal);
        foreach (var bone in armature.GetAllBones())
        {
            if (snapshot.ContainsKey(bone.BoneName))
                continue;

            var transform = bone.GetGameTransform(cBase, ModelBone.PoseType.Model);
            if (transform.Equals(Constants.NullTransform))
                continue;

            snapshot[bone.BoneName] = new BoneSnapshot
            {
                Name = bone.BoneName,
                Bone = bone,
                Position = transform.Translation.ToVector3(),
                Rotation = Quaternion.Normalize(transform.Rotation.ToQuaternion()),
                Scale = transform.Scale.ToVector3(),
                AppliedTransform = bone.AppliedTransform,
            };
        }

        return snapshot;
    }

    private static RuntimeSolveResult SolveRuntime(
        Armature armature,
        IReadOnlyDictionary<string, BoneSnapshot> snapshot,
        AdvancedBodyScalingSettings settings)
    {
        var retarget = settings.FullIkRetargeting;
        var resolvedChains = Definitions
            .Select(definition => ResolveLiveChain(snapshot, settings, definition))
            .ToDictionary(chain => chain.Definition.Chain);

        var debugChains = new List<AdvancedBodyScalingFullIkRetargetingChainDebugState>();
        var targetCorrections = new Dictionary<string, BoneTransform>(StringComparer.Ordinal);
        var locksLimited = false;
        var safetyLimited = false;

        foreach (var chain in resolvedChains.Values.Where(chain => !chain.IsValid))
        {
            locksLimited |= chain.LockLimited;
            debugChains.Add(new AdvancedBodyScalingFullIkRetargetingChainDebugState
            {
                Chain = chain.Definition.Chain,
                Label = chain.Definition.Label,
                IsValid = false,
                IsActive = false,
                LockLimited = chain.LockLimited,
                DriverSummary = chain.LockLimited ? "Locked chain preserved" : "Chain unsupported",
                Description = chain.Definition.Description,
                SkipReason = chain.SkipReason,
            });
        }

        var validChains = resolvedChains.Values.Where(chain => chain.IsValid).ToList();
        if (validChains.Count == 0)
        {
            return new RuntimeSolveResult
            {
                TargetCorrections = targetCorrections,
                DebugChains = debugChains,
                EstimatedBeforeRisk = 0f,
                EstimatedAfterRisk = 0f,
                Summary = "No supported retargeting chains were available for this actor or the current user locks.",
                LocksLimited = locksLimited,
                SafetyLimited = false,
            };
        }

        var solveResults = new Dictionary<AdvancedBodyScalingFullBodyIkChain, ChainSolveResult>();
        var pelvisTranslation = Vector3.Zero;
        var upperSpineDelta = Vector3.Zero;
        var pelvisSafety = new SafetyAssessment();

        var leftLeg = TryGetChain(resolvedChains, AdvancedBodyScalingFullBodyIkChain.LeftLeg);
        var rightLeg = TryGetChain(resolvedChains, AdvancedBodyScalingFullBodyIkChain.RightLeg);
        var pelvisChain = TryGetChain(resolvedChains, AdvancedBodyScalingFullBodyIkChain.PelvisRoot);
        var spine = TryGetChain(resolvedChains, AdvancedBodyScalingFullBodyIkChain.Spine);
        var neck = TryGetChain(resolvedChains, AdvancedBodyScalingFullBodyIkChain.NeckHead);
        var leftArm = TryGetChain(resolvedChains, AdvancedBodyScalingFullBodyIkChain.LeftArm);
        var rightArm = TryGetChain(resolvedChains, AdvancedBodyScalingFullBodyIkChain.RightArm);

        var pelvisSolve = ComputePelvisShift(leftLeg, rightLeg, retarget);
        pelvisTranslation = pelvisSolve.Shift;
        pelvisSafety = pelvisSolve.Safety;

        if (pelvisChain != null)
        {
            var rootTarget = pelvisChain.Bones[0].Position + pelvisTranslation;
            var target = BuildChainTarget(pelvisChain, rootTarget, Vector3.Zero, pelvisTranslation, retarget);
            solveResults[pelvisChain.Definition.Chain] = SolveChainPositions(pelvisChain, rootTarget, target, retarget);
        }

        if (leftLeg != null)
        {
            var rootTarget = leftLeg.Bones[0].Position + pelvisTranslation;
            var target = BuildChainTarget(leftLeg, rootTarget, Vector3.Zero, pelvisTranslation, retarget);
            solveResults[leftLeg.Definition.Chain] = SolveChainPositions(leftLeg, rootTarget, target, retarget);
        }

        if (rightLeg != null)
        {
            var rootTarget = rightLeg.Bones[0].Position + pelvisTranslation;
            var target = BuildChainTarget(rightLeg, rootTarget, Vector3.Zero, pelvisTranslation, retarget);
            solveResults[rightLeg.Definition.Chain] = SolveChainPositions(rightLeg, rootTarget, target, retarget);
        }

        if (spine != null)
        {
            var rootTarget = spine.Bones[0].Position + pelvisTranslation;
            var target = BuildChainTarget(spine, rootTarget, Vector3.Zero, pelvisTranslation, retarget);
            var result = SolveChainPositions(spine, rootTarget, target, retarget);
            solveResults[spine.Definition.Chain] = result;

            var upperIndex = Math.Min(3, result.Positions.Length - 1);
            if (upperIndex >= 0 && upperIndex < spine.Bones.Count)
                upperSpineDelta = result.Positions[upperIndex] - spine.Bones[upperIndex].Position;
        }
        else
        {
            upperSpineDelta = pelvisTranslation * 0.35f;
        }

        if (neck != null)
        {
            var rootTarget = neck.Bones[0].Position + upperSpineDelta;
            var target = BuildChainTarget(neck, rootTarget, upperSpineDelta, pelvisTranslation, retarget);
            solveResults[neck.Definition.Chain] = SolveChainPositions(neck, rootTarget, target, retarget);
        }

        if (leftArm != null)
        {
            var rootTarget = leftArm.Bones[0].Position + (upperSpineDelta * 0.75f);
            var target = BuildChainTarget(leftArm, rootTarget, upperSpineDelta, pelvisTranslation, retarget);
            solveResults[leftArm.Definition.Chain] = SolveChainPositions(leftArm, rootTarget, target, retarget);
        }

        if (rightArm != null)
        {
            var rootTarget = rightArm.Bones[0].Position + (upperSpineDelta * 0.75f);
            var target = BuildChainTarget(rightArm, rootTarget, upperSpineDelta, pelvisTranslation, retarget);
            solveResults[rightArm.Definition.Chain] = SolveChainPositions(rightArm, rootTarget, target, retarget);
        }

        if (snapshot.TryGetValue("j_kosi", out var pelvisSnapshot))
        {
            var pelvisCorrection = BuildPelvisTranslationCorrection(pelvisSnapshot, pelvisTranslation, retarget, pelvisChain?.BlendAmount ?? retarget.GlobalStrength);
            if (pelvisCorrection != null)
                targetCorrections[pelvisSnapshot.Name] = pelvisCorrection;
        }

        foreach (var chain in validChains)
        {
            var chainSafety = chain.Definition.Chain == AdvancedBodyScalingFullBodyIkChain.PelvisRoot
                ? pelvisSafety
                : solveResults.TryGetValue(chain.Definition.Chain, out var chainResult)
                    ? chainResult.Safety
                    : new SafetyAssessment();

            if (!solveResults.TryGetValue(chain.Definition.Chain, out var solveResult))
            {
                solveResult = new ChainSolveResult
                {
                    Positions = chain.Bones.Select(bone => bone.Position).ToArray(),
                    ResidualError = 0f,
                    Safety = chainSafety,
                };
            }

            var correctionMagnitude = chain.Definition.Chain == AdvancedBodyScalingFullBodyIkChain.PelvisRoot
                ? pelvisTranslation.Length()
                : 0f;
            var rotationDeadzone = GetChainRotationDeadzoneDegrees(chain.Definition.Chain, retarget);
            var chainBudget = GetChainCorrectionBudgetDegrees(chain.Definition.Chain, retarget, chain.BlendAmount);
            var responseBlend = GetChainResponseBlend(chain.Definition.Chain, retarget);
            var propagationFalloff = GetChainPropagationFalloff(chain.Definition.Chain);
            var cumulativeMagnitude = 0f;

            for (var i = 0; i < chain.Bones.Count - 1; i++)
            {
                var bone = chain.Bones[i];
                var currentDirection = chain.Bones[i + 1].Position - bone.Position;
                var targetDirection = solveResult.Positions[i + 1] - solveResult.Positions[i];
                if (currentDirection.LengthSquared() <= Epsilon || targetDirection.LengthSquared() <= Epsilon)
                    continue;

                var worldDelta = GetRotationBetween(Vector3.Normalize(currentDirection), Vector3.Normalize(targetDirection));
                var localDelta = ToLocalDelta(bone.Rotation, worldDelta);
                var maxDegrees = GetChainMaxRotationDegrees(chain.Definition.Chain, retarget, chain.BlendAmount);
                var appliedDelta = ClampRotation(localDelta, chain.BlendAmount, maxDegrees);
                if (IsIdentity(appliedDelta))
                    continue;

                var preDampingAngle = GetRotationAngleDegrees(appliedDelta);
                if (responseBlend < 0.999f)
                {
                    appliedDelta = Quaternion.Normalize(Quaternion.Slerp(Quaternion.Identity, appliedDelta, responseBlend));
                    if (GetRotationAngleDegrees(appliedDelta) + 0.01f < preDampingAngle)
                        chainSafety.Damped = true;
                }

                var appliedAngle = GetRotationAngleDegrees(appliedDelta);
                if (appliedAngle <= rotationDeadzone)
                {
                    chainSafety.Damped = true;
                    continue;
                }

                var remainingBudget = chainBudget - cumulativeMagnitude;
                if (remainingBudget <= rotationDeadzone)
                {
                    chainSafety.Clamped = true;
                    continue;
                }

                if (appliedAngle > remainingBudget)
                {
                    var budgetScale = remainingBudget / appliedAngle;
                    appliedDelta = Quaternion.Normalize(Quaternion.Slerp(Quaternion.Identity, appliedDelta, budgetScale));
                    appliedAngle = GetRotationAngleDegrees(appliedDelta);
                    chainSafety.Clamped = true;
                }

                if (appliedAngle <= rotationDeadzone)
                {
                    chainSafety.Damped = true;
                    continue;
                }

                cumulativeMagnitude += appliedAngle;
                correctionMagnitude += appliedAngle;
                AddCorrection(targetCorrections, bone.Name, Vector3.Zero, appliedDelta, propagateTranslation: false, propagateRotation: true, propagationFalloff);
            }

            if (chain.Definition.Chain == AdvancedBodyScalingFullBodyIkChain.NeckHead)
                ApplyHeadReadabilityCorrection(targetCorrections, chain, retarget, snapshot, chainSafety);

            if (string.IsNullOrWhiteSpace(chainSafety.Summary))
                chainSafety.Summary = BuildSafetySummary(chainSafety);

            var beforeRisk = EstimateChainRisk(chain);
            var effectiveStrength = chain.BlendAmount;
            if (chainSafety.Rejected)
                effectiveStrength *= 0.18f;
            else if (chainSafety.Clamped)
                effectiveStrength *= 0.62f;
            else if (chainSafety.Damped)
                effectiveStrength *= 0.80f;

            var afterRisk = beforeRisk * (1f - EstimateRiskReduction(retarget, effectiveStrength));
            debugChains.Add(new AdvancedBodyScalingFullIkRetargetingChainDebugState
            {
                Chain = chain.Definition.Chain,
                Label = chain.Definition.Label,
                IsValid = true,
                IsActive = correctionMagnitude > GetChainTranslationDeadzone(chain.Definition.Chain, retarget),
                LockLimited = chain.LockLimited,
                Clamped = chainSafety.Clamped,
                Rejected = chainSafety.Rejected,
                Damped = chainSafety.Damped,
                SafetyLimited = chainSafety.SafetyLimited,
                Strength = chain.Strength,
                BlendAmount = chain.BlendAmount,
                ProportionDelta = chain.ProportionDelta,
                ReachDelta = chain.ReachDelta,
                StrideDelta = chain.StrideDelta,
                PostureDelta = chain.PostureDelta,
                CorrectionMagnitude = correctionMagnitude,
                EstimatedBeforeRisk = beforeRisk,
                EstimatedAfterRisk = afterRisk,
                DriverSummary = BuildRuntimeDriverSummary(chain, solveResult.ResidualError, chainSafety),
                Description = chain.Definition.Description,
                SkipReason = chain.HasScalePins ? "Scale pins are present on this chain; retargeting preserves those pinned scale axes and only applies pose adjustments." : string.Empty,
                SafetySummary = chainSafety.Summary,
            });
        }

        locksLimited |= validChains.Any(chain => chain.LockLimited);
        safetyLimited = pelvisSafety.SafetyLimited || solveResults.Values.Any(result => result.Safety.SafetyLimited);

        var before = debugChains.Count == 0 ? 0f : debugChains.Where(chain => chain.IsValid).DefaultIfEmpty().Max(chain => chain?.EstimatedBeforeRisk ?? 0f);
        var after = debugChains.Count == 0 ? 0f : debugChains.Where(chain => chain.IsValid).DefaultIfEmpty().Max(chain => chain?.EstimatedAfterRisk ?? 0f);

        return new RuntimeSolveResult
        {
            TargetCorrections = targetCorrections,
            DebugChains = debugChains.OrderByDescending(chain => chain.BlendAmount).ThenByDescending(chain => chain.Strength).ToList(),
            EstimatedBeforeRisk = before,
            EstimatedAfterRisk = after,
            Summary = BuildRuntimeSummary(debugChains),
            LocksLimited = locksLimited,
            SafetyLimited = safetyLimited,
        };
    }

    private static ResolvedChain ResolveLiveChain(
        IReadOnlyDictionary<string, BoneSnapshot> snapshot,
        AdvancedBodyScalingSettings settings,
        ChainDefinition definition)
    {
        var chainSettings = settings.FullIkRetargeting.GetChainSettings(definition.Chain);
        var strength = ComputeChainStrength(settings.FullIkRetargeting, definition.Chain, chainSettings);
        if (!chainSettings.Enabled || strength <= 0.001f)
        {
            return new ResolvedChain
            {
                Definition = definition,
                Bones = Array.Empty<BoneSnapshot>(),
                ActualSegmentLengths = Array.Empty<float>(),
                EffectiveSegmentLengths = Array.Empty<float>(),
                Activation = 0f,
                Strength = 0f,
                BlendAmount = 0f,
                ProportionDelta = 0f,
                ReachDelta = 0f,
                StrideDelta = 0f,
                PostureDelta = 0f,
                HasScalePins = false,
                LockLimited = false,
                SkipReason = "This chain is disabled by the current Full IK retargeting tuning.",
            };
        }

        var bones = ResolveChainBones(snapshot, definition);
        if (bones.Count < definition.RequiredBones.Length)
        {
            return new ResolvedChain
            {
                Definition = definition,
                Bones = bones,
                ActualSegmentLengths = Array.Empty<float>(),
                EffectiveSegmentLengths = Array.Empty<float>(),
                Activation = 0f,
                Strength = strength,
                BlendAmount = 0f,
                ProportionDelta = 0f,
                ReachDelta = 0f,
                StrideDelta = 0f,
                PostureDelta = 0f,
                HasScalePins = false,
                LockLimited = false,
                SkipReason = "One or more supported bones for this chain are missing on the current actor.",
            };
        }

        var hasScalePins = bones.Any(bone => bone.AppliedTransform?.HasPinnedScaleAxes() ?? false);
        var lockedBone = bones.FirstOrDefault(bone => bone.AppliedTransform?.LockState != BoneLockState.Unlocked);
        if (lockedBone != null)
        {
            return new ResolvedChain
            {
                Definition = definition,
                Bones = bones,
                ActualSegmentLengths = Array.Empty<float>(),
                EffectiveSegmentLengths = Array.Empty<float>(),
                Activation = 0f,
                Strength = strength,
                BlendAmount = 0f,
                ProportionDelta = 0f,
                ReachDelta = 0f,
                StrideDelta = 0f,
                PostureDelta = 0f,
                HasScalePins = hasScalePins,
                LockLimited = true,
                SkipReason = $"User lock on {BoneData.GetBoneDisplayName(lockedBone.Name)} preserves existing authority for this chain.",
            };
        }

        var actualSegmentLengths = new float[bones.Count - 1];
        var effectiveSegmentLengths = new float[bones.Count - 1];
        for (var i = 0; i < bones.Count - 1; i++)
        {
            var from = bones[i];
            var to = bones[i + 1];
            var actual = MathF.Max(Vector3.Distance(from.Position, to.Position), 0.005f);
            actualSegmentLengths[i] = actual;
            effectiveSegmentLengths[i] = actual * EstimateLengthScale(from, to.Position);
        }

        var actualLength = actualSegmentLengths.Sum();
        var effectiveLength = effectiveSegmentLengths.Sum();
        var proportionDelta = actualLength <= Epsilon ? 0f : Math.Clamp((effectiveLength - actualLength) / actualLength, -0.45f, 0.65f);
        var balanceDelta = ComputeBalanceDelta(actualSegmentLengths, effectiveSegmentLengths);
        var activation = ComputeActivation(MathF.Abs(proportionDelta) + (MathF.Abs(balanceDelta) * 0.55f), settings.FullIkRetargeting.MotionSafetyBias);
        var blendAmount = ComputeBlendAmount(settings.FullIkRetargeting, strength, activation);

        return new ResolvedChain
        {
            Definition = definition,
            Bones = bones,
            ActualSegmentLengths = actualSegmentLengths,
            EffectiveSegmentLengths = effectiveSegmentLengths,
            Activation = activation,
            Strength = strength,
            BlendAmount = blendAmount,
            ProportionDelta = proportionDelta,
            ReachDelta = proportionDelta * settings.FullIkRetargeting.ReachAdaptationStrength,
            StrideDelta = proportionDelta * settings.FullIkRetargeting.StrideAdaptationStrength,
            PostureDelta = ((proportionDelta * 0.45f) + (balanceDelta * 0.85f)) * settings.FullIkRetargeting.PosturePreservationStrength,
            HasScalePins = hasScalePins,
            LockLimited = false,
        };
    }

    private static List<BoneSnapshot> ResolveChainBones(IReadOnlyDictionary<string, BoneSnapshot> snapshot, ChainDefinition definition)
    {
        var output = new List<BoneSnapshot>();
        foreach (var boneName in definition.RequiredBones)
        {
            if (!snapshot.TryGetValue(boneName, out var bone))
                return output;

            output.Add(bone);
        }

        foreach (var group in definition.OptionalTailBones)
        {
            foreach (var optional in group)
            {
                if (!snapshot.TryGetValue(optional, out var bone))
                    continue;

                output.Add(bone);
                break;
            }
        }

        return output;
    }

    private static List<float> ResolveStaticChainScales(IReadOnlyDictionary<string, BoneTransform> transforms, ChainDefinition definition)
    {
        var output = new List<float>();
        foreach (var boneName in definition.RequiredBones)
        {
            if (!transforms.TryGetValue(boneName, out var transform))
                return output;

            output.Add(AdvancedBodyScalingPipeline.GetUniformScale(transform.Scaling));
        }

        foreach (var group in definition.OptionalTailBones)
        {
            foreach (var optional in group)
            {
                if (!transforms.TryGetValue(optional, out var transform))
                    continue;

                output.Add(AdvancedBodyScalingPipeline.GetUniformScale(transform.Scaling));
                break;
            }
        }

        return output;
    }

    private static ResolvedChain? TryGetChain(IReadOnlyDictionary<AdvancedBodyScalingFullBodyIkChain, ResolvedChain> resolvedChains, AdvancedBodyScalingFullBodyIkChain chain)
        => resolvedChains.TryGetValue(chain, out var value) && value.IsValid ? value : null;

    private static ChainSolveResult SolveChainPositions(
        ResolvedChain chain,
        Vector3 rootPosition,
        Vector3 targetPosition,
        AdvancedBodyScalingFullIkRetargetingSettings settings)
    {
        var positions = chain.Bones.Select(bone => bone.Position).ToArray();
        positions[0] = rootPosition;
        var lengthBlend = Math.Clamp(chain.BlendAmount + (MathF.Abs(chain.ProportionDelta) * 0.30f), 0f, 0.82f);
        var lengths = chain.ActualSegmentLengths
            .Select((actual, index) => Lerp(actual, chain.EffectiveSegmentLengths[index], lengthBlend))
            .ToArray();
        var totalLength = lengths.Sum();

        if (totalLength <= Epsilon || positions.Length < 2)
        {
            return new ChainSolveResult
            {
                Positions = positions,
                ResidualError = 0f,
                Safety = new SafetyAssessment(),
            };
        }

        if (Vector3.Distance(rootPosition, targetPosition) >= totalLength)
        {
            var direction = targetPosition - rootPosition;
            if (direction.LengthSquared() <= Epsilon)
                direction = GetFallbackDirection(chain.Bones);

            direction = Vector3.Normalize(direction);
            for (var i = 1; i < positions.Length; i++)
                positions[i] = positions[i - 1] + (direction * lengths[i - 1]);

            return ApplyChainSafetyPass(chain, positions, rootPosition, targetPosition, Vector3.Distance(positions[^1], targetPosition), settings);
        }

        var baseRoot = rootPosition;
        var iterations = Math.Max(2, 2 + (int)MathF.Round(chain.BlendAmount * 4f));
        for (var iteration = 0; iteration < iterations; iteration++)
        {
            positions[^1] = targetPosition;
            for (var i = positions.Length - 2; i >= 0; i--)
            {
                var direction = positions[i] - positions[i + 1];
                if (direction.LengthSquared() <= Epsilon)
                    direction = GetFallbackDirection(chain.Bones);

                positions[i] = positions[i + 1] + (Vector3.Normalize(direction) * lengths[i]);
            }

            positions[0] = baseRoot;
            for (var i = 1; i < positions.Length; i++)
            {
                var direction = positions[i] - positions[i - 1];
                if (direction.LengthSquared() <= Epsilon)
                    direction = GetFallbackDirection(chain.Bones);

                positions[i] = positions[i - 1] + (Vector3.Normalize(direction) * lengths[i - 1]);
            }
        }

        return ApplyChainSafetyPass(chain, positions, rootPosition, targetPosition, Vector3.Distance(positions[^1], targetPosition), settings);
    }

    private static ChainSolveResult ApplyChainSafetyPass(
        ResolvedChain chain,
        Vector3[] positions,
        Vector3 rootPosition,
        Vector3 targetPosition,
        float residual,
        AdvancedBodyScalingFullIkRetargetingSettings settings)
    {
        var safety = new SafetyAssessment();
        var referencePositions = BuildConservativeReferencePositions(chain, rootPosition);
        if (positions.Length != referencePositions.Length || positions.Length == 0)
        {
            return new ChainSolveResult
            {
                Positions = positions,
                ResidualError = residual,
                Safety = safety,
            };
        }

        var worstAlignment = ComputeWorstSegmentAlignment(referencePositions, positions);
        var midpointDeviation = ComputeMidpointDeviation(referencePositions, positions);
        var referenceLength = MathF.Max(chain.ActualLength, 0.01f);
        var hardReject =
            worstAlignment < GetHardAlignmentThreshold(chain.Definition.Chain) ||
            midpointDeviation > (referenceLength * GetHardMidpointDeviationFraction(chain.Definition.Chain)) ||
            residual > GetResidualThreshold(chain.Definition.Chain, settings, hardLimit: true);

        if (hardReject)
        {
            safety.Rejected = true;
            safety.Summary = BuildSafetySummary(safety, "Rejected an implausible retargeted chain pose and fell back to the conservative reference pose.");
            return new ChainSolveResult
            {
                Positions = referencePositions,
                ResidualError = Vector3.Distance(referencePositions[^1], targetPosition),
                Safety = safety,
            };
        }

        var keepFactor = 1f;
        if (worstAlignment < GetSoftAlignmentThreshold(chain.Definition.Chain))
        {
            var factor = Remap(
                GetSoftAlignmentThreshold(chain.Definition.Chain) - worstAlignment,
                0f,
                GetSoftAlignmentThreshold(chain.Definition.Chain) - GetHardAlignmentThreshold(chain.Definition.Chain));
            keepFactor = MathF.Min(keepFactor, 1f - (0.68f * factor));
            safety.Clamped = true;
        }

        var softMidpoint = referenceLength * GetSoftMidpointDeviationFraction(chain.Definition.Chain);
        var hardMidpoint = referenceLength * GetHardMidpointDeviationFraction(chain.Definition.Chain);
        if (midpointDeviation > softMidpoint)
        {
            var factor = Remap(midpointDeviation, softMidpoint, hardMidpoint);
            keepFactor = MathF.Min(keepFactor, 1f - (0.70f * factor));
            safety.Clamped = true;
        }

        var softResidual = GetResidualThreshold(chain.Definition.Chain, settings, hardLimit: false);
        var hardResidual = GetResidualThreshold(chain.Definition.Chain, settings, hardLimit: true);
        if (residual > softResidual)
        {
            var factor = Remap(residual, softResidual, hardResidual);
            keepFactor = MathF.Min(keepFactor, 1f - (0.55f * factor));
            safety.Damped = true;
        }

        for (var i = 1; i < positions.Length - 1; i++)
        {
            var maxDisplacement = referenceLength * GetMaxJointDisplacementFraction(chain.Definition.Chain);
            var deviation = positions[i] - referencePositions[i];
            if (deviation.LengthSquared() <= maxDisplacement * maxDisplacement)
                continue;

            positions[i] = referencePositions[i] + ClampVectorMagnitude(deviation, maxDisplacement);
            keepFactor = MathF.Min(keepFactor, 0.90f);
            safety.Clamped = true;
        }

        if (keepFactor < 0.999f)
        {
            positions = BlendPositions(referencePositions, positions, keepFactor);
            residual = Vector3.Distance(positions[^1], targetPosition);
        }

        if (string.IsNullOrWhiteSpace(safety.Summary))
            safety.Summary = BuildSafetySummary(safety);

        return new ChainSolveResult
        {
            Positions = positions,
            ResidualError = residual,
            Safety = safety,
        };
    }

    private static Vector3[] BuildConservativeReferencePositions(ResolvedChain chain, Vector3 rootPosition)
    {
        var rootDelta = rootPosition - chain.Bones[0].Position;
        var output = new Vector3[chain.Bones.Count];
        for (var i = 0; i < chain.Bones.Count; i++)
            output[i] = chain.Bones[i].Position + rootDelta;

        output[0] = rootPosition;
        return output;
    }

    private static (Vector3 Shift, SafetyAssessment Safety) ComputePelvisShift(
        ResolvedChain? leftLeg,
        ResolvedChain? rightLeg,
        AdvancedBodyScalingFullIkRetargetingSettings settings)
    {
        var safety = new SafetyAssessment();
        var suggestions = new List<Vector3>();
        if (leftLeg != null)
            suggestions.Add(ComputeLegRootSuggestion(leftLeg, settings));
        if (rightLeg != null)
            suggestions.Add(ComputeLegRootSuggestion(rightLeg, settings));

        if (suggestions.Count == 0)
            return (Vector3.Zero, safety);

        var shift = new Vector3(
            suggestions.Average(v => v.X),
            suggestions.Average(v => v.Y),
            suggestions.Average(v => v.Z));

        if (suggestions.Count >= 2 &&
            suggestions[0].LengthSquared() > Epsilon &&
            suggestions[1].LengthSquared() > Epsilon)
        {
            var disagreement = Vector3.Dot(Vector3.Normalize(suggestions[0]), Vector3.Normalize(suggestions[1]));
            if (disagreement < 0.20f)
            {
                shift *= 0.50f;
                safety.Damped = true;
                safety.Summary = "Pelvis retargeting was damped because the leg chains disagreed about the safer direction.";
            }
        }

        var averageLength = new[] { leftLeg, rightLeg }
            .Where(chain => chain != null)
            .Select(chain => chain!.ActualLength)
            .DefaultIfEmpty(1f)
            .Average();
        var maxDistance = averageLength * (0.010f + (settings.MaxCorrectionClamp * 0.040f));
        if (shift.LengthSquared() <= Epsilon)
            return (Vector3.Zero, safety);

        shift = ClampVectorMagnitude(shift, maxDistance);
        var deadzone = averageLength * (0.0010f + (settings.MotionSafetyBias * 0.0018f));
        if (shift.LengthSquared() <= deadzone * deadzone)
        {
            if (shift.LengthSquared() > Epsilon)
            {
                safety.Damped = true;
                if (string.IsNullOrWhiteSpace(safety.Summary))
                    safety.Summary = "Pelvis retargeting stayed inside the motion deadzone and was suppressed for stability.";
            }

            return (Vector3.Zero, safety);
        }

        return (shift, safety);
    }

    private static Vector3 ComputeLegRootSuggestion(ResolvedChain chain, AdvancedBodyScalingFullIkRetargetingSettings settings)
    {
        var root = chain.Bones[0].Position;
        var effector = chain.Bones[^1].Position;
        var direction = root - effector;
        if (direction.LengthSquared() <= Epsilon)
            return Vector3.Zero;

        direction = Vector3.Normalize(direction);
        var lengthDelta = chain.EffectiveLength - chain.ActualLength;
        var retargetPressure = MathF.Abs(chain.StrideDelta) + (MathF.Abs(chain.ProportionDelta) * 0.35f);
        if (retargetPressure <= 0.002f)
            return Vector3.Zero;

        var magnitude = lengthDelta
            * settings.PelvisStrength
            * Math.Clamp(retargetPressure, 0f, 1f)
            * Math.Clamp(chain.BlendAmount + 0.12f, 0f, 1f)
            * 0.42f;
        return direction * magnitude;
    }

    private static BoneTransform? BuildPelvisTranslationCorrection(
        BoneSnapshot pelvis,
        Vector3 pelvisTranslationModel,
        AdvancedBodyScalingFullIkRetargetingSettings settings,
        float blendAmount)
    {
        if (pelvisTranslationModel.LengthSquared() <= Epsilon || blendAmount <= 0.001f)
            return null;

        var maxDistance = 0.004f + (settings.MaxCorrectionClamp * 0.020f);
        var clampedModel = ClampVectorMagnitude(pelvisTranslationModel, maxDistance * Math.Clamp(blendAmount + 0.18f, 0f, 1f));
        if (clampedModel.LengthSquared() <= Epsilon)
            return null;

        var localTranslation = Vector3.Transform(clampedModel, Quaternion.Inverse(pelvis.Rotation));
        return new BoneTransform
        {
            Translation = localTranslation,
            PropagateTranslation = true,
            PropagationFalloff = 0.97f,
        };
    }

    private static Vector3 BuildChainTarget(
        ResolvedChain chain,
        Vector3 rootPosition,
        Vector3 upperSpineDelta,
        Vector3 pelvisTranslation,
        AdvancedBodyScalingFullIkRetargetingSettings settings)
    {
        var currentRoot = chain.Bones[0].Position;
        var currentEnd = chain.Bones[^1].Position;
        var currentVector = currentEnd - currentRoot;
        if (currentVector.LengthSquared() <= Epsilon)
            currentVector = GetFallbackDirection(chain.Bones);

        var direction = Vector3.Normalize(currentVector);
        var currentDistance = MathF.Max(Vector3.Distance(currentRoot, currentEnd), 0.005f);
        var signedLengthDelta = chain.EffectiveLength - chain.ActualLength;
        var primaryDelta = chain.Definition.Chain switch
        {
            AdvancedBodyScalingFullBodyIkChain.LeftArm => chain.ReachDelta,
            AdvancedBodyScalingFullBodyIkChain.RightArm => chain.ReachDelta,
            AdvancedBodyScalingFullBodyIkChain.LeftLeg => chain.StrideDelta,
            AdvancedBodyScalingFullBodyIkChain.RightLeg => chain.StrideDelta,
            _ => chain.PostureDelta,
        };

        var correctionWeight = chain.BlendAmount * (0.65f + MathF.Min(0.35f, MathF.Abs(primaryDelta)));
        var desiredDistance = currentDistance + (signedLengthDelta * correctionWeight);
        var target = rootPosition + (direction * desiredDistance);

        switch (chain.Definition.Chain)
        {
            case AdvancedBodyScalingFullBodyIkChain.PelvisRoot:
                target += pelvisTranslation * 0.35f;
                target += GetAverageBoneAxis(chain.Bones, Vector3.UnitY) * (chain.PostureDelta * MathF.Max(chain.EffectiveLength, 0.02f) * 0.08f * chain.BlendAmount);
                break;
            case AdvancedBodyScalingFullBodyIkChain.Spine:
                target += pelvisTranslation * 0.35f;
                target += GetAverageBoneAxis(chain.Bones, Vector3.UnitY) * (chain.PostureDelta * MathF.Max(chain.EffectiveLength, 0.02f) * 0.12f * chain.BlendAmount);
                break;
            case AdvancedBodyScalingFullBodyIkChain.NeckHead:
                target += upperSpineDelta * 0.55f;
                target += GetAverageBoneAxis(chain.Bones, Vector3.UnitY) * (chain.PostureDelta * MathF.Max(chain.EffectiveLength, 0.02f) * 0.08f * chain.BlendAmount);
                break;
            case AdvancedBodyScalingFullBodyIkChain.LeftArm:
            case AdvancedBodyScalingFullBodyIkChain.RightArm:
                target += upperSpineDelta * 0.45f;
                target += GetAverageBoneAxis(chain.Bones, Vector3.UnitX) * (chain.ReachDelta * MathF.Max(chain.ActualLength, 0.02f) * 0.06f * chain.BlendAmount);
                break;
            case AdvancedBodyScalingFullBodyIkChain.LeftLeg:
            case AdvancedBodyScalingFullBodyIkChain.RightLeg:
                target += pelvisTranslation * 0.25f;
                target += direction * (chain.StrideDelta * MathF.Max(chain.ActualLength, 0.02f) * 0.08f * chain.BlendAmount);
                break;
        }

        var maxShift = MathF.Max(chain.ActualLength, 0.02f) * (0.06f + (settings.MaxCorrectionClamp * 0.22f));
        return currentEnd + ClampVectorMagnitude(target - currentEnd, maxShift);
    }

    private static void ApplyHeadReadabilityCorrection(
        Dictionary<string, BoneTransform> corrections,
        ResolvedChain neckChain,
        AdvancedBodyScalingFullIkRetargetingSettings settings,
        IReadOnlyDictionary<string, BoneSnapshot> snapshot,
        SafetyAssessment safety)
    {
        if (neckChain.Bones.Count < 3 || settings.HeadStrength <= 0.001f)
            return;

        var neckBone = neckChain.Bones[^2];
        var headBone = neckChain.Bones[^1];
        if (!snapshot.TryGetValue(neckBone.Name, out var neckSnapshot) || !snapshot.TryGetValue(headBone.Name, out var headSnapshot))
            return;

        var targetHeadRotation = headSnapshot.Rotation;
        var neckTarget = Quaternion.Inverse(neckSnapshot.Rotation) * targetHeadRotation;
        var headTarget = Quaternion.Inverse(headSnapshot.Rotation) * targetHeadRotation;

        var maxDegrees = GetChainMaxRotationDegrees(neckChain.Definition.Chain, settings, neckChain.BlendAmount) * 0.45f;
        var neckDelta = ClampRotation(neckTarget, settings.HeadStrength * neckChain.BlendAmount * 0.28f, maxDegrees);
        var headDelta = ClampRotation(headTarget, settings.HeadStrength * neckChain.BlendAmount * 0.40f, maxDegrees);
        var deadzone = GetChainRotationDeadzoneDegrees(neckChain.Definition.Chain, settings);

        if (GetRotationAngleDegrees(neckDelta) <= deadzone)
        {
            neckDelta = Quaternion.Identity;
            safety.Damped = true;
        }

        if (GetRotationAngleDegrees(headDelta) <= deadzone)
        {
            headDelta = Quaternion.Identity;
            safety.Damped = true;
        }

        if (!IsIdentity(neckDelta))
            AddCorrection(corrections, neckBone.Name, Vector3.Zero, neckDelta, propagateTranslation: false, propagateRotation: true, 0.98f);

        if (!IsIdentity(headDelta))
            AddCorrection(corrections, headBone.Name, Vector3.Zero, headDelta, propagateTranslation: false, propagateRotation: false, 0.98f);
    }

    private static bool UpdateSmoothedCorrections(
        Dictionary<string, BoneTransform> smoothedCorrections,
        IReadOnlyDictionary<string, BoneTransform> targetCorrections,
        float deltaSeconds,
        float motionSafetyBias)
    {
        var sharpness = Lerp(15f, 4.5f, Math.Clamp(motionSafetyBias, 0f, 1f));
        var targetBlend = Lerp(0.78f, 0.52f, Math.Clamp(motionSafetyBias, 0f, 1f));
        var translationDeadzone = 0.00030f + (motionSafetyBias * 0.00100f);
        var rotationDeadzone = 0.22f + (motionSafetyBias * 0.95f);
        var temporalStabilityActive = false;

        foreach (var (bone, target) in targetCorrections)
        {
            if (!smoothedCorrections.TryGetValue(bone, out var existing))
            {
                existing = new BoneTransform();
                smoothedCorrections[bone] = existing;
            }

            var stabilizedTarget = new BoneTransform(target);
            var existingRotation = existing.Rotation.ToQuaternion();
            var targetRotation = target.Rotation.ToQuaternion();
            var translationDelta = Vector3.Distance(existing.Translation, target.Translation);
            var rotationDelta = GetRotationAngleDegrees(Quaternion.Normalize(Quaternion.Inverse(existingRotation) * targetRotation));

            if (translationDelta <= translationDeadzone && rotationDelta <= rotationDeadzone)
            {
                temporalStabilityActive = true;
                stabilizedTarget = new BoneTransform(existing);
            }
            else
            {
                stabilizedTarget.Translation = Vector3.Lerp(existing.Translation, target.Translation, targetBlend);
                stabilizedTarget.Rotation = BoneTransform.FromQuaternionDegrees(Quaternion.Normalize(Quaternion.Slerp(existingRotation, targetRotation, targetBlend)));
                stabilizedTarget.PropagateTranslation = target.PropagateTranslation;
                stabilizedTarget.PropagateRotation = target.PropagateRotation;
                stabilizedTarget.PropagationFalloff = target.PropagationFalloff;
                temporalStabilityActive |= targetBlend < 0.999f;
            }

            existing.SmoothTowards(stabilizedTarget, deltaSeconds, sharpness);
        }

        foreach (var bone in smoothedCorrections.Keys.ToList())
        {
            if (targetCorrections.TryGetValue(bone, out _))
                continue;

            var existing = smoothedCorrections[bone];
            if (existing.SmoothTowards(new BoneTransform(), deltaSeconds, sharpness) && !existing.IsEdited(true))
                smoothedCorrections.Remove(bone);
        }

        return temporalStabilityActive;
    }

    private static void ApplyCorrections(CharacterBase* cBase, Armature armature, IReadOnlyDictionary<string, BoneTransform> corrections)
    {
        if (cBase == null || corrections.Count == 0)
            return;

        foreach (var chain in GetOrderedChains())
        {
            if (!DefinitionMap.TryGetValue(chain, out var definition))
                continue;

            foreach (var boneName in definition.RequiredBones.Concat(definition.OptionalTailBones.SelectMany(group => group)))
            {
                if (!corrections.TryGetValue(boneName, out var correction) || !correction.IsEdited(true))
                    continue;

                var modelBone = armature.GetAllBones().FirstOrDefault(bone => bone.BoneName == boneName);
                modelBone?.ApplyRuntimeCorrection(cBase, correction);
            }
        }
    }

    private static float EstimateLengthScale(BoneSnapshot bone, Vector3 childPosition)
    {
        var direction = childPosition - bone.Position;
        if (direction.LengthSquared() <= Epsilon)
            return AdvancedBodyScalingPipeline.GetUniformScale(bone.Scale);

        direction = Vector3.Normalize(direction);
        var localDirection = Vector3.Transform(direction, Quaternion.Inverse(bone.Rotation));
        var weight = new Vector3(MathF.Abs(localDirection.X), MathF.Abs(localDirection.Y), MathF.Abs(localDirection.Z));
        var total = weight.X + weight.Y + weight.Z;
        if (total <= Epsilon)
            return AdvancedBodyScalingPipeline.GetUniformScale(bone.Scale);

        var weightedScale =
            ((weight.X * MathF.Abs(bone.Scale.X)) +
             (weight.Y * MathF.Abs(bone.Scale.Y)) +
             (weight.Z * MathF.Abs(bone.Scale.Z))) / total;
        return Math.Clamp(weightedScale, 0.40f, 2.50f);
    }

    private static float ComputeChainStrength(
        AdvancedBodyScalingFullIkRetargetingSettings settings,
        AdvancedBodyScalingFullBodyIkChain chain,
        AdvancedBodyScalingFullIkRetargetingChainSettings chainSettings)
    {
        var regionalStrength = chain switch
        {
            AdvancedBodyScalingFullBodyIkChain.PelvisRoot => settings.PelvisStrength,
            AdvancedBodyScalingFullBodyIkChain.Spine => settings.SpineStrength,
            AdvancedBodyScalingFullBodyIkChain.NeckHead => settings.HeadStrength,
            AdvancedBodyScalingFullBodyIkChain.LeftArm => settings.ArmStrength,
            AdvancedBodyScalingFullBodyIkChain.RightArm => settings.ArmStrength,
            AdvancedBodyScalingFullBodyIkChain.LeftLeg => settings.LegStrength,
            AdvancedBodyScalingFullBodyIkChain.RightLeg => settings.LegStrength,
            _ => 0f,
        };

        var rawStrength = settings.GlobalStrength * regionalStrength * chainSettings.Strength;
        return CompressChainStrength(chain, rawStrength);
    }

    private static float ComputeBlendAmount(AdvancedBodyScalingFullIkRetargetingSettings settings, float strength, float activation)
    {
        var blend = activation * strength * Lerp(0.55f, 1f, settings.BlendBias);
        return Math.Clamp(blend, 0f, 0.65f);
    }

    private static float ComputeActivation(float pressure, float motionSafetyBias)
    {
        var deadzone = 0.030f + (motionSafetyBias * 0.050f);
        var full = 0.14f + (motionSafetyBias * 0.10f);
        return Remap(pressure, deadzone, full);
    }

    private static float EstimateRiskReduction(AdvancedBodyScalingFullIkRetargetingSettings settings, float blendAmount)
        => Math.Clamp(blendAmount * (0.18f + (settings.MaxCorrectionClamp * 0.20f)), 0f, 0.24f);

    private static float EstimateChainRisk(ResolvedChain chain)
        => Math.Clamp(
            (MathF.Abs(chain.ProportionDelta) * 90f) +
            (MathF.Abs(chain.ReachDelta) * 28f) +
            (MathF.Abs(chain.StrideDelta) * 32f) +
            (MathF.Abs(chain.PostureDelta) * 28f),
            0f,
            100f);

    private static float ComputeBalanceDelta(IReadOnlyList<float> actualSegments, IReadOnlyList<float> effectiveSegments)
    {
        if (actualSegments.Count == 0 || effectiveSegments.Count == 0 || actualSegments.Count != effectiveSegments.Count)
            return 0f;

        if (actualSegments.Count == 1)
            return 0f;

        var splitIndex = Math.Max(1, actualSegments.Count / 2);
        var actualHead = actualSegments.Take(splitIndex).Sum();
        var actualTail = actualSegments.Skip(splitIndex).Sum();
        var effectiveHead = effectiveSegments.Take(splitIndex).Sum();
        var effectiveTail = effectiveSegments.Skip(splitIndex).Sum();
        if (actualTail <= Epsilon || effectiveTail <= Epsilon)
            return 0f;

        var actualRatio = actualHead / actualTail;
        var effectiveRatio = effectiveHead / effectiveTail;
        return Math.Clamp(effectiveRatio - actualRatio, -0.45f, 0.45f);
    }

    private static float AverageNeighborContinuity(IReadOnlyList<float> values)
    {
        if (values.Count < 2)
            return 0f;

        var total = 0f;
        for (var i = 1; i < values.Count; i++)
            total += MathF.Abs(values[i] - values[i - 1]);

        return total / (values.Count - 1);
    }

    private static string BuildStaticDriverSummary(float proportionDelta, float continuity, float balanceDelta)
    {
        if (MathF.Abs(proportionDelta) <= 0.03f && continuity <= 0.05f && MathF.Abs(balanceDelta) <= 0.03f)
            return "Proportion drift low";

        if (MathF.Abs(balanceDelta) > 0.06f)
            return $"Segment balance drift {balanceDelta:0.00}";

        if (continuity > 0.08f)
            return $"Chain continuity drift {continuity:0.00}";

        return $"Proportion delta {proportionDelta:+0.00;-0.00;0.00}";
    }

    private static string BuildRuntimeDriverSummary(ResolvedChain chain, float residual, SafetyAssessment safety)
    {
        var descriptors = new List<string>();
        if (MathF.Abs(chain.ProportionDelta) > 0.03f)
            descriptors.Add($"proportion delta {chain.ProportionDelta:+0.00;-0.00;0.00}");
        if (MathF.Abs(chain.ReachDelta) > 0.02f)
            descriptors.Add($"reach {chain.ReachDelta:+0.00;-0.00;0.00}");
        if (MathF.Abs(chain.StrideDelta) > 0.02f)
            descriptors.Add($"stride {chain.StrideDelta:+0.00;-0.00;0.00}");
        if (MathF.Abs(chain.PostureDelta) > 0.02f)
            descriptors.Add($"posture {chain.PostureDelta:+0.00;-0.00;0.00}");
        if (chain.HasScalePins)
            descriptors.Add("scale pins preserved");
        if (residual > 0.001f)
            descriptors.Add($"residual {residual:0.000}");
        if (safety.Rejected)
            descriptors.Add("safety fallback");
        else if (safety.Clamped)
            descriptors.Add("safety clamp");
        else if (safety.Damped)
            descriptors.Add("temporal damping");

        return descriptors.Count == 0 ? "Retarget pressure low" : string.Join(", ", descriptors);
    }

    private static string BuildRuntimeSummary(IReadOnlyList<AdvancedBodyScalingFullIkRetargetingChainDebugState> chains)
    {
        var active = chains.Where(chain => chain.IsActive).OrderByDescending(chain => chain.BlendAmount).Take(3).ToList();
        if (active.Count == 0)
            return "Full IK retargeting found no supported chain with enough proportion drift to justify a conservative adjustment.";

        var labels = string.Join(", ", active.Select(chain => chain.Label));
        var safetyLimited = active.Where(chain => chain.SafetyLimited).Select(chain => chain.Label).Distinct(StringComparer.Ordinal).ToList();
        var safetyText = safetyLimited.Count == 0
            ? string.Empty
            : $" Safety limiting was active on {string.Join(", ", safetyLimited)}.";
        return $"Full IK retargeting adapted {labels} before the final Full-Body IK pass.{safetyText}";
    }

    private static Vector3[] BlendPositions(Vector3[] from, Vector3[] to, float keepFactor)
    {
        var output = new Vector3[Math.Min(from.Length, to.Length)];
        var amount = Math.Clamp(keepFactor, 0f, 1f);
        for (var i = 0; i < output.Length; i++)
            output[i] = Vector3.Lerp(from[i], to[i], amount);

        return output;
    }

    private static float ComputeWorstSegmentAlignment(Vector3[] referencePositions, Vector3[] solvedPositions)
    {
        var worst = 1f;
        for (var i = 0; i < Math.Min(referencePositions.Length, solvedPositions.Length) - 1; i++)
        {
            var referenceDirection = referencePositions[i + 1] - referencePositions[i];
            var solvedDirection = solvedPositions[i + 1] - solvedPositions[i];
            if (referenceDirection.LengthSquared() <= Epsilon || solvedDirection.LengthSquared() <= Epsilon)
                continue;

            var dot = Vector3.Dot(Vector3.Normalize(referenceDirection), Vector3.Normalize(solvedDirection));
            worst = MathF.Min(worst, dot);
        }

        return worst;
    }

    private static float ComputeMidpointDeviation(Vector3[] referencePositions, Vector3[] solvedPositions)
    {
        if (referencePositions.Length == 0 || solvedPositions.Length == 0)
            return 0f;

        var midpointIndex = Math.Min(referencePositions.Length, solvedPositions.Length) / 2;
        return Vector3.Distance(referencePositions[midpointIndex], solvedPositions[midpointIndex]);
    }

    private static Vector3 GetFallbackDirection(IReadOnlyList<BoneSnapshot> bones)
    {
        if (bones.Count >= 2)
        {
            var direction = bones[1].Position - bones[0].Position;
            if (direction.LengthSquared() > Epsilon)
                return Vector3.Normalize(direction);
        }

        return Vector3.UnitY;
    }

    private static Vector3 GetAverageBoneAxis(IReadOnlyList<BoneSnapshot> bones, Vector3 localAxis)
    {
        var output = Vector3.Zero;
        foreach (var bone in bones)
            output += Vector3.Transform(localAxis, bone.Rotation);

        if (output.LengthSquared() <= Epsilon)
            return localAxis;

        return Vector3.Normalize(output);
    }

    private static void AddCorrection(
        Dictionary<string, BoneTransform> corrections,
        string boneName,
        Vector3 localTranslation,
        Quaternion localRotation,
        bool propagateTranslation,
        bool propagateRotation,
        float propagationFalloff)
    {
        if (!corrections.TryGetValue(boneName, out var correction))
        {
            correction = new BoneTransform();
            corrections[boneName] = correction;
        }

        if (localTranslation.LengthSquared() > Epsilon)
        {
            correction.Translation += localTranslation;
            correction.PropagateTranslation |= propagateTranslation;
        }

        if (!IsIdentity(localRotation))
        {
            var existing = correction.Rotation.ToQuaternion();
            var combined = Quaternion.Normalize(existing * localRotation);
            correction.Rotation = BoneTransform.FromQuaternionDegrees(combined);
            correction.PropagateRotation |= propagateRotation;
        }

        correction.PropagationFalloff = Math.Clamp(
            MathF.Min(correction.PropagationFalloff, propagationFalloff),
            0.82f,
            0.99f);
    }

    private static Quaternion ToLocalDelta(Quaternion modelRotation, Quaternion modelDelta)
        => Quaternion.Normalize(Quaternion.Inverse(modelRotation) * modelDelta * modelRotation);

    private static Quaternion ClampRotation(Quaternion localDelta, float strength, float maxDegrees)
    {
        if (strength <= 0.001f || IsIdentity(localDelta))
            return Quaternion.Identity;

        var blended = Quaternion.Normalize(Quaternion.Slerp(Quaternion.Identity, localDelta, Math.Clamp(strength, 0f, 1f)));
        var currentAngle = GetRotationAngleDegrees(blended);
        if (currentAngle <= maxDegrees || currentAngle <= Epsilon)
            return blended;

        var scale = maxDegrees / currentAngle;
        return Quaternion.Normalize(Quaternion.Slerp(Quaternion.Identity, blended, scale));
    }

    private static float GetChainMaxRotationDegrees(AdvancedBodyScalingFullBodyIkChain chain, AdvancedBodyScalingFullIkRetargetingSettings settings, float blendAmount)
    {
        var target = chain switch
        {
            AdvancedBodyScalingFullBodyIkChain.PelvisRoot => 6f,
            AdvancedBodyScalingFullBodyIkChain.Spine => 10f,
            AdvancedBodyScalingFullBodyIkChain.NeckHead => 9f,
            AdvancedBodyScalingFullBodyIkChain.LeftArm => 14f,
            AdvancedBodyScalingFullBodyIkChain.RightArm => 14f,
            AdvancedBodyScalingFullBodyIkChain.LeftLeg => 10f,
            AdvancedBodyScalingFullBodyIkChain.RightLeg => 10f,
            _ => 10f,
        };

        return Lerp(2.5f, target, Math.Clamp(settings.MaxCorrectionClamp * Math.Clamp(blendAmount + 0.10f, 0f, 1f), 0f, 1f));
    }

    private static float GetChainRotationDeadzoneDegrees(AdvancedBodyScalingFullBodyIkChain chain, AdvancedBodyScalingFullIkRetargetingSettings settings)
    {
        var baseline = chain switch
        {
            AdvancedBodyScalingFullBodyIkChain.PelvisRoot => 0.40f,
            AdvancedBodyScalingFullBodyIkChain.Spine => 0.50f,
            AdvancedBodyScalingFullBodyIkChain.NeckHead => 0.60f,
            AdvancedBodyScalingFullBodyIkChain.LeftLeg => 0.55f,
            AdvancedBodyScalingFullBodyIkChain.RightLeg => 0.55f,
            _ => 0.35f,
        };

        return baseline + (settings.MotionSafetyBias * 0.90f);
    }

    private static float GetChainTranslationDeadzone(AdvancedBodyScalingFullBodyIkChain chain, AdvancedBodyScalingFullIkRetargetingSettings settings)
        => chain switch
        {
            AdvancedBodyScalingFullBodyIkChain.PelvisRoot => 0.0005f + (settings.MotionSafetyBias * 0.0012f),
            _ => 0.00035f + (settings.MotionSafetyBias * 0.0009f),
        };

    private static float GetChainCorrectionBudgetDegrees(AdvancedBodyScalingFullBodyIkChain chain, AdvancedBodyScalingFullIkRetargetingSettings settings, float blendAmount)
    {
        var maxDegrees = chain switch
        {
            AdvancedBodyScalingFullBodyIkChain.PelvisRoot => 6f,
            AdvancedBodyScalingFullBodyIkChain.Spine => 10f,
            AdvancedBodyScalingFullBodyIkChain.NeckHead => 9f,
            AdvancedBodyScalingFullBodyIkChain.LeftLeg => 11f,
            AdvancedBodyScalingFullBodyIkChain.RightLeg => 11f,
            AdvancedBodyScalingFullBodyIkChain.LeftArm => 18f,
            AdvancedBodyScalingFullBodyIkChain.RightArm => 18f,
            _ => 10f,
        };

        return Lerp(3f, maxDegrees, Math.Clamp(settings.MaxCorrectionClamp * Math.Clamp(blendAmount + 0.10f, 0f, 1f), 0f, 1f));
    }

    private static float GetChainResponseBlend(AdvancedBodyScalingFullBodyIkChain chain, AdvancedBodyScalingFullIkRetargetingSettings settings)
        => chain switch
        {
            AdvancedBodyScalingFullBodyIkChain.LeftLeg => Lerp(0.80f, 0.52f, settings.MotionSafetyBias),
            AdvancedBodyScalingFullBodyIkChain.RightLeg => Lerp(0.80f, 0.52f, settings.MotionSafetyBias),
            AdvancedBodyScalingFullBodyIkChain.PelvisRoot => Lerp(0.78f, 0.55f, settings.MotionSafetyBias),
            AdvancedBodyScalingFullBodyIkChain.Spine => Lerp(0.82f, 0.58f, settings.MotionSafetyBias),
            AdvancedBodyScalingFullBodyIkChain.NeckHead => Lerp(0.80f, 0.56f, settings.MotionSafetyBias),
            _ => Lerp(0.88f, 0.66f, settings.MotionSafetyBias),
        };

    private static float GetChainPropagationFalloff(AdvancedBodyScalingFullBodyIkChain chain)
        => chain switch
        {
            AdvancedBodyScalingFullBodyIkChain.PelvisRoot => 0.97f,
            AdvancedBodyScalingFullBodyIkChain.Spine => 0.96f,
            AdvancedBodyScalingFullBodyIkChain.NeckHead => 0.98f,
            _ => 0.93f,
        };

    private static float CompressChainStrength(AdvancedBodyScalingFullBodyIkChain chain, float rawStrength)
    {
        var softLimit = chain switch
        {
            AdvancedBodyScalingFullBodyIkChain.PelvisRoot => 0.10f,
            AdvancedBodyScalingFullBodyIkChain.Spine => 0.12f,
            AdvancedBodyScalingFullBodyIkChain.NeckHead => 0.10f,
            AdvancedBodyScalingFullBodyIkChain.LeftLeg => 0.14f,
            AdvancedBodyScalingFullBodyIkChain.RightLeg => 0.14f,
            _ => 0.18f,
        };

        var compression = chain switch
        {
            AdvancedBodyScalingFullBodyIkChain.PelvisRoot => 0.20f,
            AdvancedBodyScalingFullBodyIkChain.Spine => 0.22f,
            AdvancedBodyScalingFullBodyIkChain.NeckHead => 0.22f,
            AdvancedBodyScalingFullBodyIkChain.LeftLeg => 0.20f,
            AdvancedBodyScalingFullBodyIkChain.RightLeg => 0.20f,
            _ => 0.28f,
        };

        if (rawStrength <= softLimit)
            return rawStrength;

        return softLimit + ((rawStrength - softLimit) * compression);
    }

    private static float GetSoftAlignmentThreshold(AdvancedBodyScalingFullBodyIkChain chain)
        => chain switch
        {
            AdvancedBodyScalingFullBodyIkChain.LeftLeg => 0.25f,
            AdvancedBodyScalingFullBodyIkChain.RightLeg => 0.25f,
            AdvancedBodyScalingFullBodyIkChain.Spine => 0.18f,
            AdvancedBodyScalingFullBodyIkChain.NeckHead => 0.22f,
            AdvancedBodyScalingFullBodyIkChain.LeftArm => 0.10f,
            AdvancedBodyScalingFullBodyIkChain.RightArm => 0.10f,
            _ => 0.12f,
        };

    private static float GetHardAlignmentThreshold(AdvancedBodyScalingFullBodyIkChain chain)
        => chain switch
        {
            AdvancedBodyScalingFullBodyIkChain.LeftLeg => -0.05f,
            AdvancedBodyScalingFullBodyIkChain.RightLeg => -0.05f,
            AdvancedBodyScalingFullBodyIkChain.Spine => -0.10f,
            AdvancedBodyScalingFullBodyIkChain.NeckHead => -0.08f,
            AdvancedBodyScalingFullBodyIkChain.LeftArm => -0.18f,
            AdvancedBodyScalingFullBodyIkChain.RightArm => -0.18f,
            _ => -0.12f,
        };

    private static float GetSoftMidpointDeviationFraction(AdvancedBodyScalingFullBodyIkChain chain)
        => chain switch
        {
            AdvancedBodyScalingFullBodyIkChain.LeftLeg => 0.07f,
            AdvancedBodyScalingFullBodyIkChain.RightLeg => 0.07f,
            AdvancedBodyScalingFullBodyIkChain.Spine => 0.06f,
            AdvancedBodyScalingFullBodyIkChain.NeckHead => 0.05f,
            AdvancedBodyScalingFullBodyIkChain.LeftArm => 0.08f,
            AdvancedBodyScalingFullBodyIkChain.RightArm => 0.08f,
            _ => 0.06f,
        };

    private static float GetHardMidpointDeviationFraction(AdvancedBodyScalingFullBodyIkChain chain)
        => chain switch
        {
            AdvancedBodyScalingFullBodyIkChain.LeftLeg => 0.13f,
            AdvancedBodyScalingFullBodyIkChain.RightLeg => 0.13f,
            AdvancedBodyScalingFullBodyIkChain.Spine => 0.11f,
            AdvancedBodyScalingFullBodyIkChain.NeckHead => 0.09f,
            AdvancedBodyScalingFullBodyIkChain.LeftArm => 0.16f,
            AdvancedBodyScalingFullBodyIkChain.RightArm => 0.16f,
            _ => 0.11f,
        };

    private static float GetMaxJointDisplacementFraction(AdvancedBodyScalingFullBodyIkChain chain)
        => chain switch
        {
            AdvancedBodyScalingFullBodyIkChain.LeftLeg => 0.08f,
            AdvancedBodyScalingFullBodyIkChain.RightLeg => 0.08f,
            AdvancedBodyScalingFullBodyIkChain.Spine => 0.07f,
            AdvancedBodyScalingFullBodyIkChain.NeckHead => 0.05f,
            AdvancedBodyScalingFullBodyIkChain.LeftArm => 0.10f,
            AdvancedBodyScalingFullBodyIkChain.RightArm => 0.10f,
            _ => 0.07f,
        };

    private static float GetResidualThreshold(AdvancedBodyScalingFullBodyIkChain chain, AdvancedBodyScalingFullIkRetargetingSettings settings, bool hardLimit)
    {
        var baseTolerance = Lerp(0.0045f, 0.015f, settings.MotionSafetyBias);
        var scale = hardLimit ? 1.9f : 1.1f;
        var multiplier = chain switch
        {
            AdvancedBodyScalingFullBodyIkChain.LeftLeg => 0.90f,
            AdvancedBodyScalingFullBodyIkChain.RightLeg => 0.90f,
            AdvancedBodyScalingFullBodyIkChain.NeckHead => 0.85f,
            _ => 1f,
        };

        return baseTolerance * scale * multiplier;
    }

    private static string BuildSafetySummary(SafetyAssessment safety, string? explicitSummary = null)
    {
        if (!string.IsNullOrWhiteSpace(explicitSummary))
            return explicitSummary;

        var descriptors = new List<string>();
        if (safety.Rejected)
            descriptors.Add("solve rejected");
        if (safety.Clamped)
            descriptors.Add("correction clamped");
        if (safety.Damped)
            descriptors.Add("damping/deadzone applied");

        return descriptors.Count == 0 ? string.Empty : string.Join(", ", descriptors);
    }

    private static Quaternion GetRotationBetween(Vector3 from, Vector3 to)
    {
        var dot = Math.Clamp(Vector3.Dot(from, to), -1f, 1f);
        if (dot >= 1f - 0.0001f)
            return Quaternion.Identity;

        if (dot <= -1f + 0.0001f)
        {
            var orthogonal = MathF.Abs(from.Y) < 0.99f ? Vector3.UnitY : Vector3.UnitX;
            var axis = Vector3.Normalize(Vector3.Cross(from, orthogonal));
            return Quaternion.CreateFromAxisAngle(axis, MathF.PI);
        }

        var axisCross = Vector3.Normalize(Vector3.Cross(from, to));
        var angle = MathF.Acos(dot);
        return Quaternion.CreateFromAxisAngle(axisCross, angle);
    }

    private static float GetRotationAngleDegrees(Quaternion rotation)
    {
        rotation = Quaternion.Normalize(rotation);
        var w = Math.Clamp(MathF.Abs(rotation.W), 0f, 1f);
        return (2f * MathF.Acos(w)) * (180f / MathF.PI);
    }

    private static bool IsIdentity(Quaternion rotation)
        => 1f - MathF.Abs(Quaternion.Dot(Quaternion.Normalize(rotation), Quaternion.Identity)) <= 0.0001f;

    private static Vector3 ClampVectorMagnitude(Vector3 vector, float maxMagnitude)
    {
        if (vector.LengthSquared() <= maxMagnitude * maxMagnitude)
            return vector;

        return Vector3.Normalize(vector) * maxMagnitude;
    }

    private static float Remap(float value, float start, float full)
    {
        if (full <= start)
            return value >= full ? 1f : 0f;

        return Math.Clamp((value - start) / (full - start), 0f, 1f);
    }

    private static float Lerp(float from, float to, float amount)
        => from + ((to - from) * Math.Clamp(amount, 0f, 1f));
}
