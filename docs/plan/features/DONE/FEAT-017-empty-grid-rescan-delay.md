# FEAT-017: Empty grid rescan delay — skip grids with no weld/grind targets

## Status: Done
## Priority: Medium
## Version: v2.5.0

## Summary
After scanning a grid and finding zero weld or grind targets, cache it as "empty" and skip expensive block-level scanning for a configurable duration (`EmptyGridRescanDelaySeconds`). Connections (rotors, pistons, connectors) are still traversed so newly docked ships are discovered.

## Motivation
Profiling showed the BaR's own grid (1328 blocks) being scanned every 10s for zero results, wasting 2.7-7ms per cycle. In multi-BaR setups with many grids, this waste compounds across all grids that have no actionable targets.

## Design

### New setting: `EmptyGridRescanDelaySeconds`
- `SyncModSettings.EmptyGridRescanDelaySeconds` (ProtoMember 36, default 30, range 0-300)
- 0 = disabled (every grid is scanned every cycle as before)
- Clamped in `SyncModSettings.Load()` to 0-300 range

### Per-BaR empty grid cache
- `NanobotSystem._EmptyGridCache` — `ConcurrentDictionary<long, TimeSpan>` mapping grid EntityId to the playTime when it was found empty.
- Populated/cleared inside `AsyncAddBlocksOfGrid`.

### Fast path in `AsyncAddBlocksOfGrid`
At the top of the method, before `GetBlocksFromCache()`:
1. Check `_EmptyGridCache` for the grid
2. If cached and not expired: use raw (unsorted) block list from `SharedGridBlockCache.GetBlocks()` to traverse connections only — skip `AsyncAddBlockIfTarget` and sorted cache entirely
3. Log `skippedEmpty=True` in profiler output

### Cache update after normal scanning
After the full block scan:
- If no new weld or grind targets were added (before/after count comparison), cache the grid
- If targets were found, remove the grid from cache (if present)

### Applies to all grids
Both the BaR's own grid and other target grids in range. Any grid with no weld/grind targets gets cached and skipped.

### Cache cleanup
- `CleanupEmptyGridCache()` runs at the end of each `AsyncClusterScan` cycle, removing expired entries (two-pass: collect keys, then remove).
- `_EmptyGridCache.Clear()` in `Close()` prevents memory leaks when a BaR block is removed.

### Debug panel
`EmptyGrids: N` shown in the custom info panel when debug mode is enabled, alongside `MaxSystems/Grid`.

## Profiling results (120s session, 60 BaRs, 3 target grids)

| Metric | Value |
|--------|-------|
| `AsyncAddBlocksOfGrid` total calls | 75 |
| Calls with `skippedEmpty=True` | **37 (49%)** |
| Fast-path time | 0.067-0.321ms |
| Full-scan time | 2-17ms |
| Estimated savings | **~111ms** over 120s |

Grids with active grind targets were never skipped. Grids with zero targets (own grid, idle target grids) were correctly cached and skipped after the first scan, with re-scans occurring at ~30s intervals matching the default delay.

## Files Affected
- `Models/SyncModSettings.cs` — New `EmptyGridRescanDelaySeconds` property, clamping in `Load()`
- `NanobotSystem.cs` — New `_EmptyGridCache` field
- `NanobotSystem.Init.cs` — `_EmptyGridCache.Clear()` in `Close()`
- `NanobotSystem.Scanning.cs` — Fast path in `AsyncAddBlocksOfGrid`, before/after target counting, cache update, `CleanupEmptyGridCache()` method
- `NanobotSystem.CustomInfo.cs` — `EmptyGrids` count in debug panel

## Testing
1. Place BaR on a grid with no damaged/projected blocks and no grind targets
2. Enable profiling: `/nanobars profile start 60`
3. Check `AsyncAddBlocksOfGrid` logs — own grid should show `skippedEmpty=True` after first scan
4. Dock a new ship via connector — verify it is discovered and scanned despite parent grid being cached
5. Damage a block on the cached grid — verify it is re-discovered within `EmptyGridRescanDelaySeconds` (default 30s)
6. Set `EmptyGridRescanDelaySeconds` to 0 in settings — verify all grids are scanned every cycle (no caching)
