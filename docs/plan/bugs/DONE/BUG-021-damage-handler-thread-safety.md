# BUG-021: DamageHandler thread-safety issues with FriendlyDamage and NanobotSystems
## Severity: High
## Version: v2.5.0
## Status: DONE

## File
`Handlers/DamageHandler.cs:109-118`

## Description
Two thread-safety issues in `OnAfterDamage`:

1. **FriendlyDamage dictionary** — `Dictionary<IMySlimBlock, TimeSpan>` (not thread-safe) is written from the damage handler and read from the main thread via `IsFriendlyDamage()` and `CleanupFriendlyDamage()`. The SE damage handler can fire from different contexts, risking corruption.

2. **Mod.NanobotSystems iteration** — `Mod.NanobotSystems` is a regular `Dictionary<long, NanobotSystem>` iterated with `foreach`. If a NanobotSystem is added/removed concurrently (block placed/removed), throws `InvalidOperationException`. The outer try/catch swallows it but friendly damage won't be recorded for remaining systems.

## Fix
1. Change `FriendlyDamage` to `ConcurrentDictionary<IMySlimBlock, TimeSpan>`, or wrap all access with a lock.
2. Snapshot `Mod.NanobotSystems` values before iterating (e.g., copy to local list or use lock).
