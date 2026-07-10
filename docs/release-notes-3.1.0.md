# Customize+ 3.1.0

## Added

- Profile-local race-specific neck preset overrides for Advanced Body Scaling.
- Six conservative Semantic Body Goal starting recipes: Subtle Athletic, Gentle Taper, Balanced Silhouette, Soft Frame, Compact Power, and Broad but Balanced.
- A current-session Activity Log under Settings with categories, filtering, copy-selected, copy-full-log, and clear actions.

## Improved

- Profile race preset editing now makes clear that runtime follows each actor's detected race.
- Activity Log entries now cover profile-management actions, profile Advanced Scaling overrides, global Advanced Scaling changes, metadata pack actions, grouped import/export, template edits, and Semantic Goal applications.
- Global and profile Advanced Scaling changes report clearer old-to-new values where practical.
- Semantic Goal Activity Log entries include the loaded recipe name when applicable.
- The Template Editor bone table now uses the required Hideable table flag for column enable/disable behavior.

## Safety and compatibility

- The Activity Log is local, current-session only, capped at 50 entries, and is not saved, synced, exported, telemetered, IPC-exposed, or used for rollback.
- Recipes only populate existing sliders. Semantic preview remains read-only, and Apply remains the only semantic mutation path.
- No runtime transform, Bone Importance Weighting, skeleton hardening, IPC/API, sync, metadata-trust, or Profile Context Preview behavior changed.
