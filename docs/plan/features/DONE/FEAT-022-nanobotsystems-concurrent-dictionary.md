# FEAT-022: Change Mod.NanobotSystems to ConcurrentDictionary

## Status: Done
## Priority: Medium
## Version: v2.5.0

## Summary
Replace `Dictionary<long, NanobotSystem>` with `ConcurrentDictionary<long, NanobotSystem>` for `Mod.NanobotSystems` to eliminate inconsistent locking and prevent concurrent-access exceptions.

## Motivation
`Mod.NanobotSystems` had 17 access sites but only 4 used `lock()`. The remaining 13 — foreach iterations, `.Count` reads, and `TryGetValue` lookups — were unprotected. If a BaR block was placed or removed while another thread was iterating (e.g., background scan via `ScanClusterCoordinator.RebuildClusters()`), it could throw `InvalidOperationException` or return corrupt data. This was a latent crash risk in any server with BaR blocks being built/removed during active scanning.

## Design

### Declaration change
`Mod.NanobotSystems` changed from `Dictionary<long, NanobotSystem>` to `ConcurrentDictionary<long, NanobotSystem>`.

### Lock removal — 4 sites
All manual `lock(Mod.NanobotSystems)` blocks removed since `ConcurrentDictionary` provides thread-safe operations natively:

1. **`NanobotSystem.Init.cs` — `Init()`**: `lock` + `ContainsKey` + `Add` → `TryAdd()`
2. **`NanobotSystem.Init.cs` — `Close()`**: `lock` + `Remove` → `TryRemove()`
3. **`Mod.cs` — `SaveData()`**: Removed lock wrapper around foreach
4. **`Mod.cs` — `BuildGridSystemCountCache()`**: Removed lock wrapper around foreach

### Lock removal — DamageHandler (2 sites)
5. **`DamageHandler.cs` — `OnBeforeDamage()`**: Removed lock wrapper around `TryGetValue`
6. **`DamageHandler.cs` — `OnAfterDamage()`**: Removed lock wrapper around foreach

### Already-safe sites (no changes needed) — 11 sites
These unprotected sites became safe automatically with the type change:
- `Mod.cs` — foreach in `SettingsChanged()`
- `Mod.cs` — `.Count` in `InitControls()`
- `Mod.cs` — foreach `.Values` in `RebuildSourcesAndTargetsTimer()`
- `Mod.cs` — `.Count` in profiler lambdas (×2)
- `NanobotSystem.Grinding.cs` — foreach in `ServerDoGrind()`
- `ScanClusterCoordinator.cs` — foreach `.Values` in `RebuildClusters()`
- `ScanClusterCoordinator.cs` — `.Count` in profiler lambda
- `NetworkMessagingHandler.cs` — `TryGetValue` lookups (×3)

## Files Affected
- `Mod.cs` — Declaration change, using added, locks removed from `SaveData()` and `BuildGridSystemCountCache()`
- `NanobotSystem.Init.cs` — `Init()` and `Close()` lock blocks simplified
- `Handlers/DamageHandler.cs` — Locks removed from `OnBeforeDamage()` and `OnAfterDamage()`

## Testing
1. Place and remove multiple BaR blocks rapidly while other BaRs are actively scanning
2. Verify no `InvalidOperationException` in SE log
3. Verify save/load works correctly (SaveData iteration)
4. Verify friendly damage tracking still works (DamageHandler iteration)
5. Verify GridSystemCountCache still counts correctly under load
