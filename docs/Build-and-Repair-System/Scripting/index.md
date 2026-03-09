---
layout: default
title: Scripting API
parent: Build and Repair System
nav_order: 3
---

# Scripting API

The Build and Repair System exposes a set of terminal properties that can be read and written from a **Programmable Block** script. These properties use the standard Space Engineers `GetValue` / `SetValue` terminal property interface.

All property names are prefixed with `BuildAndRepair.`.

---

## Detecting a Build and Repair Block

To check whether a block is a Build and Repair System, try reading a known property. If the property exists, the block is a BaR system:

```csharp
bool IsBuildAndRepairBlock(IMyTerminalBlock block)
{
    try
    {
        block.GetValueBool("BuildAndRepair.ScriptControlled");
        return true;
    }
    catch
    {
        return false;
    }
}
```

---

## Properties

### General

| Property                          | Type   | Description                                                                                                    |
| --------------------------------- | ------ | -------------------------------------------------------------------------------------------------------------- |
| `BuildAndRepair.AllowBuild`       | `bool` | Enable or disable welding of projected blocks.                                                                 |
| `BuildAndRepair.ScriptControlled` | `bool` | When `true`, the system does not automatically pick targets. Each weld/grind action must be chosen via script. |
| `BuildAndRepair.CollectIfIdle`    | `bool` | When `true`, floating objects are collected even when there is nothing to weld or grind.                       |

### Search & Work Mode

| Property                  | Type   | Description                                                                                                                                     |
| ------------------------- | ------ | ----------------------------------------------------------------------------------------------------------------------------------------------- |
| `BuildAndRepair.Mode`     | `long` | The active search mode. Cast to/from the `SearchModes` enum (`Grids`, `BoundingBox`).                                                           |
| `BuildAndRepair.WorkMode` | `long` | The active work mode. Cast to/from the `WorkModes` enum (`WeldBeforeGrind`, `GrindBeforeWeld`, `GrindIfWeldGetStuck`, `WeldOnly`, `GrindOnly`). |
| `BuildAndRepair.WeldMode` | `long` | The active weld mode. Cast to/from the `AutoWeldOptions` enum (`WeldFull`, `WeldFunctional`, `WeldSkeleton`).                                   |

### Colors

| Property                        | Type      | Description                                |
| ------------------------------- | --------- | ------------------------------------------ |
| `BuildAndRepair.UseIgnoreColor` | `bool`    | Whether the ignore-color filter is active. |
| `BuildAndRepair.IgnoreColor`    | `Vector3` | The ignore color as an HSV vector.         |
| `BuildAndRepair.UseGrindColor`  | `bool`    | Whether the grind-color filter is active.  |
| `BuildAndRepair.GrindColor`     | `Vector3` | The grind color as an HSV vector.          |

### Grind Janitor

| Property                                       | Type   | Description                                            |
| ---------------------------------------------- | ------ | ------------------------------------------------------ |
| `BuildAndRepair.GrindJanitorEnemies`           | `bool` | Grind enemy-owned blocks.                              |
| `BuildAndRepair.GrindJanitorNotOwned`          | `bool` | Grind blocks with no ownership.                        |
| `BuildAndRepair.GrindJanitorNeutrals`          | `bool` | Grind neutral-owned blocks.                            |
| `BuildAndRepair.GrindJanitorOptionDisableOnly` | `bool` | Only grind functional blocks until they stop working.  |
| `BuildAndRepair.GrindJanitorOptionHackOnly`    | `bool` | Only grind functional blocks until they can be hacked. |

### Work Area

| Property                             | Type    | Description                            |
| ------------------------------------ | ------- | -------------------------------------- |
| `BuildAndRepair.AreaWidth`           | `float` | Work area width in metres.             |
| `BuildAndRepair.AreaHeight`          | `float` | Work area height in metres.            |
| `BuildAndRepair.AreaDepth`           | `float` | Work area depth in metres.             |
| `BuildAndRepair.AreaOffsetLeftRight` | `float` | Work area left/right offset in metres. |
| `BuildAndRepair.AreaOffsetUpDown`    | `float` | Work area up/down offset in metres.    |
| `BuildAndRepair.AreaOffsetFrontBack` | `float` | Work area front/back offset in metres. |

### Push / Collect

| Property                                 | Type   | Description                                               |
| ---------------------------------------- | ------ | --------------------------------------------------------- |
| `BuildAndRepair.PushIngotOreImmediately` | `bool` | Immediately push ingots and ore to connected inventories. |

### Priority Lists

Priority lists are accessed through delegate-style properties. The index corresponds to the position in the priority list.

| Property                            | Type                     | Description                                          |
| ----------------------------------- | ------------------------ | ---------------------------------------------------- |
| `BuildAndRepair.WeldPriorityList`   | `MemorySafeList<string>` | Read-only list of weld priority class names.         |
| `BuildAndRepair.GetWeldPriority`    | `Func<int, int>`         | Get the priority value for a weld class by index.    |
| `BuildAndRepair.SetWeldPriority`    | `Action<int, int>`       | Set the priority value for a weld class by index.    |
| `BuildAndRepair.GetWeldEnabled`     | `Func<int, bool>`        | Check whether a weld class is enabled by index.      |
| `BuildAndRepair.SetWeldEnabled`     | `Action<int, bool>`      | Enable or disable a weld class by index.             |
| `BuildAndRepair.GrindPriorityList`  | `MemorySafeList<string>` | Read-only list of grind priority class names.        |
| `BuildAndRepair.GetGrindPriority`   | `Func<int, int>`         | Get the priority value for a grind class by index.   |
| `BuildAndRepair.SetGrindPriority`   | `Action<int, int>`       | Set the priority value for a grind class by index.   |
| `BuildAndRepair.GetGrindEnabled`    | `Func<int, bool>`        | Check whether a grind class is enabled by index.     |
| `BuildAndRepair.SetGrindEnabled`    | `Action<int, bool>`      | Enable or disable a grind class by index.            |
| `BuildAndRepair.ComponentClassList` | `MemorySafeList<string>` | Read-only list of collect priority class names.      |
| `BuildAndRepair.GetCollectPriority` | `Func<int, int>`         | Get the priority value for a collect class by index. |
| `BuildAndRepair.SetCollectPriority` | `Action<int, int>`       | Set the priority value for a collect class by index. |
| `BuildAndRepair.GetCollectEnabled`  | `Func<int, bool>`        | Check whether a collect class is enabled by index.   |
| `BuildAndRepair.SetCollectEnabled`  | `Action<int, bool>`      | Enable or disable a collect class by index.          |

### Script Control – Target Picking

When `BuildAndRepair.ScriptControlled` is `true`, the script chooses which block the system welds or grinds.

| Property                                  | Type           | Description                                   |
| ----------------------------------------- | -------------- | --------------------------------------------- |
| `BuildAndRepair.CurrentPickedTarget`      | `IMySlimBlock` | Get or set the block the system should weld.  |
| `BuildAndRepair.CurrentPickedGrindTarget` | `IMySlimBlock` | Get or set the block the system should grind. |

### Read-Only State

| Property                                | Type                                        | Description                                               |
| --------------------------------------- | ------------------------------------------- | --------------------------------------------------------- |
| `BuildAndRepair.MissingComponents`      | `MemorySafeDictionary<MyDefinitionId, int>` | Components required to complete all current weld targets. |
| `BuildAndRepair.PossibleTargets`        | `MemorySafeList<IMySlimBlock>`              | Blocks currently queued for welding.                      |
| `BuildAndRepair.PossibleGrindTargets`   | `MemorySafeList<IMySlimBlock>`              | Blocks currently queued for grinding.                     |
| `BuildAndRepair.PossibleCollectTargets` | `MemorySafeList<IMyEntity>`                 | Floating objects currently queued for collection.         |

---

## Companion Script

A maintained companion script is available that handles automatic assembler queuing and multi-display status output:

- **Steam Workshop:** [Nanobot Build and Repair System Queuing / Display / Scripting (Maintained)](https://steamcommunity.com/sharedfiles/filedetails/?id=3472701905)
- **Source:** `SKO-Nanobot-BuildAndRepair-System-Script/Script.cs` in this repository

See the [FAQ](../FAQ/#companion-script) for setup instructions.
