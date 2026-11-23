# SCN-CLUSTER-004: Scan caches return the right data and expire

## Area: CLUSTER
## Tier: 2
## Preconditions
- The scan caches shared by all BaRs: SharedEntityCache (entities in a box, 4 s, a hit needs the cached box to contain
  the requested one), SharedGridBlockCache (a grid's block list, keeps the partial list when the grid changes mid-read),
  GridScanCache (per-grid weld / grind candidates, keyed by grid and scan parameters, 3 s).
## Steps
1. Query / store / expire / invalidate through each cache.
## Expected outcome
- Repeated identical requests within the TTL reach the engine once; a contained box hits, a larger or distant box misses;
  results are copies; old entries are evicted.
- Grid-scan entries only match the same scan parameters; empty results can be dropped after new damage.
## Perf envelope
Engine calls saved (hit / miss counts), not measured in ms here.
## Coverage
unit:CacheTests
## Last verified
v2.5.7 build 261123.19 2026-11-23 — unit green.
## Related
BUG-149, BUG-260610.8, BUG-260824.6, BUG-261122.10
