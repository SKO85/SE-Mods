# BUG-007: Push targets include other BaR inventories — causes circular transfers and inventory-full gridlock

## Status: Fixed
## Severity: High
## Version: v2.5.0
## Found In: NanobotSystem.Inventory.cs, NanobotSystem.Scanning.cs, Helpers/InventoryHelper.cs

## Description

When BaR push settings are enabled (PushIngotOreImmediately / PushComponentImmediately / PushItemsImmediately), the push destination list (`_PossibleSources`) includes **all** inventory-bearing blocks: cargo containers, assemblers, grinders, sorters, connectors, **and other BaR welders**. When all cargo containers are full, BaRs push items to other BaR inventories, which then attempt to push those same items out — creating a circular transfer loop where items bounce between BaRs without ever reaching stable storage.

This causes two problems:
1. **Circular item transfers**: BaR A pushes to BaR B, BaR B pushes back to BaR A (or to BaR C, etc.), wasting transfer operations every tick.
2. **Massive performance cost**: `ServerEmptyTransportInventory` becomes the #1 hotspot. Every BaR iterates the entire `_PossibleSources` list (including 20+ other BaR inventories) on every call, all failing — O(n * sources) wasted work per tick.

### Profiler Evidence (120s window, ~25 active BaRs — BEFORE fix)

| Method | Calls | Total ms | Avg ms | Max ms | Notes |
|--------|-------|----------|--------|--------|-------|
| ServerEmptyTransportInventory | 5,347 | 45,443 | 8.50 | 172.6 | **#1 hotspot**; every call shows `inventoryFull=True` |
| ServerTryPushInventory | 5,347 | 649 | 0.12 | 9.7 | Also iterates full source list |
| ServerTryWeldingGrindingCollecting | 8,550 | 46,327 | 5.42 | 172.9 | Calls ServerEmptyTransportInventory |

The `ServerEmptyTransportInventory` alone consumes **~38% of the total Update domain time** (45.4s out of 120s).

## Root Cause

### 1. `_PossibleSources` is used for both pulling AND pushing

`_PossibleSources` is populated in `AsyncAddBlockIfTargetOrSource()` (Scanning.cs) via `InventoryHelper.AddIfConnectedToInventory()`. This helper adds inventories from all block types: `IMyCargoContainer`, `IMyAssembler`, `IMyShipWelder`, `IMyShipGrinder`, `IMyConveyorSorter`, `IMyShipConnector`.

The same list is used for:
- **Pulling** components in `PullComponents()` — needs all sources (correct)
- **Pushing** items in `ServerTryPushInventory()` — should only target cargo containers
- **Pushing** transport inventory in `ServerEmptyTransportInventory()` — should only target cargo containers

### 2. `ServerEmptyTransportInventory` bypasses all BaR exclusion

`ServerEmptyTransportInventory` called `PushComponents(_PossibleSources, null)` — the `null` exclude delegate meant **no BaR was excluded**.

### 3. `ShouldStopScan` was blocking source discovery

When weld/grind target lists hit their caps (256), `ShouldStopScan()` broke out of the block iteration loop, stopping source scanning as collateral damage.

### 4. `IsConnectedTo` is unreliable from background threads

`IsConnectedTo` returns inconsistent results when called from background scanning threads (`Mod.AddAsyncAction()`). SE's conveyor graph traversal is likely not thread-safe.

**Evidence**: On a grid with 120 BaRs + 2 cargo containers (all confirmed conveyor-connected via manual transfer), `IsConnectedTo` consistently splits BaRs into two groups:
- Group A (24 BaRs): `srcEligible=122, srcConnected=25` — sees 23 BaRs + 2 cargos
- Group B (96 BaRs): `srcEligible=122, srcConnected=95` — sees 95 BaRs + 0 cargos

All BaRs iterate the same 1419 blocks on the same grid with the same 122 eligible source blocks. The ONLY difference is `IsConnectedTo` returning `false` for blocks that manual transfers prove are connected.

### 5. ConnectionCache returned stale hits without re-adding inventories

The static `ConnectionCache` (15s TTL) returned `isConnected=true` from a previous scan but the method returned early without re-adding inventories to `possibleSources` (which is cleared each scan cycle). Latent bug since cache TTL (15s) < source scan interval (60s), but incorrect by design.

## Implemented Fixes

### Fix 1: Separate push-target list (DONE — deployed, verified)

Created `_PossiblePushTargets` / `_TempPossiblePushTargets` lists populated during scanning with only `IMyCargoContainer` inventories. All push operations now use this list.

**Files modified:**
- `NanobotSystem.cs` — Added `_PossiblePushTargets` and `_TempPossiblePushTargets` fields
- `NanobotSystem.Scanning.cs` — Populates push targets from sources during scan, swaps under lock
- `NanobotSystem.Inventory.cs` — All 4 push calls changed from `_PossibleSources` to `_PossiblePushTargets`
- `NanobotSystem.Init.cs` — Cleanup in Close()

**Profiler result AFTER fix:**
| Method | Calls | Total ms (before) | Total ms (after) |
|--------|-------|-------------------|-----------------|
| ServerEmptyTransportInventory | 1,008 | 45,443 | **0.185** |

### Fix 2: ShouldStopScan no longer blocks source discovery (DONE — deployed)

Introduced `scanStopped` flag in `AsyncAddBlocksOfGrid`. When target lists are full, source scanning continues — only target evaluation is skipped. Also updated recursion guards for mechanical connections, connectors, and attachable blocks.

**Files modified:** `NanobotSystem.Scanning.cs`

### Fix 3: ConnectionCache re-adds inventories on cache hit (DONE — deployed)

On cache hit with `isConnected=true`, inventories are now re-added to `possibleSources`. Also moved `ConnectionCache.Set()` to after the inventory loop (was called per-inventory inside the loop).

**Files modified:** `Helpers/InventoryHelper.cs`

### Fix 4: Conveyor connectivity check for same-grid blocks (v1: FAILED, v2: REVERTED, v3: DONE)

**v1 (CubeGrid.EntityId comparison — FAILED):** Compared `terminalBlock.CubeGrid.EntityId == welder.CubeGrid.EntityId` in `AddIfConnectedToInventory`. Profiler results (60 BaRs + 2 cargos, 120s window) showed the same split pattern as the original `IsConnectedTo` bug:
- 48 BaRs: sources=47, pushTargets=0 (sees 47 Group A peers, no cargos)
- 12 BaRs: sources=13, pushTargets=2 (sees 11 Group B peers + 2 cargos)

Root cause: `CubeGrid.EntityId` is also unreliable from background threads — same fundamental issue as `IsConnectedTo`.

**v2 (caller-passed `isSameGrid` flag — REVERTED):** Bypassed `IsConnectedTo` entirely for same-grid blocks by passing an `isSameGrid` flag from the caller. This eliminated the background-thread unreliability but introduced a new bug: blocks on the same grid but NOT conveyor-connected (e.g., two disconnected conveyor networks on one grid) were incorrectly included as sources and push targets. The BaR cannot actually pull/push items from these blocks, so they should be excluded.

**v3 (unified `IsConnectedTo` + cache for all blocks — DONE):** Removed the `isSameGrid` special case entirely. All blocks (same-grid and cross-grid) now go through the unified `IsConnectedTo()` + `ConnectionCache` (15s TTL) path. This correctly excludes blocks that are on the same grid but not conveyor-connected. Cache capacity bumped from 1024 → 2048 to accommodate same-grid blocks. Added `MethodProfiler` instrumentation to `AddIfConnectedToInventory`.

**Files modified:** `Helpers/InventoryHelper.cs`, `NanobotSystem.Scanning.cs`

**Cache staleness tradeoff:** If a conveyor tube is destroyed, cached `true` expires in 15s. During that window, `TransferItemFrom` silently fails (same as pre-fix behavior). Acceptable.

### Fix 5: Custom info panel shows source/push target counts (DONE — deployed)

Added `Sources: N | Push Targets: N` line to the terminal custom info panel for easy in-game verification.

**Files modified:** `NanobotSystem.CustomInfo.cs`

## Completed

- [x] Fix 1: Separate push-target list (cargo-only)
- [x] Fix 2: ShouldStopScan no longer blocks source discovery
- [x] Fix 3: ConnectionCache re-adds inventories on cache hit
- [x] Fix 4 v3: Unified `IsConnectedTo` + cache for all blocks (v1 CubeGrid.EntityId failed; v2 `isSameGrid` bypass reverted — included non-conveyor-connected blocks)
- [x] Fix 5: Custom info panel shows source/push target counts
- [x] Removed `srcEligible`/`srcConnected` diagnostic counters
- [x] Removed dead `_Ignore4*` sets (populated but never consumed since push targets are cargo-only)

## Steps to Reproduce (original bug)

1. Place 20+ BaR blocks on a grid with push settings enabled (all three: ore, components, items).
2. Fill all cargo containers to capacity.
3. Observe: BaRs show `inventoryFull=True` state, items get transferred between BaR inventories cyclically.
4. Run profiler for 120s — `ServerEmptyTransportInventory` will dominate at ~8-10ms per call.
