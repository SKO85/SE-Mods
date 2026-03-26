# BUG-051: BlockName IMyCubeGrid branch unreachable — grids show EntityId
## Status: Fixed
## Severity: Low
## Version: v2.5.0
## Found In: Code review — SKO-Nanobot-BuildAndRepair-System-Script/Script.cs

## Description

In `BlockName()` at lines 2038-2045, the `IMyCubeGrid` check is placed AFTER the `IMyEntity` check. Since `IMyCubeGrid` inherits from `IMyEntity`, the entity branch always matches first, making the grid branch unreachable.

```csharp
var entity = block as IMyEntity;
if (entity != null)
{
    return string.Format("{0} ({1})", entity.DisplayName, entity.EntityId);  // Grids match here
}

var cubeGrid = block as IMyCubeGrid;  // UNREACHABLE for grids
if (cubeGrid != null) return cubeGrid.DisplayName;
```

Grids display as `"GridName (12345)"` instead of just `"GridName"`.

## Fix

Move the `IMyCubeGrid` check before the `IMyEntity` check:
```csharp
var cubeGrid = block as IMyCubeGrid;
if (cubeGrid != null) return cubeGrid.DisplayName;

var entity = block as IMyEntity;
if (entity != null) { ... }
```
