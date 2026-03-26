# FEAT-019: AsyncAddBlocksOfBox scan optimization
## Status: Done (Won't Fix — Superseded)
## Priority: Medium
## Resolution: Key optimization (early-out via ShouldStopScan) already implemented. Remaining micro-optimizations not worth the complexity given 7.9ms avg cost.
## Version: v2.5.0

## Summary
Reduce the cost of `AsyncAddBlocksOfBox` (199ms total, 7.9ms avg per call) — the second largest background scan cost.

## Motivation
Profiling (120s, 60 BaRs) shows `AsyncAddBlocksOfBox` running 25 times at 7.9ms steady avg (199ms total). It enumerates entities in the bounding box and calls `AsyncAddBlocksOfGrid` for each discovered grid. This is the largest scan cost after `AsyncClusterScan` itself.

## Design
Options to investigate:
1. **Early-out when target caps reached** — if `clusterWeldTargets` and `clusterGrindTargets` are already at max capacity, skip remaining entities.
2. **Entity-level empty cache** — similar to FEAT-017's grid-level empty cache, but skip entire entities that previously yielded no targets.
3. **Skip entity iteration for known grids** — if all grids in the bounding box are already in the `grids` list (scanned via connections from own grid), skip the bounding box entity enumeration entirely.

## Profiling baseline (120s, 60 BaRs)
| Metric | Value |
|--------|-------|
| AsyncAddBlocksOfBox calls | 25 |
| AsyncAddBlocksOfBox total | 199ms |
| AsyncAddBlocksOfBox steady avg | 7.9ms |

## Files Affected
- `NanobotSystem.Scanning.cs` — `AsyncAddBlocksOfBox`

## Testing
1. Deploy 40+ BaRs with BoundingBox search mode, nearby grids in range
2. Run `/nanobars profile start 120`
3. Compare `AsyncAddBlocksOfBox` total/avg with baseline
4. Verify all grids in range are still discovered and targeted correctly
