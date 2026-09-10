# Hierarchical Shaping Performance Validation

This development record applies to `5612334 Add hierarchical body shaping`.
It records Debug-only DAB observations; the measurement surface and timing
instrumentation are excluded from Release builds.

## Nine-Actor Gate

- 9 active armatures and 9 active template assignments.
- Every validated binding was current.
- A 20-second idle observation produced zero unnecessary hierarchy solves,
  cache churn, rebuild churn, and binding churn.
- One deliberate mass OFF-to-ON rebuild produced 15 expected and 15 actual
  recomputes: 9 profile rebinds and 6 ordinary BIW model-weight finalization
  rebuilds.
- Hierarchical solve timing was 0.0689 ms median and 0.0895 ms p95/max, for
  1.0571 ms total across the 15 recomputes.
- Full deformation-stage timing was 0.6363 ms median and 0.9686 ms maximum.
- Ten OFF/ON cycles produced 150 expected and 150 actual recomputes, with no
  duplicate solve and a cache bounded at 9 entries.

Across the gate, the maximum derived correction was 0.999999%, with zero stale
and unsafe writes. Objective FPS telemetry was not available through DAB, so
these figures establish solver/rebuild behavior only, not a visual or frame-rate
guarantee.
