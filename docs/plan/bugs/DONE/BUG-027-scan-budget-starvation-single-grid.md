# BUG-027: BaRs idle when multiple grids available — scan budget consumed by single large grid
## Status: Fixed
## Severity: High
## Version: v2.5.0
## Found In: `NanobotSystem.Scanning.cs` — `AsyncAddBlocksOfGrid` + `ApplyClusterResultToSelf`
## Description
With 60 BaRs and multiple separate grids to grind (`MaxSystemsPerTargetGrid=20`), only ~20 BaRs grind one grid while the remaining ~40 idle instead of moving to other available grids.
## Steps to Reproduce
1. Place 60 BaRs near 3+ separate grids (each 300+ blocks) to grind
2. Set `MaxSystemsPerTargetGrid=20`
3. Observe only ~20 BaRs grind the first grid; remaining ~40 idle
## Root Cause
Two-layer problem in the scanning pipeline:

**Layer 1 — Scan cap starvation in connected grid traversal:** `AsyncAddBlocksOfGrid` recursively scans grids connected via connectors/rotors. The first connected grid fills the `maxGrind` limit (128 per-grid or 512 total), then `ShouldStopScan` fires — preventing traversal to the next connected grid. Profiler data confirmed: at 11:36:03Z, grid `123259...` reaches 128 grind targets, grid `116926...` (1000 blocks) adds 0.

Initial hypothesis (per-entity cap in `AsyncAddBlocksOfBox`) was wrong — the grids are connected to the home grid via connectors, not separate top-level entities. The cap at the box level had no effect since all connected grids are scanned recursively under one `AsyncAddBlocksOfGrid` call.

**Layer 2 — Truncation homogeneity:** `ApplyClusterResultToSelf` sorts candidates by priority/distance (which clusters same-grid blocks together), then truncates to `MaxPossibleGrindTargets = 128` via simple `RemoveRange`. Even if the pool has blocks from multiple grids, the truncation removes the tail, which contains under-represented grids.

**Result:** Each BaR's `PossibleGrindTargets` contains only blocks from one grid. The grinding loop's `lastRejectedGridId` correctly skips that grid when it's at the 20-BaR limit, but finds nothing else in the list. BaR idles.

Note: The grinding loop itself (`NanobotSystem.Grinding.cs`) is NOT the problem — it already handles multi-grid lists correctly.
## Fix
Three changes in `NanobotSystem.Scanning.cs`:

### 1. Per-grid scan budget in `AsyncAddBlocksOfGrid`
Each grid computes `thisGridMaxGrind = min(maxGrind, grindBefore + MaxPossibleGrindTargets)` — capping what THIS grid's blocks contribute to 128 targets. When the per-grid limit is hit, the block loop skips `AsyncAddBlockIfTarget` but continues iterating for connection traversal (connectors, rotors, pistons). Recursive calls to connected grids pass the ORIGINAL `maxWeld`/`maxGrind`, so `ShouldStopScan` (the global circuit breaker) only fires when the total budget is full. Each connected sub-grid computes its own per-grid cap independently.

Example trace (3 connected grids, budget=512):
- Home grid: `thisGridMaxGrind = min(512, 0+128) = 128`. Adds 0. Traverses connector.
- Grid B: `thisGridMaxGrind = min(512, 0+128) = 128`. Adds 128. `ShouldStopScan(128 < 512)` → false → continues. Traverses connector.
- Grid C: `thisGridMaxGrind = min(512, 128+128) = 256`. Adds up to 128 more. Total: 256.
- All grids represented proportionally.

### 2. Grid-aware truncation via `TruncateGridAware` helper
Replaces both weld and grind `RemoveRange` truncations in `ApplyClusterResultToSelf`. When multiple grids are present, each grid gets a guaranteed minimum of `max(maxCount/numGrids, 4)` slots. Remaining slots fill by sort order. Single-grid lists fall through to simple truncation (no behavioral change).

### 3. Profiler instrumentation
`ApplyClusterResultToSelf` profiler now logs unique grid count: `grindTargets=128(pre=384,grids=3)`.

### Files changed
- `NanobotSystem.Scanning.cs` — all 3 changes
