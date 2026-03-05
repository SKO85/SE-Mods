---
layout: default
title: General Settings
parent: Configuration File
grand_parent: Build and Repair System
nav_order: 1
---

# General Settings

These settings apply globally across all Build and Repair blocks on the server. All values listed below are the defaults used when the setting is omitted from `ModSettings.xml`.

---

## Range & Offset

| Setting         | Default | Description                                                                                                 |
| --------------- | ------- | ----------------------------------------------------------------------------------------------------------- |
| `Range`         | `100`   | Maximum working range in metres. Blocks, projections, and floating objects outside this radius are ignored. |
| `MaximumOffset` | `200`   | Maximum distance in metres the work area can be offset from the block's position via the terminal.          |

---

## Power

Power values are in **MW**. These set the upper limit; actual draw scales with activity.

| Setting                                 | Default | Description                                   |
| --------------------------------------- | ------- | --------------------------------------------- |
| `MaximumRequiredElectricPowerStandby`   | `0.05`  | Maximum power draw when idle (50 kW).         |
| `MaximumRequiredElectricPowerTransport` | `0.1`   | Maximum power draw during transport (100 kW). |

---

## Background Processing

| Setting              | Default | Description                                                                                                                                    |
| -------------------- | ------- | ---------------------------------------------------------------------------------------------------------------------------------------------- |
| `MaxBackgroundTasks` | `4`     | Maximum number of background scan tasks that may run in parallel across all blocks on the server. Reduce to lower CPU impact on large servers. |

---

## Behaviour

| Setting                               | Default     | Description                                                                                                                                                                                                                                                                                                                                                                                                          |
| ------------------------------------- | ----------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `SafeZoneCheckEnabled`                | `true`      | When `true`, the system respects Safe Zone rules before welding, grinding, or building projections. Set to `false` to disable the check entirely.                                                                                                                                                                                                                                                                    |
| `ShieldCheckEnabled`                  | `true`      | When `true`, grids protected by an active Defence Shield are skipped. Set to `false` to disable the check.                                                                                                                                                                                                                                                                                                           |
| `DecreaseFactionReputationOnGrinding` | `true`      | When `true`, grinding grids belonging to other factions or NPCs causes a reputation penalty, matching the behaviour of manual grinding.                                                                                                                                                                                                                                                                              |
| `DeleteBotsWhenDead`                  | `true`      | When `true`, NPC bot bodies are deleted when they die, preventing them from cluttering the world.                                                                                                                                                                                                                                                                                                                    |
| `AssignToSystemEnabled`               | `true`      | When `true`, target blocks for welding and grinding are assigned to individual Build and Repair systems so that multiple systems divide work efficiently instead of all targeting the same block. For welding, if a block has the **Help Others** option enabled, the assignment is ignored and multiple systems may weld the same target block simultaneously. Set to `false` to disable the mechanism server-wide. |
| `FriendlyDamageTimeoutTicks`          | `600000000` | Number of game ticks before a friendly-damage record expires. At 60 ticks per second this is approximately 115 days.                                                                                                                                                                                                                                                                                                 |
| `FriendlyDamageCleanupTicks`          | `100000000` | Number of game ticks between friendly-damage record cleanup passes.                                                                                                                                                                                                                                                                                                                                                  |

---

## System Limits

| Setting                            | Default                                | Description                                                                                                                                                                                                                                                                                                                                    |
| ---------------------------------- | -------------------------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `MaxSystemsPerTargetGrid`          | `20` (local/listen) / `10` (dedicated) | Maximum number of Build and Repair blocks that may work on the same target grid simultaneously. Prevents many systems piling onto one grid while nearby grids are left untouched. The default is **20** in local and listen-server games and **10** on dedicated servers. Setting this in the config file overrides whichever default applies. |
| `DisableLimitSystemsPerTargetGrid` | `false`                                | Set to `true` to remove the per-grid system limit entirely.                                                                                                                                                                                                                                                                                    |
| `MaxInventoryFullPushAttempts`     | `100`                                  | Number of consecutive 5-second push cycles with a full inventory and no active welding before the system automatically disables itself. Set to `0` to disable this behaviour entirely. Prevents a block with a backed-up conveyor network from continuously attempting futile push operations.                                                 |

---

## Sound & Visuals

| Setting                  | Default | Description                                                                                                                                                      |
| ------------------------ | ------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `DisableTickingSound`    | `false` | Set to `true` to silence the ticking/unable sound for all blocks and all players on the server.                                                                  |
| `DisableParticleEffects` | `false` | Set to `true` to disable the flying nanobot trace animations globally. Individual blocks can also toggle this in the terminal unless this setting forces it off. |

---

## Logging & Localisation

| Setting               | Default | Description                                                                                                                                                                               |
| --------------------- | ------- | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `LogLevel`            | `Error` | Controls how much detail is written to the game log. Accepted values: `Error`, `Warning`, `Info`, `Debug`. Use `Debug` only when troubleshooting — it generates a large amount of output. |
| `DisableLocalization` | `false` | Set to `true` to disable mod localisation and fall back to English regardless of the player's language setting.                                                                           |
