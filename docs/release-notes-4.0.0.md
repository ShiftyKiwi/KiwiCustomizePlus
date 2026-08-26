# Customize+ 4.0.0

## Highlights

- Major runtime and lifecycle hardening for safer armature recovery across redraws, appearance changes, equipment changes, and native object replacement.
- A compositional skeleton capability system that combines validated live topology with curated metadata, keeps unsupported controls conservative, and improves compatibility reporting for supported vanilla, IVCS1, IVCS2, YAS, NFLB, and Skelomae cases.
- Expanded Advanced Body Scaling with bounded automatic support, bilateral consistency, transition-aware falloff, Bone Importance Weighting integration, proportional balance, surface smoothness, cross-section conditioning, local volume intent, shape fairness, and pose-aware joint correctives.
- A more coherent Template Editor workflow with better explainability, compatibility and health diagnostics, region/batch tools, working-copy analysis, safe Apply/Revert operations, and shared transaction-based Undo/Redo.

## Added

- Canonical skeleton metadata, functional bone roles, automation trust, live topology manifests, and compatibility-aware resolution.
- Bone Explainability, Actor Health, template/profile comparison, compatibility preview, and conservative Unknown Bone Workbench support.
- Local Bone Metadata Packs for advisory labels, aliases, notes, and support hints for unknown or custom bones.
- More complete body-shaping diagnostics, solver previews, deformation-quality metrics, and authoring tools.
- Improved Semantic Body Goals authoring with safe preview, stale-preview handling, Apply/Revert, and working-copy consistency.

## Improved

- Appearance lifecycle recovery for Glamourer-driven race, outfit, redraw, and Revert-to-Game transitions.
- Profile/template resolution and binding scheduling so stable actors avoid unnecessary rebuilds and high-frequency log churn.
- Bone Importance Weighting scheduling and crowd behavior while preserving capability- and model-aware shaping where it is eligible.
- Pose-space corrective, IK retargeting, motion-warping, Full-Body IK, and Advanced Body Scaling diagnostics where these systems are enabled.
- Template Editor analysis and preview panels now operate consistently against the editable working template and use conflict-safe authoring transactions.

## Safety and compatibility

- Native transform writes now require a validated current binding, finite transforms, valid propagation math, and capability/trust eligibility.
- Armature publication is transactional, preserves last-known-good state through transient redraws, and confirms changed topology before publishing it.
- Explicit transforms, locks, pinned axes, and manual-only controls remain authoritative. Automatic shaping only affects eligible trusted support controls.
- Metadata packs remain advisory only. They never grant mirroring, parenting, propagation, guardrail, Bone Importance Weighting, automation, or transform-write authority.
- The in-game plugin remains **Customize+**. Its assembly, DLL, configuration, namespaces, and IPC/API identity remain **CustomizePlus**.
- DEBUG AgentBridge diagnostics are development-only and are not a Release dependency.

## Validation

- Validated with the full automated test suite, Debug and Release builds, package inspection, generated metadata coverage, and live multi-actor binding/runtime checks.
- Release validation confirms stale and unsafe transform writes remain blocked by the hardened runtime boundaries.
