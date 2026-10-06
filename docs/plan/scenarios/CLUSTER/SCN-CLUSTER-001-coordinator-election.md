# SCN-CLUSTER-001: Coordinator election and shared scan results for co-located BaRs
## Area: CLUSTER
## Tier: 3
## Preconditions
- 5 BaRs on one rig with identical settings (same work/search mode, flags, priorities, colours,
  owner), 50 damaged armor blocks, stocked cargo. DebugMode on for the panel cluster lines.
## Steps
1. Enable all 5; wait 5 s; read `Cluster: <hash> | Members: 5` and `Coordinator: <name>` on each panel.
2. Profile 60 s; compare `AsyncClusterScan.calls` with `ApplyClusterResultToSelf.calls`.
3. Disable the coordinator; read the panels again. Change one member's `WorkMode`; read again.
4. `/nanobars debug cluster-area` (local game) to see the per-member working areas.
## Expected outcome
- All 5 share one `AssignedCluster` (same `ClusterKey` hash), one coordinator scans
  (`AsyncClusterScan` ≈ runS / `TargetsUpdateInterval`), the others apply the shared result
  (`ApplyClusterResultToSelf` ≈ 4× the scans) and filter it to their own area.
- Disabling the coordinator re-elects within one rebuild (2 s); the member with a different `WorkMode`
  leaves the cluster (different key hash) and scans solo; `_rescanForced` survives a reshuffle
  (BUG-260501.1); a member that misses 3 result cycles falls back to solo (`MissedResultCycles`).
- Distant members are not starved (BUG-088); a safe-zone-split grid splits the cluster (BUG-053).
## Perf envelope
`AsyncClusterScan` maxMs ≤ 20 (background); `ApplyClusterResultToSelf` steadyAvgMs ≤ 1;
`ScanClusterCoordinator.RebuildClusters` maxMs ≤ 2 with 20 BaRs.
## Coverage
ingame:S07
## Last verified
never
## Related
FEAT-008, BUG-053, BUG-088, BUG-095, BUG-109..111, BUG-117, BUG-260501.1, BUG-260610.13, FEAT-260910.2
