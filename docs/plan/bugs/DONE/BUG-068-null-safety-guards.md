# BUG-068: Missing null guards in welding/collecting hot paths
## Status: Fixed
## Severity: Medium
## Version: v2.5.0
## Found In: Code review round 5 — NanobotSystem.Welding.cs:540, NanobotSystem.Collecting.cs:114
## Description
Two null-safety gaps:
1. `ServerFindMissingComponents` casts `targetData.Block.CubeGrid as MyCubeGrid` (line 540) but does not check if the cast returned null before using it. Blocks from closing grids can have null CubeGrid.
2. `ServerDoCollectFloating` uses `collectingFirstTarget.Entity` (line 119) to compute transport position without null-checking Entity. The entity reference can become null if the floating object was removed between iteration and transport start.
## Root Cause
Missing null guards after `as` cast and on entity reference.
## Fix
- `NanobotSystem.Welding.cs:541` — Added `if (cubeGrid == null) return false;` after `as MyCubeGrid` cast.
- `NanobotSystem.Collecting.cs:114` — Added `collectingFirstTarget.Entity != null` to the transport start condition.
