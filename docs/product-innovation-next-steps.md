# KiwiCustomizePlus Product Innovation Notes

These notes intentionally follow the 2.1.10.2 hardening release. They are design TODOs, not implemented systems.

## Local Skeleton Metadata Packs

- Load optional local JSON or compressed JSON metadata packs from a known plugin folder.
- Metadata should describe bone code name, display name, family, aliases, support class, risk notes, and safety flags.
- Use per-bone aliases here to refine the current Phase 1 family-level alias search, so terms like "wrist" can narrow to specific bones instead of the broader arms family.
- Optional mirror partners and parent overrides should be accepted only from trusted/explicit metadata.
- Unknown/custom bones must remain manual and experimental unless a metadata pack explicitly classifies them later.
- Conflicts should be deterministic: built-in safe data wins by default, user-local overrides can opt in to taking precedence.

## Unknown Bone Workbench

- Show unknown bones detected on the current live skeleton.
- Allow manual labels, aliases, support class notes, and local export.
- Do not infer mirror behavior, parentage, propagation safety, or automation safety from naming alone.
- Keep any nudge/testing tools clearly labeled as experimental and actor/context dependent.

## Semantic Body Goal Editor

- Add deterministic local goal controls such as broader shoulders, smaller waist, wider hips, thicker thighs, fuller chest, and stronger calves.
- Goals should output normal template `BoneTransform` edits that advanced users can inspect afterward.
- Goals must respect row locks, per-axis pins, unknown-bone safety, and existing advanced-scaling guardrails.
- Start with hand-authored mappings and curves before considering any optimizer-like behavior.

## Proportion Dashboard / Delta Inspector

- Show advisory ratios such as shoulder-to-waist, hip-to-waist, thigh-to-calf, upper-arm-to-forearm, and left/right asymmetry.
- Treat values as styling instrumentation, not true anatomical measurement.
- Investigate safe `BindPose` and `World` transform inspection separately before exposing them.
- If a reference frame cannot be implemented safely, leave it disabled with clear explanation rather than guessing.
