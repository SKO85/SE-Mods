# FEAT-075: Saturated scan skip
## Status: In Progress
## Priority: High
## Version: v2.5.4
## Summary
Skip the expensive full-grid rescan when the coordinator still has plenty of valid (non-destroyed) targets. Quick-count live targets (~12µs) instead of running the 50ms+ scan that would produce a nearly identical result.
## Motivation
Profiling session `20260416211403` (58 BaRs grinding 10K-block grid) shows AsyncClusterScan at 49ms avg, 17 calls in 180s. Each scan iterates all ~10K blocks, partitions, and sorts — producing 256 grind targets. Between scans (10s), only ~10-20 targets are consumed. So 90%+ of each rescan is wasted work producing identical results.
## Design
Before triggering a coordinator scan, count live (non-destroyed) targets in the coordinator's existing target lists. If the count exceeds `SaturatedRescanThreshold` (64 = 25% of 256-cap), skip the scan. Members also skip when the coordinator does.

Safety: force a rescan every `MaxScanSkipDuration` (60s) regardless of saturation to catch new blocks, external changes, or edge cases.

Implementation:
- Add `IsTargetListSaturated()` helper: iterates target lists counting live blocks, early-exits at threshold
- In `UpdateSourcesAndTargetsTimer`: coordinator checks saturation before calling `StartAsyncClusterScan`
- `_scanSkippedSaturated` volatile flag: set by coordinator, read by members to also skip
- `_lastFullScanTime`: tracks when a real scan last ran, forces rescan at 60s
## Files Affected
- `NanobotSystem.cs` — new fields
- `NanobotSystem.Scanning.cs` — `IsTargetListSaturated()`, modified `UpdateSourcesAndTargetsTimer`, `_lastFullScanTime` set in `AsyncClusterScan`
## Testing
- Profile grinding 10K+ block grid: AsyncClusterScan calls should drop from ~17/180s to ~3-5/180s
- Verify grinding continues normally (BaRs don't idle prematurely)
- Verify forced rescan happens within 60s even when saturated
- Verify new targets are discovered after large batch of blocks destroyed
- Verify settings change triggers rescan (cluster rebuild resets coordinator)
