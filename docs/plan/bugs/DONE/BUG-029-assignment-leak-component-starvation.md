# BUG-029: Assignment leak during component starvation causes BaR lockout
## Status: Fixed
## Severity: High
## Version: v2.5.0
## Found In: `NanobotSystem.Welding.cs`, `NanobotSystem.Grinding.cs`

## Summary
When a BaR assigns a block via `AssignToSystem` but then can't weld it (no components available), it fell through the loop without releasing the assignment. The BaR then assigned the NEXT block, failed again, and repeated — potentially locking all targets to itself. Other BaRs saw `skipAssign=128` and went completely idle.

## Symptoms
- Profiler shows `skipAssign` values of 94-128 (near-total lockout)
- BaRs idle despite many available targets
- `welding=False, needWelding=True, skipAssign=128` pattern across all BaRs

## Fix
### Welding loop
- Release assignment when welding fails (component starvation) for non-lock-on blocks
- Added early-exit after 3 consecutive component failures to prevent thundering herd
- Lock-on block is preserved (not released) so the BaR retries it next tick

### Grinding loop
- Release assignment when `TryClaimGrindSlot` fails after block was assigned
- Release assignment unconditionally when `ServerDoGrind` returns false (simplified from `Ignore || IsFullyDismounted` condition)

## Files Changed
- `NanobotSystem.Welding.cs`
- `NanobotSystem.Grinding.cs`
