---
layout: default
title: Welder Settings
parent: Configuration File
grand_parent: Build and Repair System
nav_order: 2
---

# Welder Settings

These settings control the behaviour of the welder/grinder subsystem. They are nested inside the `<Welder>` element in `ModSettings.xml`.

Many settings come in pairs: a `Default` value that sets what players see in the terminal, and a `Fixed` flag that locks the setting so players cannot change it.

---

## Power

Power values are in **MW**. These set the upper limit; actual draw scales with activity.

| Setting                                | Default | Description                                 |
| -------------------------------------- | ------- | ------------------------------------------- |
| `MaximumRequiredElectricPowerWelding`  | `0.2`   | Maximum power draw while welding (200 kW).  |
| `MaximumRequiredElectricPowerGrinding` | `0.2`   | Maximum power draw while grinding (200 kW). |

---

## Speed Multipliers

| Setting              | Default | Description                                                                   |
| -------------------- | ------- | ----------------------------------------------------------------------------- |
| `WeldingMultiplier`  | `1`     | Multiplier applied to welding speed per tick. `2` doubles the per-tick amount, `0.5` halves it.  |
| `GrindingMultiplier` | `1`     | Multiplier applied to grinding speed per tick. `2` doubles the per-tick amount, `0.5` halves it. |

---

## Update Speed

| Setting     | Default | Description                                                                                                                                                                                                                                                                                                                                                                           |
| ----------- | ------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `WorkSpeed` | `1`     | Controls how often Build and Repair operations run, from `1` (slowest) to `10` (fastest). At `1`, operations fire roughly every 1.67 seconds. At `10`, they fire roughly every 0.17 seconds — 10x more frequently. This setting is independent of `WeldingMultiplier` and `GrindingMultiplier`, which control how much work is done per tick. Accepted range: 1–10. |

### Speed examples

The total effective speed is `WorkSpeed × Multiplier`. Some common combinations:

| WeldingMultiplier | WorkSpeed | Per-tick amount | Updates/sec | Relative speed vs defaults |
|---|---|---|---|---|
| 1 | 1 (default) | 1x | ~0.6 | 1x (baseline) |
| 2 | 1 | 2x | ~0.6 | 2x |
| 1 | 5 | 1x | ~3 | 5x |
| 2 | 5 | 2x | ~3 | 10x |
| 10 | 10 | 10x | ~6 | 100x |

{: .note }
> In previous versions (before v2.5.0), setting `WeldingMultiplier` or `GrindingMultiplier` above 10 also changed the update frequency. This is no longer the case — multipliers now only affect the per-tick amount. Use `WorkSpeed` to control update frequency independently. Existing worlds with a multiplier above 10 will automatically have `WorkSpeed` set to `10` on first load.

---

## Search Modes

Search mode controls which targets the system scans for.

| Setting              | Default             | Description                                                                                                           |
| -------------------- | ------------------- | --------------------------------------------------------------------------------------------------------------------- |
| `AllowedSearchModes` | `Grids BoundingBox` | Space-separated list of search modes available to players in the terminal. Remove a value to hide that mode entirely. |
| `SearchModeDefault`  | `Grids`             | The search mode applied when a block is first placed or reset. Accepted values: `Grids`, `BoundingBox`.               |

---

## Weld Mode

The weld mode controls how far the system welds blocks. Players can change this per block in the terminal dropdown.

| Mode | Behaviour |
| --- | --- |
| `WeldFull` (default) | Welds blocks to 100% integrity. |
| `WeldFunctional` | Welds blocks until they become functional. Saves components and time. |
| `WeldSkeleton` | Only places projected blocks (first component). Does not weld or repair existing blocks. |

There is no server-side setting to lock or override the weld mode — it is always controlled per block in the terminal.

---

## Build (Weld Projections)

| Setting             | Default | Description                                                                                     |
| ------------------- | ------- | ----------------------------------------------------------------------------------------------- |
| `AllowBuildDefault` | `true`  | Default state of the Build (weld projected blocks) option in the terminal.                      |
| `AllowBuildFixed`   | `false` | Set to `true` to lock the Build option server-wide. Players cannot change it from the terminal. |

---

## Work Modes

Work mode determines the order in which the system tackles welding and grinding.

| Setting            | Default                                                                  | Description                                                                                                                                                         |
| ------------------ | ------------------------------------------------------------------------ | ------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `AllowedWorkModes` | `WeldBeforeGrind GrindBeforeWeld GrindIfWeldGetStuck WeldOnly GrindOnly` | Space-separated list of work modes available to players. Remove a value to hide that mode from the terminal.                                                        |
| `WorkModeDefault`  | `WeldBeforeGrind`                                                        | The work mode applied when a block is first placed or reset. Accepted values: `WeldBeforeGrind`, `GrindBeforeWeld`, `GrindIfWeldGetStuck`, `WeldOnly`, `GrindOnly`. |

---

## Push & Collect Defaults

These control whether collected items are immediately pushed to connected inventories.

| Setting                           | Default | Description                                                                                                                                         |
| --------------------------------- | ------- | --------------------------------------------------------------------------------------------------------------------------------------------------- |
| `PushIngotOreImmediatelyDefault`  | `true`  | Default state of the Push Ingots/Ore Immediately option.                                                                                            |
| `PushIngotOreImmediatelyFixed`    | `false` | Set to `true` to lock this option server-wide.                                                                                                      |
| `PushComponentImmediatelyDefault` | `true`  | Default state of the Push Components Immediately option.                                                                                            |
| `PushComponentImmediatelyFixed`   | `false` | Set to `true` to lock this option server-wide.                                                                                                      |
| `PushItemsImmediatelyDefault`     | `true`  | Default state of the Push Items Immediately option.                                                                                                 |
| `PushItemsImmediatelyFixed`       | `false` | Set to `true` to lock this option server-wide.                                                                                                      |
| `CollectIfIdleDefault`            | `false` | Default state of the Collect If Idle option. When `true`, the block collects floating objects even when there is no welding or grinding work to do. |
| `CollectIfIdleFixed`              | `false` | Set to `true` to lock the Collect If Idle option server-wide.                                                                                       |

---

## Grind Janitor

The Grind Janitor automatically grinds blocks belonging to specific ownership categories.

| Setting                        | Default                       | Description                                                                                                                         |
| ------------------------------ | ----------------------------- | ----------------------------------------------------------------------------------------------------------------------------------- |
| `UseGrindJanitorDefault`       | `NoOwnership Enemies`         | Default ownership categories the Grind Janitor targets. Space-separated list. Accepted values: `NoOwnership`, `Neutral`, `Enemies`. |
| `GrindJanitorOptionsDefault`   | _(empty)_                     | Additional Grind Janitor option flags applied by default.                                                                           |
| `AllowedGrindJanitorRelations` | `NoOwnership Neutral Enemies` | Space-separated list of ownership categories players may enable for the Grind Janitor. Remove a value to hide it from the terminal. |
| `UseGrindJanitorFixed`         | `false`                       | Set to `true` to lock all Grind Janitor settings server-wide.                                                                       |

---

## Color Settings

Colors are specified as HSV values: **Hue** (0–360), **Saturation** (0–100), **Value** (0–100).

### Ignore Color

Blocks painted with this color are skipped during welding (not targeted).

| Setting                 | Default        | Description                                                    |
| ----------------------- | -------------- | -------------------------------------------------------------- |
| `UseIgnoreColorDefault` | `true`         | Default state of the Use Ignore Color option.                  |
| `UseIgnoreColorFixed`   | `false`        | Set to `true` to lock the Use Ignore Color option server-wide. |
| `IgnoreColorDefault`    | `321, 100, 51` | Default ignore color as HSV values (H, S, V).                  |

### Grind Color

Blocks painted with this color are treated as grind targets.

| Setting                | Default        | Description                                                   |
| ---------------------- | -------------- | ------------------------------------------------------------- |
| `UseGrindColorDefault` | `true`         | Default state of the Use Grind Color option.                  |
| `UseGrindColorFixed`   | `false`        | Set to `true` to lock the Use Grind Color option server-wide. |
| `GrindColorDefault`    | `321, 100, 50` | Default grind color as HSV values (H, S, V).                  |

---

## Sound

| Setting              | Default | Description                                                           |
| -------------------- | ------- | --------------------------------------------------------------------- |
| `SoundVolumeDefault` | `1`     | Default sound volume for the block (0.0 = silent, 1.0 = full volume). |
| `SoundVolumeFixed`   | `false` | Set to `true` to lock the sound volume setting server-wide.           |

---

## Visual Effects

`AllowedEffects` controls which effect types are available. Remove an entry to disable that effect globally — players will not be able to enable it from the terminal.

| Setting          | Default                                                                                                 | Description                                             |
| ---------------- | ------------------------------------------------------------------------------------------------------- | ------------------------------------------------------- |
| `AllowedEffects` | `WeldingVisualEffect WeldingSoundEffect GrindingVisualEffect GrindingSoundEffect TransportVisualEffect` | Space-separated list of effects the system may produce. |

---

## Lock Settings

These `Fixed` flags lock specific terminal options server-wide. When set to `true`, players cannot change the corresponding setting from the block's terminal.

| Setting                | Default | Locks                     |
| ---------------------- | ------- | ------------------------- |
| `ShowAreaFixed`        | `false` | Show Work Area option     |
| `AreaSizeFixed`        | `false` | Work Area size slider     |
| `AreaOffsetFixed`      | `false` | Work Area offset controls |
| `PriorityFixed`        | `false` | Weld/grind priority list  |
| `CollectPriorityFixed` | `false` | Collect priority list     |
| `ScriptControlFixed`   | `false` | Script control option     |
