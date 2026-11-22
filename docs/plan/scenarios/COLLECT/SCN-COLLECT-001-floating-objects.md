# SCN-COLLECT-001: Floating objects inside area only, CollectIfIdle, component class priority
## Area: COLLECT
## Tier: 2
## Preconditions
- One BaR, `SearchMode = BoundingBox` (collecting is only possible in BoundingBox mode), push-target
  cargo attached. Drop floating items: 5 SteelPlate stacks inside the area, 3 just outside, 1 ore, 1 ingot.
- Collect priority list: `Components` enabled, `Ore` disabled, `Ingots` enabled.
- A damaged block on the own grid with stock available (to exercise `CollectIfIdle`).
## Steps
1. `CollectIfIdle` off: enable the BaR; observe `Items to collect` and the cargo.
2. `CollectIfIdle` on: repeat with the damaged block present, then after it is welded.
3. Enable `Ore` in the collect priority list.
## Expected outcome
- Only items whose OBB containment passes are listed (BUG-020b); the 3 outside stay put;
  `MaxPossibleFloatingTargets = 16` cap respected.
- Ore is ignored until its class is enabled; ingots and components are picked up and pushed to cargo.
- `CollectIfIdle` on: no collecting while weld work exists; collecting starts once `Idle`.
  Off: collecting interleaves (`Collecting (Transporting)` state).
- Collected items never exceed welder capacity (`InventoryFull` pause, BUG-015).
- Conservation (BUG-261023.5): every collected item arrives — cargo + welder + transport equals what
  was dropped; a refused add never removes or shrinks the floating object.
## Perf envelope
`ServerTryCollectingFloatingTargets` maxMs ≤ 3 (engine work outside the list lock, BUG-260610.22).
## Coverage
manual
ingame:S10 (5 SteelPlate stacks inside the area, conservation; not the outside/ore/priority steps)
unit:TransportInventoryMoveTests.MoveItemVerified_RefusedAdd_LeavesTheSourceUntouched
unit:TransportInventoryMoveTests.MoveItemVerified_RemovesOnlyWhatLanded
## Last verified
v2.5.6 build 261023.2 2026-10-23 — unit (verified-move helpers only; in-game S10 not yet run)
## Related
BUG-012, BUG-015, BUG-020b, BUG-260610.22, BUG-260612.21..24, BUG-261023.5
