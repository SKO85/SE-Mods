# BUG-109: Cluster scan allocation pressure causing recurring 5-7s GC spikes on main thread

## Status: In Progress (data collection)
## Severity: Medium (perf — recurring spikes felt as 21→70% CPU jumps every ~5s during grinding)
## Version: v2.5.4
## Found In: `NanobotSystem.Scanning.cs`

## Description

User-visible symptom: during active grinding/welding, the server CPU jumps from ~21% baseline to ~70% in periodic spikes that recur roughly every 5 seconds. Not a sustained load, not a freeze — a regular rhythm of brief stalls.

Profile session `20260428204153-profiling` (58 BaRs, active grinding, 120 s) shows the rhythm correlates with `AsyncClusterScan` / `AsyncAddBlocksOfBox` start times:

```
AsyncClusterScan      : 20:42:02, 20:42:14, 20:42:24, 20:43:18, 20:43:33  (5 calls)
AsyncAddBlocksOfBox   : 20:42:02, 20:42:14, 20:42:24, 20:43:18, 20:43:33  (5 calls)
Mod.UpdateBeforeSimulation spikes (>1 ms): 20:41:54, 20:41:59, 20:42:02, 20:42:03, 20:42:10
```

Background scans take 200-300 ms each on the **background thread**, so they don't directly block the main thread. But the spikes on the main thread coincide with scan start times, suggesting indirect impact via:

1. **GC pressure** — cluster scan allocates heavily, runs gen-1/2 collections that pause all managed threads (including main).
2. **Lock contention** — main-thread weld/grind code waits for `lock(MyAPIGateway.Entities)` and target-list locks held by the scan thread.

The two `IsProtectedFromGrinding` outliers (12.58 ms, 10.90 ms) **both fire at `20:42:02Z`** — the same tick the cluster scan starts. Expected SE-engine work that's normally ~1 µs jumps 10,000× when GC pauses or lock contention bites.

## Allocation audit (NanobotSystem.Scanning.cs)

Full audit performed on the 2,403-line file. Hot-spots ranked by frequency × size:

### Critical (one allocation per cluster scan, fires every 5-7 seconds)

| Line | Allocation | Notes |
|---|---|---|
| 1088 | `new List<IMyCubeGrid>()` | Grids list, hot path |
| 1108 | `new List<Vector3D>()` | `_ClusterMemberAreaCenters` (only re-created when null) |
| 1113 | `new List<MyOrientedBoundingBoxD>()` | `_ClusterMemberAreaBoxes` (only re-created when null) |
| 1141 | `new List<IMyInventory>()` | `tempSources`, allocated when `updateSource=true` |
| 1279 | `new Dictionary<IMySlimBlock, double>()` | Distance cache for sort comparator |
| 186 / 187 | `new HashSet<long>()` + `new Queue<IMyCubeGrid>()` | `AsyncScanForSources` traversal state |
| 910 / 911 | `new List<IMyEntity>()` × 2 | `AsyncAddBlocksOfBox` sort buffers (only when `GrindSmallestGridFirst` enabled) |

### High frequency (per-block / per-candidate inside scan loops)

| Line | Allocation | Notes |
|---|---|---|
| 1808 | `new MyOrientedBoundingBoxD(new BoundingBoxD(...))` | Per-candidate inside member-aware partition (O(N) per scan) |
| 450, 471, 546, 793 | `new ClusterTargetCandidate(...)` | Per qualifying block; ~100-1000 per scan |
| 965, 975, 985 | `new ClusterFloatingCandidate(...)` | Per floating entity in box scan |
| 2019, 2033 | `new TargetBlockData(...)` | Per in-range result in `ApplyClusterResultToSelf` |

### Moderate (per scan, lambda closures)

| Line | Allocation | Notes |
|---|---|---|
| 925 | `Sort((a, b) => ...)` lambda | `AsyncAddBlocksOfBox` smallest-first sort |
| 1148-1172 | `tempSources.Sort((a, b) => ...)` | Source sort, captures locals |
| 1187 | `tempSources.RemoveAll(inv => ...)` | Refinery filter predicate |
| 1318-1330, 1342-1354 | `Sort((a, b) => ...)` | `PreSortClusterCandidates` weld + grind |
| 1886-1951 | `Comparer<ClusterTargetCandidate>.Create((a, b) => ...)` | Per-grid quickselect comparator |
| 2061-2070, 2090-2099, 2139-2154, 2198-2213 | `Sort((a, b) => ...)` × 4 | `ApplyClusterResultToSelf` per-member sorts |
| 2237-2255 | `Comparer<TargetBlockData>.Create((a, b) => ...)` | Per-band truncation comparator |

### Confirmed clean

- **Profiler-log closures** (10 sites) — all gated by `if (profilerTs != 0L)` so zero allocation when profiling is disabled.
- **No LINQ** in scan paths or per-block loops.
- **No boxing** detected (no `params object[]`, no value-type-stored-in-`object`).

## Note on allocation measurement

Initial plan was to add `GC.GetTotalMemory(false)` sampling around the scan methods to measure bytes allocated per call. **`System.GC` is on the Space Engineers sandbox prohibited list** (same family as `System.Threading` and `System.Reflection` — see `CLAUDE.md`), so direct allocation measurement isn't available from inside the mod. The audit above gives strong-enough circumstantial evidence to proceed without the measurement:

- 7 critical per-scan list/dict allocations.
- 11 lambda-closure sites (each captures locals on the heap).
- 100-1000 per-block struct allocations per scan (`ClusterTargetCandidate`, `ClusterFloatingCandidate`, `TargetBlockData`).
- Recurrence rhythm matches the cluster scan interval.

The user-visible 5-7 s rhythm of 21→70 % CPU spikes correlated with `AsyncClusterScan` start times is consistent with periodic gen-1 GC pauses. Lock contention with the scan thread is also possible but shouldn't compound to 5+ ms because the locked sections are short (entity enumeration only).

## Follow-up plan

Apply the audit-driven pooling fixes directly under **BUG-110** (sibling ticket). Order of payoff:

1. **Static `Comparison<T>` delegates** — replace per-call lambdas at lines 925, 1148-1172, 1187, 1318-1330, 1342-1354, 1886-1951, 2061-2070, 2090-2099, 2139-2154, 2198-2213, 2237-2255 with `static readonly` delegate fields. Each closure currently allocates ~32-48 bytes plus the captured-locals object; eliminating 11 sites at ~3 firings/scan removes ~30+ allocations per scan with zero behavior change.
2. **Pre-allocated thread-static buffers** — replace `new List<...>()` at lines 1088, 1108, 1113, 1141 with reusable instance fields (`_TempGrids`, `_TempSources`, etc.) cleared at scan entry. Same for `new Dictionary<...>()` at line 1279.
3. **Eliminate per-candidate `MyOrientedBoundingBoxD`** at line 1808 — compute distance directly without allocating the OBB struct (it's a value type but the constructor performs internal allocations through `BoundingBoxD`).
4. **`AsyncScanForSources` hash/queue reuse** — lines 186/187 are infrequent (twice in 120 s) but trivial to fix: instance fields cleared at method entry.

Verification: simply re-profile the active-grinding scenario after the fixes. If the 5-7 s rhythm of spikes disappears or compresses, GC was the cause. If it persists, lock contention is the next hypothesis.

## See also

- BUG-098 — earlier hot-path allocation cleanup (BlockKey struct + profiler closure guards). This ticket is the next phase of the same effort, focused on the scan thread.
- Profile session: `20260428204153-profiling` (58 BaRs, active grinding, sim-speed avg 1.01 with 21→70% CPU spikes every ~5s).

## See also

- BUG-098 — earlier hot-path allocation cleanup (BlockKey struct + profiler closure guards). This ticket is the next phase of the same effort, focused on the scan thread.
- Profile session: `20260428204153-profiling` (58 BaRs, active grinding, sim-speed avg 1.01 with 21→70% CPU spikes every ~5s).
