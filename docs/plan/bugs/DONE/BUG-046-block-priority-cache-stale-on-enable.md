# BUG-046: BlockPriorityHandling cache ignores block Enabled state changes
## Status: Fixed
## Severity: Low
## Version: v2.5.0
## Found In: Code review — Handlers/BlockPriorityHandling.cs

## Description

The static `GetItemKeyCache` has a 5-minute TTL. When `real=false` (used during priority sorting), the block class depends on `IMyFunctionalBlock.Enabled` — disabled blocks return `ArmorBlock` (lowest priority). However, the cache doesn't invalidate when a block's Enabled state changes.

**Scenario:**
1. Block X (e.g., Thruster) is disabled → cached as ArmorBlock (14) for `real=false`
2. Player re-enables Block X
3. For up to 5 minutes, the cache still returns ArmorBlock priority
4. Block X is welded at low priority instead of its actual Thruster priority

The `_HashDirty` bypass at line 69 only triggers when the user changes priority list settings, NOT when individual blocks change their Enabled state.

## Root Cause

`GetItemKeyCache` TTL (5 minutes) is too long for a value that depends on mutable block state (`IMyFunctionalBlock.Enabled`). The cache key is `block.EntityId` (negated for `real=false`), so there's no way to detect state change without either:
- Reducing TTL
- Invalidating on block state change events

## Fix

Reduce `GetItemKeyCache` TTL from 5 minutes to ~30 seconds. The cache still provides value (avoids per-block type-checking every tick) but refreshes fast enough to track Enabled state changes:
```csharp
public static readonly TtlCache<long, int> GetItemKeyCache = new TtlCache<long, int>(
    defaultTtl: TimeSpan.FromSeconds(30),
    ...);
```

Alternatively, only cache the `real=true` variant (which doesn't depend on Enabled state) and always compute `real=false` live.
