# Local Bone Metadata Packs

Local bone metadata packs are optional JSON files that help Customize+ explain unknown or custom bones in the Template Editor.

They are loaded from:

```text
<Customize+ config>/bone_metadata/*.json
```

## What Metadata Can Do

- Improve display labels for unknown/custom bones.
- Add search aliases for unknown/custom bones.
- Show support class notes such as `ManualOnly`, `KnownModded`, or `Risky`.
- Provide local risk notes for troubleshooting.
- Store mirror or parent notes for future design discussion.

## What Metadata Cannot Do

Metadata is advisory only. It does not grant runtime trust and does not affect:

- Transform writes.
- Parenting.
- Mirroring.
- Propagation.
- Bone Importance Weighting.
- Guardrails.
- Advanced scaling automation.
- Mare/Synchronos-facing behavior.

Built-in Customize+ bone data remains authoritative for supported known bones.

## Schema Version 1

```json
{
  "schemaVersion": 1,
  "packName": "Example Local Pack",
  "packAuthor": "Local user",
  "source": "Local notes",
  "description": "Advisory labels for unknown bones.",
  "entries": [
    {
      "boneName": "example_unknown_bone",
      "displayName": "Example Unknown Bone",
      "family": "Unknown",
      "aliases": ["example", "custom"],
      "supportClass": "ManualOnly",
      "riskNotes": "Manual/experimental only.",
      "manualOnly": true,
      "allowSearchAlias": true,
      "mirrorPartner": "",
      "parentOverride": ""
    }
  ]
}
```

Unsupported schema versions and malformed packs fail safely with status notes in the Template Editor.
