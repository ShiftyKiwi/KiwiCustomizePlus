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
- Export compact Unknown Bone Workbench evidence for later registry review.

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
Local entries targeting a known curated bone are ignored. Candidate mirror and
parent fields are documentation only; they are never used as runtime topology.

## Schema Version 1

```json
{
  "schemaVersion": 1,
  "packId": "example-local-pack",
  "packVersion": "0.1",
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
      "parentOverride": "",
      "candidateOrigin": "UnknownCustom",
      "candidateFunctionalRole": "Unknown",
      "candidateBodyRegion": "Unknown",
      "candidateAutomationTrust": "ManualOnly",
      "trustLevel": "ManualExtension"
    }
  ]
}
```

Unsupported schema versions and malformed packs fail safely with status notes in the Template Editor.
