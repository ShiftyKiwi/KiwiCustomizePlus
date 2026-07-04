# Customize+ 3.0.0 Release Notes

## Highlights

- Added Local Bone Metadata Packs for advisory labels, search aliases, notes, and support hints for unknown/custom bones.
- Added the Unknown Bone Workbench in the Template Editor.
- Added read-only Template Health / Delta Details.
- Added a read-only Proportion Dashboard.
- Added Semantic Body Goals as conservative creative authoring helpers.
- Added built-in Shape Recipes as semantic slider presets.
- Added Preview with Profile Context so other assigned templates can remain visible while editing one template.

## Safety And Compatibility

- Metadata is advisory only and does not grant runtime trust.
- Template Health and Proportion Dashboard are read-only.
- Semantic Body Goals output ordinary scale-only `BoneTransform` edits through the normal editor path.
- Shape Recipes only load slider values until the user previews and applies them.
- Preview with Profile Context uses other profile templates as visual-only context.
- Only the currently edited template is mutated or saved.
- No Auto-Fix or auto-balancing exists.
- No runtime semantic solver exists.
- Runtime transform behavior, Bone Importance Weighting, skeleton hardening, IPC/API, sync behavior, and plugin identity are unchanged.
