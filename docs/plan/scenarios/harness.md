# In-game test harness — scenario reference

The harness runs **scenarios** (`S00`..`S57`) inside a world with the `-Testing` mod variant. Each one spawns its own
rig, configures the BaRs, runs, checks the outcome and the performance numbers, removes everything, and writes one
`result=` line to `NanobotTest.<session>.log`. The behaviour catalogue (`SCN-<AREA>-NNN` files next to this page)
says *what* must hold; the scenarios here are the *automated checks* for it. Every `ingame:Sxx` coverage token in
the catalogue points to an entry below (`CatalogueConsistencyTests` enforces both directions).

Code: `_Scripts/BuildAndRepairSystem/Testing/Data/Scripts/SKO-Nanobot-BuildAndRepair-System/Testing/` (registry:
`Scenarios/ScenarioRegistry.cs`). Results: `%AppData%\SpaceEngineers\Storage\SKO-Nanobot-BuildAndRepair-System-Testing_SKO-Nanobot-BuildAndRepair-System\`.

## Running

- World: empty **survival** world, only the `-Testing` mod, admin creative tools **off** (`test run` refuses creative
  worlds; creative tools skip the game's projector component check).
- `/nanobars test clear` — delete old results. `/nanobars test list` — show the registry.
- `/nanobars test run <id|id,id|id-id|all> [xN]` — e.g. `S03`, `S03,S20`, `S28-S33`, `S01-S05 x3`. Ranges follow the
  `test list` order. A hidden warm-up (`S00`) runs first in every session; each scenario posts a start line in chat.
- Spawn safety: at the start of a run, leftover test grids (`NanobotTest-*` and anything attached, e.g. saved by an
  autosave mid-run) are removed; any other grid within 150 m of the origin stops the run with its name (move away or
  use `test origin`) — spawning into a grid makes the physics explode. Parts that split off a test grid while
  grinding are tracked and named `NanobotTest-split-*`, so they are removed with the scenario.
- Scenarios that cannot run in the world (e.g. DefenseShields not loaded, no pirate faction) are reported as
  `SKIPPED` with the reason (`pass=skipped` in the log) and count as neither pass nor fail.
- `/nanobars test status`, `/nanobars test abort`, `/nanobars test origin <x> <y> <z>|reset` (default: 300 m in front of you).
- Every scenario: rigs spawn with their BaRs **switched off**, are configured, the game warms up 3 s, then the BaRs
  are switched on and the measured run starts. Mod settings are snapshotted before and restored after each scenario
  (never written to `ModSettings.xml`); spawned grids, zones and subgrids are removed.
- Standard metrics on every result line: `runS`, `tickAvgMs`/`tickPeakMs` (mod tick cost), `simMin`/`simAvg`,
  `weldBudgetPeak`, `grindBudgetPeak`, `assignmentsPeak` (claims), `otherBars`, plus `calls/steadyAvgMs/maxMs` for the
  scenario's profiled methods.

## Reuse or add? — index by area

When changing or investigating something, run the scenarios of that area first; add a new one only when the
behaviour is not listed here (then also add/extend the `SCN-` catalogue entry).

| Area | Scenarios |
|---|---|
| Welding basics (repair, stock, shortage, state) | S01, S02, S57 |
| Damage detection / re-damage / placed frames (BUG-261122.10, .11) | S57, S72 |
| Projector building, component accounting, blockers | S03, S27, S42, S46 |
| Grinding / janitor | S04, S11–S13, S16, S17, S45, S47, S73, S74 |
| Protected grids (indestructible, plugin protection) | S73, S74 |
| Grind order (far / near / smallest) | S11, S12, S13 |
| Priority lists (order, disabled classes) | S14, S15, S16 |
| Ownership and relations (own, own-built unowned, derelict, pirates) | S17, S06, S08, S44, S45 |
| Subgrids: rotor, advanced rotor, hinge, piston, wheel, docked connector | S18, S34, S35, S36, S37, S38 |
| Ships, moving grids | S28–S33, S43, S44, S45 |
| Power | S19, S20 |
| Weld options (Full / Functional / Skeleton) | S01, S21, S22 |
| Colours (ignore / grind) | S23, S24 |
| Area, range, search mode | S25, S49 |
| Work modes | S26, S48 |
| Safe zones | S39, S40, S41, S42, S53 |
| DefenseShields (skipped without DS) | S58, S59, S60, S61, S62, S63 |
| Clusters, per-grid limit | S06, S07, S50, S54, S64–S70 |
| Block claims (assign / release / TTL) | S05, S51, S55, S56 |
| Admin restrictions and config switches | S25, S46–S53 |
| Collecting floating objects | S10 |
| Performance envelopes / measurements | S08, S28–S33, S43, S64–S70 |
| Many BaRs / load (weld, grind, mixed; large vs small clusters) | S64–S70 |
| PB scripting API, companion script (unit: CompanionScriptBehaviourTests, CompanionScriptContractTests) | S09, S71 |

## Summary

Time = measured run (plus ~5 s setup). Last green = BuildId of the last passing in-game run (2026-11-22, 01_211_018).

| ID | What it proves | Catalogue | Time | Last green |
|---|---|---|---|---|
| S00 | Warm-up (not a test): JIT the weld/grind/scan paths once per session | — | 3 s | 261122.11 |
| S01 | One BaR repairs 10 damaged blocks from stocked cargo | CORE-001, WELD-001 | 3 s | 261122.8 |
| S02 | Empty cargo: missing components reported, nothing welded, no spikes | WELD-005 | 20 s | 261122.8 |
| S03 | Projector: 10 blocks built from an empty block inventory, charged once | WELD-002 | 3 s | 261122.20 |
| S04 | Janitor (GrindOnly, NoOwnership) razes a derelict grid | GRIND-001 | 11 s | 261122.8 |
| S05 | Two BaRs never weld the same or a foreign-claimed block | BUDGET-001 | 3 s | 261122.8 |
| S06 | MaxSystemsPerTargetGrid=2 held with 4 BaRs, grid still finished | CLUSTER-002 | 9 s | 261122.8 |
| S07 | Five co-located BaRs: one cluster, one scanner | CLUSTER-001 | 3 s | 261122.8 |
| S08 | Perf: 5 BaRs weld a 1000-block station (#143 envelope) | BUDGET-006 | 64 s | 261122.8 |
| S09 | PB API: all companion-script properties exist and read back; EnsureQueued fills an assembler | API-001 | 3 s | 261122.18 — 46/46 properties, EnsureQueued 10/10 |
| S10 | Floating stacks collected into cargo, no item lost | COLLECT-001 | 1 s | 261122.8 |
| S11 | Grind order farthest-first (default) | GRIND-003 | 2 s | 261122.8 |
| S12 | Grind order GrindNearFirst | GRIND-003 | 2 s | 261122.8 |
| S13 | Grind order GrindSmallestGridFirst | GRIND-003 | 2 s | 261122.8 |
| S14 | Weld priority: armor first, all armor before the first conveyor | PRIO-001 | 4 s | 261122.8 |
| S15 | Weld priority: conveyor class disabled, conveyors untouched | PRIO-001 | 6 s | 261122.8 |
| S16 | Grind priority: conveyor class disabled, janitor grinds armor only | PRIO-001 | 1 s | 261122.8 |
| S17 | Ownership matrix: own + own-built welded, derelict + pirate ground | GRIND-001, GRIND-002 | 4 s | 261122.8 |
| S18 | Rotor head subgrid repaired, not ground | WELD-007 | 1 s | 261122.8 |
| S19 | Battery off: nothing; battery on at 10 s: all repaired | CORE-003 | 12 s | 261122.8 |
| S20 | Welding power above supply: BaR reports unpowered (BUG-261122.9) | CORE-003 | 30 s | 261122.11 |
| S21 | WeldFunctional stops at functional integrity | WELD-003 | 7 s | 261122.8 |
| S22 | WeldSkeleton does not repair damaged blocks | WELD-003 | 15 s | 261122.8 |
| S23 | Ignore colour: painted damaged blocks skipped | WELD-004 | 7 s | 261122.8 |
| S24 | Grind colour: painted own blocks ground | GRIND-005 | 2 s | 261122.8 |
| S25 | Mod Range 20 m: near grid repaired, far grid untouched | CORE-002 | 18 s | 261122.8 |
| S26 | WeldOnly never grinds (janitor configured) | WELD-001 | 12 s | 261122.8 |
| S27 | Projector with AllowBuild off: nothing built | WELD-002 | 20 s | 261122.11 |
| S28 | Measure: station, ShowArea off | BUDGET-007 | 60 s | 261122.8 |
| S29 | Measure: station, ShowArea on | BUDGET-007 | 60 s | 261122.8 |
| S30 | Measure: ship standing still, ShowArea off | BUDGET-007 | 60 s | 261122.8 |
| S31 | Measure: ship standing still, ShowArea on | BUDGET-007 | 60 s | 261122.8 |
| S32 | Measure: ship moving 20 m/s, ShowArea off | BUDGET-007 | 60 s | 261122.8 |
| S33 | Measure: ship moving 20 m/s, ShowArea on | BUDGET-007 | 60 s | 261122.8 |
| S34 | Advanced rotor head subgrid repaired | WELD-007 | 2 s | 261122.11 |
| S35 | Hinge head subgrid repaired | WELD-007 | 1 s | 261122.11 |
| S36 | Piston head subgrid repaired | WELD-007 | 2 s | 261122.12 |
| S37 | Wheel subgrid repaired | WELD-007 | 1 s | 261122.12 |
| S38 | Ship docked by connector repaired through the connector | WELD-007 | 1 s | 261122.11 |
| S39 | Safe zone without Welding over the BaR: nothing welded | ZONE-001 | 20 s | 261122.11 |
| S40 | Safe zone allowing everything: welded as usual | ZONE-001 | 2 s | 261122.11 |
| S41 | Safe zone without Grinding around a derelict: not ground | ZONE-001 | 20 s | 261122.11 |
| S42 | Safe zone without BuildingProjections: projection not built | ZONE-001, WELD-002 | 20 s | 261122.11 |
| S43 | Very large ship: 4000 blocks, 5 BaRs on board, 120 s | BUDGET-006 | 120 s | 261122.11 |
| S44 | Two ships in flight: friendly ship repaired | WELD-007 | 2 s | 261122.11 |
| S45 | Two ships in flight: pirate ship ground | GRIND-002 | 2 s | 261122.11 |
| S46 | Admin AllowBuildFixed (default off): projection not built | CFG-003, WELD-002 | 20 s | 261122.11 |
| S47 | Admin janitor relations = Enemies: NoOwnership forced off | CFG-003 | 20 s | 261122.11 |
| S48 | Admin work modes = WeldOnly: GrindOnly forced to WeldOnly | CFG-003 | 12 s | 261122.11 |
| S49 | Admin search modes = Grids: BoundingBox forced to Grids | CFG-003 | 12 s | 261122.11 |
| S50 | DisableLimitSystemsPerTargetGrid: limit 2 ignored | CFG-003, CLUSTER-002 | 4 s | 261122.11 |
| S51 | AssignToSystemEnabled=false: finished without claims | CFG-003, BUDGET-001 | 4 s | 261122.11 |
| S52 | MaxWeldsPerTick=1: never more than one weld per tick | CFG-003, BUDGET-002 | 1 s | 261122.11 |
| S53 | SafeZoneCheckEnabled=false: BaR ignores a no-welding zone | CFG-003, ZONE-001 | 2 s | 261122.11 |
| S54 | Per-grid limit lowered 4 → 2 while working: surplus released | CLUSTER-002 | 35 s | 261122.12 |
| S55 | Blocks claimed while welded, none left claimed afterwards | BUDGET-001 | 7 s | 261122.13 |
| S56 | Welding BaR switched off: claim released, other BaR finishes | BUDGET-001 | 22 s | 261122.13 |
| S57 | Repaired blocks damaged again (twice) are detected and repaired | CORE-001 | 41 s | 261122.16 — detected 2.4 s / 1.5 s |
| S58 | DS: pirate ship with its shield up is not ground | ZONE-001 | ~40 s | 261122.20 — shield up in 0.5 s, 10/10 left |
| S59 | DS: pirate ship with its shield switched off is ground (BUG-261122.12) | ZONE-001 | ~45 s | 261122.22 — ground in 6.7 s with the shield down |
| S60 | DS: ShieldCheckEnabled=false grinds through a raised shield | ZONE-001 | ~35 s | 261122.25 — ground in 7.7 s |
| S61 | DS: shield check switched on mid-grind stops the grinding | ZONE-001 | ~45 s | 261122.25 — 0 lost after the switch |
| S62 | DS: BaR ship inside its own shield welds its own blocks | ZONE-001 | ~30 s | 261122.20 — 6/6 welded, IsShielded |
| S63 | DS: friendly ship with its shield up is welded from outside | ZONE-001 | ~30 s | 261122.20 — 6/6 welded |
| S64 | Load weld: 20 BaRs (2 x 10) on ships of 25/150/600 blocks | BUDGET-008 | 22 s | 261122.16 — 35.9 welds/s, 2 clusters |
| S65 | Load weld: 20 BaRs (10 x 2) | BUDGET-008 | 14 s | 261122.16 — 55.7 welds/s, 10 clusters |
| S66 | Load grind: 20 BaRs (2 x 10) on derelicts of 25/150/600 blocks | BUDGET-008 | 38 s | 261122.16 — 20.4 ground/s |
| S67 | Load grind: 20 BaRs (10 x 2) | BUDGET-008 | 21 s | 261122.16 — 37.5 ground/s |
| S68 | Load mixed: 20 BaRs (2 x 10), own + derelict ships | BUDGET-008 | 57 s | 261122.16 — 13.6 + 13.6 /s |
| S69 | Load mixed: 20 BaRs (10 x 2) | BUDGET-008 | 37 s | 261122.16 — 21.0 + 21.0 /s |
| S70 | Stress mixed: 40 BaRs (4 x 10) | BUDGET-008 | 36 s | 261122.16 — 21.8 + 21.8 /s, 40 BaRs |
| S71 | Companion script in a real PB: queuing, LCDs, info-only / queue, recompile | API-002 | 20 s | 261122.18 — queued in 3.7 s; info-only kept over recompile |
| S72 | Block frame added to a repaired grid (after 3 s and 30 s idle) is welded | CORE-001 | 39 s | 261122.20 — welded 2.8 s / 0.2 s after placing |
| S73 | Protected grid: indestructible derelict is not ground (no grind call) | GRIND-006 | 20 s | 261122.25 — 0 grind calls |
| S74 | Protected grid: damage-handler (plugin style) protection respected, no retry storm | GRIND-006 | 20 s | 261122.25 — 1 refused attempt, nothing ground |

## Scenarios

Common rig ("BaR rig"): a static large grid with N BaRs side by side on the base layer, small cargo containers
around them (conveyor-connected), a charged battery; damaged armor on top (`DamagedArmor`, 20 % integrity unless
noted). Separate target grids are 40 m-ish away. `WorkSpeed=10` unless noted (fast, so runs are short).

### S00
Warm-up, not a test. 1-BaR rig with 5 damaged blocks plus a derelict 3-block grid; weld + janitor grind. Queued first
in every session so the first real scenario does not pay the one-time JIT cost after a world load (S01 once measured
38 ms there). No checks.

### S01
**Proves** the basic repair loop. **Setup** 1 BaR, 10 damaged armor, 500 SteelPlate, Grids, WeldBeforeGrind, WeldFull.
**Pass** all 10 at full integrity, no missing components; `ServerDoWeld` avg ≤ 2 ms / max ≤ 30 ms, `ServerTryWelding`
max ≤ 10 ms, tick peak ≤ 10 ms, sim ≥ 0.9. **Use when** touching the weld loop, targets, inventories.

### S02
**Proves** shortage handling. **Setup** as S01 but no stock, 20 s window. **Pass** nothing welded, `MissingComponents`
lists SteelPlate, state `MissingComponents`, `ServerTryWelding` max ≤ 5 ms, aggregate max ≤ 2 ms, tick peak ≤ 5 ms.
**Use when** touching component sourcing / the missing-components aggregate (BUG-261023.1).

### S03
**Proves** projected building on SE 1.211.018 (BUG-261122.1): the creation component is lent to the BaR block
inventory, used once. **Setup** 1 BaR + projector at (2,0,1); the projector shows its own grid plus 10 armor blocks on
top of the rig (blueprint block 0 = the projector, so the projection lines up); AllowBuild + PushComponentImmediately;
1000 SteelPlate. **Pass** 10 new blocks, all on the expected cells, all welded; SteelPlate on the rig = 1000 − 10 × 25
(no free, no double charge); `ServerDoWeld` max ≤ 40 ms. **Records** `builtAt`, `projectorRemaining`. **Use when**
touching `ServerDoWeld` projected path, projector scanning, accounting.

### S04
**Proves** the janitor razes an unowned grid. **Setup** 1 BaR, GrindOnly, janitor NoOwnership; derelict 30-block
grid (no owner, built by nobody) 40 m away. **Pass** grid gone; grind perf numbers recorded.

### S05
**Proves** claims prevent double work. **Setup** 2 BaRs, 30 damaged blocks, claims on. **Pass** no sample where a
BaR welds a block claimed by the other or both weld the same block; all welded.

### S06
**Proves** `MaxSystemsPerTargetGrid` (=2) with 4 BaRs around one 40-block grid. **Pass** grid counter never above 2,
limit actually reached (peak 2), grid finished (no deadlock). Armor-only target built by you — also exercises
BUG-261122.7 (janitor must not grind it).

### S07
**Proves** clustering: 5 identical BaRs → one cluster with a coordinator; scans ≤ run time / interval + 3; all 50
blocks welded; `AsyncClusterScan` max ≤ 20 ms.

### S08
**Proves / measures** the large-grid envelope (#143). **Setup** 5 BaRs, BoundingBox 200 m, 1000-block station
(32 × 32), 180 s window. **Pass** progress, BaRs saw the grid, `UpdateBeforeSimulation10_100` avg ≤ 1.5 ms,
`ServerDoWeld` avg ≤ 3 / max ≤ 40 ms, aggregate max ≤ 5 ms, tick avg ≤ 0.5 / peak ≤ 15 ms, sim ≥ 0.8, weld budget
within cap. **Records** `weldedPerSecond` (~16/s). Run `x5` for stable numbers.

### S09
**Proves** the PB API contract on a real BaR. **Setup** 1 BaR, 3 damaged armor, empty cargo, an assembler
("S09 Assembler"). **Pass** every `BuildAndRepair.*` property the companion script uses is registered
(`CompanionScriptContract.Properties`, kept equal to Script.cs by `CompanionScriptContractTests`);
MissingComponents lists SteelPlate; PossibleTargets non-empty; Mode / WorkMode read Grids / WeldBeforeGrind;
ScriptControlled round-trips; `ProductionBlock.EnsureQueued(assembler, SteelPlate, 10)` leaves >= 10 queued.
**Records** `missingProperties`, `missingSteel`, `possibleTargets`, `ensureResult`, `queuedAfterEnsure`.

### S10
**Proves** collecting is lossless (BUG-261023.5): 5 × 100 SteelPlate floating stacks collected; SteelPlate in rig +
BaR + transport = 500; `ServerTryCollectingFloatingTargets` max ≤ 10 ms; sim ≥ 0.75 (physics of the stacks).

### S11
**Proves** default grind order (farthest first). **Setup** 1 BaR GrindOnly janitor NoOwnership; derelict grids at
20 / 45 / 70 m with 4 / 1 / 4 blocks. **Pass** the 70 m grid is touched first. **Records** `firstGround`.

### S12
As S11 with `GrindNearFirst` → the 20 m grid first.

### S13
As S11 with `GrindSmallestGridFirst` → the 1-block grid (45 m) first.

### S14
**Proves** weld priority order. **Setup** 4 damaged armor + 4 damaged conveyors on the rig, all components stocked;
armor moved to the top of the weld list (PB setter path). **Pass** all armor finished before the first conveyor.
**Records** `lastArmorDoneS`, `firstConveyorDoneS`.

### S15
As S14 with the conveyor class disabled. **Pass** armor welded, conveyors gain no integrity.

### S16
**Proves** grind priority disable. **Setup** derelict grid of 4 armor + 4 conveyors, janitor NoOwnership, conveyor
class disabled in the grind list. **Pass** armor ground away, all conveyors intact.

### S17
**Proves** ownership decisions with the default janitor (Enemies + NoOwnership), WeldBeforeGrind, four grids around
the BaR: owned by you (battery), armor-only built by you (no owner — BUG-261122.7), derelict (built by nobody),
pirate-owned (SPRT; skipped if the world has no pirates). **Pass** own + own-built welded and intact; derelict and
pirate grids gone.

### S18
**Proves** mechanical subgrids are part of the construct. **Setup** rotor stator on the rig; the rotor head is added by
the game's "AddRotorTopPart" action; 3 damaged armor blocks (built by you) are added on the head's subgrid; Grids
mode, janitor NoOwnership on. **Pass** the 3 subgrid blocks welded and not ground. S34–S37 reuse this with other blocks.

### S19
**Proves** power gating. **Setup** rig battery off, 5 damaged blocks; at 10 s the battery is switched on. **Pass**
the BaR showed unpowered, nothing welded while off, everything repaired after power returns.

### S20
**Measures** insufficient power: `Welder.MaximumRequiredElectricPowerWelding` = 100 MW (battery gives 12 MW), WorkSpeed 1,
30 s. **Pass** the BaR reported unpowered at least once. **Known:** it still welds at near full speed (flickers
standby-powered / unpowered) — BUG-261122.9; tighten the check when that is decided.

### S21
**Proves** WeldFunctional: 5 blocks at 20 % stop at functional integrity (critical + 1), none at full, 5 s after reaching it.

### S22
**Proves** WeldSkeleton: damaged blocks gain no integrity in 15 s.

### S23
**Proves** ignore colour: 3 unpainted damaged blocks welded, 3 painted damaged blocks (ignore colour) untouched.

### S24
**Proves** grind colour on the own grid: 3 unpainted damaged blocks welded, 3 painted intact blocks ground away.

### S25
**Proves** mod `Range` = 20 m: BaR area clamped to 40 m; owned damaged grid at 12 m repaired, at 60 m untouched.
**Records** `areaSizeX`.

### S26
**Proves** WeldOnly never grinds: own blocks welded, a derelict grid 25 m away (janitor NoOwnership configured) intact.

### S27
S03 with AllowBuild off: no block built in 20 s.

### S28
**Measures** grid state × ShowArea (BUG-261122.8 report). **Setup** 5 BaRs, BoundingBox, 20 damaged blocks on their own
grid, 60 s window. S28 station / ShowArea off. Profiled: `ShowAreaDraw`, `UpdateBeforeSimulation10_100`, scans,
cluster rebuilds, entity queries. **Pass** all 20 welded, `ShowAreaDraw` called only with ShowArea on, sim ≥ 0.5;
moving rigs moved > 100 m. Compare S28–S33 side by side (first run: no difference; draw 0.004 ms/BaR/frame).

### S29
S28 with a station and ShowArea on.

### S30
S28 with a ship (dynamic grid) standing still, ShowArea off.

### S31
S28 with a ship standing still, ShowArea on.

### S32
S28 with a ship drifting at 20 m/s with a slow yaw, ShowArea off.

### S33
S28 with a moving ship, ShowArea on.

### S34
S18 with an advanced rotor (`LargeAdvancedStator`, action "AddRotorTopPart").

### S35
S18 with a hinge (`LargeHinge`, action "AddHingeTopPart").

### S36
S18 with a piston (`LargePistonBase`, action "Add Top Part"; the piston stays retracted).

### S37
S18 with a wheel suspension (`Suspension1x1`, action "Add Top Part"); armor goes along the wheel's axle.

### S38
**Proves** docked ships are reached through connectors. **Setup** rig connector at (1,0,-1) facing out; a small
dynamic ship turned around with its connector touching it, locked through the game's `Connect()`; 3 damaged armor
behind the ship's connector; Grids mode. **Pass** the 3 ship blocks welded.

### S39
**Proves** a safe zone without Welding around the BaR blocks welding (40 m sphere). **Pass** nothing welded, BaR
state `SafeZoneAllowsWelding=false`.

### S40
Control for S39: zone allows everything → all welded.

### S41
**Proves** a zone without Grinding protects a derelict: 15 m zone around a derelict 60 m away, BaR outside the zone,
GrindOnly janitor NoOwnership. **Pass** derelict intact after 20 s.

### S42
S03 inside a 60 m zone without BuildingProjections: nothing built (AllowBuild on).

### S43
**Measures** a very large ship: 4000 damaged blocks (64 × 63) on a dynamic grid, 5 BaRs on board, Grids, 120 s.
**Pass** progress, `UpdateBeforeSimulation10_100` avg ≤ 1.5 ms, `ServerDoWeld` max ≤ 40 ms, aggregate max ≤ 5 ms,
tick avg ≤ 0.5 / peak ≤ 15 ms, sim ≥ 0.8. **Records** `weldedPerSecond` (first run 14.2/s).

### S44
**Proves** welding between ships in flight: BaR ship and a friendly ship (battery owned by you, 4 damaged blocks) both
at 15 m/s, 30 m apart, BoundingBox. **Pass** friendly blocks welded. **Records** `gapStart`/`gapEnd`.

### S45
As S44 with a pirate-owned ship (SPRT), janitor Enemies: ground in flight. Documents current behaviour — there is no
"enemy grinding in motion" switch (FEAT-261122.2 proposes one). Skipped without pirates.

### S46
S03 with admin `Welder.AllowBuildFixed` + `AllowBuildDefault=false`; the BaR tries AllowBuild on. **Pass** AllowBuild
forced off on the block, nothing built.

### S47
**Proves** admin `AllowedGrindJanitorRelations = Enemies`: a BaR set to janitor NoOwnership (GrindOnly) gets it
cleared; derelict 25 m away intact. **Records** `janitor`, `workMode`, `searchMode`.

### S48
Admin `AllowedWorkModes = WeldOnly` (+ default WeldOnly): a BaR set to GrindOnly is forced to WeldOnly; own blocks
welded, derelict intact.

### S49
Admin `AllowedSearchModes = Grids` (+ default Grids): a BaR set to BoundingBox is forced to Grids; own blocks
welded, a separate owned grid 20 m away untouched.

### S50
`DisableLimitSystemsPerTargetGrid` with limit 2, 4 BaRs around one 40-block grid. **Pass** more than 2 BaRs weld the
grid at the same time (sampled from their current blocks), grid finished.

### S51
`AssignToSystemEnabled=false`, 2 BaRs, 30 blocks. **Pass** finished, no claim made (`assignmentsPeak` 0).

### S52
`MaxWeldsPerTick=1`, 5 BaRs, 20 blocks. **Pass** finished, `weldBudgetPeak` ≤ 1.

### S53
`SafeZoneCheckEnabled=false` under a no-welding zone: the BaR ignores the zone (state says allowed) and welds.

### S54
**Proves** lowering the per-grid limit at runtime (BUG-261006.1). **Setup** 4 BaRs around an 80-block grid, limit 4,
WorkSpeed 1; at 15 s the limit drops to 2. **Pass** more than 2 on the grid before, never more than 2 from 5 s after,
welding continues. **Records** `peakBefore`, `peakAfter`, `violationsAfter`.

### S55
**Proves** claims are made and released: 2 BaRs, 10 blocks, `WeldingMultiplier` 0.1 (many weld calls per block,
so claims are visible). **Pass** at least one target claimed while welding, none still claimed within
`AssignmentTtlSeconds` + 5 s after all are done. **Records** `claimsPeak`, `claimsAtDone`, `releaseSeconds`
(261122.13: peak 2, all released within one sample of completion).

### S56
**Proves** a stopped BaR's claim expires and another BaR takes over: 2 BaRs, one damaged block X, WorkSpeed 1 and
`WeldingMultiplier` 0.1 (X takes ~20 s); B off; as soon as A has welded part of X (claimed by A), A is switched off
and B on. **Pass** A's claim on X released within TTL + 3 s, B finishes X. **Records** `releaseSeconds`,
`takeoverSeconds`, `finishSeconds` (261122.13: 8.0 s = TTL, 9.6 s, 17.6 s).

### S57
**Proves** damage detection: 5 blocks repaired, then damaged again (60 % of max integrity, bullet damage) after 3 s
and again after 30 s idle. The game applies the damage on the grid's next update; the lowest integrity after the
hit is the baseline, "detected" = first rise above it. **Pass** each time noticed within `TargetsUpdateInterval`
+ 3 s (8 s) and repaired; no block destroyed. **Records** `cycleNDetectS`, `cycleNRepairS`, `detectLimitS`.
Before BUG-261122.10 (BuildId 261122.13): detected 18.4 s / 11.8 s after the hit (empty-grid skip + idle backoff).

### S58
**Proves** a raised DefenseShields shield protects an enemy grid from the janitor. **Setup** 1-BaR rig (GrindOnly,
janitor Enemies, BoundingBox 200 m); pirate (SPRT) ship 60 m away: single row of 10 intact tracked armor, DS
controller (`DSControlLarge`) and ship emitter (`EmitterL`) at the row ends, two batteries. The run starts once the
shield is online (ready timeout 120 s). **Pass** nothing ground in 20 s. **Records** `shieldOnlineS`,
`apiIsBlockProtected`, `apiProtectedByShield`, `apiGridShieldOnline`, `shieldStatus`. **Skipped** without DS / SPRT.

### S59
S58, then the controller is switched off (functional, shield down) before the run. **Pass** the ship is ground.
Failed on 261122.20 (DS `IsBlockProtected` ignores the shield state) — BUG-261122.12, fixed in 261122.21, green since.

### S60
S58 with `ShieldCheckEnabled=false`: **Pass** ground through the raised shield.

### S61
S58 with `ShieldCheckEnabled=false`, WorkSpeed 1 (slow cycle; not GrindingMultiplier, which also slows the transport),
a 20-block ship, and the BaR grinding armor only (other grind classes disabled — grinding the shield's own controller /
batteries took the shield down first); as soon as grinding starts the check is
switched on. **Pass** no further integrity loss from 3 s after the switch (BUG-260612.13 grind-time recheck).
**Records** `switchedAtS`, `integrityLostAfterGrace`.

### S62
**Proves** the own-grid exemption: BaR ship (dynamic rig) with its own DS controller + emitter + battery, 6 damaged
armor on board, Grids mode. **Pass** all welded inside the shield. **Records** `barIsShielded` (State.IsShielded).

### S63
Friendly (caller-owned) shielded ship with 6 damaged armor, BaR rig outside (BoundingBox). **Pass** all welded —
welding has no shield check.

### S64
**Measures / proves** load: 2 rigs x 10 BaRs (large clusters) on own damaged ships of 25 / 150 / 600 blocks (dynamic,
in a row along X), WeldBeforeGrind, BoundingBox 200 m, 1000 SteelPlate per container, 75 s or until done.
**Pass** every ship got welding, >= 75 % of BaRs worked, tick avg <= 2 ms / peak <= 50 ms, sim >= 0.5.
**Records** `bars`, `rigs`, `clusters`, `barsWorked(Pct)`, `activePeak`, `weldedS/M/L`, `weldedPerSecond`, and
weld / grind / scan / cluster method stats.

### S65
S64 with 10 rigs x 2 BaRs (small clusters).

### S66
S64 grinding: derelict ships (no owner, built by nobody, intact) of 25 / 150 / 600 blocks, GrindOnly, janitor
NoOwnership. **Records** `groundS/M/L`, `groundPerSecond`.

### S67
S66 with 10 rigs x 2 BaRs.

### S68
S64 + S66 together: own damaged ships at y 0 and derelicts 20 m above, WeldBeforeGrind + janitor NoOwnership.

### S69
S68 with 10 rigs x 2 BaRs.

### S70
Stress: S68 with 4 rigs x 10 BaRs (40 BaRs, two levels on both lines).

### S71
**Proves** the companion script end to end with its shipped config, in a real programmable block (script text from
`Data/CompanionScript.txt`, copied into the -Testing variant by build_post.cmd). **Setup** BaR in group
BuildAndRepairGroup1, 5 damaged armor, empty cargo; assembler in AssemblerGroup1; LCD "BuildAndRepairGroup1StatusPanel";
LCD "Hangar LCD [BaR:1]" with Custom Data `Kinds=Status`; cockpit seat "Cockpit" (default display Cockpit[0]); the PB
is switched on once the Custom Data is set. **Pass** missing SteelPlate queued in the assembler (<= 40 s); status LCD,
tagged LCD ("Enabled (1 assembler)") and Cockpit[0] written; after `info-only` nothing is queued for 8 s and the LCD
shows "Disabled (info-only)"; still nothing after `Recompile()` (mode kept in Storage); `queue` resumes within 15 s.
**Skipped** when in-game scripts are disabled. **Records** `queuedAtS`, `queuedSteel`, `storageAfterInfoOnly`,
`queuedAfterRecompile`, `requeueS`. Its tick peak (~300 ms, sim 0.72) is the game compiling the script on `Recompile()`, called from the harness tick — not mod cost.

### S72
**Proves** BUG-261122.11: a block added to a fully repaired grid is noticed. **Setup** 1 BaR, 3 damaged armor, stock;
once repaired, an armor frame (5 %) is added with `grid.AddBlock` (like a hand-placed block) 3 s after the repair,
and another after a 30 s idle. **Pass** welding starts within `TargetsUpdateInterval` + 3 s (8 s) and each frame is
welded to full. **Records** `frameNDetectS`, `frameNWeldedS`.

### S73
**Proves** an indestructible grid (`DestructibleBlocks = false`, `RigSpec.Indestructible`) is not ground: derelict
10-block row 25 m away, GrindOnly, janitor NoOwnership, 20 s. **Pass** nothing ground and ServerDoGrind never called.

### S74
**Proves** BUG-261122.15: a protection plugin's damage handler is respected. The harness registers a BeforeDamage
handler (once per session, inert unless a grid is listed) that zeroes grind damage on the target. **Pass** nothing ground
and at most 3 grind attempts in 20 s (refused grid skipped 30 s). **Records** `grindCalls`, `handlerRefusals`.

## Building blocks for new scenarios

- `RigSpec`: `Bars`, `DamagedArmor` (+ `DamagedBuildPercent`, 1 = intact but tracked), `Armor`, `RowWidth`, `Offset`,
  `OwnerId` (−1 caller, 0 nobody, or an identity), `Projector`, `BatteryEnabled`, `Dynamic` + `LinearVelocity` /
  `AngularVelocity`, `Forward` (grid orientation), `Extra` blocks.
- `RigBlock` (extra blocks, tagged): kinds Armor, Conveyor, Battery, Stator, AdvancedStator, Hinge, Piston, Suspension,
  Connector; per block `BuildPercent`, `OwnerId`, `BuiltBy`, `Color`, `Tag` → `rig.Tag("name")`.
- `TestContext`: `SpawnRig`, `SpawnSafeZone(center, radius, allowedActions)`, `StockCargo` / `StockAllCargo`,
  `ConfigureBars` (runs `SettingsChanged` → `CheckLimits`, like a client message), `SetModSettings` (restored after),
  `SetRigBarsEnabled`, `TrackSpawned` (cleanup of runtime entities), `CountFullIntegrity`, `CountExisting`,
  `IntegritySum`, `CountComponentOnRig`, `RunSeconds`, `RunSecondsSinceSetup`, `Metric`, `Check*`.
- `ScenarioUtil`: `StockEverything`, `ComponentsPerBlock`, `AllGone`, `AddRow`, `Paint`.
- More extras: `Assembler` (1x1x2), `ProgrammableBlock` (`Program` text, `Enabled`), `Lcd`, `CockpitSeat`; any extra
  can carry a terminal `Name` and `Groups`; `RigSpec.BarGroup` puts the rig BaRs in a terminal group.
  `TestHarnessComponent.ReadModFile(path)` reads data shipped in the -Testing variant.
- `SkipReason(ctx)`: return why the scenario cannot run here (missing mod, faction) — reported as skipped.
  `ReadyTimeoutSeconds` (default 60): raise it when setup must wait (e.g. a shield charging).
- DefenseShields blocks: `RigBlockKind.ShieldController` (`DSControlLarge`) / `ShieldEmitter` (`EmitterL`, ships
  only — a static grid needs the station emitter); query DS through `Mod.Shield` (GridShieldOnline, IsShieldUp, ...).
- Prefer parameterised classes for variants (S11 modes, S03 projection modes, S39 zone modes, S47/S50 configs).

## Adding a scenario

1. Pick the next free id; implement it under `Testing/Scenarios/` (C# 6, ModAPI whitelist — no reflection/threads).
2. Register it in `ScenarioRegistry.All` (the order is the `test list` / range order).
3. Add `ingame:Sxx` to the `## Coverage` of the `SCN-` entry it checks (new entry if none fits) and the
   `scenarios/state.md` row.
4. Add it to this page: summary row + `### Sxx` section (`CatalogueConsistencyTests` fails otherwise).
5. Build (bump `Constants.BuildId`), `dotnet test`, then run it in game and fill in "Last green".
