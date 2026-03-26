# BUG-033: Null dereference in ServerTryGrinding when targetData.Block is null
## Status: Fixed
## Severity: Critical
## Version: v2.5.0
## Found In: NanobotSystem.Grinding.cs:37-44

## Description

The null guard at line 37 is inverted — it only skips when Block IS non-null AND FatBlock IS non-null AND FatBlock is Closed:

```csharp
if (targetData.Block != null && targetData.Block.FatBlock != null && targetData.Block.FatBlock.Closed)
{
    continue;
}
```

If `targetData.Block` is null, the short-circuit evaluates to false and execution falls through to line 44 where `targetData.Block.CubeGrid.EntityId` is accessed, causing a NullReferenceException crash.

The welding loop (NanobotSystem.Welding.cs:105) has the same pattern but it's inside a nested `if (isWeldable)` block that effectively guards against null blocks earlier. The grinding loop has no such guard.

## Root Cause

Missing explicit null check for `targetData.Block` before the grid limit and assignment logic at lines 42-84.

## Fix

Add `if (targetData.Block == null) continue;` before line 42 (the grid limit check), or restructure the FatBlock.Closed check to also handle null Block:

```csharp
if (targetData.Block == null) continue;
if (targetData.Block.FatBlock != null && targetData.Block.FatBlock.Closed) continue;
```
