# BUG-010: SafeZone/Shield False Warnings on Init
## Status: Fixed
## Severity: Medium
## Version: v2.5.0
## Found In: NanobotSystem.CustomInfo.cs, SyncBlockState.cs
## Description
On restart or re-login, the custom info panel briefly showed safe zone and shield warnings (e.g. "SafeZone: Welding disabled!") even when no safe zones were active. This is because `_SafeZoneAllowsWelding`, `_SafeZoneAllowsBuildingProjections`, and `_SafeZoneAllowsGrinding` default to `false`, and the first safe zone check hadn't run yet.
## Steps to Reproduce
1. Place a BaR block outside any safe zone
2. Save and reload the world (or relog in multiplayer)
3. Open the BaR terminal immediately — false safe zone warnings appear for a few seconds
## Root Cause
Bool fields default to `false` in C#, so before `SetSafeZoneAndShieldStates()` runs for the first time, all safe zone flags read as "not allowed", causing the info panel to display warnings.
## Fix
- Added `SafeZoneAndShieldsChecked` flag to `SyncBlockState` (defaults to `false`)
- `SetSafeZoneAndShieldStates()` sets it to `true` after the first check completes (`NanobotSystem.State.cs`)
- Client side sets it to `true` when receiving state from server (`SyncBlockState.cs`)
- Custom info panel wraps all safe zone and shield warnings inside `if (State.SafeZoneAndShieldsChecked)` (`NanobotSystem.CustomInfo.cs`)
