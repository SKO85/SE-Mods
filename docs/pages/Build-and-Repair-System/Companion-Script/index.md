---
layout: default
title: Companion Script
parent: Build and Repair System
nav_order: 4
---

# Companion Script

> **This script only works with SKO's maintained versions of the Nanobot Build and Repair System mod. It will not work with the original mod by Dummy08.**
>
> Compatible versions: [Nerfed Version](https://steamcommunity.com/sharedfiles/filedetails/?id=2111073562) · [Original Resources Version](https://steamcommunity.com/sharedfiles/filedetails/?id=3099489876)

The companion script runs in a Programmable Block and provides two features on top of the mod itself:

- **Automatic assembler queuing** — when the Build and Repair System is missing components, the script reads its missing-components list and queues the required items into connected assemblers so welding can continue without manual intervention.
- **Multi-display status output** — status pages (weld/grind targets, missing items, priority lists, etc.) are written to configured LCD panels or cockpit screens and cycle automatically.

The script handles one or more independent **groups**, each pairing a set of Build and Repair blocks with a set of assemblers and optional display panels. A typical ship needs only one group; large builds with separate hangar bays can define one group per bay.

- **Steam Workshop:** [Nanobot Build and Repair System Queuing / Display / Scripting (Maintained)](https://steamcommunity.com/sharedfiles/filedetails/?id=3472701905)

---

## Quick Start

1. Place a **Programmable Block** on your grid.
2. Subscribe to the script on the [Steam Workshop](https://steamcommunity.com/sharedfiles/filedetails/?id=3472701905) and load it into the block.
3. Compile and run. The script auto-detects all Build and Repair blocks on the grid — no further configuration is needed for a basic setup.

For assembler queuing and named displays, continue to the configuration section below.

---

## Configuration

The configuration lives at the top of the script in a static array:

```csharp
static BuildAndRepairSystemQueuingGroup[] BuildAndRepairSystemQueuingGroups = { ... };
```

Each element of the array is one group. A group links a set of Build and Repair blocks to a set of assemblers and zero or more display panels.

### BuildAndRepairSystemQueuingGroup

| Property                        | Type                  | Description |
| ------------------------------- | --------------------- | ----------- |
| `Name`                          | `string`              | Display name shown in the status panel header. Optional. |
| `BuildAndRepairSystemGroupName` | `string`              | Name of a block group containing Build and Repair blocks. |
| `BuildAndRepairSystemNames`     | `string[]`            | Individual Build and Repair block names. |
| `AssemblerGroupName`            | `string`              | Name of a block group containing assemblers. |
| `AssemblerNames`                | `string[]`            | Individual assembler block names. |
| `Displays`                      | `DisplayDefinition[]` | Display panel definitions. Omit to skip all display output. |

`BuildAndRepairSystemGroupName` and `BuildAndRepairSystemNames` can be used together — their blocks are merged into one internal list. The same applies to the assembler properties. If no assembler is configured, display output still works; only queuing is skipped.

### DisplayDefinition

| Property          | Type            | Default    | Description |
| ----------------- | --------------- | ---------- | ----------- |
| `DisplayNames`    | `string[]`      | —          | Names of text panels or cockpit screens. Cockpit screens use the format `"BlockName[index]"`, e.g. `"Cockpit[0]"`. |
| `DisplayKinds`    | `DisplayKind[]` | `Status`   | Pages to cycle through on this display. See [Display Kinds](#display-kinds) below. |
| `DisplayMaxLines` | `int`           | `19`       | Maximum lines shown for list pages. |
| `DisplaySwitchTime` | `double`      | `5`        | Seconds between page switches. Set to `0` to show only the first page without cycling. |

---

## Display Kinds

| Value                | Description |
| -------------------- | ----------- |
| `ShortStatus`        | Compact summary: online state, current weld/grind target, counts of queued targets and floating items. |
| `Status`             | Full status: everything in ShortStatus plus search mode, work mode, weld mode, and script-controlled state. |
| `WeldTargets`        | List of blocks currently queued for welding. |
| `GrindTargets`       | List of blocks currently queued for grinding. |
| `CollectTargets`     | List of floating objects currently queued for collection. |
| `MissingItems`       | Components missing to complete all current weld targets. |
| `BlockWeldPriority`  | Weld priority list with enabled/disabled state per block class. |
| `BlockGrindPriority` | Grind priority list with enabled/disabled state per block class. |

---

## Examples

### Minimal — display only, no assemblers

```csharp
static BuildAndRepairSystemQueuingGroup[] BuildAndRepairSystemQueuingGroups = {
   new BuildAndRepairSystemQueuingGroup() {
      BuildAndRepairSystemGroupName = "BuildAndRepairGroup1",
      Displays = new[] {
         new DisplayDefinition {
            DisplayNames = new[] { "BuildAndRepairGroup1StatusPanel" },
            DisplayKinds = new[] { DisplayKind.Status, DisplayKind.WeldTargets, DisplayKind.MissingItems },
            DisplayMaxLines = 19,
            DisplaySwitchTime = 4
         }
      }
   }
};
```

### Standard — one group with assembler queuing and a status panel

```csharp
static BuildAndRepairSystemQueuingGroup[] BuildAndRepairSystemQueuingGroups = {
   new BuildAndRepairSystemQueuingGroup() {
      BuildAndRepairSystemGroupName = "BuildAndRepairGroup1",
      AssemblerGroupName = "AssemblerGroup1",
      Displays = new[] {
         new DisplayDefinition {
            DisplayNames = new[] { "BuildAndRepairGroup1StatusPanel", "Cockpit[0]" },
            DisplayKinds = new[] {
               DisplayKind.ShortStatus,
               DisplayKind.Status,
               DisplayKind.WeldTargets,
               DisplayKind.GrindTargets,
               DisplayKind.MissingItems
            },
            DisplayMaxLines = 19,
            DisplaySwitchTime = 4
         }
      }
   }
};
```

### Multiple panels, each showing a different page

You can place a wall of LCD panels and have each one show a different page permanently with no cycling. The `Displays` array can contain as many `DisplayDefinition` entries as you like, each pointing at a different panel with a single `DisplayKind`:

```csharp
static BuildAndRepairSystemQueuingGroup[] BuildAndRepairSystemQueuingGroups = {
   new BuildAndRepairSystemQueuingGroup() {
      BuildAndRepairSystemGroupName = "BuildAndRepairGroup1",
      AssemblerGroupName = "AssemblerGroup1",
      Displays = new[] {
         new DisplayDefinition {
            DisplayNames = new[] { "BaR Status Panel" },
            DisplayKinds = new[] { DisplayKind.Status }
         },
         new DisplayDefinition {
            DisplayNames = new[] { "BaR Weld Targets Panel" },
            DisplayKinds = new[] { DisplayKind.WeldTargets }
         },
         new DisplayDefinition {
            DisplayNames = new[] { "BaR Missing Items Panel" },
            DisplayKinds = new[] { DisplayKind.MissingItems }
         },
         new DisplayDefinition {
            DisplayNames = new[] { "BaR Grind Targets Panel" },
            DisplayKinds = new[] { DisplayKind.GrindTargets }
         }
      }
   }
};
```

Each panel shows exactly one page and never switches. `DisplaySwitchTime` is irrelevant when only one `DisplayKind` is listed. You can also put multiple panel names in a single `DisplayNames` array if you want the same page mirrored across several screens.

### Multiple groups — separate hangar bays

```csharp
static BuildAndRepairSystemQueuingGroup[] BuildAndRepairSystemQueuingGroups = {
   new BuildAndRepairSystemQueuingGroup() {
      Name = "Hangar 1",
      BuildAndRepairSystemGroupName = "Hangar1BaRSystems",
      AssemblerGroupName = "Hangar1Assemblers",
      Displays = new[] {
         new DisplayDefinition {
            DisplayNames = new[] { "Hangar1StatusPanel" },
            DisplayKinds = new[] { DisplayKind.Status, DisplayKind.MissingItems },
            DisplayMaxLines = 19,
            DisplaySwitchTime = 5
         }
      }
   },
   new BuildAndRepairSystemQueuingGroup() {
      Name = "Hangar 2",
      BuildAndRepairSystemGroupName = "Hangar2BaRSystems",
      AssemblerGroupName = "Hangar2Assemblers"
   }
};
```

---

## info-only Mode

To display status without queuing any components into assemblers, set the Programmable Block **argument** to `info-only` and rerun the script.

---

## Refresh

Block groups are refreshed automatically every 2 minutes. To force an immediate refresh, open the Programmable Block editor and close it, or recompile the script.

---

## Credits

Based on the original script by **Dummy08** ([original Workshop page](https://steamcommunity.com/sharedfiles/filedetails/?id=867822734)). The original script is no longer maintained.
