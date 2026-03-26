# BUG-026: SafeZoneHandler LINQ allocations in GetSafeZonesInRange
## Severity: Medium
## Version: v2.5.0
## Status: DONE

## File
`Handlers/SafeZoneHandler.cs:163-181`

## Description
`GetSafeZonesInRange` uses a LINQ chain `.Where().Take().ToList()` which allocates delegate objects, enumerator objects, and a new `List<MySafeZone>` on every call. This method is called from `GetIntersectingSafeZone` and `GetIntersectingAttackerSafeZone`, both invoked per-block during weld/grind operations.

Additionally, line 165 allocates an empty list even when there are no zones.

## Fix
Replaced LINQ with a `foreach` loop and early break. Added a static `EmptyZoneList` field returned when no zones match (zero allocations on the empty path). The result list is only allocated when a matching zone is found, pre-sized to `take`. Removed `using System.Linq` (no longer needed in this file).
