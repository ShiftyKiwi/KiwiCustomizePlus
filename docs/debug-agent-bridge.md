# DEBUG AgentBridge

Customize+ includes a development-only local bridge using `Franthropy.AgentBridge` version `0.1.0`. It is compiled and started only in a DEBUG build; Release builds do not reference or advertise it.

The host implementation is `CustomizePlus/Core/Services/CustomizePlusAgentBridgeService.cs`. It uses the official authenticated named-pipe host and stores only the library-protected local access token in plugin configuration.

## DAB workflow

1. Build `CustomizePlus` in Debug.
2. Use DAB's guarded `bridge_deploy` for the `CustomizePlus` development plugin.
3. Query `bridge_list`, then `bridge_health`, `bridge_manifest`, and `bridge_snapshot`.

The bridge advertises only read-only diagnostics. It has no actions that rebuild armatures, change profiles, modify templates, or force refreshes.

## Snapshot schema

`customizeplus.debug.snapshot.v1` is bounded and cached. It reports published armature state, structural/native/profile/deformation revisions, capability manifest, profile assignment applicability, BIW identity, native write counters, deformation-quality solver regions/contribution counters, NFLB/Skelomae role and automatic-contribution counts, coarse timings, and local evidence summary. It consumes the existing hardened armature state and does not reparse native skeletons for a query.

The bridge exposes no capture/mutation action. Named evidence capture remains a local DEBUG UI operation, and the snapshot only reports its count/latest comparison summary. DAB queries return the last cached immutable snapshot; they do not invoke profile resolution, model parsing, or native transform writes.

The bridge is a development observer/deployer, not a production runtime dependency.
