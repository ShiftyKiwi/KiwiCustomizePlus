# Customize+ 4.1.0

## Highlights

- Added **Hierarchical Shaping**, an opt-in Advanced Body Scaling layer that
  applies small, bounded scale-only continuity support across compatible body
  regions while preserving authored proportions.
- Added optional **Bounded Authored Relaxation**, allowing a maximum 1%
  session-only derived adjustment around eligible explicit unlocked values.
- Added validated support for chest/shoulder/upper-arm,
  ribs/abdomen/waist, and pelvis/thigh/knee/calf chains.

## Profiles and performance

- Hierarchical Shaping and Bounded Authored Relaxation can inherit global
  settings or be overridden per profile.
- Derived corrections are revision-gated and cached at validated template
  binding rebuilds rather than solved continuously every frame.

## Safety and UI

- Locks, pinned axes, unknown or unsupported bones, clothing, props, and
  untrusted controls remain excluded.
- Derived corrections are never persisted to templates or profiles.
- Global Neck/Shoulder Baseline no longer uniquely opens by default.

External synchronization of Advanced Body Scaling shaping settings remains
outside the current compatibility scope.
