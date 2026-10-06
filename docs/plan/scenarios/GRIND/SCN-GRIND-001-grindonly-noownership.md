# SCN-GRIND-001: GrindOnly with NoOwnership grinds only unowned grids
## Area: GRIND
## Tier: 1
## Preconditions
- One BaR, `WorkMode = GrindOnly`, `SearchMode = BoundingBox`, `UseGrindJanitorOn = NoOwnership` only,
  `Welder.AllowedGrindJanitorRelations` includes `NoOwnership`; BaR inventory empty, push target cargo attached.
- Three separate grids 40 m away: A owned by nobody (30 armor), B owned by the player, C owned by a
  neutral faction.
## Steps
1. Enable the BaR; watch `Blocks to grind` and the debug HUD `Grind Budget`.
2. Wait until grid A is gone; inspect the push-target cargo and the area for floating objects.
## Expected outcome
- Only grid A's blocks are listed and ground (`ServerDoGrind`); B and C untouched.
- Grind loot lands in the BaR inventory and is pushed to the cargo (`EmptyBlockInventories`,
  `ServerTryPushInventory`); no floating debris remains inside the area (collect picks up any).
- Grid A's entity closes (last block razed via `RazeQueueHandler`); the BaR goes `Idle`.
- `GrindBudgetPeakUsed ≤ GetEffectiveMaxGrindsPerTick()`.
## Perf envelope
`ServerDoGrind` steadyAvgMs ≤ 2, maxMs ≤ 30 (`decreaseMs` engine cascade on full dismount);
`RazeQueueHandler.Process` maxMs ≤ 5.
## Coverage
ingame:S04
## Last verified
never
## Related
BUG-033, BUG-042, BUG-067, BUG-127 (raze queue), BUG-260612.30 (NoOwnership policy), BUG-260827.2 (loot inventory path — verify), FEAT-260910.2
