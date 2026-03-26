# BUG-078: SharedEntityCache crashes on null Session during load/unload
## Status: Fixed
## Severity: Medium
## Version: v2.5.0
## Found In: Code review round 6 — Caches/SharedEntityCache.cs:33,100
## Description
`GetEntitiesInBox()` and `Cleanup()` both access `MyAPIGateway.Session.ElapsedPlayTime` without checking if `Session` is null. During world load or session unload, `Session` can be null, causing a `NullReferenceException` that crashes the mod. The sibling `TtlCache` class properly guards against this.
## Root Cause
Missing null check on `MyAPIGateway.Session`.
## Fix
- `SharedEntityCache.cs:31-33` — Added `var session = MyAPIGateway.Session; if (session == null) return new List<IMyEntity>();` early return in `GetEntitiesInBox`.
- `SharedEntityCache.cs:100` — Added same null guard with `return` in `Cleanup`.
