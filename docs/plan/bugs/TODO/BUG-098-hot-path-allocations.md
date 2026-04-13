# BUG-098: Hot-path allocations — BlockSystemAssigningHandler string key + unguarded profiler lambdas

## Status: Fixed
## Severity: Medium (main-thread GC pressure)
## Version: v2.5.3
## Found In: `Handlers/BlockSystemAssigningHandler.cs`, `Handlers/SafeZoneHandler.cs`, `NanobotSystem.Operations.cs`, `Helpers/InventoryHelper.cs`

## Description

Two independent allocation hot paths found in the scan/sort/priority audit. Both generate persistent GC pressure without being necessary.

### A. `BlockSystemAssigningHandler.GetBlockKey` — string allocation per call

```csharp
// Handlers/BlockSystemAssigningHandler.cs (before)
private static TtlCache<string, long> Cache = new TtlCache<string, long>(...);

private static string GetBlockKey(IMySlimBlock block)
{
    return string.Format("{0}:{1}", block.CubeGrid.EntityId, block.Position);
}
```

`string.Format("{0}:{1}", long, Vector3I)` allocates a new `string` **plus** boxes the two arguments (`long` and `Vector3I` are both value types), which is ~3 allocations per call (~100-150 bytes).

Called from the **main-thread** weld/grind loops:

| Call site | Function | Frequency in weld loop |
|---|---|---|
| `IsAssignedToOtherSystem` | `NanobotSystem.Welding.cs:105` | per target block iteration |
| `AssignToSystem` | `NanobotSystem.Welding.cs:148, 173` | per target block iteration |
| `ReleaseFromSystem` | `NanobotSystem.Welding.cs:163, 204, 226, 255, 273` | per failure / cleanup / lock-on swap |
| `IsAssignedToOtherSystem` / `AssignToSystem` / `ReleaseFromSystem` | `NanobotSystem.Grinding.cs:51, 68, 91` | per target block iteration |

Worst case per `ServerTryWelding` call with 256 candidates: up to ~768 `GetBlockKey` invocations → **~768 string allocations on a main-thread tick**. For 20 BaRs on a busy base at roughly 5 weld calls/sec each (stagger-adjusted), that's ~77,000 allocs/sec and **multi-MB/s of hot-path garbage** — exactly the kind of GC pressure that eventually triggers gen2 stop-the-world collections and visible sim-speed dips.

### B. Unguarded `MethodProfiler.StopAndLog` lambda call sites

`MethodProfiler.StopAndLog(name, profilerTs, () => string.Format(...))` takes a `Func<string> detailsFactory`. The `() => ...` lambda is **converted to a delegate at the call site**, which means both the delegate object and the closure class capturing locals are allocated **before** the method runs — even when `profilerTs == 0L` and `StopAndLog` will short-circuit.

The established pattern elsewhere in the codebase is to wrap the call in `if (profilerTs != 0L) { StopAndLog(...) }` so the lambda only materializes when profiling is active. Eight sites in hot paths were missing that guard:

| File | Method | Hot-path frequency |
|---|---|---|
| `SafeZoneHandler.cs:90` | `GetSafeZones` | Periodic (low, but still) |
| `SafeZoneHandler.cs:286` | `GetIntersectingSafeZone` | Per-grid during scan + per-block via `IsProtectedFromGrinding` |
| `SafeZoneHandler.cs:539` | `IsProtectedFromGrinding` | **Per grind candidate** in `AsyncAddBlockIfGrindTarget` |
| `NanobotSystem.Operations.cs:404` | `TryTransmitState` (skip-not-needed exit) | **Main thread, per tick per BaR** |
| `NanobotSystem.Operations.cs:412` | `TryTransmitState` (skip-interval exit) | **Main thread, per tick per BaR** |
| `NanobotSystem.Operations.cs:434` | `TryTransmitState` (send exit) | **Main thread, per tick per BaR** |
| `Helpers/InventoryHelper.cs:56` | `AddIfConnectedToInventory` (cache-hit path) | Per source block during source scan |
| `Helpers/InventoryHelper.cs:77` | `AddIfConnectedToInventory` (cache-miss path) | Per source block during source scan |

`TryTransmitState` is the worst: it runs on the main thread once per tick per BaR (via `ServerTryWeldingGrindingCollecting`), so for 20 BaRs at 60 Hz that's 1,200 unnecessary lambda allocations per second even with profiling disabled. `IsProtectedFromGrinding` is second-worst because it's per grind candidate during scan.

## Fix

### A. Struct key replaces string key

`BlockSystemAssigningHandler.cs` — new `BlockKey` struct implementing `IEquatable<BlockKey>`:

```csharp
public struct BlockKey : IEquatable<BlockKey>
{
    public readonly long GridEntityId;
    public readonly Vector3I Position;

    public BlockKey(long gridEntityId, Vector3I position)
    {
        GridEntityId = gridEntityId;
        Position = position;
    }

    public bool Equals(BlockKey other)
    {
        return GridEntityId == other.GridEntityId
            && Position.X == other.Position.X
            && Position.Y == other.Position.Y
            && Position.Z == other.Position.Z;
    }

    public override bool Equals(object obj)
    {
        if (!(obj is BlockKey)) return false;
        return Equals((BlockKey)obj);
    }

    public override int GetHashCode()
    {
        unchecked
        {
            var hash = 17;
            hash = hash * 31 + GridEntityId.GetHashCode();
            hash = hash * 31 + Position.X;
            hash = hash * 31 + Position.Y;
            hash = hash * 31 + Position.Z;
            return hash;
        }
    }
}

private static TtlCache<BlockKey, long> Cache = new TtlCache<BlockKey, long>(TimeSpan.FromSeconds(8));

private static BlockKey GetBlockKey(IMySlimBlock block)
{
    return new BlockKey(block.CubeGrid.EntityId, block.Position);
}
```

`IEquatable<BlockKey>` is required so `ConcurrentDictionary<BlockKey, CacheItem>` uses `EqualityComparer<T>.Default` without boxing on hash lookups. The struct is constructed on the stack at each call site — zero heap allocation per call. External API is unchanged (the extension methods still take `IMySlimBlock`).

C# 6 compatibility: the object `Equals` override uses `if (!(obj is BlockKey)) return false; return Equals((BlockKey)obj);` instead of C# 7's pattern `obj is BlockKey other`.

### B. `if (profilerTs != 0L)` guards on the 8 unguarded sites

Each unguarded `MethodProfiler.StopAndLog(...)` is now wrapped:

```csharp
if (profilerTs != 0L)
{
    var _result = result;   // hoist locals so the lambda only captures _-prefixed copies
    MethodProfiler.StopAndLog("Name", profilerTs, () => string.Format(...));
}
```

Values captured by the lambda are copied into `_`-prefixed locals inside the guard so the original variables can still be returned or reused after the guard. This matches the established pattern in `Welding.cs`, `Grinding.cs`, and `Scanning.cs`.

## Performance Impact

- **(A) String → struct key**: eliminates ~768 string allocations per `ServerTryWelding` call per BaR on the main thread. For 20 BaRs on a busy base, cuts hot-path GC pressure by ~several MB/s. Struct construction is stack-only (~ns per call), dictionary hash/equals uses `IEquatable<T>` with no boxing.
- **(B) Profiler guards**: eliminates 1,200+ lambda allocations per second on the main thread (from `TryTransmitState` alone at 20 BaRs @ 60 Hz), plus ~5,000-20,000 background-thread allocations per second (from `SafeZoneHandler` + `InventoryHelper` per-block sites). With profiling disabled, these sites now allocate **zero** garbage.
- **Net**: no behavior change, only allocation reduction. Expected outcome: fewer gen2 GC pauses on long-running servers and marginally more headroom for the welding/grinding hot path.

## Verification

1. **Build**: `dotnet build SKO-Nanobot-BuildAndRepair-System/Data/Scripts/SKO-Nanobot-BuildAndRepair-System/SKO-Nanobot-BuildAndRepair-System.csproj -c Release -v minimal` → 0 warnings, 0 errors.
2. **(A) Assignment cache correctness**: place two BaRs on the same grid, let them compete for the same target block. Verify `AssignmentCount` (`/nanobars debug show`) stays stable and assignments correctly lock blocks to one BaR at a time. Verify TTL expiration still works (assignments vanish after `AssignmentTtlSeconds`).
3. **(A) Same-block identity**: force the background scan to produce fresh `IMySlimBlock` references (wait one scan cycle, ~2s) and verify `IsAssignedToOtherSystem`/`AssignToSystem` still correctly identify the block across reference changes. The struct key is `(gridId, position)` so fresh references for the same physical block produce identical keys — verified by the BaR holding its assignment across scan cycles.
4. **(B) Profiling on**: `/nanobars profile start 60` — verify all eight profile lines appear in the output manifest:
   - `SafeZoneHandler.GetSafeZones`
   - `SafeZoneHandler.GetIntersectingSafeZone`
   - `SafeZoneHandler.IsProtectedFromGrinding`
   - `TryTransmitState` (three action types: skip-notNeeded, skip-interval, send)
   - `AddIfConnectedToInventory` (cached=true and cached=false variants)
5. **(B) Profiling off**: run the mod with profiling disabled. No behavior change, no profile log output. Expected: lower GC pressure under load.
6. **Regression — `AssignmentCount` HUD line**: `/nanobars debug show` — `Block assignments` count on the HUD should display correctly and update as BaRs claim/release blocks.

## See also

- BUG-005 (v2.5.0) — eliminated `IMySlimBlock` reference equality as the cache key by switching to a string key. This ticket supersedes that with a value-type struct key, keeping correctness while eliminating the string allocation BUG-005's fix introduced.
- BUG-070 (v2.5.0) — profiler closure allocations; established the `if (profilerTs != 0L)` guard pattern. Most sites adopted it; the eight sites fixed here were missed at the time.
- BUG-097 (v2.5.3) — companion ticket from the same audit pass covering work-mode correctness + priority hash race + GridSystemCount dip.
- FEAT-070 (v2.5.3) — sort comparator consolidation from the same audit pass.
