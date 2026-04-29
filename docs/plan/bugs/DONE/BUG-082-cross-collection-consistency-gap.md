# BUG-082: Cross-collection consistency gap in ApplyClusterResultToSelf
## Status: Won't Fix (accepted as known limitation)
## Severity: Medium (theoretical only — no observable impact)
## Version: v2.5.0
## Found In: Code review round 6 — `NanobotSystem.Scanning.cs` (target-list swap sites; current code lines ~2360, ~2454, ~1085, ~1472)

## Description
When applying scan results, each target list is swapped under its own separate lock:
```
lock (State.PossibleWeldTargets) { clear + refill }
// lock released
lock (State.PossibleGrindTargets) { clear + refill }
// lock released
lock (State.PossibleFloatingTargets) { clear + refill }
```
Between the three lock/unlock cycles, the main thread could observe weld targets from the new scan but grind targets from the old scan, creating an inconsistent world view for one tick.

## Resolution: Won't Fix

After review, the trade-off favors leaving the current finer-grained locking in place.

### Why no fix is warranted

- **Self-correcting**: the next tick (~16 ms) re-aligns the world view; the inconsistency cannot accumulate or compound.
- **No incorrect behavior**: a stale weld entry next to a fresh grind list means at worst one wasted iteration — `NeedRepair` returns false on a no-longer-needed block, or the block is already `Closed`. No incorrect operation is performed.
- **Race window is microseconds**: the gap exists only between lock-release/lock-acquire on consecutive locks; the main thread has to read both lists exactly within that window to observe it.

### Why the proposed fix would regress

Option 1 (single `_targetSwapLock` wrapping all three swaps) would:
- Touch every consumer site (`ServerTryWelding`, `ServerTryGrinding`, `ServerTryCollecting`, `UpdateCustomInfo`) to use the unified lock.
- **Increase lock contention** by serializing three currently-independent locks. The main thread reading weld targets would now block on a grind-list refill, and vice versa. That's a measurable performance regression in exchange for closing a non-observable race.

### Why profiling won't help

This is a correctness/race concern, not a performance issue:
- The race only manifests at lock-release timing boundaries (microseconds wide).
- Even if reproduced, the visible effect is "one wasted tick" — invisible in profile logs.
- Profiling adds no signal here; reproduction would require pause-and-step debugging.

## See also

- `NanobotSystem.Scanning.cs` lines ~2360, ~2389, ~2397 — `ApplyClusterResultToSelf` swap sites (the original 1392-1436 region after intervening edits).
- BUG-083 — adjacent concern about TimeSpan memory barrier; similar low-impact race profile.
