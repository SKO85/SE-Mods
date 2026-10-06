# SCN-CORE-001: Idle → Welding → Idle state transitions on one damaged grid
## Area: CORE
## Tier: 1
## Preconditions
- Offline creative or survival world, admin player, default `ModSettings.xml`.
- One static large grid: 1 large BaR (`SELtdLargeNanobotBuildAndRepairSystem`), 1 large cargo container
  stocked with 500 SteelPlate, 1 battery, 10 `LargeBlockArmorBlock` at ~20 % build level.
- BaR: `SearchMode = Grids`, `WorkMode = WeldBeforeGrind`, `AllowBuild` on, default priorities, block enabled.
## Steps
1. Enable the BaR and watch the terminal info panel (`State:` line) and, with DebugMode on, `Next target scan`.
2. Wait for the first scan (≤ `TargetsUpdateInterval`, default 2 s) — `Blocks to build` lists the 10 armor blocks.
3. Let it weld until all 10 report full integrity.
4. Wait a further 10 s.
## Expected outcome
- `GetWorkingState()` sequence: `NotReady` (before first scan) → `Welding` (transport phases show
  `Welding (Transporting)`) → `Idle`. Never `MissingComponents` or `InventoryFull`.
- `State.PossibleWeldTargets.CurrentCount` goes 10 → 0; `State.MissingComponents` stays empty.
- After completion `TriggerImmediateRescan(reason=weldComplete)` fires once, then the idle backoff
  (`_consecutiveEmptyScans` reaches `IdleScansBeforeBackoff = 3`) stretches the scan interval to
  `IdleScanInterval` (10 s) — `Next target scan` on the panel reflects it.
- Cargo SteelPlate decreased by the sum of the 10 blocks' missing components; BaR inventory ends empty
  (or holds only leftovers pushed back when `PushComponentImmediately` is on).
## Perf envelope
- `ServerDoWeld` steadyAvgMs ≤ 2, maxMs ≤ 30; `ServerTryWelding` maxMs ≤ 10.
- Mod tick cost peak ≤ 10 ms; sim speed min ≥ 0.9.
## Coverage
ingame:S01
## Last verified
never
## Related
FEAT-260910.2, BUG-014 (no work before initial scan), FEAT-071 (idle backoff), FEAT-080 (immediate rescan)
