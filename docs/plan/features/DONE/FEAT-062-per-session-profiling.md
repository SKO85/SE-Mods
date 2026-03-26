# FEAT-062: Per-Session Profiling with Named Sessions
## Status: Done
## Priority: Medium
## Version: v2.5.0
## Summary
Each profiling session now stores files with a unique session prefix so multiple sessions coexist for later comparison.
## Motivation
Previously, starting a new profiling session deleted the previous session's files. Admins needed to compare performance across different configurations or time periods but could only keep one session at a time.
## Design
- Each session gets a name: user-provided via `/nanobars profile start [s] [ms] [name]`, or auto-generated as `yyyyMMddHHmmss-profiling`.
- All files are prefixed: `{session}.NanobotProfiler.Summary.log`, `{session}.NanobotProfiler.{Method}.log`, `{session}.NanobotProfiler.manifest`.
- A master index file `NanobotProfiler.sessions` tracks all session names.
- Sessions accumulate — starting a new session does not delete previous ones.
- New commands: `profile list` (list sessions), `profile clear <name|all>` (delete session files).
- Session name shown in status messages, stop messages, and profile summary HUD.
- `MsgProfileSummary` includes `SessionName` field (ProtoMember 6) for network broadcast.
- `DeletePreviousLogs()` removed entirely — replaced by explicit `clear` command.
- `ClearSession` uses `DeleteFileInLocalStorage` to actually remove files from disk.
## Files Affected
- `Profiling/MethodProfiler.cs` — session name, prefixed file paths, ClearSession, AppendSessionIndex, ReadSessionIndex, GetSessionListText
- `Chat/Commands/ProfileCommand.cs` — parse session name, add clear/list commands, update help
- `Models/MsgProfileSummary.cs` — add SessionName field
- `Handlers/HudHandler.cs` — show session name in profile summary header
## Additional Behavior
- Starting a session with a name that already exists deletes the old session's files first (via manifest lookup), so the session is cleanly overwritten.
- Auto-generated datetime names never collide so they always start fresh.
## Testing
Build succeeds. In-game: start two sessions with different names, verify both sets of files exist, verify `profile list` shows both, verify `profile clear all` deletes them. Restart a named session to verify old files are cleaned up.

