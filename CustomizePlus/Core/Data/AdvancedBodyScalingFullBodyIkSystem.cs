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

internal static unsafe class AdvancedBodyScalingFullBodyIkSystem
{
    private const float Epsilon = 0.0001f;

    private sealed record ChainDefinition(
        AdvancedBodyScalingFullBodyIkChain Chain,
        string Label,
        string Description,
        string[] RequiredBones,
        string[][] OptionalTailBones,
        bool UsesPelvisCompensation,
        bool UsesSpineShift);

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
        public required float Activation { get; init; }
        public required float LengthPressure { get; init; }
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
        public required IReadOnlyList<AdvancedBodyScalingFullBodyIkChainDebugState> DebugChains { get; init; }
        public required bool Converged { get; init; }
        public required int IterationsUsed { get; init; }
        public required float MaxResidualError { get; init; }
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
            "Translates and stabilizes the pelvis based on leg reach pressure so planted poses stay more coherent after scaling.",
            new[] { "j_kosi" },
            Array.Empty<string[]>(),
            UsesPelvisCompensation: true,
            UsesSpineShift: false),
        new(
            AdvancedBodyScalingFullBodyIkChain.Spine,
            "Spine",
            "Redistributes compensation through the supported spine chain so torso bends stay more coherent instead of collapsing into one joint.",
            new[] { "j_kosi", "j_sebo_a", "j_sebo_b", "j_sebo_c" },
            Array.Empty<string[]>(),
            UsesPelvisCompensation: true,
            UsesSpineShift: true),
        new(
            AdvancedBodyScalingFullBodyIkChain.NeckHead,
            "Neck / Head",
            "Keeps the neck and head better aligned after pelvis and spine compensation without fighting the source animation.",
            new[] { "j_sebo_c", "j_kubi", "j_kao" },
            Array.Empty<string[]>(),
            UsesPelvisCompensation: true,
            UsesSpineShift: true),
        new(
            AdvancedBodyScalingFullBodyIkChain.LeftArm,
            "Left Arm",
            "Preserves hand continuity and shoulder/chest coherence on the supported left arm chain.",
            new[] { "j_sako_l", "j_ude_a_l", "j_ude_b_l" },
            new[]
            {
                new[] { "n_hte_l" },
                new[] { "j_te_l" },
            },
            UsesPelvisCompensation: false,
            UsesSpineShift: true),
        new(
            AdvancedBodyScalingFullBodyIkChain.RightArm,
            "Right Arm",
            "Preserves hand continuity and shoulder/chest coherence on the supported right arm chain.",
            new[] { "j_sako_r", "j_ude_a_r", "j_ude_b_r" },
            new[]
            {
                new[] { "n_hte_r" },
                new[] { "j_te_r" },
            },
            UsesPelvisCompensation: false,
            UsesSpineShift: true),
        new(
            AdvancedBodyScalingFullBodyIkChain.LeftLeg,
            "Left Leg",
            "Keeps the supported left leg chain closer to planted motion by sharing scale-driven reach changes back into the pelvis.",
            new[] { "j_kosi", "j_asi_a_l", "j_asi_b_l", "j_asi_c_l", "j_asi_d_l" },
            Array.Empty<string[]>(),
            UsesPelvisCompensation: true,
            UsesSpineShift: false),
        new(
            AdvancedBodyScalingFullBodyIkChain.RightLeg,
            "Right Leg",
            "Keeps the supported right leg chain closer to planted motion by sharing scale-driven reach changes back into the pelvis.",
            new[] { "j_kosi", "j_asi_a_r", "j_asi_b_r", "j_asi_c_r", "j_asi_d_r" },
            Array.Empty<string[]>(),
            UsesPelvisCompensation: true,
            UsesSpineShift: false),
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
        if (!settings.Enabled || settings.Mode == AdvancedBodyScalingMode.Manual || !settings.FullBodyIk.Enabled)
            return advisories;

        var ik = settings.FullBodyIk;
        if (ik.GlobalStrength > AdvancedBodyScalingFullBodyIkTuning.RecommendedGlobalStrengthMax)
            advisories.Add("Global IK strength exceeds the recommended range and may cause visible over-solving or jitter.");
        if (ik.LegStrength > AdvancedBodyScalingFullBodyIkTuning.RecommendedLegStrengthMax)
            advisories.Add("Leg IK strength is above the recommended range; legs are intentionally safer at lower strengths.");
        if (ik.PelvisCompensationStrength > AdvancedBodyScalingFullBodyIkTuning.RecommendedPelvisStrengthMax)
            advisories.Add("Pelvis compensation is above the recommended range and can overdrive the rest of the body.");
        if (ik.SpineRedistributionStrength > AdvancedBodyScalingFullBodyIkTuning.RecommendedSpineStrengthMax)
            advisories.Add("Spine redistribution is above the recommended range and may amplify torso jitter.");
        if (ik.GroundingBias > AdvancedBodyScalingFullBodyIkTuning.RecommendedGroundingBiasMax)
            advisories.Add("Grounding bias is above the recommended range and may force unstable planted-feet behavior.");
        if (ik.MotionSafetyBias < AdvancedBodyScalingFullBodyIkTuning.RecommendedMotionSafetyBiasMin)
            advisories.Add("Motion-safety bias is below the recommended range; damping and deadzone protection may be too weak.");
        if (ik.MaxCorrectionClamp > AdvancedBodyScalingFullBodyIkTuning.RecommendedMaxCorrectionClampMax)
            advisories.Add("Max IK correction clamp is above the recommended range and may allow visibly bad corrections through.");

        foreach (var chain in GetOrderedChains())
        {
            var chainSettings = ik.GetChainSettings(chain);
            if (chainSettings.Strength <= AdvancedBodyScalingFullBodyIkTuning.GetRecommendedChainStrengthMax(chain))
                continue;

            advisories.Add($"{GetChainLabel(chain)} strength exceeds the recommended range and may solve less predictably.");
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
        AdvancedBodyScalingFullBodyIkDebugState debugState)
    {
        var ikSettings = settings.FullBodyIk;
        debugState.Reset(ikSettings.Enabled, profileOverridesActive, ikSettings.IterationCount, ikSettings.ConvergenceTolerance);

        if (cBase == null || !settings.Enabled || settings.Mode == AdvancedBodyScalingMode.Manual || !ikSettings.Enabled || ikSettings.GlobalStrength <= 0f)
        {
            smoothedCorrections.Clear();
            debugState.FinalizeState(false, false, false, false, 0f, 0f, 0f, "Full-body IK is disabled.");
            return;
        }

        var snapshot = BuildLiveSnapshot(armature, cBase);
        if (snapshot.Count == 0)
        {
            smoothedCorrections.Clear();
            debugState.FinalizeState(false, false, false, false, 0f, 0f, 0f, "No supported live chain data was available.");
            return;
        }

        var runtimeSolve = SolveRuntime(armature, snapshot, settings);
        var temporalStabilityActive = UpdateSmoothedCorrections(smoothedCorrections, runtimeSolve.TargetCorrections, deltaSeconds, ikSettings.MotionSafetyBias);
        ApplyCorrections(cBase, armature, smoothedCorrections);
        var summary = temporalStabilityActive
            ? $"{runtimeSolve.Summary} Temporal damping held or softened small frame-to-frame changes."
            : runtimeSolve.Summary;

        debugState.Reset(ikSettings.Enabled, profileOverridesActive, runtimeSolve.IterationsUsed, ikSettings.ConvergenceTolerance);
        debugState.Chains.AddRange(runtimeSolve.DebugChains);
        debugState.FinalizeState(
            smoothedCorrections.Count > 0,
            runtimeSolve.Converged,
            runtimeSolve.LocksLimited,
            runtimeSolve.SafetyLimited || temporalStabilityActive,
            runtimeSolve.EstimatedBeforeRisk,
            runtimeSolve.EstimatedAfterRisk,
            runtimeSolve.MaxResidualError,
            summary);
    }

    public static IReadOnlyList<AdvancedBodyScalingFullBodyIkEstimate> EstimateStaticSupport(
        IReadOnlyDictionary<string, BoneTransform> transforms,
        AdvancedBodyScalingSettings settings)
    {
        var output = new List<AdvancedBodyScalingFullBodyIkEstimate>();
        if (!settings.Enabled || settings.Mode == AdvancedBodyScalingMode.Manual || !settings.FullBodyIk.Enabled || settings.FullBodyIk.GlobalStrength <= 0f)
            return output;

        foreach (var definition in Definitions)
        {
            var chainSettings = settings.FullBodyIk.GetChainSettings(definition.Chain);
            var strength = ComputeChainStrength(settings.FullBodyIk, definition.Chain, chainSettings);
            if (!chainSettings.Enabled || strength <= 0.001f)
            {
                output.Add(new AdvancedBodyScalingFullBodyIkEstimate
                {
                    Chain = definition.Chain,
                    Label = definition.Label,
                    IsValid = false,
                    IsSolved = false,
                    DriverSummary = "Chain disabled",
                    Description = definition.Description,
                    SkipReason = "This chain is disabled by the current Full-Body IK tuning.",
                });
                continue;
            }

            var scales = ResolveStaticChainScales(transforms, definition);
            if (scales.Count < definition.RequiredBones.Length)
            {
                output.Add(new AdvancedBodyScalingFullBodyIkEstimate
                {
                    Chain = definition.Chain,
                    Label = definition.Label,
                    IsValid = false,
                    IsSolved = false,
                    DriverSummary = "Unsupported chain data",
                    Description = definition.Description,
                    SkipReason = "One or more supported bones for this chain are missing in the current template/profile data.",
                });
                continue;
            }

            var averageScale = scales.Average();
            var pressure = MathF.Abs(averageScale - 1f);
            var continuity = AverageNeighborContinuity(scales);
            var activation = ComputeActivation(pressure + (continuity * 0.6f), settings.FullBodyIk.MotionSafetyBias);
            var solveStrength = activation * strength;
            var beforeRisk = Math.Clamp((pressure * 85f) + (continuity * 55f), 0f, 100f);
            var reductionFraction = EstimateRiskReduction(settings.FullBodyIk, solveStrength);
            var afterRisk = beforeRisk * (1f - reductionFraction);
            output.Add(new AdvancedBodyScalingFullBodyIkEstimate
            {
                Chain = definition.Chain,
                Label = definition.Label,
                IsValid = true,
                IsSolved = solveStrength > 0.01f,
                Activation = activation,
                Strength = solveStrength,
                EstimatedRiskReduction = reductionFraction,
                EstimatedBeforeRisk = beforeRisk,
                EstimatedAfterRisk = afterRisk,
                DriverSummary = BuildStaticDriverSummary(averageScale, continuity),
                Description = definition.Description,
            });
        }

        return output
            .OrderByDescending(entry => entry.Strength)
            .ToList();
    }

    public static AdvancedBodyScalingFullBodyIkRegionEstimate EstimateRegionRiskReduction(
        IReadOnlyDictionary<string, BoneTransform> transforms,
        AdvancedBodyScalingSettings settings,
        AdvancedBodyScalingCorrectiveRegion region,
        float poseWeight)
    {
        const string defaultSummary = "No strong full-body IK response is expected for this region in this pose.";
        if (!RegionChainMap.TryGetValue(region, out var chains))
        {
            return new AdvancedBodyScalingFullBodyIkRegionEstimate
            {
                Summary = defaultSummary,
            };
        }

        var estimates = EstimateStaticSupport(transforms, settings)
            .ToDictionary(estimate => estimate.Chain, estimate => estimate);

        var relevant = chains
            .Where(chain => estimates.TryGetValue(chain, out var estimate) && estimate.IsValid && estimate.Strength > 0.01f)
            .Select(chain => estimates[chain])
            .OrderByDescending(estimate => estimate.Strength)
            .ToList();

        if (relevant.Count == 0)
        {
            return new AdvancedBodyScalingFullBodyIkRegionEstimate
            {
                Summary = defaultSummary,
            };
        }

        var blendedReduction = Math.Clamp(relevant.Average(estimate => estimate.EstimatedRiskReduction) * Math.Clamp(poseWeight, 0f, 1f), 0f, 0.35f);
        var top = relevant.Take(2).Select(estimate => estimate.Label).ToList();
        return new AdvancedBodyScalingFullBodyIkRegionEstimate
        {
            EstimatedRiskReduction = blendedReduction,
            Strength = relevant.Max(estimate => estimate.Strength),
            ChainLabels = top,
            Summary = $"Estimated full-body IK activity {relevant.Max(estimate => estimate.Strength):0.00} via {string.Join(" and ", top)}, trimming about {(blendedReduction * 100f):0}% of this region's residual risk.",
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
                AppliedTransform = armature.GetAppliedBoneTransform(bone.BoneName),
            };
        }

        return snapshot;
    }

    private static RuntimeSolveResult SolveRuntime(
        Armature armature,
        IReadOnlyDictionary<string, BoneSnapshot> snapshot,
        AdvancedBodyScalingSettings settings)
    {
        var ikSettings = settings.FullBodyIk;
        var resolvedChains = Definitions
            .Select(definition => ResolveLiveChain(armature, snapshot, settings, definition))
            .ToDictionary(chain => chain.Definition.Chain);

        var debugChains = new List<AdvancedBodyScalingFullBodyIkChainDebugState>();
        var targetCorrections = new Dictionary<string, BoneTransform>(StringComparer.Ordinal);
        var locksLimited = false;
        var safetyLimited = false;

        foreach (var chain in resolvedChains.Values.Where(chain => !chain.IsValid))
        {
            locksLimited |= chain.LockLimited;
            debugChains.Add(new AdvancedBodyScalingFullBodyIkChainDebugState
            {
                Chain = chain.Definition.Chain,
                Label = chain.Definition.Label,
                IsValid = false,
                IsSolved = false,
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
                Converged = false,
                IterationsUsed = 0,
                MaxResidualError = 0f,
                EstimatedBeforeRisk = 0f,
                EstimatedAfterRisk = 0f,
                Summary = "No supported full-body IK chains were available for this actor or the current user locks.",
                LocksLimited = locksLimited,
                SafetyLimited = false,
            };
        }

        var pelvisTranslation = Vector3.Zero;
        var upperSpineDelta = Vector3.Zero;
        var converged = false;
        var maxResidual = 0f;
        var iterationsUsed = 0;

        ResolvedChain? leftLeg = TryGetChain(resolvedChains, AdvancedBodyScalingFullBodyIkChain.LeftLeg);
        ResolvedChain? rightLeg = TryGetChain(resolvedChains, AdvancedBodyScalingFullBodyIkChain.RightLeg);
        ResolvedChain? spine = TryGetChain(resolvedChains, AdvancedBodyScalingFullBodyIkChain.Spine);
        ResolvedChain? neck = TryGetChain(resolvedChains, AdvancedBodyScalingFullBodyIkChain.NeckHead);
        ResolvedChain? leftArm = TryGetChain(resolvedChains, AdvancedBodyScalingFullBodyIkChain.LeftArm);
        ResolvedChain? rightArm = TryGetChain(resolvedChains, AdvancedBodyScalingFullBodyIkChain.RightArm);

        var solveResults = new Dictionary<AdvancedBodyScalingFullBodyIkChain, ChainSolveResult>();
        var chainResiduals = new Dictionary<AdvancedBodyScalingFullBodyIkChain, float>();
        var pelvisSafety = new SafetyAssessment();

        for (var iteration = 0; iteration < ikSettings.IterationCount; iteration++)
        {
            iterationsUsed = iteration + 1;

            var pelvisSolve = ComputePelvisShift(leftLeg, rightLeg, ikSettings);
            var targetPelvisShift = pelvisSolve.Shift;
            pelvisSafety = MergeSafety(pelvisSafety, pelvisSolve.Safety);
            pelvisTranslation = Vector3.Lerp(pelvisTranslation, targetPelvisShift, 0.35f + ((1f - ikSettings.MotionSafetyBias) * 0.25f));

            if (leftLeg != null)
            {
                var result = SolveChainPositions(leftLeg, leftLeg.Bones[0].Position + pelvisTranslation, leftLeg.Bones[^1].Position, ikSettings);
                solveResults[leftLeg.Definition.Chain] = result;
                chainResiduals[leftLeg.Definition.Chain] = result.ResidualError;
            }

            if (rightLeg != null)
            {
                var result = SolveChainPositions(rightLeg, rightLeg.Bones[0].Position + pelvisTranslation, rightLeg.Bones[^1].Position, ikSettings);
                solveResults[rightLeg.Definition.Chain] = result;
                chainResiduals[rightLeg.Definition.Chain] = result.ResidualError;
            }

            if (spine != null)
            {
                var headTarget = spine.Bones[^1].Position + (pelvisTranslation * (1f - (ikSettings.SpineRedistributionStrength * spine.Strength)));
                var result = SolveChainPositions(spine, spine.Bones[0].Position + pelvisTranslation, headTarget, ikSettings);
                solveResults[spine.Definition.Chain] = result;
                chainResiduals[spine.Definition.Chain] = result.ResidualError;

                var upperSpineIndex = Math.Min(3, result.Positions.Length - 1);
                upperSpineDelta = result.Positions[upperSpineIndex] - spine.Bones[upperSpineIndex].Position;
            }
            else
            {
                upperSpineDelta = pelvisTranslation * 0.5f;
            }

            if (neck != null)
            {
                var headTarget = neck.Bones[^1].Position + (pelvisTranslation * (1f - (ikSettings.HeadAlignmentStrength * neck.Strength)));
                var result = SolveChainPositions(neck, neck.Bones[0].Position + upperSpineDelta, headTarget, ikSettings);
                solveResults[neck.Definition.Chain] = result;
                chainResiduals[neck.Definition.Chain] = result.ResidualError;
            }

            if (leftArm != null)
            {
                var rootTarget = leftArm.Bones[0].Position + (upperSpineDelta * 0.80f);
                var result = SolveChainPositions(leftArm, rootTarget, leftArm.Bones[^1].Position, ikSettings);
                solveResults[leftArm.Definition.Chain] = result;
                chainResiduals[leftArm.Definition.Chain] = result.ResidualError;
            }

            if (rightArm != null)
            {
                var rootTarget = rightArm.Bones[0].Position + (upperSpineDelta * 0.80f);
                var result = SolveChainPositions(rightArm, rootTarget, rightArm.Bones[^1].Position, ikSettings);
                solveResults[rightArm.Definition.Chain] = result;
                chainResiduals[rightArm.Definition.Chain] = result.ResidualError;
            }

            maxResidual = chainResiduals.Count == 0 ? targetPelvisShift.Length() : MathF.Max(targetPelvisShift.Length(), chainResiduals.Values.Max());
            if (maxResidual <= ikSettings.ConvergenceTolerance)
            {
                converged = true;
                break;
            }
        }

        if (snapshot.TryGetValue("j_kosi", out var pelvisSnapshot))
        {
            var pelvisChain = resolvedChains[AdvancedBodyScalingFullBodyIkChain.PelvisRoot];
            var pelvisCorrection = BuildPelvisTranslationCorrection(pelvisSnapshot, pelvisTranslation, ikSettings, pelvisChain.Strength);
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
                solveResult = new ChainSolveResult
                {
                    Positions = chain.Bones.Select(bone => bone.Position).ToArray(),
                    ResidualError = 0f,
                    Safety = chainSafety,
                };

            var positions = solveResult.Positions;
            if (chain.Definition.Chain == AdvancedBodyScalingFullBodyIkChain.PelvisRoot)
                positions = chain.Bones.Select(bone => bone.Position).ToArray();

            if (positions.Length == 0)
                positions = chain.Bones.Select(bone => bone.Position).ToArray();

            var cumulativeMagnitude = 0f;
            var rotationDeadzone = GetChainRotationDeadzoneDegrees(chain.Definition.Chain, ikSettings);
            var chainBudget = GetChainCorrectionBudgetDegrees(chain.Definition.Chain, ikSettings, chain.Strength);
            var responseBlend = GetChainResponseBlend(chain.Definition.Chain, ikSettings);
            var propagationFalloff = GetChainPropagationFalloff(chain.Definition.Chain, ikSettings);

            for (var i = 0; i < chain.Bones.Count - 1; i++)
            {
                var bone = chain.Bones[i];
                var currentDirection = chain.Bones[i + 1].Position - bone.Position;
                var targetDirection = positions[i + 1] - positions[i];
                if (currentDirection.LengthSquared() <= Epsilon || targetDirection.LengthSquared() <= Epsilon)
                    continue;

                var worldDelta = GetRotationBetween(Vector3.Normalize(currentDirection), Vector3.Normalize(targetDirection));
                var localDelta = ToLocalDelta(bone.Rotation, worldDelta);
                var maxDegrees = GetChainMaxRotationDegrees(chain.Definition.Chain, ikSettings, chain.Strength);
                var appliedDelta = ClampRotation(localDelta, chain.Strength, maxDegrees);
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
                AddCorrection(targetCorrections, bone.Name, Vector3.Zero, appliedDelta, propagateTranslation: false, propagateRotation: true, propagationFalloff);
            }

            if (chain.Definition.Chain == AdvancedBodyScalingFullBodyIkChain.NeckHead)
                ApplyHeadAlignmentCorrection(targetCorrections, chain, ikSettings, snapshot, chainSafety);

            if (string.IsNullOrWhiteSpace(chainSafety.Summary))
                chainSafety.Summary = BuildSafetySummary(chainSafety);

            var residual = chainResiduals.TryGetValue(chain.Definition.Chain, out var value) ? value : solveResult.ResidualError;
            var beforeRisk = EstimateChainRisk(chain.LengthPressure, chain.Activation, 1f);
            var effectiveStrength = chain.Strength;
            if (chainSafety.Rejected)
                effectiveStrength *= 0.20f;
            else if (chainSafety.Clamped)
                effectiveStrength *= 0.65f;
            else if (chainSafety.Damped)
                effectiveStrength *= 0.82f;

            var correctionMagnitude = chain.Definition.Chain == AdvancedBodyScalingFullBodyIkChain.PelvisRoot
                ? pelvisTranslation.Length()
                : cumulativeMagnitude;
            var isSolved = chain.Definition.Chain == AdvancedBodyScalingFullBodyIkChain.PelvisRoot
                ? correctionMagnitude > GetChainTranslationDeadzone(chain.Definition.Chain, ikSettings)
                : correctionMagnitude > rotationDeadzone;
            var afterRisk = beforeRisk * (1f - EstimateRiskReduction(ikSettings, effectiveStrength));
            debugChains.Add(new AdvancedBodyScalingFullBodyIkChainDebugState
            {
                Chain = chain.Definition.Chain,
                Label = chain.Definition.Label,
                IsValid = true,
                IsSolved = isSolved,
                LockLimited = chain.LockLimited,
                Clamped = chainSafety.Clamped,
                Rejected = chainSafety.Rejected,
                Damped = chainSafety.Damped,
                SafetyLimited = chainSafety.SafetyLimited,
                Activation = chain.Activation,
                Strength = chain.Strength,
                CorrectionMagnitude = correctionMagnitude,
                ResidualError = residual,
                EstimatedBeforeRisk = beforeRisk,
                EstimatedAfterRisk = afterRisk,
                DriverSummary = BuildRuntimeDriverSummary(chain, residual, chainSafety),
                Description = chain.Definition.Description,
                SkipReason = chain.HasScalePins ? "Scale pins are present on this chain; IK preserves those pinned scale axes and only applies pose adjustments." : string.Empty,
                SafetySummary = chainSafety.Summary,
            });
        }

        locksLimited |= validChains.Any(chain => chain.LockLimited);
        safetyLimited = pelvisSafety.SafetyLimited || solveResults.Values.Any(result => result.Safety.SafetyLimited);

        var before = debugChains.Count == 0 ? 0f : debugChains.Where(chain => chain.IsValid).DefaultIfEmpty().Max(chain => chain?.EstimatedBeforeRisk ?? 0f);
        var after = debugChains.Count == 0 ? 0f : debugChains.Where(chain => chain.IsValid).DefaultIfEmpty().Max(chain => chain?.EstimatedAfterRisk ?? 0f);
        var summary = BuildRuntimeSummary(debugChains, converged, maxResidual);

        return new RuntimeSolveResult
        {
            TargetCorrections = targetCorrections,
            DebugChains = debugChains.OrderByDescending(chain => chain.Strength).ToList(),
            Converged = converged,
            IterationsUsed = iterationsUsed,
            MaxResidualError = maxResidual,
            EstimatedBeforeRisk = before,
            EstimatedAfterRisk = after,
            Summary = summary,
            LocksLimited = locksLimited,
            SafetyLimited = safetyLimited,
        };
    }

    private static ResolvedChain ResolveLiveChain(
        Armature armature,
        IReadOnlyDictionary<string, BoneSnapshot> snapshot,
        AdvancedBodyScalingSettings settings,
        ChainDefinition definition)
    {
        var chainSettings = settings.FullBodyIk.GetChainSettings(definition.Chain);
        var strength = ComputeChainStrength(settings.FullBodyIk, definition.Chain, chainSettings);
        if (!chainSettings.Enabled || strength <= 0.001f)
        {
            return new ResolvedChain
            {
                Definition = definition,
                Bones = Array.Empty<BoneSnapshot>(),
                ActualSegmentLengths = Array.Empty<float>(),
                EffectiveSegmentLengths = Array.Empty<float>(),
                Strength = 0f,
                Activation = 0f,
                LengthPressure = 0f,
                HasScalePins = false,
                LockLimited = false,
                SkipReason = "This chain is disabled by the current Full-Body IK tuning.",
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
                Activation = 0f,
                LengthPressure = 0f,
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
                Activation = 0f,
                LengthPressure = 0f,
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
        var lengthPressure = actualLength <= Epsilon ? 0f : MathF.Abs(effectiveLength - actualLength) / actualLength;
        var continuityPressure = AverageNeighborContinuity(bones.Select(bone => AdvancedBodyScalingPipeline.GetUniformScale(bone.Scale)).ToList());
        var activation = ComputeActivation(lengthPressure + (continuityPressure * 0.35f), settings.FullBodyIk.MotionSafetyBias);

        return new ResolvedChain
        {
            Definition = definition,
            Bones = bones,
            ActualSegmentLengths = actualSegmentLengths,
            EffectiveSegmentLengths = effectiveSegmentLengths,
            Strength = activation * strength,
            Activation = activation,
            LengthPressure = lengthPressure,
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
        AdvancedBodyScalingFullBodyIkSettings settings)
    {
        var positions = chain.Bones.Select(bone => bone.Position).ToArray();
        positions[0] = rootPosition;
        var lengths = chain.EffectiveSegmentLengths;
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
            var direction = Vector3.Normalize(targetPosition - rootPosition);
            for (var i = 1; i < positions.Length; i++)
                positions[i] = positions[i - 1] + (direction * lengths[i - 1]);

            return ApplyChainSafetyPass(chain, positions, rootPosition, targetPosition, Vector3.Distance(positions[^1], targetPosition), settings);
        }

        var baseRoot = rootPosition;
        for (var iteration = 0; iteration < Math.Max(2, settings.IterationCount); iteration++)
        {
            positions[^1] = targetPosition;
            for (var i = positions.Length - 2; i >= 0; i--)
            {
                var direction = positions[i] - positions[i + 1];
                if (direction.LengthSquared() <= Epsilon)
                    direction = Vector3.UnitY;

                direction = Vector3.Normalize(direction);
                positions[i] = positions[i + 1] + (direction * lengths[i]);
            }

            positions[0] = baseRoot;
            for (var i = 1; i < positions.Length; i++)
            {
                var direction = positions[i] - positions[i - 1];
                if (direction.LengthSquared() <= Epsilon)
                    direction = Vector3.UnitY;

                direction = Vector3.Normalize(direction);
                positions[i] = positions[i - 1] + (direction * lengths[i - 1]);
            }

            var residual = Vector3.Distance(positions[^1], targetPosition);
            if (residual <= settings.ConvergenceTolerance)
                return ApplyChainSafetyPass(chain, positions, rootPosition, targetPosition, residual, settings);
        }

        return ApplyChainSafetyPass(chain, positions, rootPosition, targetPosition, Vector3.Distance(positions[^1], targetPosition), settings);
    }

    private static ChainSolveResult ApplyChainSafetyPass(
        ResolvedChain chain,
        Vector3[] positions,
        Vector3 rootPosition,
        Vector3 targetPosition,
        float residual,
        AdvancedBodyScalingFullBodyIkSettings settings)
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
        var actualLength = MathF.Max(chain.ActualLength, 0.01f);
        var hardReject =
            worstAlignment < GetHardAlignmentThreshold(chain.Definition.Chain) ||
            midpointDeviation > (actualLength * GetHardMidpointDeviationFraction(chain.Definition.Chain)) ||
            residual > GetResidualThreshold(chain.Definition.Chain, settings, hardLimit: true);

        if (hardReject)
        {
            safety.Rejected = true;
            safety.Summary = BuildSafetySummary(safety, "Rejected implausible chain solve and fell back to a conservative reference pose.");
            var fallbackResidual = Vector3.Distance(referencePositions[^1], targetPosition);
            return new ChainSolveResult
            {
                Positions = referencePositions,
                ResidualError = fallbackResidual,
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
            keepFactor = MathF.Min(keepFactor, 1f - (0.70f * factor));
            safety.Clamped = true;
        }

        var softMidpoint = actualLength * GetSoftMidpointDeviationFraction(chain.Definition.Chain);
        var hardMidpoint = actualLength * GetHardMidpointDeviationFraction(chain.Definition.Chain);
        if (midpointDeviation > softMidpoint)
        {
            var factor = Remap(midpointDeviation, softMidpoint, hardMidpoint);
            keepFactor = MathF.Min(keepFactor, 1f - (0.75f * factor));
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
            var maxDisplacement = actualLength * GetMaxJointDisplacementFraction(chain.Definition.Chain);
            var deviation = positions[i] - referencePositions[i];
            if (deviation.LengthSquared() <= maxDisplacement * maxDisplacement)
                continue;

            positions[i] = referencePositions[i] + ClampVectorMagnitude(deviation, maxDisplacement);
            safety.Clamped = true;
            keepFactor = MathF.Min(keepFactor, 0.88f);
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

    private static (Vector3 Shift, SafetyAssessment Safety) ComputePelvisShift(ResolvedChain? leftLeg, ResolvedChain? rightLeg, AdvancedBodyScalingFullBodyIkSettings settings)
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
            if (disagreement < 0.30f)
            {
                shift *= 0.45f;
                safety.Damped = true;
                safety.Summary = "Pelvis compensation was damped because the leg chains disagreed about the safer direction.";
            }
        }

        var averageLength = new[] { leftLeg, rightLeg }
            .Where(chain => chain != null)
            .Select(chain => chain!.ActualLength)
            .DefaultIfEmpty(1f)
            .Average();
        var maxDistance = averageLength * (0.018f + (settings.MaxCorrectionClamp * 0.07f));
        if (shift.LengthSquared() <= Epsilon)
            return (Vector3.Zero, safety);

        var clamped = Vector3.Clamp(shift, new Vector3(-maxDistance), new Vector3(maxDistance));
        if (!clamped.IsApproximately(shift, 0.0001f))
        {
            safety.Clamped = true;
            if (string.IsNullOrWhiteSpace(safety.Summary))
                safety.Summary = "Pelvis compensation was clamped to keep root motion conservative.";
        }

        var deadzone = averageLength * (0.0012f + (settings.MotionSafetyBias * 0.0022f));
        if (clamped.LengthSquared() <= deadzone * deadzone)
        {
            if (clamped.LengthSquared() > Epsilon)
            {
                safety.Damped = true;
                if (string.IsNullOrWhiteSpace(safety.Summary))
                    safety.Summary = "Pelvis compensation stayed inside the motion deadzone and was suppressed for stability.";
            }

            return (Vector3.Zero, safety);
        }

        return (clamped, safety);
    }

    private static Vector3 ComputeLegRootSuggestion(ResolvedChain chain, AdvancedBodyScalingFullBodyIkSettings settings)
    {
        var root = chain.Bones[0].Position;
        var effector = chain.Bones[^1].Position;
        var direction = root - effector;
        if (direction.LengthSquared() <= Epsilon)
            return Vector3.Zero;

        direction = Vector3.Normalize(direction);
        var stretchRatio = Math.Clamp(Vector3.Distance(root, effector) / MathF.Max(chain.ActualLength, 0.01f), 0f, 1f);
        var lengthDelta = chain.EffectiveLength - chain.ActualLength;
        var deadzone = chain.ActualLength * (0.003f + (settings.MotionSafetyBias * 0.004f));
        if (MathF.Abs(lengthDelta) <= deadzone)
            return Vector3.Zero;

        var magnitude = lengthDelta
            * (0.18f + (0.62f * stretchRatio))
            * settings.PelvisCompensationStrength
            * settings.GroundingBias
            * chain.Strength;
        return direction * magnitude;
    }

    private static BoneTransform? BuildPelvisTranslationCorrection(
        BoneSnapshot pelvis,
        Vector3 pelvisTranslationModel,
        AdvancedBodyScalingFullBodyIkSettings settings,
        float strength)
    {
        if (pelvisTranslationModel.LengthSquared() <= Epsilon || strength <= 0.001f)
            return null;

        var maxDistance = 0.006f + (settings.MaxCorrectionClamp * 0.030f);
        var clampedModel = ClampVectorMagnitude(pelvisTranslationModel, maxDistance * Math.Clamp(strength, 0f, 1f));
        if (clampedModel.LengthSquared() <= Epsilon)
            return null;

        var localTranslation = Vector3.Transform(clampedModel, Quaternion.Inverse(pelvis.Rotation));
        return new BoneTransform
        {
            Translation = localTranslation,
            PropagateTranslation = true,
            PropagationFalloff = 0.96f,
        };
    }

    private static void ApplyHeadAlignmentCorrection(
        Dictionary<string, BoneTransform> corrections,
        ResolvedChain neckChain,
        AdvancedBodyScalingFullBodyIkSettings settings,
        IReadOnlyDictionary<string, BoneSnapshot> snapshot,
        SafetyAssessment safety)
    {
        if (neckChain.Bones.Count < 3 || settings.HeadAlignmentStrength <= 0.001f)
            return;

        var neckBone = neckChain.Bones[^2];
        var headBone = neckChain.Bones[^1];
        if (!snapshot.TryGetValue(neckBone.Name, out var neckSnapshot) || !snapshot.TryGetValue(headBone.Name, out var headSnapshot))
            return;

        var targetHeadRotation = headSnapshot.Rotation;
        var neckTarget = Quaternion.Inverse(neckSnapshot.Rotation) * targetHeadRotation;
        var headTarget = Quaternion.Inverse(headSnapshot.Rotation) * targetHeadRotation;

        var maxDegrees = GetChainMaxRotationDegrees(neckChain.Definition.Chain, settings, neckChain.Strength) * 0.55f;
        var neckDelta = ClampRotation(neckTarget, settings.HeadAlignmentStrength * neckChain.Strength * 0.45f, maxDegrees);
        var headDelta = ClampRotation(headTarget, settings.HeadAlignmentStrength * neckChain.Strength * 0.60f, maxDegrees);
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
        var sharpness = Lerp(18f, 5.5f, Math.Clamp(motionSafetyBias, 0f, 1f));
        var targetBlend = Lerp(0.82f, 0.56f, Math.Clamp(motionSafetyBias, 0f, 1f));
        var translationDeadzone = 0.00035f + (motionSafetyBias * 0.00110f);
        var rotationDeadzone = 0.20f + (motionSafetyBias * 0.95f);
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

                if (translationDelta > translationDeadzone || rotationDelta > rotationDeadzone)
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
        AdvancedBodyScalingFullBodyIkSettings settings,
        AdvancedBodyScalingFullBodyIkChain chain,
        AdvancedBodyScalingFullBodyIkChainSettings chainSettings)
    {
        var regionalStrength = chain switch
        {
            AdvancedBodyScalingFullBodyIkChain.PelvisRoot => settings.PelvisCompensationStrength,
            AdvancedBodyScalingFullBodyIkChain.Spine => settings.SpineRedistributionStrength,
            AdvancedBodyScalingFullBodyIkChain.NeckHead => settings.HeadAlignmentStrength,
            AdvancedBodyScalingFullBodyIkChain.LeftArm => settings.ArmStrength,
            AdvancedBodyScalingFullBodyIkChain.RightArm => settings.ArmStrength,
            AdvancedBodyScalingFullBodyIkChain.LeftLeg => settings.LegStrength,
            AdvancedBodyScalingFullBodyIkChain.RightLeg => settings.LegStrength,
            _ => 0f,
        };

        var rawStrength = settings.GlobalStrength * regionalStrength * chainSettings.Strength;
        return CompressChainStrength(chain, rawStrength);
    }

    private static float ComputeActivation(float pressure, float motionSafetyBias)
    {
        var deadzone = 0.045f + (motionSafetyBias * 0.055f);
        var full = 0.17f + (motionSafetyBias * 0.13f);
        return Remap(pressure, deadzone, full);
    }

    private static float EstimateRiskReduction(AdvancedBodyScalingFullBodyIkSettings settings, float strength)
        => Math.Clamp(strength * (0.14f + (settings.MaxCorrectionClamp * 0.18f)), 0f, 0.28f);

    private static float EstimateChainRisk(float lengthPressure, float activation, float multiplier)
        => Math.Clamp(((lengthPressure * 110f) + (activation * 35f)) * multiplier, 0f, 100f);

    private static float AverageNeighborContinuity(IReadOnlyList<float> values)
    {
        if (values.Count < 2)
            return 0f;

        var total = 0f;
        for (var i = 1; i < values.Count; i++)
            total += MathF.Abs(values[i] - values[i - 1]);

        return total / (values.Count - 1);
    }

    private static string BuildStaticDriverSummary(float averageScale, float continuity)
    {
        if (MathF.Abs(averageScale - 1f) <= 0.03f && continuity <= 0.05f)
            return "Scale pressure low";

        if (continuity > 0.10f)
            return $"Chain continuity drift {continuity:0.00}";

        return $"Average chain scale {averageScale:0.00}";
    }

    private static string BuildRuntimeDriverSummary(ResolvedChain chain, float residual, SafetyAssessment safety)
    {
        var descriptors = new List<string>();
        if (chain.LengthPressure > 0.03f)
            descriptors.Add($"reach pressure {chain.LengthPressure:0.00}");
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

        return descriptors.Count == 0 ? "Pose pressure low" : string.Join(", ", descriptors);
    }

    private static string BuildRuntimeSummary(IReadOnlyList<AdvancedBodyScalingFullBodyIkChainDebugState> chains, bool converged, float maxResidual)
    {
        var solved = chains.Where(chain => chain.IsSolved).OrderByDescending(chain => chain.Strength).Take(3).ToList();
        if (solved.Count == 0)
            return "Full-body IK found no chain with enough pressure to justify a conservative solve.";

        var labels = string.Join(", ", solved.Select(chain => chain.Label));
        var safetyLimited = solved.Where(chain => chain.SafetyLimited).Select(chain => chain.Label).Distinct(StringComparer.Ordinal).ToList();
        var safetyText = safetyLimited.Count == 0
            ? string.Empty
            : $" Safety limiting was active on {string.Join(", ", safetyLimited)}.";
        return converged
            ? $"Full-body IK converged on {labels}; max residual {maxResidual:0.000}.{safetyText}"
            : $"Full-body IK remained conservative on {labels}; max residual {maxResidual:0.000} after the allotted iterations.{safetyText}";
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
            0.80f,
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

    private static float GetChainMaxRotationDegrees(AdvancedBodyScalingFullBodyIkChain chain, AdvancedBodyScalingFullBodyIkSettings settings, float strength)
    {
        var target = chain switch
        {
            AdvancedBodyScalingFullBodyIkChain.PelvisRoot => 8f,
            AdvancedBodyScalingFullBodyIkChain.Spine => 14f,
            AdvancedBodyScalingFullBodyIkChain.NeckHead => 12f,
            AdvancedBodyScalingFullBodyIkChain.LeftArm => 18f,
            AdvancedBodyScalingFullBodyIkChain.RightArm => 18f,
            AdvancedBodyScalingFullBodyIkChain.LeftLeg => 12f,
            AdvancedBodyScalingFullBodyIkChain.RightLeg => 12f,
            _ => 14f,
        };

        return Lerp(3f, target, Math.Clamp(settings.MaxCorrectionClamp * Math.Clamp(strength, 0f, 1f), 0f, 1f));
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

    private static float GetSoftAlignmentThreshold(AdvancedBodyScalingFullBodyIkChain chain)
        => chain switch
        {
            AdvancedBodyScalingFullBodyIkChain.LeftLeg => 0.45f,
            AdvancedBodyScalingFullBodyIkChain.RightLeg => 0.45f,
            AdvancedBodyScalingFullBodyIkChain.Spine => 0.30f,
            AdvancedBodyScalingFullBodyIkChain.NeckHead => 0.35f,
            AdvancedBodyScalingFullBodyIkChain.LeftArm => 0.20f,
            AdvancedBodyScalingFullBodyIkChain.RightArm => 0.20f,
            _ => -1f,
        };

    private static float GetHardAlignmentThreshold(AdvancedBodyScalingFullBodyIkChain chain)
        => chain switch
        {
            AdvancedBodyScalingFullBodyIkChain.LeftLeg => 0.12f,
            AdvancedBodyScalingFullBodyIkChain.RightLeg => 0.12f,
            AdvancedBodyScalingFullBodyIkChain.Spine => 0.05f,
            AdvancedBodyScalingFullBodyIkChain.NeckHead => 0.08f,
            AdvancedBodyScalingFullBodyIkChain.LeftArm => -0.05f,
            AdvancedBodyScalingFullBodyIkChain.RightArm => -0.05f,
            _ => -1f,
        };

    private static float GetSoftMidpointDeviationFraction(AdvancedBodyScalingFullBodyIkChain chain)
        => chain switch
        {
            AdvancedBodyScalingFullBodyIkChain.LeftLeg => 0.08f,
            AdvancedBodyScalingFullBodyIkChain.RightLeg => 0.08f,
            AdvancedBodyScalingFullBodyIkChain.Spine => 0.07f,
            AdvancedBodyScalingFullBodyIkChain.NeckHead => 0.06f,
            AdvancedBodyScalingFullBodyIkChain.LeftArm => 0.10f,
            AdvancedBodyScalingFullBodyIkChain.RightArm => 0.10f,
            _ => 0.08f,
        };

    private static float GetHardMidpointDeviationFraction(AdvancedBodyScalingFullBodyIkChain chain)
        => chain switch
        {
            AdvancedBodyScalingFullBodyIkChain.LeftLeg => 0.15f,
            AdvancedBodyScalingFullBodyIkChain.RightLeg => 0.15f,
            AdvancedBodyScalingFullBodyIkChain.Spine => 0.12f,
            AdvancedBodyScalingFullBodyIkChain.NeckHead => 0.10f,
            AdvancedBodyScalingFullBodyIkChain.LeftArm => 0.18f,
            AdvancedBodyScalingFullBodyIkChain.RightArm => 0.18f,
            _ => 0.15f,
        };

    private static float GetMaxJointDisplacementFraction(AdvancedBodyScalingFullBodyIkChain chain)
        => chain switch
        {
            AdvancedBodyScalingFullBodyIkChain.LeftLeg => 0.09f,
            AdvancedBodyScalingFullBodyIkChain.RightLeg => 0.09f,
            AdvancedBodyScalingFullBodyIkChain.Spine => 0.08f,
            AdvancedBodyScalingFullBodyIkChain.NeckHead => 0.06f,
            AdvancedBodyScalingFullBodyIkChain.LeftArm => 0.12f,
            AdvancedBodyScalingFullBodyIkChain.RightArm => 0.12f,
            _ => 0.08f,
        };

    private static float GetResidualThreshold(AdvancedBodyScalingFullBodyIkChain chain, AdvancedBodyScalingFullBodyIkSettings settings, bool hardLimit)
    {
        var scale = hardLimit ? 1.8f : 1.1f;
        var multiplier = chain switch
        {
            AdvancedBodyScalingFullBodyIkChain.LeftLeg => 0.90f,
            AdvancedBodyScalingFullBodyIkChain.RightLeg => 0.90f,
            AdvancedBodyScalingFullBodyIkChain.NeckHead => 0.80f,
            _ => 1f,
        };

        return settings.ConvergenceTolerance * scale * multiplier;
    }

    private static float GetChainRotationDeadzoneDegrees(AdvancedBodyScalingFullBodyIkChain chain, AdvancedBodyScalingFullBodyIkSettings settings)
    {
        var baseline = chain switch
        {
            AdvancedBodyScalingFullBodyIkChain.PelvisRoot => 0.35f,
            AdvancedBodyScalingFullBodyIkChain.Spine => 0.45f,
            AdvancedBodyScalingFullBodyIkChain.NeckHead => 0.55f,
            AdvancedBodyScalingFullBodyIkChain.LeftLeg => 0.55f,
            AdvancedBodyScalingFullBodyIkChain.RightLeg => 0.55f,
            _ => 0.35f,
        };

        return baseline + (settings.MotionSafetyBias * 0.85f);
    }

    private static float GetChainTranslationDeadzone(AdvancedBodyScalingFullBodyIkChain chain, AdvancedBodyScalingFullBodyIkSettings settings)
        => chain switch
        {
            AdvancedBodyScalingFullBodyIkChain.PelvisRoot => 0.0006f + (settings.MotionSafetyBias * 0.0014f),
            _ => 0.0004f + (settings.MotionSafetyBias * 0.0010f),
        };

    private static float GetChainCorrectionBudgetDegrees(AdvancedBodyScalingFullBodyIkChain chain, AdvancedBodyScalingFullBodyIkSettings settings, float strength)
    {
        var maxDegrees = chain switch
        {
            AdvancedBodyScalingFullBodyIkChain.PelvisRoot => 8f,
            AdvancedBodyScalingFullBodyIkChain.Spine => 14f,
            AdvancedBodyScalingFullBodyIkChain.NeckHead => 12f,
            AdvancedBodyScalingFullBodyIkChain.LeftLeg => 18f,
            AdvancedBodyScalingFullBodyIkChain.RightLeg => 18f,
            AdvancedBodyScalingFullBodyIkChain.LeftArm => 26f,
            AdvancedBodyScalingFullBodyIkChain.RightArm => 26f,
            _ => 18f,
        };

        return Lerp(4f, maxDegrees, Math.Clamp(settings.MaxCorrectionClamp * Math.Clamp(strength, 0f, 1f), 0f, 1f));
    }

    private static float GetChainResponseBlend(AdvancedBodyScalingFullBodyIkChain chain, AdvancedBodyScalingFullBodyIkSettings settings)
        => chain switch
        {
            AdvancedBodyScalingFullBodyIkChain.LeftLeg => Lerp(0.78f, 0.52f, settings.MotionSafetyBias),
            AdvancedBodyScalingFullBodyIkChain.RightLeg => Lerp(0.78f, 0.52f, settings.MotionSafetyBias),
            AdvancedBodyScalingFullBodyIkChain.PelvisRoot => Lerp(0.80f, 0.55f, settings.MotionSafetyBias),
            AdvancedBodyScalingFullBodyIkChain.Spine => Lerp(0.84f, 0.60f, settings.MotionSafetyBias),
            AdvancedBodyScalingFullBodyIkChain.NeckHead => Lerp(0.82f, 0.58f, settings.MotionSafetyBias),
            _ => Lerp(0.90f, 0.68f, settings.MotionSafetyBias),
        };

    private static float GetChainPropagationFalloff(AdvancedBodyScalingFullBodyIkChain chain, AdvancedBodyScalingFullBodyIkSettings settings)
        => chain switch
        {
            AdvancedBodyScalingFullBodyIkChain.LeftLeg => 0.94f + ((1f - settings.MotionSafetyBias) * 0.03f),
            AdvancedBodyScalingFullBodyIkChain.RightLeg => 0.94f + ((1f - settings.MotionSafetyBias) * 0.03f),
            AdvancedBodyScalingFullBodyIkChain.Spine => 0.95f + ((1f - settings.MotionSafetyBias) * 0.02f),
            AdvancedBodyScalingFullBodyIkChain.NeckHead => 0.97f,
            _ => 0.91f + ((1f - settings.MotionSafetyBias) * 0.05f),
        };

    private static float CompressChainStrength(AdvancedBodyScalingFullBodyIkChain chain, float rawStrength)
    {
        var softLimit = chain switch
        {
            AdvancedBodyScalingFullBodyIkChain.PelvisRoot => 0.14f,
            AdvancedBodyScalingFullBodyIkChain.Spine => 0.16f,
            AdvancedBodyScalingFullBodyIkChain.NeckHead => 0.12f,
            AdvancedBodyScalingFullBodyIkChain.LeftLeg => 0.18f,
            AdvancedBodyScalingFullBodyIkChain.RightLeg => 0.18f,
            _ => 0.22f,
        };

        var compression = chain switch
        {
            AdvancedBodyScalingFullBodyIkChain.PelvisRoot => 0.22f,
            AdvancedBodyScalingFullBodyIkChain.Spine => 0.24f,
            AdvancedBodyScalingFullBodyIkChain.NeckHead => 0.26f,
            AdvancedBodyScalingFullBodyIkChain.LeftLeg => 0.24f,
            AdvancedBodyScalingFullBodyIkChain.RightLeg => 0.24f,
            _ => 0.32f,
        };

        if (rawStrength <= softLimit)
            return rawStrength;

        return softLimit + ((rawStrength - softLimit) * compression);
    }

    private static SafetyAssessment MergeSafety(SafetyAssessment current, SafetyAssessment next)
    {
        current.Clamped |= next.Clamped;
        current.Rejected |= next.Rejected;
        current.Damped |= next.Damped;
        if (!string.IsNullOrWhiteSpace(next.Summary))
            current.Summary = next.Summary;

        return current;
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

    private static float Remap(float value, float start, float full)
    {
        if (full <= start)
            return value >= full ? 1f : 0f;

        return Math.Clamp((value - start) / (full - start), 0f, 1f);
    }

    private static float Lerp(float from, float to, float amount)
        => from + ((to - from) * Math.Clamp(amount, 0f, 1f));
}
