# BUG-054: Block assignments growing — two confirmed assignment leaks
## Status: Fixed
## Severity: High
## Version: v2.5.0
## Found In: In-game testing — DEBUG HUD showing increasing Block Assigns count

## Description

The Block Assigns counter in the debug HUD keeps increasing during welding operations, suggesting that assignments are not being released properly.

## Investigation Results

### Ruled Out (NOT causes)

- **Weldable() weld-mode check:** `IsWeldIntegrityReached()` correctly checks Full (100%), Functional (critical ratio), and Skeleton modes. Not the issue.
- **Component starvation:** The starvation path (Welding.cs:212-220) correctly calls `ReleaseFromSystem()` for both lock-on and non-lock-on blocks.
- **Lock-on survival across scans:** `IsSameBlock()` uses EntityId+Position comparison and reliably matches across scan rebuilds. Reference is updated to the fresh one at line 96.
- **Grid limit eviction (BUG-052):** The lock-on eviction path at lines 156-167 correctly releases the assignment.

### Confirmed Leak #1: Projected block assignment never released

**File:** `NanobotSystem.Welding.cs:422-432`

When a projected block is built and becomes a real block:
1. Projected block has assignment keyed by `projectorGridId:Position`
2. Block becomes physical with key `realGridId:Position` (different grid EntityId)
3. New assignment is created for the physical block (line 432)
4. Old projected assignment is **never released** — sits in TTL for 8 seconds

With many projectors and fast welding (especially Skeleton mode), this accumulates rapidly.

### Confirmed Leak #2: Lock-on vanished — no release on retry

**File:** `NanobotSystem.Welding.cs:264-277`

When the lock-on block vanishes from the target list (e.g., projected grid EntityId changes after projector update):
```csharp
// Line 264-268:
State.CurrentWeldingBlock = null;   // Lock-on cleared
// ... reset counters ...
lockOnRetry = true;
goto LockOnRetry;                   // Re-iterate — but NO ReleaseFromSystem() call!
```

The old block's assignment was refreshed to 8s TTL on the previous tick (line 171). After lock-on loss, nobody releases it.

## Fix

### Fix 1: Release projected assignment before physical transition

**File:** `NanobotSystem.Welding.cs`, before line 424 (inside the `target != null` branch after projected build)

```csharp
// Release the projected block's assignment before switching to the physical block.
if (Mod.Settings.AssignToSystemEnabled)
    targetData.Block.ReleaseFromSystem();
targetData.Block = target;  // existing line 424 — switch to physical
```

### Fix 2: Release assignment in lock-on vanished retry path

**File:** `NanobotSystem.Welding.cs`, at line 264-266 (before clearing CurrentWeldingBlock)

```csharp
if (!lockOnRetry && State.CurrentWeldingBlock != null && !lockOnFound)
{
    // Release the vanished block's assignment before clearing lock-on.
    if (Mod.Settings.AssignToSystemEnabled)
        State.CurrentWeldingBlock.ReleaseFromSystem();
    State.CurrentWeldingBlock = null;
    // ... rest of retry logic ...
```

Both are one-line additions. Together they eliminate the two confirmed sources of assignment accumulation.
