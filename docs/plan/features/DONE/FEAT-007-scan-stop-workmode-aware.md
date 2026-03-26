# FEAT-007: Work-mode-aware scan stopping
## Status: Done
## Priority: Medium
## Version: v2.5.0
## Summary
Make `ShouldStopScan` and grind-target collection aware of the actual work mode and grind conditions so the scan stops as soon as the relevant target lists are full, rather than iterating the entire grid looking for targets that can never be found.
## Motivation
When `grindingEnabled` is true but no grind conditions are configured (no grind color, no auto-grind relation), `AsyncAddBlockIfTarget` never adds grind targets (gated by `useGrindColor || autoGrindRelation != 0`). However, the non-null grind list causes `ShouldStopScan` to wait for 64 grind entries that never arrive — forcing the scan to iterate ALL blocks on every grid.

Example: WeldBeforeGrind mode with no grind color/auto-grind set. Weld targets fill to 64 early, but the scan continues through all 7,474 blocks because grind list stays at 0/64.

Similarly in GrindOnly mode when welding targets are irrelevant, or when collecting is disabled but the floating list is still checked.
## Design
Pass `null` for the grind target list when no grind conditions are configured (`!useGrindColor && autoGrindRelation == 0`), so `ShouldStopScan` treats it as "full" immediately. This makes the scan stop as soon as the actually-active target lists are full.

The fix is in `AsyncUpdateSourcesAndTargets` where `grindingEnabled` is computed — add the grind condition check. This naturally flows through all callers (`AsyncAddBlocksOfGrid`, `AsyncAddBlocksOfBox`) since they pass `null` for disabled lists.
## Files Affected
- `NanobotSystem.Scanning.cs` — `AsyncUpdateSourcesAndTargets()` grindingEnabled calculation
## Testing
- WeldOnly mode: scan should stop after MaxPossibleWeldTargets (already works via null grind list)
- GrindOnly mode: scan should stop after MaxPossibleGrindTargets (already works via null weld list)
- WeldBeforeGrind with no grind color/auto-grind: scan should stop after MaxPossibleWeldTargets instead of iterating all blocks
- WeldBeforeGrind with grind color set: scan should continue until both lists full (existing behavior preserved)
- Profile: `AsyncAddBlocksOfGrid` call count and total time should drop significantly when grind conditions aren't configured
