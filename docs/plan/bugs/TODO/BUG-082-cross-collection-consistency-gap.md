# BUG-082: Cross-collection consistency gap in ApplyClusterResultToSelf
## Status: Open
## Severity: Medium
## Version: v2.5.0
## Found In: Code review round 6 — NanobotSystem.Scanning.cs:1392-1436
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
## Steps to Reproduce
Requires tight timing: main thread reads grind targets between the weld swap and grind swap. Self-correcting next tick.
## Root Cause
Three separate locks instead of a single atomic swap for all collections.
## Fix
Options:
1. Introduce a single `_targetSwapLock` object that wraps all three swaps. Requires updating all consumer lock sites (ServerTryWelding, ServerTryGrinding, ServerTryCollecting, UpdateCustomInfo) to use the same lock — invasive change.
2. Accept as known limitation — the one-tick inconsistency is self-correcting and unlikely to cause visible issues.
