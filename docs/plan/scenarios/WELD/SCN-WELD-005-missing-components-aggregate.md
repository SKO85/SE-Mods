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
  failure cooldown or a transport timer runs (the aggregate rebuild runs every 3 s independent of
  the weld loop). `GetWorkingState() == MissingComponents`.
- Step 3: the amount drops by the stock on hand (welder + transport + connected sources subtracted).
- Step 4: list empty; state `Idle`. The companion script queues the reported total to assemblers in
  one go (no 5–6-item trickle), and queues nothing once the list is empty.
- No block integrity changes while starved; no spikes (`ServerTryWelding` exits early on exhaustion).
## Perf envelope
`RebuildMissingComponentsAggregate` maxMs ≤ 2 with 10 targets; `ServerTryWelding` maxMs ≤ 5 while starved.
## Coverage
ingame:S02
## Last verified
never
## Related
BUG-260824.2 (GitHub #142 / #83), BUG-260909.1 (aggregate cost at scale), FEAT-260910.2
