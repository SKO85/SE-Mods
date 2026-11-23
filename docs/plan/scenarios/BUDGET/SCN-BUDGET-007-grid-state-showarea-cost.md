# SCN-BUDGET-007: Station vs ship standing still vs ship in motion, ShowArea off/on: cost and work

## Area: BUDGET
## Tier: 3
## Preconditions
- 5 BaRs (BoundingBox) with 20 damaged armor on their own grid. Grid states: station; ship (dynamic)
  standing still; ship drifting at 20 m/s with a slow yaw. Each with ShowArea off and on. Reported:
  server performance drops when players enable ShowArea on moving BaRs.
## Steps
1. Run each combination for 60 s (`test run S28`..`S33`, `x5` for stable numbers).
2. Compare `ShowAreaDraw` (per-frame draw cost, clients / listen server only), tick cost, scans
   (`AsyncClusterScan`, `AsyncAddBlocksOfBox`), cluster rebuilds, entity queries and sim speed.
## Expected outcome
- All 20 blocks repaired in every grid state.
- ShowArea adds only the per-frame draw cost on machines that render (never on a dedicated server; the
  flag is not part of the cluster key). Moving grids should not multiply scans or cluster rebuilds.
- Differences found here become a ticket with numbers.
## Perf envelope
harness metrics; thresholds from the first green runs.
## Coverage
ingame:S28
ingame:S29
ingame:S30
ingame:S31
ingame:S32
ingame:S33
unit:AreaDrawModeTests
## Last verified
v2.5.6 build 261122.8 2026-11-22 — in game (01_211_018, SP, 5 BaRs): all six green; tick avg 0.010-0.014 ms, sim 1.00,
scans 9 / cluster rebuilds 30 / entity queries 18 in every state; ShowAreaDraw 0.004 ms per BaR per frame (BUG-261122.8).
## Related
FEAT-260910.2
