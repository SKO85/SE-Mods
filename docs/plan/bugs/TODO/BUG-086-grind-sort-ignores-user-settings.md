# BUG-086: Grind sort order (farthest/nearest/smallest) ignored when targets exceed per-grid cap

## Status: Fixed
## Severity: High
## Version: v2.5.1
## Found In: NanobotSystem.Scanning.cs — SortAndCapGridCandidates / TruncateGridAware

## Description

When grinding with `GrindIgnorePriorityOrder` enabled, the farthest/nearest/smallest-grid sort settings are silently overridden during the scan phase. The BaR appears to grind in the correct order initially, then switches to processing blocks in seemingly random or middle-of-the-range order.

The issue has two parts:

### 1. Per-grid cap ignores `GrindIgnorePriorityOrder` (primary cause)

`SortAndCapGridCandidates` enforces a 256-block per-grid budget during scanning. When a grid contributes >256 grind targets, it sorts and keeps the "best" 256. However, it **always** sorts by priority first, ignoring the `GrindIgnorePriorityOrder` flag.

With `GrindIgnorePriorityOrder` ON and "farthest first":
- Grid has 500 targets: high-priority Armor at d=100-200, lower-priority Conveyors at d=300-400
- Cap sorts by priority → keeps Armor (closer), discards Conveyors (farther)
- The truly farthest blocks are thrown away before any downstream sort can recover them

### 2. `TruncateGridAware` disrupts sort order (secondary cause)

In `ApplyClusterResultToSelf`, after sorting, `TruncateGridAware` enforces per-grid minimum quotas by sending excess items to an overflow list appended at the end. This breaks the distance ordering. Pre-sorted cluster results compound the issue since members skip their own sort.

## Steps to Reproduce

1. Place 20+ BaRs on a grid/station
2. Have multiple connected grids with >256 grindable blocks each
3. Enable `GrindIgnorePriorityOrder` in terminal
4. Set "Farthest First" sort mode
5. Observe: BaRs grind blocks in the correct distance order while target count is low, but switch to random-seeming order when target count exceeds 256 per grid

## Root Cause

1. `SortAndCapGridCandidates` (NanobotSystem.Scanning.cs:~1197) unconditionally sorts by block type priority before distance, regardless of the `GrindIgnorePriorityOrder` flag. This causes the per-grid 256-block cap to discard blocks that are the farthest/nearest but have lower priority.

2. `TruncateGridAware` (NanobotSystem.Scanning.cs:~1125) appends overflow items at the end of the kept list, breaking the sort order established by `PreSortClusterCandidates` or the local sort.

Profiling data confirms the scenario: 58 BaRs in one cluster, 760-1024 grind candidates across 3-10 grids, all truncated to 256. The per-grid cap fires on large grids, discarding blocks based on priority instead of the user's sort preference.

## Fix

### Fix 1 — SortAndCapGridCandidates (NanobotSystem.Scanning.cs:~1197)
Read the `GrindIgnorePriorityOrder` flag and skip the priority comparison when it's set. The cap now selects blocks matching the user's actual sort preference.

### Fix 2 — ApplyClusterResultToSelf (NanobotSystem.Scanning.cs:~1321, ~1397)
Add a post-truncation re-sort that always runs when truncation occurred or when using pre-sorted cluster results. This restores correct distance ordering after `TruncateGridAware` and applies the member's own distances instead of the coordinator's.

### Fix 3 — Smallest-grid sort: group blocks by grid (all 4 sort locations)
When `GrindSmallestGridFirst` is selected, blocks from different grids of the same size were interleaved by distance, causing "all over the place" grinding. Added grid entity ID as a secondary sort key between grid-size and distance, so all blocks from the same grid stay together and are sorted nearest-first within that grid. Sort order is now: smallest grid → group by grid → nearest within grid.
