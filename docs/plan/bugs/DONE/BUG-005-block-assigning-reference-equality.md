# BUG-005: IMySlimBlock cache key uses reference equality

## Status: Fixed
## Severity: Medium
## Version: v2.5.0
## Found In: Code Review / Handlers/BlockSystemAssigningHandler.cs:9

## Description

`BlockSystemAssigningHandler` uses a `TtlCache<IMySlimBlock, long>` where `IMySlimBlock` (an interface) is the dictionary key. Since `IMySlimBlock` is an interface, the `ConcurrentDictionary` inside `TtlCache` will use `object.ReferenceEquals` for key comparison (the default `EqualityComparer<T>.Default` for reference types without `GetHashCode`/`Equals` overrides).

This means that if the game engine returns a *different object instance* for the same logical slim block (e.g., after a grid merge/split, block repair, or internal cache refresh), the cache lookup will fail to find the existing entry. This could lead to:
- The same block being assigned to multiple BaR systems simultaneously
- Stale entries not being matched for release

```csharp
// BlockSystemAssigningHandler.cs:9
private static TtlCache<IMySlimBlock, long> Cache = new TtlCache<IMySlimBlock, long>(TimeSpan.FromSeconds(8));
```

## Steps to Reproduce

1. Have a BaR system assign to a block on a grid.
2. Trigger a grid merge or split that causes the game to recreate `IMySlimBlock` instances.
3. Attempt to check or release the assignment — the cache lookup misses because the new `IMySlimBlock` instance has a different reference.

## Root Cause

`IMySlimBlock` is an interface that does not guarantee value-based equality. The underlying implementation (`MySlimBlock`) may or may not override `GetHashCode`/`Equals`. If it doesn't, reference equality is used, which breaks after object recreation.

## Fix

**Option A:** Use a stable identifier as the cache key instead of the object reference:

```csharp
// Use a composite key of GridEntityId + BlockPosition
private static TtlCache<string, long> Cache = new TtlCache<string, long>(TimeSpan.FromSeconds(8));

// Key generation helper:
private static string GetBlockKey(IMySlimBlock block)
{
    return string.Format("{0}:{1}", block.CubeGrid.EntityId, block.Position);
}
```

**Option B:** Verify that the SE game engine's `MySlimBlock` implementation does override `GetHashCode`/`Equals` with value-based semantics. If confirmed, this bug can be closed as "Won't Fix" with documentation.

**Recommendation:** Option A is safer and doesn't depend on game engine internals that could change between SE updates.
