# FEAT-075: Saturated scan skip
## Status: Done (shipped — `IsTargetListSaturated()` at `Scanning.cs:158`, all four fix layers in field declarations + `Scanning.cs:111,145,1295,1311`)
## Priority: High
## Version: v2.5.4
## Summary
Skip the expensive full-grid rescan when the coordinator still has plenty of valid targets. Quick-count live targets (~12µs) instead of running the 50ms+ scan that would produce a nearly identical result.
## Motivation
Profiling session `20260416211403` (58 BaRs grinding 10K-block grid) shows AsyncClusterScan at 49ms avg, 17 calls in 180s. Each scan iterates all ~10K blocks, partitions, and sorts — producing 256 grind targets. Between scans (10s), only ~10-20 targets are consumed. So 90%+ of each rescan is wasted work producing identical results.
## Design
Before triggering a coordinator scan, count live (non-destroyed) targets in the coordinator's existing target lists. If the count exceeds `SaturatedRescanThreshold` (64 = 25% of 256-cap), skip the scan. Members also skip when the coordinator does.

Safety: force a rescan every `MaxScanSkipDuration` (60s) regardless of saturation.

### Implementation details:
- `IsTargetListSaturated()`: iterates target lists counting live blocks, early-exits at threshold
- `_scanSkippedSaturated` volatile flag: set by coordinator, read by members to also skip
- `_lastFullScanTime`: tracks when a real scan last ran, forces rescan at 60s

### Fix 1: Per-type saturation check (mixed work modes)
`IsTargetListSaturated()` checks grind AND weld independently per work mode. If grind is saturated but weld targets are depleted, the scan runs to discover weld targets for grid-limited BaRs that fall through to welding. Uses `_lastScanWeldCandidateCount` / `_lastScanGrindCandidateCount` to distinguish "type depleted" from "type never existed" — avoids endless rescanning in pure-grind scenarios.

### Fix 2: Forced rescan with debounce
When a member BaR runs out of targets (weld/grind transition to idle), it signals the coordinator via `_rescanForced = true` and resets `coordinator._LastTargetsUpdate`. The coordinator bypasses the saturated check on the next tick. Debounced at 5s (half the scan interval) to prevent scan storms when many members deplete simultaneously.

### Fix 3: Coordinator exhaustion bypass
When the coordinator's own weld AND grind loops are both exhausted (`_weldLoopExhausted && _grindLoopExhausted`), the saturated check is bypassed. This catches stale projected-block references that look "alive" (non-null IMySlimBlock) after being built — `IsTargetListSaturated()` counts them as valid but they're no longer actionable.

### Fix 4: Idle counter uses filtered targets
`_consecutiveEmptyScans` now uses the coordinator's own range-filtered target counts (`State.PossibleWeldTargets.CurrentCount`) instead of the cluster-wide raw candidate counts. The cluster scan with `skipRangeCheck=true` may find blocks in the BoundingBox area that no member can reach — those shouldn't prevent idle backoff.
## Files Affected
- `NanobotSystem.cs` — fields: `_scanSkippedSaturated`, `_rescanForced`, `_lastFullScanTime`, `_lastScanWeldCandidateCount`, `_lastScanGrindCandidateCount`
- `NanobotSystem.Scanning.cs` — `IsTargetListSaturated()`, `UpdateSourcesAndTargetsTimer`, `AsyncClusterScan`
- `NanobotSystem.Operations.cs` — `ImmediateRescanTrigger` coordinator signal
