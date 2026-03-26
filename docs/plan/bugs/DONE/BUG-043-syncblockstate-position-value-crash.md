# BUG-043: SyncBlockState.AssignReceived crashes on null Position.Value
## Status: Fixed
## Severity: Medium
## Version: v2.5.0
## Found In: Code review — Models/SyncBlockState.cs

## Description

In `AssignReceived()`, the code accesses `item.Entity.Position.Value` without checking `HasValue` first. `Position` is `Vector3I?` (nullable). If a corrupted or incomplete network message arrives with `EntityId == 0` but `Position == null`, calling `.Value` throws `InvalidOperationException`.

**Line 608 (weld targets):**
```csharp
var block = grid?.GetCubeBlock(item.Entity.Position.Value);
```

**Line 643 (grind targets):**
```csharp
var block = grid?.GetCubeBlock(item.Entity.Position.Value);
```

## Root Cause

The ProtoBuf deserialization of `SyncEntityId` can produce an object where `Position` is null if the serialized data is incomplete (e.g., version mismatch, truncated message, or packet corruption). The code assumes Position is always set when `EntityId == 0`, which is true for well-formed data from `GetSyncId()` but not guaranteed after deserialization.

## Fix

Add `HasValue` guard before accessing `.Value`:
```csharp
if (item.Entity.Position.HasValue)
{
    var block = grid?.GetCubeBlock(item.Entity.Position.Value);
    if (block != null)
    {
        PossibleWeldTargets.Add(new TargetBlockData(block, item.Distance, 0));
    }
}
```

Apply same fix at both line 608 and line 643.
