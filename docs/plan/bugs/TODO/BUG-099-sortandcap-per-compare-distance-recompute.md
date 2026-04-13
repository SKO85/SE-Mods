# BUG-099: `SortAndCapGridCandidates` sort comparator recomputes member distances per comparison

## Status: Fixed
## Severity: Medium (background-thread hot spot on large multi-member clusters)
## Version: v2.5.3
## Found In: `NanobotSystem.Scanning.cs` `SortAndCapGridCandidates`

## Description

Discovered in the latest profiling session (`20260413214958-profiling`, 120 s, 58-member cluster, mega-base with ~11.5k grind candidates per scanned grid):

```
method=SortAndCapGridCandidates;domain=Utility;calls=35;totalMs=1133.071;
  avgMs=32.373;minMs=1.980;maxMs=125.124;steadyAvgMs=28.518
```

Individual call traces from the per-method log:

```
ms=77.082  count=11655 effectiveCount=11655 maxKeep=256 kept=256 members=58 partitionMs=4.115  sortMs=72.948
ms=94.805  count=11639 effectiveCount=11639 maxKeep=256 kept=256 members=58 partitionMs=4.796  sortMs=89.984
ms=118.028 count=11601 effectiveCount=11601 maxKeep=256 kept=256 members=58 partitionMs=4.747  sortMs=113.253
ms=125.124 count=11573 effectiveCount=11573 maxKeep=256 kept=256 members=58 partitionMs=5.273  sortMs=119.826
```

The BUG-096 partition runs in **~4 ms** — that part is fine. The **sort** is the hot spot at 70–120 ms per call.

## Root cause

The sort comparator in `SortAndCapGridCandidates` recomputes the min-squared-distance-to-any-member for **both** blocks on **every comparison**:

```csharp
list.Sort(startIndex, effectiveCount, Comparer<ClusterTargetCandidate>.Create((a, b) =>
{
    ...
    var posA = a.Block.CubeGrid.GridIntegerToWorld(a.Block.Position);
    var posB = b.Block.CubeGrid.GridIntegerToWorld(b.Block.Position);
    double distA, distB;
    if (useMemberAware)
    {
        distA = double.MaxValue;
        distB = double.MaxValue;
        for (int i = 0; i < memberCenters.Count; i++)   // iterates all members per compare
        {
            var c = memberCenters[i];
            var dA = (c - posA).LengthSquared();
            if (dA < distA) distA = dA;
            var dB = (c - posB).LengthSquared();
            if (dB < distB) distB = dB;
        }
    }
    ...
}));
```

For `count=11555` and `memberCount=58`:
- `List.Sort` performs ~`n log n` = ~156,000 comparisons
- Each comparison does `58 × 2 = 116` squared-distance calcs plus two `GridIntegerToWorld` transforms
- Total: **~18 million distance calcs per sort**, plus the SE API matrix math

Matches the observed 70–120 ms per sort cleanly. Sim speed dipped to 0.77–0.93 during those scans.

`PreSortClusterCandidates` already avoids this by pre-computing the distances into a `Dictionary<IMySlimBlock, double>` before `list.Sort` and letting the comparator do a cheap lookup. `SortAndCapGridCandidates` was missing the same optimization.

## Fix

**Pool a `Dictionary<IMySlimBlock, double>` as a per-`NanobotSystem` field** (`_sortCandidateDistances`) and **populate it during the BUG-096 partition pass** — which already iterates every candidate and already computes `blockMatrix.Translation` for the OBB intersection test. Adding the inner 58-iteration min-distance loop to the partition body costs ~638k ops (`11k × 58`), replacing ~18M comparator ops. The sort comparator then does two `TryGetValue` calls per comparison instead of 116 squared-distance calcs.

`NanobotSystem.cs` — new field:

```csharp
private Dictionary<IMySlimBlock, double> _sortCandidateDistances = new Dictionary<IMySlimBlock, double>();
```

`NanobotSystem.Scanning.cs` `SortAndCapGridCandidates` partition loop, after the in-range check succeeds:

```csharp
if (inRange)
{
    // Compute min-squared-distance to any member center while the
    // block position is in L1. Used by the sort comparator below.
    var blockPos = blockMatrix.Translation;
    var minDist = double.MaxValue;
    for (int ci = 0; ci < memberCenters.Count; ci++)
    {
        var d = (memberCenters[ci] - blockPos).LengthSquared();
        if (d < minDist) minDist = d;
    }
    _sortCandidateDistances[candidate.Block] = minDist;

    // ... existing swap-to-front code ...
}
```

Cache is `Clear()`ed at the start of the partition loop. Field is per-BaR; `_AsyncUpdateSourcesAndTargetsRunning` ensures only one scan runs per BaR at a time so there's no concurrent access.

Sort comparator reads the cache via a local snapshot (`var distCache = useMemberAware ? _sortCandidateDistances : null;`) to keep the branch out of the hot path:

```csharp
double distA, distB;
if (distCache != null)
{
    distCache.TryGetValue(a.Block, out distA);
    distCache.TryGetValue(b.Block, out distB);
}
else
{
    // Solo BaR path — one squared distance per block, no cache needed.
    var posA = a.Block.CubeGrid.GridIntegerToWorld(a.Block.Position);
    var posB = b.Block.CubeGrid.GridIntegerToWorld(b.Block.Position);
    distA = (center - posA).LengthSquared();
    distB = (center - posB).LengthSquared();
}
return (isGrinding && !grindNearFirst) ? distB.CompareTo(distA) : distA.CompareTo(distB);
```

Solo path is unchanged — the inline single-center distance compute is cheap (1 op per block vs. 58) and caching for solo would just add dict lookup overhead.

## Expected impact

Based on the profiling numbers above:

| Phase | Before | After (expected) |
|---|---|---|
| Partition | ~4 ms | ~10 ms (added 58-iter distance loop) |
| Sort | ~90 ms avg (70–125) | ~6–10 ms |
| **Total per call** | ~94 ms | **~16–20 ms** |

**Roughly 5× speedup** on the 11k-candidate mega-base case. The session had ~12 of these big-sort calls in 120 s; expected total-time drop from ~1 s to ~200 ms on the background scan thread.

The partition becomes slower in absolute terms (~4 ms → ~10 ms), but the overall call is dramatically cheaper because the sort's 2N-member-iterations-per-compare × n log n comparisons is the actual cost center.

Sim speed dips correlated with the big-sort calls (min 0.77 during them). Expected: dips should flatten out.

## Memory cost

`_sortCandidateDistances` grows to hold one entry per in-range candidate on the current grid. On the worst-case 11.5k-block grid, the dict capacity grows to ~16k buckets (~600 KB). Per-BaR field, so 20 BaRs × 600 KB = ~12 MB peak if every BaR also scans a mega-base grid (only coordinators do heavy sorts — most BaRs are cluster members and call `ApplyClusterResultToSelf` instead). Acceptable.

The dict is `Clear()`ed, not re-allocated, between calls — amortized cost is zero after warmup.

## Verification

1. **Build**: `dotnet build ...SKO-Nanobot-BuildAndRepair-System.csproj -c Release -v minimal` → 0 warnings, 0 errors. ✓
2. **Regression — solo BaR farthest-first**: unchanged path (`distCache == null`), sort output identical to pre-fix.
3. **Regression — nearest-first multi-member**: output identical (same comparator ordering, just faster).
4. **Regression — `GrindSmallestGridFirst` multi-member**: output identical (BUG-091 per-grid tiebreaker still runs in `CompareGrindNonDistance`, distance is only the final tiebreaker).
5. **Perf verification**: repeat the 58-member mega-base profile run. Expect:
   - `SortAndCapGridCandidates` avg ms: 28.5 → ~4–6 ms
   - `sortMs` in the per-call log: 70–125 → 6–10 ms
   - `partitionMs` in the per-call log: 3–5 → 9–12 ms (added distance compute)
   - Sim-speed dip during big scans: 0.77–0.93 → should flatten toward 0.95+
6. **Correctness — same-block distance**: background scan creates new `IMySlimBlock` references every cycle. The cache is populated and sorted within a single `SortAndCapGridCandidates` call (same references throughout), so no stale reference issue.

## See also

- **BUG-096** (v2.5.3) — introduced the partition pass this fix piggybacks on.
- **FEAT-070** (v2.5.3) — sort comparator consolidation; the shared helpers are still used, only the distance compute was cached.
- **PreSortClusterCandidates** — already uses the same dict-caching pattern; BUG-099 brings `SortAndCapGridCandidates` into alignment with it.
- Profiling session `20260413214958-profiling` — surfaced the cost.
