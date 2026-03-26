# BUG-081: Lock ordering inconsistency in Scanning.cs — potential deadlock
## Status: Open
## Severity: High
## Version: v2.5.0
## Found In: Code review round 6 — NanobotSystem.Scanning.cs (multiple locations)
## Description
The code acquires locks in different orders across different code paths:
- `StartAsyncClusterScan()`: `lock(_Welder)` then later `lock(State.PossibleWeldTargets)` (in the async action)
- `ApplyClusterResultToSelf()`: `lock(State.PossibleWeldTargets)` then `lock(State.PossibleGrindTargets)` then `lock(_PossibleSources)`
- `ServerTryWelding()`: `lock(State.PossibleWeldTargets)` only (no `_Welder`)
- `Close()`: `lock(_Welder)` then later `lock(State.PossibleWeldTargets)`

There is no documented or enforced global lock ordering. If thread A holds `_Welder` and waits for `PossibleWeldTargets`, while thread B holds `PossibleWeldTargets` and calls something that needs `_Welder`, deadlock occurs.
## Steps to Reproduce
Difficult to reproduce — requires specific timing with multiple BaRs on a server. More likely under high load with many background scan threads.
## Root Cause
No consistent lock ordering convention established when the locking model was built.
## Fix
Requires architectural work:
1. Document the intended lock order (e.g., `_Welder` > `PossibleWeldTargets` > `PossibleGrindTargets` > `PossibleFloatingTargets` > `_PossibleSources` > `_PossiblePushTargets`)
2. Audit all lock acquisition sites to enforce the order
3. Extensive testing required — changes to lock ordering can introduce new deadlocks if any site is missed
