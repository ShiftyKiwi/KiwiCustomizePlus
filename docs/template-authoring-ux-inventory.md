# Templates Authoring UX Inventory

## Pre-alignment findings

The Template Editor owns a temporary working copy while bone editing is active.
Direct bone edits, region tools, mirror, template diff copy, and Semantic Body
Goals already mutate that copy through `TemplateEditorManager` and its
session-local undo history.

Before this authoring-consistency pass, Body Analyzer and Advanced Scaling
Preview were exceptions:

- **BUG:** both read the persisted selected template while editing was active,
  so their results could omit unsaved edits in the working copy.
- **UX INCONSISTENCY:** both disabled Apply while editing was active, then
  mutated and saved the persisted template directly while editing was off.
- **BUG:** their tool-specific Revert paths restored snapshots of affected
  rows without checking whether those rows changed after Apply. That could
  overwrite a later edit to an affected row.
- **UX INCONSISTENCY:** Body Analyzer discarded its result after Apply, which
  made its tool-specific Revert inaccessible until a new analysis was run.
  The panel expansion itself was not deliberately reset by the source path.
- **INTENDED:** Semantic recipe loading changes slider/UI state only. Semantic
  Apply already requires active editing and creates one shared transaction.
- **MISSING EXPECTED CONTROL:** Semantic Goals had no convenience Revert for
  its last exact application, even though shared Undo was available.

## Target authoring model

1. Starting bone editing creates a temporary copy of the selected saved
   template and activates the editor profile.
2. All editor-aware tools read that temporary copy while editing is active.
3. A mutating authoring operation runs as one named editor transaction and
   affects the temporary copy only. Normal resolution provides live feedback
   for an assigned preview actor; no tool creates a native-write path.
4. Save copies the final temporary state to the selected saved template.
   Do Not Save disposes the whole temporary session.
5. Tool-specific Revert uses a narrow, conflict-checked delta. It is disabled
   when an affected row has changed since Apply; shared Undo/Redo remains the
   authoritative general history.

## Final tool semantics

| Tool | Editing off | Editing on | Source | Apply / Revert / history |
| --- | --- | --- | --- | --- |
| Direct Bone Editor | unavailable | working copy | working copy | normal transactions, Undo/Redo, Save/Discard |
| Body Analyzer | saved-template analysis only | working-copy analysis | selected saved template or temporary editor copy | Apply requires editing and is one transaction; Revert Fix is conflict-checked; Undo/Redo is authoritative |
| Advanced Scaling Preview | saved-template dry preview only | working-copy preview | selected saved template or temporary editor copy | Apply requires editing and is one transaction; Revert Applied Preview is conflict-checked |
| Semantic Body Goals | controls may be viewed, no preview/apply | preview and apply available | temporary editor copy and live-bone presence | recipe load is UI-only; Apply and Revert Goals are normal transactions |
| Region / Batch / Mirror / Diff Copy | unavailable | available | temporary editor copy | normal transactions and Undo/Redo |
| Explainability / Health / Dashboard / Solver Preview | read-only | read-only | managed template and published armature state | no template mutation |

Repeated Analyzer application requires a new analysis, repeated Advanced
Preview application requires a new preview, and repeated Semantic Goals
application requires a new preview. Semantic goals are deliberately relative
adjustments; a newly rebuilt preview with unchanged sliders is an explicit
additional adjustment, not an accidental second click on the same preview.

Body Analyzer, Advanced Scaling Preview, and Semantic Body Goals retain their
ImGui expansion choice across analysis, preview, apply, undo, and revert. A
result may become stale after an editor revision changes, but stale state does
not collapse a panel, save data, discard the editor session, or clear shared
Undo/Redo history.

## Scope

This document records the inventory and narrow alignment work only. It does
not change runtime deformation, BIW, topology handling, IPC, synchronization,
or skeleton trust rules.
