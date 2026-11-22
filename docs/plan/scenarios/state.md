# Scenarios — State

Index of the scenario catalogue. `Coverage` is copied verbatim from each file so drift is visible.
Legend: `planned:` = automation designed but not landed (FEAT-260910.2 harness, FEAT-260910.3 unit tests).

## Counters (next number per area)

CORE 3 · WELD 7 · GRIND 5 · COLLECT 2 · INV 3 · PRIO 2 · CLUSTER 4 · BUDGET 7 · ZONE 2 · FX 2 · TERM 2 · API 2 · CFG 3 · CHAT 3 · NET 2 · LOC 2

## Index

| ID | Area | Tier | Title | Coverage | Last verified |
|---|---|---|---|---|---|
| SCN-CORE-001 | CORE | 1 | Idle → Welding → Idle state transitions on one damaged grid | unit:GridChangeHandlerTests; ingame:S01, S57, S72 | never |
| SCN-CORE-002 | CORE | 2 | BoundingBox search mode: area size/offset clamps and Show Area | manual; ingame:S25; ingame:S75, S76 | never |
| SCN-CORE-003 | CORE | 2 | No power / power restored / insufficient power | ingame:S19; ingame:S20 | never |
| SCN-CORE-004 | CORE | 2 | Work-mode order when weld and grind work exist together | ingame:S77; ingame:S78 | 261122.28 in game |
| SCN-CFG-001 | CFG | 1 | ModSettings.xml out-of-range values are clamped on load | unit:SyncModSettingsTests.ValidateAndClamp_ClampsEveryBoundedField | 261006.3 unit |
| SCN-CFG-002 | CFG | 1 | Legacy settings migrate to version 7; deprecated GrindIfWeldGetStuck folded | unit:SyncModSettingsTests.AdjustSettings_MigratesOldVersion_AndIsIdempotent; unit:BlockSettingsLegacyLoadTests | 261006.3 unit |
| SCN-CFG-003 | CFG | 2 | Admin restrictions and switches in ModSettings win over block settings | ingame:S46, S47, S48, S49, S50, S51, S52, S53; unit:BlockSettingsLimitsTests | never |
| SCN-CHAT-001 | CHAT | 1 | /nanobars config set/get round trip and bounds rejection | unit:ConfigCommandTests.SetGetRoundTrip_AndBoundsRejection | 261006.3 unit |
| SCN-CHAT-002 | CHAT | 4 | Dedicated server: admin command forwarding and response routing | manual | never |
| SCN-BUDGET-001 | BUDGET | 1 | Block assignment TTL: a claimed block is released to another BaR | unit:TtlCacheTests; ingame:S05, S51, S55, S56 | 261006.3 unit |
| SCN-BUDGET-002 | BUDGET | 1 | Per-tick weld/grind count budget refuses beyond cap, resets next frame | unit:PerTickBudgetTests; ingame:S52 | 261006.3 unit |
| SCN-BUDGET-003 | BUDGET | 1 | Ms budget caps work even when count budget remains | unit:PerTickBudgetTests.MsCapWins_WhenCountBudgetRemains | 261006.3 unit |
| SCN-BUDGET-004 | BUDGET | 3 | Auto stagger groups 1/2/3 by enabled BaR count | unit:ModResolverTests.AutoStaggerGroups_Thresholds; unit:StaggerGroupsTests | 261006.3 unit |
| SCN-BUDGET-005 | BUDGET | 3 | Failure cooldown: many BaRs do not bounce off one unweldable block | manual | never |
| SCN-BUDGET-006 | BUDGET | 4 | Large-grid performance envelope (3000+ blocks, 5 BaRs) | ingame:S08, S43 | never |
| SCN-BUDGET-007 | BUDGET | 3 | Station vs ship standing still vs ship in motion, ShowArea off/on: cost and work | ingame:S28; ingame:S29; ingame:S30; ingame:S31; ingame:S32; ingame:S33; unit:AreaDrawModeTests | 261122.8 in game |
| SCN-BUDGET-008 | BUDGET | 3 | Many BaRs on target ships of mixed sizes: weld, grind, mixed; large vs small clusters | ingame:S64; ingame:S65; ingame:S66; ingame:S67; ingame:S68; ingame:S69; ingame:S70 | never |
| SCN-BUDGET-009 | BUDGET | 2 | Speed settings (WorkSpeed, multipliers) and throughput floors | ingame:S79; ingame:S80 | 261122.28 in game |
| SCN-WELD-001 | WELD | 1 | WeldOnly repairs damaged blocks and never grinds | ingame:S01, S26 | never |
| SCN-WELD-002 | WELD | 2 | Projected block build incl. DLC-missing skip, expiring silent-fail skip, SE 1.211.018 builder-inventory lend | unit:ProjectedBuildComponentTests, ProjectorScanCacheTests, LimitsPreCheckTests; ingame:S03, S27, S42, S46 | 261122.3 unit |
| SCN-WELD-003 | WELD | 2 | Weld options Full / Functional / Skeleton stop at the right integrity | ingame:S21, S22 | never |
| SCN-WELD-004 | WELD | 2 | Ignore-colour blocks are skipped; creative mode needs no components | manual; ingame:S23 | never |
| SCN-WELD-005 | WELD | 2 | Missing components aggregate over all weld targets | ingame:S02, S84, unit | 261122.32 in game |
| SCN-WELD-006 | WELD | 2 | Dented full-integrity blocks get a budgeted skeleton reset, once | manual, unit | 261023.3 unit |
| SCN-WELD-007 | WELD | 2 | Mechanical subgrid (rotor head) is repaired in Grids mode, not ground by the janitor | ingame:S18, S34, S35, S36, S37, S38, S44 | never |
| SCN-WELD-008 | WELD | 2 | A starved BaR does not stop a stocked BaR elsewhere from repairing the same grid | ingame:S86; unit:BlockFailureCooldownTests | 261123.7 in game |
| SCN-GRIND-001 | GRIND | 1 | GrindOnly with NoOwnership grinds only unowned grids | ingame:S04, S17; unit:JanitorOwnershipTests | never |
| SCN-GRIND-002 | GRIND | 2 | Janitor relation matrix × DisableOnly / HackOnly | manual; ingame:S17, S45 | never |
| SCN-GRIND-003 | GRIND | 2 | Grind order: near / far / smallest-grid first, ignore priority order | unit:QuickSelectTests.MatchesSortForKSmallest_RandomInputs; ingame:S11, S12, S13 | 261006.3 unit |
| SCN-GRIND-004 | GRIND | 2 | Raze queue and mech / dismount per-tick throttles | unit:PerTickBudgetTests | 261006.3 unit |
| SCN-GRIND-005 | GRIND | 2 | Grind colour grinds painted blocks on the own grid | ingame:S24 | never |
| SCN-GRIND-006 | GRIND | 2 | Protected grids are not ground (indestructible, immune, damage-handler protection) | unit:GrindDamageVetoTests; ingame:S73, S74 | 261122.25 in game |
| SCN-COLLECT-001 | COLLECT | 2 | Floating objects inside area only, CollectIfIdle, component class priority | manual, ingame:S10, unit | 261023.2 unit |
| SCN-INV-001 | INV | 2 | Source scan via conveyors, pull cursor rotation, push-targets-full backoff | manual | never |
| SCN-INV-002 | INV | 2 | Push-immediately flags and inventory-full pause | manual | never |
| SCN-PRIO-001 | PRIO | 2 | Priority list enable / disable / reorder reflected in target order; reset | manual, unit; ingame:S14, S15, S16 | 261023.4 unit |
| SCN-CLUSTER-001 | CLUSTER | 3 | Coordinator election and shared scan results for co-located BaRs | ingame:S07 | never |
| SCN-CLUSTER-002 | CLUSTER | 3 | MaxSystemsPerTargetGrid saturation (strictly greater-than) and secondary task | unit:GridSaturationTrackerTests; ingame:S06, S50, S54 | 261006.3 unit |
| SCN-CLUSTER-003 | CLUSTER | 3 | Fair-share target truncation across grids | planned: unit:TruncateGridAwareTests (second wave) | never |
| SCN-ZONE-001 | ZONE | 3 | Safe zones and DefenseShields block weld / grind / build; characters protected | manual; unit:ShieldProtectionTests; ingame:S39, S40, S41, S42, S53, S58-S63, S85 | 261122.36 in game |
| SCN-FX-001 | FX | 1 | Weld / grind particle and sound effects at block position, distance-culled | manual | never |
| SCN-TERM-001 | TERM | 2 | Terminal controls, actions and sliders reflect fixed vs default mod settings | manual, unit | 261023.6 unit |
| SCN-API-001 | API | 3 | PB scripting API incl. ScriptControlled and ScriptControllFixed lockdown | manual, unit:CompanionScriptContractTests; ingame:S09 | 261122.18 in game |
| SCN-API-002 | API | 3 | Companion script: groups, assembler queuing, displays, tags, Custom Data, arguments | unit:CompanionScriptBehaviourTests; ingame:S71, S81, S82, S83 | 261122.29 in game |
| SCN-NET-001 | NET | 4 | Dedicated server: client settings sync and per-sender rate limit | manual; unit:ProtoContractTests.ProtoMemberTags_AreUniquePerType | 261006.3 unit |
| SCN-LOC-001 | LOC | 1 | Terminal labels render in EN / DE / PL / RU and DisableLocalization | manual; unit:LocalizationTests.Translation_HasExactlyTheEnglishKeys | 261006.3 unit |

## Coverage summary

- Planned in-game (FEAT-260910.2): CORE-001, WELD-001, WELD-002, WELD-005, GRIND-001, BUDGET-001, BUDGET-006, CLUSTER-001, CLUSTER-002, API-001.
- Planned unit (FEAT-260910.3): CFG-001, CFG-002, CHAT-001, BUDGET-001..004, GRIND-003, GRIND-004, CLUSTER-002, CLUSTER-003.
- Manual only (by nature): CHAT-002, FX-001, TERM-001, NET-001, LOC-001, CORE-002.
- Manual for now, automation candidates later: WELD-003, WELD-004, GRIND-002, COLLECT-001, INV-001, INV-002, PRIO-001, BUDGET-005, ZONE-001.
