# FEAT-070: Consolidate scan sort comparators + fix farthest-first smallest-grid inconsistency

## Status: Done
## Priority: Medium
## Version: v2.5.3

## Summary

The scan pipeline in `NanobotSystem.Scanning.cs` had seven independent sort call sites — four for grind, three for weld — each open-coding the same autogrind-bucket / priority / `GrindSmallestGridFirst` / BUG-091 min-distance tiebreaker / stable tiebreaker logic. BUG-086, BUG-091, and BUG-094 had to touch every copy and at least one copy drifted from the others. Consolidated into three shared helpers near `MinSquaredDistanceToClusterMembers`; every site now delegates the non-distance key while keeping local control over its own distance metric (squared / non-squared / member-aware / dict-cached). While folding the comparators together, fixed a real ordering bug in one of the duplicated copies.

## Motivation

The same sort logic was written seven times with subtle variations:

| Site | File | Type | Candidate type | Distance metric |
|---|---|---|---|---|
| `SortAndCapGridCandidates` (grind branch) | `Scanning.cs:1440-1599` | grind | `ClusterTargetCandidate` | inline squared, member-aware |
| `SortAndCapGridCandidates` (weld branch) | same | weld | `ClusterTargetCandidate` | inline squared, member-aware |
| `PreSortClusterCandidates` (grind) | `Scanning.cs:1082-1119` | grind | `ClusterTargetCandidate` | dict-cached squared |
| `PreSortClusterCandidates` (weld) | `Scanning.cs:1131-1150` | weld | `ClusterTargetCandidate` | dict-cached squared |
| `ApplyClusterResultToSelf` (weld pre-sort) | `Scanning.cs:1555-1571` | weld | `TargetBlockData` | non-squared `.Distance` |
| `ApplyClusterResultToSelf` (weld post-truncate re-sort) | `Scanning.cs:1591-1607` | weld | `TargetBlockData` | non-squared `.Distance` |
| `ApplyClusterResultToSelf` (grind pre-sort) | `Scanning.cs:1647-1683` | grind | `TargetBlockData` | non-squared `.Distance` |
| `ApplyClusterResultToSelf` (grind post-truncate re-sort) | `Scanning.cs:1721-1763` | grind | `TargetBlockData` | non-squared `.Distance` |

Eight lambdas, four distance metrics, seven hand-written copies of the priority+tiebreaker chain. A fix to the chain (BUG-086, BUG-091) had to be applied in each copy. BUG-094 + BUG-096 stacked more changes on top. The drift between `PreSortClusterCandidates` and `ApplyClusterResultToSelf` went undetected long enough to become a real behavior bug — see the folded fix below.

## Design

### Three helpers

**`CompareGrindNonDistance(blockA, attrsA, blockB, attrsB, usePriority, smallestGridFirst, perGridMinDist)`** — instance method. Returns an int that encodes the non-distance sort key in order:
1. Autogrind-first bucket (autogrind blocks precede non-autogrind)
2. Optional priority via `BlockGrindPriority.GetPriority(...)` (caller supplies `usePriority`)
3. `GrindSmallestGridFirst`: smaller-grid-count wins; equal-size grids use BUG-091's `perGridMinDist` lookup for nearest-block-on-grid tiebreaker; deterministic `CubeGrid.EntityId` fallback for equal-size + equal-min-distance

Returns 0 when the caller should fall through to its own distance compare (which is always the caller's responsibility so it keeps control of metric and direction).

**`CompareWeldPriority(blockA, blockB)`** — instance method. Just returns `BlockWeldPriority.GetPriority(a) - .GetPriority(b)`. Weld has no autogrind bucket and no smallest-grid-first.

**`CompareBlockStableTiebreak(blockA, blockB)`** — static. Grid `EntityId` → block position X/Y/Z. Used after priority+distance to keep sort order reproducible across ties so output doesn't churn across scan cycles.

### Call site pattern (grind)

```csharp
list.Sort((a, b) =>
{
    var cmp = CompareGrindNonDistance(a.Block, a.Attributes, b.Block, b.Attributes,
        grindUsePriority, grindSmallestGridFirst,
        grindSmallestGridFirst ? _gridMinDistLookup : null);
    if (cmp != 0) return cmp;

    // Caller-specific distance compare. Metric and direction (nearest/farthest) stay local.
    return grindNearFirst ? distA.CompareTo(distB) : distB.CompareTo(distA);
});
```

### Call site pattern (weld)

```csharp
list.Sort((a, b) =>
{
    var cmp = CompareWeldPriority(a.Block, b.Block);
    if (cmp != 0) return cmp;

    var distCmp = /* caller-specific distance compare */;
    if (distCmp != 0) return distCmp;

    return CompareBlockStableTiebreak(a.Block, b.Block);
});
```

### Folded behavior fix — farthest-first + smallest-grid + same-size grid

The two grind sort sites in `ApplyClusterResultToSelf` (pre-sort and post-truncate) previously contained this code path after `GrindSmallestGridFirst`'s block-count tiebreaker:

```csharp
// Stable deterministic fallback for rare min-dist ties.
var gridCmp = a.Block.CubeGrid.EntityId.CompareTo(b.Block.CubeGrid.EntityId);
if (gridCmp != 0) return gridCmp;
return Utils.Utils.CompareDistance(a.Distance, b.Distance);   // <-- always nearest-first
```

The final `return CompareDistance(a.Distance, b.Distance)` is **always nearest-first**, regardless of the user's `GrindNearFirst` flag. For a user with farthest-first + smallest-grid-first both enabled, blocks within the same grid (after the smallest-grid tiebreakers reduce to one grid) were ordered nearest-first instead of farthest-first. `PreSortClusterCandidates` already had the correct branching (`return grindNearFirst ? a.CompareTo(b) : b.CompareTo(a)`), so the coordinator's pre-sort and the member's own sort disagreed on ordering — two different sort orders for the same data, depending on which path ran.

The consolidated helper removes the within-branch distance compare entirely and always returns 0 after the smallest-grid tiebreakers complete — the caller's lambda then applies `grindNearFirst` uniformly. All three grind sort sites now honor farthest-first end-to-end.

## Files Affected

- `SKO-Nanobot-BuildAndRepair-System/Data/Scripts/SKO-Nanobot-BuildAndRepair-System/NanobotSystem.Scanning.cs`
  - Added three helpers: `CompareGrindNonDistance`, `CompareWeldPriority`, `CompareBlockStableTiebreak`
  - Refactored all seven sort sites to delegate to them
  - `SortAndCapGridCandidates`: removed the unused `priorityHandler` local that the consolidation made redundant

No new fields, no new allocations in hot paths — helpers take primitives and return `int`. Main-thread cost zero (all sort sites run on the background scan thread).

## Performance Impact

Net +6 lines in `Scanning.cs` (helpers add ~100 lines with XML docs + design comment block; call sites lose ~140 lines). Build clean, 0 warnings. Zero allocations added — the helpers are methods, not delegate factories, so no closure churn per sort call.

## Testing

1. **Build**: `dotnet build SKO-Nanobot-BuildAndRepair-System/Data/Scripts/SKO-Nanobot-BuildAndRepair-System/SKO-Nanobot-BuildAndRepair-System.csproj -c Release -v minimal` → 0 warnings, 0 errors.
2. **Regression — solo BaR farthest-first on large grid**: sort output identical to v2.5.2 (per BUG-094 verification).
3. **Regression — nearest-first cluster**: sort output identical.
4. **Regression — `GrindSmallestGridFirst` cluster, nearest-first**: sort output identical (BUG-091's spatial tiebreaker preserved).
5. **Behavior fix verification — `GrindSmallestGridFirst` cluster, farthest-first**: within same-size grids, blocks now order farthest-first instead of nearest-first. Observable in the profile log (`ApplyClusterResultToSelf` sort trace shows consistent farthest ordering across pre-sort and re-sort).
6. **Weld sort regression**: all four weld sort sites produce identical output for identical input. Stable tiebreakers work across ties.

## See also

- BUG-086 (v2.5.1) — post-truncate re-sort fix, now centralized.
- BUG-091 (v2.5.2) — per-grid min-distance spatial tiebreaker, now accepted via `perGridMinDist` parameter.
- BUG-094 (v2.5.3) — farthest-first cap-gate fix; triggers the sort path the consolidation targets.
- BUG-096 (v2.5.3) — member-OBB partition in `SortAndCapGridCandidates`; interacts with but is independent of the comparator consolidation.
- BUG-097 (v2.5.3) — priority hash race fix; the consolidation sites automatically inherit it via the shared `GetPriority` call path.
