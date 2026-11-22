# SCN-INV-001: Source scan via conveyors, pull cursor rotation, push-targets-full backoff
## Area: INV
## Tier: 2
## Preconditions
- One BaR on a grid with 3 cargo containers on the conveyor network (each holding some SteelPlate),
  1 cargo *not* conveyor-connected, 1 sorter blocking components toward the BaR, an assembler, a cryo
  chamber and a refinery. 20 damaged armor blocks.
- Second run: all connected containers full (tiny containers), BaR inventory holding grind loot.
## Steps
1. Enable the BaR; with DebugMode on read `Sources: N | Push Targets: M` on the panel.
2. Profile 60 s; read `AsyncScanForSources` (`sourcesFound`, `addIfConnCalls`) and
   `PullFromSourcesOnePass` (`visited`, `transferSucceeded`, `lastSuccessfulHit`).
3. Second run: watch `ServerTryPushInventory` and the HUD `Inventory Full`.
## Expected outcome
- `Sources` counts only conveyor-reachable inventories (`AddIfConnectedToInventory`); the unconnected
  cargo is excluded; cryo/refinery/assembler are included (FEAT-011); other BaR inventories are never
  push targets (BUG-007).
- Pulling rotates through sources via the cursor and remembers the last successful source
  (`_LastSuccessfulSource`, BUG-129); the sorter-blocked source fails cheaply (`TransferItemFrom` false).
- Push-full: after one full pass with no successful push the BaR pauses pushing
  (`_PushTargetsFull`, signature-based) instead of retrying every cycle (BUG-016/BUG-090); freeing space
  or adding a container resumes within one cycle (BUG-126 constant-spike case).
## Perf envelope
`PullFromSourcesOnePass` maxMs ≤ 3 with 100 sources; `ServerTryPushInventory` steadyAvgMs ≤ 0.5 while full.
## Coverage
manual
## Last verified
never
## Related
BUG-007, BUG-011, BUG-016, BUG-090, BUG-119, BUG-126, BUG-129, BUG-148, FEAT-006, FEAT-011
