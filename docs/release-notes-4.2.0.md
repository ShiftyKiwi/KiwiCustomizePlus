# Customize+ 4.2.0

## Resolved Shape Template Export

- Added **Copy Resolved Shape as Template** to Profile headers.
- The action copies the current profile's stable static Advanced Body Scaling result as a normal Customize+ template.
- Import the copied template to preserve that body shape as ordinary bone transforms and use it with Advanced Body Scaling disabled.

## Behavior and safety

- The export honors the active profile's effective settings, including profile overrides, template composition, Hierarchical Shaping, Bounded Authored Relaxation, locks, and pinned scale axes.
- Dynamic pose, IK, and motion corrections are excluded.
- Source profiles and templates are not modified.
- The export uses the normal Customize+ template clipboard and import format.
