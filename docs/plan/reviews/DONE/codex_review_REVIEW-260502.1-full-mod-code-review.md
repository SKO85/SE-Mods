# REVIEW-260502.1: Full Mod Code Review (Codex)

## Status: Resolved (triaged into BUG tickets)

## Reviewer: GitHub Copilot (GPT-5.3-Codex)

## Date: 2026-05-02

## Version: Current workspace HEAD

## Scope

- Full read-only review of C# mod code under:
  - SKO-Nanobot-BuildAndRepair-System/Data/Scripts/SKO-Nanobot-BuildAndRepair-System
- Focus areas requested:
  - Issues
  - Inconsistencies
  - Performance
  - Dead code
  - Duplications
  - Logic flow
- Validation run:
  - dotnet build SKO-Nanobot-BuildAndRepair-System/Data/Scripts/SKO-Nanobot-BuildAndRepair-System/SKO-Nanobot-BuildAndRepair-System.csproj -c Release -v minimal
  - Result: Build succeeded.

## Findings

### 1) High: Safe-zone permission conversion is brittle and can fail-open

- Category: Logic flow, correctness
- Location:
  - Handlers/SafeZoneHandler.cs:419
  - Handlers/SafeZoneHandler.cs:468
  - Handlers/SafeZoneHandler.cs:469
  - Handlers/SafeZoneHandler.cs:470
  - Handlers/SafeZoneHandler.cs:519
- Detail:
  - CastProhibit<T> performs (T)val using boxed enum values from a different enum type.
  - This conversion path is unsafe and can throw at runtime depending on boxing/unboxing behavior.
  - GetActionsAllowedForSystem catches exceptions and returns defaults with all actions allowed.
- Risk:
  - If conversion fails, safe-zone restrictions may be bypassed for weld/grind/build checks.

### 2) High: Grinder protection check fails open on exception and caches the open result

- Category: Correctness, defensive behavior
- Location:
  - Handlers/SafeZoneHandler.cs:488
  - Handlers/SafeZoneHandler.cs:591
- Detail:
  - IsProtectedFromGrinding catches all exceptions and sets cache to false (not protected), then returns false.
- Risk:
  - Transient errors in safe-zone logic can allow grinding that should be blocked until TTL expires.

### 3) Medium: Admin-role checks are duplicated and inconsistent across modules

- Category: Inconsistency, duplication, minor perf
- Location:
  - Chat/ChatHandler.cs:286
  - Handlers/HudHandler.cs:667
  - Handlers/NetworkMessagingHandler.cs:364
  - Handlers/NetworkMessagingHandler.cs:158 (uses enum compare pattern)
- Detail:
  - Three sites use PromoteLevel.ToString() + string comparison.
  - Another site already uses direct MyPromoteLevel enum comparison.
- Risk:
  - Behavior drift and extra allocations in frequently called paths.

### 4) Medium: O(N^2) friendly-cache rebuild runs every 5 seconds

- Category: Performance, scaling
- Location:
  - Managers/PeriodicMaintenanceScheduler.cs:61
  - Managers/PeriodicMaintenanceScheduler.cs:65
  - Handlers/FriendlyRelationsHandler.cs:123
  - Handlers/FriendlyRelationsHandler.cs:134
- Detail:
  - FriendlyRelationsHandler.Rebuild does nested iteration over Mod.NanobotSystems.
  - Scheduled every 5 seconds.
- Risk:
  - Large BaR fleets can spend noticeable server time in ownership/friendly recomputation.

### 5) Medium: Broad exception swallowing reduces observability in core paths

- Category: Reliability, maintainability
- Representative locations:
  - Managers/BackgroundTaskQueue.cs:84
  - Managers/PeriodicMaintenanceScheduler.cs:49-54
  - Managers/PeriodicMaintenanceScheduler.cs:65-66
  - Managers/PeriodicMaintenanceScheduler.cs:89
  - Mod.cs:337-375
  - Handlers/NetworkMessagingHandler.cs:381
  - Handlers/NetworkMessagingHandler.cs:392
  - Handlers/NetworkMessagingHandler.cs:404
  - Handlers/NetworkMessagingHandler.cs:415
- Detail:
  - Numerous catch {} blocks in scheduling/network/background task flows.
- Risk:
  - Hidden faults are hard to diagnose and can appear as silent state desync or missing behavior.

### 6) Medium-Low: Grid scan cache key uses 32-bit hash of large settings surface

- Category: Correctness risk (edge), performance design
- Location:
  - NanobotSystem.Scanning.Block.cs:328
  - NanobotSystem.Scanning.Block.cs:344
  - NanobotSystem.Scanning.Block.cs:345
  - Cluster/GridScanCache.cs:40
- Detail:
  - Cache reuses per-grid results by comparing a single int ParamsHash.
  - Priority strings are folded using string.GetHashCode().
- Risk:
  - Rare collisions can produce incorrect cache hits and stale/mismatched target sets for up to TTL window.

### 7) Low: Unused cluster-key string field appears to be dead state

- Category: Dead code
- Location:
  - NanobotSystem.cs:341
  - Cluster/ScanClusterCoordinator.cs:82
  - Cluster/ScanClusterCoordinator.cs:88
- Detail:
  - \_lastClusterKey is assigned/reset but not consumed in decisions.
  - \_lastClusterKeyHash is used for fast-change detection; string key field appears vestigial.
- Risk:
  - Minor maintenance overhead and possible confusion about source of truth.

### 8) Low: Per-update allocation in HUD stats path

- Category: Performance (micro)
- Location:
  - Handlers/HudHandler.cs:506
  - Handlers/HudHandler.cs:583
  - Handlers/HudHandler.cs:584
- Detail:
  - BuildStats allocates new List<IMyPlayer>() each call.
- Risk:
  - Small but avoidable GC pressure in continuous debug/profiling sessions.

## Recommendations

1. Harden safe-zone action conversion and avoid fail-open behavior on conversion or lookup exceptions.
2. Change safe-zone grinder exception fallback from allow to conservative block (or explicitly configurable behavior).
3. Centralize admin-role checks to one helper using direct enum comparisons.
4. Rework FriendlyRelationsHandler.Rebuild toward incremental/dirty updates or lower-frequency recomputation based on changes.
5. Replace critical catch {} blocks with at least throttled logging in scheduler/network/background pathways.
6. Strengthen GridScanCache keying (larger signature or collision-resistant composite).
7. Remove or repurpose \_lastClusterKey if no longer required.
8. Reuse a pooled player list in HUD stats collection.

## Action Items

- [ ] Prioritize safe-zone fail-open findings (#1 and #2) for hotfix triage.
- [ ] Unify admin-check logic and remove string-based role checks.
- [ ] Evaluate scalability budget for FriendlyRelations rebuild under high BaR counts.
- [ ] Add minimal diagnostic logging for currently silent exception paths.
- [ ] Decide whether GridScanCache collision risk warrants stronger keying now or later.

## Resolution (2026-05-02)

Each finding verified against current code. Triage outcome:

| # | Finding | Verdict | Ticket |
| --- | --- | --- | --- |
| 1 | Safe-zone fail-open (CastProhibit half dismissed) | Valid for fail-open default; `CastProhibit` itself is the canonical SE-modding pattern and not a bug | BUG-260502.1 |
| 2 | IsProtectedFromGrinding fails open + caches | Valid | BUG-260502.2 |
| 3 | Admin-role check duplication | Valid | BUG-260502.3 |
| 4 | Friendly rebuild O(N²) every 5 s | Valid (actual cost `O(distinct_owner_count × N)` due to existing `seenOwners` dedup, but still grows on multi-faction servers) | BUG-260502.4 |
| 5 | Exception swallowing | Valid for runtime paths (BackgroundTaskQueue, PeriodicMaintenanceScheduler, NetworkMessagingHandler); `UnloadData` blanket catches are intentional and stay | BUG-260502.5 |
| 6 | 32-bit GridScanCache hash collision | Theoretical only — birthday-paradox math gives ~10⁻⁷ collision risk under realistic settings diversity, and the cache stores `paramsHash` for equality not as the dict key, so distinct hashes already produce distinct entries. **Won't Fix** unless evidence of an actual collision surfaces. | — |
| 7 | Dead `_lastClusterKey` field | Valid — vestigial after FEAT-072 switched to `_lastClusterKeyHash` | BUG-260502.6 |
| 8 | HUD `BuildStats` per-call list allocation | Valid but bounded (only when DebugMode/profiling active) | BUG-260502.7 |

Filed 7 BUG tickets; finding #6 dismissed as a theoretical collision concern with negligible operational risk.
