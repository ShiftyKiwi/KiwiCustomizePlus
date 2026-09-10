# Hierarchical Shaping

Hierarchical Shaping is an optional Advanced Body Scaling layer for small,
scale-only continuity adjustments across three curated vanilla body chains:

- chest, shoulder, and upper arm
- ribs, abdomen, and waist
- pelvis, thigh, knee, and calf

It is an authoring aid, not an ideal-proportions system. It does not create a
new target body type, modify mesh data, or replace ordinary bone editing.

## Using It

1. Enable Advanced Body Scaling for the intended global setting or profile.
2. Open `Hierarchical Shaping`, located after `Bone Importance Weighting` and
   before `Global Neck/Shoulder Baseline`.
3. Enable `Hierarchical shaping`.
4. Leave `Allow bounded authored relaxation (1% max)` disabled unless you
   intentionally want the optional final, session-only authored-scale support.

The diagnostic text below the settings reports each chain's applied or safe
refusal status. Bone explainability also records a `Hierarchical shaping`
stage when a derived contribution exists.

## Safety Model

The layer runs only while Advanced Body Scaling is enabled in a non-manual
mode and only during a validated template-binding rebuild. Its output is
derived from freshly resolved profile transforms and is never persisted to a
template or profile.

It runs after the existing Advanced Body Scaling pipeline and deformation
conditioners, then before the rebuilt transforms are bound to live bones.
Existing pose-aware, RBF, retargeting, motion-warping, and full-body IK layers
keep their established downstream ordering. Hierarchical Shaping does not
replace or re-run any of those systems.

The following are hard exclusions:

- locks and pinned scale axes
- unknown, manual-only, modded, extension, clothing, prop, wing, and tongue
  controls
- invalid, unavailable, or untrusted live controls
- deliberately asymmetric left/right chain input

With authored relaxation disabled, explicit rows are hard constraints. When
enabled, an explicit unlocked and unpinned curated vanilla body row can receive
at most a 1% session-only scale multiplier. The cap is fixed; there is no
strength slider.

## Rebuild Cache

The derived result is cached per armature only after a validated rebuild. The
key includes the actor lifetime, profile identity and resolved transforms,
binding/topology capability fingerprint, conditioned input transforms, feature
state, and solver version. A cache replay rechecks every receiver gate before
it stages and applies any scale change. Any changed binding, profile input,
lock, pin, capability, appearance lifetime, or feature option invalidates the
entry and rebuilds or safely refuses it.

## Profiles And Compatibility

Both flags use the existing Advanced Body Scaling profile-override semantics:
an unset profile field inherits the global value. The fields are stored with
the normal local profile/configuration data; no IPC, sync, or external profile
API contract was added or changed.

The current IPC character-profile payload carries template transforms, not
Advanced Body Scaling settings. This checkout also has no Lightless-facing
profile schema. Consequently, local settings are sufficient for deterministic
local reconstruction, but remote parity for Hierarchical Shaping intent would
need a separately versioned external profile/schema change in a follow-up.

## Performance And Limits

Only three short curated chains are evaluated, and only at template-binding
rebuild time. It does not run a per-frame solver, make native research writes,
or change BIW policy. Its visual result remains dependent on the active model,
game skinning, animations, and other enabled Advanced Body Scaling layers.
