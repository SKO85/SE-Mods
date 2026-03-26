# FEAT-004: Custom Info Panel Periodic Refresh
## Status: Done
## Priority: Medium
## Version: v2.5.0
## Summary
Force-refresh the custom info panel every 2 seconds regardless of state changes.
## Motivation
The custom info panel sometimes showed stale information — users had to re-enable the block to see updated data. The panel only refreshed when state actually changed, so missed transitions or unreliable terminal redraws left the panel outdated.
## Design
Changed the periodic safe zone check block in `UpdateBeforeSimulation10_100` to always call `UpdateCustomInfo(true)` every 2 seconds, instead of only when safe zone state changed. The existing 1-second throttle inside `UpdateCustomInfo` prevents excessive refreshes from other callers.
## Files Affected
- `NanobotSystem.Update.cs` — simplified periodic check to always force `UpdateCustomInfo(true)`
## Testing
- Open BaR terminal, observe info panel updates while welding/grinding/idling
- Verify panel refreshes within ~2 seconds of state changes
