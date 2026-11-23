# SCN-INV-002: Push-immediately flags and inventory-full pause
## Area: INV
## Tier: 2
## Preconditions
- One BaR with a conveyor-connected cargo. Manually put into the BaR inventory: 100 SteelPlate,
  50 Iron ingots, 10 Iron ore, 1 hand drill (item).
- Second run: a tiny welder inventory (or `BlocksInventorySizeMultiplier` low) and `GrindOnly` on a big grid.
## Steps
| Step | Flags on | Expect pushed within 10 s |
|---|---|---|
| 1 | none | nothing (items stay until needed / grind loot push cadence) |
| 2 | `PushComponentImmediately` | SteelPlate only |
| 3 | + `PushIngotOreImmediately` | + ingots and ore |
| 4 | + `PushItemsImmediately` | + the drill |
5. Second run: grind until the welder is full; watch the panel `Inventory Full` line and HUD.
## Expected outcome
- Each flag pushes exactly its class; the idle fast-path does not skip auto-push when the welder
  holds items (BUG-089); pushing a component needed for the current weld target is deferred while
  the target is starved (components bounce-back seen in #141 must not recur after BUG-260824.1).
- Inventory-full: `CheckAndUpdateInventoryFull` sets `InventoryFull`, grinding/collecting pause
  (BUG-015), the flag clears once the welder drains (BUG-260526.1), an active pick resumes when room
  appears (Operations re-read of state).
- With `*ImmediatelyFixed` server flags the switches are disabled and the property setters are no-ops.
## Perf envelope
`CheckAndUpdateInventoryFull` steadyAvgMs ≤ 0.1; `ServerEmptyTransportInventory` maxMs ≤ 2.
## Coverage
manual
ingame:S89
unit:InventoryFullLatchTests
ingame:S90
ingame:S91
ingame:S92
ingame:S93

## Last verified
v2.5.7 build 261123.19 2026-11-23 — in game: S89-S93 (push options, full storage); unit green.

## Related
BUG-015, BUG-089, BUG-114, BUG-126, BUG-260526.1, BUG-260612.25/.26, BUG-260824.1, GitHub #141
