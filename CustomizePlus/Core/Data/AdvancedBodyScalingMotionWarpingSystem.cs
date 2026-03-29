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

internal static unsafe class AdvancedBodyScalingMotionWarpingSystem
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
        public required float Strength { get; init; }
        public required float BlendAmount { get; init; }
        public required float ProportionDelta { get; init; }
        public required float StridePressure { get; init; }
        public required float OrientationPressure { get; init; }
        public required float PosturePressure { get; init; }
        public required float MovementAlignment { get; init; }
        public required float LocomotionAmount { get; init; }
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
        public required IReadOnlyList<AdvancedBodyScalingMotionWarpingChainDebugState> DebugChains { get; init; }
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

    private static readonly ChainDefinition[] Definitions =
    {
        new(
            AdvancedBodyScalingFullBodyIkChain.PelvisRoot,
            "Pelvis / Root",
            "Shares locomotion pressure through the pelvis and root so stride length and center-of-mass read more coherently on scaled bodies.",
            new[] { "j_kosi", "j_sebo_a" },
            Array.Empty<string[]>()),
        new(
            AdvancedBodyScalingFullBodyIkChain.Spine,
            "Spine",
            "Redistributes locomotion posture through the supported spine chain so motion direction reads more coherently on scaled torsos.",
            new[] { "j_kosi", "j_sebo_a", "j_sebo_b", "j_sebo_c" },
            Array.Empty<string[]>()),
        new(
            AdvancedBodyScalingFullBodyIkChain.NeckHead,
            "Neck / Head",
            "Keeps head readability and upper-body balance calmer during locomotion on scaled proportions.",
            new[] { "j_sebo_c", "j_kubi", "j_kao" },
            Array.Empty<string[]>()),
        new(
            AdvancedBodyScalingFullBodyIkChain.LeftArm,
            "Left Arm",
            "Adds conservative locomotion balance to the left arm so upper-body swing and reach read less detached in motion.",
            new[] { "j_sako_l", "j_ude_a_l", "j_ude_b_l" },
            new[]
            {
                new[] { "n_hte_l" },
                new[] { "j_te_l" },
            }),
        new(
            AdvancedBodyScalingFullBodyIkChain.RightArm,
            "Right Arm",
            "Adds conservative locomotion balance to the right arm so upper-body swing and reach read less detached in motion.",
            new[] { "j_sako_r", "j_ude_a_r", "j_ude_b_r" },
            new[]
            {
                new[] { "n_hte_r" },
                new[] { "j_te_r" },
            }),
        new(
            AdvancedBodyScalingFullBodyIkChain.LeftLeg,
            "Left Leg",
            "Warps left-leg stride and direction response during locomotion so scaled lower-body motion reads closer to the source animation.",
            new[] { "j_kosi", "j_asi_a_l", "j_asi_b_l", "j_asi_c_l", "j_asi_d_l" },
            Array.Empty<string[]>()),
        new(
            AdvancedBodyScalingFullBodyIkChain.RightLeg,
            "Right Leg",
            "Warps right-leg stride and direction response during locomotion so scaled lower-body motion reads closer to the source animation.",
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

    public static string GetImplementationTierLabel()
        => AdvancedBodyScalingMotionWarpingTuning.ImplementationTierLabel;

    public static IReadOnlyList<string> GetTuningAdvisories(AdvancedBodyScalingSettings settings)
    {
        var advisories = new List<string>();
        if (!settings.Enabled || settings.Mode == AdvancedBodyScalingMode.Manual || !settings.MotionWarping.Enabled)
            return advisories;

        var motion = settings.MotionWarping;
        if (motion.GlobalStrength > AdvancedBodyScalingMotionWarpingTuning.RecommendedGlobalStrengthMax)
            advisories.Add("Global motion-warping strength exceeds the recommended range and may start to replace the original locomotion read too strongly.");
        if (motion.StrideWarpStrength > AdvancedBodyScalingMotionWarpingTuning.RecommendedStrideWarpStrengthMax)
            advisories.Add("Stride warping strength exceeds the recommended range and may exaggerate lower-body motion.");
        if (motion.OrientationWarpStrength > AdvancedBodyScalingMotionWarpingTuning.RecommendedOrientationWarpStrengthMax)
            advisories.Add("Orientation warping strength exceeds the recommended range and may over-steer pelvis and spine direction.");
        if (motion.PostureWarpStrength > AdvancedBodyScalingMotionWarpingTuning.RecommendedPostureWarpStrengthMax)
            advisories.Add("Posture / locomotion coherence strength exceeds the recommended range and may make upper-body motion feel overly guided.");
        if (motion.MotionSafetyBias < AdvancedBodyScalingMotionWarpingTuning.RecommendedMotionSafetyBiasMin)
            advisories.Add("Motion-warping safety bias is below the recommended range; damping and deadzone protection may be too weak.");
        if (motion.BlendBias > AdvancedBodyScalingMotionWarpingTuning.RecommendedBlendBiasMax)
            advisories.Add("Motion-warping blend bias exceeds the recommended range and may make the source animation feel replaced.");
        if (motion.MaxCorrectionClamp > AdvancedBodyScalingMotionWarpingTuning.RecommendedMaxCorrectionClampMax)
            advisories.Add("Max warp correction clamp exceeds the recommended range and may allow visibly implausible motion adjustments.");

        foreach (var chain in GetOrderedChains())
        {
            var chainSettings = motion.GetChainSettings(chain);
            if (chainSettings.Strength <= AdvancedBodyScalingMotionWarpingTuning.GetRecommendedChainStrengthMax(chain))
                continue;

            advisories.Add($"{GetChainLabel(chain)} motion-warping strength exceeds the recommended range and may adapt less predictably.");
        }

        return advisories;
    }

    public static IReadOnlyList<AdvancedBodyScalingMotionWarpingEstimate> EstimateStaticSupport(
        IReadOnlyDictionary<string, BoneTransform> transforms,
        AdvancedBodyScalingSettings settings)
    {
        var output = new List<AdvancedBodyScalingMotionWarpingEstimate>();
        if (!settings.Enabled || settings.Mode == AdvancedBodyScalingMode.Manual || !settings.MotionWarping.Enabled || settings.MotionWarping.GlobalStrength <= 0f)
            return output;

        foreach (var definition in Definitions)
        {
            var chainSettings = settings.MotionWarping.GetChainSettings(definition.Chain);
            var strength = ComputeChainStrength(settings.MotionWarping, definition.Chain, chainSettings);
            if (!chainSettings.Enabled || strength <= 0.001f)
            {
                output.Add(new AdvancedBodyScalingMotionWarpingEstimate
                {
                    Chain = definition.Chain,
                    Label = definition.Label,
                    IsValid = false,
                    IsActive = false,
                    DriverSummary = "Chain disabled",
                    Description = definition.Description,
                    SkipReason = "This chain is disabled by the current motion-warping tuning.",
                });
                continue;
            }

            var scales = ResolveStaticChainScales(transforms, definition);
            if (scales.Count < definition.RequiredBones.Length)
            {
                output.Add(new AdvancedBodyScalingMotionWarpingEstimate
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
            var continuity = AverageNeighborContinuity(scales);
            var balanceDelta = ComputeBalanceDelta(scales, scales);
            var proportionDelta = averageScale - 1f;
            var stridePressure = MathF.Abs(proportionDelta) * settings.MotionWarping.StrideWarpStrength * GetStrideChainWeight(definition.Chain);
            var orientationPressure = (MathF.Abs(proportionDelta) * 0.55f + continuity * 0.45f) * settings.MotionWarping.OrientationWarpStrength * GetOrientationChainWeight(definition.Chain);
            var posturePressure = (MathF.Abs(balanceDelta) * 0.75f + MathF.Abs(proportionDelta) * 0.35f) * settings.MotionWarping.PostureWarpStrength * GetPostureChainWeight(definition.Chain);
            var activation = ComputeActivation(stridePressure + orientationPressure + posturePressure, settings.MotionWarping.MotionSafetyBias);
            var blendAmount = ComputeBlendAmount(settings.MotionWarping, strength, activation);
            var beforeRisk = Math.Clamp((MathF.Abs(proportionDelta) * 82f) + (continuity * 44f) + (MathF.Abs(balanceDelta) * 40f), 0f, 100f);
            var reductionFraction = EstimateRiskReduction(settings.MotionWarping, blendAmount);
            output.Add(new AdvancedBodyScalingMotionWarpingEstimate
            {
                Chain = definition.Chain,
                Label = definition.Label,
                IsValid = true,
                IsActive = blendAmount > 0.01f,
                Strength = strength,
                BlendAmount = blendAmount,
                ProportionDelta = proportionDelta,
                StridePressure = stridePressure,
                OrientationPressure = orientationPressure,
                PosturePressure = posturePressure,
                EstimatedRiskReduction = reductionFraction,
                EstimatedBeforeRisk = beforeRisk,
                EstimatedAfterRisk = beforeRisk * (1f - reductionFraction),
                DriverSummary = BuildStaticDriverSummary(proportionDelta, continuity, balanceDelta),
                Description = $"{definition.Description} This estimate assumes locomotion is actually happening; no target-based motion warping backend exists in this runtime.",
            });
        }

        return output
            .OrderByDescending(entry => entry.BlendAmount)
            .ThenByDescending(entry => entry.Strength)
            .ToList();
    }

    public static AdvancedBodyScalingMotionWarpingRegionEstimate EstimateRegionRiskReduction(
        IReadOnlyDictionary<string, BoneTransform> transforms,
        AdvancedBodyScalingSettings settings,
        AdvancedBodyScalingCorrectiveRegion region,
        float poseWeight)
    {
        const string defaultSummary = "No strong locomotion-warping response is expected for this region unless the actor is moving.";
        if (!RegionChainMap.TryGetValue(region, out var chains))
        {
            return new AdvancedBodyScalingMotionWarpingRegionEstimate
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
            return new AdvancedBodyScalingMotionWarpingRegionEstimate
            {
                Summary = defaultSummary,
            };
        }

        var blendedReduction = Math.Clamp(relevant.Average(estimate => estimate.EstimatedRiskReduction) * Math.Clamp(poseWeight, 0f, 1f), 0f, 0.24f);
        var top = relevant.Take(2).Select(estimate => estimate.Label).ToList();
        return new AdvancedBodyScalingMotionWarpingRegionEstimate
        {
            EstimatedRiskReduction = blendedReduction,
            Strength = relevant.Max(estimate => estimate.Strength),
            ChainLabels = top,
            Summary = $"Estimated motion-warping activity {relevant.Max(estimate => estimate.BlendAmount):0.00} via {string.Join(" and ", top)}, trimming about {(blendedReduction * 100f):0}% of this region's residual locomotion risk before the final Full-Body IK pass.",
        };
    }

    public static void EvaluateAndApply(
        Armature armature,
        CharacterBase* cBase,
        AdvancedBodyScalingSettings settings,
        bool profileOverridesActive,
        float deltaSeconds,
        AdvancedBodyScalingMotionWarpingContext? context,
        Dictionary<string, BoneTransform> smoothedCorrections,
        AdvancedBodyScalingMotionWarpingDebugState debugState)
    {
        var motion = settings.MotionWarping;
        debugState.Reset(motion.Enabled, profileOverridesActive, motion.MotionSafetyBias, motion.BlendBias, context);

        if (cBase == null || !settings.Enabled || settings.Mode == AdvancedBodyScalingMode.Manual || !motion.Enabled || motion.GlobalStrength <= 0f)
        {
            smoothedCorrections.Clear();
            debugState.FinalizeState(false, false, false, 0f, 0f, "Motion warping is disabled.");
            return;
        }

        var snapshot = BuildLiveSnapshot(armature, cBase);
        if (snapshot.Count == 0)
        {
            smoothedCorrections.Clear();
            debugState.FinalizeState(false, false, false, 0f, 0f, "No supported motion-warping chain data was available.");
            return;
        }

        context ??= new AdvancedBodyScalingMotionWarpingContext();
        var runtimeSolve = SolveRuntime(snapshot, settings, context);
        var temporalStabilityActive = UpdateSmoothedCorrections(smoothedCorrections, runtimeSolve.TargetCorrections, deltaSeconds, motion.MotionSafetyBias);
        ApplyCorrections(cBase, armature, smoothedCorrections);
        var summary = temporalStabilityActive
            ? $"{runtimeSolve.Summary} Temporal smoothing softened small frame-to-frame motion-warping changes."
            : runtimeSolve.Summary;

        debugState.Reset(motion.Enabled, profileOverridesActive, motion.MotionSafetyBias, motion.BlendBias, context);
        debugState.Chains.AddRange(runtimeSolve.DebugChains);
        debugState.FinalizeState(
            smoothedCorrections.Count > 0,
            runtimeSolve.LocksLimited,
            runtimeSolve.SafetyLimited || temporalStabilityActive,
            runtimeSolve.EstimatedBeforeRisk,
            runtimeSolve.EstimatedAfterRisk,
            summary);
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
        IReadOnlyDictionary<string, BoneSnapshot> snapshot,
        AdvancedBodyScalingSettings settings,
        AdvancedBodyScalingMotionWarpingContext context)
    {
        var motion = settings.MotionWarping;
        var resolvedChains = Definitions
            .Select(definition => ResolveLiveChain(snapshot, settings, definition, context))
            .ToDictionary(chain => chain.Definition.Chain);

        var debugChains = new List<AdvancedBodyScalingMotionWarpingChainDebugState>();
        var targetCorrections = new Dictionary<string, BoneTransform>(StringComparer.Ordinal);
        var locksLimited = false;
        var safetyLimited = false;

        foreach (var chain in resolvedChains.Values.Where(chain => !chain.IsValid))
        {
            locksLimited |= chain.LockLimited;
            debugChains.Add(new AdvancedBodyScalingMotionWarpingChainDebugState
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
                Summary = "No supported motion-warping chains were available for this actor or the current user locks.",
                LocksLimited = locksLimited,
                SafetyLimited = false,
            };
        }

        var locomotionDirection = NormalizeOrZero(ProjectPlanar(context.LocalDirection));
        var pelvisTranslation = Vector3.Zero;
        if (context.HasLocomotion && locomotionDirection.LengthSquared() > Epsilon)
        {
            pelvisTranslation = ComputePelvisTranslation(validChains, motion, locomotionDirection);
            if (pelvisTranslation.LengthSquared() > Epsilon)
            {
                targetCorrections["j_kosi"] = new BoneTransform
                {
                    Translation = pelvisTranslation,
                    PropagateTranslation = true,
                    PropagationFalloff = 0.97f,
                };
            }
        }

        foreach (var chain in validChains)
        {
            var safety = new SafetyAssessment();
            var beforeRisk = EstimateChainRisk(chain);
            var correctionMagnitude = 0f;

            if (!context.HasLocomotion || locomotionDirection.LengthSquared() <= Epsilon || chain.BlendAmount <= 0.001f)
            {
                debugChains.Add(new AdvancedBodyScalingMotionWarpingChainDebugState
                {
                    Chain = chain.Definition.Chain,
                    Label = chain.Definition.Label,
                    IsValid = true,
                    IsActive = false,
                    LockLimited = chain.LockLimited,
                    Strength = chain.Strength,
                    BlendAmount = chain.BlendAmount,
                    ProportionDelta = chain.ProportionDelta,
                    StridePressure = chain.StridePressure,
                    OrientationPressure = chain.OrientationPressure,
                    PosturePressure = chain.PosturePressure,
                    MovementAlignment = chain.MovementAlignment,
                    EstimatedBeforeRisk = beforeRisk,
                    EstimatedAfterRisk = beforeRisk,
                    DriverSummary = "Waiting for observed locomotion",
                    Description = $"{chain.Definition.Description} Target-based motion warping is unavailable in this runtime; this layer only engages when locomotion is observed.",
                });
                continue;
            }

            var orientationBasis = GetChainOrientationBasis(chain);
            var desiredPlanar = NormalizeOrFallback(Vector3.Lerp(
                orientationBasis,
                locomotionDirection,
                Math.Clamp(chain.OrientationPressure + (chain.BlendAmount * 0.20f), 0f, 0.60f)), orientationBasis);
            var pitchAxis = NormalizeOrZero(Vector3.Cross(Vector3.UnitY, desiredPlanar));
            var yawDelta = GetRotationBetween(NormalizeOrFallback(orientationBasis, desiredPlanar), desiredPlanar);

            if (chain.MovementAlignment < -0.12f)
            {
                yawDelta = Quaternion.Normalize(Quaternion.Slerp(Quaternion.Identity, yawDelta, 0.30f));
                safety.Clamped = true;
            }

            var pitchDegrees = GetChainPitchDegrees(chain, motion);
            var pitchDeltaWorld = pitchAxis.LengthSquared() <= Epsilon
                ? Quaternion.Identity
                : Quaternion.CreateFromAxisAngle(pitchAxis, DegreesToRadians(pitchDegrees));

            var signedLengthDelta = chain.EffectiveLength - chain.ActualLength;
            var strideDegrees = GetChainStrideDegrees(chain, signedLengthDelta, motion);

            for (var i = 0; i < chain.Bones.Count - 1; i++)
            {
                var bone = chain.Bones[i];
                var distribution = GetBoneDistribution(chain.Definition.Chain, i, chain.Bones.Count);
                var combinedWorld = Quaternion.Normalize(
                    Quaternion.Slerp(Quaternion.Identity, yawDelta, distribution) *
                    Quaternion.Slerp(Quaternion.Identity, pitchDeltaWorld, distribution));

                if (strideDegrees > 0.001f && (chain.Definition.Chain == AdvancedBodyScalingFullBodyIkChain.LeftLeg || chain.Definition.Chain == AdvancedBodyScalingFullBodyIkChain.RightLeg))
                {
                    var strideAxisWorld = NormalizeOrZero(Vector3.Cross(desiredPlanar, Vector3.UnitY));
                    if (strideAxisWorld.LengthSquared() > Epsilon)
                    {
                        var strideDeltaWorld = Quaternion.CreateFromAxisAngle(strideAxisWorld, DegreesToRadians(strideDegrees * distribution));
                        combinedWorld = Quaternion.Normalize(combinedWorld * strideDeltaWorld);
                    }
                }

                var localDelta = ToLocalDelta(bone.Rotation, combinedWorld);
                var maxDegrees = GetChainMaxRotationDegrees(chain.Definition.Chain, motion, chain.BlendAmount);
                var appliedDelta = ClampRotation(localDelta, chain.BlendAmount, maxDegrees);
                if (IsIdentity(appliedDelta))
                    continue;

                var angle = GetRotationAngleDegrees(appliedDelta);
                if (angle <= GetChainRotationDeadzoneDegrees(chain.Definition.Chain, motion))
                {
                    safety.Damped = true;
                    continue;
                }

                var budget = GetChainCorrectionBudgetDegrees(chain.Definition.Chain, motion, chain.BlendAmount);
                if ((correctionMagnitude + angle) > budget)
                {
                    var remaining = Math.Max(0f, budget - correctionMagnitude);
                    if (remaining <= 0.10f)
                    {
                        safety.Clamped = true;
                        continue;
                    }

                    var scale = remaining / Math.Max(angle, 0.001f);
                    appliedDelta = Quaternion.Normalize(Quaternion.Slerp(Quaternion.Identity, appliedDelta, scale));
                    angle = GetRotationAngleDegrees(appliedDelta);
                    safety.Clamped = true;
                }

                if (angle <= GetChainRotationDeadzoneDegrees(chain.Definition.Chain, motion))
                {
                    safety.Damped = true;
                    continue;
                }

                correctionMagnitude += angle;
                AddCorrection(
                    targetCorrections,
                    bone.Name,
                    Vector3.Zero,
                    appliedDelta,
                    propagateTranslation: false,
                    propagateRotation: true,
                    GetChainPropagationFalloff(chain.Definition.Chain));
            }

            if (correctionMagnitude <= GetChainTranslationDeadzone(chain.Definition.Chain, motion))
                safety.Damped = true;

            var effectiveStrength = chain.BlendAmount;
            if (safety.Clamped)
                effectiveStrength *= 0.60f;
            if (safety.Damped)
                effectiveStrength *= 0.82f;
            var afterRisk = beforeRisk * (1f - EstimateRiskReduction(motion, effectiveStrength));
            safetyLimited |= safety.SafetyLimited;

            debugChains.Add(new AdvancedBodyScalingMotionWarpingChainDebugState
            {
                Chain = chain.Definition.Chain,
                Label = chain.Definition.Label,
                IsValid = true,
                IsActive = correctionMagnitude > GetChainTranslationDeadzone(chain.Definition.Chain, motion),
                LockLimited = chain.LockLimited,
                Clamped = safety.Clamped,
                Rejected = safety.Rejected,
                Damped = safety.Damped,
                SafetyLimited = safety.SafetyLimited,
                Strength = chain.Strength,
                BlendAmount = chain.BlendAmount,
                ProportionDelta = chain.ProportionDelta,
                StridePressure = chain.StridePressure,
                OrientationPressure = chain.OrientationPressure,
                PosturePressure = chain.PosturePressure,
                MovementAlignment = chain.MovementAlignment,
                CorrectionMagnitude = correctionMagnitude,
                EstimatedBeforeRisk = beforeRisk,
                EstimatedAfterRisk = afterRisk,
                DriverSummary = BuildRuntimeDriverSummary(chain, safety),
                Description = $"{chain.Definition.Description} This implementation is locomotion-only; target-based motion warping is unavailable in the current runtime.",
                SkipReason = chain.HasScalePins ? "Scale pins are present on this chain; motion warping preserves those pinned scale axes and only applies pose adjustments." : string.Empty,
                SafetySummary = BuildSafetySummary(safety),
            });
        }

        var before = debugChains.Count == 0 ? 0f : debugChains.Where(chain => chain.IsValid).DefaultIfEmpty().Max(chain => chain?.EstimatedBeforeRisk ?? 0f);
        var after = debugChains.Count == 0 ? 0f : debugChains.Where(chain => chain.IsValid).DefaultIfEmpty().Max(chain => chain?.EstimatedAfterRisk ?? 0f);

        return new RuntimeSolveResult
        {
            TargetCorrections = targetCorrections,
            DebugChains = debugChains.OrderByDescending(chain => chain.BlendAmount).ThenByDescending(chain => chain.Strength).ToList(),
            EstimatedBeforeRisk = before,
            EstimatedAfterRisk = after,
            Summary = BuildRuntimeSummary(debugChains, context),
            LocksLimited = locksLimited || validChains.Any(chain => chain.LockLimited),
            SafetyLimited = safetyLimited,
        };
    }

    private static ResolvedChain ResolveLiveChain(
        IReadOnlyDictionary<string, BoneSnapshot> snapshot,
        AdvancedBodyScalingSettings settings,
        ChainDefinition definition,
        AdvancedBodyScalingMotionWarpingContext context)
    {
        var chainSettings = settings.MotionWarping.GetChainSettings(definition.Chain);
        var strength = ComputeChainStrength(settings.MotionWarping, definition.Chain, chainSettings);
        if (!chainSettings.Enabled || strength <= 0.001f)
        {
            return new ResolvedChain
            {
                Definition = definition,
                Bones = Array.Empty<BoneSnapshot>(),
                ActualSegmentLengths = Array.Empty<float>(),
                EffectiveSegmentLengths = Array.Empty<float>(),
                Strength = 0f,
                BlendAmount = 0f,
                ProportionDelta = 0f,
                StridePressure = 0f,
                OrientationPressure = 0f,
                PosturePressure = 0f,
                MovementAlignment = 0f,
                LocomotionAmount = 0f,
                HasScalePins = false,
                LockLimited = false,
                SkipReason = "This chain is disabled by the current motion-warping tuning.",
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
                Strength = strength,
                BlendAmount = 0f,
                ProportionDelta = 0f,
                StridePressure = 0f,
                OrientationPressure = 0f,
                PosturePressure = 0f,
                MovementAlignment = 0f,
                LocomotionAmount = 0f,
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
                Strength = strength,
                BlendAmount = 0f,
                ProportionDelta = 0f,
                StridePressure = 0f,
                OrientationPressure = 0f,
                PosturePressure = 0f,
                MovementAlignment = 0f,
                LocomotionAmount = 0f,
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
        var movementAlignment = EstimateMovementAlignment(definition.Chain, bones, context);
        var locomotionAmount = context.HasLocomotion ? context.LocomotionAmount : 0f;
        var stridePressure = MathF.Abs(proportionDelta) * settings.MotionWarping.StrideWarpStrength * GetStrideChainWeight(definition.Chain) * locomotionAmount;
        var orientationPressure = settings.MotionWarping.OrientationWarpStrength * GetOrientationChainWeight(definition.Chain) * locomotionAmount * context.TurnAmount;
        var posturePressure = ((MathF.Abs(balanceDelta) * 0.70f) + (MathF.Abs(proportionDelta) * 0.35f)) * settings.MotionWarping.PostureWarpStrength * GetPostureChainWeight(definition.Chain) * locomotionAmount;
        var activation = ComputeActivation(stridePressure + orientationPressure + posturePressure, settings.MotionWarping.MotionSafetyBias);
        var blendAmount = ComputeBlendAmount(settings.MotionWarping, strength, activation);

        return new ResolvedChain
        {
            Definition = definition,
            Bones = bones,
            ActualSegmentLengths = actualSegmentLengths,
            EffectiveSegmentLengths = effectiveSegmentLengths,
            Strength = strength,
            BlendAmount = blendAmount,
            ProportionDelta = proportionDelta,
            StridePressure = stridePressure,
            OrientationPressure = orientationPressure,
            PosturePressure = posturePressure,
            MovementAlignment = movementAlignment,
            LocomotionAmount = locomotionAmount,
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

    private static Vector3 ComputePelvisTranslation(
        IReadOnlyList<ResolvedChain> validChains,
        AdvancedBodyScalingMotionWarpingSettings settings,
        Vector3 locomotionDirection)
    {
        var legs = validChains
            .Where(chain => chain.Definition.Chain is AdvancedBodyScalingFullBodyIkChain.LeftLeg or AdvancedBodyScalingFullBodyIkChain.RightLeg)
            .ToList();
        if (legs.Count == 0)
            return Vector3.Zero;

        var averageLength = legs.Average(chain => MathF.Max(chain.ActualLength, 0.02f));
        var averageStride = legs.Average(chain => chain.StridePressure);
        var averageDelta = legs.Average(chain => chain.EffectiveLength - chain.ActualLength);
        var magnitude = averageDelta * averageStride * 0.18f;
        var maxDistance = averageLength * (0.006f + (settings.MaxCorrectionClamp * 0.020f));
        return locomotionDirection * Math.Clamp(magnitude, -maxDistance, maxDistance);
    }

    private static Vector3 GetChainOrientationBasis(ResolvedChain chain)
    {
        var basis = chain.Definition.Chain switch
        {
            AdvancedBodyScalingFullBodyIkChain.PelvisRoot => GetAverageBoneAxis(chain.Bones, Vector3.UnitZ),
            AdvancedBodyScalingFullBodyIkChain.Spine => GetAverageBoneAxis(chain.Bones, Vector3.UnitZ),
            AdvancedBodyScalingFullBodyIkChain.NeckHead => GetAverageBoneAxis(chain.Bones, Vector3.UnitZ),
            AdvancedBodyScalingFullBodyIkChain.LeftArm => chain.Bones[^1].Position - chain.Bones[0].Position,
            AdvancedBodyScalingFullBodyIkChain.RightArm => chain.Bones[^1].Position - chain.Bones[0].Position,
            AdvancedBodyScalingFullBodyIkChain.LeftLeg => chain.Bones[^1].Position - chain.Bones[0].Position,
            AdvancedBodyScalingFullBodyIkChain.RightLeg => chain.Bones[^1].Position - chain.Bones[0].Position,
            _ => Vector3.UnitZ,
        };

        return NormalizeOrFallback(ProjectPlanar(basis), Vector3.UnitZ);
    }

    private static float EstimateMovementAlignment(
        AdvancedBodyScalingFullBodyIkChain chain,
        IReadOnlyList<BoneSnapshot> bones,
        AdvancedBodyScalingMotionWarpingContext context)
    {
        var locomotionDirection = NormalizeOrZero(ProjectPlanar(context.LocalDirection));
        if (locomotionDirection.LengthSquared() <= Epsilon)
            return 0f;

        Vector3 chainDirection = chain switch
        {
            AdvancedBodyScalingFullBodyIkChain.PelvisRoot => NormalizeOrZero(ProjectPlanar(GetAverageBoneAxis(bones, Vector3.UnitZ))),
            AdvancedBodyScalingFullBodyIkChain.Spine => NormalizeOrZero(ProjectPlanar(GetAverageBoneAxis(bones, Vector3.UnitZ))),
            AdvancedBodyScalingFullBodyIkChain.NeckHead => NormalizeOrZero(ProjectPlanar(GetAverageBoneAxis(bones, Vector3.UnitZ))),
            _ => NormalizeOrZero(ProjectPlanar(bones[^1].Position - bones[0].Position)),
        };

        return chainDirection.LengthSquared() <= Epsilon ? 0f : Vector3.Dot(chainDirection, locomotionDirection);
    }

    private static float GetStrideChainWeight(AdvancedBodyScalingFullBodyIkChain chain)
        => chain switch
        {
            AdvancedBodyScalingFullBodyIkChain.LeftLeg => 1f,
            AdvancedBodyScalingFullBodyIkChain.RightLeg => 1f,
            AdvancedBodyScalingFullBodyIkChain.PelvisRoot => 0.42f,
            AdvancedBodyScalingFullBodyIkChain.Spine => 0.18f,
            _ => 0.06f,
        };

    private static float GetOrientationChainWeight(AdvancedBodyScalingFullBodyIkChain chain)
        => chain switch
        {
            AdvancedBodyScalingFullBodyIkChain.PelvisRoot => 0.80f,
            AdvancedBodyScalingFullBodyIkChain.Spine => 0.78f,
            AdvancedBodyScalingFullBodyIkChain.NeckHead => 0.30f,
            AdvancedBodyScalingFullBodyIkChain.LeftArm => 0.22f,
            AdvancedBodyScalingFullBodyIkChain.RightArm => 0.22f,
            AdvancedBodyScalingFullBodyIkChain.LeftLeg => 0.52f,
            AdvancedBodyScalingFullBodyIkChain.RightLeg => 0.52f,
            _ => 0.24f,
        };

    private static float GetPostureChainWeight(AdvancedBodyScalingFullBodyIkChain chain)
        => chain switch
        {
            AdvancedBodyScalingFullBodyIkChain.PelvisRoot => 0.68f,
            AdvancedBodyScalingFullBodyIkChain.Spine => 0.90f,
            AdvancedBodyScalingFullBodyIkChain.NeckHead => 0.30f,
            AdvancedBodyScalingFullBodyIkChain.LeftArm => 0.14f,
            AdvancedBodyScalingFullBodyIkChain.RightArm => 0.14f,
            AdvancedBodyScalingFullBodyIkChain.LeftLeg => 0.28f,
            AdvancedBodyScalingFullBodyIkChain.RightLeg => 0.28f,
            _ => 0.20f,
        };

    private static float ComputeChainStrength(
        AdvancedBodyScalingMotionWarpingSettings settings,
        AdvancedBodyScalingFullBodyIkChain chain,
        AdvancedBodyScalingMotionWarpingChainSettings chainSettings)
    {
        var regionalStrength = chain switch
        {
            AdvancedBodyScalingFullBodyIkChain.PelvisRoot => settings.PostureWarpStrength,
            AdvancedBodyScalingFullBodyIkChain.Spine => settings.PostureWarpStrength,
            AdvancedBodyScalingFullBodyIkChain.NeckHead => settings.PostureWarpStrength * 0.80f,
            AdvancedBodyScalingFullBodyIkChain.LeftArm => settings.OrientationWarpStrength * 0.75f,
            AdvancedBodyScalingFullBodyIkChain.RightArm => settings.OrientationWarpStrength * 0.75f,
            AdvancedBodyScalingFullBodyIkChain.LeftLeg => settings.StrideWarpStrength,
            AdvancedBodyScalingFullBodyIkChain.RightLeg => settings.StrideWarpStrength,
            _ => 0f,
        };

        var rawStrength = settings.GlobalStrength * regionalStrength * chainSettings.Strength;
        return CompressChainStrength(chain, rawStrength);
    }

    private static float ComputeBlendAmount(AdvancedBodyScalingMotionWarpingSettings settings, float strength, float activation)
    {
        var blend = activation * strength * Lerp(0.52f, 1f, settings.BlendBias);
        return Math.Clamp(blend, 0f, 0.58f);
    }

    private static float ComputeActivation(float pressure, float motionSafetyBias)
    {
        var deadzone = 0.018f + (motionSafetyBias * 0.040f);
        var full = 0.10f + (motionSafetyBias * 0.12f);
        return Remap(pressure, deadzone, full);
    }

    private static float EstimateRiskReduction(AdvancedBodyScalingMotionWarpingSettings settings, float blendAmount)
        => Math.Clamp(blendAmount * (0.17f + (settings.MaxCorrectionClamp * 0.20f)), 0f, 0.22f);

    private static float EstimateChainRisk(ResolvedChain chain)
        => Math.Clamp(
            (MathF.Abs(chain.ProportionDelta) * 84f) +
            (chain.StridePressure * 50f) +
            (chain.OrientationPressure * 42f) +
            (chain.PosturePressure * 40f),
            0f,
            100f);

    private static string BuildStaticDriverSummary(float proportionDelta, float continuity, float balanceDelta)
    {
        if (MathF.Abs(proportionDelta) <= 0.03f && continuity <= 0.05f && MathF.Abs(balanceDelta) <= 0.03f)
            return "Locomotion drift low";

        if (MathF.Abs(balanceDelta) > 0.06f)
            return $"Segment balance drift {balanceDelta:0.00}";

        if (continuity > 0.08f)
            return $"Chain continuity drift {continuity:0.00}";

        return $"Proportion delta {proportionDelta:+0.00;-0.00;0.00}";
    }

    private static string BuildRuntimeDriverSummary(ResolvedChain chain, SafetyAssessment safety)
    {
        var descriptors = new List<string>();
        if (MathF.Abs(chain.ProportionDelta) > 0.03f)
            descriptors.Add($"proportion delta {chain.ProportionDelta:+0.00;-0.00;0.00}");
        if (chain.StridePressure > 0.02f)
            descriptors.Add($"stride {chain.StridePressure:0.00}");
        if (chain.OrientationPressure > 0.02f)
            descriptors.Add($"orientation {chain.OrientationPressure:0.00}");
        if (chain.PosturePressure > 0.02f)
            descriptors.Add($"posture {chain.PosturePressure:0.00}");
        if (MathF.Abs(chain.MovementAlignment) > 0.10f)
            descriptors.Add($"alignment {chain.MovementAlignment:+0.00;-0.00;0.00}");
        if (chain.HasScalePins)
            descriptors.Add("scale pins preserved");
        if (safety.Clamped)
            descriptors.Add("safety clamp");
        if (safety.Damped)
            descriptors.Add("temporal damping");

        return descriptors.Count == 0 ? "Locomotion pressure low" : string.Join(", ", descriptors);
    }

    private static string BuildRuntimeSummary(
        IReadOnlyList<AdvancedBodyScalingMotionWarpingChainDebugState> chains,
        AdvancedBodyScalingMotionWarpingContext context)
    {
        var active = chains.Where(chain => chain.IsActive).OrderByDescending(chain => chain.BlendAmount).Take(3).ToList();
        if (active.Count == 0)
            return context.HasLocomotion
                ? "Motion warping observed locomotion, but no supported chain needed enough correction to justify a conservative warp."
                : "Motion warping is active only during observed locomotion. No target-based motion-warping backend is available in this runtime.";

        var labels = string.Join(", ", active.Select(chain => chain.Label));
        var safetyText = active.Any(chain => chain.SafetyLimited)
            ? $" Safety limiting was active on {string.Join(", ", active.Where(chain => chain.SafetyLimited).Select(chain => chain.Label).Distinct(StringComparer.Ordinal))}."
            : string.Empty;
        return $"Motion warping adapted {labels} during locomotion before the final Full-Body IK pass.{safetyText} This implementation is locomotion-only; target-based warp windows are unavailable in the current runtime.";
    }

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

    private static float GetBoneDistribution(AdvancedBodyScalingFullBodyIkChain chain, int boneIndex, int boneCount)
    {
        if (boneCount <= 1)
            return 1f;

        var normalized = 1f - (boneIndex / (float)Math.Max(1, boneCount - 1));
        return chain switch
        {
            AdvancedBodyScalingFullBodyIkChain.PelvisRoot => Lerp(0.55f, 1f, normalized),
            AdvancedBodyScalingFullBodyIkChain.Spine => Lerp(0.35f, 0.95f, normalized),
            AdvancedBodyScalingFullBodyIkChain.NeckHead => Lerp(0.25f, 0.70f, normalized),
            AdvancedBodyScalingFullBodyIkChain.LeftLeg => Lerp(0.30f, 1f, normalized),
            AdvancedBodyScalingFullBodyIkChain.RightLeg => Lerp(0.30f, 1f, normalized),
            _ => Lerp(0.25f, 0.70f, normalized),
        };
    }

    private static float GetChainPitchDegrees(ResolvedChain chain, AdvancedBodyScalingMotionWarpingSettings settings)
        => chain.Definition.Chain switch
        {
            AdvancedBodyScalingFullBodyIkChain.PelvisRoot => chain.PosturePressure * (3f + (settings.MaxCorrectionClamp * 6f)),
            AdvancedBodyScalingFullBodyIkChain.Spine => chain.PosturePressure * (4f + (settings.MaxCorrectionClamp * 7f)),
            AdvancedBodyScalingFullBodyIkChain.NeckHead => chain.PosturePressure * (2f + (settings.MaxCorrectionClamp * 4f)),
            _ => chain.PosturePressure * (1.2f + (settings.MaxCorrectionClamp * 2f)),
        };

    private static float GetChainStrideDegrees(ResolvedChain chain, float signedLengthDelta, AdvancedBodyScalingMotionWarpingSettings settings)
    {
        if (chain.Definition.Chain is not (AdvancedBodyScalingFullBodyIkChain.LeftLeg or AdvancedBodyScalingFullBodyIkChain.RightLeg))
            return 0f;

        return MathF.Abs(signedLengthDelta) * chain.StridePressure * (16f + (settings.MaxCorrectionClamp * 18f));
    }

    private static float GetChainMaxRotationDegrees(AdvancedBodyScalingFullBodyIkChain chain, AdvancedBodyScalingMotionWarpingSettings settings, float blendAmount)
    {
        var target = chain switch
        {
            AdvancedBodyScalingFullBodyIkChain.PelvisRoot => 5f,
            AdvancedBodyScalingFullBodyIkChain.Spine => 8f,
            AdvancedBodyScalingFullBodyIkChain.NeckHead => 7f,
            AdvancedBodyScalingFullBodyIkChain.LeftArm => 8f,
            AdvancedBodyScalingFullBodyIkChain.RightArm => 8f,
            AdvancedBodyScalingFullBodyIkChain.LeftLeg => 9f,
            AdvancedBodyScalingFullBodyIkChain.RightLeg => 9f,
            _ => 8f,
        };

        return Lerp(2f, target, Math.Clamp(settings.MaxCorrectionClamp * Math.Clamp(blendAmount + 0.10f, 0f, 1f), 0f, 1f));
    }

    private static float GetChainRotationDeadzoneDegrees(AdvancedBodyScalingFullBodyIkChain chain, AdvancedBodyScalingMotionWarpingSettings settings)
    {
        var baseline = chain switch
        {
            AdvancedBodyScalingFullBodyIkChain.PelvisRoot => 0.45f,
            AdvancedBodyScalingFullBodyIkChain.Spine => 0.55f,
            AdvancedBodyScalingFullBodyIkChain.NeckHead => 0.60f,
            AdvancedBodyScalingFullBodyIkChain.LeftLeg => 0.55f,
            AdvancedBodyScalingFullBodyIkChain.RightLeg => 0.55f,
            _ => 0.38f,
        };

        return baseline + (settings.MotionSafetyBias * 0.95f);
    }

    private static float GetChainTranslationDeadzone(AdvancedBodyScalingFullBodyIkChain chain, AdvancedBodyScalingMotionWarpingSettings settings)
        => chain switch
        {
            AdvancedBodyScalingFullBodyIkChain.PelvisRoot => 0.00045f + (settings.MotionSafetyBias * 0.0011f),
            _ => 0.00030f + (settings.MotionSafetyBias * 0.00085f),
        };

    private static float GetChainCorrectionBudgetDegrees(AdvancedBodyScalingFullBodyIkChain chain, AdvancedBodyScalingMotionWarpingSettings settings, float blendAmount)
    {
        var maxDegrees = chain switch
        {
            AdvancedBodyScalingFullBodyIkChain.PelvisRoot => 5f,
            AdvancedBodyScalingFullBodyIkChain.Spine => 8f,
            AdvancedBodyScalingFullBodyIkChain.NeckHead => 7f,
            AdvancedBodyScalingFullBodyIkChain.LeftLeg => 9f,
            AdvancedBodyScalingFullBodyIkChain.RightLeg => 9f,
            AdvancedBodyScalingFullBodyIkChain.LeftArm => 10f,
            AdvancedBodyScalingFullBodyIkChain.RightArm => 10f,
            _ => 8f,
        };

        return Lerp(2.5f, maxDegrees, Math.Clamp(settings.MaxCorrectionClamp * Math.Clamp(blendAmount + 0.10f, 0f, 1f), 0f, 1f));
    }

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
            AdvancedBodyScalingFullBodyIkChain.PelvisRoot => 0.09f,
            AdvancedBodyScalingFullBodyIkChain.Spine => 0.10f,
            AdvancedBodyScalingFullBodyIkChain.NeckHead => 0.08f,
            AdvancedBodyScalingFullBodyIkChain.LeftLeg => 0.12f,
            AdvancedBodyScalingFullBodyIkChain.RightLeg => 0.12f,
            _ => 0.14f,
        };

        var compression = chain switch
        {
            AdvancedBodyScalingFullBodyIkChain.PelvisRoot => 0.18f,
            AdvancedBodyScalingFullBodyIkChain.Spine => 0.20f,
            AdvancedBodyScalingFullBodyIkChain.NeckHead => 0.18f,
            AdvancedBodyScalingFullBodyIkChain.LeftLeg => 0.18f,
            AdvancedBodyScalingFullBodyIkChain.RightLeg => 0.18f,
            _ => 0.24f,
        };

        if (rawStrength <= softLimit)
            return rawStrength;

        return softLimit + ((rawStrength - softLimit) * compression);
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

    private static bool UpdateSmoothedCorrections(
        Dictionary<string, BoneTransform> smoothedCorrections,
        IReadOnlyDictionary<string, BoneTransform> targetCorrections,
        float deltaSeconds,
        float motionSafetyBias)
    {
        var sharpness = Lerp(14f, 4f, Math.Clamp(motionSafetyBias, 0f, 1f));
        var targetBlend = Lerp(0.76f, 0.50f, Math.Clamp(motionSafetyBias, 0f, 1f));
        var translationDeadzone = 0.00025f + (motionSafetyBias * 0.00090f);
        var rotationDeadzone = 0.20f + (motionSafetyBias * 0.90f);
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
            if (targetCorrections.ContainsKey(bone))
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

        correction.PropagationFalloff = Math.Clamp(MathF.Min(correction.PropagationFalloff, propagationFalloff), 0.82f, 0.99f);
    }

    private static string BuildSafetySummary(SafetyAssessment safety)
    {
        var descriptors = new List<string>();
        if (safety.Rejected)
            descriptors.Add("solve rejected");
        if (safety.Clamped)
            descriptors.Add("correction clamped");
        if (safety.Damped)
            descriptors.Add("damping/deadzone applied");

        return descriptors.Count == 0 ? string.Empty : string.Join(", ", descriptors);
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

    private static Vector3 GetAverageBoneAxis(IReadOnlyList<BoneSnapshot> bones, Vector3 localAxis)
    {
        var output = Vector3.Zero;
        foreach (var bone in bones)
            output += Vector3.Transform(localAxis, bone.Rotation);

        return output.LengthSquared() <= Epsilon ? localAxis : Vector3.Normalize(output);
    }

    private static Vector3 ProjectPlanar(Vector3 value)
        => new(value.X, 0f, value.Z);

    private static Vector3 NormalizeOrZero(Vector3 value)
        => value.LengthSquared() <= Epsilon ? Vector3.Zero : Vector3.Normalize(value);

    private static Vector3 NormalizeOrFallback(Vector3 value, Vector3 fallback)
        => value.LengthSquared() <= Epsilon ? NormalizeOrZero(fallback) : Vector3.Normalize(value);

    private static float Remap(float value, float start, float full)
    {
        if (full <= start)
            return value >= full ? 1f : 0f;

        return Math.Clamp((value - start) / (full - start), 0f, 1f);
    }

    private static float Lerp(float from, float to, float amount)
        => from + ((to - from) * Math.Clamp(amount, 0f, 1f));

    private static float DegreesToRadians(float degrees)
        => degrees * (MathF.PI / 180f);
}
