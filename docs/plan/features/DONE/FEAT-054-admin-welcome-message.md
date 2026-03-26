# FEAT-054: Admin welcome message on session join
## Status: Proposed
## Priority: Low
## Version: v2.5.0
## Summary
Show a one-time welcome message to admin players when they join a server or local session, informing them about `/nanobars help` for admin commands.
## Motivation
Admins may not know that chat commands exist. A brief welcome notification on session start improves discoverability without being intrusive.
## Design
- In `Mod.UpdateBeforeSimulation()`, after initialization is complete and the local player is available, check if the player is an admin.
- Show a one-time chat message: "Nanobot Build and Repair System v{version} loaded. Type /nanobars help for admin commands."
- Use a `_welcomeShown` flag to ensure it only fires once per session.
- Only show on the client side (not on dedicated server console).
## Files Affected
- `Mod.cs` — add welcome message logic after init
## Testing
- Join a local/listen server as admin — message should appear once.
- Join as non-admin — no message.
- Message should not repeat during the session.
