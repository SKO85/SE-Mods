# FEAT-058: "Powered by SKO85" footer in debug HUD
## Status: Done
## Priority: Low
## Version: v2.5.0
## Summary
Short "Powered by SKO85" attribution line at the bottom of the debug HUD overlay.
## Motivation
Brand attribution in the debug overlay.
## Design
- Added as a subtle gray row at the bottom of `RenderDebugRows()` in `HudHandler.cs`.
## Files Affected
- `Handlers/HudHandler.cs`
## Testing
- Enable debug mode — footer should appear at bottom of debug overlay.
