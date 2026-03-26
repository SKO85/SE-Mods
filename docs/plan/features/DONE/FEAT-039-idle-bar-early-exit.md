# FEAT-039: Early Exit in ServerTryWeldingGrindingCollecting for Idle BaRs

## Status: Done
## Priority: Low
## Version: v2.5.0

## Summary

Add an early-exit path at the top of `ServerTryWeldingGrindingCollecting` for BaRs with zero targets in all categories and no active transport, avoiding the overhead of calling into sub-methods.

## Motivation

Profiling (180s DS, 118 BaRs) shows ~60 idle BaRs (GrindBeforeWeld with 0 targets) cost 0.15-0.27ms each in `ServerTryWeldingGrindingCollecting`, even though `ServerTryWelding` and `ServerTryGrinding` both exit in 0.002ms via their own early-exit paths.

The 0.15ms overhead per idle BaR comes from the parent method's:
- Transport state checks
- Calling into `ServerTryWelding` (which immediately exits)
- Calling into `ServerTryGrinding` (which immediately exits)
- Calling into `ServerTryCollectingFloatingTargets` (which immediately exits)
- Calling into `ServerTryPushInventory` (which immediately exits)

### Profiling Evidence (consistent across both sessions)

```
# Idle BaR in ServerTryWeldingGrindingCollecting: 0.15-0.27ms
# Same BaR in ServerTryWelding: 0.002ms (exhaustedSkip=True)
# Overhead = 0.15 - 0.002 = ~0.15ms from parent dispatching

# Impact: 60 idle BaRs × 0.15ms / 3 stagger groups × 2 ticks/s ≈ 6ms/s
```

## Design

At the top of `ServerTryWeldingGrindingCollecting`, after the initial state checks but before entering the work-mode switch:

```csharp
// Quick exit for BaRs with nothing to do
if (State.PossibleWeldTargets.CurrentCount == 0 &&
    State.PossibleGrindTargets.CurrentCount == 0 &&
    State.PossibleFloatingTargets.CurrentCount == 0 &&
    !IsTransporting &&
    !inventoryFull)
{
    return;
}
```

This skips all sub-method calls when there's clearly nothing to do.

## Files Affected

| File | Change |
|------|--------|
| `NanobotSystem.Operations.cs` | Add early-exit check at top of `ServerTryWeldingGrindingCollecting` |

## Testing

1. Verify idle BaRs (0 targets, not transporting) no longer show 0.15ms in profiler for this method.
2. Verify BaRs that HAVE targets still work normally.
3. Verify a BaR that transitions from idle to active (targets appear after scan) resumes work promptly.
4. Verify transport-in-progress BaRs are NOT skipped (they need to complete transport).
