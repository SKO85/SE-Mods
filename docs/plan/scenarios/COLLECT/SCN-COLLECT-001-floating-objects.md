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
## Perf envelope
`ServerTryCollectingFloatingTargets` maxMs ≤ 3 (engine work outside the list lock, BUG-260610.22).
## Coverage
manual
## Last verified
never
## Related
BUG-012, BUG-015, BUG-020b, BUG-260610.22, BUG-260612.21..24
