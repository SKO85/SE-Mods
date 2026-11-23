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
- `/nanobars test run <id|id,id|id-id|@group|all> [xN]` — e.g. `S03`, `S03,S20`, `S28-S33`, `@zone`,
  `@zone,@shields,S03 x2`. Ranges follow the `test list` order. A hidden warm-up (`S00`) runs first in every session;
  each scenario posts a start line in chat. `/nanobars test groups` lists the groups (see [Groups](#groups)).
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

## Groups

`/nanobars test run @name` runs a group; groups combine with ids and ranges (`@zone,@shields,S03`). A scenario can be in
several groups; S00 (warm-up) is in none and runs first anyway. Defined in `ScenarioRegistry.Groups`;
`ScenarioGroupsTests` fails when a scenario has no group, a group lists an unknown id, or a group is missing here.
Pick the groups of the area you changed, e.g. a zone fix: `@zone,@projection`; an inventory fix: `@inventory,@weld`.

| Group | What | Scenarios |
| ----- | ---- | --------- |
| `@core` | Repair, damage detection, range, work modes, power, speeds | S01, S19, S20, S25, S57, S72, S75-S80 |
| `@weld` | Welding: repair, weld options, colours, docked ships, sources | S01-S03, S18, S21-S23, S26, S27, S34-S38, S42, S44, S46, S72, S84, S86-S88, S111, S112, S114, S115, S124, S125 |
| `@projection` | Projector building, incl. admin and safe-zone rules | S03, S27, S42, S46, S85 |
| `@subgrids` | Rotors, pistons, hinges, wheels, connectors, ships in flight | S18, S34-S38, S44, S45, S111, S112 |
| `@grind` | Grinding: janitor, order, colours, protection, leaving targets, full storage | S04, S11-S13, S16, S17, S24, S45, S73, S74, S76, S89, S112 |
| `@zone` | Safe zones | S39-S42, S53, S85 |
| `@shields` | DefenseShields (skipped without the mod) | S58-S63 |
| `@inventory` | Component sources, sorter, empty cargo, push targets, push options, logistics at scale | S02, S84, S87-S93, S103-S110, S113 |
| `@claims` | Block claims, per-grid limit, BaRs sharing work, takeovers | S05, S06, S50, S51, S54-S56, S86, S94-S98 |
| `@cluster` | Cluster coordination and the per-grid limit | S06, S07, S50, S54 |
| `@perf` | Load, large grids, scan cost by grid size, logistics at scale, ShowArea and budget cost (long) | S08, S28-S33, S43, S52, S64-S70, S99-S112, S119-S123 |
| `@companion` | PB API and the companion script | S09, S71, S81-S83 |
| `@config` | Admin settings (Fixed / Default, limits, checks) | S46-S53 |
| `@sync` | Network load on servers: settings / state sync rate and size (run on Torch with a client) | S43, S70, S106, S111, S116-S123 |
| `@priority` | Weld / grind priority lists | S14-S16 |
| `@collect` | Collecting floating objects | S10 |

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
| Area, range, search mode (incl. targets leaving the area) | S25, S49, S75, S76 |
| Work modes (order: S77, S78) | S26, S48, S77, S78 |
| Safe zones | S39, S40, S41, S42, S53 |
| DefenseShields (skipped without DS) | S58, S59, S60, S61, S62, S63 |
| Clusters, per-grid limit | S06, S07, S50, S54, S64–S70 |
| Block claims (assign / release / TTL) | S05, S51, S55, S56 |
| Admin restrictions and config switches | S25, S46–S53 |
| Collecting floating objects | S10 |
| Performance envelopes / measurements | S08, S28–S33, S43, S64–S70 |
| Speed settings and throughput floors | S79, S80; floors in S08, S43, S64–S67 |
| Many BaRs / load (weld, grind, mixed; large vs small clusters) | S64–S70 |
| PB scripting API, companion script (unit: CompanionScriptBehaviourTests, CompanionScriptContractTests) | S09, S71, S81–S83 |

## Summary

Time = measured run (plus ~5 s setup). Last green = BuildId of the last passing in-game run (2026-11-22, 01_211_018).

| ID | What it proves | Catalogue | Time | Last green |
|---|---|---|---|---|
| S00 | Warm-up (not a test): JIT the weld/grind/scan/projector-build paths once per session | — | 3 s | 261122.11 |
| S01 | One BaR repairs 10 damaged blocks from stocked cargo | CORE-001, WELD-001 | 3 s | 261122.8 |
| S02 | Empty cargo: missing components reported, nothing welded, no spikes | WELD-005 | 20 s | 261122.8 |
| S03 | Projector: 10 blocks built from an empty block inventory, charged once | WELD-002 | 3 s | 261122.36 — 10/10, 25 each |
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
| S39 | Safe zone without Welding over the BaR: nothing welded | ZONE-001 | 20 s | 261122.36 — nothing welded |
| S40 | Safe zone allowing everything: welded as usual | ZONE-001 | 2 s | 261122.11 |
| S41 | Safe zone without Grinding around a derelict: not ground | ZONE-001 | 20 s | 261122.11 |
| S42 | Safe zone without BuildingProjections: projection not built | ZONE-001, WELD-002 | 20 s | 261122.36 — nothing built |
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
| S64 | Load weld: 20 BaRs (2 x 10) on ships of 25/150/600 blocks | BUDGET-008 | 22 s | 261122.27 — 59.8 welds/s (BUG-261122.14 fixed) |
| S65 | Load weld: 20 BaRs (10 x 2) | BUDGET-008 | 14 s | 261122.27 — 62.1 welds/s |
| S66 | Load grind: 20 BaRs (2 x 10) on derelicts of 25/150/600 blocks | BUDGET-008 | 38 s | 261122.27 — 38.4 ground/s |
| S67 | Load grind: 20 BaRs (10 x 2) | BUDGET-008 | 21 s | 261122.27 — 36.7 ground/s |
| S68 | Load mixed: 20 BaRs (2 x 10), own + derelict ships | BUDGET-008 | 57 s | 261122.16 — 13.6 + 13.6 /s |
| S69 | Load mixed: 20 BaRs (10 x 2) | BUDGET-008 | 37 s | 261122.16 — 21.0 + 21.0 /s |
| S70 | Stress mixed: 40 BaRs (4 x 10) | BUDGET-008 | 36 s | 261122.16 — 21.8 + 21.8 /s, 40 BaRs |
| S71 | Companion script in a real PB: queuing, LCDs, info-only / queue, recompile | API-002 | 20 s | 261122.28 — tagged cockpit written |
| S72 | Block frame added to a repaired grid (after 3 s and 30 s idle) is welded | CORE-001 | 39 s | 261122.20 — welded 2.8 s / 0.2 s after placing |
| S73 | Protected grid: indestructible derelict is not ground (no grind call) | GRIND-006 | 20 s | 261122.25 — 0 grind calls |
| S74 | Protected grid: damage-handler (plugin style) protection respected, no retry storm | GRIND-006 | 20 s | 261122.25 — 1 refused attempt, nothing ground |
| S75 | Leaving target: own ship pushed out of the area is not welded from afar | CORE-002 | ~20 s | 261122.27 — no change after leaving |
| S76 | Leaving target: derelict ship pushed out of the area is not ground from afar | CORE-002 | ~20 s | 261122.27 — no change after leaving |
| S77 | Work mode WeldBeforeGrind: own blocks welded before the derelict is ground | CORE-004 | ~20 s | 261122.28 — welded 1.7 s, grinding from 1.9 s |
| S78 | Work mode GrindBeforeWeld: derelict ground before own blocks are welded | CORE-004 | ~20 s | 261122.28 — ground 3.1 s, welding from 3.2 s |
| S79 | Weld speed: WorkSpeed 10 vs 1 ~10x, WeldingMultiplier 0.2 vs 0.1 ~2x | BUDGET-009 | ~40 s | 261122.28 — x9.4 / x2.0 |
| S80 | Grind speed: WorkSpeed 10 vs 1 ~10x, GrindingMultiplier 0.2 vs 0.1 ~2x | BUDGET-009 | ~40 s | 261122.28 — x9.5 / x1.9 |
| S81 | Companion script, two groups: each queues into its own assembler, titles its own LCD | API-002 | ~20 s | 261122.29 — A 100, B 0 |
| S82 | Companion script, two assemblers: queue = missing, both used, no 0 items, stable 20 s | API-002 | ~35 s | 261122.32 — 100/100, stable |
| S83 | Companion script, stock in cargo: nothing queued while welding from stock | API-002 | 25 s | 261122.32 — nothing queued |
| S84 | Sorter-blocked stock stays in the missing list (companion script keeps queuing) | WELD-005 | 20 s | 261123.10 — blocked stock kept |
| S85 | Safe zone without Welding, with BuildingProjections: projections placed, not welded further | ZONE-001 | ~20 s | 261122.36 — 10 placed, 0 welded, 750 + 240 + 10 = 1000 |
| S86 | A starved BaR does not stop a stocked BaR on another ship from repairing its grid | WELD-008 | ~20 s | 261123.7 — 10/10 in 3.4 s (5/10 in 45 s before the fix) |
| S87 | Source: components only in a docked ship's cargo are used for welding | INV-001 | ~15 s | 261123.10 — 5/5 in 2.5 s, 100 plates from the docked ship |
| S88 | Source: components only in an assembler's output are used for welding | INV-001 | ~10 s | 261123.10 — 5/5, assembler at (0,0,3) |
| S89 | Push targets full: BaR pauses without losing ore, resumes once space is freed | INV-001, INV-002 | ~60 s | 261123.15 — full reported, 124155 kg iron kept, done 15 s after freeing |
| S90 | Push immediately off: nothing leaves the BaR's inventory | INV-002 | 45 s | 261123.19 — nothing pushed in 45 s |
| S91 | Push ingots/ore immediately: only ore leaves the BaR | INV-002 | ≤75 s | 261123.19 — ore pushed at 21.9 s |
| S92 | Push components immediately: only components leave the BaR | INV-002 | ≤75 s | 261123.19 — plates pushed at 34.3 s |
| S93 | Push items immediately: only other items (tools) leave the BaR | INV-002 | ≤75 s | 261123.19 — tool pushed at 34.2 s |
| S94 | Claims: 2 BaRs finish each block they start (never more half-welded blocks than BaRs) | BUDGET-001 | ~30 s | 261123.19 — peak 2 half-welded, done 7.2 s |
| S95 | Claims: 2 BaRs finish each block they grind (never more half-ground blocks than BaRs) | BUDGET-001 | ~30 s | 261123.19 — peak 2 half-ground, done 14.8 s |
| S96 | Claims: BaR destroyed mid-weld, claim freed at once, other BaR finishes | BUDGET-001 | ~30 s | 261123.19 — claim freed 0.5 s, finished 11.0 s |
| S97 | Claims: BaR loses power mid-weld, claim expires, BaR on another ship finishes | BUDGET-001 | ~40 s | 261123.19 — claim expired 8.2 s, B finished 19.2 s |
| S98 | Claims: BaR switched off mid-grind, claim expires, other BaR finishes grinding | BUDGET-001 | ~40 s | 261123.19 — claim expired 7.7 s, B finished 31.7 s |
| S99 | Scan cost: 1 BaR scanning a 1000-block grid (area mode), 45 s | BUDGET-006 | ~60 s | 261123.20 — scan 1.4 ms, GetBlocks 0.05 ms, tick 0.015 ms |
| S100 | Scan cost: 1 BaR scanning a 2000-block grid (area mode), 45 s | BUDGET-006 | ~60 s | 261123.20 — scan 2.5 ms, GetBlocks 0.08 ms |
| S101 | Scan cost: 1 BaR scanning a 5000-block grid (area mode), 45 s | BUDGET-006 | ~60 s | 261123.20 — scan 6.0 ms, GetBlocks 0.28 ms |
| S102 | Scan cost: 1 BaR scanning a 10000-block grid (area mode), 45 s | BUDGET-006 | ~60 s | 261123.20 — scan 11.5 ms, GetBlocks 0.47 ms, tick 0.08 ms |
| S103 | Logistics: 10 BaRs, ~50 containers, plates spread thin, ships around | INV-001, BUDGET-006 | 3-7 min | not yet |
| S104 | Logistics: 20 BaRs, ~100 containers, plates spread thin, ships around | INV-001, BUDGET-006 | 3-7 min | not yet |
| S105 | Logistics: 50 BaRs, ~250 containers, plates spread thin, ships around | INV-001, BUDGET-006 | 3-7 min | not yet |
| S106 | Logistics: 100 BaRs, ~500 containers, plates spread thin, ships around | INV-001, BUDGET-006 | 3-7 min | not yet |
| S107 | Logistics, busy base: 10 BaRs, moving items, docking supply ship, switching sorter | INV-001, BUDGET-006 | 3-7 min | not yet |
| S108 | Logistics, busy base: 20 BaRs, moving items, docking supply ship, switching sorter | INV-001, BUDGET-006 | 3-7 min | not yet |
| S109 | Logistics, busy base: 50 BaRs, moving items, docking supply ship, switching sorter | INV-001, BUDGET-006 | 3-7 min | not yet |
| S110 | Logistics, busy base: 100 BaRs, moving items, docking supply ship, switching sorter | INV-001, BUDGET-006 | 3-7 min | not yet |
| S111 | Moving arm: 1 BaR on piston + rotor welds a ring of grids via the conveyors, ShowArea toggling | WELD-007, BUDGET-006 | <=3 min | 261123.32 — 48/48 in 27.4 s, 960 plates via conveyors, sim 1.0 |
| S112 | Moving arm: 3 BaRs on piston + rotor weld and grind a ring of grids, ShowArea toggling | WELD-007, BUDGET-006 | <=3 min | 261123.32 — 24 welded + 24 ground in 14.4 s, sim 1.0 |
| S113 | Probe: ore / ingot / component fit and transport -> BaR move (MaxItemsAddable) | INV-002 | <1 min | 261123.40 — all fit / moved, incl. 99 % full BaR (422 kg ore, 52 plates) |
| S114 | Small-grid projection on a small ship built by a large BaR 12 m away | WELD-002 | <1 min | 261123.42 — 10/10 built + welded in 2.9 s, plates exact |
| S115 | Small-grid projection built by a small BaR on the same small ship | WELD-002 | <1 min | 261123.42 — 10/10 built + welded in 3.4 s, plates exact |
| S116 | Sync: 20 BaRs, settings toggled every 0.5 s (settings broadcast rate) | BUDGET-010 | <1 min | never |
| S117 | Sync: one BaR with a 512-entry target list (state message size / rate) | BUDGET-010 | <1 min | never |
| S118 | Sync: 50 idle BaRs, nothing to do (idle traffic) | BUDGET-010 | <1 min | never |
| S119 | Traffic / spikes: 10 BaRs welding a 100-block grid | BUDGET-010 | ~1 min | never |
| S120 | Traffic / spikes: 10 BaRs welding a 1,000-block grid | BUDGET-010 | ~1 min | never |
| S121 | Traffic / spikes: 10 BaRs welding a 3,000-block grid | BUDGET-010 | ~1 min | never |
| S122 | Traffic / spikes: 10 BaRs welding an 8,000-block grid | BUDGET-010 | ~1 min | never |
| S123 | Traffic / spikes: 10 BaRs welding 100 + 1,000 + 3,000 + 8,000-block grids at once | BUDGET-010 | ~1 min | never |
| S124 | Projection with weld option Functional: placed and welded to functional, not full | WELD-003, WELD-002 | <1 min | never |
| S125 | Projection with weld option Skeleton: placed, not welded further | WELD-003, WELD-002 | <1 min | never |

## Scenarios

Common rig ("BaR rig"): a static large grid with N BaRs side by side on the base layer, small cargo containers
around them (conveyor-connected), a charged battery; damaged armor on top (`DamagedArmor`, 20 % integrity unless
noted). Separate target grids are 40 m-ish away. `WorkSpeed=10` unless noted (fast, so runs are short).

### S00
Warm-up, not a test. 1-BaR rig with 5 damaged blocks, a projector building 2 projected armor blocks, plus a derelict
3-block grid; weld + projector build + janitor grind. Queued first in every session so the first real scenario does not
pay the one-time JIT cost after a world load (S01 once measured 38 ms there; the first projected build of a session
76 ms in S03, full run 2026-11-22). No checks.

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
within cap, `weldedPerSecond` >= 11 (speed floor). **Records** `weldedPerSecond` (~16/s). Run `x5` for stable numbers.

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
`SafeZoneCheckEnabled=false` under a no-welding zone: the BaR ignores the zone (state says allowed) and tries to weld
(`weldCalls > 0`). Whether the weld lands is the game's rule: single player with admin / creative rights welds all 5;
on a dedicated server the game refuses inside the zone (Torch 261123.42: 745 calls, 0 welded) — both pass, partial fails.
20 s window. **Records** `welded`, `weldCalls`, `gameRefusedInZone`, `dedicated`.

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
**Pass** every ship got welding, >= 75 % of BaRs worked, tick avg <= 2 ms / peak <= 50 ms, sim >= 0.5, and the speed
floor (blocks per second; S64 42, S65 43, S66 27, S67 26 — ~70 % of the 2026-11-22 baseline).
**Records** `bars`, `rigs`, `clusters`, `barsWorked(Pct)`, `activePeak`, `weldedS/M/L`, `weldedPerSecond`, weld / grind /
scan / cluster method stats, and (BUG-261122.14) `busyPct`, `transportPct`, `avgTargetDistM`, `avgListSize`,
`sharedBlockSamples`. `test run S64-S67 detail` keeps per-call profiler logs.

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

### S75
**Proves** BUG-261122.16: a target leaving the area is not worked from afar. BaR with a 40 m BoundingBox area, own
ship (10 damaged armor, dynamic) 12 m away, WorkSpeed 1; once welding has started the ship is pushed away at 20 m/s.
**Pass** from 1 s after its last block left the area, the ship's integrity does not change for 6 s.
**Records** `workBeforePush`, `outOfAreaS`, `integrityChangedAfterLeaving`.

### S76
S75 with a derelict ship and janitor grinding (GrindOnly, NoOwnership).

### S77
**Proves** WeldBeforeGrind order: 1 BaR (BoundingBox, janitor NoOwnership) with 6 damaged own blocks and a derelict
6-block grid 20 m away. **Pass** both finished; the derelict loses integrity while own blocks still need welding in at
most 1 sample (hand-over); grinding starts after welding finished (0.5 s slack). **Records** `firstWeldS`, `weldDoneS`,
`firstGrindS`, `grindDoneS`, `outOfOrderSamples`.

### S78
S77 with GrindBeforeWeld: no welding while the derelict still has blocks.

### S79
**Proves** the speed settings work. One BaR, 40 damaged own blocks, WeldOnly; three phases with settings switched live
(1 s settle each): A WorkSpeed 10 / WeldingMultiplier 0.1 (8 s), B WorkSpeed 1 / 0.1 (15 s), C WorkSpeed 10 / 0.2 (8 s).
**Pass** A/B between 6 and 14 (~10), C/A between 1.5 and 2.5 (~2), no phase runs out of work. **Records** `rateWs10M01`,
`rateWs1M01`, `rateWs10M02` (integrity per second), `ratioWorkSpeed`, `ratioMultiplier`.

### S80
S79 grinding a derelict 40-block grid 15 m away (GrindOnly, janitor NoOwnership, GrindingMultiplier).

### S81
**Proves** the companion script with several queuing groups. The harness swaps the script's group config (between
`BuildAndRepairSystemQueuingGroups = {` and `};`, kept in shape by `CompanionScriptContractTests`) for two groups: BaR A
+ "S81 Assemblers A" + "S81 Panel A", BaR B (GrindOnly, needs nothing) + "S81 Assemblers B" + "S81 Panel B"; one rig
with per-BaR terminal groups (`RigSpec.BarGroups`). **Pass** assembler A queues, assembler B gets nothing (no leak
between groups), Panel A titled "BaR Group 1", Panel B "BaR Group 2". Skipped like S71.

### S82
One group with both assemblers. **Pass** total queued SteelPlate = what the BaRs miss, spread over both assemblers, no
0-amount queue entries, unchanged after 20 s without production (Steam: "keeps queuing 0 item on repeat").

### S83
One group, 500 SteelPlate per container, 30 damaged blocks at WorkSpeed 1, 25 s. **Pass** the BaR welds from stock and
nothing is queued (Steam: components queued with thousands on the grid). **Records** `queuedMax`, `missingSteelMax`.

### S84
**Proves** BUG-261122.17: a cargo with 500 SteelPlate sits behind a sorter that blacklists SteelPlate (rig cargo
(0,0,2) -> sorter (0,0,3) -> cargo (0,0,4)); the rig's own cargo is empty. **Layout checks** the cargo is
conveyor-connected to the BaR and the sorter refuses SteelPlate (otherwise the scenario proves nothing). **Pass** the
stock stays put, SteelPlate is reported missing (the companion script keeps queuing), aggregate max <= 2 ms.

### S85
**Proves** BUG-261122.19: S03's projector rig inside a safe zone that forbids Welding but allows BuildingProjections.
**Pass** all 10 projected blocks placed, none welded further, SteelPlate conserved (rig + the placed blocks'
construction stockpiles, plus the mounted creation plates = stock: the build may stockpile the BaR's ready components).
Run with admin creative tools and "ignore safe zones" off (creative tools build instantly; the bypass welds). Counterparts: S39 (no welding, real blocks), S42 (no building projections).

### S86
**Proves** BUG-261123.1: ship A (BaR, 10 damaged blocks, empty cargo) and ship B 25 m away (BaR in Fly mode,
500 SteelPlate, no damage), not connected. **Pass** B repairs all of A's blocks within 45 s. **Records** `firstWeldS`,
`doneS`, ship A's BaR state (expected MissingComponents).

### S87
**Proves** components are pulled from a ship docked by connector (S38 docking; ship cargo at ship cell (0,0,1)) when
the rig's own cargo is empty. **Layout check** the ship cargo is conveyor-connected to the BaR. **Pass** all 5 damaged
rig blocks welded within 40 s and SteelPlate taken from the ship cargo.

### S88
**Proves** an assembler's output inventory is a source: 500 SteelPlate only in a switched-off assembler's output.
The large assembler has conveyor ports on some faces only, so three candidates are placed (rig cells (2,0,0), (0,0,3),
(1,0,3)); the first connected to the BaR is stocked (`sourceCandidate`). **Layout check** (none connected = fail) and
**Pass** as S87.

### S89
**Proves** the push-targets-full path. Rig: BaR (GrindOnly, janitor NoOwnership, PushIngotOreImmediately), every rig
container filled with stone (volume-based fill, 3 passes). Three single-container derelicts 25 m away (no armor next to
them, so nothing splits off), each full of iron ore. Records `worldMaxFloatingObjects`, `barMaxVolume`, iron per
location at the end and at the first loss. **Pass** the BaR reports a full
inventory while nothing can be pushed (first 30 s), resumes after two rig containers are emptied at 30 s and grinds the
container within 120 s; iron ore (derelict + rig + BaR + transport) is never below the starting amount.

### S90
**Proves** the three "push immediately" options, one variant each (S90 none, S91 ingots/ore, S92 components, S93 other
items). The idle BaR holds 200 kg iron ore, 50 SteelPlate and 1 hand welder (`WelderItem`); the rig cargo is empty.
**Pass** only the class whose option is on leaves the BaR (into the rig cargo; up to 75 s, `pushedAtS`; S90 watches
45 s), the others stay, and every class is conserved (BaR + cargo = start). An idle BaR refreshes its push targets with
its slower idle scan and backs off 15 s after a push that found no target, so a fresh idle BaR can take ~45 s to push.
Diagnostics: `barFlags`, engine `*CanTransfer` / `*CanAdd` for BaR -> rig container.

### S91
Variant of S90: `PushIngotOreImmediately` only.

### S92
Variant of S90: `PushComponentImmediately` only.

### S93
Variant of S90: `PushItemsImmediately` only.

### S94
**Proves** BaRs finish what they start: 2 BaRs (BoundingBox, WeldOnly), 10 damaged blocks, WorkSpeed 10,
WeldingMultiplier 0.1. Sampled every 0.5 s: blocks welded a bit but not finished (`inProgressPeak`). **Pass** never
more than 2 (one per BaR) and all finished within 120 s.

### S95
Grinding counterpart of S94: 2 BaRs grind a 10-block derelict (janitor NoOwnership, GrindingMultiplier 0.1).
**Pass** never more than 2 blocks partly ground at once, all gone within 120 s.

### S96
**Proves** a destroyed BaR frees its claims at once (`Close` -> `ReleaseAllForSystem`): A welds X alone (B off,
WorkSpeed 1, WeldingMultiplier 0.1); as soon as X gained integrity, A's block is razed and B switched on.
**Pass** A held the claim, the claim is gone within 3 s, B finishes X. **Records** `releaseSeconds`, `takeoverSeconds`,
`finishSeconds`.

### S97
**Proves** an unpowered BaR's claim expires: A on its own rig welds X; B on a second rig 25 m away (own battery,
stocked). Mid-weld A's battery is switched off and B on. **Pass** A's claim released within TTL + 3 s, B finishes X.

### S98
Grinding counterpart of S56: A grinds a derelict block X alone; mid-grind A is switched off and B on. **Pass** A's
claim released within TTL + 3 s, B finishes grinding X.

### S99
**Records** scan cost by grid size: one BaR (BoundingBox, area 200 m, WeldOnly) next to a static grid of 1,000 blocks
(20 damaged, welded slowly so the BaR keeps scanning; the rest intact armor, rows of 100). 45 s. Per-method cost of
the game lookups (`SharedEntityCache.GetEntitiesInBox` = grids in the area, `SharedGridBlockCache.GetBlocks*` = all
blocks of a grid) and the mod's scan steps (`AsyncClusterScan`, `AsyncAddBlocksOfBox`, `PreSortClusterCandidates`,
`ApplyClusterResultToSelf`). **Pass** main-thread tick avg <= 1 ms and sim speed >= 0.8 (the scan runs on a
background thread; its cost is recorded, compare across S99-S102).

### S100
S99 with a 2,000-block grid.

### S101
S99 with a 5,000-block grid.

### S102
S99 with a 10,000-block grid.

### S103
**Proves / records** component logistics at scale. Station: N = 10 BaRs in a row (rig cargo ~3 per BaR) plus 2 extra
containers per BaR behind the back row; SteelPlate spread evenly over every container (the ships' missing plates x 1.2)
and 10 plates in each BaR's own inventory (BaRs are sources for each other). Static ships with 20 damaged armor
blocks each (one per 5 BaRs, at least 2) 25 m in front; BaRs BoundingBox, area 100 x 60 x 100 m, WeldOnly, WorkSpeed 3.
**Pass** all ship blocks welded within max(180 s, 120 s + 3 s per BaR) (10 BaRs 180 s, 50 BaRs 270 s, 100 BaRs 420 s;
throughput is recorded, not gated — BUG-261123.6); plates conserved (exactly the missing plates left storage); sim speed
>= 0.8 (10 / 20 BaRs) or >= 0.5 (50 / 100). **Records** `weldedPerSecond`, `platesUsed` vs `platesNeeded`, and the
cost of `ServerFindMissingComponents`, `PullFromSourcesOnePass`, `ServerEmptyTransportInventory`,
`ServerTryPushInventory`, `AsyncScanForSources`.

### S104
S103 with 20 BaRs.

### S105
S103 with 50 BaRs.

### S106
S103 with 100 BaRs.

### S107
S103 on a busy base (10 BaRs): plate stacks of 5 moved between containers every 2 s (max(2, N/5) moves, like
assemblers / refineries), a supply ship with a container docked by connector (S38 docking) that undocks every 8 s
(first at 4 s) and redocks ~4 s later, and a container section behind a sorter that is switched off / on and has its
SteelPlate filter flipped every 6 s. All of it is conservation-neutral. **Pass** as S103, plus the churn really happened (`itemMoves`,
`undocks`, `redocks`, `sorterChanges`). Compare `weldedPerSecond` and the pull / push cost with S103.

### S108
S107 with 20 BaRs.

### S109
S107 with 50 BaRs.

### S110
S107 with 100 BaRs.

### S111
**Proves** BaRs on a moving arm, supplied through the conveyors. Static base: conveyor junction with a piston on top,
4 containers beside it (500 SteelPlate each), battery. Built at run time (directions from the geometry, not the heads'
own Up; the partly built heads "Add Top Part" makes in survival are completed first): advanced rotor (a regular rotor
does not pass conveyors) on the piston head, a spacer junction and then the conveyor-junction hub on the rotor head
(keeps the turning BaRs clear of the rotor), BaR(s) with their front face (the
model's main conveyor port, dummy at z = -2.36) against the hub, pointing outward. Components travel
containers -> junction -> piston -> advanced rotor -> spacer -> hub -> BaR. The piston reverses every 6 s (0.5 m/s), the
rotor turns at 1 RPM for 5 s, then 2 RPM, ShowArea toggles every 4 s. Ring of 8 small static grids (6 damaged armor each) 15 m out, 8 m up;
WorkSpeed 3 so the BaRs work ~20-30 s on the moving arm; BaR area 60 m (whole ring from any arm angle).
**Layout checks** heads attached, BaRs working and conveyor-connected to the base containers. **Pass** all ring blocks
welded within 180 s, plates left the base containers, sim >= 0.8, tick avg <= 1 ms. **Records** per-step cost incl.
`ShowAreaDraw`, `pistonReversals`, `showAreaToggles`, `basePlatesUsed`.

### S112
S111 with 3 BaRs on the hub and a mixed ring: every second grid is an unowned derelict (6 intact armor, janitor
NoOwnership) to grind. **Pass** additionally: all derelict blocks gone.

### S113
**Probes** BUG-261123.5: does `MaxItemsAddable` (the `CanItemsBeAdded` search the mod uses for transport -> BaR, push
and pull) refuse ore or ingots? Per item class (iron ore, iron ingot, steel plate), on an empty cargo and the empty BaR:
`CanItemsBeAdded` with the type read from a real inventory item and with `MyItemType.Make*`, `MaxItemsAddable`, then
moves 100 units from the BaR's transport into the BaR exactly like the mod. Then the S89 case: BaR filled with stone
to ~99 %, the transport offering 3x what fits: the fit must match free volume / item volume and move fully. One tick. **Pass** each class
fits fully and moves fully. **Records** every probe value (`ore.bar.maxAddableReal`, `ore.transport.moved`, ...).

### S114
**Proves** small-grid projections are built (Steam report "will not build small grid projections"). A static small-grid
ship (`RigSpec.SmallGrid`: small battery, armor, small projector) projects 10 small armor blocks next to the projector;
a large BaR on the rig 12 m away (BoundingBox, AllowBuild, 50 SteelPlate in the rig cargo) builds them. **Pass** all 10
built on the expected cells and welded, plates charged exactly once. **Records** projector total / remaining /
buildable, the BaR's weld targets, skipped projected blocks and the limits flag.

### S115
**Proves** the same with a small BaR (3x3x1) on the small ship itself (Grids mode, 50 SteelPlate in its own inventory).

### S116
**Proves** settings changes made by scripts / timers do not flood clients. 20 BaRs (Grids, WeldOnly, WorkSpeed 1,
damaged armor on the rig) get ShowArea toggled every 0.5 s for 20 s. **Pass** tick avg <= 1 ms; with a remote player
connected (Torch): settings broadcasts <= 1 per BaR per second (+2 s slack) and > 0. **Records** `toggles`,
`settingsPerBarPerS`, `statePerBarPerS` and the standard `net.*` metrics. Single player: traffic checks skipped.

### S117
**Proves** a BaR with a full target list does not send oversized or too frequent state messages. One BaR
(BoundingBox, 60 m area) above a 1,500-block damaged armor field (target list capped at 512). **Pass** tick avg <= 1 ms;
with a remote player: largest state message <= 64 KB, state sends <= 1 per BaR per second. **Records** `stateMaxKB`,
`barWeldTargets`, `net.*`.

### S118
**Proves** an idle base costs (almost) no traffic. 50 BaRs, nothing to weld or grind; counting starts 10 s after they
switch on, then 30 s. **Pass** tick avg <= 1 ms; with a remote player: <= 0.1 state sends per BaR per second and no
settings broadcasts.

### S119
**Measures** CPU and network spikes by target grid size. 10 BaRs (BoundingBox, 100 m area, WeldOnly, WorkSpeed 3,
3,000 SteelPlate) above a damaged armor field of 100 blocks (rows of 40, 6 m below), 40 s. **Pass** (loose, data first)
sim >= 0.5, tick avg <= 2 ms, something welded; with a remote player the largest state message <= 64 KB. **Records**
`targetBlocks`, `welded`, `weldedPerSecond`, `weldTargetsListedTotal / Max`, tick / sim, `net.*` incl. `net.peakKBps`,
`net.peakMsgsPerS`, `net.peakAtS`.

### S120
S119 with a 1,000-block field.

### S121
S119 with a 3,000-block field.

### S122
S119 with an 8,000-block field.

### S123
S119 with all four fields at once (stacked 6 m apart: 100, 1,000, 3,000, 8,000 blocks).

### S124
**Proves** weld option Functional works on projections (S21 covers damaged blocks only). S03's rig and 10-block
projection with WeldFunctional. **Pass** all 10 placed, each at functional integrity, none at full 5 s after the last
reached functional, fewer plates used than full welding would. **Records** `atFunctional`, `steelOnRig`, `steelIfFull`.

### S125
**Proves** weld option Skeleton on projections: all 10 placed, none welded further, SteelPlate conserved (rig +
construction stockpiles).

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
2. Register it in `ScenarioRegistry.All` (the order is the `test list` / range order) and add it to at least one
   group in `ScenarioRegistry.Groups` and the [Groups](#groups) table.
3. Add `ingame:Sxx` to the `## Coverage` of the `SCN-` entry it checks (new entry if none fits) and the
   `scenarios/state.md` row.
4. Add it to this page: summary row + `### Sxx` section (`CatalogueConsistencyTests` fails otherwise).
5. Build (bump `Constants.BuildId`), `dotnet test`, then run it in game and fill in "Last green".
