# SCN-WELD-006: Dented full-integrity blocks get a budgeted skeleton reset, once
## Area: WELD
## Tier: 2
## Preconditions
- Creative or survival world. One BaR (`WeldFull`, Grids or BoundingBox) on a rig with 50 armor blocks.
- Dent the blocks without destroying them (collide a small ship into them, or damage then weld them back
  to full integrity) so their `MaxDeformation` is above `SlimBlockExtensions.MinDeformation`.
- Variant B: same with `WeldOptions = WeldSkeleton`, with the blocks painted the ignore colour, or with
  the armor class disabled in the weld priority list.
## Steps
1. `/nanobars profile start 300 0 dents`; enable the BaR and let it run 5 minutes.
2. Read `SkeletonResetHandler.Process` lines in the profile.
3. Variant B: repeat.
## Expected outcome
- Dents are repaired visually within ~1 minute (resets ≤ 2 per tick across all BaRs).
- After that, the blocks are **not** reset again every 60 s: `MaxDeformation` stays latched in the engine,
  so they are still enqueued, but the drain re-checks `HasDeformation` and logs them as
  `skippedNotDeformed` instead of `resets` (BUG-261023.3). ≤ 16 dequeues per tick.
- Variant B: nothing is enqueued — skeleton mode never repairs existing blocks, and ignore colour /
  disabled priority class / safe-zone welding block it like any weld target.
- Deformation-only blocks never appear in `Blocks to build` (BUG-260827.1).
## Perf envelope
`SkeletonResetHandler.Process` maxMs ≤ 1; steady state after the first minute: `resets=0`.
## Coverage
manual
unit:SkeletonResetHandlerTests.NotDeformedBlocks_AreSkipped_AndGetTheCooldown
unit:SkeletonResetHandlerTests.MixedQueue_SkipsDoNotCountAgainstTheResetCap
## Last verified
v2.5.6 build 261023.3 2026-10-23 — unit (drain logic only; in-game steps not yet run)
## Related
BUG-260824.7, BUG-260827.1, BUG-260610.5, BUG-261023.3
