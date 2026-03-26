# BUG-024: UtilsSorting accesses Welder.WorldAABB from background thread
## Severity: Medium
## Version: v2.5.0
## Status: TODO

## File
`Utils/UtilsSorting.cs:31`

## Description
`SortWithPriorityAndDistance` is called from `GetBlocksFromCache` which runs on a background scan thread. On line 31, it accesses `system.Welder.WorldAABB.Center`, which reads the game entity's world-space bounding box. Game entity properties like `WorldAABB` involve matrix computations that can return inconsistent values if the entity is being updated on the main thread simultaneously.

Could produce incorrect distance calculations causing wrong sort order. In the worst case, partially-written matrix data could produce NaN/Infinity values, making `List.Sort` throw `InvalidOperationException` (which is then silently swallowed by BUG-023's catch{}).

## Fix
Capture the welder position on the main thread before enqueueing the background scan, and pass it as a parameter to the sort method.
