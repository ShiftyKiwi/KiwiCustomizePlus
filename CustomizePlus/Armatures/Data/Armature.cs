// Copyright (c) Customize+.
// Licensed under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using FFXIVClientStructs.FFXIV.Client.Graphics.Scene;
using Penumbra.GameData.Actors;
using CustomizePlus.Core.Data;
using CustomizePlus.Profiles.Data;
using CustomizePlus.Templates.Data;
using CustomizePlus.GameData.Extensions;
using FFXIVClientStructs.Havok.Animation.Rig;

namespace CustomizePlus.Armatures.Data;

/// <summary>
/// Represents a "copy" of the ingame skeleton upon which the linked character profile is meant to operate.
/// Acts as an interface by which the in-game skeleton can be manipulated on a bone-by-bone basis.
/// </summary>
public unsafe class Armature
{
    /// <summary>
    /// Gets the Customize+ profile for which this mockup applies transformations.
    /// </summary>
    public Profile Profile { get; set; }

    /// <summary>
    /// Static identifier of the actor associated with this armature
    /// </summary>
    public ActorIdentifier ActorIdentifier { get; init; }

    /// <summary>
    /// Gets or sets a value indicating whether or not this armature has any renderable objects on which it should act.
    /// </summary>
    public bool IsVisible { get; set; }

    /// <summary>
    /// Represents date and time when actor associated with this armature was last seen.
    /// Implemented mostly as a armature cleanup protection hack for mare and penumbra.
    /// </summary>
    public DateTime LastSeen { get; private set; }

    /// <summary>
    /// Gets a value indicating whether or not this armature has successfully built itself with bone information.
    /// </summary>
    public bool IsBuilt => _isBuilt;
    /// <summary>
    /// True only while the live skeleton has been verified to match the published snapshot.
    /// A valid last-known-good snapshot may remain built while native writes are paused during redraw.
    /// </summary>
    public bool IsSkeletonBindingCurrent { get; private set; }

    /// <summary>
    /// The latest reason the live skeleton could not be used with the published binding.
    /// This is diagnostic-only and is cleared after a validated binding is restored.
    /// </summary>
    public string LastSkeletonBindingIssue { get; private set; } = "none";

    /// <summary>
    /// Monotonically increases only when a validated skeleton topology is published.
    /// </summary>
    public long SkeletonRevision { get; private set; }

    /// <summary>
    /// Public name for the published armature/topology revision used by diagnostics.
    /// </summary>
    public long ArmatureRevision => SkeletonRevision;

    /// <summary>
    /// Monotonically increases when the validated backing native skeleton binding is replaced.
    /// This is intentionally separate from the structural capability fingerprint.
    /// </summary>
    public long NativeBindingGeneration { get; private set; }

    /// <summary>
    /// Monotonically increases when the actor/native lifetime is observed to become unavailable.
    /// It is intentionally separate from topology and survives an identical-pointer reuse case.
    /// </summary>
    public long ActorLifetimeGeneration { get; private set; }

    /// <summary>
    /// True after the previous live actor/native binding disappeared and until a validated replacement is published.
    /// </summary>
    public bool IsAwaitingActorReacquisitionPublication => _requiresActorReacquisitionPublication;

    /// <summary>
    /// True while a Glamourer or actor-customize appearance change has been observed and is waiting
    /// for one safe, post-transition template binding refresh.
    /// </summary>
    public bool IsAwaitingAppearanceContextRebind
        => _pendingAppearanceTransitionId > _lastAppliedAppearanceTransitionId
           || _pendingAppearanceContext.HasValue;

    /// <summary>
    /// Latest Glamourer appearance operation observed for this actor. This is intentionally
    /// independent from native, topology, profile, and deformation revisions.
    /// </summary>
    public long CurrentAppearanceEpoch { get; private set; }

    public long LatestPendingStableAppearanceEpoch => _pendingAppearanceTransitionId;
    public long LastAppliedStableAppearanceEpoch => _lastAppliedAppearanceTransitionId;
    public string AppearanceEpochState { get; private set; } = "idle";
    public string CurrentAppearanceOperationType { get; private set; } = "none";
    public string LastAppearanceLifecycleEvent { get; private set; } = "none";
    public string PendingAppearanceContext
        => _pendingAppearanceContext is { } context
            ? $"race={context.Race}; clan={context.Clan}; gender={context.Gender}"
            : "none";
    public string LastAppearanceRebindReason { get; private set; } = "none";
    public long LastAppearanceRebindEpoch { get; private set; }
    public long RevertFinalizedAtMs { get; private set; }
    public long RevertStableRebindQueuedAtMs { get; private set; }
    public long RevertFirstValidRecoveryObservationAtMs { get; private set; }
    public long RevertPublicationAtMs { get; private set; }
    public long RevertStableRebindCompletedAtMs { get; private set; }
    public long RevertRecoveryLatencyMs => RevertFinalizedAtMs > 0 && RevertStableRebindCompletedAtMs >= RevertFinalizedAtMs
        ? RevertStableRebindCompletedAtMs - RevertFinalizedAtMs
        : 0;

    /// <summary>
    /// The armature revision for which the current template-to-ModelBone links were built.
    /// </summary>
    public long TemplateBindingRevision { get; private set; }

    // Debug-only observability for bounded DAB validation. These fields describe the last
    // static binding build; they do not participate in transform or publication decisions.
    public long TemplateBindingBuildCount { get; private set; }
    public string LastTemplateBindingBuildReason { get; private set; } = "not built";

    /// <summary>
    /// Monotonically increases only when the effective profile/template resolution changes.
    /// Animation and diagnostic reads do not affect this revision.
    /// </summary>
    public long ProfileResolutionRevision { get; private set; }

    /// <summary>
    /// Monotonically increases only when the rebuild-time deformation solver output changes.
    /// </summary>
    public long DeformationRevision { get; private set; }

    /// <summary>
    /// Monotonically increases when the published deformation diagnostics change.
    /// </summary>
    public long DiagnosticsRevision { get; private set; }

    /// <summary>
    /// Number of live ModelBones currently linked to a resolved template transform.
    /// </summary>
    public int BoundModelBoneCount { get; private set; }

    /// <summary>
    /// Current candidate publication identity while a redraw replacement is being confirmed.
    /// </summary>
    public string? PendingPublicationIdentity => _pendingSkeletonSignature;

    /// <summary>
    /// Number of matching observations behind the pending candidate. This is diagnostic-only.
    /// </summary>
    public int PendingPublicationObservations { get; private set; }

    /// <summary>
    /// Read-only capabilities observed from the last published skeleton topology.
    /// </summary>
    public SkeletonCapabilityManifest CapabilityManifest { get; private set; } = SkeletonCapabilityManifest.Unavailable;

    /// <summary>
    /// Internal flag telling ArmatureManager that it should attempt to rebind profile to (another) profile whenever possible.
    /// </summary>
    public bool IsPendingProfileRebind { get; set; }

    /// <summary>
    /// For debugging purposes, each armature is assigned a globally-unique ID number upon creation.
    /// </summary>
    private static uint _nextGlobalId;
    private readonly uint _localId;

    /// <summary>
    /// Binding telling which bones are bound to each template for this armature. Built from template list in profile.
    /// </summary>
    public Dictionary<string, Template> BoneTemplateBinding { get; init; }

    /// <summary>
    /// Resolved target transforms for this armature after weighted profile evaluation.
    /// </summary>
    public Dictionary<string, BoneTransform> ResolvedBoneTransforms { get; init; }

    public AdvancedBodyScalingSettings? ActiveAdvancedBodyScalingSettings { get; private set; }
    internal AdvancedBodyScalingBoneImportanceResult ActiveBoneImportanceResult { get; private set; }
        = AdvancedBodyScalingBoneImportanceResult.CreateFallback("Not evaluated yet.", enabled: false, preferSkinWeights: true, heuristicBlend: 0f);
    internal bool BoneImportanceAppliedToPipeline { get; private set; }
    internal DeformationQualityDiagnostics DeformationQualityDiagnostics { get; private set; } = DeformationQualityDiagnostics.Empty;
    internal RuntimePerformanceMetrics PerformanceMetrics { get; } = new();
    internal RootScaleApplicationDiagnostics RootScaleDiagnostics { get; private set; } = RootScaleApplicationDiagnostics.Empty;
    internal AdvancedBodyScalingBoneImportanceRuntimeState BoneImportanceRuntimeState { get; } = new();

    internal AdvancedBodyScalingPoseCorrectiveDebugState PoseCorrectiveDebugState { get; } = new();
    internal PoseAwareJointCorrectiveDebugState PoseAwareJointCorrectiveDebugState { get; } = new();
    internal AdvancedBodyScalingFullIkRetargetingDebugState FullIkRetargetingDebugState { get; } = new();
    internal AdvancedBodyScalingMotionWarpingDebugState MotionWarpingDebugState { get; } = new();
    internal AdvancedBodyScalingFullBodyIkDebugState FullBodyIkDebugState { get; } = new();

    private readonly Dictionary<string, Vector3> _poseCorrectiveScaleMultipliers = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Vector3> _rbfPoseCorrectiveScaleMultipliers = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Vector3> _jointPoseCorrectiveScaleMultipliers = new(StringComparer.Ordinal);
    private readonly Dictionary<AdvancedBodyScalingCorrectiveRegion, AdvancedBodyScalingCorrectiveRuntimeState> _poseCorrectiveRuntimeState = new();
#if DEBUG
    private DebugPoseCorrectiveValidationSession? _debugPoseCorrectiveValidation;
    private DebugPoseCorrectiveValidationSnapshot _debugPoseCorrectiveValidationSnapshot = DebugPoseCorrectiveValidationSnapshot.Idle;
#endif
    private readonly Dictionary<string, ModelBone> _publishedBonesByName = new(StringComparer.Ordinal);
    private readonly HashSet<string> _explicitTemplateTransformNames = new(StringComparer.Ordinal);
    private readonly Dictionary<string, BoneTransform> _fullIkRetargetingCorrections = new(StringComparer.Ordinal);
    private readonly Dictionary<string, BoneTransform> _motionWarpingCorrections = new(StringComparer.Ordinal);
    private readonly Dictionary<string, BoneTransform> _fullBodyIkCorrections = new(StringComparer.Ordinal);
    private long _lastFullBodyIkSolveAtMs;
    private float _deferredFullBodyIkDeltaSeconds;
    private readonly Dictionary<string, long> _optionalLayerFailureLogAtMs = new(StringComparer.Ordinal);
    private readonly Dictionary<string, OptionalLayerHealthState> _optionalLayerHealth = new(StringComparer.Ordinal);
    private readonly AdvancedBodyScalingMotionWarpingContext _motionWarpingContext = new();
    private Vector3 _lastMotionSampleWorldPosition;
    private Vector3 _smoothedMotionDirectionWorld = Vector3.Zero;
    private float _smoothedPlanarSpeed;
    private bool _hasMotionSample;
    private (int Race, int Clan, int Gender)? _appliedAppearanceContext;
    private (int Race, int Clan, int Gender)? _pendingAppearanceContext;
    private int _pendingAppearanceContextObservations;
    private long _pendingAppearanceTransitionId;
    private long _lastAppliedAppearanceTransitionId;
    private long _trackedRevertAppearanceEpoch;

    private List<ModelBone> _activeBones;
    public IReadOnlyList<ModelBone> ActiveBones => _activeBones;

    /// <summary>
    /// Each skeleton is made up of several smaller "partial" skeletons.
    /// Each partial skeleton has its own list of bones, with a root bone at index zero.
    /// The root bone of a partial skeleton may also be a regular bone in a different partial skeleton.
    /// </summary>
    private ModelBone[][] _partialSkeletons;
    private bool _isBuilt;
    private long _lastSkeletonBuildFailureAtMs;
    private string? _lastSkeletonBuildFailure;
    private long _lastInvalidBindingRecoveryAttemptAtMs;
    private long _lastSkeletonMismatchAtMs;
    private string? _lastSkeletonMismatch;
    private string? _publishedSkeletonSignature;
    private int _profileResolutionSignature;
    private int _deformationSignature;
    private int _diagnosticsSignature;
    private bool _requiresActorReacquisitionPublication;
    private ulong _publishedNativeBindingIdentity;
    // The primary live actor is fully topology-validated before each apply pass. Cache only
    // pointer identities from that validation so ModelBone can avoid repeating string marshaling
    // for every native read/write while still rejecting a swapped Havok pose immediately.
    private nint _validatedWriteCharacterBase;
    private nint _validatedWriteSkeleton;
    private nint[] _validatedWritePoses = Array.Empty<nint>();
    private nint[] _validatedWritePoseSkeletons = Array.Empty<nint>();
    private string? _pendingSkeletonSignature;
    private long _lastCapabilityManifestFailureAtMs;
    private string? _lastCapabilityManifestFailure;
    private bool _debugNativeWriteDiagnosticsEnabled;
    private long _debugNativeWriteAttempts;
    private long _debugNativeWriteAccepted;
    private long _debugNativeWriteSkippedMissingBone;
    private long _debugNativeWriteSkippedStaleBinding;
    private long _debugNativeWriteSkippedPoseNotInSync;
    private long _debugNativeWriteSkippedUnsafeTransform;
    private int _debugNativeWriteActiveTargetBoneCount;

    #region Bone Accessors -------------------------------------------------------------------------------

    /// <summary>
    /// Gets the number of partial skeletons contained in this armature.
    /// </summary>
    public int PartialSkeletonCount => _partialSkeletons.Length;

    /// <summary>
    /// Get the list of bones belonging to the partial skeleton at the given index.
    /// </summary>
    public ModelBone[] this[int i]
    {
        get => _partialSkeletons[i];
    }

    /// <summary>
    /// Returns the number of bones contained within the partial skeleton with the given index.
    /// </summary>
    public int GetBoneCountOfPartial(int partialIndex) => _partialSkeletons[partialIndex].Length;

    /// <summary>
    /// Get the bone at index 'j' within the partial skeleton at index 'i'.
    /// </summary>
    public ModelBone this[int i, int j]
    {
        get => _partialSkeletons[i][j];
    }

    /// <summary>
    /// Return the bone at the given indices, if it exists
    /// </summary>
    public ModelBone? GetBoneAt(int partialIndex, int boneIndex)
    {
        if (partialIndex >= 0 && partialIndex < _partialSkeletons.Length
            && boneIndex >= 0 && boneIndex < _partialSkeletons[partialIndex].Length)
        {
            return this[partialIndex, boneIndex];
        }

        return null;
    }

    /// <summary>
    /// Returns the root bone of the partial skeleton with the given index.
    /// </summary>
    public ModelBone GetRootBoneOfPartial(int partialIndex) => this[partialIndex, 0];

    public ModelBone MainRootBone => GetRootBoneOfPartial(0);

    /// <summary>
    /// Get the total number of bones in each partial skeleton combined.
    /// </summary>
    // In exactly one partial skeleton will the root bone be an independent bone. In all others, it's a reference to a separate, real bone.
    // For that reason we must subtract the number of duplicate bones
    public int TotalBoneCount => _partialSkeletons.Sum(x => x.Length);

    public IEnumerable<ModelBone> GetAllBones()
    {
        for (var i = 0; i < _partialSkeletons.Length; ++i)
        {
            for (var j = 0; j < _partialSkeletons[i].Length; ++j)
            {
                yield return this[i, j];
            }
        }
    }

    //----------------------------------------------------------------------------------------------------
    #endregion

    public Armature(ActorIdentifier actorIdentifier, Profile profile)
    {
        _localId = _nextGlobalId++;

        _partialSkeletons = Array.Empty<ModelBone[]>();

        BoneTemplateBinding = new Dictionary<string, Template>();
        ResolvedBoneTransforms = new Dictionary<string, BoneTransform>(StringComparer.Ordinal);
        _activeBones = new List<ModelBone>();

        ActorIdentifier = actorIdentifier;
        Profile = profile;
        IsVisible = false;

        UpdateLastSeen();

        Profile.Armatures.Add(this);

        Plugin.Logger.Debug($"Instantiated {this}, attached to {Profile}");
    }

    /// <inheritdoc/>
    public override string ToString()
    {
        return IsBuilt
            ? $"Armature (#{_localId}) on {ActorIdentifier.IncognitoDebug()} ({Profile}) with {TotalBoneCount} bone/s"
            : $"Armature (#{_localId}) on {ActorIdentifier.IncognitoDebug()} ({Profile}) with no skeleton reference";
    }

    public bool IsSkeletonUpdated(CharacterBase* cBase)
    {
        ClearNativeWriteValidation();
        if (cBase == null || cBase->Skeleton == null || !_isBuilt)
        {
            IsSkeletonBindingCurrent = false;
            LastSkeletonBindingIssue = "character base or skeleton unavailable";
            if (_isBuilt)
                MarkNativeBindingUnavailable("character base or skeleton became unavailable");
            return false;
        }

        var skeleton = cBase->Skeleton;
        if (skeleton->PartialSkeletonCount <= 0)
        {
            IsSkeletonBindingCurrent = false;
            LastSkeletonBindingIssue = "skeleton exposed no partials";
            MarkNativeBindingUnavailable("skeleton exposed no partials");
            return false;
        }

        // An actor can disappear and return with a structurally identical skeleton and even
        // recycled native addresses. A prior observed lifetime break is authoritative: do not
        // reuse ModelBone bindings until the returning actor has been published again.
        if (_requiresActorReacquisitionPublication)
        {
            IsSkeletonBindingCurrent = false;
            LastSkeletonBindingIssue = "validated actor reacquisition publication required";
            return true;
        }

        // A redraw can expose an incomplete skeleton for a frame. An empty published
        // partial is safe to keep empty, but a missing populated partial invalidates
        // the current binding until it can be validated again.
        for (var i = 0; i < skeleton->PartialSkeletonCount; ++i)
        {
            var pose = skeleton->PartialSkeletons[i].GetHavokPose(Constants.TruePoseIndex);
            if (pose == null || pose->Skeleton == null || pose->Skeleton->Bones.Length <= 0
                || pose->Skeleton->ParentIndices.Length < pose->Skeleton->Bones.Length)
            {
                if (i < _partialSkeletons.Length && _partialSkeletons[i].Length == 0)
                    continue;

                IsSkeletonBindingCurrent = false;
                LastSkeletonBindingIssue = $"partial {i} pose or parent data unavailable";
                return false;
            }
        }

        if (skeleton->PartialSkeletonCount != _partialSkeletons.Length)
        {
            IsSkeletonBindingCurrent = false;
            LogSkeletonMismatch($"partial count mismatch. Expected {_partialSkeletons.Length}, found {skeleton->PartialSkeletonCount}");
            return true;
        }

        for (var i = 0; i < skeleton->PartialSkeletonCount; ++i)
        {
            var newPose = skeleton->PartialSkeletons[i].GetHavokPose(Constants.TruePoseIndex);
            if (newPose == null || newPose->Skeleton == null)
            {
                if (_partialSkeletons[i].Length == 0)
                    continue;

                IsSkeletonBindingCurrent = false;
                LastSkeletonBindingIssue = $"partial {i} pose unavailable";
                return false;
            }

            if (newPose->Skeleton->Bones.Length != _partialSkeletons[i].Length)
            {
                IsSkeletonBindingCurrent = false;
                LogSkeletonMismatch($"partial {i} bone count mismatch. Expected {_partialSkeletons[i].Length}, found {newPose->Skeleton->Bones.Length}");
                return true;
            }

            for (var boneIndex = 0; boneIndex < newPose->Skeleton->Bones.Length; boneIndex++)
            {
                var expectedBone = _partialSkeletons[i][boneIndex];
                var actualName = newPose->Skeleton->Bones[boneIndex].Name.String;
                if (!string.Equals(actualName, expectedBone.BoneName, StringComparison.Ordinal))
                {
                    IsSkeletonBindingCurrent = false;
                    LogSkeletonMismatch($"partial {i} bone name mismatch at index {boneIndex}. Expected {expectedBone.BoneName}, found {actualName ?? "<null>"}");
                    return true;
                }

                var actualParent = newPose->Skeleton->ParentIndices[boneIndex];
                if (actualParent != expectedBone.ParentBoneIndex)
                {
                    IsSkeletonBindingCurrent = false;
                    LogSkeletonMismatch($"partial {i} parent mismatch at index {boneIndex} ({expectedBone.BoneName}). Expected {expectedBone.ParentBoneIndex}, found {actualParent}");
                    return true;
                }
            }
        }

        // A race/model redraw can replace Havok and pose objects while preserving names and
        // parent indices. Structural equivalence is not sufficient for native binding safety.
        var nativeBindingIdentity = BuildNativeBindingIdentity(cBase);
        if (nativeBindingIdentity != _publishedNativeBindingIdentity)
        {
            IsSkeletonBindingCurrent = false;
            LogSkeletonMismatch("native skeleton binding identity changed while topology remained valid");
            return true;
        }

        IsSkeletonBindingCurrent = true;
        CaptureNativeWriteValidation(cBase);
        LastSkeletonBindingIssue = "none";
        _pendingSkeletonSignature = null;
        PendingPublicationObservations = 0;
        _lastSkeletonMismatch = null;
        _lastSkeletonMismatchAtMs = 0;
        return false;
    }

    /// <summary>
    /// Rebuild the armature using the provided character base as a reference.
    /// </summary>
    internal bool RebuildSkeleton(
        CharacterBase* cBase,
        bool enableSoftScaleLimits = true,
        bool enableAutomaticChildCompensation = true,
        AdvancedBodyScalingSettings? advancedBodyScaling = null,
        AdvancedBodyScalingBoneImportanceResult? boneImportance = null)
    {
        ModelBone[][] candidate;
        string failureReason;
        try
        {
            if (!TryBuildSkeletonCandidate(cBase, out candidate, out failureReason))
            {
                ClearNativeWriteValidation();
                IsSkeletonBindingCurrent = false;
                LogSkeletonBuildFailure(failureReason);
                return false;
            }
        }
        catch (Exception ex)
        {
            ClearNativeWriteValidation();
            IsSkeletonBindingCurrent = false;
            LogSkeletonBuildFailure($"candidate construction threw {ex.GetType().Name}");
            return false;
        }

        var candidateSignature = BuildSkeletonSignature(candidate);
        var candidateNativeBindingIdentity = BuildNativeBindingIdentity(cBase);
        var publishedIdentity = new ArmaturePublicationIdentity(_publishedSkeletonSignature ?? string.Empty, _publishedNativeBindingIdentity.ToString("X16"));
        var candidatePublicationIdentity = new ArmaturePublicationIdentity(candidateSignature, candidateNativeBindingIdentity.ToString("X16"));
        var publicationChanged = _requiresActorReacquisitionPublication
            || ArmatureBindingLifecycle.RequiresPublication(_isBuilt, publishedIdentity, candidatePublicationIdentity);

        // A redraw can briefly expose a structurally valid but incomplete replacement.
        // Require one consistent observation before replacing an existing known-good snapshot,
        // including a structurally equivalent native skeleton replacement.
        if (_isBuilt && publicationChanged)
        {
            if (!string.Equals(candidatePublicationIdentity.PendingKey, _pendingSkeletonSignature, StringComparison.Ordinal))
            {
                _pendingSkeletonSignature = candidatePublicationIdentity.PendingKey;
                PendingPublicationObservations = 1;
                IsSkeletonBindingCurrent = false;
                RecordRevertRecoveryObservation(published: false);
                LogSkeletonBuildPending();
                return false;
            }

            PendingPublicationObservations = 2;
        }

        var nextRevision = publicationChanged ? SkeletonRevision + 1 : SkeletonRevision;
        var nextManifest = CapabilityManifest;
        if (publicationChanged)
        {
            var manifestStarted = PerformanceMetrics.Start();
            try
            {
                nextManifest = BoneData.EvaluateCapabilityManifest(
                    BuildObservedSkeletonBones(candidate),
                    candidate.Select(static partial => partial.Length).ToArray(),
                    nextRevision,
                    stableObservations: _isBuilt ? 2 : 1,
                    bindingCurrent: true);
            }
            catch (Exception ex)
            {
                // Diagnostics must never prevent a validated armature from being published.
                nextManifest = SkeletonCapabilityManifest.Unavailable;
                LogCapabilityManifestFailure(ex.GetType().Name);
            }
            finally
            {
                PerformanceMetrics.Record("manifest-build", manifestStarted);
            }
        }

        _partialSkeletons = candidate;
        _isBuilt = true;
        IsSkeletonBindingCurrent = true;
        SkeletonRevision = nextRevision;
        NativeBindingGeneration = publicationChanged ? NativeBindingGeneration + 1 : NativeBindingGeneration;
        CapabilityManifest = nextManifest;
        _publishedSkeletonSignature = candidateSignature;
        _publishedNativeBindingIdentity = candidateNativeBindingIdentity;
        CaptureNativeWriteValidation(cBase);
        _pendingSkeletonSignature = null;
        PendingPublicationObservations = 0;
        _requiresActorReacquisitionPublication = false;
        _lastSkeletonBuildFailure = null;
        _lastSkeletonBuildFailureAtMs = 0;
        _lastInvalidBindingRecoveryAttemptAtMs = 0;
        LastSkeletonBindingIssue = "none";
        _lastSkeletonMismatch = null;
        _lastSkeletonMismatchAtMs = 0;
        if (publicationChanged)
            RecordRevertRecoveryObservation(published: true);

        RebuildBoneTemplateBinding(
            enableSoftScaleLimits,
            enableAutomaticChildCompensation,
            advancedBodyScaling,
            boneImportance,
            publicationChanged ? "skeleton publication" : "skeleton validation refresh"); //todo: intentionally not calling ArmatureChanged.Type.Updated because this is pending rewrite

        Plugin.Logger.Debug($"Rebuilt {this}");
        return true;
    }

    public BoneTransform? GetAppliedBoneTransform(string boneName)
    {
#if DEBUG
        if (_debugPoseCorrectiveValidation?.TryGetSyntheticTransform(boneName, out var syntheticTransform) == true)
            return syntheticTransform;
#endif
        var liveBone = GetAllBones().FirstOrDefault(b => b.BoneName == boneName && b.AppliedTransform != null);
        if (liveBone?.AppliedTransform != null)
            return liveBone.AppliedTransform;

        if (ResolvedBoneTransforms.TryGetValue(boneName, out var boneTransform))
            return boneTransform;

        return null;
    }

    internal bool TryGetPublishedBone(string boneName, out ModelBone bone)
        => _publishedBonesByName.TryGetValue(boneName, out bone!);

    internal bool IsExplicitTemplateTransform(string boneName)
        => _explicitTemplateTransformNames.Contains(boneName);

#if DEBUG
    internal DebugPoseCorrectiveValidationSnapshot DebugPoseCorrectiveValidationSnapshot
        => _debugPoseCorrectiveValidation?.Snapshot() ?? _debugPoseCorrectiveValidationSnapshot;

    internal bool TryStartDebugPoseCorrectiveValidation(out string reason)
    {
        if (_debugPoseCorrectiveValidation is { IsComplete: false })
        {
            reason = "A bounded RBF validation fixture is already running for this armature.";
            return false;
        }

        if (ActiveAdvancedBodyScalingSettings == null)
        {
            reason = "No resolved Advanced Body Scaling settings are available for this armature.";
            _debugPoseCorrectiveValidationSnapshot = new DebugPoseCorrectiveValidationSnapshot(
                "unavailable", reason, "none", 0, 25, 0, 0, 0, 0f, 0f, 0f, 0f, 0f, 0f, false, true, 0L, Array.Empty<string>(), Array.Empty<DebugPoseCorrectiveNativeSample>(), Array.Empty<DebugPoseCorrectiveTimingSnapshot>());
            return false;
        }

        if (!DebugPoseCorrectiveValidationSession.TryCreate(this, ActiveAdvancedBodyScalingSettings, out var session, out reason))
        {
            _debugPoseCorrectiveValidationSnapshot = new DebugPoseCorrectiveValidationSnapshot(
                "unavailable", reason, "none", 0, 25, 0, 0, 0, 0f, 0f, 0f, 0f, 0f, 0f, false, true, 0L, Array.Empty<string>(), Array.Empty<DebugPoseCorrectiveNativeSample>(), Array.Empty<DebugPoseCorrectiveTimingSnapshot>());
            return false;
        }

        _debugPoseCorrectiveValidation = session;
        _debugPoseCorrectiveValidationSnapshot = session!.Snapshot();
        return true;
    }

    internal bool TryGetDebugPoseCorrectiveDriverOverride(AdvancedBodyScalingCorrectiveRegion region, int expectedCount, out IReadOnlyList<float> drivers)
    {
        if (_debugPoseCorrectiveValidation?.TryGetDriverOverride(region, expectedCount, out drivers) == true)
            return true;

        drivers = Array.Empty<float>();
        return false;
    }

    internal bool IsDebugPoseCorrectiveValidationRegion(AdvancedBodyScalingCorrectiveRegion region)
        => _debugPoseCorrectiveValidation?.IsDrivenRegion(region) == true;

    internal bool HasDebugPoseCorrectiveValidation
        => _debugPoseCorrectiveValidation is { IsComplete: false };

    internal void FilterDebugPoseCorrectiveValidationMultipliers(Dictionary<string, Vector3> scaleMultipliers)
        => _debugPoseCorrectiveValidation?.FilterScaleMultipliers(scaleMultipliers);

    internal bool TryGetDebugPoseCorrectiveValidationTransform(string boneName, out BoneTransform transform)
    {
        if (_debugPoseCorrectiveValidation?.TryGetNativeBaselineTransform(boneName, out transform) == true)
            return true;

        transform = null!;
        return false;
    }

    internal IReadOnlyList<ModelBone> GetDebugPoseCorrectiveValidationBones()
    {
        if (_debugPoseCorrectiveValidation is null || _debugPoseCorrectiveValidation.IsComplete)
            return Array.Empty<ModelBone>();

        return _debugPoseCorrectiveValidation.GetTargetBoneNames()
            .Select(name => TryGetPublishedBone(name, out var bone) ? bone : null)
            .Where(static bone => bone != null)
            .Cast<ModelBone>()
            .ToArray();
    }

    internal void RecordDebugPoseCorrectiveValidationApplication(
        string boneName,
        Vector3 beforeTranslation,
        Quaternion beforeRotation,
        Vector3 beforeScale,
        Vector3 afterTranslation,
        Quaternion afterRotation,
        Vector3 afterScale,
        Vector3 correctiveScale)
    {
        _debugPoseCorrectiveValidation?.RecordNativeApplication(
            boneName,
            beforeTranslation,
            beforeRotation,
            beforeScale,
            afterTranslation,
            afterRotation,
            afterScale,
            correctiveScale);
    }

    internal void CompleteDebugPoseCorrectiveValidationFrame()
    {
        if (_debugPoseCorrectiveValidation is null)
            return;

        _debugPoseCorrectiveValidation.CompleteFrame();
        _debugPoseCorrectiveValidationSnapshot = _debugPoseCorrectiveValidation.Snapshot();
        if (_debugPoseCorrectiveValidation.IsComplete)
            _debugPoseCorrectiveValidation = null;
    }

    internal void RecordDebugPoseCorrectiveValidationEvaluation(double elapsedMilliseconds)
        => _debugPoseCorrectiveValidation?.RecordEvaluationMilliseconds(elapsedMilliseconds);
#endif

    /// <summary>
    /// Update last time actor for this armature was last seen in the game
    /// </summary>
    public void UpdateLastSeen(DateTime? dateTime = null)
    {
        if(dateTime == null)
            dateTime = DateTime.UtcNow;

        LastSeen = (DateTime)dateTime;
    }

    private unsafe bool TryBuildSkeletonCandidate(CharacterBase* cBase, out ModelBone[][] candidate, out string failureReason)
    {
        candidate = Array.Empty<ModelBone[]>();
        failureReason = string.Empty;

        if (cBase == null || cBase->Skeleton == null)
        {
            failureReason = "character base or skeleton was unavailable";
            return false;
        }

        var skeleton = cBase->Skeleton;
        if (skeleton->PartialSkeletonCount <= 0)
        {
            failureReason = "skeleton had no partials";
            return false;
        }

        var newPartials = new ModelBone[skeleton->PartialSkeletonCount][];
        var parentIndices = new int[skeleton->PartialSkeletonCount][];

        for (var partialIndex = 0; partialIndex < skeleton->PartialSkeletonCount; ++partialIndex)
        {
            var pose = skeleton->PartialSkeletons[partialIndex].GetHavokPose(Constants.TruePoseIndex);
            if (pose == null || pose->Skeleton == null)
            {
                // Optional draw-object partials may not expose a Havok pose at all.
                // Retain their index as an empty slot so body partial indices stay
                // stable without blocking the usable skeleton from being published.
                newPartials[partialIndex] = Array.Empty<ModelBone>();
                parentIndices[partialIndex] = Array.Empty<int>();
                continue;
            }

            var boneCount = pose->Skeleton->Bones.Length;
            if (boneCount <= 0)
            {
                newPartials[partialIndex] = Array.Empty<ModelBone>();
                parentIndices[partialIndex] = Array.Empty<int>();
                continue;
            }

            if (pose->Skeleton->ParentIndices.Length < boneCount)
            {
                failureReason = $"partial {partialIndex} had incomplete parent-index data";
                return false;
            }

            var bones = new ModelBone[boneCount];
            var parents = new int[boneCount];
            var names = new HashSet<string>(StringComparer.Ordinal);
            for (var boneIndex = 0; boneIndex < boneCount; ++boneIndex)
            {
                var boneName = pose->Skeleton->Bones[boneIndex].Name.String;
                if (string.IsNullOrWhiteSpace(boneName))
                {
                    failureReason = $"partial {partialIndex} had an unnamed bone at index {boneIndex}";
                    return false;
                }

                if (!names.Add(boneName))
                {
                    failureReason = $"partial {partialIndex} repeated bone name {boneName}";
                    return false;
                }

                var parentIndex = pose->Skeleton->ParentIndices[boneIndex];
                if (parentIndex < -1 || parentIndex >= boneCount || parentIndex == boneIndex)
                {
                    failureReason = $"partial {partialIndex} had invalid parent index {parentIndex} for bone {boneName} at index {boneIndex}";
                    return false;
                }

                bones[boneIndex] = new ModelBone(this, boneName, partialIndex, boneIndex);
                parents[boneIndex] = parentIndex;
            }

            if (!SkeletonTopologyValidator.HasValidTopology(parents))
            {
                failureReason = $"partial {partialIndex} had an invalid parent topology";
                return false;
            }

            newPartials[partialIndex] = bones;
            parentIndices[partialIndex] = parents;
        }

        if (newPartials.All(static partial => partial.Length == 0))
        {
            failureReason = "no partial exposed a usable Havok pose";
            return false;
        }

        // Parent/child relationships are only attached after every partial has passed validation.
        for (var partialIndex = 0; partialIndex < newPartials.Length; ++partialIndex)
        {
            var bones = newPartials[partialIndex];
            var parents = parentIndices[partialIndex];
            for (var boneIndex = 0; boneIndex < bones.Length; ++boneIndex)
            {
                var parentIndex = parents[boneIndex];
                if (parentIndex < 0)
                    continue;

                bones[boneIndex].AddParent(partialIndex, parentIndex);
                bones[parentIndex].AddChild(partialIndex, boneIndex);
            }
        }

        var knownBones = new List<ModelBone>();
        foreach (var partial in newPartials)
        {
            foreach (var bone in partial)
            {
                foreach (var existing in knownBones)
                {
                    if (!AreTwinnedNames(bone.BoneName, existing.BoneName))
                        continue;

                    bone.AddTwin(existing.PartialSkeletonIndex, existing.BoneIndex);
                    existing.AddTwin(bone.PartialSkeletonIndex, bone.BoneIndex);
                    break;
                }

                knownBones.Add(bone);
            }
        }

        BoneData.LogNewBones(knownBones.Select(static bone => bone.BoneName).ToArray());
        candidate = newPartials;
        return true;
    }

    /// <summary>
    /// Limits candidate construction while a redraw has left the published binding unsafe.
    /// The manager still reacts immediately to an actual topology change; this only covers the
    /// otherwise-ambiguous "not current but not changed" state.
    /// </summary>
    internal bool ShouldAttemptInvalidBindingRecovery()
    {
        const long recoveryIntervalMs = 250;
        if (IsSkeletonBindingCurrent || !_isBuilt)
            return false;

        var now = Environment.TickCount64;
        if (now - _lastInvalidBindingRecoveryAttemptAtMs < recoveryIntervalMs)
            return false;

        _lastInvalidBindingRecoveryAttemptAtMs = now;
        return true;
    }

    /// <summary>
    /// Records one observed loss of the live actor/native lifetime. The profile remains attached;
    /// only the old ModelBone/native binding is invalidated until a new validated publication.
    /// </summary>
    internal void MarkNativeBindingUnavailable(string reason)
    {
        ClearNativeWriteValidation();
        if (!_isBuilt || _requiresActorReacquisitionPublication)
            return;

        _requiresActorReacquisitionPublication = true;
        ActorLifetimeGeneration++;
        IsSkeletonBindingCurrent = false;
        _pendingSkeletonSignature = null;
        PendingPublicationObservations = 0;
        Plugin.Logger.Debug($"Observed live actor/native lifetime loss for armature {_localId}: {reason}. A validated reacquisition publication is required before writes resume.");
    }

    /// <summary>
    /// Returns true only for the exact primary native pose pointers that passed the full topology
    /// check in the current armature refresh. This is a performance cache, never a substitute for
    /// the normal null, bounds, pose-sync, or transform-safety checks at each write.
    /// </summary>
    internal bool HasValidatedNativeWritePose(CharacterBase* cBase, int partialIndex, hkaPose* pose)
        => IsSkeletonBindingCurrent
           && cBase != null
           && cBase->Skeleton != null
           && pose != null
           && pose->Skeleton != null
           && (nint)cBase == _validatedWriteCharacterBase
           && (nint)cBase->Skeleton == _validatedWriteSkeleton
           && partialIndex >= 0
           && partialIndex < _validatedWritePoses.Length
           && _validatedWritePoses[partialIndex] == (nint)pose
           && _validatedWritePoseSkeletons[partialIndex] == (nint)pose->Skeleton;

    /// <summary>
    /// Observes the authoritative Glamourer appearance-operation epoch. A newer epoch supersedes
    /// any older uncommitted settle work, but never promotes it before a stable binding rebuild.
    /// </summary>
    internal string ObserveGlamourerAppearanceEpoch(
        long appearanceEpoch,
        bool transitionActive,
        string transitionState,
        string operationType,
        long finalizedAtMs = 0)
    {
        if (transitionActive && appearanceEpoch > 0)
        {
            if (appearanceEpoch > CurrentAppearanceEpoch)
            {
                var supersededEpoch = _pendingAppearanceTransitionId > _lastAppliedAppearanceTransitionId
                    ? _pendingAppearanceTransitionId
                    : 0;
                CurrentAppearanceEpoch = appearanceEpoch;
                _pendingAppearanceTransitionId = Math.Max(_pendingAppearanceTransitionId, appearanceEpoch);
                AppearanceEpochState = transitionState;
                CurrentAppearanceOperationType = string.IsNullOrWhiteSpace(operationType) ? "appearance" : operationType;
                ResetRevertRecoveryTiming(appearanceEpoch, CurrentAppearanceOperationType);
                var eventName = supersededEpoch > 0 && supersededEpoch < appearanceEpoch
                    ? $"AppearanceEpochSuperseded {supersededEpoch}->{appearanceEpoch} ({CurrentAppearanceOperationType})"
                    : $"GlamourerAppearanceChanged epoch {appearanceEpoch} ({CurrentAppearanceOperationType})";
                LastAppearanceLifecycleEvent = eventName;
                return eventName;
            }

            var previousState = AppearanceEpochState;
            AppearanceEpochState = transitionState;
            CurrentAppearanceOperationType = string.IsNullOrWhiteSpace(operationType) ? CurrentAppearanceOperationType : operationType;
            var finalized = !string.Equals(previousState, transitionState, StringComparison.Ordinal)
                   && string.Equals(transitionState, "settling", StringComparison.Ordinal)
                ? $"GlamourerAppearanceFinalized epoch {appearanceEpoch} ({CurrentAppearanceOperationType})"
                : string.Empty;
            if (_trackedRevertAppearanceEpoch == appearanceEpoch
                && string.Equals(transitionState, "settling", StringComparison.Ordinal)
                && finalizedAtMs > 0)
                RevertFinalizedAtMs = finalizedAtMs;
            if (!string.IsNullOrWhiteSpace(finalized))
                LastAppearanceLifecycleEvent = finalized;
            return finalized;
        }

        var previousIdleState = AppearanceEpochState;
        AppearanceEpochState = _pendingAppearanceTransitionId > _lastAppliedAppearanceTransitionId
            ? "ready for stable refresh"
            : "idle";
        var queued = !string.Equals(previousIdleState, AppearanceEpochState, StringComparison.Ordinal)
               && string.Equals(AppearanceEpochState, "ready for stable refresh", StringComparison.Ordinal)
            ? $"StableAppearanceRebindQueued epoch {_pendingAppearanceTransitionId}"
            : string.Empty;
        if (!string.IsNullOrWhiteSpace(queued) && _trackedRevertAppearanceEpoch == _pendingAppearanceTransitionId)
            RevertStableRebindQueuedAtMs = Environment.TickCount64;
        if (!string.IsNullOrWhiteSpace(queued))
            LastAppearanceLifecycleEvent = queued;
        return queued;
    }

    internal bool TryGetPendingStableAppearanceRefresh(
        bool transitionActive,
        out long appearanceEpoch,
        out string refreshReason)
    {
        appearanceEpoch = 0;
        refreshReason = string.Empty;
        if (transitionActive || _pendingAppearanceTransitionId <= _lastAppliedAppearanceTransitionId)
            return false;

        appearanceEpoch = _pendingAppearanceTransitionId;
        AppearanceEpochState = "ready for stable refresh";
        refreshReason = "Glamourer appearance epoch settled";
        return true;
    }

    /// <summary>
    /// Uses race/customize identity only when no Glamourer appearance epoch is pending. This remains
    /// a bounded fallback for missing/incomplete IPC events, not a competing lifecycle authority.
    /// </summary>
    internal bool ShouldRefreshForAppearanceContextFallback(
        int race,
        int clan,
        int gender,
        bool glamourerTransitionActive,
        out string refreshReason)
    {
        refreshReason = string.Empty;
        if (_pendingAppearanceTransitionId > _lastAppliedAppearanceTransitionId)
            return false;

        var context = (race, clan, gender);
        if (!_appliedAppearanceContext.HasValue)
        {
            // The first successful skeleton/template publication establishes the baseline.  It must
            // not look like an appearance change merely because the armature was just created.
            _appliedAppearanceContext = context;
        }
        else if (_appliedAppearanceContext.Value != context)
        {
            if (_pendingAppearanceContext != context)
            {
                _pendingAppearanceContext = context;
                _pendingAppearanceContextObservations = 1;
            }
            else if (!glamourerTransitionActive)
            {
                _pendingAppearanceContextObservations++;
            }
        }
        else if (!glamourerTransitionActive)
        {
            _pendingAppearanceContext = null;
            _pendingAppearanceContextObservations = 0;
        }

        if (glamourerTransitionActive)
            return false;

        if (_pendingAppearanceContext.HasValue && _pendingAppearanceContextObservations >= 2)
        {
            refreshReason = "actor race/customize context changed";
            return true;
        }

        return false;
    }

    /// <summary>
    /// Records fallback customize inputs used by a successful binding rebuild.
    /// </summary>
    internal void MarkAppearanceContextBindingApplied(int race, int clan, int gender)
    {
        _appliedAppearanceContext = (race, clan, gender);
        _pendingAppearanceContext = null;
        _pendingAppearanceContextObservations = 0;
    }

    /// <summary>
    /// Commits only the exact stable Glamourer epoch whose rebuild completed. A newer epoch remains
    /// pending if it arrived while this rebuild was in progress.
    /// </summary>
    internal void MarkStableAppearanceEpochApplied(long appearanceEpoch, string reason)
    {
        if (appearanceEpoch <= 0)
            return;

        _lastAppliedAppearanceTransitionId = Math.Max(_lastAppliedAppearanceTransitionId, appearanceEpoch);
        if (_pendingAppearanceTransitionId <= appearanceEpoch)
            _pendingAppearanceTransitionId = 0;

        LastAppearanceRebindEpoch = appearanceEpoch;
        LastAppearanceRebindReason = reason;
        LastAppearanceLifecycleEvent = $"StableAppearanceRebindCompleted epoch {appearanceEpoch}";
        if (_trackedRevertAppearanceEpoch == appearanceEpoch)
            RevertStableRebindCompletedAtMs = Environment.TickCount64;
        AppearanceEpochState = _pendingAppearanceTransitionId > _lastAppliedAppearanceTransitionId
            ? "ready for stable refresh"
            : "idle";
    }

    internal void RecordRevertRecoveryObservation(bool published)
    {
        if (_trackedRevertAppearanceEpoch == 0)
            return;

        var now = Environment.TickCount64;
        if (!published)
        {
            if (RevertFirstValidRecoveryObservationAtMs == 0)
                RevertFirstValidRecoveryObservationAtMs = now;
            return;
        }

        RevertPublicationAtMs = now;
    }

    private void ResetRevertRecoveryTiming(long appearanceEpoch, string operationType)
    {
        _trackedRevertAppearanceEpoch = operationType.Contains("revert", StringComparison.OrdinalIgnoreCase)
            ? appearanceEpoch
            : 0;
        RevertFinalizedAtMs = 0;
        RevertStableRebindQueuedAtMs = 0;
        RevertFirstValidRecoveryObservationAtMs = 0;
        RevertPublicationAtMs = 0;
        RevertStableRebindCompletedAtMs = 0;
    }

    public SkeletonCapabilityManifest GetCapabilityManifestSnapshot()
        => CapabilityManifest.WithBindingState(IsSkeletonBindingCurrent);

    private static string BuildSkeletonSignature(ModelBone[][] partials)
    {
        var builder = new StringBuilder();
        foreach (var partial in partials)
        {
            builder.Append(partial.Length).Append(':');
            foreach (var bone in partial)
            {
                builder.Append(bone.BoneName)
                    .Append('@')
                    .Append(bone.ParentBoneIndex)
                    .Append(';');
            }

            builder.Append('|');
        }

        return builder.ToString();
    }

    /// <summary>
    /// Private lifecycle identity for detecting backing Havok replacement. It is never exposed
    /// as a structural fingerprint or serialized diagnostic because raw addresses are ephemeral.
    /// </summary>
    private static unsafe ulong BuildNativeBindingIdentity(CharacterBase* cBase)
    {
        if (cBase == null || cBase->Skeleton == null)
            return 0;

        var skeleton = cBase->Skeleton;
        var hash = 14695981039346656037UL;
        MixNativeBindingIdentity(ref hash, (nuint)cBase);
        MixNativeBindingIdentity(ref hash, (nuint)skeleton);
        MixNativeBindingIdentity(ref hash, (uint)skeleton->PartialSkeletonCount);
        for (var partialIndex = 0; partialIndex < skeleton->PartialSkeletonCount; ++partialIndex)
        {
            var pose = skeleton->PartialSkeletons[partialIndex].GetHavokPose(Constants.TruePoseIndex);
            MixNativeBindingIdentity(ref hash, (uint)partialIndex);
            MixNativeBindingIdentity(ref hash, (nuint)pose);
            if (pose != null)
                MixNativeBindingIdentity(ref hash, (nuint)pose->Skeleton);
        }

        return hash;
    }

    private void CaptureNativeWriteValidation(CharacterBase* cBase)
    {
        ClearNativeWriteValidation();
        if (cBase == null || cBase->Skeleton == null)
            return;

        var skeleton = cBase->Skeleton;
        if (skeleton->PartialSkeletonCount <= 0)
            return;

        _validatedWriteCharacterBase = (nint)cBase;
        _validatedWriteSkeleton = (nint)skeleton;
        _validatedWritePoses = new nint[skeleton->PartialSkeletonCount];
        _validatedWritePoseSkeletons = new nint[skeleton->PartialSkeletonCount];
        for (var partialIndex = 0; partialIndex < skeleton->PartialSkeletonCount; ++partialIndex)
        {
            var pose = skeleton->PartialSkeletons[partialIndex].GetHavokPose(Constants.TruePoseIndex);
            _validatedWritePoses[partialIndex] = (nint)pose;
            _validatedWritePoseSkeletons[partialIndex] = pose == null ? 0 : (nint)pose->Skeleton;
        }
    }

    private void ClearNativeWriteValidation()
    {
        _validatedWriteCharacterBase = 0;
        _validatedWriteSkeleton = 0;
        _validatedWritePoses = Array.Empty<nint>();
        _validatedWritePoseSkeletons = Array.Empty<nint>();
    }

    private static void MixNativeBindingIdentity(ref ulong hash, nuint value)
    {
        hash ^= (ulong)value;
        hash *= 1099511628211UL;
    }

    private static IReadOnlyList<ObservedSkeletonBone> BuildObservedSkeletonBones(ModelBone[][] partials)
        => partials.SelectMany((partial, partialIndex) => partial.Select((bone, boneIndex) =>
            new ObservedSkeletonBone(partialIndex, boneIndex, bone.BoneName, bone.ParentBoneIndex))).ToArray();

    private void LogSkeletonBuildFailure(string failureReason)
    {
        LastSkeletonBindingIssue = failureReason;
        const long failureLogIntervalMs = 5000;
        var now = Environment.TickCount64;
        if (string.Equals(_lastSkeletonBuildFailure, failureReason, StringComparison.Ordinal)
            && now - _lastSkeletonBuildFailureAtMs < failureLogIntervalMs)
            return;

        _lastSkeletonBuildFailure = failureReason;
        _lastSkeletonBuildFailureAtMs = now;
        Plugin.Logger.Warning($"Retained the last-known-good skeleton for armature {_localId}: {failureReason}.");
    }

    private void LogSkeletonBuildPending()
    {
        const long pendingLogIntervalMs = 5000;
        const string pendingReason = "waiting for a second matching replacement snapshot";
        LastSkeletonBindingIssue = pendingReason;
        var now = Environment.TickCount64;
        if (string.Equals(_lastSkeletonBuildFailure, pendingReason, StringComparison.Ordinal)
            && now - _lastSkeletonBuildFailureAtMs < pendingLogIntervalMs)
            return;

        _lastSkeletonBuildFailure = pendingReason;
        _lastSkeletonBuildFailureAtMs = now;
        Plugin.Logger.Debug($"Retained the last-known-good skeleton for armature {_localId}: {pendingReason}.");
    }

    private void LogCapabilityManifestFailure(string failureReason)
    {
        const long failureLogIntervalMs = 5000;
        var now = Environment.TickCount64;
        if (string.Equals(_lastCapabilityManifestFailure, failureReason, StringComparison.Ordinal)
            && now - _lastCapabilityManifestFailureAtMs < failureLogIntervalMs)
            return;

        _lastCapabilityManifestFailure = failureReason;
        _lastCapabilityManifestFailureAtMs = now;
        Plugin.Logger.Warning($"Could not build the skeleton capability manifest for armature {_localId}: {failureReason}. The armature remains usable.");
    }

    private void LogSkeletonMismatch(string mismatch)
    {
        LastSkeletonBindingIssue = mismatch;
        const long mismatchLogIntervalMs = 5000;
        var now = Environment.TickCount64;
        if (string.Equals(_lastSkeletonMismatch, mismatch, StringComparison.Ordinal)
            && now - _lastSkeletonMismatchAtMs < mismatchLogIntervalMs)
            return;

        _lastSkeletonMismatch = mismatch;
        _lastSkeletonMismatchAtMs = now;
        Plugin.Logger.Debug($"Skeleton changed for armature {_localId}: {mismatch}. Rebinding armature.");
    }

    internal void RebuildBoneTemplateBinding(
        bool enableSoftScaleLimits = true,
        bool enableAutomaticChildCompensation = true,
        AdvancedBodyScalingSettings? advancedBodyScaling = null,
        AdvancedBodyScalingBoneImportanceResult? boneImportance = null,
        string rebuildReason = "unspecified")
    {
        ActiveAdvancedBodyScalingSettings = advancedBodyScaling?.DeepCopy();
        ActiveBoneImportanceResult = boneImportance ?? AdvancedBodyScalingBoneImportanceResult.CreateFallback(
            advancedBodyScaling?.ModelDerivedBoneImportanceEnabled == true
                ? "No live actor model was resolved for this evaluation, so heuristic fallback remained active."
                : "Model-derived bone importance is disabled for this evaluation.",
            enabled: advancedBodyScaling?.ModelDerivedBoneImportanceEnabled == true,
            preferSkinWeights: advancedBodyScaling?.PreferTrueSkinWeightImportance ?? true,
            heuristicBlend: advancedBodyScaling?.BoneImportanceHeuristicBlend ?? 0f);
        BoneImportanceAppliedToPipeline = false;

        if (ActiveAdvancedBodyScalingSettings == null)
        {
            ClearPoseCorrectives();
            ClearFullIkRetargeting();
            ClearMotionWarping();
        }

        ClearFullIkRetargeting();
        ClearMotionWarping();
        ClearFullBodyIk();

        var profileResolutionStarted = PerformanceMetrics.Start();
        var manifest = GetCapabilityManifestSnapshot();
        var resolution = ProfileTransformResolver.Resolve(Profile, manifest);
        PerformanceMetrics.Record("profile-resolution", profileResolutionStarted);
        var effectiveTransforms = resolution.EffectiveTransforms;
        var explicitTransformNames = effectiveTransforms.Keys.ToHashSet(StringComparer.Ordinal);
        _explicitTemplateTransformNames.Clear();
        _explicitTemplateTransformNames.UnionWith(explicitTransformNames);

        var deformationStarted = PerformanceMetrics.Start();
        if (advancedBodyScaling != null && advancedBodyScaling.Enabled && advancedBodyScaling.Mode != AdvancedBodyScalingMode.Manual)
        {
            BoneImportanceAppliedToPipeline = ActiveBoneImportanceResult.ModelDerivedActive
                && advancedBodyScaling.ModelDerivedBoneImportanceEnabled
                && advancedBodyScaling.BoneImportanceHeuristicBlend > 0f;
            effectiveTransforms = AdvancedBodyScalingPipeline.Apply(effectiveTransforms, advancedBodyScaling, boneImportance: ActiveBoneImportanceResult);
        }

        var solverDiagnostics = DeformationQualitySolverDiagnostics.Inactive;
        if (advancedBodyScaling != null && advancedBodyScaling.Enabled && advancedBodyScaling.Mode != AdvancedBodyScalingMode.Manual)
        {
            var liveBones = GetAllBones().Select(static bone => bone.BoneName).ToHashSet(StringComparer.Ordinal);
            solverDiagnostics = AdvancedBodyScalingDeformationSolver.Apply(
                effectiveTransforms,
                explicitTransformNames,
                liveBones,
                manifest,
                ActiveBoneImportanceResult,
                advancedBodyScaling);
        }

        var nextProfileResolutionSignature = ComputeTransformSignature(resolution.EffectiveTransforms, manifest.Revision);
        if (nextProfileResolutionSignature != _profileResolutionSignature)
        {
            _profileResolutionSignature = nextProfileResolutionSignature;
            ProfileResolutionRevision++;
        }

        var nextDeformationSignature = ComputeTransformSignature(effectiveTransforms, manifest.Revision);
        if (nextDeformationSignature != _deformationSignature)
        {
            _deformationSignature = nextDeformationSignature;
            DeformationRevision++;
        }

        DeformationQualityDiagnostics = DeformationQualityAnalyzer.Analyze(effectiveTransforms, solverDiagnostics);
        var diagnosticsHash = new HashCode();
        diagnosticsHash.Add(DeformationQualityDiagnostics.MaxBilateralDifference);
        diagnosticsHash.Add(DeformationQualityDiagnostics.MaxContinuityDifference);
        diagnosticsHash.Add(solverDiagnostics.PrimaryContributionCount);
        diagnosticsHash.Add(solverDiagnostics.SupportContributionCount);
        diagnosticsHash.Add(solverDiagnostics.TransitionContributionCount);
        diagnosticsHash.Add(solverDiagnostics.SecondaryContributionCount);
        diagnosticsHash.Add(solverDiagnostics.DoubleContributionPreventionCount);
        diagnosticsHash.Add(solverDiagnostics.FallbackCount);
        diagnosticsHash.Add(solverDiagnostics.MaximumProportionalCorrection);
        diagnosticsHash.Add(solverDiagnostics.MaximumPostSmoothingGradient);
        diagnosticsHash.Add(solverDiagnostics.SurfaceSmoothnessAffectedBoneCount);
        var nextDiagnosticsSignature = diagnosticsHash.ToHashCode();
        if (nextDiagnosticsSignature != _diagnosticsSignature)
        {
            _diagnosticsSignature = nextDiagnosticsSignature;
            DiagnosticsRevision++;
        }
        PerformanceMetrics.Record("deformation-solve", deformationStarted);

        var bindingStarted = PerformanceMetrics.Start();
        BoneTemplateBinding.Clear();
        ResolvedBoneTransforms.Clear();
        _activeBones.Clear();
        _publishedBonesByName.Clear();

        foreach (var kvPair in resolution.BoneOwners)
            BoneTemplateBinding[kvPair.Key] = kvPair.Value;

        foreach (var kvPair in effectiveTransforms)
        {
            var adjusted = BoneRuntimeSafeguards.Apply(
                kvPair.Key,
                kvPair.Value,
                enableSoftScaleLimits,
                enableAutomaticChildCompensation);

            if (adjusted.IsEdited())
                ResolvedBoneTransforms[kvPair.Key] = adjusted;
        }

        foreach (var bone in GetAllBones())
        {
            _publishedBonesByName.TryAdd(bone.BoneName, bone);
            BoneTemplateBinding.TryGetValue(bone.BoneName, out var template);
            ResolvedBoneTransforms.TryGetValue(bone.BoneName, out var transform);
            bone.LinkToTemplate(template, transform);

            if (bone.IsActive)
                _activeBones.Add(bone);
        }

        _activeBones = _activeBones
            .OrderBy(b => b.PartialSkeletonIndex)
            .ThenBy(b => b.BoneIndex)
            .ToList();

        BoundModelBoneCount = _activeBones.Count;
        PerformanceMetrics.Record("live-modelbone-binding", bindingStarted);

        TemplateBindingRevision = SkeletonRevision;
        TemplateBindingBuildCount++;
        LastTemplateBindingBuildReason = string.IsNullOrWhiteSpace(rebuildReason) ? "unspecified" : rebuildReason;

        Plugin.Logger.Verbose($"Rebuilt template binding for armature {_localId} ({LastTemplateBindingBuildReason})");
    }

    private static int ComputeTransformSignature(IReadOnlyDictionary<string, BoneTransform> transforms, long manifestRevision)
    {
        var hash = new HashCode();
        hash.Add(manifestRevision);
        foreach (var (name, transform) in transforms.OrderBy(static pair => pair.Key, StringComparer.Ordinal))
        {
            hash.Add(name, StringComparer.Ordinal);
            hash.Add(transform.Scaling);
            hash.Add(transform.Translation);
            hash.Add(transform.Rotation);
            hash.Add(transform.LockState);
            hash.Add(transform.PinX);
            hash.Add(transform.PinY);
            hash.Add(transform.PinZ);
        }

        return hash.ToHashCode();
    }

    internal void SetDebugNativeWriteDiagnosticsEnabled(bool enabled)
    {
        if (_debugNativeWriteDiagnosticsEnabled == enabled)
            return;

        _debugNativeWriteDiagnosticsEnabled = enabled;
        if (enabled)
        {
            _debugNativeWriteAttempts = 0;
            _debugNativeWriteAccepted = 0;
            _debugNativeWriteSkippedMissingBone = 0;
            _debugNativeWriteSkippedStaleBinding = 0;
            _debugNativeWriteSkippedPoseNotInSync = 0;
            _debugNativeWriteSkippedUnsafeTransform = 0;
            _debugNativeWriteActiveTargetBoneCount = 0;
        }
    }

    internal void BeginDebugNativeWriteFrame(int activeTargetBoneCount)
    {
        if (_debugNativeWriteDiagnosticsEnabled)
            _debugNativeWriteActiveTargetBoneCount = Math.Max(activeTargetBoneCount, 0);
    }

    internal void RecordDebugNativeWriteAttempt()
    {
        if (_debugNativeWriteDiagnosticsEnabled)
            _debugNativeWriteAttempts++;
    }

    internal void RecordDebugNativeWriteOutcome(NativeTransformWriteOutcome outcome)
    {
        if (!_debugNativeWriteDiagnosticsEnabled)
            return;

        switch (outcome)
        {
            case NativeTransformWriteOutcome.Accepted:
                _debugNativeWriteAccepted++;
                break;
            case NativeTransformWriteOutcome.SkippedMissingBone:
                _debugNativeWriteSkippedMissingBone++;
                break;
            case NativeTransformWriteOutcome.SkippedStaleBinding:
                _debugNativeWriteSkippedStaleBinding++;
                break;
            case NativeTransformWriteOutcome.SkippedPoseNotInSync:
                _debugNativeWriteSkippedPoseNotInSync++;
                break;
            default:
                _debugNativeWriteSkippedUnsafeTransform++;
                break;
        }
    }

    internal ArmatureNativeWriteDiagnostics GetDebugNativeWriteDiagnostics()
        => _debugNativeWriteDiagnosticsEnabled
            ? new ArmatureNativeWriteDiagnostics(
                _debugNativeWriteAttempts,
                _debugNativeWriteAccepted,
                _debugNativeWriteSkippedMissingBone,
                _debugNativeWriteSkippedStaleBinding,
                _debugNativeWriteSkippedPoseNotInSync,
                _debugNativeWriteSkippedUnsafeTransform,
                _debugNativeWriteActiveTargetBoneCount)
            : ArmatureNativeWriteDiagnostics.Empty;

    internal void RecordRootScaleApplication(
        bool rootScaleModified,
        bool actorEligible,
        Vector3 observedBefore,
        Vector3 requested,
        Vector3 observedAfter,
        bool applied)
    {
        var previous = RootScaleDiagnostics;
        RootScaleDiagnostics = new RootScaleApplicationDiagnostics(
            previous.Attempts + 1,
            previous.Applied + (applied ? 1 : 0),
            rootScaleModified,
            actorEligible,
            observedBefore,
            requested,
            observedAfter);
    }

    public unsafe void EvaluatePoseCorrectives(CharacterBase* cBase)
    {
        if (cBase == null || ActiveAdvancedBodyScalingSettings == null)
        {
            ClearPoseCorrectives();
            return;
        }

        var started = PerformanceMetrics.Start();
        try
        {
            RunOptionalLayer(
                "pose-space correctives",
                () => AdvancedBodyScalingPoseCorrectiveSystem.Evaluate(this, cBase, ActiveAdvancedBodyScalingSettings, Profile.AdvancedBodyScalingOverrides.UseProfileOverrides, _poseCorrectiveRuntimeState, _rbfPoseCorrectiveScaleMultipliers, PoseCorrectiveDebugState),
                ClearRbfPoseCorrectives);
            RunOptionalLayer(
                "pose-aware joint correctives",
                () => AdvancedBodyScalingPoseAwareJointCorrectiveSystem.Evaluate(this, cBase, ActiveAdvancedBodyScalingSettings, _jointPoseCorrectiveScaleMultipliers, PoseAwareJointCorrectiveDebugState),
                ClearPoseAwareJointCorrectives);
            RebuildPoseCorrectiveScaleMultipliers();
        }
        finally
        {
#if DEBUG
            RecordDebugPoseCorrectiveValidationEvaluation(
                (System.Diagnostics.Stopwatch.GetTimestamp() - started) * 1000d / System.Diagnostics.Stopwatch.Frequency);
#endif
            PerformanceMetrics.Record("pose-corrective-evaluation", started);
        }
    }

    public bool TryGetPoseCorrectiveScale(string boneName, out Vector3 correctiveScale)
    {
        if (_poseCorrectiveScaleMultipliers.TryGetValue(boneName, out correctiveScale))
            return true;

        correctiveScale = Vector3.One;
        return false;
    }

    public void ClearPoseCorrectives()
    {
        _poseCorrectiveScaleMultipliers.Clear();
        _rbfPoseCorrectiveScaleMultipliers.Clear();
        _jointPoseCorrectiveScaleMultipliers.Clear();
        _poseCorrectiveRuntimeState.Clear();
        var path = AdvancedBodyScalingPoseCorrectiveSystem.DetectSupportedPath();
        var poseSettings = ActiveAdvancedBodyScalingSettings?.PoseCorrectives;
        PoseCorrectiveDebugState.Reset(
            path,
            AdvancedBodyScalingPoseCorrectiveSystem.GetPathDescription(path),
            Profile.AdvancedBodyScalingOverrides.UseProfileOverrides,
            poseSettings?.Enabled ?? false,
            poseSettings?.Strength ?? 0f,
            poseSettings?.PoseMapSharpness ?? 0f,
            poseSettings?.Damping ?? 0f,
            poseSettings?.MaxCorrectionClamp ?? 0f);
        PoseAwareJointCorrectiveDebugState.Reset(
            ActiveAdvancedBodyScalingSettings?.PoseAwareJointCorrectivesEnabled ?? false,
            ActiveAdvancedBodyScalingSettings?.PoseAwareJointCorrectivesStrength ?? 0f);
    }

    private void ClearRbfPoseCorrectives()
    {
        _rbfPoseCorrectiveScaleMultipliers.Clear();
        _poseCorrectiveRuntimeState.Clear();
        var path = AdvancedBodyScalingPoseCorrectiveSystem.DetectSupportedPath();
        var poseSettings = ActiveAdvancedBodyScalingSettings?.PoseCorrectives;
        PoseCorrectiveDebugState.Reset(
            path,
            AdvancedBodyScalingPoseCorrectiveSystem.GetPathDescription(path),
            Profile.AdvancedBodyScalingOverrides.UseProfileOverrides,
            poseSettings?.Enabled ?? false,
            poseSettings?.Strength ?? 0f,
            poseSettings?.PoseMapSharpness ?? 0f,
            poseSettings?.Damping ?? 0f,
            poseSettings?.MaxCorrectionClamp ?? 0f);
    }

    private void ClearPoseAwareJointCorrectives()
    {
        _jointPoseCorrectiveScaleMultipliers.Clear();
        PoseAwareJointCorrectiveDebugState.Reset(
            ActiveAdvancedBodyScalingSettings?.PoseAwareJointCorrectivesEnabled ?? false,
            ActiveAdvancedBodyScalingSettings?.PoseAwareJointCorrectivesStrength ?? 0f);
    }

    private void RebuildPoseCorrectiveScaleMultipliers()
    {
        _poseCorrectiveScaleMultipliers.Clear();
        foreach (var (bone, multiplier) in _rbfPoseCorrectiveScaleMultipliers)
            _poseCorrectiveScaleMultipliers[bone] = multiplier;

        foreach (var (bone, multiplier) in _jointPoseCorrectiveScaleMultipliers)
        {
            if (_poseCorrectiveScaleMultipliers.TryGetValue(bone, out var existing))
                _poseCorrectiveScaleMultipliers[bone] = existing * multiplier;
            else
                _poseCorrectiveScaleMultipliers[bone] = multiplier;
        }
    }

    public unsafe void EvaluateAndApplyFullBodyIk(CharacterBase* cBase, float deltaSeconds, bool useReducedCadence)
    {
        if (cBase == null || ActiveAdvancedBodyScalingSettings == null)
        {
            ClearFullBodyIk();
            return;
        }

        var settings = ActiveAdvancedBodyScalingSettings.FullBodyIk;
        var now = Environment.TickCount64;
        const long reducedCadenceIntervalMs = 33;
        if (useReducedCadence
            && settings.Enabled
            && settings.GlobalStrength > 0f
            && _lastFullBodyIkSolveAtMs > 0
            && now - _lastFullBodyIkSolveAtMs < reducedCadenceIntervalMs)
        {
            _deferredFullBodyIkDeltaSeconds = Math.Min(_deferredFullBodyIkDeltaSeconds + deltaSeconds, 0.10f);
            var cachedStarted = PerformanceMetrics.Start();
            try
            {
                AdvancedBodyScalingFullBodyIkSystem.ApplyCachedCorrections(cBase, this, _fullBodyIkCorrections);
            }
            finally
            {
                PerformanceMetrics.Record("fullbody-ik-cached-application", cachedStarted);
            }

            return;
        }

        var solveDeltaSeconds = Math.Min(deltaSeconds + _deferredFullBodyIkDeltaSeconds, 0.10f);
        _deferredFullBodyIkDeltaSeconds = 0f;
        _lastFullBodyIkSolveAtMs = now;
        var started = PerformanceMetrics.Start();
        try
        {
            RunOptionalLayer(
                "full-body IK",
                () =>
                {
                    AdvancedBodyScalingFullBodyIkSystem.EvaluateAndApply(
                        this,
                        cBase,
                        ActiveAdvancedBodyScalingSettings,
                        Profile.AdvancedBodyScalingOverrides.UseProfileOverrides,
                        solveDeltaSeconds,
                        _fullBodyIkCorrections,
                        FullBodyIkDebugState);

                    MotionWarpingDebugState.SetFullBodyIkFollowup(FullBodyIkDebugState.Active, FullBodyIkDebugState.Summary);
                    FullIkRetargetingDebugState.SetFullBodyIkFollowup(FullBodyIkDebugState.Active, FullBodyIkDebugState.Summary);
                },
                ClearFullBodyIk);
        }
        finally
        {
            PerformanceMetrics.Record("fullbody-ik-evaluation", started);
        }
    }

    public unsafe void EvaluateAndApplyFullIkRetargeting(CharacterBase* cBase, float deltaSeconds)
    {
        if (cBase == null || ActiveAdvancedBodyScalingSettings == null)
        {
            ClearFullIkRetargeting();
            return;
        }

        var started = PerformanceMetrics.Start();
        try
        {
            RunOptionalLayer(
                "full IK retargeting",
                () => AdvancedBodyScalingFullIkRetargetingSystem.EvaluateAndApply(
                    this,
                    cBase,
                    ActiveAdvancedBodyScalingSettings,
                    Profile.AdvancedBodyScalingOverrides.UseProfileOverrides,
                    deltaSeconds,
                    _fullIkRetargetingCorrections,
                    FullIkRetargetingDebugState),
                ClearFullIkRetargeting);
        }
        finally
        {
            PerformanceMetrics.Record("full-ik-retargeting-evaluation", started);
        }
    }

    public unsafe void EvaluateAndApplyMotionWarping(CharacterBase* cBase, float deltaSeconds)
    {
        if (cBase == null || ActiveAdvancedBodyScalingSettings == null)
        {
            ClearMotionWarping();
            return;
        }

        var started = PerformanceMetrics.Start();
        try
        {
            RunOptionalLayer(
                "motion warping",
                () => AdvancedBodyScalingMotionWarpingSystem.EvaluateAndApply(
                    this,
                    cBase,
                    ActiveAdvancedBodyScalingSettings,
                    Profile.AdvancedBodyScalingOverrides.UseProfileOverrides,
                    deltaSeconds,
                    _motionWarpingContext,
                    _motionWarpingCorrections,
                    MotionWarpingDebugState),
                ClearMotionWarping);
        }
        finally
        {
            PerformanceMetrics.Record("motion-warping-evaluation", started);
        }
    }

    public void ClearFullBodyIk()
    {
        _fullBodyIkCorrections.Clear();
        _lastFullBodyIkSolveAtMs = 0;
        _deferredFullBodyIkDeltaSeconds = 0f;
        FullBodyIkDebugState.Reset(false, Profile.AdvancedBodyScalingOverrides.UseProfileOverrides, 0, 0f);
        FullBodyIkDebugState.FinalizeState(false, false, false, false, 0f, 0f, 0f, "Full-body IK is inactive.");
        MotionWarpingDebugState.SetFullBodyIkFollowup(false, "Full-body IK is inactive.");
        FullIkRetargetingDebugState.SetFullBodyIkFollowup(false, "Full-body IK is inactive.");
    }

    public void ClearFullIkRetargeting()
    {
        _fullIkRetargetingCorrections.Clear();
        FullIkRetargetingDebugState.Reset(false, Profile.AdvancedBodyScalingOverrides.UseProfileOverrides, 0f, 0f);
        FullIkRetargetingDebugState.FinalizeState(false, false, false, 0f, 0f, "Full IK retargeting is inactive.");
        FullIkRetargetingDebugState.SetFullBodyIkFollowup(false, "Full-body IK follow-up has not run.");
    }

    public void ClearMotionWarping()
    {
        _motionWarpingCorrections.Clear();
        MotionWarpingDebugState.Reset(false, Profile.AdvancedBodyScalingOverrides.UseProfileOverrides, 0f, 0f, _motionWarpingContext);
        MotionWarpingDebugState.FinalizeState(false, false, false, 0f, 0f, "Motion warping is inactive.");
        MotionWarpingDebugState.SetFullBodyIkFollowup(false, "Full-body IK follow-up has not run.");
    }

    public void ResetMotionWarpingContext(string summary = "Waiting for locomotion context.")
    {
        _motionWarpingContext.Reset(summary);
        _smoothedMotionDirectionWorld = Vector3.Zero;
        _smoothedPlanarSpeed = 0f;
        _hasMotionSample = false;
    }

    public void UpdateMotionWarpingContext(Vector3 worldPosition, float facingRadians, float deltaSeconds)
    {
        if (deltaSeconds <= 0f)
        {
            _motionWarpingContext.Reset("Waiting for locomotion context.");
            return;
        }

        if (!_hasMotionSample)
        {
            _lastMotionSampleWorldPosition = worldPosition;
            _hasMotionSample = true;
            _motionWarpingContext.Reset("Waiting for locomotion context.");
            _motionWarpingContext.HasObservation = true;
            _motionWarpingContext.FacingRadians = facingRadians;
            return;
        }

        var delta = worldPosition - _lastMotionSampleWorldPosition;
        _lastMotionSampleWorldPosition = worldPosition;
        var planarDelta = new Vector3(delta.X, 0f, delta.Z);
        var rawSpeed = planarDelta.Length() / MathF.Max(deltaSeconds, 0.0001f);
        var smoothing = Math.Clamp(deltaSeconds * 10f, 0f, 1f);
        _smoothedPlanarSpeed += (rawSpeed - _smoothedPlanarSpeed) * smoothing;

        if (planarDelta.LengthSquared() > 0.000001f)
        {
            var rawDirection = Vector3.Normalize(planarDelta);
            _smoothedMotionDirectionWorld = _smoothedMotionDirectionWorld.LengthSquared() <= 0.0001f
                ? rawDirection
                : Vector3.Normalize(Vector3.Lerp(_smoothedMotionDirectionWorld, rawDirection, smoothing));
        }

        var localDirection = _smoothedMotionDirectionWorld.LengthSquared() <= 0.0001f
            ? Vector3.Zero
            : Vector3.Transform(_smoothedMotionDirectionWorld, Quaternion.CreateFromAxisAngle(Vector3.UnitY, -facingRadians));
        localDirection = new Vector3(localDirection.X, 0f, localDirection.Z);
        if (localDirection.LengthSquared() > 0.0001f)
            localDirection = Vector3.Normalize(localDirection);

        var locomotionAmount = Remap(_smoothedPlanarSpeed, 0.10f, 1.65f);
        var turnAmount = localDirection.LengthSquared() <= 0.0001f
            ? 0f
            : Math.Clamp(MathF.Abs(localDirection.X) + (MathF.Max(0f, -localDirection.Z) * 0.35f), 0f, 1f) * locomotionAmount;

        _motionWarpingContext.HasObservation = true;
        _motionWarpingContext.HasLocomotion = locomotionAmount > 0.02f;
        _motionWarpingContext.PlanarSpeed = _smoothedPlanarSpeed;
        _motionWarpingContext.LocomotionAmount = locomotionAmount;
        _motionWarpingContext.TurnAmount = turnAmount;
        _motionWarpingContext.FacingRadians = facingRadians;
        _motionWarpingContext.WorldDirection = _smoothedMotionDirectionWorld;
        _motionWarpingContext.LocalDirection = localDirection;
        _motionWarpingContext.Summary = _motionWarpingContext.HasLocomotion
            ? $"Observed locomotion at {_smoothedPlanarSpeed:0.00} units/s with locomotion pressure {locomotionAmount:0.00}."
            : "Movement is below the locomotion activation threshold, so motion warping stays conservative.";
    }

    public void UpdateRuntimeTransforms(float deltaSeconds, float transitionSharpness)
    {
        for (var i = _activeBones.Count - 1; i >= 0; --i)
        {
            if (!_activeBones[i].UpdateRuntimeTransform(deltaSeconds, transitionSharpness))
                _activeBones.RemoveAt(i);
        }
    }

    private void RunOptionalLayer(string layerName, Action evaluate, Action clear)
    {
        try
        {
            evaluate();
            _optionalLayerFailureLogAtMs.Remove(layerName);
            GetOptionalLayerHealth(layerName).RecordSuccess();
        }
        catch (Exception ex)
        {
            clear();
            GetOptionalLayerHealth(layerName).RecordFailure(ex);

            const long logIntervalMs = 5000;
            var now = Environment.TickCount64;
            if (_optionalLayerFailureLogAtMs.TryGetValue(layerName, out var lastLogged)
                && now - lastLogged < logIntervalMs)
                return;

            _optionalLayerFailureLogAtMs[layerName] = now;
            Plugin.Logger.Warning($"Skipped {layerName} for armature {_localId} after {ex.GetType().Name}; base scaling remains active.");
        }
    }

    internal IReadOnlyList<OptionalLayerHealthSnapshot> GetOptionalLayerHealthSnapshot()
        => _optionalLayerHealth
            .OrderBy(static pair => pair.Key, StringComparer.Ordinal)
            .Select(static pair => pair.Value.Freeze(pair.Key))
            .ToArray();

    private OptionalLayerHealthState GetOptionalLayerHealth(string layerName)
    {
        if (!_optionalLayerHealth.TryGetValue(layerName, out var health))
            _optionalLayerHealth[layerName] = health = new OptionalLayerHealthState();
        return health;
    }

    private static bool AreTwinnedNames(string name1, string name2)
    {
        if (string.IsNullOrEmpty(name1) || string.IsNullOrEmpty(name2))
            return false;

        return name1[^1] == 'r' ^ name2[^1] == 'r'
            && name1[^1] == 'l' ^ name2[^1] == 'l'
            && name1[0..^1] == name2[0..^1];
    }

    private static float Remap(float value, float start, float full)
    {
        if (full <= start)
            return value >= full ? 1f : 0f;

        return Math.Clamp((value - start) / (full - start), 0f, 1f);
    }
}
