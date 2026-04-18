# FEAT-077: Projector cold-start detection
## Status: In Progress
## Priority: Medium
## Version: v2.5.4
## Summary
Detect buildable projected blocks during idle to trigger an immediate scan, reducing the cold-start delay from 20s+ to 1-2s when a player places the first block of a projection.
## Motivation
With FEAT-071 idle backoff (20s) and FEAT-075 saturated scan skip, idle BaRs can wait 20-60s before discovering new buildable projected blocks. Players place a block, the projection becomes buildable, but the BaR doesn't react.
## Design
- `HasBuildableProjectorOnGrid()`: checks projectors on own grid + connected grids + BoundingBox entities
  - Phase 1: BFS traversal of connected grids via mechanical connections and connectors, checks `IMyProjector.BuildableBlocksCount > 0`
  - Phase 2 (BoundingBox mode): `GetTopMostEntitiesInBox` for unconnected grids in working area
- Called once per 1-second timer tick, only when: coordinator, idle (consecutiveEmptyScans >= 1), WorkMode != GrindOnly, AllowBuild enabled
- On detection: resets `_consecutiveEmptyScans = 0` and sets `_rescanForced = true`
## Files Affected
- `NanobotSystem.Scanning.cs` — `HasBuildableProjectorOnGrid()`, check in `UpdateSourcesAndTargetsTimer`
