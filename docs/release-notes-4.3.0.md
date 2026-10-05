# Customize+ 4.3.0

## Template Editor Checkpoints & A/B Comparison

- Create session-local checkpoints while Bone Editing.
- Restore earlier working states through the normal Undo/Redo-aware editor path.
- Compare the current working copy against checkpoints without modifying the saved template.

## Live Changed-Bone Filtering

- Filter the bone table to rows changed since the selected checkpoint.
- The checkpoint filter works together with existing bone search and filtering.

## Pose Stress Correctness

- Pose Stress now analyzes the active Template Editor working copy while editing.
- Results are invalidated when the editor state changes so stale analysis is not presented as current.

## Template Compare Correctness

- Template Compare reports now detect stale editor or comparison-target inputs.
- Stale reports are hidden until explicitly rebuilt, and copying is disabled while stale.

## Advanced Body Scaling Profile Overrides

- Added clearer inherited versus overridden presentation.
- Added Show Overrides Only.
- Added override counts and clearer return-to-inherited behavior.

## Actor / Effective-State Inspector

- Added a read-only Profiles-tab inspector for the authoritative actor, profile, template, published ABS, runtime-layer, binding, capability, and safety information currently available.

## Scope

- No new solver or scaling mathematics was added.
- No runtime or native deformation behavior was changed by this authoring and inspection release.
