# FEAT-074: Quickselect sort optimization for large grid scans
## Status: Done (shipped — QuickSelect helper at `Scanning.cs:1745`, used in `SortAndCapGridCandidates` at `Scanning.cs:2027`)
## Priority: High
## Version: v2.5.4
## Summary
Replace full O(n log n) sort in `SortAndCapGridCandidates` with quickselect O(n) partial sort to find top-K candidates, then sort only those K items.
## Motivation
Profiling session `20260416205112` (58 BaRs grinding an 11,732-block grid, 180s) shows `SortAndCapGridCandidates` at 13.1ms avg (max 46.9ms, total 617ms). The sort step alone costs 20-33ms per call sorting all 11,732 items when only 256 are kept. Combined with the 53-76ms block iteration, each scan of this grid costs ~100ms. Over 15 scans that's ~1,500ms. The sort step is the one phase that can be optimized without losing correctness.
## Design
Implement a quickselect (introselect) algorithm that partitions the candidate list around the K-th element in O(n) average time. After quickselect, the first K elements are the top-K candidates (unordered). Then sort only those K elements in O(k log k).

For n=11,732 and k=256:
- Current: O(n log n) ≈ 158K comparisons → 20-33ms
- Quickselect + sort K: O(n + k log k) ≈ 14K comparisons → ~3ms
- Expected savings: ~22ms per scan, ~330ms over 15 scans

Implementation:
- Add a `QuickSelect` helper method that works with `List<T>`, start/end indices, k, and `IComparer<T>`
- Use median-of-three pivot selection for robustness against sorted/reverse-sorted inputs
- Fall back to insertion sort for small partitions (≤16 elements)
- In `SortAndCapGridCandidates`: when `effectiveCount > maxKeep * 2`, use quickselect to partition around the k-th element, then `list.Sort(startIndex, keep, comparator)` on just the top portion
- When `effectiveCount <= maxKeep * 2`, use the existing full sort (quickselect overhead not worth it for small lists)
## Files Affected
- `NanobotSystem.Scanning.cs` — QuickSelect helper, modified `SortAndCapGridCandidates`
## Testing
- Profile with 58 BaRs grinding the 11,732-block grid: sort step should drop from 20-33ms to ~3ms
- Verify grind target selection produces same quality results (nearest/farthest blocks selected correctly)
- Verify no change in behavior for small grids (< 512 candidates)
