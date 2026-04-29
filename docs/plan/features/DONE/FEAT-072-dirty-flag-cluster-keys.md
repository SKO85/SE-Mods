# FEAT-072: Dirty-flag cluster key optimization
## Status: Done (shipped — `_clusterSettingsVersion` + version-compare fast path in `ScanClusterCoordinator.cs:48,144`)
## Priority: Medium
## Version: v2.5.4
## Summary
Replace per-second full cluster key string recomputation with a version counter that triggers rebuild only on actual settings changes.
## Motivation
Profiling session `20260416194445` shows `ScanClusterCoordinator.RebuildClusters` at 0.586ms steady avg, 171 calls in 180s (103ms total). All entries show `skipped=True` — the "fast path" runs every time but still computes `ComputeClusterKey()` for all 58 BaRs (12+ string concatenations each) just to confirm nothing changed. This is 100% wasted work on the main thread during idle.
## Design
Add a `_clusterSettingsVersion` integer counter to each `NanobotSystem`. Increment it whenever a cluster-relevant setting changes (the same fields that make up `ComputeClusterKey`):
- Settings flags changes (via `SyncBlockSettings.Settings` relevant bits)
- Work mode, search mode changes
- Priority string changes
- Color setting changes
- Grind janitor / weld option changes
- Owner changes, conveyor system toggle
- Safe zone state changes

In `RebuildClusters` fast path, compare `_clusterSettingsVersion` against `_lastClusterSettingsVersion` (a stored snapshot) instead of calling `ComputeClusterKey()`. Only if a version mismatch is found, do the full rebuild with key computation.

Implementation:
- Add `internal int _clusterSettingsVersion` and `internal int _lastClusterSettingsVersion` to `NanobotSystem`
- Add `internal void BumpClusterSettingsVersion()` helper that increments `_clusterSettingsVersion`
- Call `BumpClusterSettingsVersion()` from settings apply paths and `SetSafeZoneAndShieldStates` when state changes
- Modify `RebuildClusters` fast path to compare version ints instead of computing keys
## Files Affected
- `NanobotSystem.cs` — new fields
- `NanobotSystem.State.cs` — bump version in `SetSafeZoneAndShieldStates`
- `Cluster/ScanClusterCoordinator.cs` — version-based fast path
- `Models/SyncBlockSettings.cs` — bump version on settings apply
## Testing
- Profile idle BaRs: `RebuildClusters` should drop from ~0.586ms to near-zero
- Change a BaR setting (e.g., toggle ignore color): cluster rebuild should trigger within 1s
- Toggle BaR on/off: cluster should reassign correctly
