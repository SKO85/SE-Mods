# BUG-020: SafeZoneHandler null reference crash on safeZoneBlock lookup
## Severity: High
## Version: v2.5.0
## Status: Done

## File
`Handlers/SafeZoneHandler.cs:446-449`

## Description
`MyEntities.GetEntityByName(safeZone.SafeZoneBlockId.ToString()) as IMySafeZoneBlock` can return null (entity deleted, not loaded, or cast fails). The next line accesses `safeZoneBlock.OwnerId` without a null check, causing a `NullReferenceException`.

The method is inside a try/catch so it won't crash the game, but it returns `false` (not protected) as the fallback — meaning a BaR could grind a block inside a safe zone when the safe zone entity isn't loaded.

## Fix
Add null check on `safeZoneBlock`. If null, default to "protected" (return true) as the safe fallback.
