# FEAT-056: /nanobars profile summary command
## Status: Done
## Priority: Low
## Version: v2.5.0
## Summary
Live profiling summary shown in a mission screen dialog via `/nanobars profile summary`.
## Motivation
Admins needed a way to view profiling data in-game without stopping the session or reading log files.
## Design
- Added `GetSummaryText()` to `MethodProfiler` — reads `_methodStats` under lock, formats domain summary + top 20 methods by total time.
- Added `summary` subcommand to `ProfileCommand` — returns `ChatCommandResult.MissionScreen`.
- Works while profiling is running (live snapshot) or after it stops (last collected data).
- Shows: domain breakdown (Scan, Weld, Grind, Collect, Inventory, Update, Utility) and per-method calls, totalMs, avgMs, minMs, maxMs.
- Steady-state stats preferred over total (excludes warmup calls).
## Files Affected
- `Profiling/MethodProfiler.cs` — `GetSummaryText()`
- `Chat/Commands/ProfileCommand.cs` — `summary` subcommand
- `Chat/Commands/HelpCommand.cs` — help text update
## Testing
- Start profiling, run `/nanobars profile summary` — should show live data in dialog.
- Stop profiling, run summary again — should show last collected data.
