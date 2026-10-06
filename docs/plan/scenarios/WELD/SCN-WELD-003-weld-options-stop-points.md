# SCN-WELD-003: Weld options Full / Functional / Skeleton stop at the right integrity
## Area: WELD
## Tier: 2
## Preconditions
- One BaR, stocked cargo, three identical damaged functional blocks (e.g. `LargeBlockSmallContainer`
  at ~5 % build) — one per run — plus one projected copy of each for the `Create` level.
## Steps
| Run | `WeldOptions` (`WeldMode_*_On` action) | Expected stop |
|---|---|---|
| 1 | `WeldFull` | `IsFullIntegrity` |
| 2 | `WeldFunctional` | block becomes functional (`IsFunctional`), welding stops below full |
| 3 | `WeldSkeleton` | projected block is placed and welded only to the `Create` level |
1. Run each option from a fresh damaged/projected block; watch the panel and the block's build state.
2. Switch from `WeldSkeleton` to `WeldFull` while targets exist.
## Expected outcome
- `IsWeldIntegrityReached(target)` uses the level from `Settings.WeldOptions`; after the stop point the
  block leaves `Blocks to build` and the BaR moves on / goes Idle.
- The missing-components aggregate uses the same level (`RebuildMissingComponentsAggregate`), so
  `Missing` under `WeldSkeleton` lists only `Create`-level components.
- Switching the option triggers an immediate rescan and the half-welded blocks reappear as targets.
## Perf envelope
n/a
## Coverage
manual
## Last verified
never
## Related
BUG-075 (silent catch weld integrity), BUG-260824.2 (aggregate level), FEAT-260910.2 (candidate S10)
