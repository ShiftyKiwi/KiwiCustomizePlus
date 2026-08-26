# Runtime Capability and Support Contract

## Runtime flow

`validated native armature -> immutable capability manifest -> profile compatibility selection -> weighted transform resolution -> existing Advanced Body Scaling -> bounded deformation-quality support solver -> current ModelBone binding -> guarded native writes`

The structural fingerprint identifies published topology and capability evidence. Native binding generation identifies the lifetime of the live native binding; it is not a topology identity.

## Compatibility assignments

Profile template assignments default to **Always**, preserving all legacy behavior. An assignment may instead require IVCS1, IVCS2, YAS, NFLB, or Skelomae. A required capability is satisfied only when the live manifest reports it as `Present`.

Unsatisfied assignments are dormant, not disabled or deleted. Their transforms, weight, order, and identifier remain saved and reactivate automatically when the capability returns. A missing live bone likewise never deletes a saved transform.

## Skeleton support

Live topology is authoritative for parentage and binding. Curated metadata only supplies conservative semantic roles and automation trust.

- Vanilla, IVCS1, IVCS2, YAS, NFLB, and Skelomae are compositional capabilities.
- Explicit user-authored transforms may apply to a present, safely bound bone even when it is manual-only.
- Automation trust controls automatic systems only.
- NFLB clothing and props remain manual-only by default; automated clothing and prop contribution is zero.
- Skelomae tongue and wings remain outside automatic body deformation.

## Deformation quality solver

The rebuild-time deformation-quality stage is part of Advanced Body Scaling, not a separate runtime deformation backend. It derives region targets from the resolved transform set and may create small, bounded automatic transforms only for live, curated support and transition controls that have no explicit template row.

- Regions cover chest, shoulders, upper arms, forearms, abdomen, waist, pelvis/glutes, thighs, calves, and neck/traps.
- Primary controls establish the region target; support and transition controls receive a reduced deterministic falloff.
- Automatic left/right controls use the curated mirror relationship and are normalized together. Explicit left/right template edits remain independent.
- Automatic support uses cross-axis-biased, longitudinally tempered scale compensation. This is a practical visual heuristic, not exact mesh-volume preservation.
- Shoulder, elbow, wrist, hip, knee, ankle, and neck transitions receive bounded support rather than being forced to identity scale.
- Existing pose correctives, IK retargeting, motion warping, and full-body IK remain after the resolved binding layer; this stage does not create a second pose solver.

### Secondary controls and model influence

IVCS2, YAS, NFLB, and Skelomae body extensions can participate only when all of the following are true: the capability is present or partial, the live bone exists, the curated role is a body extension/control, and `AdvancedCorrectiveSafe` trust is granted. Model-derived Bone Importance Weighting attenuates an automatic secondary contribution when a score is available; it never removes an explicit saved transform.

Shared boundaries blend automatic output rather than repeatedly applying full primary and secondary strength. NFLB clothing/props, Skelomae tongue, and Skelomae wings are explicit/manual boundaries: automatic body contribution is zero.

## Revisions and performance

Published state uses separate monotonic revisions for armature topology, native binding generation, manifest revision, profile resolution, deformation output, and diagnostics. The profile/deformation/diagnostics revisions advance only when their rebuild-time input/output signature changes, not for UI/DAB reads or animation frames.

Coarse rolling timings cover armature update checks, manifest builds, profile resolution, deformation solving, ModelBone binding, and native transform application. They are bounded aggregate measurements rather than per-bone telemetry.

## Development evidence

DEBUG builds offer local named evidence captures under `<Customize+ config>/development-evidence`. The versioned JSON records build identity, structural/capability state, profile applicability, deformation diagnostics, extension counts, BIW identity, guarded-write counters, and timing summaries. Captures are diagnostic-only and are never loaded as runtime authority.

The DEBUG UI can list, compare, and delete captures. Comparisons use structural/capability and resolved-state data, never native pointer addresses.

## Diagnostics and support

The DEBUG capability view, support log, and local AgentBridge snapshot expose manifest state, template applicability, solver regions/contributions, BIW state, native-write counters, revisions, timings, evidence summary, and bilateral/continuity quality measurements. These diagnostics are advisory and never rewrite a template.

## Troubleshooting

If an assignment is not visible, check that its profile/template is enabled, its weight is non-zero, and any compatibility requirement is currently present. A dormant requirement is expected to reactivate without toggling after redraw/model changes. Stale or unsafe native writes are skipped by design and should be included in a support snapshot.
