# FEAT-040: Background Scan Cost Scaling for Large Grids

## Status: Done
## Priority: Low
## Version: v2.5.0

## Summary

Investigate and reduce background scan costs (`AsyncClusterScan`, `AsyncAddBlocksOfGrid`, `AsyncAddBlocksOfBox`) that scale poorly with large grids.

## Motivation

Profiling across two sessions shows background scan costs increase dramatically when a large grind grid is present:

| Method | Session 1 (weld) | Session 2 (grind+large grid) |
|--------|-----------------|------------------------------|
| `AsyncAddBlocksOfGrid` calls | 99 | **586** (+6x) |
| `AsyncAddBlocksOfGrid` max | 26.7ms | **98.3ms** |
| `AsyncClusterScan` max | 62.5ms | **144.4ms** |
| `AsyncAddBlocksOfBox` max | 44.1ms | **131.2ms** |

A single `AsyncClusterScan` at 144ms holds a background thread for 9 frames worth of time. While this doesn't directly affect sim speed (background thread), it can cause:
- Thread pool starvation if multiple scans queue up
- Delayed scan results for other BaR clusters
- Increased memory pressure from larger intermediate lists

### Profiling Evidence (Session 2)

```
method=AsyncClusterScan;calls=33;totalMs=1199.312;avgMs=36.343;maxMs=144.431
method=AsyncAddBlocksOfGrid;calls=586;totalMs=1504.254;avgMs=2.567;maxMs=98.258
method=AsyncAddBlocksOfBox;calls=33;totalMs=795.197;avgMs=24.097;maxMs=131.155
```

## Design

This is primarily an investigation ticket. Potential approaches:

### Option A: Chunked Grid Scanning

For grids over a threshold size (e.g., 500+ blocks), split `AsyncAddBlocksOfGrid` across multiple background ticks instead of scanning the entire grid in one pass.

### Option B: Incremental Scan for Grinding

When grinding a grid, blocks are removed over time. Instead of re-scanning the full grid each cycle, maintain a delta list — remove ground blocks from the cached list without full rescan.

### Option C: Scan Interval Scaling

Increase the scan interval for large grids (e.g., 4s instead of 2s for grids with 1000+ blocks). The grid changes slowly relative to its size.

## Files Affected

| File | Change |
|------|--------|
| `NanobotSystem.Scanning.cs` | Scan interval/chunking logic |
| `Utils/SharedGridBlockCache.cs` | Incremental cache updates |
| `Utils/ScanCoordinator.cs` | Grid size awareness |

## Testing

1. Profile with a 2000+ block grid being ground. Compare scan times before/after.
2. Verify scan results remain accurate (no missed blocks, no stale entries).
3. Verify thread pool doesn't starve — other BaR clusters still get scanned promptly.
4. Monitor `AsyncClusterScan` max time — target: under 80ms per scan.
