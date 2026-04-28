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
| BUG-093 | High | v2.5.3 | `Models/SyncModSettings.cs` | Done | Empty `AllowedGrindJanitorRelations` in ModSettings.xml silently broke grinding on every BaR — `SyncBlockSettings.cs:772` AND-masks each block's `UseGrindJanitorOn` against the mod-wide value. Version-gated v5 migration didn't run on files already at v7. Unconditional clamp in `Load()` validation auto-heals broken configs on load |
| BUG-094 | High | v2.5.3 | `NanobotSystem.Scanning.cs` | Done | Farthest-first grind sort starts in the wrong place on grids larger than the scan cap — per-block count gate in `AsyncAddBlockIfTarget` short-circuited after 1024 candidates, leaving the per-grid sort operating on an arbitrary cache-order prefix instead of the true top-N. Regression from v2.5.0 deleting the full-grid pre-sort but keeping the cap gate. Fix passes `int.MaxValue` through the per-block gates and relies on post-loop `SortAndCapGridCandidates` |
| BUG-095 | Medium | v2.5.3 | `NanobotSystem.Scanning.cs` (`StartAsyncClusterScan` / `StartAsyncApplyClusterResults` early-exit) | Done | Disabled/unpowered early-exit unconditionally cleared `_AsyncUpdateSourcesAndTargetsRunning` without checking if a background scan from a prior tick was still in flight — a disable→enable bounce across ticks could enqueue a second concurrent scan of the same BaR, producing interleaved target-list swaps. Fix gates the cleanup on the flag under `lock(_Welder)`; if a scan is running the early-exit returns and lets the background finally clear the flag naturally |
| BUG-096 | High | v2.5.3 | `NanobotSystem.Scanning.cs` (`SortAndCapGridCandidates`, `AsyncAddBlocksOfGrid`) | Done | Two BUG-094 follow-ups: (A) farthest-first in multi-member cluster picked blocks nobody could reach because `skipRangeCheck=true` collection let out-of-range blocks into the sort input — fixed by partitioning in-range-to-any-member candidates before sorting; (B) cluster-wide per-type cap no longer enforced after `int.MaxValue` gate change, list grew unbounded across grids in grind-only/weld-only workloads — fixed by cap check at grid entry (nulls per-type list for that grid without touching BUG-094's per-block path) plus empty-grid-cache safety guard |
| BUG-097 | High | v2.5.3 | `NanobotSystem.Operations.cs`, `Handlers/PriorityHandling.cs`, `Models/SyncBlockState.cs` | Done | Three issues from the scan/sort/priority/work-mode audit: (A) `GrindIfWeldGetStuck` was functionally identical to `WeldBeforeGrind` — fall-through `!(welding \| transporting)` fired when there was simply nothing to weld; now requires `needWelding && !welding && !transporting` (weld actually stuck) — **superseded by BUG-101 (mode removed entirely after deadlock report)**; (B) `_PrioHash` had a data race between main-thread `UpdateHash` (Clear + Add) and background-scan sort comparator `TryGetValue` — fixed by atomic reference swap; (C) `CurrentWeldingBlock`/`CurrentGrindingBlock` setters did Dec→Inc on the same `GridSystemCount` bucket during same-grid lock-on reference refresh, briefly dipping the counter and letting other BaRs bypass `MaxSystemsPerTargetGrid` — fixed by skipping the pair when old and new grid IDs match |
| BUG-098 | Medium | v2.5.3 | `Handlers/BlockSystemAssigningHandler.cs`, `Handlers/SafeZoneHandler.cs`, `NanobotSystem.Operations.cs`, `Helpers/InventoryHelper.cs` | Done | Hot-path allocation cleanup from the audit. (A) `BlockSystemAssigningHandler.GetBlockKey` allocated a new `string` via `string.Format("{0}:{1}", ...)` on every call from the main-thread weld/grind loops — up to ~768 string allocations per `ServerTryWelding` call per BaR. Replaced with a `BlockKey` struct implementing `IEquatable<BlockKey>` so the `ConcurrentDictionary` lookup is zero-alloc. (B) Eight `MethodProfiler.StopAndLog(..., () => ...)` call sites in hot paths were missing the `if (profilerTs != 0L)` guard, so the Func+closure was allocated per call even with profiling disabled: SafeZoneHandler (3), `TryTransmitState` (3, main thread per tick per BaR), `AddIfConnectedToInventory` (2). All eight wrapped in the guard matching the established pattern |
| BUG-099 | Medium | v2.5.3 | `NanobotSystem.Scanning.cs` (`SortAndCapGridCandidates`) | Done | Profiling session `20260413214958` (58-member cluster, 11k-candidate mega-base grid) showed `SortAndCapGridCandidates` at 28.5 ms steady avg (max 125 ms, sim-speed dipped to 0.77 during big scans). Root cause: the sort comparator recomputed `min(squared-distance to 58 member centers)` for **both** blocks on **every comparison** — ~18M squared-distance calcs per 11k-item sort. `PreSortClusterCandidates` already avoided this via a dict cache; `SortAndCapGridCandidates` was missing the same optimization. Fix: populate `_sortCandidateDistances` (new pooled per-BaR field) during the BUG-096 partition pass (piggybacks on the existing per-candidate iteration), sort comparator does a `TryGetValue` per compare. **Measured** via re-profile session `20260413215939`: sort cost -69% (92 → 28.5 ms avg), total per call -64% (97 → 35 ms), method total -66% (1133 → 381 ms), total mod CPU per 120 s session -35% (12,985 → 8,469 ms), sim-speed min 0.77 → 0.80, main-thread max spike 15.6 → 12.9 ms |
| BUG-100 | Medium | v2.5.3 | `NanobotSystem.Scanning.cs` (`SortAndCapGridCandidates`) | Done | Follow-up to BUG-099. Profile session `20260413220505` (58 members, 4 large grids, 180 s, ~9,900 candidates per grid) showed sort still at 37-46 ms per call — BUG-099's distance cache removed the recompute cost but the comparator's `BlockGrindPriority.GetPriority` / `BlockWeldPriority.GetPriority` calls were the new bottleneck (~34 ms of priority lookups per 9.9k-candidate sort). Fix: pre-fetch priority once per candidate during the BUG-096 partition pass into a new `_sortCandidatePriorities` pooled dict, inline the `SortAndCapGridCandidates` comparator to read from the cache (shared helpers stay untouched). **Measured** via re-profile session `20260413222549`: `SortAndCapGridCandidates` -25% total (2,678 → 2,007 ms), -28% avg (46.2 → 33.4 ms), -37% steady avg (48.2 → 30.6 ms); cascade savings `AsyncClusterScan` -21.5% (4,049 → 3,177), `AsyncAddBlocksOfGrid` -21.5% (4,077 → 3,201); total mod CPU -12% (26,098 → 22,919 ms); **sim-speed min 0.68 → 0.80**, avg 0.96 → 0.99; main-thread max spikes all down ~30% (ServerTryWeldingGrindingCollecting 16.8 → 11.8 ms). Actual sort savings ~30% of predicted magnitude — dict lookup with interface-type keys slower than estimated — but user-visible sim metrics are the strong wins |
| BUG-101 | High | v2.5.4 | `NanobotSystem.Operations.cs`, `Models/SyncBlockSettings.cs`, `Terminal/ComboBoxes.cs`, `Terminal.cs`, `Models/SyncModSettings.cs`, `Models/SyncModSettingsWelder.cs` | Done | Player-reported regression from BUG-097 section A. The narrowed `weldStuck = needWelding && !welding && !transporting` gate left the BaR permanently in `State: Idle` whenever the weld loop had no targets or was exhausted-and-skipped — `needWelding` initialized `false` and only flipped on iterating an unweldable target, so the gate could never open. Resolved by **removing `GrindIfWeldGetStuck` entirely**: dropdown entry deleted, `WorkMode` setter and merge path silently rewrite the deprecated value to `WeldBeforeGrind`, switch case stacks defensively onto `WeldBeforeGrind`, default `AllowedWorkModes` no longer includes the bit, enum value `0x0004` reserved for backwards-compat deserialization. Mode was redundant with `WeldBeforeGrind` and the label was ambiguous; removal preferred over re-litigating the definition |
| FEAT-068 | Medium | v2.5.2 | Done | Companion script — per-LCD config: `[BaR:group]` name tag with multi-surface `@0,1,2`, unscoped + scoped `@BaR@<surfaceIdx>` Custom Data blocks, `FontSize=<n>/auto` key, forced Monospace font for column alignment, auto-seeded default template in empty Custom Data, `Auto-queuing` state line on the Status page, reinit interval 120 s → 30 s, and script comment cleanup (2634 → 2075 lines) |
| FEAT-069 | Medium | — | Proposed | Public ModAPI (delegate-dictionary via `SendModMessage`) so other mods can read and seed BaR settings from code — `TryInitializeSetting` honors admin `ModSettings.xml` precedence, `SetSetting` is unconditional. Extracts shared `SettingsRegistry` from `ConfigCommand` and exposes the 4 `MaximumRequiredElectricPower*` settings that are currently only tunable via direct code |
| FEAT-070 | Medium | v2.5.3 | Done | Consolidate the seven open-coded weld/grind sort comparators in `NanobotSystem.Scanning.cs` into three shared helpers (`CompareGrindNonDistance`, `CompareWeldPriority`, `CompareBlockStableTiebreak`). Every call site (SortAndCapGridCandidates, PreSortClusterCandidates × 2, ApplyClusterResultToSelf × 4) now delegates the non-distance key while keeping local control of distance metric and direction. Folded behavior fix: `ApplyClusterResultToSelf`'s two grind sort sites previously used unconditional nearest-first within same-size grids after the smallest-grid-first tiebreakers, ignoring the user's `GrindNearFirst` flag — now consistent with `PreSortClusterCandidates` so farthest-first is honored end-to-end |
| FEAT-071 | High | v2.5.4 | In Progress | Idle scan backoff — when cluster coordinator finds zero targets for 3 consecutive scans (~30s), extend scan interval from 10s to 30s. Resets instantly when targets appear. Profiling shows 582ms/180s of background scanning producing zero results on 58 idle BaRs |
| FEAT-072 | Medium | v2.5.4 | In Progress | Dirty-flag cluster key optimization — replace per-second `ComputeClusterKey()` string recomputation (0.586ms avg × 171 calls = 103ms/180s) with a version counter that triggers rebuild only on actual settings changes. Eliminates 12+ string concatenations per BaR per second in idle fast path |
| FEAT-073 | Medium | v2.5.4 | In Progress | Empty grid connection traversal optimization — in the empty-grid-cache fast path of `AsyncAddBlocksOfGrid`, iterate only fat blocks instead of all blocks (skips hundreds of armor blocks on large grids). Connection blocks (mechanical, connector, attachable) are always fat blocks |
| FEAT-074 | High | v2.5.4 | In Progress | Quickselect sort optimization — replace O(n log n) full sort in `SortAndCapGridCandidates` with O(n) quickselect + O(k log k) partial sort. On an 11,732-block grid (profiling session `20260416205112`) the sort step drops from 20-33ms to ~3ms per scan. Correctly handles nearest/farthest ordering since all candidates are still considered |
| FEAT-075 | High | v2.5.4 | In Progress | Saturated scan skip — skip full-grid rescan when coordinator has >64 live targets. Per-type saturation check for mixed work modes (grind+weld). Forced rescan with 5s debounce when members deplete targets. Coordinator exhaustion bypass for stale projected-block references. Idle counter now uses coordinator's filtered targets (not cluster-wide raw count). Profiled: scans reduced from 17 to 3-7 per 180s |
| FEAT-076 | Medium | v2.5.4 | In Progress | Grind loop exhaustion — when `ServerTryGrinding` finds nothing grindable (all grid-limited/assigned/destroyed), skip the 256-entry iteration on subsequent ticks. Resets on target list hash change or saturated grid set change. Falls through correctly to welding in GrindBeforeWeld mode |
| FEAT-077 | Medium | v2.5.4 | In Progress | Projector cold-start detection — `HasBuildableProjectorOnGrid()` checks `BuildableBlocksCount > 0` on projectors across own grid, connected grids, and BoundingBox entities during idle. Triggers immediate scan within 1-2s of a player placing the first block of a projection. Respects WorkMode (skipped for GrindOnly) |
| FEAT-078 | Low | v2.5.4 | In Progress | Profiler guard fixes — add `if (profilerTs != 0L)` guards to 16 unguarded `StopAndLog` calls across 10 files. Eliminates ~140 garbage lambda closure allocations/second when profiling is disabled. New diagnostic profiling points: `CheckAndUpdateInventoryFull`, `RebuildSaturatedGrids`, `ImmediateRescanTrigger`, `CleanupFriendlyDamage` |
| FEAT-079 | Low | v2.5.4 | In Progress | Custom info panel — show "Next target scan: Xs" when idle (server), "Scanning for targets..." on clients. Debug mode: scan mode, idle count, last scan candidate counts, forced flag. Helps players understand the BaR is waiting for targets, not stuck |
