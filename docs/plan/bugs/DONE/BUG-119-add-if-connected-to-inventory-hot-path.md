# BUG-119: AddIfConnectedToInventory dominates inventory-domain time (1.7 s of 2.1 s total)

## Status: Fixed
## Severity: Medium (touches both background scans and main-thread inventory ops)
## Version: v2.5.4
## Found In: `Helpers/InventoryHelper.cs` `AddIfConnectedToInventory` (and caller `NanobotSystem.Scanning.cs:AsyncScanForSources`)

## Symptom

Profile session `20260429013527-profiling` (120 s, large-ship welding scenario), domain summary:

```
domain=Inventory; calls=6144; totalMs=2085.976; avgMs=0.340
```

Method breakdown:

| Method | Calls | Avg ms | Max ms | Total ms |
|---|---:|---:|---:|---:|
| **AddIfConnectedToInventory** | 2,177 | 0.80 | **14.9** | 1,738 |
| PullComponents | 1,858 | 0.18 | 2.85 | 330 |
| ServerTryPushInventory | 490 | 0.025 | 1.66 | 12 |
| ServerEmptyTransportInventory | 1,129 | 0.003 | 0.11 | 4 |

`AddIfConnectedToInventory` accounts for **83% of all inventory-domain time** — 1 738 ms out of 2 086 ms total. `steadyAvgMs=0.806 vs warmupAvgMs=0.035` — 23× slower in steady state. The 14.9 ms tail is a single-call worst case (engine `IsConnectedTo` walking a deep conveyor graph).

## Root cause (after reading the code)

The doc was written before reading the helper — **a connection cache already existed**. `Helpers/InventoryHelper.cs` had `ConnectionCache` keyed by `(blockId, welderId) → bool` with TTL 15 s. So memoization wasn't missing; the cost came from elsewhere on both paths.

### Cache miss path: TTL too short

Cluster scans and `AsyncScanForSources` fire every ~6 s; TTL was 15 s. Many entries expired between scans, forcing the engine `IsConnectedTo` conveyor walk for every block whose entry had aged past 15 s. On a large-ship grid with 50+ inventory candidates and a deep conveyor network, each miss is hundreds of microseconds to multiple ms.

### Cache hit path: O(n) `List.Contains`

```csharp
if (cachedConnected) {
    var maxInvCached = terminalBlock.InventoryCount;
    for (var i = 0; i < maxInvCached; i++) {
        var inventory = terminalBlock.GetInventory(i);
        if (!possibleSources.Contains(inventory))   // ← O(n) linear scan
            possibleSources.Add(inventory);
    }
}
```

`possibleSources` is a `List<IMyInventory>` that grows during the scan to 50+ entries on large ships. Every cache hit ran a linear scan that lengthened as the scan progressed — exactly the steady-state-vs-warmup asymmetry the profile showed (warmup hits with a near-empty list, steady-state hits scan a full one).

The same O(n) Contains was on the cache-miss path too, before the engine `IsConnectedTo` call.

## Fix

Two-part change in `Helpers/InventoryHelper.cs` and `NanobotSystem.Scanning.cs`:

### 1. Extend cache TTL 15 s → 60 s

```csharp
private static readonly TtlCache<MyTuple<long, long>, bool> ConnectionCache = new TtlCache<MyTuple<long, long>, bool>(
    defaultTtl: TimeSpan.FromSeconds(60),  // was 15
    ...);
```

Cuts the miss rate roughly 4×. Stale entries are harmless: a disconnected inventory just fails its actual transfer later in `PullComponents`, and a newly connected one is picked up on the next refresh (welds run for minutes, so a one-minute discovery delay is invisible to players).

### 2. O(1) HashSet dedup replaces `List.Contains`

Added a reusable `_ScanSourceDedupSet` (`HashSet<IMyInventory>`) field on `NanobotSystem`. `AsyncScanForSources` clears it at scan start and passes it through to `AddIfConnectedToInventory`:

```csharp
// Cache hit path, inside the inventory loop:
if (possibleSourcesSet.Add(inventory))   // returns false if already present (O(1))
{
    possibleSources.Add(inventory);
}
```

Cache-miss path mirrors the same idea (speculatively add to the set, roll back with `Remove` if `IsConnectedTo` rejects). The `possibleSourcesSet == null` branch keeps the helper safe for any future caller that doesn't supply a set.

## Why this is the right scope

- **No invalidation hook needed.** Tried to avoid grid-topology event wiring (`MyCubeGrid.OnBlockAdded/Removed`) — the cost/risk of subscribing globally for a marginal gain wasn't justified. Stale cache entries are functionally harmless given how the data flows downstream.
- **No allocation in the hot path.** The HashSet is a reused field, cleared per scan. No per-call allocations beyond the existing `CacheItem` heap object on miss (already there).
- **Single call site touched.** Grep confirmed `AddIfConnectedToInventory` is invoked only from `AsyncScanForSources`. The doc speculated about other callers (`AsyncClusterScan`, `PullComponents`); those are not actual callers in the current code.
- **Backward-compatible signature.** Helper still works without a HashSet (falls back to `List.Contains`), so future callers don't have to thread the set through.

## Expected impact

- **Cache miss rate**: ~4× reduction (TTL 15 → 60). Each avoided miss skips the engine `IsConnectedTo` walk (~hundreds of µs to ms).
- **Cache hit cost**: O(n) → O(1) per dedup check. With list size ~25 avg during scan, each hit drops by roughly the cost of 25 reference comparisons (~250 ns), summed over thousands of hits.
- **Steady-state total**: target `AddIfConnectedToInventory` total ms drops from ~1 740 ms to roughly **300-500 ms** at the same call volume, dominated by remaining genuine misses on cold blocks.
- **Side effects**: `AsyncScanForSources` and `AsyncClusterScan` get faster proportionally since they're the outer loops (BUG-117/118 partially benefit without their own fix).

## Verification

1. **Build clean** — `dotnet build ... -c Release -v minimal` → 0 warnings, 0 errors. ✓
2. **Re-profile the same scenario** that produced the 1 738 ms total. Look for:
   - `AddIfConnectedToInventory` total ms drops 3-5×.
   - `cached=true` vs `cached=false` ratio in the per-call log shifts strongly toward `true`.
   - Steady-state avg should drop closer to the warmup avg (0.035 ms) — the gap was the steady-state cost of `Contains` against a long list.
3. **Functional regression check**: BaR still discovers all expected source containers when conveyor-connected; placing a new container during a session shows up in the source list within ~60 s.

## Profile reference

- Session: `20260429013527-profiling`
- File: `20260429013527-profiling.NanobotProfiler.AddIfConnectedToInventory.log`

## See also

- BUG-117 — AsyncClusterScan spike (outer caller of source-discovery shape)
- BUG-118 — AsyncScanForSources cost (direct caller; TTL extension also reduces this method's tail)
