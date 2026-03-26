# FEAT-009: Debug Mode for Terminal Custom Info Panel
## Status: Done
## Priority: Medium
## Version: v2.5.0
## Summary
Add a `DebugMode` setting to mod settings that gates additional diagnostic information in the terminal custom info panel.
## Motivation
Sources count, push targets count, and cluster information are useful for debugging and profiling but clutter the panel for normal gameplay. A toggle allows developers/admins to enable this information when needed.
## Design
- New `DebugMode` bool property on `SyncModSettings` (ProtoMember 35, XML-serialized, default `true` during development).
- When enabled, the custom info panel shows:
  - `Sources: N | Push Targets: N`
  - `Cluster: <hash> | Members: N`
  - `Coordinator: <block name> (self)` — with "(self)" suffix if this BaR is coordinator
  - `MaxSystems/Grid: N` — current per-grid BaR limit
- When disabled, these lines are hidden — only standard info (state, power, safezone, targets) shown.
- Set via `<DebugMode>true</DebugMode>` in ModSettings.xml.
## Files Affected
- `Models/SyncModSettings.cs` — new `DebugMode` property
- `NanobotSystem.CustomInfo.cs` — debug-gated section with sources, cluster, and grid limit info
## Testing
- Enable DebugMode: verify sources, push targets, cluster ID, coordinator name, member count, MaxSystems/Grid all appear
- Disable DebugMode: verify those lines are hidden
