# BUG-096: BUG-094 follow-ups — farthest-first picks unreachable blocks; cluster cap no longer enforced

## Status: Fixed
## Severity: High
## Version: v2.5.3
## Found In: `NanobotSystem.Scanning.cs` — `SortAndCapGridCandidates` + `AsyncAddBlocksOfGrid` (follow-ups to BUG-094's `int.MaxValue` gate change)

## Description

Two distinct follow-ups to **BUG-094** (v2.5.3). BUG-094 replaced the per-block cap gate (`clusterGrindTargets.Count < maxGrind`) with `int.MaxValue` so every qualifying block on a scanned grid reaches the per-grid sort. That fix was correct in isolation, but it exposed two latent problems:

### Issue A — Farthest-first in multi-member cluster selects unreachable blocks

With a multi-member BaR cluster (20 BaRs in the reported case) and `GrindNearFirst` off (farthest-first), grinding on a large target grid stops after a short time and most members of the cluster go idle — only 3-5 out of 20 are actively grinding. Re-pasting the target grid **and** enlarging the BaR working area restarts grinding on the very same blocks that were previously ignored.

### Issue B — Cluster-wide per-type cap no longer enforced across scanned grids (Codex review)

When only one target type has work (grinding-only mode with welding still enabled, or vice versa), `clusterWeldTargets`/`clusterGrindTargets` grows past `maxWeld`/`maxGrind` across scanned grids. `ShouldStopScan` only stops when **both** lists are full, and BUG-094's `int.MaxValue` gate removed the per-block protection. `PreSortClusterCandidates` and `ApplyClusterResultToSelf` then pay unbounded O(n log n) cost each scan cycle on bases with many connected grids, causing scan-time spikes on large setups.

## Steps to Reproduce

1. Cluster of 20 BaRs on one base, default settings.
2. Target grid large enough that its grindable blocks (grind-color or janitor relation) extend beyond the combined working areas of the cluster.
3. In a BaR terminal: disable **Grind Near First** (farthest-first).
4. Start grinding. Observe: a handful of BaRs grind briefly, then the cluster goes idle — `/nanobars debug show` or the custom info panel reports 0 grind targets on most members even though thousands of grindable blocks remain on the target grid inside the visible work area.
5. Confirm diagnosis: enlarge each BaR's working area (or re-paste the target grid closer). Grinding resumes immediately on the same physical blocks that were previously ignored.

## Root Cause — Issue A (farthest-first picks unreachable)

Two pieces in `NanobotSystem.Scanning.cs` combine into Issue A:

1. **Coordinator scan skips per-block range checks in multi-member clusters** (line 845 / 871):
   ```csharp
   var skipRangeCheck = cluster.Members.Count > 1;
   ...
   AsyncAddBlocksOfGrid(..., clusterGrindTargets, maxWeld, maxGrind, skipRangeCheck);
   ```
   This is the correct performance optimization — each member applies its own `IsInRange` in `ApplyClusterResultToSelf`, so the coordinator doesn't need to filter per-block. But it means `clusterGrindTargets` receives **every qualifying block on the target grid, including ones well outside any member's working area**.

2. **Per-grid sort scores candidates by `min(distance to any member center)`** (`SortAndCapGridCandidates`, lines 1357-1375):
   ```csharp
   if (useMemberAware)
   {
       distA = double.MaxValue;
       distB = double.MaxValue;
       for (int i = 0; i < memberCenters.Count; i++)
       {
           var c = memberCenters[i];
           var dA = (c - posA).LengthSquared();
           if (dA < distA) distA = dA;
           ...
       }
   }
   return (isGrinding && !grindNearFirst) ? distB.CompareTo(distA) : distA.CompareTo(distB);
   ```
   For **farthest-first** (`!grindNearFirst`), the comparator keeps the block with the *largest* min-distance-to-any-member. When the raw input includes out-of-range blocks (because of step 1), the kept top-256 is by construction the 256 blocks that are farthest from every cluster member — i.e., the blocks **nobody can reach**.

3. Each member then runs `ApplyClusterResultToSelf` which filters the shared result through its own `IsInRange(ref areaOrientedBox)`. The 256 candidates fail every member's range test, so `_TempPossibleGrindTargets` stays empty and the member idles.

The "3-5 of 20 grinding" detail is the cluster members positioned closest to the grid's far edge — the only ones whose working areas happen to still intersect the retained far-end blocks.

### History

- **BUG-086 (v2.5.1)** and **BUG-091 (v2.5.2)** added per-grid sort + spatial tiebreakers on top of the (then-broken) BUG-094 cap gate. Their fixes were correct individually but operated on a cache-order prefix.
- **BUG-094 (v2.5.3)** removed the cap-gate short-circuit so the sort now sees the full qualifying set. For nearest-first and solo BaRs this is strictly better; for farthest-first in multi-member clusters it exposed BUG-096 because the sort is now given the unreachable blocks too.

Before v2.5.3 the cap-gate arbitrary-prefix selection *accidentally* included some reachable blocks, which masked Issue A.

## Root Cause — Issue B (unbounded cluster growth)

`ShouldStopScan` (line 349) returns true only when **all** candidate lists are at their cap:
```csharp
var weldFull = weldCandidates == null || weldCandidates.Count >= maxWeld;
var grindFull = grindCandidates == null || grindCandidates.Count >= maxGrind;
var floatingFull = floatingCandidates == null || floatingCandidates.Count >= maxFloat;
return weldFull && grindFull && floatingFull;
```

If welding is enabled but the workload has no welds, `clusterWeldTargets.Count` stays at 0, `weldFull = false`, and `ShouldStopScan` always returns false. Pre-BUG-094 this didn't matter because `AsyncAddBlockIfTarget`'s per-block gate stopped adding to `clusterGrindTargets` once it reached `maxGrind`, capping cluster total.

Post-BUG-094, the per-block gate is `int.MaxValue` (disabled), so `clusterGrindTargets` keeps growing on every scanned grid. The per-grid sort at line 566-568 only trims each **grid's own** contribution to 256 (`MaxPossibleGrindTargets`) — the cluster total is `N_grids × 256`, unbounded by `maxGrind`.

For a 10-grid base: cluster list reaches 2560 entries vs. the intended cap of 1024 (solo) or 4096 (16-member cluster). Downstream cost in `PreSortClusterCandidates` (line 939) and `ApplyClusterResultToSelf` (line 1381) scales O(n log n) on the oversized list. On large bases with many connected grids this produces scan-time spikes.

## Fix

`NanobotSystem.Scanning.cs` — four changes. Issue A is addressed by a pre-sort partition; Issue B by a per-type cluster cap at grid entry. Both are needed.

### 0. Per-type cluster cap at grid entry (Issue B)

**`AsyncAddBlocksOfGrid`** — at grid entry (after `weldBefore`/`grindBefore` capture, before the existing scenario/immunity check on `grindTargetsForGrid`), null the per-type target list for this grid's scan if its cluster cap is already hit:

```csharp
var weldCapSkipped = false;
var weldTargetsForGrid = clusterWeldTargets;
if (weldTargetsForGrid != null && weldTargetsForGrid.Count >= maxWeld)
{
    weldTargetsForGrid = null;
    weldCapSkipped = true;
}

var grindCapSkipped = false;
var grindTargetsForGrid = clusterGrindTargets;
if (grindTargetsForGrid != null && grindTargetsForGrid.Count >= maxGrind)
{
    grindTargetsForGrid = null;
    grindCapSkipped = true;
}
// existing scenario/immunity/destructible checks on grindTargetsForGrid follow
```

Replace the `AsyncAddBlockIfTarget` call (line 475) to pass `weldTargetsForGrid` instead of `clusterWeldTargets`. `int.MaxValue` stays for the per-block maxWeld/maxGrind args — BUG-094's fix for per-grid sort quality is preserved. When `weldTargetsForGrid` is null the per-block path is a silent no-op for welds; the grind side still collects the full grid's qualifying set for the per-grid sort.

Replace the projector/projected-blocks section (lines 510-559) to use `weldTargetsForGrid` in both the null check and the `Add`/`Count < maxWeld` guard. The per-projector inner cap check (`weldTargetsForGrid.Count < maxWeld`) stays — it bounds this grid's own projected contribution as the count climbs during the loop.

**Empty-grid-cache safety guard** at line 579: do not mark a grid empty when we cap-skipped either type at entry — we literally did not evaluate targets for the capped type, so marking it empty would evict it from scanning for `EmptyGridRescanDelaySeconds` even after the cluster cap frees:

```csharp
if (weldAfter == weldBefore && grindAfter == grindBefore && !weldCapSkipped && !grindCapSkipped)
{
    _EmptyGridCache[gridEntityId] = playTime;
}
else if (weldAfter != weldBefore || grindAfter != grindBefore)
{
    TimeSpan dummy;
    _EmptyGridCache.TryRemove(gridEntityId, out dummy);
}
```

#### Why grid-entry cap, not per-block cap

Option considered and rejected: reinstate `maxWeld`/`maxGrind` in the per-block `AsyncAddBlockIfTarget` call. That would directly regress BUG-094 — the cap would fire mid-iteration on a large grid and leave the per-grid sort operating on an arbitrary cache-order prefix again.

The grid-entry cap strikes the right balance: every scanned grid still feeds its full qualifying set to the per-grid sort (BUG-094 preserved), and grids scanned after a type's cap is hit contribute nothing for that type (cluster total bounded). Tradeoff: grids scanned later are all-or-nothing for the capped type; users relying on "farthest" across many grids may prefer to increase working area or reduce `MaxSystemsPerTargetGrid` if they hit the cap frequently. This matches pre-BUG-094 behavior in spirit but without breaking per-grid sort quality.

### 1. Snapshot each cluster member's full working-area OBB (not just centers)

**`NanobotSystem.cs`** — add field:
```csharp
// BUG-096: Snapshot of each cluster member's full working-area OBB. Captured in
// parallel with _ClusterMemberAreaCenters so SortAndCapGridCandidates can drop
// candidates that no member can actually reach before it applies farthest-first
// sorting — without this, farthest-first on a grid extending beyond the cluster's
// reach deliberately kept the blocks nobody could weld/grind and starved the members.
private List<MyOrientedBoundingBoxD> _ClusterMemberAreaBoxes;
```

**`AsyncClusterScan`** (lines 852-868) — populate alongside `_ClusterMemberAreaCenters` inside the same `if (memberCount > 1)` block. The `memberBox` is already constructed for the center computation; just add it to the new list.

**Cleanup** (line 963-966) — clear both lists in the `finally` block.

### 2. Partition unreachable candidates to the back of the subrange before sorting

**`SortAndCapGridCandidates`** (line 1323): before the `list.Sort` call, when `useMemberAware && _ClusterMemberAreaBoxes` is non-empty, run an in-place partition that moves blocks intersecting at least one member OBB to the front `[startIndex, startIndex + effectiveCount)` and leaves the unreachable blocks in the tail `[startIndex + effectiveCount, startIndex + count)`.

Short-circuit: if `effectiveCount == 0` (nothing reachable from this grid), `RemoveRange(startIndex, count)` and return — the unreachable blocks would have been dropped by every member's `IsInRange` anyway, and dropping them here frees cluster slots.

Sort only the in-range portion: `list.Sort(startIndex, effectiveCount, comparer)`. Then trim:
```csharp
var keep = effectiveCount < maxKeep ? effectiveCount : maxKeep;
if (count > keep) list.RemoveRange(startIndex + keep, count - keep);
```
This drops both the unsorted out-of-range tail and any overflow beyond the per-grid budget in one call.

### Why partition instead of filtering via a LINQ pass / new list

Per-grid sort runs on the background scan thread on up to a few thousand candidates for large grids. Allocating a new list per grid every scan cycle would churn the GC and re-open FEAT-028's allocation problem. The in-place partition uses zero allocations and O(n) work — the same order as the existing sort.

Block OBB cost per candidate is bounded: one `ComputeScaledHalfExtents` + one world-matrix fetch + up to N `OBB.Intersects(ref OBB)` calls where N ≤ member count. For a 20-member cluster this is ≤ 20 intersection tests per candidate, early-out on first hit.

### Solo BaRs and nearest-first are unaffected

- Solo scans have `skipRangeCheck=false`, so `AsyncAddBlocksOfGrid` already filters per-block during collection. `useMemberAware` is false in `SortAndCapGridCandidates` (no snapshot), the new partition branch is skipped, and the pre-existing sort+cap path runs exactly as before.
- Nearest-first multi-member scans *also* benefit from the partition (less noise in the sorted set, slightly cheaper sort) but the bug didn't manifest there — nearest-first already prefers reachable blocks because small min-distance = near-member.

## Performance Impact

**Background-thread only.**

- **Partition (Issue A):** adds one block-OBB intersection loop per candidate, capped at the cluster member count. For a 20-member cluster scanning 4096 candidates: ≤ 82k OBB tests per scan, early-terminated on first hit (typical cost ~10-20ms on the background thread). For solo BaRs: zero added cost (fast-path skip). `list.Sort` now operates on `effectiveCount` (in-range blocks only) instead of `count`, which in the pathological case (huge grid extending far beyond cluster coverage) is significantly smaller than before — net sort cost can go **down** when most candidates are unreachable.
- **Grid-entry cap (Issue B):** two integer comparisons + possible null assignment at the top of each `AsyncAddBlocksOfGrid` call. Effectively free. The downstream wins are substantial: `PreSortClusterCandidates` and `ApplyClusterResultToSelf` no longer grow unbounded on large multi-grid bases; peak cluster list size is bounded by `maxGrind + lastGridGrindAdded` (≤ 1024 + 256 = 1280 for solo, ≤ 4096 + 256 = 4352 for a 16-member cluster) instead of `N_grids × 256`.

## Verification

1. **Build:** `dotnet build "SKO-Nanobot-BuildAndRepair-System/Data/Scripts/SKO-Nanobot-BuildAndRepair-System/SKO-Nanobot-BuildAndRepair-System.csproj" -c Release -v minimal` → 0 warnings, 0 errors. ✓
2. **Repro Issue A (farthest unreachable).** 20-BaR cluster, large target grid extending beyond combined cluster reach, `GrindNearFirst` off. Before: 3-5 BaRs grind for a few seconds then the cluster goes idle. After: all BaRs with reachable blocks in their working areas keep grinding until the in-range portion is fully destroyed.
3. **Repro Issue B (unbounded cluster).** Grinding-only workload (or weld-only) with the other task type still enabled, on a base with 5-10 connected grids each carrying qualifying blocks. Before: profile session for `AsyncClusterScan` / `PreSortClusterCandidates` shows list sizes several times `maxGrind`/`maxWeld`. After: list sizes bounded by `maxGrind + lastGrid256` per scan cycle.
4. **Regression check — solo BaR farthest-first on large grid.** Same as BUG-094 verification — should still start at the farthest in-range block and work inward. The new partition branch is skipped for solo; the grid-entry cap only fires once solo's 1024 cluster cap is reached across connected subgrids.
5. **Regression check — nearest-first cluster.** All existing behavior preserved; the partition runs and reduces sort input size, and the grid-entry cap bounds cluster growth across grids. Nearest-first comparator is unchanged.
6. **Regression check — `GrindSmallestGridFirst` cluster.** BUG-091's per-grid min-distance tiebreaker still applies after the partition. `AsyncAddBlocksOfBox` still sorts grids by block count before iterating, so smallest grids reach the grid-entry cap check first; larger grids may be cap-skipped for one type but still contribute for the other. Unchanged.
7. **Regression check — unreachable grid.** A grid with zero qualifying blocks in any member's working area should be dropped entirely (early `RemoveRange + return` in `SortAndCapGridCandidates`). Not marked empty because we actually iterated and found 0 in-range — the existing "added nothing" path naturally caches as empty.
8. **Regression check — empty-grid-cache no false evictions.** Start a scan with the cluster grind cap already hit from a previous grid, then enter a new grid. That grid's `grindCapSkipped=true`; verify the grid is **not** added to `_EmptyGridCache` so the next scan (after cap frees) re-evaluates it.

## See also

- BUG-094 (v2.5.3) — cap-gate regression the fix of which exposed both follow-ups here.
- BUG-091 (v2.5.2) — per-grid min-distance spatial tiebreaker; still applies post-partition.
- BUG-086 (v2.5.1) — post-truncate re-sort; unchanged.
- FEAT-040 — grid-level containment fast-path in `AsyncAddBlocksOfGrid`; unchanged (only runs for solo).
- Codex code review (v2.5.3 pre-push) — flagged Issue B independently and prompted its inclusion in this ticket.
