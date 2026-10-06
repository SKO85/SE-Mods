# SCN-PRIO-001: Priority list enable / disable / reorder reflected in target order; reset
## Area: PRIO
## Tier: 2
## Preconditions
- One BaR, stocked cargo, damaged blocks of three classes on the own grid (e.g. Armor, Conveyors,
  Thrusters), 5 each.
## Steps
1. Terminal weld priority list: move `Thrusters` to the top; observe `Blocks to build` order and the
   first `CurrentWeldingBlock`.
2. Disable `Armor` (checkbox); observe armor blocks vanish from the list after the rescan.
3. `Disable All` then `Enable All` buttons; toggle the `WeldPriority_OnOff` action.
4. `Reset All Settings` button.
5. From a PB: `GetWeldPriority(i)` / `SetWeldPriority(i, n)` / `SetWeldEnabled(i, false)`
   (requires script control not fixed).
## Expected outcome
- Order follows the list (`BlockWeldPriority.GetPriority`), cache keyed per block honours the
  `real` parameter (BUG-022) and is cleared on enable/disable (BUG-046); the scan-side sort cache is
  cleared per scan (BUG-260610.40).
- Disabled classes are not scanned as targets; `Enable All` restores them; settings-change triggers an
  immediate rescan (FEAT-080).
- Reset restores the default order and all-enabled state (BUG-056); the grind and collect lists behave
  identically (`GrindPriority`, `ComponentClassList`).
- The persisted `WeldPriority` string round-trips through world save/load and the PB API.
## Perf envelope
`priorityMs` in `ServerTryWelding` details ≤ 0.2 per call.
## Coverage
manual
## Last verified
never
## Related
BUG-004, BUG-022, BUG-046, BUG-049, BUG-056, BUG-260610.16, BUG-260610.43, FEAT-080
