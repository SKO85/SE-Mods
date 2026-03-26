# BUG-003: CleanupExpired() iterates live ConcurrentDictionary while removing

## Status: Fixed
## Severity: Medium
## Version: v2.5.0
## Found In: Code Review / Models/TtlCache.cs:101-112

## Description

`TtlCache.CleanupExpired()` iterates over the `Entries` `ConcurrentDictionary` using `foreach` while simultaneously removing entries via `TryRemove()` within the same loop. While `ConcurrentDictionary` does not throw on concurrent modification from *other threads*, calling `TryRemove()` on the *same dictionary being enumerated* within the *same thread* can produce undefined behavior depending on the .NET runtime version.

On .NET Framework 4.8 (used by Space Engineers), this pattern has been observed to:
- Skip entries (enumerator jumps over items due to internal bucket reshuffling)
- In rare cases, throw `InvalidOperationException` if the internal state changes during enumeration

```csharp
// TtlCache.cs:101-112
public void CleanupExpired()
{
    var now = MyAPIGateway.Session.ElapsedPlayTime;
    foreach (var pair in Entries)
    {
        if (pair.Value.IsExpired(now))
        {
            CacheItem removed;
            Entries.TryRemove(pair.Key, out removed);
        }
    }
}
```

## Steps to Reproduce

1. Fill a `TtlCache` with many entries, some expired.
2. Call `CleanupExpired()` while another thread is also adding/removing entries.
3. Observe skipped entries or (rarely) exceptions.

## Root Cause

`ConcurrentDictionary.GetEnumerator()` returns a point-in-time snapshot of the collection's state, but removal during iteration can cause the enumerator to skip entries. The combination of enumeration + removal in the same loop is not guaranteed safe.

## Fix

Collect expired keys first, then remove in a second pass:

```csharp
public void CleanupExpired()
{
    var now = MyAPIGateway.Session.ElapsedPlayTime;
    var expiredKeys = new List<TKey>();
    foreach (var pair in Entries)
    {
        if (pair.Value.IsExpired(now))
        {
            expiredKeys.Add(pair.Key);
        }
    }
    CacheItem removed;
    foreach (var key in expiredKeys)
    {
        Entries.TryRemove(key, out removed);
    }
}
```

**Note:** This requires adding `using System.Collections.Generic;` if not already present.
