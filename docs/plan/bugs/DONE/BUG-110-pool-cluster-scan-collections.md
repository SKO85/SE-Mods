# BUG-110: Pool per-cluster-scan collections to reduce GC pressure on main thread

## Status: Fixed (initial pass — collection pooling)
## Severity: Medium
## Version: v2.5.4
## Found In: `NanobotSystem.cs`, `NanobotSystem.Scanning.cs`

## Description

BUG-109's audit identified the cluster scan as the most likely source of the user-reported recurring 21→70% CPU spikes every ~5s during grinding. Background scans run 200-300 ms each on a worker thread but allocate enough that gen-1 GC pauses propagate to the main thread.

This ticket applies the highest-confidence pooling fixes from the audit. **It deliberately does not touch the per-block class allocations** (`ClusterTargetCandidate`, `ClusterFloatingCandidate`, `TargetBlockData`) — those are a bigger refactor (class-to-struct or object-pool) and warrant their own ticket once we measure the impact of this pass.

## Audit correction

The audit flagged `MyOrientedBoundingBoxD` allocations (`Scanning.cs:1808`, also `1094`, `1123`) as per-candidate heap pressure. **`MyOrientedBoundingBoxD` and `BoundingBoxD` are VRageMath structs (value types)** — the `new ...()` calls are stack-allocated, not heap. Skipped.

Confirmed via grep: `class ClusterTargetCandidate`, `class ClusterFloatingCandidate`, `class TargetBlockData` — all reference types. Those allocations are real, deferred to BUG-111.

## Fix — collection pooling on the scan thread

Six allocation sites pooled via lazy-init + `Clear()` instance fields, mirroring the existing `_ClusterMemberAreaCenters` / `_ClusterMemberAreaBoxes` pattern.

### `NanobotSystem.cs` — new instance fields

```csharp
// AsyncClusterScan reusable buffers
private List<IMyCubeGrid> _ScanGridsBuffer;
private List<IMyInventory> _ScanSourcesBuffer;
private Dictionary<IMySlimBlock, double> _ScanPreSortDistances;

// AsyncScanForSources reusable traversal state
private HashSet<long> _ScanSourceVisitedGridIds;
private Queue<IMyCubeGrid> _ScanSourceGridQueue;

// AsyncAddBlocksOfBox reusable sort buffers (only fires when GrindSmallestGridFirst flag is set)
private List<IMyEntity> _ScanSortedGrids;
private List<IMyEntity> _ScanNonGridEntities;
```

`using VRage.ModAPI;` added for `IMyEntity`.

### `NanobotSystem.Scanning.cs` — call-site replacements

| Method | Old | New |
|---|---|---|
| `AsyncClusterScan` (~1088) | `var grids = new List<IMyCubeGrid>();` | Lazy-init `_ScanGridsBuffer` + Clear, alias to `grids`. |
| `AsyncClusterScan` (~1141) | `var tempSources = new List<IMyInventory>();` | Lazy-init `_ScanSourcesBuffer` + Clear. |
| `PreSortClusterCandidates` (~1279) | `var distances = maxCap > 0 ? new Dictionary<...>(maxCap) : null;` | Lazy-init `_ScanPreSortDistances` + Clear when `maxCap > 0`. |
| `AsyncScanForSources` (~186/187) | `var visited = new HashSet<long>(); var toVisit = new Queue<IMyCubeGrid>();` | Lazy-init `_ScanSourceVisitedGridIds` + `_ScanSourceGridQueue`, both Clear before use. |
| `AsyncAddBlocksOfBox` (~910/911) | `var sortedGrids = new List<IMyEntity>(entityInRange.Count); var nonGridEntities = new List<IMyEntity>();` | Lazy-init `_ScanSortedGrids` + `_ScanNonGridEntities`, both Clear. |

All locals retain their original names (`grids`, `tempSources`, `distances`, `visited`, `toVisit`, `sortedGrids`, `nonGridEntities`) so the bodies of the methods are unchanged.

### Threading note

The pool fields are written and read only from the background scan thread for a given BaR. The mod's existing scan dispatch (via `Mod.AddAsyncAction()`) serializes scans per BaR, so a second scan can't start until the previous one's `try`/`finally` completes. No locks needed.

## Allocations eliminated per cluster scan

- 1 × `List<IMyCubeGrid>`
- 1 × `List<IMyInventory>` (when `updateSource=true`)
- 1 × `Dictionary<IMySlimBlock, double>` (when there are any candidates to sort)
- 1 × `HashSet<long>` (in `AsyncScanForSources`, fires when `updateSource=true`)
- 1 × `Queue<IMyCubeGrid>` (same)
- 2 × `List<IMyEntity>` (only when `GrindSmallestGridFirst` flag is set)

**Total: 5-7 fewer heap allocations per cluster scan.** Each scan also avoids the corresponding internal array growth as the lists fill up — net allocation reduction is larger than just the 7 list/dict headers.

## Allocations still present (deferred to BUG-111)

- **Per-block class allocations** (~100-1000 per scan): `new ClusterTargetCandidate(...)`, `new ClusterFloatingCandidate(...)`, `new TargetBlockData(...)`. These are reference types stored in the published scan result. Either convert to structs (compile-time risk if any caller does in-place mutation) or implement an object pool. Both warrant their own ticket.
- **Lambda/Comparer<T>.Create closures** (~10-15 per scan): the per-grid `Comparer<ClusterTargetCandidate>.Create((a, b) => ...)` at line 1913 captures multiple locals (`isGrinding`, `grindIgnorePriority`, `distCache`, `priCache`, `center`, etc.). Refactoring to a static comparer would require packing the captured state into a struct or instance field. Significant work for ~10-15 allocs per scan.
- **`ScanClusterResult` itself** at line 1081: allocated fresh each scan, published via reference swap. Pooling requires consumer-side awareness of recycling — defer.

## Verification

1. **Build clean** — `dotnet build ... -c Release -v minimal` → 0 warnings, 0 errors. ✓
2. **Behavior unchanged** — pure refactor; the scan logic reads/writes the same collections at the same call sites.
3. **Re-profile the active grinding scenario** that produced the 21→70% spike rhythm. Expected outcomes:
   - **Spike rhythm reduces or compresses**: GC pressure was a meaningful contributor. Class-allocation pooling (BUG-111) is the next step if spikes persist.
   - **Spike rhythm unchanged**: GC was not the dominant cause. Lock contention (`lock(MyAPIGateway.Entities)` window in scan thread) is the next hypothesis — file BUG-111 to narrow that lock scope.
   - **Spike rhythm gone**: success. Close BUG-110 with the data, file BUG-111 only if user reports new patterns.

## See also

- BUG-098 — earlier hot-path allocation cleanup (BlockKey struct, profiler closure guards). This is the next phase of the same effort.
- BUG-109 — audit + diagnosis ticket; this fixes the simplest items from that audit.
- Profile session: `20260428204153-profiling`.
