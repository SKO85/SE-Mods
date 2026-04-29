# BUG-118: AsyncScanForSources avg 107 ms / max 303 ms on large-ship worlds

## Status: Fixed (cascading from BUG-119)
## Severity: Medium (background, infrequent firing but very expensive per call)
## Version: v2.5.4
## Found In: `NanobotSystem.Scanning.cs` `AsyncScanForSources`, `Helpers/InventoryHelper.cs` `AddIfConnectedToInventory`

## Symptom

Profile session `20260429013527-profiling` (120 s, large-ship welding scenario):

| Method | Calls | Avg ms | Max ms | Total ms |
|---|---:|---:|---:|---:|
| AsyncScanForSources | 19 | 106.7 | **302.9** | 2,028 |
| AddIfConnectedToInventory | 2,177 | 0.80 | **14.9** | 1,738 |

19 calls × 107 ms avg = the source scan fires every ~6 s and takes 100+ ms each time. `AddIfConnectedToInventory` is called inside this scan; 2 177 calls × ~0.8 ms each = walking the conveyor graph dominated.

## Root cause

The original ticket suggested investigating "do we even need to scan all inventories?" but the verification profile (below) shows the inner predicate `AddIfConnectedToInventory` fully accounted for `AsyncScanForSources`'s cost. There was no additional outer-loop work to optimize — the scan loop itself is just a grid BFS calling the predicate per terminal block. Once the predicate got cheap, the scan got cheap.

## Fix

No changes to `AsyncScanForSources` itself. The fix is BUG-119's two-part change to `AddIfConnectedToInventory`:

1. `ConnectionCache` TTL extended 15 s → 60 s — cuts the cache miss rate roughly 4×, eliminating most engine `IsConnectedTo` conveyor walks per scan.
2. `HashSet<IMyInventory>` dedup replaces the prior O(n) `List.Contains` on both cache-hit and cache-miss paths.

`AsyncScanForSources` was modified only to wire the reusable dedup HashSet through to the predicate (lazy-init `_ScanSourceDedupSet` field, clear/seed at scan start, pass to `AddIfConnectedToInventory`).

## Verification — profile session `20260429144151-profiling`

| Metric | Before (`20260429013527`) | After (`20260429144151`) | Change |
|---|---:|---:|---:|
| Sim-speed avg / min | 0.69 / 0.43 | **1.00 / 0.88** | recovered |
| **AsyncScanForSources total** | **2 028 ms** | **30.6 ms** | **98.5% ↓** |
| **AsyncScanForSources max** | **302.9 ms** | **16.8 ms** | **18× faster** |
| AsyncScanForSources avg | 106.7 ms | 10.2 ms | 10.5× faster |
| AddIfConnectedToInventory total | 1 738 ms | 22.1 ms | 98.7% ↓ |
| AddIfConnectedToInventory steady avg | 0.806 ms | 0.062 ms | 13× faster |
| AddIfConnectedToInventory max | 14.9 ms | 1.98 ms | 7.5× faster |
| Inventory domain total | 2 086 ms | 333 ms | 84% ↓ |

Per-call log inspection (`AsyncScanForSources.log`) confirms the scan walks 2 grids and finds 94 source inventories per call at 2-17 ms — reasonable for the work being done. Cache hit ratio in the verification session was 102/306 (33%), limited by only 3 source scans firing in 120 s and the 60 s TTL spacing between scans (scan 1 fills cache, scan 2 ~38 s later hits, scan 3 ~74 s later hits expired entries again). Hit rate would rise on longer sessions.

## Why no further fix is needed

- 30 ms total over 120 s = 0.025% of background-thread wall-clock. Even at this ship size, AsyncScanForSources is no longer a noticeable cost.
- Per-call max 16.8 ms is well within the budget for a once-per-6-s background task.
- The remaining cost is genuine grid traversal + a small number of cache misses; further optimization (e.g., grid-topology event invalidation to keep the cache warmer across scans) would cut milliseconds off an already-cheap method.

## What this profile *did* expose

`AsyncClusterScan` and `AsyncAddBlocksOfBox` are now the dominant background cost on this scenario (17 calls × ~450 ms avg / ~535 ms max each). That is BUG-117 territory; data is stronger now that BUG-118/119 are out of the way. Different test scenario from the original BUG-117 capture, so direct comparison is risky — recommend re-profiling specifically for cluster scan before acting.

## Profile reference

- Before: `20260429013527-profiling`
- After: `20260429144151-profiling`
- Files: `.AsyncScanForSources.log`, `.AddIfConnectedToInventory.log`, `.Summary.log`

## See also

- BUG-119 — The actual fix that resolved this. Inner predicate cost (`AddIfConnectedToInventory`) accounted for the entire AsyncScanForSources cost.
- BUG-117 — AsyncClusterScan spike, now visibly the next outer-loop bottleneck.
