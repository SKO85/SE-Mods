# BUG-073: Close() spin-wait without lock — may not observe background write
## Status: Fixed
## Severity: Critical
## Version: v2.5.0
## Found In: Code review round 6 — NanobotSystem.Init.cs:170-171
## Description
`Close()` polls `_AsyncUpdateSourcesAndTargetsRunning` in a tight loop (`while (flag && --maxWait > 0) { }`) without acquiring any lock. The background scan thread sets this flag to `false` inside `lock(_Welder)` in its finally block. Without the matching lock on the read side, the CPU may cache the stale `true` value, causing the loop to spin all 1000 iterations and then proceed to clear shared state while the async task is still running.
## Root Cause
Read of cross-thread flag outside the lock that protects its write.
## Fix
- `NanobotSystem.Init.cs:170-175` — Replaced the bare spin-wait with a lock-checked loop: reads `_AsyncUpdateSourcesAndTargetsRunning` inside `lock(_Welder)` to match the write-side synchronization. Added a 5-second deadline instead of an iteration counter for bounded wait time.
