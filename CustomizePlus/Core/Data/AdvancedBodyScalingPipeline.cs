// Copyright (c) Customize+.
// Licensed under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using CustomizePlus.Core.Extensions;

namespace CustomizePlus.Core.Data;

internal static class AdvancedBodyScalingPipeline
{
    private const float Epsilon = 0.0001f;
    private const float DefaultSurfaceBalanceThreshold = 0.15f;
    private const float DefaultMassRedistributionThreshold = 0.25f;
    private const float DefaultCurveThreshold = 0.12f;
    private const float DefaultPropagationFalloff = 0.6f;
    private const int DefaultPropagationDepth = 2;
    private const float MaxNeckLengthReduction = 0.15f;
    private const float MinAutoScale = 0.25f;
    private const float MaxAutoScale = 3.0f;

    private static readonly HashSet<BoneData.BoneFamily> BodyFamilies = new()
    {
        BoneData.BoneFamily.Spine,
        BoneData.BoneFamily.Chest,
        BoneData.BoneFamily.Groin,
        BoneData.BoneFamily.Arms,
        BoneData.BoneFamily.Hands,
        BoneData.BoneFamily.Legs,
        BoneData.BoneFamily.Feet,
        BoneData.BoneFamily.Tail
    };

    private static readonly string[] NeckBones = { "j_kubi" };
    private static readonly string[] UpperSpineBones = { "j_sebo_c" };
    private static readonly string[] ClavicleBones = { "j_sako_l", "j_sako_r" };
    private static readonly string[] ShoulderRootBones = { "n_hkata_l", "n_hkata_r" };

    private static readonly HashSet<string> NeckShoulderBones = new(StringComparer.Ordinal)
    {
        "j_sebo_c",
        "j_kubi",
        "j_sako_l",
        "j_sako_r",
        "n_hkata_l",
        "n_hkata_r"
    };

    private readonly record struct ModeTuning(
        float InfluenceStrength,
        float SurfaceSmoothing,
        float MassRedistribution,
        float GuardrailStrength,
        float CurveStrength,
        float NaturalizationMultiplier,
        int PropagationDepth);

    public static Dictionary<string, BoneTransform> Apply(
        IReadOnlyDictionary<string, BoneTransform> userTransforms,
        AdvancedBodyScalingSettings settings,
        AdvancedBodyScalingDebugReport? debug = null)
    {
        var output = new Dictionary<string, BoneTransform>(StringComparer.Ordinal);
        foreach (var kvp in userTransforms)
            output[kvp.Key] = kvp.Value.DeepCopy();

        if (!settings.Enabled || settings.Mode == AdvancedBodyScalingMode.Manual)
            return output;

        var tuning = GetTuning(settings.Mode);
        var surfaceStrength = tuning.SurfaceSmoothing * settings.SurfaceBalancingStrength;
        var massStrength = tuning.MassRedistribution * settings.MassRedistributionStrength;
        var guardrailStrength = tuning.GuardrailStrength * GetGuardrailMultiplier(settings.GuardrailMode);
        var poseValidationStrength = tuning.GuardrailStrength * GetPoseValidationMultiplier(settings.PoseValidationMode);
        var sources = new HashSet<string>(StringComparer.Ordinal);
        var relevantBones = new HashSet<string>(StringComparer.Ordinal);

        foreach (var kvp in userTransforms)
        {
            if (!IsBodyBone(kvp.Key))
                continue;

            relevantBones.Add(kvp.Key);

            var uniformScale = GetUniformScale(kvp.Value.Scaling);
            if (kvp.Value.LockState == BoneLockState.Priority || MathF.Abs(uniformScale - 1f) > Epsilon)
                sources.Add(kvp.Key);
        }

        if (sources.Count == 0)
        {
            ApplyNeckCompensation(output, userTransforms, settings);
            return output;
        }

        foreach (var source in sources)
            ExpandNeighbors(source, tuning.PropagationDepth, relevantBones);

        if (debug != null)
        {
            debug.ActiveCurveChains = CurveChains.All
                .Select(chain => chain.Where(relevantBones.Contains).ToList())
                .Where(chain => chain.Count >= 2)
                .Select(chain => (IReadOnlyList<string>)chain)
                .ToList();
        }

        var uniformScales = new Dictionary<string, float>(StringComparer.Ordinal);
        var lockStates = new Dictionary<string, BoneLockState>(StringComparer.Ordinal);
        var regionProfiles = new Dictionary<string, AdvancedBodyScalingRegionProfile>(StringComparer.Ordinal);

        foreach (var bone in relevantBones)
        {
            if (userTransforms.TryGetValue(bone, out var transform))
            {
                uniformScales[bone] = GetUniformScale(transform.Scaling);
                lockStates[bone] = transform.LockState;
            }
            else
            {
                uniformScales[bone] = 1f;
                lockStates[bone] = BoneLockState.Unlocked;
            }

            regionProfiles[bone] = settings.GetRegionProfile(ResolveRegion(bone));
        }

        var originalScales = uniformScales.ToDictionary(kvp => kvp.Key, kvp => kvp.Value, StringComparer.Ordinal);

        if (debug != null)
            AdvancedBodyScalingDebugReport.CopyScales(uniformScales, debug.InitialScales);

        ApplyInfluencePropagation(uniformScales, lockStates, regionProfiles, sources, tuning);
        if (debug != null)
        {
            AdvancedBodyScalingDebugReport.CopyScales(uniformScales, debug.AfterPropagation);
            debug.PropagationDeltas.Clear();
            foreach (var kvp in uniformScales)
                debug.PropagationDeltas[kvp.Key] = kvp.Value - originalScales[kvp.Key];
        }

        ApplySurfaceBalancing(uniformScales, lockStates, regionProfiles, surfaceStrength, DefaultSurfaceBalanceThreshold);
        if (debug != null)
            AdvancedBodyScalingDebugReport.CopyScales(uniformScales, debug.AfterSurfaceBalancing);

        ApplyMassRedistribution(uniformScales, lockStates, regionProfiles, massStrength, DefaultMassRedistributionThreshold);
        if (debug != null)
            AdvancedBodyScalingDebugReport.CopyScales(uniformScales, debug.AfterMassRedistribution);

        ApplyGuardrails(uniformScales, lockStates, regionProfiles, guardrailStrength, debug);
        if (debug != null)
            AdvancedBodyScalingDebugReport.CopyScales(uniformScales, debug.AfterGuardrails);

        ApplyCurveSolver(uniformScales, lockStates, regionProfiles, tuning.CurveStrength, DefaultCurveThreshold);
        if (debug != null)
            AdvancedBodyScalingDebugReport.CopyScales(uniformScales, debug.AfterCurveSmoothing);

        ApplyPoseAwareValidation(uniformScales, lockStates, regionProfiles, poseValidationStrength, debug);
        if (debug != null)
            AdvancedBodyScalingDebugReport.CopyScales(uniformScales, debug.AfterPoseValidation);

        var naturalizationStrength = Math.Clamp(
            settings.NaturalizationStrength * tuning.NaturalizationMultiplier,
            0f,
            1f);

        foreach (var kvp in uniformScales.ToList())
        {
            var bone = kvp.Key;
            var original = originalScales[bone];
            var finalUniform = kvp.Value;
            var regionProfile = GetRegionProfile(regionProfiles, bone);

            if (lockStates.TryGetValue(bone, out var lockState) && lockState != BoneLockState.Unlocked)
                finalUniform = original;
            else if (!regionProfile.AllowNaturalization)
                finalUniform = original;
            else
                finalUniform = Lerp(original, finalUniform, naturalizationStrength * regionProfile.NaturalizationMultiplier);

            var isUserBone = userTransforms.ContainsKey(bone);
            if (!isUserBone || MathF.Abs(finalUniform - original) > Epsilon)
                finalUniform = ClampScale(finalUniform);

            if (output.TryGetValue(bone, out var transform))
            {
                transform.Scaling = ApplyUniformScale(transform.Scaling, finalUniform);
                output[bone] = transform;
            }
            else
            {
                if (MathF.Abs(finalUniform - 1f) <= 0.0005f)
                    continue;

                output[bone] = new BoneTransform
                {
                    Scaling = new Vector3(finalUniform),
                };
            }

            if (debug != null)
                debug.FinalScales[bone] = finalUniform;
        }

        ApplyNeckCompensation(output, userTransforms, settings);

        return output;
    }

    internal static BodyAnalysisResult Analyze(
        IReadOnlyDictionary<string, BoneTransform> userTransforms,
        AdvancedBodyScalingSettings settings)
    {
        var relevantBones = userTransforms.Keys.Where(IsBodyBone).ToHashSet(StringComparer.Ordinal);
        foreach (var bone in relevantBones.ToList())
            ExpandNeighbors(bone, DefaultPropagationDepth, relevantBones);

        var uniformScales = new Dictionary<string, float>(StringComparer.Ordinal);
        foreach (var bone in relevantBones)
        {
            if (userTransforms.TryGetValue(bone, out var transform))
                uniformScales[bone] = GetUniformScale(transform.Scaling);
            else
                uniformScales[bone] = 1f;
        }

        var surfaceSmoothness = ScoreSurfaceSmoothness(uniformScales);
        var proportionBalance = ScoreProportionBalance(uniformScales);
        var symmetry = ScoreSymmetry(uniformScales);

        var issues = new List<string>();
        AddGuardrailIssues(uniformScales, issues);
        AddSymmetryIssues(uniformScales, issues);

        var tunedSettings = new AdvancedBodyScalingSettings
        {
            Enabled = true,
            Mode = settings.Mode == AdvancedBodyScalingMode.Manual ? AdvancedBodyScalingMode.Automatic : settings.Mode,
            SurfaceBalancingStrength = settings.SurfaceBalancingStrength,
            MassRedistributionStrength = settings.MassRedistributionStrength,
            GuardrailMode = settings.GuardrailMode,
            PoseValidationMode = settings.PoseValidationMode,
            NaturalizationStrength = settings.NaturalizationStrength,
            NeckLengthCompensation = settings.NeckLengthCompensation,
            NeckShoulderBlendStrength = settings.NeckShoulderBlendStrength,
            ClavicleShoulderSmoothing = settings.ClavicleShoulderSmoothing,
            UseRaceSpecificNeckCompensation = settings.UseRaceSpecificNeckCompensation,
            RaceNeckPresets = settings.RaceNeckPresets == null
                ? new Dictionary<Penumbra.GameData.Enums.Race, AdvancedBodyScalingNeckCompensationPreset>()
                : settings.RaceNeckPresets.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.DeepCopy()),
            RegionProfiles = settings.RegionProfiles.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.DeepCopy())
        };

        var suggested = Apply(userTransforms, tunedSettings);
        var suggestedFixes = new Dictionary<string, BoneTransform>(StringComparer.Ordinal);

        foreach (var kvp in suggested)
        {
            if (!IsBodyBone(kvp.Key))
                continue;

            var fromScale = userTransforms.TryGetValue(kvp.Key, out var existing)
                ? existing.Scaling
                : Vector3.One;

            if (!fromScale.IsApproximately(kvp.Value.Scaling, 0.0005f))
                suggestedFixes[kvp.Key] = kvp.Value.DeepCopy();
        }

        return new BodyAnalysisResult(surfaceSmoothness, proportionBalance, symmetry, issues, suggestedFixes);
    }

    private static ModeTuning GetTuning(AdvancedBodyScalingMode mode)
        => mode switch
        {
            AdvancedBodyScalingMode.Assist => new ModeTuning(0.15f, 0.12f, 0.10f, 0.15f, 0.10f, 0.45f, DefaultPropagationDepth),
            AdvancedBodyScalingMode.Automatic => new ModeTuning(0.25f, 0.22f, 0.18f, 0.25f, 0.18f, 0.70f, DefaultPropagationDepth),
            AdvancedBodyScalingMode.Strong => new ModeTuning(0.40f, 0.35f, 0.28f, 0.40f, 0.28f, 0.90f, DefaultPropagationDepth + 1),
            _ => new ModeTuning(0f, 0f, 0f, 0f, 0f, 0f, 0)
        };

    private static float GetGuardrailMultiplier(AdvancedBodyScalingGuardrailMode mode)
        => mode switch
        {
            AdvancedBodyScalingGuardrailMode.Off => 0f,
            AdvancedBodyScalingGuardrailMode.Strong => 1.25f,
            _ => 1f
        };

    private static float GetPoseValidationMultiplier(AdvancedBodyScalingPoseValidationMode mode)
        => mode switch
        {
            AdvancedBodyScalingPoseValidationMode.Off => 0f,
            AdvancedBodyScalingPoseValidationMode.Strong => 1f,
            _ => 0.5f
        };

    private static AdvancedBodyRegion ResolveRegion(string boneName)
    {
        if (IsNeckShoulderBone(boneName))
            return AdvancedBodyRegion.NeckShoulder;

        var family = BoneData.GetBoneFamily(boneName);
        return family switch
        {
            BoneData.BoneFamily.Chest => AdvancedBodyRegion.Chest,
            BoneData.BoneFamily.Groin => AdvancedBodyRegion.Pelvis,
            BoneData.BoneFamily.Arms => AdvancedBodyRegion.Arms,
            BoneData.BoneFamily.Hands => AdvancedBodyRegion.Hands,
            BoneData.BoneFamily.Legs => AdvancedBodyRegion.Legs,
            BoneData.BoneFamily.Feet => IsToeBone(boneName) ? AdvancedBodyRegion.Toes : AdvancedBodyRegion.Feet,
            BoneData.BoneFamily.Tail => AdvancedBodyRegion.Tail,
            _ => AdvancedBodyRegion.Spine
        };
    }

    private static bool IsNeckShoulderBone(string boneName)
        => NeckShoulderBones.Contains(boneName);

    private static bool IsToeBone(string boneName)
        => boneName.StartsWith("iv_asi_", StringComparison.Ordinal)
           || boneName.StartsWith("j_asi_e_", StringComparison.Ordinal);

    private static AdvancedBodyScalingRegionProfile GetRegionProfile(
        IReadOnlyDictionary<string, AdvancedBodyScalingRegionProfile> profiles,
        string bone)
        => profiles.TryGetValue(bone, out var profile) ? profile : new AdvancedBodyScalingRegionProfile();

    private static bool IsBodyBone(string boneName)
    {
        var family = BoneData.GetBoneFamily(boneName);
        return BodyFamilies.Contains(family);
    }

    private static void ExpandNeighbors(string source, int depth, HashSet<string> output)
    {
        if (depth <= 0)
            return;

        var graph = BoneDependencyGraph.Instance;
        var queue = new Queue<(string Bone, int Depth)>();
        var visited = new HashSet<string>(StringComparer.Ordinal) { source };

        queue.Enqueue((source, 0));

        while (queue.Count > 0)
        {
            var (bone, currentDepth) = queue.Dequeue();
            if (currentDepth >= depth)
                continue;

            foreach (var neighbor in graph.GetNeighbors(bone))
            {
                if (!IsBodyBone(neighbor.Name))
                    continue;

                if (!visited.Add(neighbor.Name))
                    continue;

                output.Add(neighbor.Name);
                queue.Enqueue((neighbor.Name, currentDepth + 1));
            }
        }
    }

    private static void ApplyInfluencePropagation(
        Dictionary<string, float> scales,
        IReadOnlyDictionary<string, BoneLockState> lockStates,
        IReadOnlyDictionary<string, AdvancedBodyScalingRegionProfile> regionProfiles,
        IReadOnlyCollection<string> sources,
        ModeTuning tuning)
    {
        if (tuning.InfluenceStrength <= 0f || tuning.PropagationDepth <= 0)
            return;

        var graph = BoneDependencyGraph.Instance;

        foreach (var source in sources)
        {
            if (!scales.TryGetValue(source, out var sourceScale))
                continue;

            if (lockStates.TryGetValue(source, out var lockState) && lockState == BoneLockState.Locked)
                continue;

            var sourceProfile = GetRegionProfile(regionProfiles, source);
            var delta = sourceScale - 1f;
            if (MathF.Abs(delta) < Epsilon)
                continue;

            var queue = new Queue<(string Bone, int Depth)>();
            var visited = new HashSet<string>(StringComparer.Ordinal) { source };

            queue.Enqueue((source, 0));

            while (queue.Count > 0)
            {
                var (bone, depth) = queue.Dequeue();
                if (depth >= tuning.PropagationDepth)
                    continue;

                foreach (var neighbor in graph.GetNeighbors(bone))
                {
                    if (!IsBodyBone(neighbor.Name))
                        continue;

                    if (!visited.Add(neighbor.Name))
                        continue;

                    var neighborProfile = GetRegionProfile(regionProfiles, neighbor.Name);
                    var regionMultiplier = (sourceProfile.InfluenceMultiplier + neighborProfile.InfluenceMultiplier) * 0.5f;
                    var attenuation = neighbor.Weight * MathF.Pow(DefaultPropagationFalloff, depth) * tuning.InfluenceStrength * regionMultiplier;

                    if (attenuation > 0f && scales.ContainsKey(neighbor.Name))
                    {
                        if (!lockStates.TryGetValue(neighbor.Name, out var neighborState) || neighborState == BoneLockState.Unlocked)
                            scales[neighbor.Name] = ClampScale(scales[neighbor.Name] + delta * attenuation);
                    }

                    queue.Enqueue((neighbor.Name, depth + 1));
                }
            }
        }
    }

    private static void ApplySurfaceBalancing(
        Dictionary<string, float> scales,
        IReadOnlyDictionary<string, BoneLockState> lockStates,
        IReadOnlyDictionary<string, AdvancedBodyScalingRegionProfile> regionProfiles,
        float smoothingFactor,
        float threshold)
    {
        if (smoothingFactor <= 0f)
            return;

        var deltas = new Dictionary<string, float>(StringComparer.Ordinal);
        var graph = BoneDependencyGraph.Instance;

        foreach (var bone in scales.Keys)
        {
            var boneProfile = GetRegionProfile(regionProfiles, bone);
            foreach (var neighbor in graph.GetNeighbors(bone))
            {
                if (string.Compare(bone, neighbor.Name, StringComparison.Ordinal) >= 0)
                    continue;

                if (!scales.TryGetValue(neighbor.Name, out var otherScale))
                    continue;

                var currentScale = scales[bone];
                var diff = currentScale - otherScale;
                if (MathF.Abs(diff) <= threshold)
                    continue;

                var neighborProfile = GetRegionProfile(regionProfiles, neighbor.Name);
                var smoothingMultiplier = (boneProfile.SmoothingMultiplier + neighborProfile.SmoothingMultiplier) * 0.5f;
                var adjust = diff * smoothingFactor * smoothingMultiplier * 0.5f;

                var boneLocked = lockStates.TryGetValue(bone, out var boneState) && boneState != BoneLockState.Unlocked;
                var neighborLocked = lockStates.TryGetValue(neighbor.Name, out var neighborState) && neighborState != BoneLockState.Unlocked;

                if (!boneLocked)
                    deltas[bone] = GetValueOrDefault(deltas, bone) - adjust;

                if (!neighborLocked)
                    deltas[neighbor.Name] = GetValueOrDefault(deltas, neighbor.Name) + adjust;
            }
        }

        foreach (var kvp in deltas)
            scales[kvp.Key] = ClampScale(scales[kvp.Key] + kvp.Value);
    }

    private static void ApplyMassRedistribution(
        Dictionary<string, float> scales,
        IReadOnlyDictionary<string, BoneLockState> lockStates,
        IReadOnlyDictionary<string, AdvancedBodyScalingRegionProfile> regionProfiles,
        float strength,
        float threshold)
    {
        if (strength <= 0f)
            return;

        var graph = BoneDependencyGraph.Instance;
        var updates = new Dictionary<string, float>(StringComparer.Ordinal);

        foreach (var (bone, scale) in scales)
        {
            if (lockStates.TryGetValue(bone, out var state) && state != BoneLockState.Unlocked)
                continue;

            var regionProfile = GetRegionProfile(regionProfiles, bone);
            var regionStrength = strength * regionProfile.MassRedistributionMultiplier;
            if (regionStrength <= 0f)
                continue;

            var delta = scale - 1f;
            if (MathF.Abs(delta) <= threshold)
                continue;

            var neighbors = graph.GetNeighbors(bone)
                .Where(n => scales.ContainsKey(n.Name)
                            && (!lockStates.TryGetValue(n.Name, out var nState) || nState == BoneLockState.Unlocked))
                .ToList();

            if (neighbors.Count == 0)
                continue;

            var totalWeight = neighbors.Sum(n => n.Weight);
            if (totalWeight <= 0f)
                continue;

            var redistribute = delta * regionStrength;
            updates[bone] = GetValueOrDefault(updates, bone) - redistribute;

            foreach (var neighbor in neighbors)
            {
                var share = redistribute * (neighbor.Weight / totalWeight);
                updates[neighbor.Name] = GetValueOrDefault(updates, neighbor.Name) + share;
            }
        }

        foreach (var kvp in updates)
            scales[kvp.Key] = ClampScale(scales[kvp.Key] + kvp.Value);
    }

    private static void ApplyGuardrails(
        Dictionary<string, float> scales,
        IReadOnlyDictionary<string, BoneLockState> lockStates,
        IReadOnlyDictionary<string, AdvancedBodyScalingRegionProfile> regionProfiles,
        float strength,
        AdvancedBodyScalingDebugReport? debug)
    {
        if (strength <= 0f)
            return;

        ApplyRatioGuardrail(scales, lockStates, regionProfiles, GuardrailBones.Shoulder, GuardrailBones.Waist, 1.1f, 1.6f, strength,
            "Shoulder/Waist guardrail", debug, allowGuardrails: true);
        ApplyRatioGuardrail(scales, lockStates, regionProfiles, GuardrailBones.Hip, GuardrailBones.Waist, 1.1f, 1.5f, strength,
            "Hip/Waist guardrail", debug, allowGuardrails: true);
        ApplyRatioGuardrail(scales, lockStates, regionProfiles, GuardrailBones.Thigh, GuardrailBones.Calf, 1.0f, 1.4f, strength,
            "Thigh/Calf guardrail", debug, allowGuardrails: true);
        ApplyRatioGuardrail(scales, lockStates, regionProfiles, GuardrailBones.UpperArm, GuardrailBones.Forearm, 0.9f, 1.3f, strength,
            "UpperArm/Forearm guardrail", debug, allowGuardrails: true);
    }

    private static void ApplyCurveSolver(
        Dictionary<string, float> scales,
        IReadOnlyDictionary<string, BoneLockState> lockStates,
        IReadOnlyDictionary<string, AdvancedBodyScalingRegionProfile> regionProfiles,
        float strength,
        float threshold)
    {
        if (strength <= 0f)
            return;

        foreach (var chain in CurveChains.All)
            ApplyCurveSmoothing(scales, lockStates, regionProfiles, chain, strength, threshold);
    }

    private static void ApplyNeckCompensation(
        Dictionary<string, BoneTransform> output,
        IReadOnlyDictionary<string, BoneTransform> userTransforms,
        AdvancedBodyScalingSettings settings)
    {
        var compensation = settings.NeckLengthCompensation;
        if (compensation <= 0f)
            return;

        var blend = Math.Clamp(settings.NeckShoulderBlendStrength, 0f, 1f);
        var clavicleSmoothing = Math.Clamp(settings.ClavicleShoulderSmoothing, 0f, 1f);

        var reduction = compensation * MaxNeckLengthReduction;
        if (reduction <= 0f)
            return;

        var upperSpineWeight = 0.6f * blend;
        var clavicleWeight = blend * Lerp(0.2f, 0.45f, clavicleSmoothing);
        var shoulderWeight = blend * Lerp(0.15f, 0.35f, clavicleSmoothing);

        foreach (var bone in NeckBones)
            ApplyNeckScale(output, userTransforms, bone, reduction, 1f);

        foreach (var bone in UpperSpineBones)
            ApplyNeckScale(output, userTransforms, bone, reduction, upperSpineWeight);

        foreach (var bone in ClavicleBones)
            ApplyNeckScale(output, userTransforms, bone, reduction, clavicleWeight);

        foreach (var bone in ShoulderRootBones)
            ApplyNeckScale(output, userTransforms, bone, reduction, shoulderWeight);
    }

    private static void ApplyNeckScale(
        Dictionary<string, BoneTransform> output,
        IReadOnlyDictionary<string, BoneTransform> userTransforms,
        string bone,
        float reduction,
        float weight)
    {
        if (weight <= 0f || reduction <= 0f)
            return;

        if (IsLockedByUser(userTransforms, bone))
            return;

        var multiplier = 1f - (reduction * weight);
        if (multiplier >= 0.999f)
            return;

        if (!output.TryGetValue(bone, out var transform))
            transform = new BoneTransform();

        var scale = transform.Scaling;
        scale.Y = Math.Clamp(scale.Y * multiplier, MinAutoScale, MaxAutoScale);
        transform.Scaling = scale;
        output[bone] = transform;
    }

    private static bool IsLockedByUser(IReadOnlyDictionary<string, BoneTransform> userTransforms, string bone)
        => userTransforms.TryGetValue(bone, out var transform) && transform.LockState != BoneLockState.Unlocked;

    private static void ApplyPoseAwareValidation(
        Dictionary<string, float> scales,
        IReadOnlyDictionary<string, BoneLockState> lockStates,
        IReadOnlyDictionary<string, AdvancedBodyScalingRegionProfile> regionProfiles,
        float strength,
        AdvancedBodyScalingDebugReport? debug)
    {
        if (strength <= 0f)
            return;

        // Heuristic stand-in for pose-aware validation to reduce common deformation risks.
        ApplyRatioGuardrail(scales, lockStates, regionProfiles, GuardrailBones.Thigh, GuardrailBones.Calf, 1.0f, 1.3f, strength,
            "Pose-aware thigh/calf", debug, allowGuardrails: false);
        ApplyRatioGuardrail(scales, lockStates, regionProfiles, GuardrailBones.UpperArm, GuardrailBones.Forearm, 0.9f, 1.2f, strength,
            "Pose-aware upperarm/forearm", debug, allowGuardrails: false);
    }

    private static void ApplyCurveSmoothing(
        Dictionary<string, float> scales,
        IReadOnlyDictionary<string, BoneLockState> lockStates,
        IReadOnlyDictionary<string, AdvancedBodyScalingRegionProfile> regionProfiles,
        IReadOnlyList<string> chain,
        float strength,
        float threshold)
    {
        if (chain.Count < 3)
            return;

        for (var i = 1; i < chain.Count - 1; i++)
        {
            var bone = chain[i];
            if (!scales.ContainsKey(bone))
                continue;

            if (lockStates.TryGetValue(bone, out var state) && state != BoneLockState.Unlocked)
                continue;

            var prevScale = GetValueOrDefault(scales, chain[i - 1], 1f);
            var currentScale = scales[bone];
            var nextScale = GetValueOrDefault(scales, chain[i + 1], 1f);
            var target = (prevScale + currentScale + nextScale) / 3f;

            if (MathF.Abs(currentScale - target) <= threshold)
                continue;

            var regionProfile = GetRegionProfile(regionProfiles, bone);
            var localStrength = strength * regionProfile.SmoothingMultiplier;
            if (localStrength <= 0f)
                continue;

            scales[bone] = ClampScale(currentScale + (target - currentScale) * localStrength);
        }
    }

    private static void ApplyRatioGuardrail(
        Dictionary<string, float> scales,
        IReadOnlyDictionary<string, BoneLockState> lockStates,
        IReadOnlyDictionary<string, AdvancedBodyScalingRegionProfile> regionProfiles,
        IReadOnlyList<string> numeratorBones,
        IReadOnlyList<string> denominatorBones,
        float minRatio,
        float maxRatio,
        float strength,
        string description,
        AdvancedBodyScalingDebugReport? debug,
        bool allowGuardrails)
    {
        if (!TryAverageScale(scales, numeratorBones, out var numerator))
            return;

        if (!TryAverageScale(scales, denominatorBones, out var denominator))
            return;

        var ratio = numerator / denominator;
        if (ratio >= minRatio && ratio <= maxRatio)
            return;

        var targetRatio = Math.Clamp(ratio, minRatio, maxRatio);
        var targetNumerator = denominator * targetRatio;
        var adjusted = TryAdjustGroup(scales, lockStates, regionProfiles, numeratorBones, targetNumerator, strength, allowGuardrails);

        if (adjusted)
        {
            RecordGuardrailCorrection(scales, numeratorBones, denominatorBones, ratio, description, debug);
            return;
        }

        var targetDenominator = numerator / targetRatio;
        if (TryAdjustGroup(scales, lockStates, regionProfiles, denominatorBones, targetDenominator, strength, allowGuardrails))
            RecordGuardrailCorrection(scales, numeratorBones, denominatorBones, ratio, description, debug);
    }

    private static bool TryAdjustGroup(
        Dictionary<string, float> scales,
        IReadOnlyDictionary<string, BoneLockState> lockStates,
        IReadOnlyDictionary<string, AdvancedBodyScalingRegionProfile> regionProfiles,
        IReadOnlyList<string> bones,
        float targetScale,
        float strength,
        bool allowGuardrails)
    {
        var modifiable = bones
            .Where(b => scales.ContainsKey(b))
            .Where(b => !lockStates.TryGetValue(b, out var state) || state == BoneLockState.Unlocked)
            .Where(b =>
            {
                var profile = GetRegionProfile(regionProfiles, b);
                return allowGuardrails ? profile.AllowGuardrails : profile.AllowPoseValidation;
            })
            .ToList();

        if (modifiable.Count == 0)
            return false;

        var currentAverage = AverageScale(scales, modifiable);
        if (currentAverage <= Epsilon)
            return false;

        var multiplier = AverageRegionMultiplier(regionProfiles, modifiable, allowGuardrails);
        var blended = Lerp(currentAverage, targetScale, strength * multiplier);
        var ratio = blended / currentAverage;

        foreach (var bone in modifiable)
            scales[bone] = ClampScale(scales[bone] * ratio);

        return true;
    }

    private static float AverageRegionMultiplier(
        IReadOnlyDictionary<string, AdvancedBodyScalingRegionProfile> regionProfiles,
        IReadOnlyList<string> bones,
        bool allowGuardrails)
    {
        if (bones.Count == 0)
            return 1f;

        var total = 0f;
        var count = 0;
        foreach (var bone in bones)
        {
            var profile = GetRegionProfile(regionProfiles, bone);
            var multiplier = allowGuardrails ? profile.GuardrailMultiplier : profile.PoseValidationMultiplier;
            total += multiplier;
            count++;
        }

        if (count == 0)
            return 1f;

        return total / count;
    }

    private static void RecordGuardrailCorrection(
        Dictionary<string, float> scales,
        IReadOnlyList<string> numeratorBones,
        IReadOnlyList<string> denominatorBones,
        float beforeRatio,
        string description,
        AdvancedBodyScalingDebugReport? debug)
    {
        if (debug == null)
            return;

        if (!TryAverageScale(scales, numeratorBones, out var numerator))
            return;

        if (!TryAverageScale(scales, denominatorBones, out var denominator))
            return;

        var afterRatio = numerator / denominator;
        debug.GuardrailCorrections.Add(new AdvancedBodyScalingDebugReport.GuardrailCorrection
        {
            Description = description,
            BeforeRatio = beforeRatio,
            AfterRatio = afterRatio,
            NumeratorBones = numeratorBones,
            DenominatorBones = denominatorBones
        });
    }

    private static float AverageScale(Dictionary<string, float> scales, IReadOnlyList<string> bones)
    {
        var values = bones
            .Where(b => scales.ContainsKey(b))
            .Select(b => scales[b])
            .ToList();

        return values.Count == 0 ? 1f : values.Average();
    }

    private static bool TryAverageScale(Dictionary<string, float> scales, IReadOnlyList<string> bones, out float average)
    {
        var values = bones
            .Where(b => scales.ContainsKey(b))
            .Select(b => scales[b])
            .ToList();

        if (values.Count == 0)
        {
            average = 0f;
            return false;
        }

        average = values.Average();
        return true;
    }

    internal static float GetUniformScale(Vector3 scale)
    {
        var x = MathF.Abs(scale.X);
        var y = MathF.Abs(scale.Y);
        var z = MathF.Abs(scale.Z);
        return (x + y + z) / 3f;
    }

    private static Vector3 ApplyUniformScale(Vector3 current, float targetUniform)
    {
        var currentUniform = GetUniformScale(current);
        if (currentUniform <= Epsilon)
            return new Vector3(targetUniform);

        var ratio = targetUniform / currentUniform;
        return current * ratio;
    }

    private static float Lerp(float a, float b, float t)
        => a + (b - a) * t;

    private static float ClampScale(float value)
        => Math.Clamp(value, MinAutoScale, MaxAutoScale);

    private static float GetValueOrDefault(Dictionary<string, float> dict, string key, float fallback = 0f)
        => dict.TryGetValue(key, out var value) ? value : fallback;

    private static int ScoreSurfaceSmoothness(Dictionary<string, float> scales)
    {
        var graph = BoneDependencyGraph.Instance;
        var diffs = new List<float>();

        foreach (var bone in scales.Keys)
        {
            foreach (var neighbor in graph.GetNeighbors(bone))
            {
                if (string.Compare(bone, neighbor.Name, StringComparison.Ordinal) >= 0)
                    continue;

                if (!scales.TryGetValue(neighbor.Name, out var other))
                    continue;

                diffs.Add(MathF.Abs(scales[bone] - other));
            }
        }

        if (diffs.Count == 0)
            return 100;

        var avg = diffs.Average();
        var score = 100f - (avg * 120f);
        return (int)Math.Clamp(score, 0f, 100f);
    }

    private static int ScoreProportionBalance(Dictionary<string, float> scales)
    {
        var penalties = new List<float>();

        AddRatioPenalty(scales, GuardrailBones.Shoulder, GuardrailBones.Waist, 1.1f, 1.6f, penalties);
        AddRatioPenalty(scales, GuardrailBones.Hip, GuardrailBones.Waist, 1.1f, 1.5f, penalties);
        AddRatioPenalty(scales, GuardrailBones.Thigh, GuardrailBones.Calf, 1.0f, 1.4f, penalties);
        AddRatioPenalty(scales, GuardrailBones.UpperArm, GuardrailBones.Forearm, 0.9f, 1.3f, penalties);

        if (penalties.Count == 0)
            return 100;

        var avgPenalty = penalties.Average();
        var score = 100f - avgPenalty * 100f;
        return (int)Math.Clamp(score, 0f, 100f);
    }

    private static int ScoreSymmetry(Dictionary<string, float> scales)
    {
        var diffs = new List<float>();

        foreach (var bone in scales.Keys)
        {
            var mirror = BoneData.GetBoneMirror(bone);
            if (mirror == null || !scales.TryGetValue(mirror, out var mirrorScale))
                continue;

            if (string.Compare(bone, mirror, StringComparison.Ordinal) >= 0)
                continue;

            diffs.Add(MathF.Abs(scales[bone] - mirrorScale));
        }

        if (diffs.Count == 0)
            return 100;

        var avg = diffs.Average();
        var score = 100f - (avg * 150f);
        return (int)Math.Clamp(score, 0f, 100f);
    }

    private static void AddRatioPenalty(
        Dictionary<string, float> scales,
        IReadOnlyList<string> numeratorBones,
        IReadOnlyList<string> denominatorBones,
        float minRatio,
        float maxRatio,
        List<float> penalties)
    {
        if (!TryAverageScale(scales, numeratorBones, out var numerator))
            return;

        if (!TryAverageScale(scales, denominatorBones, out var denominator))
            return;

        var ratio = numerator / denominator;
        if (ratio < minRatio)
            penalties.Add(minRatio - ratio);
        else if (ratio > maxRatio)
            penalties.Add(ratio - maxRatio);
    }

    private static void AddGuardrailIssues(Dictionary<string, float> scales, List<string> issues)
    {
        if (TryAverageScale(scales, GuardrailBones.Shoulder, out var shoulder) &&
            TryAverageScale(scales, GuardrailBones.Waist, out var waist))
        {
            var shoulderRatio = shoulder / waist;
            if (shoulderRatio > 1.6f + 0.05f)
                issues.Add("Shoulder width slightly high");
        }

        if (TryAverageScale(scales, GuardrailBones.Hip, out var hip) &&
            TryAverageScale(scales, GuardrailBones.Waist, out var waistHip))
        {
            var hipRatio = hip / waistHip;
            if (hipRatio < 1.1f - 0.05f)
                issues.Add("Hip width slightly low");
        }

        if (TryAverageScale(scales, GuardrailBones.Thigh, out var thigh) &&
            TryAverageScale(scales, GuardrailBones.Calf, out var calf))
        {
            var thighRatio = thigh / calf;
            if (thighRatio > 1.4f + 0.05f)
                issues.Add("Thigh-calf taper abrupt");
        }
    }

    private static void AddSymmetryIssues(Dictionary<string, float> scales, List<string> issues)
    {
        foreach (var bone in scales.Keys)
        {
            var mirror = BoneData.GetBoneMirror(bone);
            if (mirror == null || !scales.TryGetValue(mirror, out var mirrorScale))
                continue;

            if (string.Compare(bone, mirror, StringComparison.Ordinal) >= 0)
                continue;

            var diff = MathF.Abs(scales[bone] - mirrorScale);
            if (diff > 0.2f)
            {
                issues.Add("Left-right asymmetry detected");
                return;
            }
        }
    }

    private static class GuardrailBones
    {
        public static readonly string[] Waist = { "j_kosi", "n_hara" };
        public static readonly string[] Shoulder = { "j_sako_l", "j_sako_r", "n_hkata_l", "n_hkata_r" };
        public static readonly string[] Hip = { "iv_shiri_l", "iv_shiri_r", "ya_shiri_phys_l", "ya_shiri_phys_r" };
        public static readonly string[] Thigh = { "j_asi_a_l", "j_asi_a_r", "ya_daitai_phys_l", "ya_daitai_phys_r", "iv_daitai_phys_l", "iv_daitai_phys_r" };
        public static readonly string[] Calf = { "j_asi_c_l", "j_asi_c_r" };
        public static readonly string[] UpperArm = { "j_ude_a_l", "j_ude_a_r", "iv_nitoukin_l", "iv_nitoukin_r" };
        public static readonly string[] Forearm = { "j_ude_b_l", "j_ude_b_r" };
    }

    private static class CurveChains
    {
        public static readonly IReadOnlyList<IReadOnlyList<string>> All = new List<IReadOnlyList<string>>
        {
            new[] { "j_kosi", "j_sebo_a", "j_sebo_b", "j_sebo_c", "j_kubi", "j_kao" },
            new[] { "j_kosi", "ya_fukubu_phys", "iv_fukubu_phys", "j_sebo_a" },
            new[] { "j_sebo_b", "j_mune_l", "iv_c_mune_l" },
            new[] { "j_sebo_b", "j_mune_r", "iv_c_mune_r" },
            new[] { "j_kosi", "iv_shiri_l", "ya_shiri_phys_l" },
            new[] { "j_kosi", "iv_shiri_r", "ya_shiri_phys_r" },
            new[] { "j_kosi", "j_asi_a_l", "j_asi_b_l", "j_asi_c_l", "j_asi_d_l", "j_asi_e_l" },
            new[] { "j_kosi", "j_asi_a_r", "j_asi_b_r", "j_asi_c_r", "j_asi_d_r", "j_asi_e_r" },
            new[] { "j_sako_l", "j_ude_a_l", "j_ude_b_l", "n_hte_l", "j_te_l" },
            new[] { "j_sako_r", "j_ude_a_r", "j_ude_b_r", "n_hte_r", "j_te_r" },
            new[] { "n_sippo_a", "n_sippo_b", "n_sippo_c", "n_sippo_d", "n_sippo_e" }
        };
    }
}

internal sealed class BodyAnalysisResult
{
    public int SurfaceSmoothness { get; }
    public int ProportionBalance { get; }
    public int Symmetry { get; }
    public IReadOnlyList<string> Issues { get; }
    public IReadOnlyDictionary<string, BoneTransform> SuggestedFixes { get; }

    public BodyAnalysisResult(
        int surfaceSmoothness,
        int proportionBalance,
        int symmetry,
        IReadOnlyList<string> issues,
        IReadOnlyDictionary<string, BoneTransform> suggestedFixes)
    {
        SurfaceSmoothness = surfaceSmoothness;
        ProportionBalance = proportionBalance;
        Symmetry = symmetry;
        Issues = issues;
        SuggestedFixes = suggestedFixes;
    }
}

internal sealed class BoneDependencyGraph
{
    internal readonly record struct Neighbor(string Name, float Weight);

    public static BoneDependencyGraph Instance { get; } = new();

    private readonly Dictionary<string, List<Neighbor>> _neighbors = new(StringComparer.Ordinal);

    private const float ParentChildWeight = 0.40f;
    private const float MirrorWeight = 0.35f;
    private const float SiblingWeight = 0.25f;

    private BoneDependencyGraph()
    {
        Build();
    }

    public IReadOnlyList<Neighbor> GetNeighbors(string bone)
        => _neighbors.TryGetValue(bone, out var list) ? list : Array.Empty<Neighbor>();

    private void Build()
    {
        foreach (var bone in BoneData.GetBoneCodenames())
        {
            foreach (var child in BoneData.GetChildren(bone))
                AddEdge(bone, child, ParentChildWeight);

            var mirror = BoneData.GetBoneMirror(bone);
            if (!string.IsNullOrEmpty(mirror))
                AddEdge(bone, mirror, MirrorWeight);
        }

        foreach (var parent in BoneData.GetBoneCodenames())
        {
            var children = BoneData.GetChildren(parent);
            if (children.Length < 2)
                continue;

            for (var i = 0; i < children.Length; i++)
            {
                for (var j = i + 1; j < children.Length; j++)
                    AddEdge(children[i], children[j], SiblingWeight);
            }
        }
    }

    private void AddEdge(string a, string b, float weight)
    {
        if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b) || a == b)
            return;

        AddNeighbor(a, b, weight);
        AddNeighbor(b, a, weight);
    }

    private void AddNeighbor(string from, string to, float weight)
    {
        if (!_neighbors.TryGetValue(from, out var list))
        {
            list = new List<Neighbor>();
            _neighbors[from] = list;
        }

        var index = list.FindIndex(n => n.Name == to);
        if (index >= 0)
        {
            var existing = list[index];
            list[index] = existing with { Weight = Math.Max(existing.Weight, weight) };
        }
        else
        {
            list.Add(new Neighbor(to, weight));
        }
    }
}
