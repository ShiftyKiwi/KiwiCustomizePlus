# Semantic Body Goals And Shape Recipes

Semantic Body Goals are creative authoring helpers for the Template Editor.

They are starting points, not corrections. They do not inspect meshes, do not decide what a body "should" look like, and do not auto-fix templates.

## Shape Recipes

Shape Recipes are built-in slider presets.

Selecting a recipe does not change template data. Loading a recipe only fills the Semantic Body Goal sliders. Users still need to preview and explicitly apply the result.

## Preview

Preview is read-only.

It shows the known supported bones that would be affected, along with before scale, after scale, delta, and skipped/blocked reasons. Template data is not changed during preview.

Preview includes a stale-state guard. If sliders, selected recipe, targeted template rows, row locks, scale pins, or live bone presence change after preview, users must rebuild the preview before applying.

## Apply

Apply writes ordinary scale-only `BoneTransform` edits through the existing Template Editor modification path.

The MVP does not write position or rotation edits.

## Safety Boundaries

- Unknown/custom bones are skipped.
- Metadata-trusted bones are not used.
- Non-default, modded, and IVCS/modded-compatible bones are skipped by MVP rules.
- Row locks are respected.
- Pinned scale axes are preserved.
- No auto-fix exists.
- No auto-balancing exists.
- No runtime semantic solver exists.
- Runtime transform behavior, BIW, skeleton hardening, IPC/API, sync behavior, and plugin identity are unchanged.
