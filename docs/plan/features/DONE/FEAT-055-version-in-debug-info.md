# FEAT-055: Show mod version in debug HUD and terminal info
## Status: Proposed
## Priority: Low
## Version: v2.5.0
## Summary
Display the mod version string in the debug HUD overlay and the terminal custom info panel when debug mode is active.
## Motivation
When investigating issues in-game, knowing which exact mod version is running saves time — especially on dedicated servers where multiple versions might be deployed.
## Design
- **Debug HUD**: Add a "Version" row at the top of the debug section in `HudHandler.RenderDebugRows()`.
- **Terminal info**: Add `Version: {Constants.ModVersion}` line in the debug section of `AppendingCustomInfo()` in `NanobotSystem.CustomInfo.cs`.
## Files Affected
- `Handlers/HudHandler.cs` — add version row to RenderDebugRows
- `NanobotSystem.CustomInfo.cs` — add version line in debug section
## Testing
- Enable debug mode (`/nanobars debug`) — HUD should show version at top.
- Open BaR terminal panel with debug mode on — version should appear in debug section.
