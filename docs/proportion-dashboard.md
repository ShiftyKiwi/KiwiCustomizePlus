# Proportion Dashboard

The Proportion Dashboard is a compact read-only styling/debug aid inside Template Health.

It uses bone transform ratios, not mesh measurements.

## What It Can Help Review

- Shoulder-to-waist transform balance.
- Hip-to-waist transform balance.
- Thigh-to-calf transform taper.
- Upper-arm-to-forearm transform taper.
- Chest, arm, and leg left/right scale deltas.
- Extreme scale outliers.
- Motion-risky position or rotation edits.

## Status Labels

- `Balanced`
- `Mild`
- `Strong`
- `Extreme`
- `Review`

These labels are advisory only. They are not proof that a body shape is correct or incorrect.

## Safety Boundaries

The dashboard does not inspect meshes, skin weights, or actual visible surface area.

It does not:

- Apply fixes.
- Auto-balance a template.
- Mutate template values.
- Treat unknown/custom bones as trusted ratio inputs.
- Replace the Body Analyzer or Advanced Scaling Preview.

In Customize+ 2.2, no automatic correction exists for this dashboard.
