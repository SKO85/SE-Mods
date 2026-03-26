# BUG-074: Silent exception swallowing in shield checks
## Status: Fixed
## Severity: High
## Version: v2.5.0
## Found In: Code review round 6 — NanobotSystem.State.cs:65-97
## Description
`IsShieldProtected()` and `IsWelderShielded()` both have bare `catch { }` blocks that silently swallow all exceptions. If the DefenseShields API throws (e.g., mod conflict, null entity), the failure is invisible. The methods return `false` (not protected), potentially allowing BaRs to weld/grind blocks that should be shielded.
## Root Cause
Defensive exception handling with no logging.
## Fix
- `NanobotSystem.State.cs:75` — Changed `catch { }` to `catch (Exception ex)` with `Logging.Instance.Error()` in `IsShieldProtected`.
- `NanobotSystem.State.cs:92-94` — Same change in `IsWelderShielded`.
