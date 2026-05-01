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
| `AssignToSystemEnabled`               | `true`      | When `true`, target blocks for welding and grinding are assigned to individual Build and Repair systems so that multiple systems divide work efficiently instead of all targeting the same block. Set to `false` to disable the mechanism server-wide.                                                                                                                                                                                                                                                                              |
| `FriendlyDamageTimeoutTicks`          | `600000000` | How long a friendly-damage record is kept, in TimeSpan ticks (1 tick = 100 nanoseconds). Default is 60 seconds.                                                                                                                                                                                                                                                                                                      |
| `FriendlyDamageCleanupTicks`          | `100000000` | Interval between friendly-damage record cleanup passes, in TimeSpan ticks (1 tick = 100 nanoseconds). Default is 10 seconds.                                                                                                                                                                                                                                                                                        |

---

## System Limits

| Setting                            | Default                                | Description                                                                                                                                                                                                                                                                                                                                                                                                                           |
| ---------------------------------- | -------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `MaxSystemsPerTargetGrid`          | `20` (local/listen) / `10` (dedicated) | Maximum number of Build and Repair blocks that may work on the same target grid simultaneously. Prevents many systems piling onto one grid while nearby grids are left untouched. The default is **20** in local and listen-server games and **10** on dedicated servers. Setting this in the config file overrides whichever default applies. Accepted range: 1–100.                                                                |
| `DisableLimitSystemsPerTargetGrid` | `false`                                | Set to `true` to remove the per-grid system limit entirely.                                                                                                                                                                                                                                                                                                                                                                           |
| `EmptyGridRescanDelaySeconds`      | `20`                                   | After scanning a grid and finding no weld or grind targets, the system will skip that grid for this many seconds before scanning it again. This reduces CPU load from repeatedly iterating grids that have nothing for the system to do. Set to `0` to disable this behaviour. Sub-grid connections (connectors, pistons, rotors) are always traversed regardless of this setting, so a newly docked or spawned ship is never missed. |
| `AssignmentTtlSeconds`             | `8`                                    | How long a block assignment reservation is held (in seconds) before expiring. When a Build and Repair block claims a target, other systems will not attempt to work on the same block for this duration. Lower values allow faster recycling when systems disconnect; higher values prevent assignment "stealing" between systems. Accepted range: 2–30. Example: `/nanobars config set AssignmentTtlSeconds 5`                        |

---

## Performance Tuning

These settings control how the mod distributes its workload. They are useful for large servers with many Build and Repair blocks. All can be changed at runtime via `/nanobars config set <setting> <value>`.

| Setting                       | Default    | Description                                                                                                                                                                                                                                                                                                                                                              |
| ----------------------------- | ---------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| `StaggerGroupCount`           | `0` (auto) | Controls how many stagger groups are used to distribute Build and Repair updates across ticks. `0` means automatic — the mod picks the number based on how many blocks are active (1–4 blocks = 1 group, 5–10 = 2, 11+ = 3). Higher values spread the load more but increase the delay between each block's updates. Accepted range: 0–10.                                |
| `MaxGrindsPerTick`            | `0` (auto) | Maximum number of grind operations across all blocks per tick. `0` means automatic — the mod scales the budget based on block count (minimum 5, maximum 10). Setting a fixed value overrides the auto scaling. Higher values allow faster grinding but use more CPU per tick. Accepted range: 0–100. Example: `/nanobars config set MaxGrindsPerTick 8`                    |
| `MaxWeldsPerTick`             | `0` (auto) | Global cap on weld operations across all blocks per tick. `0` means automatic — the mod scales the budget based on block count. Setting a fixed value overrides the auto scaling. Higher values allow faster welding but risk frame spikes when the underlying engine weld call is heavy on a tick. Accepted range: 0–100.                                                |
| `BlockFailureCooldownSeconds` | `4`        | After a block fails to weld (missing components, projector exception, etc.) it is placed on a global fail-cooldown for this many seconds. Other Build and Repair blocks — and the same block on later ticks — skip the cooldowned block, so many systems don't all bounce off the same un-weldable target each tick. Set to `0` to disable. Accepted range: 0–30.          |

---

## Sound & Visuals

| Setting                  | Default | Description                                                                                                                                                      |
| ------------------------ | ------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `DisableTickingSound`    | `false` | Set to `true` to silence the ticking/unable sound for all blocks and all players on the server.                                                                  |
| `DisableParticleEffects` | `false` | Set to `true` to disable the flying nanobot trace animations globally. Individual blocks can also toggle this in the terminal unless this setting forces it off. |

---

## Debugging

| Setting     | Default | Description                                                                                                                                                                                                                                |
| ----------- | ------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| `DebugMode` | `false` | When `true`, the terminal custom info panel shows extra diagnostic information for all Build and Repair blocks (sources, push targets, cluster details, scan timings). Intended for testing and troubleshooting only — not for normal play. **Note:** On dedicated servers, some debug information may not be visible because the terminal panel is only rendered on connected clients. |

---

## Logging & Localisation

| Setting                        | Default | Description                                                                                                                                                                                                                                         |
| ------------------------------ | ------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `LogLevel`                     | `Error` | Controls how much detail is written to the game log. Accepted values: `Error`, `Warning`, `Info`, `Debug`. Use `Debug` only when troubleshooting — it generates a large amount of output.                                                           |
| `DisableLocalization`          | `false` | Set to `true` to disable mod localisation and fall back to English regardless of the player's language setting.                                                                                                                                     |
