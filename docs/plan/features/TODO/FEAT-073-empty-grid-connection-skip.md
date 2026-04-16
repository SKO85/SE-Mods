# FEAT-073: Optimize empty grid connection traversal
## Status: In Progress
## Priority: Medium
## Version: v2.5.4
## Summary
When a grid is in the empty cache, skip iterating all blocks for connection discovery. Only check fat blocks.
## Motivation
Profiling session `20260416194445` shows `AsyncAddBlocksOfGrid` iterating all blocks on empty-cached grids (up to 1000 blocks) just to discover mechanical connections and connectors. Most blocks are slim-only (armor, etc.) and can never be connections. This wastes CPU on the background thread, especially for large grids.
## Design
In the empty-grid-cache fast path (lines 374-419 of `NanobotSystem.Scanning.cs`), the current code iterates ALL blocks via `SharedGridBlockCache.GetBlocks()` and checks each for `FatBlock != null` before testing connection types. Since connection blocks (mechanical, attachable top, connector) are always fat blocks, we can skip slim-only blocks entirely.

Implementation:
- Modify the empty-grid-cache path to only iterate blocks that have a `FatBlock` by filtering early
- Use `CubeGrid.GetFatBlocks()` instead of `SharedGridBlockCache.GetBlocks()` for the empty-grid path, since we only need fat blocks
- This avoids iterating hundreds of armor blocks that have no fat block

Alternative (simpler): keep the current loop but use the grid's fat block list directly. SE's `MyCubeGrid` exposes fat blocks as a separate collection.
## Files Affected
- `NanobotSystem.Scanning.cs` — empty grid cache path in `AsyncAddBlocksOfGrid`
## Testing
- Profile with idle BaRs on large grids (1000+ blocks): AsyncAddBlocksOfGrid times should decrease for empty-cached grids
- Verify connected grids (via rotors, pistons, connectors) are still discovered correctly
- Verify grids connected through chains (A-rotor-B-connector-C) are all discovered
