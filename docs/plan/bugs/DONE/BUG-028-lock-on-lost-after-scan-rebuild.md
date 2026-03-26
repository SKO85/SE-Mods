# BUG-028: Lock-on lost after background scan rebuilds target list
## Status: Fixed
## Severity: Critical
## Version: v2.5.0
## Found In: `NanobotSystem.Welding.cs` — lock-on comparison in `ServerTryWelding`

## Summary
When the background scan rebuilds `PossibleWeldTargets`, the `IMySlimBlock` references in the new list are different objects than the one stored in `State.CurrentWeldingBlock`, even though they represent the same physical block. The `==` reference comparison fails, causing the BaR to skip all targets and lose lock-on. The BaR then picks a different block on the next tick, leaving the previous block partially welded.

## Symptoms
- Blocks get partially welded (e.g., 13/25 steel plates) then abandoned
- BaR moves to a different block instead of finishing the current one
- Only occurs on dedicated servers (background scan timing differs from local)
- Profiler shows `lockOnLost=True` with `skipLock=N` equal to `targets=N` (all blocks skipped)

## Root Cause (two issues)

### 1. IMySlimBlock reference mismatch
The background scan creates new `TargetBlockData` objects with `IMySlimBlock` references obtained from fresh grid iteration. On dedicated servers, these references are different objects than the one the welding loop stored in `State.CurrentWeldingBlock`. The `==` operator on `IMySlimBlock` (an interface) performs reference equality, which fails.

### 2. Projected grid EntityId changes
When a block is built from a projection, the projector updates its internal projected grid. This can change the `CubeGrid.EntityId` for remaining projected blocks. If the BaR's lock-on pointed to a projected block (recorded via `lookingForNext`), the `IsSameBlock` identity check fails because the grid EntityId changed. The BaR skipped all targets and wasted the entire tick.

## Fix (two layers)

### Layer 1: Identity-based comparison
Added `IsSameBlock(IMySlimBlock a, IMySlimBlock b)` helper that compares by `CubeGrid.EntityId + Position` instead of reference equality. Applied to all four lock-on comparison points in `ServerTryWelding`:
1. Lock-on skip (fast-forward past earlier blocks)
2. Lock-on found detection (+ reference update to current list entry)
3. Lock-on preservation (component starvation path)
4. Lock-on clear (block no longer weldable)

`ReferenceEquals` short-circuits first for the common case (no scan happened), so performance impact is negligible. This resolved the majority of lock-on losses.

### Layer 2: Graceful retry on lock-on loss
For the remaining cases where `IsSameBlock` can't match (projected grid EntityId changed), added a retry mechanism: after the foreach loop, if lock-on was set but never found, clear the stale lock-on and re-iterate the list immediately (via `goto LockOnRetry`). This ensures the BaR picks up work on the same tick instead of wasting it. The retry only fires once per tick (`lockOnRetry` flag prevents infinite loops).

## Profiler Verification
- Before fix: `lockOnLost=True` on nearly every other tick (dozens per session)
- After Layer 1: `lockOnLost=True` dropped to 8 per 120s session
- After Layer 2: remaining losses are recovered in the same tick (no wasted ticks)

## Files Changed
- `NanobotSystem.Welding.cs`
