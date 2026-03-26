# BUG-055: Debug HUD SafeZoneBlocked not counting building projections disabled
## Status: Fixed
## Severity: Low
## Version: v2.5.0
## Found In: HudHandler.cs BuildStats()
## Description
The `SafeZoneBlocked` counter in the debug HUD only checked `SafeZoneAllowsWelding` and `SafeZoneAllowsGrinding`. BaR systems inside safe zones that block building projections were not counted as safe-zone-blocked.
## Root Cause
The condition at `HudHandler.cs:220` was missing `SafeZoneAllowsBuildingProjections`:
```csharp
if (!sys.State.SafeZoneAllowsWelding || !sys.State.SafeZoneAllowsGrinding)
```
## Fix
Added `!sys.State.SafeZoneAllowsBuildingProjections` to the condition in `HudHandler.cs:220`:
```csharp
if (!sys.State.SafeZoneAllowsWelding || !sys.State.SafeZoneAllowsGrinding || !sys.State.SafeZoneAllowsBuildingProjections)
```
