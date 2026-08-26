# KiwiCustomizePlus Product Innovation Notes

These notes track future ideas after the current hardening and authoring-tooling releases. They are design TODOs, not implemented systems unless a section explicitly says otherwise.

## Local Skeleton Metadata Packs

- Load optional local JSON or compressed JSON metadata packs from a known plugin folder.
- Metadata should describe bone code name, display name, family, aliases, support class, risk notes, and safety flags.
- Use per-bone aliases here to refine the current Phase 1 family-level alias search, so terms like "wrist" can narrow to specific bones instead of the broader arms family.
- Local metadata packs are implemented as advisory labels, aliases, and notes for unknown/custom bones only.
- Metadata does not grant runtime trust for mirroring, parentage, propagation, guardrails, BIW, automation, or native transform writes.
- Future metadata enhancements must preserve that advisory/manual boundary; built-in safe data remains authoritative for known bones.

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
