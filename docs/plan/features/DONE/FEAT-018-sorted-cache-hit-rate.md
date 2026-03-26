# FEAT-018: Improve sorted block cache hit rate
## Status: Deferred
## Priority: Low
## Version: v2.5.0

## Summary
Increase the `GetBlocksFromCache` hit rate to reduce redundant `SortWithPriorityAndDistance` calls.

## Decision: Deferred (2026-03-17)
Re-profiling (120s, 60 BaRs) showed the total cost is too low to justify changes:

| Metric | Old Baseline | New Baseline |
|--------|-------------|-------------|
| GetBlocksFromCache calls | 41 | 53 |
| SortWithPriorityAndDistance calls | ~28 | 25 |
| SortWithPriorityAndDistance total | 46ms | 33.4ms |
| SortWithPriorityAndDistance avg | 1.63ms | 1.34ms |

**Why deferred:**
1. Total sort cost is only 33ms over 120s (0.03% of session time) — negligible impact.
2. Actively-ground grids genuinely have different block lists each cycle, so caching stale sorted lists would return blocks that no longer exist.
3. Stable grids benefit least because their sort cost is already small (~1.5ms).
4. Increasing TTL risks making BaRs feel slow or unresponsive (warm-up delay).
5. The cache TTL (16-20s) vs actual scan interval (~20-25s) structurally prevents hits, but fixing this trade-off isn't worth the complexity given the low absolute cost.

## Original Design Options
1. **Increase TTL** — e.g. 30-40s base instead of 16s. Trade-off: longer stale data.
2. **Adaptive TTL** — longer TTL for stable grids, shorter for actively-ground grids.
3. **Share sorted cache across cluster members** — coordinator pre-sorts, members reuse.

## Files Affected
- `NanobotSystem.Scanning.cs` — `GetBlocksFromCache`, `SortedCacheTtlSeconds`
- `NanobotSystem.cs` — `SortedCacheTtlSeconds` constant
