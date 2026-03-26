# BUG-048: Welding assignment not released for non-weldable blocks
## Status: Fixed
## Severity: Low
## Version: v2.5.0
## Found In: Code review — NanobotSystem.Welding.cs

## Description

In `ServerTryWelding()` at lines 232-247, when `Weldable()` returns false and the block is NOT marked as `Ignore`, the assignment (`AssignToSystem`) is not released. The block remains claimed by this BaR until the TTL expires (8 seconds default).

```csharp
else  // Weldable() returned false
{
    if (targetData.Ignore)
    {
        if (Mod.Settings.AssignToSystemEnabled) targetData.Block.ReleaseFromSystem();
        State.PossibleWeldTargets.ChangeHash();
    }
    if (isLockOnBlock)
    {
        State.CurrentWeldingBlock = null;
    }
    // No ReleaseFromSystem for non-Ignore, non-weldable blocks!
}
```

**Scenario:** Another BaR completes welding a block between scans. This BaR's target list still contains the block. When it tries to weld, `Weldable()` returns false (block is complete), but the assignment slot is held until TTL expiry.

## Root Cause

The `ReleaseFromSystem` call is only inside the `if (targetData.Ignore)` branch, not in the general non-weldable case.

## Fix

Release assignment for all non-weldable blocks, not just Ignore ones:
```csharp
else
{
    if (Mod.Settings.AssignToSystemEnabled) targetData.Block.ReleaseFromSystem();
    if (targetData.Ignore)
    {
        State.PossibleWeldTargets.ChangeHash();
    }
    if (isLockOnBlock)
    {
        State.CurrentWeldingBlock = null;
    }
}
```

Impact is bounded (next scan removes block from list within ~2s), but releasing early frees the slot for other BaRs immediately.
