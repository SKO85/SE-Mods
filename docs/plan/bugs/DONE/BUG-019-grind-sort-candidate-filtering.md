# BUG-019: Grind sorting priority not fully respected (nearest/farthest/smallest)
## Status: Fixed
## Severity: Medium
## Version: v2.5.0
## Found In: `UtilsSorting.cs`, `NanobotSystem.Scanning.cs` — ApplyClusterResultToSelf, AsyncAddBlocksOfGrid
## Description
Grinding sort modes (Nearest First, Farthest First, Smallest Grid First) were not fully respected due to three upstream issues that corrupted the candidate pool before the final sort ran.

### Issue 1 — Phase 1 filter excludes valid grind targets in mixed work modes
`SortWithPriorityAndDistance` (UtilsSorting.cs) uses `RemoveAll` with a single priority handler. When `isGrinding=false` (BaR in WeldBeforeGrind/GrindBeforeWeld and not currently grinding), the weld priority handler was used. Blocks whose class is disabled in weld priority but enabled in grind priority were removed from the iteration list entirely — they never reached `AsyncAddBlockIfGrindTarget`.

### Issue 2 — Pre-sort cap truncation drops wrong blocks
`ApplyClusterResultToSelf` capped `_TempPossibleGrindTargets` at 256 **during collection**, iterating `result.GrindCandidates` in Phase 1 order (per-grid, grid-visit order). When >256 in-range targets existed, the first 256 in Phase 1 order survived. If Phase 1 order didn't match the desired sort (e.g., farthest blocks first but user wants nearest), the wrong blocks made it through.

### Issue 3 — `isGrindingMode` too narrow, causes wrong sort path in GrindBeforeWeld
`AsyncAddBlocksOfGrid` defined `isGrindingMode` as `WorkModes.GrindOnly` only. In `GrindBeforeWeld` mode, `isGrindingMode` was false, so `isGrinding` depended on transient `State.Grinding`/`State.NeedGrinding` flags. During brief windows between operation cycles where both flags are false, the weld sort path triggered instead of grind sort. Profiling confirmed intermittent `isGrinding=False` entries even while actively grinding a 10k-block grid, causing:
- 2x slower sort (weld path: 75-100ms vs grind path: 31-44ms on a 10k-block grid)
- Phase 1 sort order mismatching Phase 2 grind sort, reducing cap truncation accuracy

The final Phase 2 sort correctly implements all three modes, but operated on a candidate pool that was already corrupted by the above issues.

## Root Cause
1. `UtilsSorting.cs:26` — `RemoveAll` used only one handler (`BlockWeldPriority` or `BlockGrindPriority`) instead of the union.
2. `NanobotSystem.Scanning.cs:865` — Cap applied before sort in `ApplyClusterResultToSelf`, truncating in collection order instead of desired sort order.
3. `NanobotSystem.Scanning.cs:378` — `isGrindingMode` only checked `GrindOnly`, missing `GrindBeforeWeld`.

## Fix
1. **UtilsSorting.cs** — Changed `RemoveAll` to keep blocks enabled in **either** weld or grind priority handler:
   ```csharp
   list.RemoveAll(i => !weldPriority.GetEnabled(i) && !grindPriority.GetEnabled(i));
   ```

2. **NanobotSystem.Scanning.cs (ApplyClusterResultToSelf)** — Removed pre-sort cap from weld/grind candidate collection loops. Added post-sort truncation after the Phase 2 sort, so the desired sort order determines which blocks survive the 256 cap.

3. **NanobotSystem.Scanning.cs (AsyncAddBlocksOfGrid)** — Extended `isGrindingMode` to include `GrindBeforeWeld`:
   ```csharp
   var isGrindingMode = Settings.WorkMode == WorkModes.GrindOnly || Settings.WorkMode == WorkModes.GrindBeforeWeld;
   var isGrinding = isGrindingMode || State.Grinding || State.NeedGrinding;
   ```
   Also removed redundant `(State.Transporting && isGrindingMode)` term (already covered by `isGrindingMode`).

4. **Profiler** — `ApplyClusterResultToSelf` profiler log now includes pre-truncation counts (`pre=N`) for weld and grind targets to diagnose truncation events.
