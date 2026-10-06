# SCN-CLUSTER-002: MaxSystemsPerTargetGrid saturation (strictly greater-than) and secondary task
## Area: CLUSTER
## Tier: 3
## Preconditions
- `MaxSystemsPerTargetGrid = 2`, `DisableLimitSystemsPerTargetGrid = false`.
- 4 single-BaR rigs (BoundingBox) around one separate target grid (40 damaged armor, owner = player);
  each rig also has 2 damaged blocks of its own (secondary work). Stocked cargo everywhere.
## Steps
1. Enable all 4; every 2 s read `OnGrid: W=n/max` on the panels and `Max Sys/Grid` on the HUD.
2. Wait for the target grid to be fully welded.
3. Lower the limit to 1 mid-run (`/nanobars config set MaxSystemsPerTargetGrid 1`) — include a BaR locked onto a
   projected block and one whose target was just razed; both must be released (BUG-261006.1).
4. Set `DisableLimitSystemsPerTargetGrid = true`; repeat step 1.
## Expected outcome
- `Mod.GridSystemCount[target]` never exceeds 2 (live counter, decremented atomically — BUG-077); the
  two refused BaRs report `LimitsExceeded` and immediately work their own grid's blocks instead of idling
  (BUG-018, memory "Idle BaRs when other grids available"), releasing any claim they held on the target.
- The limit does not deadlock: the target grid is still fully welded, just by ≤ 2 BaRs at a time.
- Lowering the limit releases surplus lock-ons (`ReleaseSurplusLockOnsForLoweredLimit`); the
  saturation tracker uses strictly `>` (grid at exactly the limit is not saturated); the self-subtract
  counts effective grid ids (BUG-260612.1).
- Disabled limit: all 4 work the target grid; `RebuildSaturatedGrids` is skipped.
## Perf envelope
`RebuildSaturatedGrids` maxMs ≤ 0.5; no `CountSystemsOnGrid`-style walk on the main thread (BUG-017).
## Coverage
planned: unit:GridSaturationTrackerTests.Rebuild_UsesStrictlyGreaterThan
ingame:S06
## Last verified
never
## Related
BUG-017, BUG-018, BUG-052, BUG-077, BUG-260610.21, BUG-260612.1, BUG-261006.1 (GitHub #146 / PR #147), FEAT-002, FEAT-260910.2/.3
