# FEAT-071: Idle scan backoff (updated: idle counter uses filtered targets)
## Status: In Progress
## Priority: High
## Version: v2.5.4
## Summary
Reduce background scanning frequency when all BaR systems are idle (no weld/grind/collect targets).
## Motivation
Profiling session `20260416194445` (58 idle BaRs, 180s, simSpeed ~1.0) shows cluster scans running at fixed 10s intervals producing zero targets. Each scan costs 5-18ms on background threads (AsyncClusterScan 9.15ms avg, AsyncAddBlocksOfGrid 0.116ms avg × 124 grids, AsyncAddBlocksOfBox 4.2ms avg). Over 180s that's 582ms of background work for zero results. On dedicated servers with 100+ idle BaRs this becomes significant.
## Design
Track consecutive zero-result scans per cluster coordinator. After 3 consecutive empty scans (~30s idle), extend the effective scan interval from `TargetsUpdateInterval` (10s) to a new `IdleScanInterval` (30s). Reset to normal interval immediately when:
- Any scan produces targets (weld, grind, or float)
- The BaR starts working (targets appear via state change)

Implementation:
- Add `_consecutiveEmptyScans` counter to `NanobotSystem` (per-coordinator)
- At end of `AsyncClusterScan`, after publishing result: if result has zero weld+grind+float candidates, increment counter; otherwise reset to 0
- In `UpdateSourcesAndTargetsTimer`, when checking `updateTargets`: if `_consecutiveEmptyScans >= 3`, use `IdleScanInterval` (30s) instead of `TargetsUpdateInterval` (10s)
- The idle interval applies only to the target scan — source scanning already runs at 30s and is unaffected
- No new settings needed; `IdleScanInterval` is a constant (30s)
## Files Affected
- `NanobotSystem.cs` — new field `_consecutiveEmptyScans`
- `NanobotSystem.Scanning.cs` — counter logic in `AsyncClusterScan`, interval check in `UpdateSourcesAndTargetsTimer`
## Testing
- Profile with all BaRs idle: AsyncClusterScan calls should drop from 17/180s to ~6/180s
- Place a damaged block: scan interval should reset to 10s and target found within 10s
- Remove all targets again: should ramp back to 30s after ~30s idle
