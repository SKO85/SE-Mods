# SCN-GRIND-003: Grind order: near / far / smallest-grid first, ignore priority order
## Area: GRIND
## Tier: 2
## Preconditions
- One BaR, `GrindOnly`, BoundingBox area 150 m. Three unowned grids: a 3-block grid at 100 m, a
  30-block grid at 50 m, a 10-block grid at 20 m; mixed block classes so the grind priority list matters.
## Steps
| Run | Flags | Expected first target grid |
|---|---|---|
| 1 | `GrindNearFirst` | 20 m grid |
| 2 | neither near nor smallest (= far first) | 100 m grid |
| 3 | `GrindSmallestGridFirst` | 3-block grid |
| 4 | `GrindNearFirst` + priority list with only `Armor` disabled | nearest non-armor block first |
| 5 | `GrindIgnorePriorityOrder` + near | pure distance order, list order ignored |
1. Enable, note the first `CurrentGrindingBlock` and the order in `Blocks to grind` (top 24 shown).
2. Toggle the flag (`GrindNearFirst_OnOff` action) mid-run — immediate rescan, new order.
## Expected outcome
- `SortAndCapGridCandidates` produces the documented order; the cap keeps the best `MaxPossibleGrindTargets`
  and `QuickSelect` picks the same set as a full sort would (order within the kept set may differ).
- Locality hint: after a kill, the next target is near the last ground position when near-first is on.
- Deselecting a sort toggle never clears the others (BUG-008); user settings are respected in the sort (BUG-086).
## Perf envelope
`SortAndCapGridCandidates` maxMs ≤ 5 for 1000 candidates (distance/priority cached per compare, BUG-099/100).
## Coverage
planned: unit:QuickSelectTests.MatchesSortForKSmallest_RandomInputs
## Last verified
never
## Related
BUG-008, BUG-019, BUG-086, BUG-091, BUG-094, BUG-096, BUG-099, BUG-100, FEAT-260910.3
