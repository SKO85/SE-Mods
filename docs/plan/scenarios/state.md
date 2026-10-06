# Scenarios — State

Index of the scenario catalogue. `Coverage` is copied verbatim from each file so drift is visible.
Legend: `planned:` = automation designed but not landed (FEAT-260910.2 harness, FEAT-260910.3 unit tests).

## Counters (next number per area)

CORE 3 · WELD 6 · GRIND 5 · COLLECT 2 · INV 3 · PRIO 2 · CLUSTER 4 · BUDGET 7 · ZONE 2 · FX 2 · TERM 2 · API 2 · CFG 3 · CHAT 3 · NET 2 · LOC 2

## Index

| ID | Area | Tier | Title | Coverage | Last verified |
|---|---|---|---|---|---|
| SCN-CORE-001 | CORE | 1 | Idle → Welding → Idle state transitions on one damaged grid | ingame:S01 | never |
| SCN-CORE-002 | CORE | 2 | BoundingBox search mode: area size/offset clamps and Show Area | manual | never |
| SCN-CFG-001 | CFG | 1 | ModSettings.xml out-of-range values are clamped on load | planned: unit:SyncModSettingsTests.ValidateAndClamp_ClampsEveryBoundedField | never |
| SCN-CFG-002 | CFG | 1 | Legacy settings migrate to version 7; deprecated GrindIfWeldGetStuck folded | planned: unit:SyncModSettingsTests.AdjustSettings_MigratesVersion0To7 | never |
| SCN-CHAT-001 | CHAT | 1 | /nanobars config set/get round trip and bounds rejection | planned: unit:ConfigCommandTests.SetGetRoundTrip_AndBoundsRejection | never |
| SCN-CHAT-002 | CHAT | 4 | Dedicated server: admin command forwarding and response routing | manual | never |
| SCN-BUDGET-001 | BUDGET | 1 | Block assignment TTL: a claimed block is released to another BaR | planned: unit:TtlCacheTests; ingame:S05 | never |
| SCN-BUDGET-002 | BUDGET | 1 | Per-tick weld/grind count budget refuses beyond cap, resets next frame | planned: unit:PerTickBudgetTests | never |
| SCN-BUDGET-003 | BUDGET | 1 | Ms budget caps work even when count budget remains | planned: unit:PerTickBudgetTests.MsCapWins_WhenCountBudgetRemains | never |
| SCN-BUDGET-004 | BUDGET | 3 | Auto stagger groups 1/2/3 by enabled BaR count | planned: unit:ModResolverTests.AutoStaggerGroupsFor_Thresholds | never |
| SCN-BUDGET-005 | BUDGET | 3 | Failure cooldown: many BaRs do not bounce off one unweldable block | manual | never |
| SCN-BUDGET-006 | BUDGET | 4 | Large-grid performance envelope (3000+ blocks, 5 BaRs) | planned: ingame:S08 | never |
| SCN-WELD-001 | WELD | 1 | WeldOnly repairs damaged blocks and never grinds | ingame:S01 | never |
| SCN-WELD-002 | WELD | 2 | Projected block build incl. DLC-missing skip and silent-fail blacklist | planned: ingame:S03 | never |
| SCN-WELD-003 | WELD | 2 | Weld options Full / Functional / Skeleton stop at the right integrity | manual | never |
| SCN-WELD-004 | WELD | 2 | Ignore-colour blocks are skipped; creative mode needs no components | manual | never |
| SCN-WELD-005 | WELD | 2 | Missing components aggregate over all weld targets | ingame:S02 | never |
| SCN-GRIND-001 | GRIND | 1 | GrindOnly with NoOwnership grinds only unowned grids | ingame:S04 | never |
| SCN-GRIND-002 | GRIND | 2 | Janitor relation matrix × DisableOnly / HackOnly | manual | never |
| SCN-GRIND-003 | GRIND | 2 | Grind order: near / far / smallest-grid first, ignore priority order | planned: unit:QuickSelectTests.MatchesSortForKSmallest_RandomInputs | never |
| SCN-GRIND-004 | GRIND | 2 | Raze queue and mech / dismount per-tick throttles | planned: unit:PerTickBudgetTests | never |
| SCN-COLLECT-001 | COLLECT | 2 | Floating objects inside area only, CollectIfIdle, component class priority | manual | never |
| SCN-INV-001 | INV | 2 | Source scan via conveyors, pull cursor rotation, push-targets-full backoff | manual | never |
| SCN-INV-002 | INV | 2 | Push-immediately flags and inventory-full pause | manual | never |
| SCN-PRIO-001 | PRIO | 2 | Priority list enable / disable / reorder reflected in target order; reset | manual | never |
| SCN-CLUSTER-001 | CLUSTER | 3 | Coordinator election and shared scan results for co-located BaRs | ingame:S07 | never |
| SCN-CLUSTER-002 | CLUSTER | 3 | MaxSystemsPerTargetGrid saturation (strictly greater-than) and secondary task | planned: unit:GridSaturationTrackerTests; ingame:S06 | never |
| SCN-CLUSTER-003 | CLUSTER | 3 | Fair-share target truncation across grids | planned: unit:TruncateGridAwareTests (second wave) | never |
| SCN-ZONE-001 | ZONE | 3 | Safe zones and DefenseShields block weld / grind / build; characters protected | manual | never |
| SCN-FX-001 | FX | 1 | Weld / grind particle and sound effects at block position, distance-culled | manual | never |
| SCN-TERM-001 | TERM | 2 | Terminal controls, actions and sliders reflect fixed vs default mod settings | manual | never |
| SCN-API-001 | API | 3 | PB scripting API incl. ScriptControlled and ScriptControllFixed lockdown | manual; planned: ingame:S09 | never |
| SCN-NET-001 | NET | 4 | Dedicated server: client settings sync and per-sender rate limit | manual | never |
| SCN-LOC-001 | LOC | 1 | Terminal labels render in EN / DE / PL / RU and DisableLocalization | manual | never |

## Coverage summary

- Planned in-game (FEAT-260910.2): CORE-001, WELD-001, WELD-002, WELD-005, GRIND-001, BUDGET-001, BUDGET-006, CLUSTER-001, CLUSTER-002, API-001.
- Planned unit (FEAT-260910.3): CFG-001, CFG-002, CHAT-001, BUDGET-001..004, GRIND-003, GRIND-004, CLUSTER-002, CLUSTER-003.
- Manual only (by nature): CHAT-002, FX-001, TERM-001, NET-001, LOC-001, CORE-002.
- Manual for now, automation candidates later: WELD-003, WELD-004, GRIND-002, COLLECT-001, INV-001, INV-002, PRIO-001, BUDGET-005, ZONE-001.
