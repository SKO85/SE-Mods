# FEAT-059: Extract SendToAdmins helper with reusable player list
## Status: Done
## Priority: Low
## Version: v2.5.0
## Summary
Deduplicate admin broadcast logic and eliminate per-call player list allocations.
## Motivation
`BroadcastDebugStatsToAdmins` and `BroadcastProfileSummaryToAdmins` were near-identical: both allocated `new List<IMyPlayer>()`, called `GetPlayers`, filtered by promote level, and sent bytes. Both are called every 2 seconds from the same HUD update path.
## Design
Extracted a shared `SendToAdmins(ushort msgId, byte[] bytes)` private helper that uses a static `_adminBroadcastPlayers` list, cleared and reused each call. Both broadcast methods now delegate to it.
## Files Affected
- `Handlers/NetworkMessagingHandler.cs`
## Testing
Build succeeds. Admin broadcast still works for both debug stats and profile summary messages.
