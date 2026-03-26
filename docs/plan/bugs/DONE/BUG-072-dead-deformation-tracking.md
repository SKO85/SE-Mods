# BUG-072: Unreachable deformation tracking — MinDeformation never updates
## Status: Fixed
## Severity: Critical
## Version: v2.5.0
## Found In: Code review round 6 — Utils/Utils.cs:35-41
## Description
Inside `NeedRepair()`, the outer condition checks `target.MaxDeformation > MinDeformation` (line 35). The inner condition then checks `target.MaxDeformation < MinDeformation` (line 38), which is mathematically impossible inside the outer block. The `MinDeformation` tracking logic is dead code and never executes.

This means `MinDeformation` stays permanently at its initial value of `0.01f` regardless of actual game block deformation values.
## Root Cause
Logic error — the inner condition contradicts the outer condition.
## Fix
- `Utils/Utils.cs:37-41` — Removed the unreachable inner if block. The `MinDeformation` field remains at its constant `0.01f` default, which is the intended threshold for the deformation check.
