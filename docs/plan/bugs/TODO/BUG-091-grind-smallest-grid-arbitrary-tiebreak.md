# BUG-091: `GrindSmallestGridFirst` orders same-size grids by arbitrary `EntityId` instead of proximity

## Status: Fixed
## Severity: Medium
## Version: v2.5.2
## Found In: `NanobotSystem.Scanning.cs` — `PreSortClusterCandidates` (~1014), `ApplyClusterResultToSelf` grind pre-sort (~1506), and grind post-truncate re-sort (~1552)

## Description

When `GrindSmallestGridFirst` is enabled and two grids within the BaR's working area share the same `BlocksCount`, the current grind sort comparator falls back to comparing `CubeGrid.EntityId` **before** distance. Since `EntityId` reflects arbitrary creation order, the BaR picks the lower-EntityId grid regardless of which grid is actually closer — it can travel to a far same-size grid and ignore a nearby one.

Common scenarios that trigger this:
- Two wrecks of similarly sized debris.
- Multiple drones built from the same blueprint.
- Two instances of the same prefab station.

## Steps to Reproduce

1. Place a BaR with `GrindSmallestGridFirst` enabled.
2. Spawn two small grids of the same block count:
   - Grid A: `EntityId=1000`, 200 blocks, 500m from the welder.
   - Grid B: `EntityId=2000`, 200 blocks, 50m from the welder.
3. Damage (or paint with the grind color) both grids so they both appear as grind targets.
4. Observe which grid the BaR processes first.

**Expected:** BaR grinds the closer grid (Grid B) first.
**Actual before fix:** BaR grinds the farther grid (Grid A) first because its `EntityId` is lower. All 200 blocks on Grid A are processed before the BaR touches the near grid.

## Root Cause

BUG-086 (v2.5.1) added an `EntityId` tiebreaker to the grind sort to group blocks from the same grid together, eliminating the "all over the place" interleaving between same-size grids. The comparator order became:

```csharp
if (grindSmallestGridFirst)
{
    var res = ((MyCubeGrid)a.Block.CubeGrid).BlocksCount - ((MyCubeGrid)b.Block.CubeGrid).BlocksCount;
    if (res != 0) return res;
    var gridCmp = a.Block.CubeGrid.EntityId.CompareTo(b.Block.CubeGrid.EntityId);
    if (gridCmp != 0) return gridCmp;          // ← arbitrary: lower EntityId wins
    return Utils.Utils.CompareDistance(a.Distance, b.Distance);
}
```

`EntityId` is assigned at grid creation and has no relationship to where the grid is in space. For two grids with identical `BlocksCount`, whichever grid was spawned first (lower `EntityId`) is always picked first. Distance is only consulted within the single "winning" grid.

The grouping intent was right; the tiebreaker choice was the bug.

### Affected sites

| Line range | Function | Affected |
| --- | --- | --- |
| ~1002–1026 | `PreSortClusterCandidates` grind sort | Yes — coordinator sorts cluster-wide candidates across grids |
| ~1490–1520 | `ApplyClusterResultToSelf` grind pre-sort (`!result.PreSorted` branch) | Yes — each member sorts across grids |
| ~1540–1580 | `ApplyClusterResultToSelf` grind post-truncate re-sort (the Codex-flagged line) | Yes — re-sort after `TruncateGridAware` |
| ~1290 | `SortAndCapGridCandidates` | **No in practice** — called per-grid with a single-grid index range, so both `BlocksCount` and `EntityId` are equal and distance already decides |

## Fix

Replace the arbitrary `EntityId` tiebreaker with a **per-grid minimum-distance lookup** so equal-size grids are ordered by their nearest block. Preserves BUG-086's grouping intent (blocks within the same grid still share the same min-distance key, so they stay grouped together) but orders the groups themselves by spatial proximity.

### Shared infrastructure

`NanobotSystem.cs` — added a pooled instance field alongside the other sort-helper pools:

```csharp
// BUG-091: Per-grid minimum distance used by GrindSmallestGridFirst sorts so
// same-size grids are ordered by their closest block (spatial), not by arbitrary
// EntityId. Pooled dict cleared and refilled by each sort pre-pass.
private Dictionary<long, double> _gridMinDistLookup = new Dictionary<long, double>();
```

### Per-site change pattern

Before each affected `Sort(...)` call, when `grindSmallestGridFirst` is set, do a one-pass walk over the candidate list and record the minimum distance seen per `CubeGrid.EntityId`. In the comparator, replace the old `EntityId`-vs-`EntityId` compare with:

```csharp
if (grindSmallestGridFirst)
{
    var res = ((MyCubeGrid)a.Block.CubeGrid).BlocksCount - ((MyCubeGrid)b.Block.CubeGrid).BlocksCount;
    if (res != 0) return res;

    // BUG-091: spatial tiebreaker — nearest equal-size grid wins.
    // Blocks within the same grid share a minDist so they stay grouped.
    double minA, minB;
    _gridMinDistLookup.TryGetValue(a.Block.CubeGrid.EntityId, out minA);
    _gridMinDistLookup.TryGetValue(b.Block.CubeGrid.EntityId, out minB);
    var minCmp = minA.CompareTo(minB);
    if (minCmp != 0) return minCmp;

    // Stable deterministic fallback for rare min-dist ties.
    var gridCmp = a.Block.CubeGrid.EntityId.CompareTo(b.Block.CubeGrid.EntityId);
    if (gridCmp != 0) return gridCmp;
    return Utils.Utils.CompareDistance(a.Distance, b.Distance);
}
```

### Distance metric per site

- **`PreSortClusterCandidates`** — distances come from the local `distances` dict populated by `MinSquaredDistanceToClusterMembers` (cluster-min distance for multi-member) or `coordCenter` distance (solo). The pre-pass reads from the same dict, so ordering is consistent with the existing sort.
- **`ApplyClusterResultToSelf` grind pre-sort** — distances come from `TargetBlockData.Distance`, set at lines ~1266/1280 by each member's own `IsInRange(ref areaOrientedBox, out distance)`. The per-grid min-dist therefore reflects THIS member's proximity, which is exactly what the user wants.
- **`ApplyClusterResultToSelf` grind post-truncate re-sort** — same as above, but `TruncateGridAware` may have removed blocks so the dict is rebuilt fresh after truncation.

### Why not a simpler fix

- **"Just move distance before EntityId"** reintroduces the interleaving BUG-086 was written to fix — same-size grids get their blocks interleaved by distance again.
- **"Accept as-is, it's rare"** — in practice, wreckage fields, drone swarms, and prefab bases trigger same-size grids frequently enough to be noticeable.

## Risks

- **Allocation:** one pooled `Dictionary<long, double>` reused across cycles — no per-scan GC pressure after the first cycle.
- **Thread model:** both affected sort sites run on the background scan thread (`AsyncClusterScan` → `PreSortClusterCandidates` and `ApplyClusterResultToSelf`). `_gridMinDistLookup` is accessed only from that thread, same as the existing `_ClusterMemberAreaCenters` from BUG-088. Safe.
- **Edge case — identical min-dist for two same-size grids:** falls back to the stable `EntityId` tiebreaker (retained as a deterministic last step), so the sort is still stable.
- **Behavior change is scoped:** only BaRs with `GrindSmallestGridFirst` enabled see any change. Everyone else gets byte-identical behavior.

## Verification

1. `dotnet build SKO-Nanobot-BuildAndRepair-System/Data/Scripts/SKO-Nanobot-BuildAndRepair-System/SKO-Nanobot-BuildAndRepair-System.csproj -c Release -v minimal` — compiles clean under C# 6.
2. **Codex repro:** two equal-size grids, one ~50m and one ~500m from the BaR, `GrindSmallestGridFirst` enabled. Expect the close grid to be ground first; was the far grid before the fix.
3. **BUG-086 regression:** two equal-size grids at similar distance. Expect blocks to still group by grid (no interleaving) — one grid fully processed, then the other.
4. **Different-size regression:** two grids of very different sizes. Expect the smaller grid to still win regardless of distance (primary sort key unchanged).
5. **Solo BaR regression:** `GrindSmallestGridFirst` on a solo BaR — verify no behavior change vs v2.5.1.
6. **Profile sanity:** `/nanobars profile start 60` during a grind-heavy session. Expect `AsyncClusterScan`, `ApplyClusterResultToSelf`, and `PreSortClusterCandidates` to stay within a few ms of the baseline — the pre-pass is O(n) once per sort.

## See also

- BUG-086 — introduced the `EntityId` tiebreaker that BUG-091 is refining. BUG-086 ticket has been annotated with a follow-up note pointing here.
