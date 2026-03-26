# BUG-008: Deselecting a sort toggle (Nearest/Farthest/Smallest Grid) clears all sort options

## Status: Fixed
## Severity: Medium
## Version: v2.5.0
## Found In: Terminal UI — sort toggle buttons (Nearest first, Farthest first, Smallest Grid first)
## Description
When clicking an already-active sort toggle to deselect it, the other two sort options are also turned off. Expected behavior: only the clicked toggle should be deselected and the sort should cycle to the next mode.
## Root Cause
The setter lambdas for `CreateGrindFarFirst` and `CreateGrindSmallestGridFirst` in `Terminal/OnOffSwitches.cs` only handled the `if (value)` (activation) path. There was no `else` branch for deactivation, so toggling off left flags unchanged or cleared incorrectly, causing all three toggles to appear off.
## Fix
Added `else` blocks to the two setters in `Terminal/OnOffSwitches.cs`:

- **GrindFarFirst** (~line 931): deactivating FarFirst now activates NearFirst by setting `GrindNearFirst` and clearing `GrindSmallestGridFirst`.
- **GrindSmallestGridFirst** (~line 1044): deactivating SmallestGridFirst now activates FarFirst (default) by clearing `GrindSmallestGridFirst`.

The three modes cycle: FarFirst → NearFirst → SmallestGridFirst → FarFirst.
