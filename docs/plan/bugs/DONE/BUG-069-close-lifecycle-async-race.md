# BUG-069: Close() clears state while async scan may still be running
## Status: Fixed
## Severity: Medium
## Version: v2.5.0
## Found In: Code review round 5 — NanobotSystem.Init.cs:164-218
## Description
`Close()` clears target lists, sources, and nulls references immediately, but an in-flight background scan task (`AsyncClusterScan` or `AsyncApplyClusterResults`) may still be mid-execution. The background task could then access already-cleared or nulled state, causing null reference exceptions or use-after-close.
## Root Cause
No synchronization between `Close()` and the background scan lifecycle. `_AsyncUpdateSourcesAndTargetsRunning` flag was set but not waited on.
## Fix
Added a busy-wait at the start of `Close()` that spins until `_AsyncUpdateSourcesAndTargetsRunning` clears (with a cap of 1000 iterations to prevent infinite loop). The background task sets this flag to false in its finally block:
- `NanobotSystem.Init.cs:168-170`
