# BUG-045: TtlCache accesses MyAPIGateway.Session without null guard
## Status: Fixed
## Severity: Medium
## Version: v2.5.0
## Found In: Code review — Caches/TtlCache.cs

## Description

`TtlCache.Set()`, `TryGet()`, and `CleanupExpired()` all access `MyAPIGateway.Session.ElapsedPlayTime` without checking if `Session` is null. During session unload, `MyAPIGateway.Session` becomes null. Any handler or cleanup code that calls into TtlCache during shutdown will throw `NullReferenceException`.

**Affected lines:**
- Line 59: `var now = MyAPIGateway.Session.ElapsedPlayTime;` (in `Set()`)
- Line 74: `var now = MyAPIGateway.Session.ElapsedPlayTime;` (in `TryGet()`)
- Line 103: `var now = MyAPIGateway.Session.ElapsedPlayTime;` (in `CleanupExpired()`)

TtlCache is used by SafeZoneHandler, GridOwnershipCacheHandler, BlockSystemAssigningHandler, and BlockPriorityHandling. Any of these could be invoked during the shutdown sequence.

## Root Cause

No defensive null check on the Session reference before accessing ElapsedPlayTime.

## Fix

Add null guard with early return:
```csharp
var session = MyAPIGateway.Session;
if (session == null) { value = default(TValue); return false; }
var now = session.ElapsedPlayTime;
```

For `Set()`, return silently. For `TryGet()`, return false. For `CleanupExpired()`, return without cleanup.
