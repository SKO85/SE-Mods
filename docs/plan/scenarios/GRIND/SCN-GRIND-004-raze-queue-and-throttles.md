# SCN-GRIND-004: Raze queue and mech / dismount per-tick throttles
## Area: GRIND
## Tier: 2
## Preconditions
- 6 BaRs, `GrindOnly`, one stagger group, grinding one 200-block unowned grid that contains 10
  mechanical blocks (rotors/pistons/hinges) and many fully-dismounted armor blocks.
## Steps
1. Enable all; profile 60 s; open the debug HUD.
2. Read `ServerDoGrind` details (`dismounted`, `razeMs`, `mechCheckMs`) and `RazeQueueHandler` depth.
## Expected outcome
- Full dismounts are capped at 3 per tick across all BaRs (`TryClaimDismountSlot`); mechanical blocks
  at 1 per tick (`TryClaimMechanicalGrindSlot`); a refused BaR keeps its lock-on and refunds nothing.
- Block removal is deferred to `RazeQueueHandler.Process` (throttled, batched per grid); a mech-block
  deferral does not starve non-mech razes in the same drain (BUG-260824.12); the queue drains to 0
  after grinding stops; no entry survives a grid close.
- A completed mech grind is reported as success, not failed (BUG-260612.20).
## Perf envelope
`RazeQueueHandler.Process` maxMs ≤ 5; `ServerDoGrind` `decreaseMs` ≤ 12 (engine).
## Coverage
planned: unit:PerTickBudgetTests
## Last verified
never
## Related
BUG-106, BUG-127, BUG-260511.10, BUG-260522.1/.2, BUG-260612.20, BUG-260824.12, FEAT-260910.3
