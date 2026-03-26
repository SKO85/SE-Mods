# FEAT-008: Cluster Coordinator — Shared Scanning for Co-located BaR Systems
## Status: Done
## Priority: High
## Version: v2.5.0
## Summary
Groups BaR systems with equivalent scan configurations into clusters. One BaR per cluster (the coordinator) performs the expensive scan once. All cluster members then apply their own range/distance filtering to the shared results. This eliminates N-1 redundant scans per cluster.

## Motivation
With 10 identical BaRs on the same grid, 9 out of 10 scans are redundant — they produce the same target candidates (differing only in range filtering and distance). Grid traversal, block evaluation (ownership/color/safe-zone/shield checks), sorting, and source BFS discovery are all duplicated.

## Design
- **ScanClusterCoordinator** (static): Groups BaRs by cluster key (grid, work mode, search mode, priorities, colors, owner, etc.). Called from `Mod.RebuildSourcesAndTargetsTimer()` on main thread. O(N).
- **ScanCluster**: Cluster instance with member list, coordinator election (lowest EntityId), and atomic shared result storage.
- **ScanClusterResult**: Immutable-after-publication shared output containing `ClusterTargetCandidate` (block + attributes) and `ClusterFloatingCandidate` (entity + world position) lists, plus sources/push targets.
- **All BaRs use the cluster path** — solo BaRs become coordinator of a 1-member cluster (no separate legacy path).
- **Coordinator path**: Performs full scan with `skipRangeCheck = cluster.Members.Count > 1` and increased caps (up to 4x for multi-member), stores in ScanClusterResult, then applies own range/distance filter.
- **Member path**: Reads shared result, applies own IsInRange(), computes distance, sorts, swaps into State.
- **Failsafe**: Members that miss 3 consecutive results fall back to emergency coordinator scan (AsyncClusterScan), publishing results for other members too.

## Thread Safety
- No new locks. All sharing via atomic reference swaps and immutable-after-publication data.
- RebuildClusters() runs on main thread. Background threads capture cluster reference at scan start.

## New Files
- `Cluster/ScanClusterCoordinator.cs`
- `Cluster/ScanCluster.cs`
- `Cluster/ScanClusterResult.cs`

## Modified Files
- `NanobotSystem.cs` — AssignedCluster field, MissedResultCycles counter
- `NanobotSystem.Scanning.cs` — Cluster branching, AsyncClusterScan, AsyncApplyClusterResults, ApplyClusterResultToSelf
- `NanobotSystem.Operations.cs` — Updated immediate-scan call sites to use UpdateSourcesAndTargetsTimer()
- `Mod.cs` — RebuildClusters() integration, Clear() cleanup
- `SKO-Nanobot-BuildAndRepair-System.csproj` — Compile entries

## Refactoring: Legacy Scan Path Removal (v2.5.0)
Removed the duplicate legacy scan path. Previously, solo BaRs (cluster size 1) used a separate code path writing `TargetBlockData` directly, while clustered BaRs used `ForCluster` variants writing `ClusterTargetCandidate`. Now all BaRs route through the cluster path.

**Changes:**
- `ScanClusterCoordinator.RebuildClusters()` — Removed single-member shortcut; all BaRs get assigned to a cluster.
- `UpdateSourcesAndTargetsTimer()` — Removed 3-way branch (legacy/coordinator/member) → 2-way (coordinator/member) with null guard.
- `skipRangeCheck` — Now conditional: `cluster.Members.Count > 1`. Solo BaRs scan with range checks (preserving legacy behavior).
- Deleted 8 legacy methods (~470 lines): `StartAsyncUpdateSourcesAndTargets`, `AsyncUpdateSourcesAndTargets`, `AsyncAddBlocksOfBox`, `AsyncAddBlocksOfGrid`, `ShouldStopScan`, `AsyncAddBlockIfTarget`, `AsyncAddBlockIfWeldTarget`, `AsyncAddBlockIfGrindTarget`.
- Renamed 6 `ForCluster` methods (dropped suffix): `AsyncAddBlockIfTargetForCluster` → `AsyncAddBlockIfTarget`, etc.
- Removed dead `_ContinuouslyError` field.

## Expected Impact
With 10 BaRs (same config): ~82% reduction in total scan CPU (grid traversal, block evaluation, sorting from 10x to 1x per cycle).
