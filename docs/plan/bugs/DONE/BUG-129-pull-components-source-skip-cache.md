# BUG-129: `PullComponents` walks all 94 sources via `FindItem` per missing component — cache "source has component" to skip sources known not to contain it

## Status: Fixed
## Severity: High (`pullPickMs=6.5 ms` on `LargeRefinery` projected, dominant per-tick spike on large projection welding)
## Version: v2.5.5
## Found In: `NanobotSystem.Inventory.cs:280-339` (`PullComponents` source-walk loop)

## Description

Profile session `20260429214111-profiling` (60 s, large projection, welding active):

```
2026-04-29 21:41:20Z;ms=6.535;block=LargeRefinery;projected=True;
                     getMissingMs=0.008;pullPickMs=6.509
2026-04-29 21:41:30Z;ms=5.969;block=LargeHalfSlopeArmorBlock;projected=True;
                     getMissingMs=0.003;pullPickMs=5.955
2026-04-29 21:41:31Z;ms=4.518;block=LargeAssembler;projected=True;
                     getMissingMs=0.004;pullPickMs=4.502
```

`pullPickMs` (BUG-122 sub-timer wrapping the inner `ServerFindMissingComponents(ref)`
overload — which iterates missing components and calls
`PullComponents`/`ServerPickFromWelder` per component) dominates `ServerFindMissingComponents`
total cost. Of the 6.5 ms spike, the SE-engine `GetMissingComponents` itself is
8 µs — the rest is the source-walk inside `PullComponents`.

`PullComponents` source-walk loop, per missing component, per call:

```csharp
lock (_PossibleSources)
{
    foreach (var srcInventory in _PossibleSources)            // 94 sources in 3-large-ship cluster
    {
        var srcOwner = srcInventory.Owner as IMyEntity;
        if (srcOwner == null || srcOwner.MarkedForClose) continue;

        if (srcInventory.FindItem(componentId) != null         // engine call per source
            && srcInventory.CanTransferItemTo(welderInventory, componentId))  // engine call per source
        {
            // ... actual transfer attempt ...
        }
    }
}
```

Per call: up to 94 × `FindItem` engine calls + 94 × `CanTransferItemTo`
checks for sources that don't have the component.

Compounding: `ServerFindMissingComponents` calls `PullComponents` once per
missing component. A `LargeRefinery` projection has many components
(SteelPlate, InteriorPlate, Construction, Computer, MetalGrid, LargeTube, …).
Per-weld-tick: `N components × 94 sources × FindItem` engine calls. With
default `WeldingMultiplier=1` only 1 of each component is actually pulled per
call (budget caps at ~1 SteelPlate volume), so the work is heavy relative to
the throughput.

## Root Cause

For each (source, component) pair the engine does a linear scan of the
source's items. Most sources don't have a given component — all those
`FindItem` calls return `null` after walking the source's items. There's no
short-circuit telling us "this source can't have this component, skip it."

## Fix — Source-has-component cache

Mirror BUG-119's `ConnectionCache` pattern. Cache the boolean
"source contains component" with TTL; skip sources whose cached value is
`false`.

### Cache key and TTL

- Key: `MyTuple<long, string>` — `(srcOwner.EntityId, componentId.SubtypeName)`.
  Using SubtypeName string instead of `MyDefinitionId` keeps the key value-type
  and avoids GC on the hot path.
- Value: `bool` (true = source had the component at last check).
- TTL: 30 s. Long enough to amortize the engine call across many BaR weld
  ticks, short enough that container content drift (player adds new component
  types, refinery produces new outputs) corrects automatically.

### Cache invalidation

- **Successful pull** (we transferred items from a source): leave the cache
  entry alone. The source still has the component (we removed some, didn't
  empty it most of the time); if we drained it fully, next miss in 30 s.
- **Failed pull** (source has the component but `MaxItemsAddable=0`, welder
  full): leave cache entry alone — the source still has it, just we can't
  receive.
- Any other path: TTL handles it.

False-cache cases:
- **Stale "false"** (source acquired the component since cache check) → BaR
  skips this source for up to 30 s. Worst case: BaR pulls from another source
  that also has it; if no other source has it, BaR misses the pull until cache
  expires. Mitigation: cache TTL kept low (30 s). Acceptable trade-off vs the
  per-call cost.
- **Stale "true"** (source had it at cache time, now empty): existing
  `FindItem` runs, returns `null`, no transfer. Wasted call but identical
  outcome to today.

### Implementation

In `Helpers/InventoryHelper.cs`, add a static cache mirroring
`ConnectionCache`:

```csharp
// BUG-129: source-has-component cache. Mirrors ConnectionCache pattern.
// Key: (srcOwner.EntityId, componentSubtypeName). Skips sources known not to
// contain a given component during PullComponents' source walk.
public static readonly TtlCache<MyTuple<long, string>, bool> SourceHasComponentCache =
    new TtlCache<MyTuple<long, string>, bool>(
        defaultTtl: TimeSpan.FromSeconds(30),
        comparer: new MyTupleComparer<long, string>(),
        concurrencyLevel: 4,
        capacity: 4096);
```

In `NanobotSystem.Inventory.cs:290-298` `PullComponents` source-walk:

```csharp
foreach (var srcInventory in _PossibleSources)
{
    var srcOwner = srcInventory.Owner as IMyEntity;
    if (srcOwner == null || srcOwner.MarkedForClose) continue;

    // BUG-129: skip sources cached as "doesn't have this component".
    var cacheKey = new MyTuple<long, string>(srcOwner.EntityId, componentId.SubtypeName);
    bool cachedHas;
    if (InventoryHelper.SourceHasComponentCache.TryGet(cacheKey, out cachedHas) && !cachedHas)
    {
        continue;
    }

    var hasItem = srcInventory.FindItem(componentId) != null;
    InventoryHelper.SourceHasComponentCache.Set(cacheKey, hasItem);
    if (!hasItem) continue;

    if (srcInventory.CanTransferItemTo(welderInventory, componentId))
    {
        // ... existing transfer path ...
    }
}
```

The cache is hit before the `FindItem` engine call, and updated after. A
"true" entry doesn't skip the call — we still need `FindItem` to get the item
index for transfer — but we get the per-source-skip benefit on the dominant
"no" path.

### Cleanup

`InventoryHelper.Cleanup()` already runs every 2 minutes (existing TTL
sweep). Add `SourceHasComponentCache.CleanupExpired()` alongside the existing
`ConnectionCache` cleanup.

## Verification

1. **Build clean** — `dotnet build ... -c Release -v minimal`.
2. **Re-profile** the welding-projection scenario.
3. **Expected**:
   - `ServerFindMissingComponents` `pullPickMs` drops from 5-6 ms range to
     sub-millisecond on cache hits.
   - `ServerTryWelding` total `ms` correspondingly lower on weld-active ticks.
   - Sim-speed dips during heavy-component-projection welding should reduce.
4. **Cache hit rate visibility**: not exposed today; if needed, add a
   `cacheHits / cacheMisses` counter to `PullComponents`'s profiler line for
   the next session. Out of scope for this ticket — first measure the gross
   cost reduction.
5. **No behavioural change** when cache empty (cold start) or all entries
   stale — falls through to existing engine-call path.

## Why this is safe

- Cache is per-mod-singleton; concurrent access protected by `TtlCache`'s
  internal `ConcurrentDictionary`.
- Stale `true` entries → wasted `FindItem` call but correct outcome.
- Stale `false` entries → BaR misses pulling from a newly-stocked source for
  up to 30 s, then automatically re-checks. Doesn't break grinding/welding,
  just delays a single component pickup if no alternative source has the
  component.
- TTL cleanup runs in the existing 2-minute timer; no new background work.

## See also

- BUG-119 — `AddIfConnectedToInventory` `ConnectionCache` (60 s TTL). Same
  shape, same `TtlCache<MyTuple, bool>` pattern. Established baseline for
  this design.
- BUG-126 — welder-full early-out in `PullComponents`. Independent fix
  targeting the welder-full state, this ticket targets the source-walk cost.
- Related `WeldingMultiplier` setting — players can already raise this to get
  bigger per-call pulls (which reduces call count). BUG-129 makes each call
  cheap regardless of bulk size.
