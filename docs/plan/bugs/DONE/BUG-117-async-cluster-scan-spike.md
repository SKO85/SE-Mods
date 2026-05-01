# BUG-117: AsyncClusterScan max 428 ms / avg 84 ms on large-ship worlds

## Status: Fixed
## Severity: Medium (background, not main-thread, but contributes to scan latency)
## Version: v2.5.4
## Found In: `NanobotSystem.Scanning.cs` `AsyncClusterScan`, `AsyncAddBlocksOfGrid`; `Utils/Utils.cs` `GetUserRelationToOwner`

## Symptom

Original profile session `20260429013527-profiling` (120 s, large-ship welding scenario):

| Method | Calls | Avg ms | Max ms | Total ms |
|---|---:|---:|---:|---:|
| AsyncClusterScan | 71 | 84.6 | **428.8** | 6,009 |
| AsyncAddBlocksOfGrid | 374 | 13.4 | **74.3** | 5,019 |
| AsyncAddBlocksOfBox | 71 | 19.0 | **114.2** | 1,350 |

Verification profile `20260429144151-profiling` (different but related scenario, sim 1.00):

| Method | Calls | Avg ms | Max ms | Total ms |
|---|---:|---:|---:|---:|
| AsyncClusterScan | 17 | 454.3 | **537.7** | 7,723 |
| AsyncAddBlocksOfGrid | 452 | 17.0 | **185.5** | 7,682 |
| AsyncAddBlocksOfBox | 17 | 448.4 | **533.8** | 7,623 |

Different cluster size between sessions; the per-call shape (AsyncClusterScan ≈ AsyncAddBlocksOfBox in the second session) tells us BoundingBox-mode scanning of the cluster's nearby grids is the dominant child path.

## Root cause analysis

Per-grid log breakdown (`AsyncAddBlocksOfGrid.log` from `20260429144151`):

3 huge grids dominate every cluster scan:

| gridId | Blocks | Per-scan cost | grindAdded | weldAdded |
|---|---:|---:|---:|---:|
| 142961911592140912 | ~7,300 | 138-185 ms | 128 (per-grid cap) | 0 |
| 129497315277299994 | ~7,300 | 135-185 ms | 128 | 0 |
| 116207788909290906 | ~7,300 | 135-160 ms | 128 | 0 |

Three grids × ~150 ms = ~450 ms ≈ AsyncClusterScan total. The grids are target-rich (player is autograbbing — block count slowly drops from 7 354 → 7 244 over the 120 s as grinding happens) but never cap-skipped at entry. Each scan re-evaluates ~22 000 blocks even though the result barely changes between scans.

### Per-block cost source

Inside `foreach (var slimBlock in newBlocks)` for each grid (`Scanning.cs:754`), every block runs `AsyncAddBlockIfTarget` → `AsyncAddBlockIfGrindTarget` and `AsyncAddBlockIfWeldTarget`. The inner-most engine calls per block:
- `block.IsProjected()`
- `block.NeedRepair(...)` (weld check)
- `block.GetColorMask()` (weld + color check)
- `BlockGrindPriority.GetEnabled(block)` / `BlockWeldPriority.GetEnabled(block)` (dict lookups)
- `block.GetUserRelationToOwner(_Welder.OwnerId)` — ownership relation

The relation extension lives in `Utils.cs:190` and uses `GridOwnershipCacheHandler` for slim-only blocks (armor, FatBlock=null). For **fat blocks** it always called `fatBlock.GetUserRelationToOwner(userId)` (engine, ~5 µs) first, falling back to the grid cache only if the engine returned NoOwnership. Since most fat blocks inherit grid ownership (`OwnerId == 0`), the engine call's only output for them is "NoOwnership, please use the grid cache" — we paid the engine cost just to learn that.

On a 7 300-block grid with ~25 % fat blocks (~1 800), that's ~9 ms of avoidable engine calls per grid × 3 grids ≈ 27 ms per scan in the dominant case. Real total is higher in worst-case grids where multiple fat blocks have specific owners.

## Layer A fix (delivered)

`Utils/Utils.cs:190` `GetUserRelationToOwner(this IMySlimBlock, long)`:

```csharp
var fatBlock = slimBlock.FatBlock;
if (fatBlock != null)
{
    // BUG-117: When the block has no individual owner (OwnerId == 0), the engine's
    // GetUserRelationToOwner returns NoOwnership and the slow path falls back to
    // the grid relation anyway. Skip the engine call entirely and go straight to
    // the cached grid relation.
    if (fatBlock.OwnerId == 0)
    {
        return GridOwnershipCacheHandler.GetRelationBetweenGridAndPlayer(slimBlock.CubeGrid, userId);
    }

    var relation = fatBlock.GetUserRelationToOwner(userId);
    // ... existing fallback unchanged
}
```

### Why this is the right scope for Layer A

- **Semantically identical**: SE's relation calculation for a block with `OwnerId == 0` ultimately defers to grid ownership; we just take that shortcut without the engine round-trip.
- **Foundational, not point-fix**: every caller of the `IMySlimBlock.GetUserRelationToOwner` extension benefits — `AsyncAddBlockIfGrindTarget`, `IsRelationAllowed4Welding`, BUG-118's source scan, future scan code.
- **No new state**: no caches, no invalidation, no settings — just a property check.
- **Low risk**: `IMyCubeBlock.OwnerId` is the canonical field SE uses internally for the same decision.

### Expected impact

Modest, ~10-30 % reduction on per-grid scan cost. The full per-block engine load (NeedRepair, IsProjected, GetColorMask) is still there — Layer A only removes the ownership-relation engine call for the dominant `OwnerId == 0` case.

## Layers B/C (still open)

If Layer A's measured gain isn't sufficient:

- **Layer B — per-grid candidate memoization**: when a grid contributed at the per-grid cap (e.g., 128 grind targets) and the block count hasn't dropped meaningfully, skip the foreach for N seconds and reuse the prior candidate list. Risk: staleness when grids get painted, ownership flips, or blocks finish welding/grinding. Needs cheap invalidation signal.
- **Layer C — pre-filtered block cache** in the background thread: `SharedGridSortedCache` (or new sibling) returns only blocks that could be grind/weld candidates, pre-filtered by quick checks (priority enabled, not projected). Iteration scope drops from ~7 300 to ~few hundred per grid. Risk: integrity / color / ownership invalidation. Substantial cache infrastructure.

Decision deferred until Layer A re-profile data is available.

## Verification

1. **Build clean** — `dotnet build ... -c Release -v minimal` → 0 warnings, 0 errors. ✓
2. **Re-profile** session `20260429145732-profiling` (sim 1.00 avg / 0.85 min, same scenario shape):

| Metric | Before Layer A (`20260429144151`) | After Layer A (`20260429145732`) | Change |
|---|---:|---:|---:|
| AsyncClusterScan total | 7 723 ms | 4 953 ms | **-36 %** |
| AsyncClusterScan avg | 454 ms | 354 ms | -22 % |
| AsyncClusterScan max | 537 ms | 448 ms | -17 % |
| AsyncAddBlocksOfGrid total | 7 682 ms | 4 894 ms | **-36 %** |
| AsyncAddBlocksOfGrid avg | 17.0 ms | 10.3 ms | **-41 %** |
| 3 huge grids per-scan avg | ~150 ms | 113 ms | -24 % |
| 3 huge grids per-scan min | ~135 ms | 83.9 ms | **-38 %** |
| 3 huge grids per-scan max | 185.5 ms | 158.2 ms | -15 % |
| AsyncAddBlocksOfBox total | 7 623 ms | 4 882 ms | -36 % |

Result lands at the top of the predicted 10-30 % range, with headline reductions of 36-41 %. Background-thread wall-clock cost dropped from 6.4 % → 4.1 % of session. The min on huge-grid scans dropped most cleanly (38 %) — the clearest measurement of the engine-call elimination since contention noise is lowest there. No behavioral regression observed: same grid selection, same target counts (128 per grid per cluster cap), autograb continues to fire on enemy/neutral grids.

## Decision on Layers B/C

Layer A's 36 % background-thread reduction with no behavioral risk is the best win-to-effort ratio currently visible. Layers B/C remain technically possible but:

- **Sim-speed is unaffected** (1.00 avg) — players see no slowdown from the residual cost.
- **Staleness max ~0.45 s** — acceptable for grinding/welding cadence.
- **Layer B (per-grid candidate memoization)** would push further but adds invalidation complexity (color-paint, integrity, ownership flips) that isn't justified by the current cost.

Recommend **closing BUG-117 as Fixed** at this layer; revisit if a future profile or report shows the residual cost is a problem.

## Profile reference

- Original symptom: `20260429013527-profiling` (sim 0.43-0.69)
- Investigation source: `20260429144151-profiling` (sim 1.00, post-BUG-119)
- Layer A verification: `20260429145732-profiling` (sim 1.00, post-Layer-A)
- Files: `.AsyncClusterScan.log`, `.AsyncAddBlocksOfGrid.log`, `.AsyncAddBlocksOfBox.log`, `.Summary.log`

## See also

- BUG-110/111 — earlier pooling work on cluster-scan collections
- BUG-112 / BUG-116 — cheap-first filter ordering precedents (welding path)
- BUG-118 / BUG-119 — inner-predicate optimizations that cleared up the source-scan side; this ticket is the analogous outer-loop work for the cluster scan side
- `Handlers/GridOwnershipCacheHandler.cs` — the cached grid relation we now route to in the OwnerId=0 fast path
