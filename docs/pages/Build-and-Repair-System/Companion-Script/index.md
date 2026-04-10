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
| `Status`             | Full status: everything in ShortStatus plus search mode, work mode, weld mode, script-controlled state, and the auto-queuing state (`Enabled (N assemblers)`, `Disabled (info-only)`, or `Disabled (no assemblers)`). |
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

## Per-LCD Configuration

In addition to the script-level `DisplayDefinition` config, each LCD (or cockpit surface) can be configured individually — either by tagging its name, by writing a small block into its Custom Data, or both. Both mechanisms are optional and are layered on top of the script config:

1. **Script config** (`BuildAndRepairSystemQueuingGroups`) — base config for every panel.
2. **Name tag** — decides which group a panel belongs to.
3. **Custom Data** — overrides the display settings for that single panel.

### Auto-discovery via a name tag

Add `[BaR:<group>]` anywhere in an LCD's CustomName and the script will auto-attach that panel to the matching Build and Repair group — you do not have to list it in `DisplayNames`.

- `<group>` can be either
  - the group's `Name` (as set in `BuildAndRepairSystemQueuingGroup.Name`, case-insensitive), **or**
  - the 1-based index into `BuildAndRepairSystemQueuingGroups` (`1` = first group).
- For blocks that expose multiple surfaces (cockpits, programmable blocks, fighter cockpits, etc.) append a surface index (or a comma-separated list of surface indices) with `@`:
  - `[BaR:<group>@<surfaceIndex>]` — attach a single surface.
  - `[BaR:<group>@<a>,<b>,<c>]` — attach several surfaces of the same block at once. Each becomes an independent entry (so each can render a different page — see the scoped Custom Data blocks below).

Examples:

| CustomName                              | Attaches to                                                 |
| --------------------------------------- | ------------------------------------------------------------ |
| `Hangar LCD [BaR:1]`                    | First group in the array                                     |
| `Status Panel [BaR:Hangar BaR Group1]`  | Group with `Name = "Hangar BaR Group1"`                      |
| `Cockpit [BaR:1@0]`                     | Surface 0 of the cockpit, attached to group 1                |
| `Bridge Cockpit [BaR:1@0,1,2]`          | Surfaces 0, 1 and 2 of the cockpit, all attached to group 1  |

Tagged panels take **priority** over `DisplayNames` — if a panel is both tagged and also listed in the script config, the tag wins and the panel is only added once.

Auto-discovered panels use the group's **first** `DisplayDefinition` as their base (or a built-in default — `Status`, 19 lines, 5 s — if the group has no `Displays` entry), and then apply any Custom Data overrides on top.

### Per-LCD override via Custom Data

Add one or more config blocks to the Custom Data of an LCD (or of the cockpit whose surfaces are referenced). Any setting inside overrides the value from the script config **for the matching surface(s)**. Anything outside the tag blocks is ignored, so you can keep other Custom Data alongside them.

There are two block flavours:

- `@BaR ... @/BaR` — **unscoped**. Applies to every attached surface of the block. On a plain LCD this is the normal per-LCD override. On a cockpit it acts as a cockpit-wide base that every attached surface inherits.
- `@BaR@<surfaceIndex> ... @/BaR` — **scoped**. Applies only when attached to that specific surface index on the block. Use these when you want different pages on different surfaces of the same cockpit.

#### Precedence

For each attached surface the final configuration is built in this order:

```
script DisplayDefinition
        │
        ▼
@BaR (unscoped)           — applied first, as a cockpit-wide base
        │
        ▼
@BaR@<surfaceIndex>        — applied on top, only for the matching surface
        │
        ▼
effective DisplayDefinition used at render time
```

Any key omitted at a given layer falls through to the layer above.

#### Keys

| Key                                   | Type                            | Description |
| ------------------------------------- | ------------------------------- | ----------- |
| `Kinds` (alias: `DisplayKinds`)       | comma/semicolon-separated list  | Pages to cycle through. See [Display Kinds](#display-kinds) for the full set. Accepts short aliases: `short`, `weld`, `grind`, `collect`, `missing`, `weldpriority`, `grindpriority`. Case-insensitive. |
| `MaxLines` (alias: `DisplayMaxLines`) | positive integer                | Line cap for list pages. |
| `SwitchTime` (alias: `DisplaySwitchTime`) | seconds (double)             | Page switch interval. `0` = no rotation (stick on the first kind). |
| `FontSize`                            | number or `auto`/`fit`          | Explicit font size (e.g. `1.2`), or `auto` to measure the rendered text once and pick the largest size that fits the surface in both width and height. Omit to keep whatever font size the panel currently has in the terminal. |

Omitted keys fall back to the script config. Blank lines and lines starting with `#` or `//` inside a block are ignored.

#### Font and alignment

Panels the script writes to are automatically switched to the built-in `Monospace` font so the column-aligned status lines render correctly (otherwise the label / value columns drift apart in a proportional font). If you set a font manually in the terminal it will be overwritten on the next refresh — use `FontSize=auto` (or a number) to control sizing, and let the script own the font.

#### Auto-seeded default Custom Data

When a block is tagged with `[BaR:...]` and its Custom Data is empty, the script writes a commented default template into it on the first init. The template contains `Kinds`, `MaxLines`, `SwitchTime`, a commented `FontSize=auto` hint, and a commented example of per-surface `@BaR@0` / `@BaR@1` scoped blocks for cockpits. This only happens when Custom Data is **empty** — the script never modifies Custom Data that already has content, so your own notes and configuration are preserved.

Open the LCD's terminal → Custom Data panel to see the seeded template, edit what you want, and recompile the Programmable Block (or wait for the 30-second reinit) for your edits to take effect.

#### Examples

**Minimal — show only missing items on one specific LCD**

```
@BaR
Kinds=MissingItems
@/BaR
```

**Rotating status + targets, slower switch**

```
@BaR
Kinds=ShortStatus,WeldTargets,GrindTargets
MaxLines=20
SwitchTime=6
@/BaR
```

**Static single page (no rotation)**

```
@BaR
Kinds=Status
SwitchTime=0
@/BaR
```

**Auto-fit font size to the surface**

```
@BaR
Kinds=Status,MissingItems
FontSize=auto
@/BaR
```

The script measures the text on the first update and scales the font so every line fits both the width and the height of the LCD. Useful for small cockpit screens and oversized wall panels alike.

**Cockpit — different page on each surface**

Rename the cockpit so three surfaces are attached at once:

```
Bridge Cockpit [BaR:1@0,1,2]
```

Then in the cockpit's Custom Data, give each surface its own scoped block. The unscoped `@BaR` block sets shared defaults; the scoped blocks override per-surface.

```
@BaR
MaxLines=15
@/BaR

@BaR@0
Kinds=Status
SwitchTime=0
@/BaR

@BaR@1
Kinds=WeldTargets,MissingItems
SwitchTime=5
@/BaR

@BaR@2
Kinds=GrindTargets
SwitchTime=0
@/BaR
```

Surface 0 shows only the Status page, surface 1 rotates between weld targets and missing items, and surface 2 shows only grind targets. All three inherit `MaxLines=15` from the unscoped block.

### Common setups

**Simplest possible — no script config at all:** leave `Displays` empty (or omit it) and just put `[BaR:1]` in the CustomName of every LCD you want to use. Each LCD shows the default `Status` page; add a Custom Data block to an individual LCD to override what it shows.

**Wall of LCDs, one page per screen, no script changes:** name each LCD with `[BaR:1]` and set its Custom Data to pin a single page, e.g.

```
@BaR
Kinds=WeldTargets
SwitchTime=0
@/BaR
```

**One cockpit as the mission dashboard:** use `[BaR:1@0,1,2,3]` in the cockpit name and give each surface its own `@BaR@<n>` scoped block in Custom Data.

### When changes take effect

Both the name tag and Custom Data are read at init and again on the periodic ~30 second reinit. To see changes immediately, recompile the Programmable Block.

---

## info-only Mode

To display status without queuing any components into assemblers, set the Programmable Block **argument** to `info-only` and rerun the script.

---

## Refresh

Block groups, tagged LCDs, and Custom Data are refreshed automatically every 30 seconds. To force an immediate refresh, open the Programmable Block editor and close it, or recompile the script.

---

## Credits

Based on the original script by **Dummy08** ([original Workshop page](https://steamcommunity.com/sharedfiles/filedetails/?id=867822734)). The original script is no longer maintained.
