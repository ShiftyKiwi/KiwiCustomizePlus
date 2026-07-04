# Template Health / Delta Details

Template Health is a read-only diagnostic view in the Template Editor.

It answers:

```text
What changed in this template, and what might be missing, risky, asymmetric, unsupported, locked, pinned, or propagated?
```

## What It Shows

- Edited bone count.
- Edited bones missing from the current live preview skeleton.
- Unknown/custom edited bones.
- Locked rows and pinned axes.
- Propagated rows and propagation falloff.
- Left/right asymmetry using built-in trusted mirror pairs only.
- Live bone count.
- Local metadata pack status.
- Per-bone position, rotation, scale, and child-scale deltas.

## Safety Boundaries

Template Health is advisory and read-only.

It does not:

- Apply fixes.
- Auto-correct values.
- Mutate template data.
- Trust metadata mirror partners.
- Change runtime transform behavior.
- Change skeleton hardening behavior.
- Change Bone Importance Weighting or advanced scaling behavior.

Unknown/custom bones remain manual and experimental unless Customize+ itself adds explicit support later.
