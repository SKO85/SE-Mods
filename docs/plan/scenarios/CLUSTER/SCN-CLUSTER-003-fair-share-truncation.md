# SCN-CLUSTER-003: Fair-share target truncation across grids
## Area: CLUSTER
## Tier: 3
## Preconditions
- One BaR, BoundingBox, area covering one 2000-block damaged grid and four 20-block damaged grids.
- `MaxPossibleWeldTargets = 1024` (constant).
## Steps
1. Enable the BaR; with DebugMode on read `LastScan: W=<n>` and the profiler `ApplyClusterResultToSelf`
   details (`weldTargets=N(pre=,grids=)`).
2. Watch which grids receive welds in the first 60 s.
## Expected outcome
- After the cap, every grid keeps at least `max(1024 / 5, 4)` of its candidates (`TruncateGridAware`
  fair share) — the four small grids are fully represented, the big grid fills the overflow; the
  small grids are not starved by the big one (BUG-027).
- Pre-truncation the per-grid sort keeps the best candidates (`SortAndCapGridCandidates`), so the
  kept set for the big grid is its top-priority/nearest blocks, not scan order.
## Perf envelope
`TruncateGridAware` maxMs ≤ 2 for 5000 candidates (pooled buffers, no allocation).
## Coverage
planned: unit:TruncateGridAwareTests.FairShareAcrossGrids (second wave — needs `new NanobotSystem()` off-game)
## Last verified
never
## Related
BUG-027, BUG-094, BUG-098, FEAT-040, FEAT-260910.3
