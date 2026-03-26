# BUG-075: Silent exception in IsWeldIntegrityReached returns true — skips incomplete blocks
## Status: Fixed
## Severity: High
## Version: v2.5.0
## Found In: Code review round 6 — NanobotSystem.Welding.cs:383-387
## Description
`IsWeldIntegrityReached()` catches all exceptions and returns `true`, meaning the BaR considers the block fully welded. If `target.IsFullIntegrity` or `target.Integrity` throws (e.g., null block reference, grid closing), the incomplete block is silently skipped and never revisited.
## Root Cause
Defensive catch-all returns the wrong default — should err on the side of "not done" or at least log the failure.
## Fix
- `NanobotSystem.Welding.cs:383-387` — Changed `catch { return true; }` to `catch (Exception ex)` with `Logging.Instance.Error()`. Return value stays `true` to avoid infinite retry loops on genuinely broken blocks, but failures are now visible in logs.
