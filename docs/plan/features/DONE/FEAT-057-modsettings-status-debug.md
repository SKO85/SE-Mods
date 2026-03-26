# FEAT-057: Show ModSettings.xml loaded status in debug info
## Status: Done
## Priority: Low
## Version: v2.5.0
## Summary
Display whether a custom ModSettings.xml file was loaded in the debug HUD overlay and terminal custom info panel.
## Motivation
When troubleshooting, admins need to know if the server is running with default settings or a custom configuration file.
## Design
- Added `Mod.CustomSettingsLoaded` static bool, set in `SyncModSettings.Load()`.
- Added `CustomSettingsLoaded` field to `MsgDebugStats` for DS client sync.
- Debug HUD shows "ModSettings.xml: Loaded (custom)" or "Not found (defaults)".
- Terminal info debug section shows the same.
## Files Affected
- `Mod.cs` — `CustomSettingsLoaded` flag
- `Models/SyncModSettings.cs` — set flag in `Load()`
- `Models/MsgDebugStats.cs` — sync field
- `Handlers/HudHandler.cs` — debug HUD display
- `NanobotSystem.CustomInfo.cs` — terminal info display
## Testing
- With ModSettings.xml present: should show "Loaded (custom)".
- Without ModSettings.xml: should show "Not found (defaults)".
