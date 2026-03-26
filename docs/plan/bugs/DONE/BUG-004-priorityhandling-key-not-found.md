# BUG-004: _PrioHash[itemKey] can throw KeyNotFoundException

## Status: Fixed
## Severity: Medium
## Version: v2.5.0
## Found In: Code Review / PriorityHandling.cs:73,83

## Description

`GetPriority()` (line 73) and `GetEnabled()` (line 83) access `_PrioHash[itemKey]` using the indexer, which throws `KeyNotFoundException` if `itemKey` is not present in the dictionary.

If a new block type or item is introduced (by another mod or game update) that isn't in the priority list, `GetItemKey()` may return a key that was never added to `_PrioHash` during `UpdateHash()`. This would crash the calling code.

```csharp
// PriorityHandling.cs:69-75
internal int GetPriority(I a)
{
    var itemKey = GetItemKey(a, false);
    if (_HashDirty) UpdateHash();
    return _PrioHash[itemKey];  // throws if itemKey not in hash
}

// PriorityHandling.cs:80-85
internal bool GetEnabled(I a)
{
    var itemKey = GetItemKey(a, true);
    if (_HashDirty) UpdateHash();
    return _PrioHash[itemKey] < int.MaxValue;  // throws if itemKey not in hash
}
```

## Steps to Reproduce

1. Install a mod that adds a new block type not recognized by the priority system.
2. Have BaR attempt to weld/grind that block.
3. `GetPriority()` or `GetEnabled()` throws `KeyNotFoundException`.

## Root Cause

The indexer `_PrioHash[itemKey]` assumes the key always exists. If `GetItemKey()` returns a key for an item type that isn't in the priority list (which is populated from a fixed set during `UpdateHash()`), the lookup fails.

## Fix

Use `TryGetValue` with a sensible default:

```csharp
internal int GetPriority(I a)
{
    var itemKey = GetItemKey(a, false);
    if (_HashDirty) UpdateHash();
    int prio;
    if (_PrioHash.TryGetValue(itemKey, out prio))
        return prio;
    return int.MaxValue; // unknown items treated as lowest priority
}

internal bool GetEnabled(I a)
{
    var itemKey = GetItemKey(a, true);
    if (_HashDirty) UpdateHash();
    int prio;
    if (_PrioHash.TryGetValue(itemKey, out prio))
        return prio < int.MaxValue;
    return false; // unknown items treated as disabled
}
```
