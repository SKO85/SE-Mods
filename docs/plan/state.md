# State — Code Review Tracking

Master index for all bugs, features, and reviews.

Items in `TODO/` are pending. Items in `DONE/` are completed.

## Reviews

| ID | Topic | Date | Version | Status |
|----|-------|------|---------|--------|
| REVIEW-full-code-review | Full codebase code review | 2026-03-12 | v2.5.0 | Done |
| REVIEW-profiling-118bars-120s | Profiling analysis — 120s, 118 BaRs, healthy verdict | 2026-03-18 | v2.5.0 | Done |
| REVIEW-extended-code-review-v250 | Extended code review — duplications, dead code, quality | 2026-03-23 | v2.5.0 | Done |
| REVIEW-full-code-review-v250-r3 | Full code review round 3 — correctness, thread safety, edge cases | 2026-03-25 | v2.5.0 | Done |
| REVIEW-full-code-review-v250-r4 | Full code review round 4 — pre-merge review of fix/new_v2.5.0 | 2026-03-26 | v2.5.0 | Done |
| REVIEW-full-code-review-v250-r5 | Full code review round 5 — threading, null safety, performance (BUG-065..071) | 2026-03-26 | v2.5.0 | Done |
| REVIEW-full-code-review-v250-r6 | Full code review round 6 — dead code, exception handling, input validation, atomicity (BUG-072..083) | 2026-03-26 | v2.5.0 | Done |

## Bugs

| ID | Severity | Version | File | Status | Description |
|----|----------|---------|------|--------|-------------|
| BUG-001 | High | v2.5.0 | `DamageHandler.cs:60` | Done | `GetValueOrDefault()` C# 6 compilation error |
| BUG-002 | High | v2.5.0 | `NanobotSystem.Scanning.cs:66,371` | Done | Async flag race condition — scan updates silently dropped |
| BUG-003 | Medium | v2.5.0 | `TtlCache.cs:101-112` | Done | `CleanupExpired()` live enumeration + removal |
| BUG-004 | Medium | v2.5.0 | `PriorityHandling.cs:73,83` | Done | `_PrioHash` indexer `KeyNotFoundException` risk |
| BUG-005 | Medium | v2.5.0 | `BlockSystemAssigningHandler.cs:9` | Done | `IMySlimBlock` reference equality as cache key |
| BUG-006 | Low | v2.5.0 | `SafeZoneHandler.cs:19` | Done | `Zones` dictionary potential memory leak |
| BUG-007 | High | v2.5.0 | `NanobotSystem.Inventory.cs`, `Scanning.cs`, `InventoryHelper.cs` | Done | Push targets include other BaR inventories — circular transfers and inventory-full gridlock |
| BUG-008 | Medium | v2.5.0 | Terminal UI sort toggles | Done | Deselecting a sort toggle (Nearest/Farthest/Smallest Grid) clears all sort options |
| BUG-009 | Medium | v2.5.0 | `Effects.cs` | Done | Sound effects emitted at player position instead of BaR block |
| BUG-010 | Medium | v2.5.0 | `NanobotSystem.CustomInfo.cs`, `SyncBlockState.cs` | Done | SafeZone/Shield false warnings on init — bools default to false before first check |
| BUG-011 | Medium | v2.5.0 | `UtilsInventory.cs`, `NanobotSystem.Inventory.cs` | Done | No guard for deleted inventory owners in push/pull operations |
| BUG-012 | Low | v2.5.0 | `NanobotSystem.Operations.cs`, `NanobotSystem.State.cs`, `SyncBlockState.cs` | Done | Custom info panel shows "Grinding (Transporting)" when only collecting floating objects |
| BUG-013 | Low | v2.5.0 | `NanobotSystem.Init.cs`, `PowerHelper.cs` | Done | Max power shown in custom info panel is incorrect (350 kW instead of 200 kW) |
| BUG-014 | High | v2.5.0 | `NanobotSystem.Operations.cs`, `Scanning.cs` | Done | BaR attempts weld/grind/collect before initial scan completes — false "Missing Components" |
| BUG-015 | Medium | v2.5.0 | `NanobotSystem.Operations.cs` | Done | BaR grinds/collects when welder inventory is full (only transport checked) |
| BUG-016 | Medium | v2.5.0 | `NanobotSystem.Inventory.cs`, `Scanning.cs` | Done | Constant push attempts to full push targets — no backoff |
| BUG-017 | High | v2.5.0 | `Mod.cs`, `NanobotSystem.Operations.cs`, `Grinding.cs`, `Welding.cs` | Done | CountSystemsOnGrid main-thread performance bottleneck |
| BUG-018 | High | v2.5.0 | `NanobotSystem.Operations.cs` | Done | Secondary task blocked in WeldBeforeGrind/GrindBeforeWeld when all primary targets hit MaxSystemsPerTargetGrid |
| BUG-019 | Medium | v2.5.0 | `UtilsSorting.cs`, `NanobotSystem.Scanning.cs` | Done | Grind sorting priority not fully respected — wrong filter + pre-sort cap truncation |
| BUG-020a | High | v2.5.0 | `SafeZoneHandler.cs:446` | Done | Null reference crash on `safeZoneBlock` lookup — falls through to "not protected" |
| BUG-020b | Medium | v2.5.0 | `NanobotSystem.Scanning.cs` | Done | Floating objects collected outside working area — no OBB containment check |
| BUG-021 | High | v2.5.0 | `DamageHandler.cs:109-118` | Done | Thread-unsafe `FriendlyDamage` dictionary + `Mod.NanobotSystems` iteration without lock |
| BUG-022 | Medium | v2.5.0 | `BlockPriorityHandling.cs:68,98` | Done | `GetItemKey` cache ignores `real` parameter — mis-prioritized blocks for 5 min |
| BUG-023 | Medium | v2.5.0 | `UtilsSorting.cs:108` | Done | Silent `catch {}` swallows all sorting exceptions — hides bugs |
| BUG-024 | Medium | v2.5.0 | `UtilsSorting.cs:31` | Done | `Welder.WorldAABB` accessed from background thread — inconsistent distance sort |
| BUG-025 | Low | v2.5.0 | `Logging.cs:113` | Done | Constructor uses `_ModName` before assignment — null in init log message |
| BUG-026 | Medium | v2.5.0 | `SafeZoneHandler.cs:163-181` | Done | LINQ allocations in `GetSafeZonesInRange` — per-block allocs during weld/grind |
| BUG-027 | High | v2.5.0 | `NanobotSystem.Scanning.cs` | Done | Scan budget starvation — single large grid fills scan cap, BaRs idle on other grids |
| BUG-028 | Critical | v2.5.0 | `NanobotSystem.Welding.cs` | Done | Lock-on lost after scan rebuild — IMySlimBlock reference mismatch causes partial welding |
| BUG-029 | High | v2.5.0 | `NanobotSystem.Welding.cs`, `Grinding.cs` | Done | Assignment leak during component starvation — BaRs lock all targets to themselves |
| BUG-030 | Medium | v2.5.0 | `NanobotSystem.Welding.cs` | Done | Projected→physical assignment gap — other BaRs can steal block during stagger wait |
| BUG-031 | Low | v2.5.0 | `Mod.cs` | Done | Auto stagger counts disabled BaR blocks — inflated stagger with few active BaRs |
| BUG-032 | Critical | v2.5.0 | `Chat/Commands/ConfigCommand.cs` | Done | ConfigCommand uses System.Reflection — SE sandbox violation |
| BUG-033 | Critical | v2.5.0 | `NanobotSystem.Grinding.cs:37-44` | Done | Null dereference when targetData.Block is null — crash |
| BUG-034 | Medium | v2.5.0 | `Collections/*HashList.cs, *HashDictionary.cs` | Done | GetSyncList off-by-one: sends MaxSyncItems+1 vs hash of MaxSyncItems |
| BUG-035 | High | v2.5.0 | `Mod.cs:204-213` | Done | Busy-wait infinite loop in UnloadData with no timeout |
| BUG-036 | Low | v2.5.0 | `SafeZoneHandler.cs:36` | Done | Typo "ProtectedFromGindingCache" — missing 'r' in Grinding |

## Features

| ID | Priority | Version | Status | Description |
|----|----------|---------|--------|-------------|
| FEAT-001 | Medium | v2.5.0 | Done | Built-in method profiler with chat commands and auto-stop |
| FEAT-002 | Medium | v2.5.0 | Done | Dynamic MaxSystemsPerTargetGrid default based on game type |
| FEAT-003 | Medium | v2.5.0 | Done | Welding loop perf: AssignToSystem early-out + Ignore preservation |
| FEAT-004 | Medium | v2.5.0 | Done | Custom info panel periodic refresh every 2s |
| FEAT-005 | Low | v2.5.0 | Done | Reduce source/push target scan interval from 60s to 30s |
| FEAT-006 | Medium | v2.5.0 | Done | Decouple source/push-target scanning from target scanning |
| FEAT-007 | Medium | v2.5.0 | Done | Work-mode-aware scan stopping |
| FEAT-008 | High | v2.5.0 | Done | Cluster coordinator — shared scanning for co-located BaR systems |
| FEAT-009 | Medium | v2.5.0 | Done | Debug mode toggle for terminal custom info panel diagnostics |
| FEAT-010 | Medium | v2.5.0 | Done | Profiling additions & cache hit/miss logging for SafeZone handlers |
| FEAT-011 | Medium | v2.5.0 | Done | Add Cryo/Refinery as source/push targets |
| FEAT-012 | High | v2.5.0 | Done | Grinding performance optimizations (mechanical throttle, staggering, grind budget, sort fix, cache TTL) |
| FEAT-013 | Medium | v2.5.0 | Done | Per-cluster stagger with gradual ramp, capped at 3 groups (~500ms max) |
| FEAT-014 | Medium | v2.5.0 | Done | Sim-speed adaptive throttle & profiler sim-speed tracking |
| FEAT-015 | Medium | v2.5.0 | Done | Admin-only `/nanobars sim` command to override sim-speed for testing |
| FEAT-016 | Medium | v2.5.0 | Done | Cluster pre-sort optimization — eliminate redundant per-member sorts (40% reduction in ApplyClusterResultToSelf) |
| FEAT-017 | Medium | v2.5.0 | Done | Empty grid rescan delay — skip grids with no targets for configurable duration (default 30s) |
| FEAT-018 | Medium | v2.5.0 | Done | Improve sorted block cache hit rate — closed, sorted cache removed entirely by FEAT-027 |
| FEAT-019 | Medium | v2.5.0 | Done | AsyncAddBlocksOfBox scan optimization — closed as superseded (early-out already implemented) |
| FEAT-020 | Low | v2.5.0 | Done | ServerTryWeldingGrindingCollecting main-thread cost reduction — deferred, sim speed stable at 1.0 |
| FEAT-021 | Low | v2.5.0 | Done | AsyncScanForSources cost reduction — closed, cost dropped to 25.7ms total (negligible) |
| FEAT-022 | Medium | v2.5.0 | Done | Change Mod.NanobotSystems to ConcurrentDictionary — eliminate inconsistent locking |
| FEAT-023 | High | v2.5.0 | Done | Configurable stagger, grind budget, and assignment TTL via chat commands and ModSettings.xml |
| FEAT-024 | Medium | v2.5.0 | Done | Config reload and reset commands (`/nanobars config reload`, `/nanobars config reset`) |
| FEAT-025 | Medium | v2.5.0 | Done | Profiler auto-stop sends completion feedback to DS clients via network messaging |
| FEAT-026 | Medium | v2.5.0 | Done | Idle weld loop skip — hash-based early-exit for saturated BaRs (25% ServerTryWelding reduction) |
| FEAT-027 | High | v2.5.0 | Done | Remove redundant full-grid sort from scan pipeline — sort only qualifying candidates (45% main-thread reduction, 39% scan reduction) |
| FEAT-028 | Medium | v2.5.0 | Done | Pool TruncateGridAware allocations — 16% ApplyClusterResultToSelf reduction, eliminates 9000+ heap objects |
| FEAT-029 | Medium | v2.5.0 | Done | Add profiling to UpdateCustomInfo — revealed 22% of main-thread budget (3.44ms/s) was invisible |
| FEAT-030 | Medium | v2.5.0 | Won't Fix | Cache per-grid relation lookups — invalid, blocks can have different owners than the grid owner |
| FEAT-031 | Low | v2.5.0 | Done | Move per-block scenario/immunity checks outside loop — 12% AsyncAddBlocksOfGrid reduction |
| FEAT-032 | Medium | v2.5.0 | Done | Reduce UpdateCustomInfo cost — 2s interval (was 1s) + skip TriggerTerminalRefresh on dedicated servers |
| FEAT-033 | Low | v2.5.0 | Done | Move cache classes (TtlCache, SharedGridBlockCache, SharedEntityCache) to Caches/ folder and namespace; remove empty Managers/ |
| FEAT-034 | Medium | v2.5.0 | Done | Weld mode dropdown — Full / Functional / Skeleton combobox + integrity cap fix for non-Full modes |
| FEAT-035 | High | v2.5.0 | Done | Weld loop grid-level skip — precomputed saturated grid HashSet eliminates per-block dictionary lookups |
| FEAT-036 | High | v2.5.0 | Done | ServerDoGrind spike mitigation — time-based grind budget (8ms/tick cap) + sub-timing profiling |
| FEAT-037 | Medium | v2.5.0 | Done | ServerTryPushInventory optimization — adaptive push interval (5s→10s when <75% full), ~50% call reduction during grinding |
| FEAT-038 | Medium | v2.5.0 | Done | MsgBlockStateSend optimization — progressive backoff (1-2s→4-8s) via state fingerprint, ~50-60% transmit reduction |
| FEAT-039 | Low | v2.5.0 | Done | Idle BaR early exit — skip sub-method dispatch in ServerTryWeldingGrindingCollecting for 0-target BaRs (~6ms/s) |
| FEAT-040 | Low | v2.5.0 | Done | Background scan cost scaling — grid AABB containment pre-check skips per-block OBB range checks (~50ms/scan for contained grids) |
| FEAT-041 | Medium | v2.5.0 | Reverted | Idle scan acceleration — shorter scan interval (4s) when idle+AllowBuild for faster projector detection. Reverted: caused 4s spikes |
| FEAT-042 | Medium | v2.5.0 | Done | ServerFindMissingComponents optimization — single GetMissingComponents call for projected blocks (creation component picked first, then remaining) |
| FEAT-043 | Low | v2.5.0 | Done | Profiling additions — ServerDoWeld sub-timing (buildMs/stockpileMs/mountMs), ServerPickFromWelder profiling, Mod.UpdateBeforeSimulation per-frame profiling |
| FEAT-044 | Medium | v2.5.0 | Done | Delta state sync — skip unchanged lists via ExcludedLists bitmask + hash comparison, full sync every 5th transmit. ~80% of sends skip at least some lists |
| FEAT-045 | Low | v2.5.0 | Done | Reduce MaxSyncItems from 64 to 24 — cuts target list payload ~63% per sync message |
| FEAT-046 | Low | v2.5.0 | Done | TryTransmitState profiling — logs skip/send actions, fingerprint changes, backoff level, and excluded lists bitmask |
| BUG-037 | Medium | v2.5.0 | Done | Null CubeGrid in IsSameBlock — guard against null CubeGrid on blocks from closing grids |
| BUG-038 | Medium | v2.5.0 | Done | Null definition in EmptyBlockInventories — guard against null from GetPhysicalItemDefinition |
| BUG-039 | Medium | v2.5.0 | Done | Unguarded projector grid access in ServerDoWeld — null check after proj.Build before accessing projector CubeGrid |
| FEAT-047 | High | v2.5.0 | Done | RebuildClusters skip-when-unchanged — cache cluster key per BaR, skip full rebuild when no keys/count changed. Eliminates 28ms spikes at 234 BaRs |
| FEAT-048 | Medium | v2.5.0 | Superseded | BuildGridSystemCountCache throttle — superseded by BUG-052 live counter (cache removed entirely) |
| BUG-040 | High | v2.5.0 | Done | Safe zone cluster split — include SafeZoneAllowsWelding/Grinding in cluster key so BaRs inside vs outside safe zones get separate coordinators |
| BUG-041 | Critical | v2.5.0 | Done | Projected block welding broken by FEAT-042 — creation component not guaranteed first in transport, restored two-step pick order |
| BUG-042 | High | v2.5.0 | Done | Grinding loop missing CubeGrid null guard — crash on blocks from closing grids (line 43, 125) |
| BUG-043 | Medium | v2.5.0 | Done | SyncBlockState.AssignReceived Position.Value without HasValue check — client crash on corrupted sync |
| BUG-044 | Medium | v2.5.0 | Done | Effects.cs uses System.Threading.Interlocked — SE sandbox violation, replaced with plain ++/-- |
| BUG-045 | Medium | v2.5.0 | Done | TtlCache accesses MyAPIGateway.Session without null guard — crash during shutdown |
| BUG-046 | Low | v2.5.0 | Done | BlockPriorityHandling cache stale for 5 min on block Enabled state change — TTL reduced to 30s |
| BUG-047 | Low | v2.5.0 | Done | SafeZoneHandler returns closed/deleted zones from cache — added Closed/Enabled check |
| BUG-048 | Low | v2.5.0 | Done | Welding assignment not released for non-weldable blocks — moved release outside Ignore branch |
| BUG-049 | Medium | v2.5.0 | Done | Script priority list parsing crashes on malformed data — added values.Length >= 2 guard |
| BUG-050 | Medium | v2.5.0 | Done | Script empty DisplayKinds array causes DivideByZero — added Length > 0 check |
| BUG-051 | Low | v2.5.0 | Done | Script BlockName IMyCubeGrid branch unreachable — swapped order with IMyEntity |
| BUG-052 | High | v2.5.0 | Done | MaxSystemsPerTargetGrid overshoot — live counter replaces stale cache, lock-on blocks now enforce grid limit |
| FEAT-049 | Medium | v2.5.0 | Done | TextHudAPI debug/profiling HUD overlay — admin-only, two-column layout, comprehensive stats, soft dependency on BuildInfo |
| FEAT-050 | Low | v2.5.0 | Done | Debug HUD position command — `/nanobars debug [left\|right]` to position overlay on either side of screen |
| FEAT-051 | Low | v2.5.0 | Done | Hide custom info debug section on dedicated servers — skip terminal debug diagnostics when IsDedicated |
| FEAT-052 | Low | v2.5.0 | Done | Remove "Help Others" from terminal — hidden, forced false, no functional use for the mod |
| FEAT-053 | Low | v2.5.0 | Done | Rename "Build new" to "Build Projections" + reorder welding section (AllowBuild and WeldMode at top) |
| BUG-053 | High | v2.5.0 | Done | BaRs on partially safe-zone-covered grids don't split into separate clusters correctly |
| BUG-054 | High | v2.5.0 | Done | Block assignments growing — two leaks: projected→physical transition + lock-on vanished retry path |
| BUG-055 | Low | v2.5.0 | Done | Debug HUD SafeZoneBlocked not counting building projections disabled |
| BUG-056 | Low | v2.5.0 | Done | Reset All Settings does not restore priority list order — only enabled state was reset |
| BUG-057 | Medium | v2.5.0 | Done | PullComponents allocates new List per source inventory iteration — GC pressure in welding hot path |
| BUG-058 | Medium | v2.5.0 | Done | PreSortClusterCandidates allocates two distance dictionaries per scan cycle — reuse single dict |
| BUG-059 | Low | v2.5.0 | Done | isDedicated detection wrong in HelpCommand — DS note shown to all non-host clients |
| BUG-060 | Low | v2.5.0 | Done | Grid cost timestamp inflated by StopAndLog overhead in AsyncAddBlocksOfGrid |
| BUG-061 | Low | v2.5.0 | Done | Dead code IsBuildingBlockedAtPosition in SafeZoneHandler — never called |
| BUG-062 | Low | v2.5.0 | Done | Orphaned XML doc comment — double summary on IsProjectorGridBuildBlocked |
| BUG-063 | Low | v2.5.0 | Done | Indentation mismatch in Mod.RebuildSourcesAndTargetsTimer try block |
| FEAT-054 | Low | v2.5.0 | Done | Admin welcome message on session join — notifies about /nanobars help |
| FEAT-055 | Low | v2.5.0 | Done | Show mod version in debug HUD overlay and terminal custom info panel |
| FEAT-056 | Low | v2.5.0 | Done | `/nanobars profile summary` — live profiling summary in mission screen dialog (admin-only) |
| FEAT-057 | Low | v2.5.0 | Done | Show ModSettings.xml loaded status in debug HUD and terminal info |
| FEAT-058 | Low | v2.5.0 | Done | "Powered by SKO85" footer in debug HUD overlay |
| FEAT-059 | Low | v2.5.0 | Done | Extract SendToAdmins helper with reusable player list — deduplicate admin broadcast |
| FEAT-060 | Low | v2.5.0 | Done | Sim-speed min/avg in profile summary HUD panel |
| FEAT-061 | Low | v2.5.0 | Done | Debug command hint when local HUD is hidden |
| BUG-064 | Low | v2.5.0 | Done | Debug HUD scan age inflated by disabled/unscanned BaRs (showed 590s+) |
| FEAT-062 | Medium | v2.5.0 | Done | Per-session profiling with named sessions, list, and clear commands |
| FEAT-063 | Low | v2.5.0 | Done | Custom info panel cleanup — remove redundant debug items, restrict to local game only |
| FEAT-064 | Medium | v2.5.0 | Done | Admin commands for remote BaR system management (`/nanobars systems list/count/enable/disable`) |
| BUG-065 | High | v2.5.0 | Done | Missing volatile on cross-thread flags (_AsyncUpdateSourcesAndTargetsRunning, _InitialScanCompleted, _PushTargetsFull, AssignedCluster, _sharedResult) |
| BUG-066 | High | v2.5.0 | Done | Weld exhaustion hash read/write outside lock — TOCTOU race with background scan |
| BUG-067 | High | v2.5.0 | Done | Division by zero in ServerDoGrind on modded blocks with zero DisassembleRatio/MaxIntegrity |
| BUG-068 | Medium | v2.5.0 | Done | Missing null guards: MyCubeGrid cast in ServerFindMissingComponents, Entity in ServerDoCollectFloating |
| BUG-069 | Medium | v2.5.0 | Done | Close() clears state while async scan may still be running — use-after-close race |
| BUG-070 | Medium | v2.5.0 | Done | Profiler lambda closures allocated every tick even when profiling is off — GC pressure |
| BUG-071 | Low | v2.5.0 | Done | PullComponents allocates new List per call + LINQ .Count() on dictionary |

| BUG-072 | Critical | v2.5.0 | `Utils/Utils.cs:35-41` | Done | Dead code: inner `MaxDeformation < MinDeformation` check unreachable inside `> MinDeformation` block — deformation tracking never updates |
| BUG-073 | Critical | v2.5.0 | `NanobotSystem.Init.cs:170-171` | Done | Close() spin-wait on `_AsyncUpdateSourcesAndTargetsRunning` without lock — may not observe background thread's write, risking use-after-free |
| BUG-074 | High | v2.5.0 | `NanobotSystem.State.cs:65-97` | Done | Silent `catch {}` in IsShieldProtected/IsWelderShielded — shield check failures invisible |
| BUG-075 | High | v2.5.0 | `NanobotSystem.Welding.cs:383-387` | Done | Silent `catch {}` in IsWeldIntegrityReached returns true — silently skips incomplete blocks |
| BUG-076 | High | v2.5.0 | `Chat/Commands/ConfigCommand.cs:313-327` | Done | IntSetting/FloatSetting accept any parsed value — no bounds validation on chat config commands |
| BUG-077 | Medium | v2.5.0 | `Mod.cs:43-59` | Done | DecrementGridCount TryGetValue→TryUpdate non-atomic — second decrement silently fails, count drifts |
| BUG-078 | Medium | v2.5.0 | `Caches/SharedEntityCache.cs:33,100` | Done | GetEntitiesInBox/Cleanup access Session.ElapsedPlayTime without null check — crash during load/unload |
| BUG-079 | Medium | v2.5.0 | `Chat/Commands/SystemsCommand.cs:68-73` | Done | Empty `--owner`/`--grid` filter produces empty string — IndexOf matches all players/grids |
| BUG-080 | Medium | v2.5.0 | `Handlers/GridOwnershipCacheHandler.cs` | Done | Multiple bare `catch {}` blocks — ownership errors invisible, wrong relations used silently |
| BUG-081 | High | v2.5.0 | `NanobotSystem.Scanning.cs` | TODO | Lock ordering inconsistency: `_Welder` vs `PossibleWeldTargets` acquired in different orders across code paths — potential deadlock |
| BUG-082 | Medium | v2.5.0 | `NanobotSystem.Scanning.cs:1392-1436` | TODO | Cross-collection consistency gap: weld/grind/float targets swapped under separate locks — one-tick mixed state possible |
| BUG-083 | Low | v2.5.0 | `NanobotSystem.Scanning.cs:36-37` | TODO | `_LastTargetsUpdate`/`_LastSourceUpdate` (TimeSpan, 8 bytes) read without memory barrier from game loop, written from background |
| BUG-084 | Low | v2.5.0 | `docs/pages/.../FAQ/index.md`, `Config/index.md` | Done | Documentation references removed `-cwsf` command — replaced with `/nanobars config create` |
| BUG-085 | High | v2.5.0 | `NanobotSystem.Update.cs` | Done | Fast mode (multiplier > 10) skips housekeeping: settings never save, friendly damage never cleaned, resource sink stale, state sync broken (v2.4.4) |
| FEAT-066 | High | v2.5.0 | Done | Decouple update frequency from multiplier — new `WorkSpeed` setting (1-10) controls operation tick rate independently. Fixes BUG-085 |

## Previously Fixed (not in this review)

These were identified and fixed on the `fix/v2.5.1` branch — see MEMORY for details:
- Idle BaRs when `MaxSystemsPerTargetGrid` limit fires (welding loop)
- Idle BaRs when `MaxSystemsPerTargetGrid` limit fires (grinding loop)

| BUG-086 | High | v2.5.1 | `NanobotSystem.Scanning.cs` | Done | Grind sort ignores user settings: per-grid cap overrides `GrindIgnorePriorityOrder`, `TruncateGridAware` disrupts sort, smallest-grid interleaves blocks from same-size grids |
| BUG-087 | Low | v2.5.1 | `Effects.cs` | TODO | Effects no distance culling |
| BUG-088 | High | v2.5.2 | `NanobotSystem.Scanning.cs` | Done | Cluster distant members starved on large single-grid bases — coordinator-centric collection cap and sort starve BaRs >150m from the coordinator; companion PB script shows `NULL` target |
| FEAT-067 | Medium | v2.5.2 | Done | `/nanobars version` chat command — prints client version, and on dedicated servers also the server version via a network roundtrip, so players can diagnose version mismatches |
| BUG-089 | Medium | v2.5.2 | `NanobotSystem.Operations.cs` | Done | Idle fast-path (FEAT-039) skips `ServerTryPushInventory` when BaR has leftover items but is not full — auto-push silently stops while idle; items stuck until next target acquired |
| BUG-090 | Low | v2.5.2 | `NanobotSystem.Scanning.cs:1681`, `Inventory.cs`, `NanobotSystem.cs` | Done | Push-target backoff (BUG-016) compares `Count` only — same-size container swaps leave `_PushTargetsFull` armed for up to 60s; replaced with count+EntityId-XOR signature |
| BUG-091 | Medium | v2.5.2 | `NanobotSystem.Scanning.cs` | Done | `GrindSmallestGridFirst` ordered same-size grids by arbitrary `EntityId` (creation order) instead of proximity — BaR could travel to a far equal-size grid before a nearby one. Per-grid min-distance lookup now drives the tiebreaker (follow-up to BUG-086) |
| BUG-092 | Medium | — | `NanobotSystem.Update.cs` (hot path) | TODO | Fast mode (`WorkSpeed ≥ 5`) triggers periodic 100–170ms main-thread pauses and sim dips on large clusters — stagger-throttled `UpdateBeforeSimulation10_100` captures what looks like gen2 GC pauses. Default `WorkSpeed=1` is clean. Follow-up to FEAT-066; needs allocation audit of hot path |
| FEAT-068 | Medium | v2.5.2 | Done | Companion script — per-LCD config: `[BaR:group]` name tag with multi-surface `@0,1,2`, unscoped + scoped `@BaR@<surfaceIdx>` Custom Data blocks, `FontSize=<n>/auto` key, forced Monospace font for column alignment, auto-seeded default template in empty Custom Data, `Auto-queuing` state line on the Status page, reinit interval 120 s → 30 s, and script comment cleanup (2634 → 2075 lines) |
