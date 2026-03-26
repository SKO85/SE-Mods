# FEAT-061: Debug command hints when local HUD is hidden
## Status: Done
## Priority: Low
## Version: v2.5.0
## Summary
When an admin runs `/nanobars debug` with no arguments and the local HUD is hidden, show a hint message about `/nanobars debug show`.
## Motivation
Admins enabling debug mode on a DS may not realize they need a separate local command to show the HUD overlay. A hint message reduces confusion.
## Design
After showing the current debug status (`DebugMode: ON/OFF | Local HUD: shown/hidden`), if `LocalDebugVisible` is false, a second chat message is shown: "Use /nanobars debug show to enable the HUD overlay."
## Files Affected
- `Chat/ChatHandler.cs`
## Testing
Build succeeds. `/nanobars debug` shows the hint when HUD is hidden, no hint when already shown.
