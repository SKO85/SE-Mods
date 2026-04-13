# BUG-100: `SortAndCapGridCandidates` comparator still bottlenecked on per-compare `GetPriority` lookups

## Status: Fixed
## Severity: Medium (background-thread hot spot on large multi-member clusters)
## Version: v2.5.3
## Found In: `NanobotSystem.Scanning.cs` `SortAndCapGridCandidates` sort comparator

## Description

Follow-up to BUG-099. Profiling session `20260413220505-profiling` (180 s, 58-member cluster, 4 large grids each with ~9,900 grind candidates):

```
method=SortAndCapGridCandidates; calls=58; totalMs=2,678;
  avgMs=46.2; minMs=33.0; maxMs=67.7; steadyAvgMs=48.2
```

Each scan cycle now sorts **4 different grids** (confirmed via `AsyncAddBlocksOfGrid` log — 4 distinct gridIds scanned 14-15 times each). Per-call trace shows `partitionMs ~6, sortMs ~37-46`. The BUG-099 distance cache is working correctly; the remaining cost is dominated by `BlockGrindPriority.GetPriority` (or `BlockWeldPriority.GetPriority`) lookups inside the comparator.

### Root cause

After BUG-099, each sort comparator call still does:

- `CompareGrindNonDistance` → `BlockGrindPriority.GetPriority(blockA)` + `GetPriority(blockB)`
- Each `GetPriority` = `BlockPriorityHandling.GetItemKey` (TtlCache lookup ~100 ns) + `_PrioHash.TryGetValue` (~30 ns) = **~130 ns per GetPriority call**
- Per compare: **2 × 130 ns = ~260 ns** just on priority lookups

For `count=9,900` candidates: `n log n ≈ 132,000` comparisons × 260 ns = **~34 ms per sort call spent in priority lookups alone**. With 4 big grids per scan cycle × 15 cycles over 180 s, that's **~2,040 ms per session** bottlenecked on cache-friendly but still-not-free lookups.

Distance lookup via the BUG-099 cache: 2 × ~40 ns = 80 ns per compare, adds ~11 ms total. The remainder (~5-10 ms per sort) is partition swap overhead + branch/autogrind checks + the `list.Sort` machinery itself.

## Fix

Same pattern as BUG-099's distance cache. Pre-fetch priority **once per candidate** during the partition pass (the partition already iterates every candidate and already calls into the block's data for the OBB intersection). Store in a pooled per-BaR `Dictionary<IMySlimBlock, int>`. The sort comparator reads the cached value per compare in ~40 ns instead of calling `GetPriority` (~130 ns).

### Field

`NanobotSystem.cs`:

```csharp
// BUG-100: Per-candidate priority cache used by SortAndCapGridCandidates.
private Dictionary<IMySlimBlock, int> _sortCandidatePriorities = new Dictionary<IMySlimBlock, int>();
```

Paired with `_sortCandidateDistances` from BUG-099. Same pooling semantics: one background scan per BaR runs at a time, no concurrent access.

### Populate during partition

`NanobotSystem.Scanning.cs` `SortAndCapGridCandidates` partition loop:

```csharp
_sortCandidateDistances.Clear();
_sortCandidatePriorities.Clear();

// ... partition loop iterates every candidate for OBB intersection ...

if (inRange)
{
    // Existing BUG-099 distance compute (uses blockPos from blockMatrix.Translation)
    _sortCandidateDistances[candidate.Block] = minDist;

    // BUG-100: pre-fetch priority. One call per block here replaces
    // 2 calls per comparison × ~132k comparisons in the sort.
    var priority = isGrinding
        ? BlockGrindPriority.GetPriority(candidate.Block)
        : BlockWeldPriority.GetPriority(candidate.Block);
    _sortCandidatePriorities[candidate.Block] = priority;

    // ... existing swap-into-position code ...
}
```

One `GetPriority` call per in-range candidate = ~9,900 × 130 ns = ~1.3 ms added to the partition. Replaces ~34 ms of in-sort priority lookups. Net ~33 ms saved per call.

### Inlined comparator in `SortAndCapGridCandidates`

The shared helpers `CompareGrindNonDistance` / `CompareWeldPriority` internally call `GetPriority`. To take advantage of the cache, the sort comparator in this ONE hot site inlines a minimal autogrind + cached-priority + cached-distance compare. The shared helpers remain untouched (they're still used by `PreSortClusterCandidates` and `ApplyClusterResultToSelf`, which sort smaller lists where the optimization isn't worth the specialized code path).

```csharp
var distCache = useMemberAware ? _sortCandidateDistances : null;
var priCache  = useMemberAware ? _sortCandidatePriorities : null;
list.Sort(startIndex, effectiveCount, Comparer<ClusterTargetCandidate>.Create((a, b) =>
{
    if (isGrinding)
    {
        // Autogrind-first bucket (inline, no method call).
        var autoMask = TargetBlockData.AttributeFlags.Autogrind;
        var autoA = (a.Attributes & autoMask) != 0;
        var autoB = (b.Attributes & autoMask) != 0;
        if (autoA != autoB) return autoA ? -1 : 1;

        if (!grindIgnorePriority)
        {
            int priA, priB;
            if (priCache != null) {
                priCache.TryGetValue(a.Block, out priA);
                priCache.TryGetValue(b.Block, out priB);
            } else {
                priA = BlockGrindPriority.GetPriority(a.Block);
                priB = BlockGrindPriority.GetPriority(b.Block);
            }
            if (priA != priB) return priA - priB;
        }
        // smallestGridFirst is a no-op for per-grid sort (same grid for all candidates).
    }
    else
    {
        // Weld: priority-only compare.
        int priA, priB;
        if (priCache != null) {
            priCache.TryGetValue(a.Block, out priA);
            priCache.TryGetValue(b.Block, out priB);
        } else {
            priA = BlockWeldPriority.GetPriority(a.Block);
            priB = BlockWeldPriority.GetPriority(b.Block);
        }
        if (priA != priB) return priA - priB;
    }

    // Distance compare (BUG-099 cache, unchanged)
    ...
}));
```

The `priCache != null` check covers both: multi-member clusters (cache populated, use it) and solo BaRs (cache bypass, inline `GetPriority` — which for solo is called at most once per comparison per block on a much smaller list).

## Expected impact (theoretical)

| Phase | After BUG-099 | After BUG-099 + BUG-100 (expected) |
|---|---|---|
| Partition | ~6 ms | ~7 ms (+1 ms for priority pre-fetch, ~9,900 × 130 ns) |
| Sort | ~37 ms | **~12 ms** (-68%, removed ~34 ms of in-sort priority lookups) |
| **Per-call total** | ~46 ms | **~19 ms** |

Session-level impact (180 s, ~15 scan cycles × 4 big grids):
- `SortAndCapGridCandidates` totalMs: 2,678 → **~1,100** (-59%)
- Domain "Utility" total: 13,295 → **~11,700** (-12%)
- Total mod CPU over 180 s: 25,700 → **~24,100** (-6%)

The sim-speed dips correlated with scan cycle bursts (sim min 0.68 at 22:07:00 when a 316 ms scan was finishing) should soften because:
1. Background scan finishes faster → less GC pressure / thread contention
2. Scan-result publication (which holds `State.PossibleGrindTargets` lock briefly) arrives sooner → shorter window for main-thread contention

Main-thread spikes caused by SE API internals (`DecreaseMountLevel` up to 14.9 ms, `RazeBlock` up to 7 ms) are **unaffected** — those are outside mod control. This fix reduces *background* cost only.

## Memory cost

`_sortCandidatePriorities` adds one `int` entry per in-range candidate, per BaR. At 9,900-candidate worst case: `~9.9k × ~20 bytes per entry = ~200 KB` per BaR. Most BaRs are cluster members and never hit `SortAndCapGridCandidates` directly — only coordinators do, typically 1 per cluster. Negligible total footprint.

The dict is `Clear()`ed, not re-allocated, between calls — amortized per-call allocation is zero after warmup.

## Verification

1. **Build**: `dotnet build ... SKO-Nanobot-BuildAndRepair-System.csproj -c Release -v minimal` → 0 warnings, 0 errors. ✓
2. **Regression — solo BaR**: `distCache` and `priCache` snapshots are both `null` on the solo path (`useMemberAware=false`). The sort comparator falls into the `GetPriority` inline fallback and the inline squared-distance fallback — **identical to pre-fix behavior**. Sort output unchanged.
3. **Regression — nearest-first / farthest-first multi-member**: priority and distance cache lookups return the same values the inline code would have computed. Sort order is identical to pre-fix.
4. **Regression — `GrindIgnorePriorityOrder`**: when the flag is set, the comparator skips the priority check entirely. The cache is still populated (trivial cost) but not read. Output unchanged.
5. **Regression — weld sort**: the weld branch now uses the same cache. Priority comparison is `priA - priB` — identical semantics to the helper's `CompareWeldPriority`.
6. **Perf verification**: re-profile session after fix on the same 58-member / 4-large-grid workload. Expected:
   - `SortAndCapGridCandidates` avgMs: 46.2 → **~19** (-59%)
   - `sortMs` in per-call log: 33-46 → **~10-13**
   - `partitionMs` in per-call log: 5-15 → **~6-16** (+1 ms for priority pre-fetch)
   - Total mod CPU over 180 s: 25,700 → **~24,100** (-6%)
   - Sim-speed min during scan bursts: 0.68 → should improve toward 0.80+

## Follow-up already considered

- **Top-K partial sort (heap-based)** instead of full `List.Sort`: could drop sort cost by a further ~40% (O(n log k) vs O(n log n) = 79k vs 132k comparisons). Implementation complexity is high (custom heap in C# 6 with `IEquatable` struct elements) for a ~5 ms per call gain. Not worth it.
- **Single combined sort-key encoding** (autogrind bit + priority packed into an int): eliminates the autogrind bucket branch. Saves ~5 ms per call. Would require a third cache dict or a struct-valued dict. Marginal gain, not worth the complexity.

## See also

- **BUG-099** (v2.5.3) — distance cache; this fix is the same pattern applied to priority lookups, with the measurement loop surfacing the remaining cost.
- **BUG-022** (v2.5.0) — `BlockPriorityHandling` per-block cache that speeds up `GetPriority` internally. Still there; this fix just calls it fewer times.
- **BUG-097** (v2.5.3) — `_PrioHash` race fix; the atomic-swap ensures the pre-fetched priorities are consistent even if the user reorders priorities mid-scan.
- **FEAT-070** (v2.5.3) — sort comparator consolidation. The shared `CompareGrindNonDistance` / `CompareWeldPriority` helpers are still used by `PreSortClusterCandidates` and `ApplyClusterResultToSelf` where the optimization isn't worth the inline complexity.
- Profiling session `20260413220505-profiling` — 180 s, 58 members, 4 large grids, surfaced the remaining sort cost.
