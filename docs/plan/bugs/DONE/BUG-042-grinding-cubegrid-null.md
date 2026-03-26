# BUG-042: Grinding loop missing CubeGrid null guard
## Status: Fixed
## Severity: High
## Version: v2.5.0
## Found In: Code review — NanobotSystem.Grinding.cs

## Description

Two locations in the grinding path access `Block.CubeGrid` without null checks. CubeGrid can be null on blocks from closing/deleted grids, causing `NullReferenceException` crashes.

**Location 1 — ServerTryGrinding loop (line 43):**
```csharp
var gridId = targetData.Block.CubeGrid.EntityId;
```
The block null check at line 38 exists, but CubeGrid can independently be null on blocks from closing grids (same class of bug as BUG-037 which fixed IsSameBlock).

**Location 2 — ServerDoGrind (line 125-127):**
```csharp
var targetGrid = target.CubeGrid;
if (targetGrid.Physics == null || !targetGrid.Physics.Enabled) return false;
```
No null check on `targetGrid` before accessing `.Physics`.

## Root Cause

Blocks from grids that are in the process of closing can have CubeGrid set to null. The target list is populated from a background scan, and by the time the main thread iterates it, the grid may have been removed.

## Fix

**Location 1** — Add CubeGrid null guard after Block null check:
```csharp
if (targetData.Block == null) continue;
if (targetData.Block.CubeGrid == null) continue;
```

**Location 2** — Add null check for targetGrid:
```csharp
var targetGrid = target.CubeGrid;
if (targetGrid == null || targetGrid.Physics == null || !targetGrid.Physics.Enabled) return false;
```
