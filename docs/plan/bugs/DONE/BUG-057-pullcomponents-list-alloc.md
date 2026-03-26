# BUG-057: PullComponents allocates new List per source inventory iteration
## Status: Open
## Severity: Medium
## Version: v2.5.0
## Found In: Code review — NanobotSystem.Inventory.cs:262
## Description
`PullComponents()` creates `new List<MyInventoryItem>()` inside the `foreach (_PossibleSources)` loop at line 262. Every source inventory checked allocates a fresh list, causing GC pressure during the main-thread welding path.

With 10+ source inventories this runs every tick a BaR is welding and needs components — potentially hundreds of list allocations per second across all active BaRs.

## Root Cause
The list is created inside the loop instead of outside. The `.Clear()` call at line 297 already exists but runs too late — a new list was already allocated on the next iteration.

## Fix
Move `var tempInventoryItems = new List<MyInventoryItem>()` before the `foreach` loop. Call `tempInventoryItems.Clear()` at the top of each iteration instead of at the bottom. File: `NanobotSystem.Inventory.cs`.
