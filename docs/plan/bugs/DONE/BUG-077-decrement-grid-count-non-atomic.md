# BUG-077: DecrementGridCount TryGetValue+TryUpdate non-atomic — count drift
## Status: Fixed
## Severity: Medium
## Version: v2.5.0
## Found In: Code review round 6 — Mod.cs:43-59
## Description
`DecrementGridCount()` reads the current value with `TryGetValue`, computes the new value, then writes with `TryUpdate(key, newVal, current)`. If two BaR systems release the same grid in the same frame, the second `TryUpdate` fails silently because `current` no longer matches the actual value. The count becomes permanently inflated for that grid, eventually blocking new BaRs from targeting it (MaxSystemsPerTargetGrid hit prematurely).
## Root Cause
Non-atomic read-modify-write on ConcurrentDictionary without retry.
## Fix
- `Mod.cs:43-59` — Wrapped the TryGetValue + TryUpdate/TryRemove sequence in a `while` loop (CAS pattern). If TryUpdate or TryRemove fails because the value was modified concurrently, the loop re-reads and retries.
