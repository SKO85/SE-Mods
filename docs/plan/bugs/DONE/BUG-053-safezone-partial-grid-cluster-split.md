# BUG-053: BaRs on partially safe-zone-covered grids don't split into separate clusters
## Status: Done
## Severity: High
## Version: v2.5.0
## Found In: In-game testing — dedicated server

## Description

When a grid partially overlaps a safe zone, some BaR blocks on the grid are inside the safe zone and others are outside. BUG-040 added `SafeZoneAllowsWelding`/`SafeZoneAllowsGrinding` to the cluster key so that BaRs inside vs outside safe zones get separate coordinators.

However, BaRs that should work (outside the safe zone) were sometimes grouped with BaRs inside the safe zone due to stale safe zone state, causing them to stop working. Additionally, `proj.Build()` operates at the projector (not the BaR), so projectors inside safe zones fail to build even when the requesting BaR is outside.

## Root Cause

Two issues:

1. **Timing gap**: `SetSafeZoneAndShieldStates()` ran per-BaR every 2s with unsynchronized timers, while `RebuildClusters()` ran every 1s. When a safe zone was placed/expanded, BaRs updated their state at different times, leaving stale cluster keys that grouped BaRs incorrectly.

2. **Projector-level safe zone**: Even with correct cluster splitting, a coordinator outside a safe zone could discover a projector inside the safe zone (same grid). The coordinator's `State.SafeZoneAllowsBuildingProjections = true` (it's outside), so the projected blocks passed the gate. BaRs tried to build via `proj.Build()`, but the game engine blocked the projector (inside the safe zone), consuming/destroying the projection without placing a real block.

## Fix

### 1. Timing fix: centralized safe zone state update
In `Mod.RebuildSourcesAndTargetsTimer()`, added a loop that calls `SetSafeZoneAndShieldStates()` for all ready BaRs immediately before `RebuildClusters()`. This ensures cluster keys always reflect current safe zone state — no stale data from unsynchronized per-BaR timers. Skipped when no safe zones exist (fast path).

### 2. Projector-level safe zone check (defense in depth)
In `AsyncAddBlocksOfGrid`, after the coordinator-level `SafeZoneAllowsBuildingProjections` gate, added a check: if the projector block's world position is inside a safe zone that blocks building projections, skip the entire projector. Uses `SafeZoneHandler.IsBuildingBlockedAtPosition()` (point-in-sphere test against all active safe zones).

### Files Changed

- `Mod.cs` — centralized safe zone state refresh before `RebuildClusters()`
- `NanobotSystem.State.cs` — `SetSafeZoneAndShieldStates()` changed from `private` to `internal`
- `NanobotSystem.Scanning.cs` — added projector-level safe zone check in `AsyncAddBlocksOfGrid`
- `Handlers/SafeZoneHandler.cs` — added `IsBuildingBlockedAtPosition(Vector3D)` helper
