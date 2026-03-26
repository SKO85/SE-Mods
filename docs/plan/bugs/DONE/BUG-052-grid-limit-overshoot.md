# BUG-052: MaxSystemsPerTargetGrid limit overshoot — 30+ BaRs on one grid with limit 20
## Status: Fixed
## Severity: High
## Version: v2.5.0
## Found In: In-game testing — DEBUG HUD showed 30+ welding on a single target grid

## Description

With `MaxSystemsPerTargetGrid=20`, more than 20 BaRs could end up welding on the same grid. The limit was not enforced correctly.

## Root Cause

Two issues combined:

1. **Stale cache:** `GridSystemCountCache` only rebuilt every 5 frames (FEAT-048 throttle). Between rebuilds, an entire stagger group (~33 BaRs with 100 total) could all pass the stale limit check simultaneously and acquire targets on the same grid.

2. **Lock-on bypass:** Once a BaR acquired a lock-on (`CurrentWeldingBlock`), the grid limit check was bypassed entirely (lines 121-124 in Welding.cs). The comment said "don't let stale counts cause it to be abandoned mid-weld." This made the overshoot permanent — BaRs that got in during the stale window never got evicted.

## Fix

1. **Live counter:** Replaced the periodic `GridSystemCountCache` (Dictionary rebuilt every 5 frames) with a live `ConcurrentDictionary<long, int>` (`Mod.GridSystemCount`). The counter increments/decrements in the `CurrentWeldingBlock` and `CurrentGrindingBlock` property setters. No rebuild needed, no stale data.

2. **Lock-on grid limit:** Lock-on blocks now also check the grid limit. With the live counter, the data is always accurate — no need to bypass. When a lock-on block's grid exceeds the limit, the BaR releases the lock-on and finds a target on another grid.

### Files changed:
- `Mod.cs` — Replaced GridSystemCountCache with GridSystemCount + IncrementGridCount/DecrementGridCount
- `Models/SyncBlockState.cs` — Added counter updates to CurrentWeldingBlock/CurrentGrindingBlock setters
- `NanobotSystem.Operations.cs` — Removed BuildGridSystemCountCache call, updated references
- `NanobotSystem.Welding.cs` — Added grid limit check in lock-on branch
