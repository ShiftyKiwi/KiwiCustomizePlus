# Advanced Body Scaling Robustness Roadmap

This roadmap focuses on robustness improvements that stay on the normal supported scale/deformation path.
It does not assume support for IVCS2-specific added physics bones or any other unsupported custom skeleton-only physics extensions.
When unsupported extras are present, new tooling should ignore them and degrade safely.

## Principles
- Prefer QA tooling, explainability, and conservative safety presets before heavier runtime correction systems.
- Keep preview/apply/revert flows intact.
- Use supported deforming bones and existing scale-based relationships first.
- Avoid unsupported physics-bone manipulation entirely.

## Phase 1 - Pose Stress-Test Harness
Status: Implemented in this pass.

Delivered:
- Lightweight pose stress-test panel in the template editor.
- Built-in heuristic pose checks for:
  - arms raised
  - wide arm spread
  - torso twist
  - forward bend / squat
  - stride / extended leg
  - head tilt / neck stress
- Region-level risk scoring for:
  - neck / shoulder
  - clavicle / upper chest
  - elbow / forearm
  - waist / hips
  - thigh / knee / calf
- Overall animation-risk summary.
- Safe integration with current template values, current automation output, and unapplied preview results.

Notes:
- This is heuristic-based on supported scale relationships, not a physics simulation.
- Unsupported skeleton extras are ignored instead of being treated as required inputs.

## Phase 2 - Guardrail Explainability UI
Status: Implemented in this pass.

Delivered:
- Compact guide that explains the difference between:
  - row/group lock
  - per-axis pins
  - surface balancing
  - mass redistribution
  - guardrails
  - pose-aware corrections
  - neck/shoulder compensation
  - animation-safe mode
  - region tuning
- Status indicators showing whether the relevant system is currently off, light, active, customized, or editor-only.
- Additional last-preview activity indicators in Advanced Scaling Preview debug details for:
  - guardrail triggers
  - pose-aware correction triggers

## Phase 3 - Animation-Safe Mode
Status: Implemented in this pass.

Delivered:
- One-click Animation-safe mode toggle.
- Conservative runtime biasing that:
  - reins in propagation strength
  - increases smoothing / curve balancing near joints
  - keeps extremity influence calmer
  - upgrades guardrail / pose validation floors when they were fully off
  - makes neck/shoulder behavior more conservative and blended
- Works on top of:
  - global settings
  - profile overrides
  - per-region tuning
  - race neck presets

Notes:
- This is a coordinated preset layered onto the current system, not a separate scaling engine.
- Manual control remains available.

## Phase 4 - Data-Driven Per-Bone Importance Weighting
Status: TODO scaffolding only.

Planned direction:
- If supported mesh/skin-weight data is available, compute per-bone importance for supported deform bones only.
- Use it to improve smoothing, guardrail strength, propagation defaults, and extremity down-weighting.
- Cache results and fall back to existing heuristics when data is unavailable.

Guardrails:
- Do not depend on IVCS2-only or unsupported physics-bone targets.
- Degrade safely to current heuristics when mesh-weight access is unavailable.

## Phase 5 - Optional Collision-Risk Warnings
Status: TODO scaffolding only.

Planned direction:
- Add lightweight warnings for likely clipping/problem zones:
  - arm vs torso
  - thigh vs thigh
  - neck vs shoulder massing
- Start with warnings/indicators only.
- Use simple supported region/capsule heuristics.

Guardrails:
- No aggressive automatic repositioning.
- No dependency on unsupported custom physics bones.

## Follow-up Ideas
- Add exportable stress-test reports for preset QA.
- Add per-region "show affected bones" helper in explainability UI.
- If supported data access improves later, connect Phase 4 bone-importance caching into the same stress-test panel as an optional detail layer.
