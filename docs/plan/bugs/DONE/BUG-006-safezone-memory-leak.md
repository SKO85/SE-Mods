# BUG-006: Static Zones dictionary can leak if OnEntityRemove event doesn't fire

## Status: Fixed
## Severity: Low
## Version: v2.5.0
## Found In: Code Review / Handlers/SafeZoneHandler.cs:19

## Description

`SafeZoneHandler.Zones` is a static `ConcurrentDictionary<long, MySafeZone>` that tracks all safe zones. Zones are added when `OnEntityAdd` fires and removed when `OnEntityRemove` fires. However, if `OnEntityRemove` fails to fire for any reason (event unsubscription, game crash recovery, mod reload), the dictionary entry will persist for the lifetime of the session.

Since the dictionary holds references to `MySafeZone` game entities, leaked entries prevent garbage collection of potentially large objects.

```csharp
// SafeZoneHandler.cs:19
public static readonly ConcurrentDictionary<long, MySafeZone> Zones = new ConcurrentDictionary<long, MySafeZone>();

// SafeZoneHandler.cs:114-129 — removal depends on event
private static void OnEntityRemove(IMyEntity ent)
{
    try
    {
        if (ent is MySafeZone)
        {
            var sz = ent as MySafeZone;
            if (sz != null)
            {
                MySafeZone removed;
                Zones.TryRemove(sz.EntityId, out removed);
            }
        }
    }
    catch { }
}
```

## Steps to Reproduce

1. Create a safe zone in-game (entity added to `Zones`).
2. Delete the safe zone via a method that doesn't trigger `OnEntityRemove` (e.g., admin delete, world reload edge case).
3. Observe that `Zones` still holds the reference.

## Root Cause

No periodic validation or cleanup of the `Zones` dictionary. The code relies entirely on the `OnEntityRemove` event, which is not guaranteed to fire in all edge cases.

## Fix

Add a periodic cleanup that validates zone entries still exist. Use the two-pass pattern (collect keys, then remove) per BUG-003 — iterating and removing from `ConcurrentDictionary` in a single pass is unsafe on .NET Framework 4.8:

```csharp
public static void CleanupStaleZones()
{
    var staleKeys = new List<long>();
    foreach (var pair in Zones)
    {
        if (pair.Value == null || pair.Value.MarkedForClose || pair.Value.Closed)
        {
            staleKeys.Add(pair.Key);
        }
    }
    MySafeZone removed;
    foreach (var key in staleKeys)
    {
        Zones.TryRemove(key, out removed);
    }
}
```

Call this from a low-frequency update (e.g., every 100 ticks).

**Note:** This is low severity because safe zones are relatively rare objects, and the leak only matters in long-running sessions with frequent zone creation/deletion. The `catch { }` in `OnEntityRemove` also silently swallows errors that could help diagnose missed removals — consider logging exceptions there.
