# FEAT-024: Config reload and reset commands

## Status: Done
## Priority: Medium
## Version: v2.5.0

## Summary
Added `/nanobars config reload` and `/nanobars config reset` chat commands for live settings management on dedicated servers.

## Commands
- **`/nanobars config reload`** — Re-reads `ModSettings.xml` from world storage (or local storage fallback), applies settings, and notifies all BaR systems.
- **`/nanobars config reset`** — Resets all settings to defaults (with appropriate `MaxSystemsPerTargetGrid` for multiplayer vs local).

Both call `Mod.SettingsChanged()` to propagate changes to all BaR instances immediately.

## Files Changed
- `Chat/Commands/ConfigCommand.cs`
