# BUG-047: SafeZoneHandler returns closed zones from cache
## Status: Fixed
## Severity: Low
## Version: v2.5.0
## Found In: Code review — Handlers/SafeZoneHandler.cs

## Description

In `GetIntersectingSafeZone()` at lines 218-221, a cached zone ID is looked up in the `Zones` dictionary. If the zone was deleted/closed after caching, the returned `MySafeZone` object may be closed (`zone.Closed` or `zone.MarkedForClose`). The caller then uses this zone to check `GetActionsAllowedForSystem()`, which may return stale permissions.

```csharp
MySafeZone zone;
if (Zones.TryGetValue(zoneId, out zone))
{
    return zone;  // No Closed/MarkedForClose check
}
```

Compare to line 229 in the uncached path, which correctly checks:
```csharp
if (zone == null || zone.Closed || zone.MarkedForClose || !zone.Enabled)
```

## Root Cause

The cached path returns the zone without the same validity checks applied in the uncached path.

## Fix

Add closed/enabled check before returning cached zone:
```csharp
if (Zones.TryGetValue(zoneId, out zone))
{
    if (!zone.Closed && !zone.MarkedForClose && zone.Enabled)
        return zone;
}
```

If the zone is invalid, fall through to the fresh lookup path which will update the cache.
