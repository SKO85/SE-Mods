# SCN-WELD-005: Missing components aggregate over all weld targets
## Area: WELD
## Tier: 2
## Preconditions
- Survival world. One BaR, **empty** cargo and welder inventory, 10 damaged armor blocks (each needs
  ~N SteelPlate). Optional: the companion script on a PB with an assembler group.
## Steps
1. Enable the BaR; wait 20 s.
2. Read the panel `Missing` list and, from a PB, `BuildAndRepair.MissingComponents`.
3. Add 50 % of the total need to cargo; wait 6 s; read again.
4. Add the rest; wait until all welded; read again.
## Expected outcome
- Step 2: `MissingComponents` lists `SteelPlate` with the **total** shortfall over all 10 targets at
  the configured `WeldOptions` level — not one block's worth, and not empty while targets are on
  failure cooldown or a transport timer runs (the aggregate rebuild runs independent of the weld
  loop). `GetWorkingState() == MissingComponents`.
- Timing (BUG-261023.1): the first list appears within ~3 s (per-BaR phase offset) plus the pass
  duration; a pass walks ≤ 128 targets per BaR call (4 slices per tick across all BaRs), then the
  sources, and the previous list stays visible until the new one is complete.
- The aggregate covers the BaR's capped target list (`MaxPossibleWeldTargets = 512`, BUG-261023.4):
  on bigger projections it is the need for the next 512 targets, not the whole blueprint.
- Step 3: the amount drops by the stock on hand (welder + transport + connected sources subtracted).
- Step 4: list empty; state `Idle`. The companion script queues the reported total to assemblers in
  one go (no 5–6-item trickle), and queues nothing once the list is empty.
- No block integrity changes while starved; no spikes (`ServerTryWelding` exits early on exhaustion).
## Perf envelope
`RebuildMissingComponentsAggregate` maxMs ≤ 1 per call; `ServerTryWelding` maxMs ≤ 5 while starved.
## Coverage
ingame:S02
unit:MissingComponentsAggregateTests.WalksAtMost128TargetsPerCall_AndPublishesTheTotalOnce
unit:MissingComponentsAggregateTests.StockOnHandIsSubtracted
unit:MissingComponentsAggregateTests.SourcesAreWalkedInSlices_AfterTheTargets
## Last verified
v2.5.6 build 261023.1 2026-10-23 — unit (logic only; in-game steps not yet run)
## Related
BUG-260824.2 (GitHub #142 / #83), BUG-260909.1 (aggregate cost at scale), BUG-261023.1 (time slicing), FEAT-260910.2
