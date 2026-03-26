# FEAT-001: Method Profiler
## Status: Done
## Priority: Medium
## Version: 2.5.0
## Summary
Add a built-in method-level profiler that server admins can start/stop via chat commands, writing per-method timing logs and a summary to local storage.
## Motivation
Diagnosing performance issues on live servers requires visibility into which methods are slow. The profiler lets admins capture timing data in-game without attaching external tools.
## Design
- **MethodProfiler** (`Profiling/MethodProfiler.cs`): static class with `Start()` / `StopAndLog()` API. Tracks per-method call counts, min/max/avg durations, warmup vs steady-state stats. Writes per-method `.log` files and a `NanobotProfiler.Summary.log` with domain-level aggregates (Scan, Weld, Grind, Push, Update, Utility). Supports auto-stop (default 5 min, max 24h).
- **Chat commands** (admin-only, server-only):
  - `/nanobars profile start [seconds]` — starts a profiling session
  - `/nanobars profile stop` — stops and writes summary
  - `/nanobars profile status` — shows current state
- **Mod settings** (`ModSettings.xml`):
  - `EnableMethodProfiling` (bool, default `false`) — master switch
  - `MethodProfilingMinDurationMs` (int, default `1`, clamped 0-10000) — minimum duration to log a sample
- Settings version bumped from 6 to 7.
- `/nanoboars` added as a chat command alias.
- **Mod.cs integration**:
  - `MethodProfiler.TickAutoStop()` called every frame (server-only) to check the auto-stop timer.
  - `MethodProfiler.Close()` called in `UnloadData()` to release file handles on session end.
- **Guard**: All `/nanobars profile` subcommands are blocked early in `ChatHandler` when `EnableMethodProfiling=false`, showing a clear "not enabled" message.
## Files Affected
- `Profiling/MethodProfiler.cs` (new)
- `Handlers/ChatHandler.cs` (profile commands, alias, improved arg splitting, early config guard)
- `Models/SyncModSettings.cs` (new properties, version migration, clamping)
- `Mod.cs` (TickAutoStop in update loop, Close in unload)
- `SKO-Nanobot-BuildAndRepair-System.csproj` (new compile include)
## Testing
1. Set `EnableMethodProfiling=true` in `ModSettings.xml`, load a world as admin.
2. Run `/nanobars profile start` — verify chat confirmation.
3. Run `/nanobars profile status` — verify running state shown.
4. Run `/nanobars profile stop` — verify summary written to local storage.
5. Verify auto-stop fires after the configured duration.
6. Verify non-admins and clients are rejected.
7. Verify `/nanoboars` alias works identically.
8. Set `EnableMethodProfiling=false`, restart session — verify all profile subcommands show "not enabled" message.
9. Verify profiler file handles are released when session ends (no leftover lock on .log files).
