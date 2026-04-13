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

## Measured impact (re-profile session `20260413222549-profiling`)

Same workload (58-member cluster, 4 large grids, 180 s), re-profiled after the fix landed. The target grid had slightly fewer candidates this session (~8,100-9,100 per scan vs ~9,700-9,976 pre-fix) because grinding had removed some blocks, so direct-number comparisons need workload normalization.

### Sim speed (user-visible)

| | Pre | Post | Delta |
|---|---|---|---|
| avg | 0.96 | **0.99** | +0.03 |
| min | **0.68** | **0.80** | **+0.12** |
| max | 1.10 | 1.08 | -0.02 |

**Min sim speed jumped from 0.68 → 0.80.** This is the headline user-visible improvement — the deepest dip moved from "noticeable stutter" to "barely perceptible". Average is also up, essentially at full speed.

### Main-thread spikes (directly cost sim speed)

| Method | Pre max | Post max | Delta |
|---|---|---|---|
| `ServerTryWeldingGrindingCollecting` | 16.826 ms | **11.830 ms** | **-30%** |
| `ServerTryGrinding` | 15.107 ms | **11.202 ms** | **-26%** |
| `ServerDoGrind` | 14.992 ms | **10.965 ms** | **-27%** |
| `UpdateBeforeSimulation10_100` | 16.954 ms | **11.938 ms** | **-30%** |

**All max spikes dropped ~30%.** The worst single tick went from ~17 ms (over the 16.67 ms per-frame budget, causing a full frame drop) to ~12 ms (under budget, no frame drop). That's the mechanism by which min sim speed improved — scan cycles no longer coincide with SE API spikes to push a tick past the frame budget.

### `SortAndCapGridCandidates` (the direct target)

| | Pre | Post | Delta |
|---|---|---|---|
| calls | 58 | 60 | similar |
| **totalMs** | **2,678** | **2,007** | **-671 (-25%)** |
| **avgMs** | **46.2** | **33.4** | **-12.7 (-28%)** |
| **steadyAvgMs** | **48.2** | **30.6** | **-17.6 (-37%)** |
| **maxMs** | 67.7 | 53.8 | -13.9 (-21%) |

Per-call partition/sort breakdown from the log (pre had `count~9,800`, post had `count~8,600` — workload shrunk ~12% as blocks got ground):

| | Pre @ ~9,800 | Post @ ~8,600 |
|---|---|---|
| partitionMs avg | ~6 | ~5.5 |
| sortMs avg | ~37 | ~27 |

Normalizing post to pre's workload (scale by `n log n` ratio = 1.156): post-normalized sort ≈ 31 ms, which is **~16% faster than pre's 37 ms at same count**. Less than the predicted -68%, more on that below.

### Cascading savings on parent methods (inclusive times)

| Method | Pre totalMs | Post totalMs | Delta |
|---|---|---|---|
| `AsyncClusterScan` | 4,049 | 3,177 | **-872 (-21.5%)** |
| `AsyncAddBlocksOfBox` | 3,725 | 3,002 | **-723 (-19%)** |
| `AsyncAddBlocksOfGrid` | 4,077 | 3,201 | **-876 (-21.5%)** |

### Domain aggregates (180 s session)

| Domain | Pre | Post | Delta |
|---|---|---|---|
| Utility | 13,295 | 10,851 | **-2,444 (-18%)** |
| Scan | 4,336 | 3,553 | **-783 (-18%)** |
| Grind | 2,811 | 2,693 | -118 (-4%) |
| Update | 3,555 | 3,607 | +52 (~flat) |
| Weld | 1,695 | 1,742 | +47 (~flat) |
| Inventory | 406 | 473 | +67 (~flat) |
| **TOTAL** | **26,098** | **22,919** | **-3,179 (-12%)** |

**12% less total mod CPU over the 180 s session.** Background-thread domains (Utility, Scan) carry most of the savings as expected.

### Reality vs. prediction

- Predicted sort savings: ~34 ms per call (removing priority lookups)
- Observed sort savings: ~10 ms per call (workload-normalized ~16%)
- **Actual ≈ 30% of predicted magnitude.**

The priority cache is doing its job — `TryGetValue` replaces `GetPriority` — but the per-lookup cost differential is smaller in practice than theory suggests. Likely contributors:
1. `Dictionary<IMySlimBlock, int>.TryGetValue` with reference-type (interface) keys is slower than estimated because the hash code path goes through virtual dispatch on `IMySlimBlock.GetHashCode`, which may bounce into SE's internal implementation.
2. The `_PrioHash` cache inside `BlockPriorityHandling` was already hot — its lookup cost was probably lower than my ~130 ns estimate after JIT warmup.
3. Cache miss behavior: the dict doesn't fit in L1 and each lookup misses into L2 or L3.

But the **user-visible** metrics (sim speed min +0.12, max spike -30%, session mod CPU -12%) are all strong improvements. The fix shipped the intended direction even if the predicted magnitude was optimistic.

### Remaining optimization headroom (not pursued)

After BUG-099 + BUG-100, the sort comparator's remaining cost is distributed across:
- Dict lookups (priority + distance): ~40 ns × 4 = 160 ns
- Autogrind bit check + branches: ~20 ns
- `CompareTo` on doubles: ~5 ns
- List.Sort machinery per compare: ~20 ns

Total per compare ≈ 200 ns × 132k compares = ~26 ms per sort. Matches observed.

Further optimizations available but below cost/benefit threshold:
- Partial sort (top-K heap): avoid full `n log n`, save ~40% of sort cost.
- Packed sort key (autogrind bit + priority in one int): remove the branch, save ~3 ms per sort.
- Struct-valued dict: combine priority + distance into one lookup, save one `TryGetValue` per compare (~5-8 ms per sort).

None of these are worth the complexity given the workload is now acceptable. The sim speed min of 0.80 is in "healthy" territory and the max spikes are all under-budget.

## Memory cost

`_sortCandidatePriorities` adds one `int` entry per in-range candidate, per BaR. At 9,900-candidate worst case: `~9.9k × ~20 bytes per entry = ~200 KB` per BaR. Most BaRs are cluster members and never hit `SortAndCapGridCandidates` directly — only coordinators do, typically 1 per cluster. Negligible total footprint.

The dict is `Clear()`ed, not re-allocated, between calls — amortized per-call allocation is zero after warmup.

## Verification

1. **Build**: `dotnet build ... SKO-Nanobot-BuildAndRepair-System.csproj -c Release -v minimal` → 0 warnings, 0 errors. ✓
2. **Regression — solo BaR**: `distCache` and `priCache` snapshots are both `null` on the solo path (`useMemberAware=false`). The sort comparator falls into the `GetPriority` inline fallback and the inline squared-distance fallback — **identical to pre-fix behavior**. Sort output unchanged.
3. **Regression — nearest-first / farthest-first multi-member**: priority and distance cache lookups return the same values the inline code would have computed. Sort order is identical to pre-fix.
4. **Regression — `GrindIgnorePriorityOrder`**: when the flag is set, the comparator skips the priority check entirely. The cache is still populated (trivial cost) but not read. Output unchanged.
5. **Regression — weld sort**: the weld branch now uses the same cache. Priority comparison is `priA - priB` — identical semantics to the helper's `CompareWeldPriority`.
6. **Perf verification** — ✓ verified against re-profile session `20260413222549-profiling` (see Measured Impact section). Actual numbers:
   - `SortAndCapGridCandidates` avgMs: 46.2 → **33.4** (-28%, less than predicted ~59% — see reality-vs-prediction notes above)
   - `sortMs` in per-call log: 33-46 → **~18-48** (avg ~27)
   - `partitionMs` in per-call log: ~6 → **~5.5** (roughly flat; priority pre-fetch cost absorbed in noise)
   - Total mod CPU over 180 s: 26,098 → **22,919** (-12%)
   - **Sim-speed min: 0.68 → 0.80** ← headline win
   - **Main-thread max spikes: all down ~30%** (16.826 → 11.830 ms for `ServerTryWeldingGrindingCollecting`)

## Follow-up already considered

- **Top-K partial sort (heap-based)** instead of full `List.Sort`: could drop sort cost by a further ~40% (O(n log k) vs O(n log n) = 79k vs 132k comparisons). Implementation complexity is high (custom heap in C# 6 with `IEquatable` struct elements) for a ~5 ms per call gain. Not worth it.
- **Single combined sort-key encoding** (autogrind bit + priority packed into an int): eliminates the autogrind bucket branch. Saves ~5 ms per call. Would require a third cache dict or a struct-valued dict. Marginal gain, not worth the complexity.

## See also

- **BUG-099** (v2.5.3) — distance cache; this fix is the same pattern applied to priority lookups, with the measurement loop surfacing the remaining cost.
- **BUG-022** (v2.5.0) — `BlockPriorityHandling` per-block cache that speeds up `GetPriority` internally. Still there; this fix just calls it fewer times.
- **BUG-097** (v2.5.3) — `_PrioHash` race fix; the atomic-swap ensures the pre-fetched priorities are consistent even if the user reorders priorities mid-scan.
- **FEAT-070** (v2.5.3) — sort comparator consolidation. The shared `CompareGrindNonDistance` / `CompareWeldPriority` helpers are still used by `PreSortClusterCandidates` and `ApplyClusterResultToSelf` where the optimization isn't worth the inline complexity.
- Profiling session `20260413220505-profiling` — 180 s, 58 members, 4 large grids, surfaced the remaining sort cost.
