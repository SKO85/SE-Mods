# FEAT-016: Cluster pre-sort optimization — eliminate redundant per-member sorts
## Status: Done
## Priority: Medium
## Version: v2.5.0

## Summary
Coordinator pre-sorts cluster scan candidates once, so members skip their expensive local sort in `ApplyClusterResultToSelf`.

## Motivation
Profiling showed `ApplyClusterResultToSelf` averaging 0.6ms across ~680 calls (407ms total over 120s). A significant portion was spent sorting ~500 grind/weld candidates per BaR — redundant work since all 60 cluster members sorted the same shared candidate list independently. The sort accounted for ~10% of total profiled time.

## Design

### Pre-sort in coordinator
After the coordinator collects all candidates in `AsyncClusterScan`, and before publishing the result via `cluster.SetResult()`, a new `PreSortClusterCandidates()` method:
1. Computes squared distances from the coordinator's area center to each candidate block
2. Sorts grind candidates by: autogrind flag → priority → grid size (if applicable) → distance
3. Sorts weld candidates by: priority → distance → grid ID → block position
4. Sets `result.PreSorted = true`

Only runs for multi-member clusters (`memberCount > 1`). Solo BaRs are unaffected.

### Members skip local sort
In `ApplyClusterResultToSelf`, the weld and grind sorts are wrapped in `if (!result.PreSorted)`. When pre-sorted, members only do:
- Range filtering (per-BaR, preserves pre-sorted order)
- Truncation to 128 targets

### Trade-off
Members use the coordinator's distance ordering instead of computing their own. For co-located BaRs (same cluster), distances are nearly identical, so the sort order is a near-perfect approximation. The practical gameplay impact is negligible.

## Profiling results (120s session, 60 cluster members)

| Metric | Before | After | Change |
|--------|--------|-------|--------|
| `ApplyClusterResultToSelf` avg | 0.598ms | 0.359ms | **-40%** |
| `ApplyClusterResultToSelf` total | 407ms | 267ms | **-140ms** |
| `PreSortClusterCandidates` total | — | 5ms | +5ms |
| **Net savings** | | | **~135ms** |

## Files Affected
- `Cluster/ScanClusterResult.cs` — Added `PreSorted` flag
- `NanobotSystem.Scanning.cs` — New `PreSortClusterCandidates()` method, pre-sort call in `AsyncClusterScan`, conditional sort skip in `ApplyClusterResultToSelf`

## Testing
1. Deploy 40+ BaRs on one grid, enable grinding on nearby grids
2. Run `/nanobars profile start 120`
3. Compare `ApplyClusterResultToSelf` avg/total in summary — should be ~0.35-0.40ms avg (down from ~0.60ms)
4. Verify `PreSortClusterCandidates` appears in summary with low total cost (~5ms)
5. Verify grinding behavior is unchanged (correct targets, correct priority order)
