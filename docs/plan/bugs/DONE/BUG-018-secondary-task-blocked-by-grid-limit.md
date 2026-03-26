# BUG-018: Secondary task blocked in WeldBeforeGrind / GrindBeforeWeld when all primary targets hit MaxSystemsPerTargetGrid
## Status: Fixed
## Severity: High
## Version: v2.5.0
## Found In: `NanobotSystem.Operations.cs` — WorkMode switch (lines 109, 117)
## Description
When `WeldBeforeGrind` or `GrindBeforeWeld` is set and multiple BaRs are active, the secondary task (grinding/welding) never runs if the primary task has targets in the scan list — even when ALL those targets are blocked by `MaxSystemsPerTargetGrid`. Excess BaRs idle instead of falling through to the secondary task.
## Steps to Reproduce
1. Place 15+ BaRs in `WeldBeforeGrind` mode with `MaxSystemsPerTargetGrid=10`
2. Have a damaged grid in range — first 10 BaRs weld it
3. Have grindable targets also in range
4. Excess BaRs (11-15) idle instead of grinding the available targets
## Root Cause
The fallthrough condition in the WorkMode switch checks `State.PossibleWeldTargets.CurrentCount == 0` (scan list size) rather than whether the primary task actually found a workable target. Grid-limited targets remain in the scan list, keeping `CurrentCount > 0`, so the secondary task branch is never entered.

`GrindIfWeldGetStuck` does not have this bug — it checks `!(welding || transporting)` (actual progress).
## Fix
`NanobotSystem.Operations.cs` — 2 lines changed.

Replaced the scan-list count check with the `needwelding`/`needgrinding` output flag from the primary task loop. These flags are only set `true` after a target passes all filters (including grid limit, assignment, weldability).

**Line 109 (WeldBeforeGrind):**
```csharp
// Before:
if (State.PossibleWeldTargets.CurrentCount == 0 || (script override))
// After:
if (!needwelding || (script override))
```

**Line 117 (GrindBeforeWeld):**
```csharp
// Before:
if (State.PossibleGrindTargets.CurrentCount == 0 || (script override))
// After:
if (!needgrinding || (script override))
```

`needwelding` is set at `Welding.cs:101` — after lock-on skip, script control, assignment, weldability, closed-block, and grid limit checks. `needgrinding` is set at `Grinding.cs:64` — after closed-block, grid limit, AssignToSystem, script control, and not-destroyed checks.
