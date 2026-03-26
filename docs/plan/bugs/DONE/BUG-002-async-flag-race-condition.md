# BUG-002: _AsyncUpdateSourcesAndTargetsRunning flag race condition

## Status: Fixed
## Severity: High
## Version: v2.5.0
## Found In: Code Review / NanobotSystem.Scanning.cs:66,371

## Description

The `_AsyncUpdateSourcesAndTargetsRunning` flag is used to prevent concurrent async scan tasks. The flag is **set** inside a `lock(_Welder)` block (line 77), but **cleared** outside any lock at the end of `AsyncUpdateSourcesAndTargets()` (line 371).

This creates a race condition: if the main thread reads the flag between the async thread's last locked section and line 371, the flag may still be `true`, causing the main thread to skip a needed scan update. Conversely, if the flag is cleared without synchronization, a torn read could occur (though unlikely for `bool`).

More critically, because the flag reset at line 371 is outside the lock, there is a window where:
1. Async thread finishes work but hasn't cleared the flag yet
2. Main thread checks flag inside `lock(_Welder)`, sees `true`, returns early
3. Async thread clears flag — but no new scan is triggered

This means a scan update can be silently dropped.

```csharp
// NanobotSystem.Scanning.cs:74-79 — flag SET (inside lock)
lock (_Welder)
{
    if (_AsyncUpdateSourcesAndTargetsRunning) return;
    _AsyncUpdateSourcesAndTargetsRunning = true;
    Mod.AddAsyncAction(() => AsyncUpdateSourcesAndTargets(updateSource));
}

// NanobotSystem.Scanning.cs:371 — flag CLEAR (outside any lock)
_AsyncUpdateSourcesAndTargetsRunning = false;
```

## Steps to Reproduce

1. Trigger a scan while an existing async scan is finishing (between its last lock release and line 371).
2. The scan request will be silently dropped.

## Root Cause

The flag clear at line 371 is not protected by the same `lock(_Welder)` that guards the flag check and set. This breaks the mutual exclusion contract.

## Fix

Wrap the flag clear in the same lock:

```csharp
// At line 371, replace:
//   _AsyncUpdateSourcesAndTargetsRunning = false;
// With:
lock (_Welder)
{
    _AsyncUpdateSourcesAndTargetsRunning = false;
}
```

Alternatively, change `_AsyncUpdateSourcesAndTargetsRunning` to a `volatile bool` field if the intent is merely visibility (not mutual exclusion). However, the lock approach is safer since the flag is already guarded by a lock on the set side.
