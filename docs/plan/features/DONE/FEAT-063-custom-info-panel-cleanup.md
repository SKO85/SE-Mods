# FEAT-063: Clean up custom info panel — remove redundant debug info
## Status: Done
## Priority: Low
## Version: v2.5.0
## Description
The terminal custom info panel's debug section showed info that is already displayed in the debug HUD overlay, creating unnecessary duplication and clutter:
- Version
- MaxSystems/Grid + EmptyGrids
- Total BaRs + Stagger + GrindBudget
- ModSettings.xml loaded status

Additionally, the debug section was shown to all clients including those connected to a dedicated server, where it adds no value.
## Changes
1. Restricted debug section to local game only (`IsServer && !IsDedicated`) — previously showed on all non-DS clients.
2. Removed redundant items (Version, MaxSystems/Grid, EmptyGrids, Total BaRs, Stagger, GrindBudget, ModSettings.xml) from the panel. These are already shown in the debug HUD overlay.
3. Kept Sources/Push Targets count and Cluster info in the debug section — useful per-block diagnostics not available elsewhere.
## Files
- `NanobotSystem.CustomInfo.cs:109-149`
