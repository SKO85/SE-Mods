# FEAT-006: Decouple source/push-target scanning from target scanning
## Status: Done
## Priority: Medium
## Version: v2.5.0
## Summary
Separate inventory source scanning from the weld/grind target scan loop so sources use raw (unsorted) block lists and don't force the target scan to continue past full target lists.
## Motivation
Source scanning was interleaved with target scanning in `AsyncAddBlocksOfGrid`. This caused:
1. The block iteration to continue past full target lists just to find sources (iterating thousands of armor blocks for nothing).
2. Recursive traversal into connected grids even when targets were full, just for source discovery — each triggering an expensive sort.
3. Sources to use the sorted block list when they don't need sort order at all.
## Design
- Removed `possibleSources` parameter from `AsyncAddBlocksOfGrid` and renamed `AsyncAddBlockIfTargetOrSource` to `AsyncAddBlockIfTarget`.
- Main target scan loop now breaks immediately when target lists are full. Connected grid recursion only continues when more targets are needed.
- New `AsyncScanForSources()` method: separate BFS traversal starting from the BaR's own grid. Uses `SharedGridBlockCache.GetBlocks()` (raw, unsorted) to iterate only FatBlocks. Follows mechanical connections + connectors to connected grids. No sorting, no priority filtering.
- Called in `AsyncUpdateSourcesAndTargets` after both target scans (Grid + BoundingBox) complete.
## Files Affected
- `NanobotSystem.Scanning.cs` — removed sources from `AsyncAddBlocksOfGrid`, added `AsyncScanForSources`, renamed target method
## Testing
- Verify sources are still found correctly (cargo, assembler, welder, grinder, sorter, connector, cryo, refinery on connected grids)
- Verify push targets still work (cargo containers + refineries)
- Verify cross-grid sources via connectors and mechanical connections still discovered
- Profile: `AsyncScanForSources` should show low cost; `AsyncAddBlocksOfGrid` should stop earlier when target lists fill up
