# Offline Authoring Tools

Customize+ provides on-demand Template Editor tools for investigating and authoring
body-scale templates. They operate on the normal temporary editor template and
existing resolver; they do not create an additional runtime deformation path.

## Explainability and Health

- **Bone Explainability** shows the published transform context, active Advanced Body Shaping stage
  deltas, BIW influence, explicit authority, and safe reason codes for an
  automatic skip. The `Why?` row action opens the same inspector.
- **Actor Health** classifies the preview actor as Healthy, Temporarily Waiting,
  Limited Compatibility, or Needs Attention. A normal Glamourer/rebind wait is
  informational rather than an error.

## Compare and Compatibility

- Template and profile diffs compare semantic transform/assignment state, not
  raw JSON formatting. Copying a diff applies only to the editable temporary
  template through the normal undoable mutation path.
- Compatibility Preview calls the production resolver with the currently
  published immutable capability manifest. Dormant data remains stored and can
  reactivate when the required capability is available.

## Authoring Operations

- Undo/redo is session-local, bounded to 50 named transactions, and stores only
  managed template snapshots. Slider drags become one transaction.
- Region tools use curated body regions and registry mirror metadata. Unknown,
  clothing, prop, appendage, and gear controls are excluded by default.
- Solver A/B Preview runs the current resolver and Advanced Body Shaping conditioning on copied
  managed data. It never saves settings, replaces a profile/template, installs
  a live override, or writes native transforms.

## Unknown Bones and Local Metadata

The Unknown Bone Workbench is an evidence and manual-authoring environment.
Unknown bones remain `UnknownCustom` and `ManualOnly` by default. Evidence JSON
contains observed topology facts separately from candidate notes.

Local metadata packs support informational labels, aliases, notes, and candidate
registry fields. The trust labels are Informational, Manual Extension, and
Locally Trusted for authoring notes. No pack can bypass live topology, binding
generation, finite transform validation, native safety, or grant runtime
automation, mirroring, parenting, propagation, guardrail, or BIW authority.

The DEBUG AgentBridge exposes compact read-only authoring status. It is not part
of the Release runtime architecture.
