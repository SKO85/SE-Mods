# BUG-094: Farthest-first grind sort starts in the wrong place on large grids (per-block cap-gate regression)

## Status: Fixed
## Severity: High
## Version: v2.5.3
## Found In: `NanobotSystem.Scanning.cs` — `AsyncAddBlocksOfGrid` per-block loop + `AsyncAddBlockIfTarget` per-block count gate

## Description

With `GrindNearFirst` disabled (farthest-first grinding) on a grid whose qualifying block count exceeds the global scan cap (`maxGrind = MaxPossibleGrindTargets × capMultiplier`), the BaR does **not** start at the far end of the grid. Instead it starts somewhere in the middle or early part of the ship.

Reported by a player on a 7000-block ship. With the default `MaxPossibleGrindTargets = 256` (so `maxGrind = 1024` for a solo BaR), grinding started in the wrong place. Bumping `MaxPossibleGrindTargets` to 10,000 (larger than the grid's qualifying block count) restored the correct farthest-first behavior — confirming the cap was the gate.

## Steps to Reproduce

1. Place a single BaR near a large ship (≥ 7000 blocks, ≥ 1024 of them needing grinding — e.g. grind-color applied or janitor relation matches most blocks).
2. In the BaR terminal: disable **Grind Near First** (so farthest-first is active). Enable grinding via grind-color or janitor.
3. Wait for the next scan cycle.
4. Observe: grinding starts somewhere in the middle/beginning of the ship rather than at the far end.
5. Quit, edit `NanobotSystem.cs` to set `MaxPossibleGrindTargets = 10000`, rebuild, reload. Grinding now starts at the far end as expected.

## Root Cause

`AsyncAddBlocksOfGrid` (`NanobotSystem.Scanning.cs:461-553`) iterates the grid's raw block cache in cache order — **not** in spatial order. Each block is passed to `AsyncAddBlockIfTarget`, which at line 165 short-circuits when the global count is already at the cap:

```csharp
if (clusterGrindTargets.Count < maxGrind)
{
    added = AsyncAddBlockIfGrindTarget(...);
}
```

Once `clusterGrindTargets.Count == maxGrind` (1024 for a solo BaR), every subsequent block on the grid is silently skipped by the gate, regardless of how well it would have sorted. The result: `clusterGrindTargets` ends up with the first 1024 qualifying blocks in grid-cache iteration order — an arbitrary spatial subset.

After the loop, `SortAndCapGridCandidates` (line 558-562) sorts those 1024 by the user's setting (farthest-first) and keeps the top 256. But because the input is only the first 1024 in cache order, the "top 256" are the 256-farthest-of-those-1024 — not the 256 farthest of the grid's entire qualifying set. On a 7000-block ship the grid-cache order roughly corresponds to block creation order, which clusters around the middle/beginning of the build.

### History of the regression

- **v2.4.4 (`Utils/UtilsSorting.cs::SortWithPriorityAndDistance`)**: pre-sorted the **entire grid** by priority + distance before iteration. With sorted input, the early-exit at the cap was safe — the first N iterated were already the "best" N.
- **v2.5.0 commit `cd6551f`, "Remove redundant full-grid sort from scan pipeline — 45% main-thread reduction"**: deleted `UtilsSorting.cs` and removed the pre-sort, under the reasoning that "`PreSortClusterCandidates` already sorts the final candidates." That's true for the candidates that made it into the list — but it doesn't help if the list was built from an arbitrary cache-order prefix of the grid.
- The per-block count gate at `AsyncAddBlockIfTarget:165` was left in place. Without the pre-sort to make iteration order spatially meaningful, the gate became a correctness bug.

BUG-086 (v2.5.1) and BUG-091 (v2.5.2) added per-grid sort + min-distance tiebreakers on top of this broken foundation. Those fixes were correct individually but could not recover the blocks that were already dropped by the cap gate during collection.

## Fix

**`NanobotSystem.Scanning.cs:461-478`** — change the call to `AsyncAddBlockIfTarget` to pass `int.MaxValue` for the per-block count gates:

```csharp
foreach (var slimBlock in newBlocks)
{
    if (ShouldStopScan(clusterWeldTargets, clusterGrindTargets, null, maxWeld, maxGrind, 0)) break;

    // BUG-094: Pass int.MaxValue for the per-block count gates so EVERY qualifying
    // block on this grid enters the candidate list. The global cap (maxWeld/maxGrind)
    // must not short-circuit the per-block add here, because on grids larger than
    // the cap the iteration would keep whatever blocks happened to be first in the
    // grid-cache order — not the true top-N by priority/distance.
    // The per-grid budget (MaxPossibleWeldTargets / MaxPossibleGrindTargets) is
    // enforced after the loop via SortAndCapGridCandidates (below), which sorts
    // the qualifying candidates and keeps the user's preferred top-N (nearest,
    // farthest, smallest-grid-first, etc.). Regression introduced in v2.5.0 when
    // the full-grid pre-sort was removed but the cap-gate short-circuit was kept.
    AsyncAddBlockIfTarget(ref areaBox, useIgnoreColor, ref ignoreColor, useGrindColor,
        ref grindColor, autoGrindRelation, autoGrindOptions, slimBlock,
        clusterWeldTargets, grindTargetsForGrid,
        int.MaxValue, int.MaxValue,    // was: maxWeld, maxGrind
        blockSkipRange);
    ...
}
```

The outer `ShouldStopScan` at line 463 stays — it's a rare safety net that only fires when all three target types are simultaneously full. Post-loop `SortAndCapGridCandidates` enforces the per-grid cap as before, now operating on the *true* qualifying set.

Sub-grid recursion (mechanical connections, attachables, connectors, projectors at lines 476/486/496) still checks `ShouldStopScan` before descending, so the global cap still limits cross-grid scans.

## Performance Impact

**Background-thread only.** `AsyncAddBlocksOfGrid` is called from `AsyncClusterScan` on the background scan thread; the main simulation tick pays nothing extra.

### Cost per grid

| Grid profile | Qualifying blocks | Extra cost per scan |
|---|---|---|
| Small (fits in area, FEAT-040 applies) | < 256 | **0** — cap never hit, no change |
| Medium | 200–1000 | **0** — cap never hit |
| Large single ship (user's case) | 1500 | **~5–10 ms** on background thread |
| Very large base (10 × 5000-block grids) | ~10,000 total | **~30–50 ms** on background thread |

Cost scales with the number of qualifying blocks ABOVE the cap, not total grid size. FEAT-040's grid-level containment pre-check (line 445-457) still skips per-block `IsInRange` on grids that fit entirely inside the work area, so the added cost only applies when the grid is actually bigger than the work range.

### Comparison to v2.4.4

v2.4.4 pre-sorted the entire grid by priority + distance before iteration — cited as costing 45% of main-thread time when removed in v2.5.0. The BUG-094 fix is **cheaper** than v2.4.4's approach: we sort only the qualifying subset (not the whole grid) and we do it on the background thread (v2.4.4 did it on main).

### sim speed

Three recent 120s profile sessions showed sim speed avg 1.01 with `AsyncClusterScan` avg ~75 ms / ~63 ms on the background thread. Post-fix expected: avg ~80 ms / ~70 ms. No impact on sim speed.

## Why Option A (list + final sort) instead of a bounded heap

Considered alternatives:
- **Option A (chosen): iterate all, add qualifying to list, sort + cap at end via existing `SortAndCapGridCandidates`.** One-call-site change. Peak memory = qualifying blocks on current grid (~48 KB for a 1500-block case).
- **Option B: bounded top-N heap.** More code (custom heap or sorted-insert wrapper). Peak memory capped at exactly 256. Slightly more total CPU than A for realistic sizes.
- **Option C: restore v2.4.4's full-grid pre-sort.** Most expensive; the one v2.5.0 explicitly removed.

Option A is the simplest correct fix and the peak memory cost is trivial at realistic grid sizes. Option B is a tight optimization we can do later if profiling ever shows it matters; it's not worth the additional code complexity now.

## Verification

1. Build: `dotnet build SKO-Nanobot-BuildAndRepair-System/Data/Scripts/SKO-Nanobot-BuildAndRepair-System/SKO-Nanobot-BuildAndRepair-System.csproj -c Release -v minimal` — 0 warnings / 0 errors. ✓
2. **Repro the user's scenario.** 7000-block ship, grind-color applied to most blocks, `GrindNearFirst` OFF (farthest-first), default `MaxPossibleGrindTargets = 256`. Before the fix: grinding starts in the middle. After the fix: grinding starts at the far end.
3. **Regression check — nearest-first on the same ship.** Enable `GrindNearFirst`. After the fix: grinding starts at the closest block and moves outward. Same as before.
4. **Regression check — small ship (< 256 qualifying).** Fix should have no observable effect; the cap gate never fires.
5. **Regression check — `GrindSmallestGridFirst` on two same-size grids of different distances.** BUG-091's spatial tiebreaker still applies; the nearest equal-size grid wins. Unchanged.
6. **Profile sanity.** Run `/nanobars profile start 120` during a large-grid grind session. `AsyncAddBlocksOfGrid` avg/max should rise by a few ms per large grid; `AsyncClusterScan` total should stay within ~20% of the pre-fix baseline. sim speed avg should remain ≥ 1.0.

## See also

- BUG-086 (v2.5.1) — added post-truncate re-sort. Correct individually, but its input was already corrupted by the BUG-094 cap gate.
- BUG-091 (v2.5.2) — added per-grid min-distance spatial tiebreaker. Same input-corruption caveat as BUG-086.
- FEAT-040 — grid-level containment pre-check for skipping per-block `IsInRange`. Unchanged; still applies.
- v2.5.0 commit `cd6551f` / `f399ea5` — "Remove redundant full-grid sort from scan pipeline". This is the commit whose incomplete refactor introduced the regression.
- `Utils/UtilsSorting.cs` (deleted in v2.5.0) — the v2.4.4 full-grid pre-sort implementation that this fix makes obsolete via the post-loop sort instead.
