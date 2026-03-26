# FEAT-050: Debug HUD position command (`/nanobars debug [left|right]`)
## Status: Done
## Priority: Low
## Version: v2.5.0
## Summary
Add optional `left`/`right` parameter to `/nanobars debug` command to position the HUD overlay on either side of the screen.
## Motivation
Players may want the debug HUD on the right side to avoid overlapping with other UI elements.
## Design
- `HudHandler` stores two origin constants (`OriginLeft` at -0.98, `OriginRight` at 0.48) and a `_rightAligned` flag (default false = left).
- `HudHandler.SetPosition(bool right)` updates the flag and repositions existing HUD messages immediately.
- `/nanobars debug` — toggles debug on/off (position unchanged).
- `/nanobars debug left` — always enables debug, positions HUD left.
- `/nanobars debug right` — always enables debug, positions HUD right.
- Position is client-side only, not persisted in ModSettings.xml.
## Files Affected
- `Handlers/HudHandler.cs` — `OriginLeft`/`OriginRight` constants, `_rightAligned` flag, `SetPosition()` method, `Origin` property.
- `Chat/ChatHandler.cs` — parse optional position arg in debug command, force enable when position specified.
- `Chat/Commands/HelpCommand.cs` — updated help text.
## Testing
- `/nanobars debug` — toggles on/off as before.
- `/nanobars debug right` — HUD appears on the right side, debug enabled.
- `/nanobars debug left` — HUD moves to the left side, debug enabled.
- `/nanobars debug` — toggles off regardless of position.
