# FEAT-025: Profiler auto-stop sends feedback to DS clients

## Status: Done
## Priority: Medium
## Version: v2.5.0

## Summary
When a profiling session auto-stops on a dedicated server, the completion message is now sent back to the client who started it via the network messaging system. Previously, the message was only written to the server log and `ShowMessage` (invisible to remote clients).

## Implementation
- `MethodProfiler.StartSession` now accepts a `steamId` parameter, stored in `_startedBySteamId`
- `TickAutoStop` returns the message and steamId via out parameters
- `Mod.UpdateBeforeSimulation` sends a `MsgModCommandResponse` to the originating client when auto-stop triggers
- `ChatHandler.ExecuteServerCommand` passes the sender steamId through to `StartSession`

## Files Changed
- `Profiling/MethodProfiler.cs`
- `Mod.cs`
- `Handlers/ChatHandler.cs`
- `Handlers/NetworkMessagingHandler.cs` — `SendCommandResponse` changed to `internal`
