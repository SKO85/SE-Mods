# FEAT-051: Hide custom info debug section on dedicated servers
## Status: Done
## Priority: Low
## Version: v2.5.0
## Summary
Only show debug diagnostics (sources, push targets, cluster info, stagger, grind budget) in the terminal custom info panel on local/listen-server games, not on dedicated servers.
## Motivation
On dedicated servers there is no local terminal to view the custom info panel. Computing and appending the debug section is wasted work. Additionally, DS admins reviewing logs don't need terminal debug clutter.
## Design
Added `!MyAPIGateway.Utilities.IsDedicated` guard to the existing `Mod.Settings.DebugMode` check in `AppendingCustomInfo`.
## Files Affected
- `NanobotSystem.CustomInfo.cs` — added `IsDedicated` check on the debug block.
## Testing
- Local game with debug on: terminal shows sources, cluster, stagger info.
- Dedicated server with debug on: terminal does not show debug section.
