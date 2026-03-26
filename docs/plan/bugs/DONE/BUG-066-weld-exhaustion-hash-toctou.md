# BUG-066: Weld exhaustion hash read outside lock (TOCTOU)
## Status: Fixed
## Severity: High
## Version: v2.5.0
## Found In: Code review round 5 — NanobotSystem.Welding.cs:51-52, 290-293
## Description
The weld loop exhaustion optimization reads `State.PossibleWeldTargets.CurrentHash` outside the lock on `State.PossibleWeldTargets`. The background scan thread modifies the hash under that lock, creating a time-of-check-time-of-use (TOCTOU) race. A stale or partially-updated hash could cause the exhaustion check to incorrectly skip or not skip the iteration.
## Root Cause
Hash read (line 52) and write (line 292) both occurred outside `lock (State.PossibleWeldTargets)`, while the background scan rebuilds the hash under that lock.
## Fix
Moved both the exhaustion check and the exhaustion set inside the existing `lock (State.PossibleWeldTargets)` block in `ServerTryWelding`:
- `NanobotSystem.Welding.cs` — check moved from pre-lock to start of lock block; set moved from post-lock to end of lock block.
