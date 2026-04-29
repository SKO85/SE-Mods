# BUG-111: Convert ClusterTargetCandidate / ClusterFloatingCandidate to structs

## Status: Fixed
## Severity: Medium (perf — biggest remaining per-scan allocation source after BUG-110)
## Version: v2.5.4
## Found In: `Cluster/ScanClusterResult.cs`

## Description

After BUG-110 pooled the per-scan list/dict/hashset/queue allocations on the cluster-scan thread, the dominant remaining allocation source was **per-candidate class instantiations**:

| Site (NanobotSystem.Scanning.cs) | Frequency | Allocation |
|---|---|---|
| 455, 476 | per qualifying weld block | `new ClusterTargetCandidate(...)` |
| 551 | per qualifying grind block | `new ClusterTargetCandidate(...)` |
| 798 | per projected weld target on a grid | `new ClusterTargetCandidate(...)` |
| 975, 985, 995 | per floating object / character / inventory bag | `new ClusterFloatingCandidate(...)` |

Each cluster scan creates **100-1000** of these on busy servers. Both classes were small (2 fields each, no inheritance, no virtual methods, no interfaces, mutated only at construction) — ideal candidates for value-type conversion.

Profile session `20260428212757-profiling` (post-BUG-110, 58 BaRs, active grinding) confirmed the pooling pass dropped scan cost 30-40 % across the board, but the recurring 5-7 s spike rhythm was still visible early in the session. With the per-collection allocations gone, the per-candidate allocations were the next-largest GC source.

## Audit (pre-conversion safety check)

Grepped for any pattern that would break under struct semantics:

- `list[i].Block = ...` or `list[i].Attributes = ...` — would fail to compile for structs (mutating a value-type indexer accessor). **None found.**
- `var x = list[i]; x.Block = ...` — would silently no-op for structs (mutating a copy). **None found.**
- All references to `.Block` / `.Attributes` / `.Entity` / `.WorldPosition` outside the constructor are reads.
- `QuickSelect` and `Sort` paths use whole-element swap (`list[i] = list[j]`) — works identically for structs.
- `IComparer<ClusterTargetCandidate>` / `Comparer<ClusterTargetCandidate>.Create((a, b) => ...)` — `IComparer<T>` is interface-based but compares by value, no boxing concern when `T` is the struct type itself.

## Fix

`Cluster/ScanClusterResult.cs` — `class` → `struct` for both:

```csharp
public struct ClusterTargetCandidate
{
    public IMySlimBlock Block;
    public Models.TargetBlockData.AttributeFlags Attributes;
    public ClusterTargetCandidate(IMySlimBlock block, Models.TargetBlockData.AttributeFlags attributes) { ... }
}

public struct ClusterFloatingCandidate
{
    public IMyEntity Entity;
    public Vector3D WorldPosition;
    public ClusterFloatingCandidate(IMyEntity entity, Vector3D worldPosition) { ... }
}
```

Field layouts and constructor signatures unchanged. All callers compile and behave identically because the only operations on the items are construction, list add, sort/swap, and read.

`ScanClusterResult` (the container) stays a class — it's allocated once per scan via reference swap and accessed across threads; struct semantics there would break the publish pattern.

## Allocations eliminated per cluster scan

For an active 58-BaR scan with ~500 weld + 500 grind candidates and ~30 floating targets:

- **~1,030 fewer heap allocations per cluster scan** (was 1 alloc per candidate).
- The `List<T>` backing-array growth still allocates as the lists fill, but that's `O(log N)` allocations (each doubling) instead of `O(N)`.
- **Net per-scan reduction: ~1,000 heap allocations.** With cluster scans firing every 5-7 s on busy servers, that's roughly 8,000-12,000 fewer allocations per minute on the scan thread.

## Sort cost note

Comparator lambdas (`Sort((a, b) => ...)` and `Comparer<T>.Create(...)`) still allocate a closure per call site, but those are bounded by grid count (~10 per scan), not candidate count. They remain a future optimization target.

## Verification

1. **Build clean** — `dotnet build ... -c Release -v minimal` → 0 warnings, 0 errors. ✓
2. **Behavior unchanged** — pure type-system change. The compiler verified no in-place mutation pattern exists; all callers compile.
3. **Re-profile the active grinding scenario** that produced the 5-7 s spike rhythm. Expected outcomes:
   - **Spike rhythm gone or further reduced**: per-candidate allocations were a meaningful contributor; with both BUG-110 and BUG-111 the GC pressure should be substantially lower.
   - **Spike rhythm unchanged from BUG-110**: GC is no longer the dominant cause; remaining spikes are SE-engine work or lock contention.

## Risk note

If a future change adds in-place mutation of these structs — e.g. `myList[5].Attributes |= someFlag;` — the compiler will reject it. That's a feature: the immutability after construction is a property the new struct definition guarantees. To "modify" an item you must replace it: `myList[5] = new ClusterTargetCandidate(myList[5].Block, myList[5].Attributes | someFlag);`.

## See also

- BUG-110 — first allocation pass (pool the per-scan lists/dicts/hashset/queue).
- BUG-098 — earlier hot-path allocation cleanup (BlockKey struct, profiler closure guards).
- BUG-109 — the audit ticket that identified these candidates as a GC-pressure source.
- Profile session: `20260428212757-profiling` (58 BaRs, 4× scan throughput vs `204153`, sim-speed avg 1.00).
