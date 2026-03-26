# BUG-079: SystemsCommand empty filter matches all players/grids
## Status: Fixed
## Severity: Medium
## Version: v2.5.0
## Found In: Code review round 6 — Chat/Commands/SystemsCommand.cs:68-73
## Description
When `--owner` or `--grid` is specified without an argument (e.g., `/nanobars systems list --owner`), `Array.Copy` creates a zero-length array and `string.Join` produces an empty string. `string.IndexOf("")` returns 0 for any input, so the empty filter matches all players or grids. This leaks all BaR information and can enable/disable all BaRs unexpectedly.
## Root Cause
No validation that the filter string is non-empty after parsing.
## Fix
- `SystemsCommand.cs:73` — Added `if (string.IsNullOrEmpty(ownerFilter)) return ChatCommandResult.Error(...)` after parsing the owner filter in `ExecuteList`.
- `SystemsCommand.cs:216,251` — Added same empty-string validation for `--grid` and `--owner` filters in `ExecuteSetEnabled`.
