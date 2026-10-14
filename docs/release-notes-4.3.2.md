# Customize+ 4.3.2

## Bone Importance Weighting Performance Fix

- Fixed unnecessary repeated BIW model-weight resolutions caused by inconsistent model-signature comparisons.
- Reduced redundant Penumbra resource lookups and memory allocations.
- Improved performance when model-derived Bone Importance Weighting is enabled.
- Preserved existing bone importance results, profile overrides, and actor eligibility behavior.

## Scope

- This is a narrow performance maintenance fix, not a new solver or a reduction in weighting quality.
- Existing model/resource identity checks, actor isolation, skeleton validation, and native-write safety remain intact.
- Performance varies with models, scene, and other plugins; this release does not claim to eliminate every possible microstutter.
