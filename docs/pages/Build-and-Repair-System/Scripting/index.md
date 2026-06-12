---
layout: default
title: Scripting API
parent: Build and Repair System
nav_order: 3
---

# Scripting API

The Build and Repair System exposes a set of terminal properties that can be read and written from a **Programmable Block** script. These properties use the standard Space Engineers `GetValue` / `SetValue` terminal property interface.

All property names are prefixed with `BuildAndRepair.`.

> **Note:** When the server locks script control (`ScriptControllFixed = true` in `ModSettings.xml`), the script-control toggle and the list/function properties (priority lists, targets, missing components, production helpers) are not registered and cannot be used from scripts.

> Properties whose terminal option is locked server-wide via a `*Fixed` setting are read-only — writes from a script are ignored.

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
| `BuildAndRepair.AllowBuild`       | `bool` | Enable or disable welding of projected blocks (the **Build Projections** toggle).                              |
| `BuildAndRepair.ScriptControlled` | `bool` | When `true`, the system does not automatically pick targets. Each weld/grind action must be chosen via script. |
| `BuildAndRepair.CollectIfIdle`    | `bool` | When `true`, floating objects are collected even when there is nothing to weld or grind.                       |
| `BuildAndRepair.ShowArea`         | `bool` | Show or hide the in-world work area box.                                                                       |
| `BuildAndRepair.SoundVolume`      | `float` | Per-block sound volume as a percentage (0–100), matching the terminal slider.                                 |
| `BuildAndRepair.DisableTickingSound` | `bool` | Silence the ticking / unable sound for this block. Only available while the server-wide `DisableTickingSound` is off. |
| `BuildAndRepair.DisableParticleEffects` | `bool` | Disable the flying nanobot trace for this block. Only available while the server-wide `DisableParticleEffects` is off. |

### Search & Work Mode

| Property                  | Type   | Description                                                                                                                                     |
| ------------------------- | ------ | ----------------------------------------------------------------------------------------------------------------------------------------------- |
| `BuildAndRepair.Mode`     | `long` | The active search mode. Cast to/from the `SearchModes` enum (`Grids`, `BoundingBox`).                                                           |
| `BuildAndRepair.WorkMode` | `long` | The active work mode. Cast to/from the `WorkModes` enum (`WeldBeforeGrind`, `GrindBeforeWeld`, `WeldOnly`, `GrindOnly`). The legacy `GrindIfWeldGetStuck` value still exists in the enum for backwards-compatibility but is migrated to `WeldBeforeGrind` at runtime — new scripts should not use it. |
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

### Grind Order

| Property                                   | Type   | Description                                                                  |
| ------------------------------------------ | ------ | ----------------------------------------------------------------------------- |
| `BuildAndRepair.GrindIgnorePriorityOrder`  | `bool` | Ignore the grind priority list order and target the nearest grind block.     |
| `BuildAndRepair.GrindNearFirst`            | `bool` | Grind the nearest blocks first.                                              |
| `BuildAndRepair.GrindFarFirst`             | `bool` | Grind the farthest blocks first.                                             |
| `BuildAndRepair.GrindSmallestGridFirst`    | `bool` | Grind blocks on the smallest grid first.                                     |

Only one of `GrindNearFirst`, `GrindFarFirst`, and `GrindSmallestGridFirst` is active at a time — setting one clears the others (farthest-first is the state where the other two are off).

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

| Property                                  | Type   | Description                                                |
| ----------------------------------------- | ------ | ---------------------------------------------------------- |
| `BuildAndRepair.PushIngotOreImmediately`  | `bool` | Immediately push ingots and ore to connected inventories.  |
| `BuildAndRepair.PushComponentImmediately` | `bool` | Immediately push components to connected inventories.      |
| `BuildAndRepair.PushItemsImmediately`     | `bool` | Immediately push other items to connected inventories.     |

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
| `BuildAndRepair.CurrentTarget`          | `IMySlimBlock`                              | The block currently being welded (read-only).             |
| `BuildAndRepair.CurrentGrindTarget`     | `IMySlimBlock`                              | The block currently being ground (read-only).             |

### Production & Inventory Helpers

| Property                                              | Type                                                                        | Description                                                                                                              |
| ----------------------------------------------------- | --------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------ |
| `BuildAndRepair.ProductionBlock.EnsureQueued`         | `Func<IEnumerable<long>, MyDefinitionId, int, int>`                        | Ensures a component is queued in one of the specified assemblers. Returns the amount still needed after queuing.          |
| `BuildAndRepair.Inventory.NeededComponents4Blueprint` | `Func<IMyProjector, Dictionary<MyDefinitionId, MyFixedPoint>, int>`        | Calculates the components needed to complete a projector's blueprint. Returns the number of component types.             |

---

## Companion Script

A maintained companion script is available that handles automatic assembler queuing and multi-display status output. **It only works with SKO's maintained versions of the mod — not the original mod by Dummy08.**

- **Steam Workshop:** [Nanobot Build and Repair System Queuing / Display / Scripting (Maintained)](https://steamcommunity.com/sharedfiles/filedetails/?id=3472701905)
- **Documentation:** [Companion Script](../Companion-Script/)
