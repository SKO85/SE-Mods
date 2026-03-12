---
layout: default
title: Build and Repair System
nav_order: 2
has_children: true
---

# Nanobot Build and Repair System

The SKO Nanobot Build and Repair System automatically welds, repairs, grinds, and collects floating objects within its range. It replaces tedious manual work with an automated system that is fully configurable per block through the terminal, and tuned server-wide through a `ModSettings.xml` configuration file.

Based on the original mod by [Dummy08](https://steamcommunity.com/sharedfiles/filedetails/?id=857053359), which has been inactive for some time. This maintained version includes several fixes, improvements, and ongoing updates.

---

## Steam Workshop

| Version                                   | Description                                                         | Link                                                                           |
| ----------------------------------------- | ------------------------------------------------------------------- | ------------------------------------------------------------------------------ |
| (Maintained) - Nerfed Version             | Rebalanced build cost and weight — requires more resources to build | [Subscribe](https://steamcommunity.com/sharedfiles/filedetails/?id=2111073562) |
| (Maintained) - Original Resources Version | Same functionality and fixes, uses the original build components    | [Subscribe](https://steamcommunity.com/sharedfiles/filedetails/?id=3099489876) |

Both versions are functionally identical. They only differ in the resources required to build the block. You only need one — they cannot be used together in the same world.

---

## Features

- **Auto Weld** — repairs and completes projected or damaged blocks within range
- **Auto Grind** — grinds enemy, neutral, or color-tagged blocks automatically
- **Auto Collect** — picks up floating objects and delivers them to connected inventories
- **Multigrid Projection** — welds projected grids using the multigrid-projection plugin
- **Creative Mode** — welds and builds blocks without requiring components in inventory
- **Priority Lists** — configure which block types to weld or grind first, with Enable All / Disable All buttons
- **Ignore Priority Order** — optionally bypass the priority list and target the nearest block instead
- **Weld Mode** — choose how far blocks are welded: Weld to Full (100%), Weld to Functional Only (stops at functional threshold), or Weld to Skeleton (places projected blocks only, never repairs)
- **Work Modes** — choose between Weld Before Grind, Grind Before Weld, Grind If Stuck, Weld Only, or Grind Only; in Weld Before Grind and Grind Before Weld modes, if no actionable targets exist for the primary mode the system now falls through to the secondary mode instead of going idle
- **Cluster Scan Coordinator** — blocks sharing the same working area elect a single coordinator to scan for targets, eliminating redundant scans across the cluster; the role is automatically re-elected if the coordinator is disabled or removed
- **System Limit** — limit how many Build and Repair blocks may work on the same grid simultaneously
- **Safe Zone Support** — respects Safe Zone rules before taking any action
- **Shields Support** — skips targets protected by the Shields mod
- **DLC Aware** — skips projected blocks requiring DLC the owner does not have
- **Plugin Aware** — skips grids protected by server plugins (e.g. `!protect` from ALE PCU Transferrer)
- **Sound & Effects** — ticking sound and flying nanobot particle effects toggleable per block and server-wide
- **Reset All Settings** — restores all per-block settings to their defaults from the terminal
- **Script Support** — exposes a scripting interface for Programmable Block integration

---

## Fixes & Adjustments

### Safe Zone & Grinding

Grinding blocks or grids protected by Safe Zones is now properly restricted:

- Grinding inside a Safe Zone is only allowed if the **Allow Grinding** option is enabled on the Safe Zone block.
- If the grid inside the Safe Zone is owned by another player or NPC, grinding is blocked even if **Allow Grinding** is enabled.
- If the grid is owned by you, grinding is allowed when the Safe Zone permits it.

### Defence Shields & Grinding

Full support for the [Defence Shields](https://steamcommunity.com/workshop/filedetails/?id=3154379105) mod:

- Grids protected by an active shield cannot be ground down by other players.
- If your own shields are active, you cannot grind grids outside your shield. This prevents abusing shields to safely grind NPC, faction, or player grids. Disable your shields first to grind external targets.

### Other Fixes & Changes

- Reputation is now correctly reduced when grinding NPCs and other factions.
- Welding and grinding speed increased slightly over the original mod.
- Power consumption increased from the original mod's very low values to more balanced levels.
- Build resources increased to make the block appropriately expensive to obtain.
- Grids in preview mode are skipped until fully placed.
- Indestructible and immune grids are correctly excluded from grinding.
- Components can be pulled from Sorters, Connectors, and Grinder blocks.
- Power is no longer drained when welding a projected block is not possible.
- Block settings now persist correctly after a server restart or relog.
- DLC ownership is checked before attempting to build a projected block.
- Multiplayer network updates reduced for better performance in large sessions.

---

## Power Consumption

_Applies to the (Maintained) - Nerfed Version. The Original Resources Version uses the original mod values._

| State              | Power  |
| ------------------ | ------ |
| Off                | 0 kW   |
| Idle (Standby)     | 50 kW  |
| Transport          | 100 kW |
| Welding / Grinding | 200 kW |

---

## Build Requirements

_Applies to the (Maintained) - Nerfed Version only. See the [Original Resources Version](https://steamcommunity.com/sharedfiles/filedetails/?id=3099489876) for the original build components._

### Large Grid

| Component              | Amount |
| ---------------------- | ------ |
| Steel Plate            | 400    |
| Construction Component | 1320   |
| Interior Plate         | 250    |
| Small Steel Tube       | 600    |
| Large Steel Tube       | 100    |
| Computer               | 200    |
| Motor                  | 400    |
| Superconductor         | 120    |

### Small Grid

| Component              | Amount |
| ---------------------- | ------ |
| Steel Plate            | 150    |
| Construction Component | 500    |
| Interior Plate         | 80     |
| Small Steel Tube       | 200    |
| Large Steel Tube       | 20     |
| Computer               | 100    |
| Motor                  | 120    |
| Superconductor         | 40     |

---

## Documentation

- [Configuration File](Config/) — all `ModSettings.xml` settings with defaults and descriptions
- [Scripting API](Scripting/) — terminal properties available to Programmable Block scripts
- [FAQ](FAQ/) — common questions and troubleshooting steps
- [Release Notes](Release-Notes/) — full version history

---

## Support

- **Discord:** [Join the server](https://discord.gg/5XkQW5tdQM) — ask questions in **#help-topics**
- **Bug reports:** [Open an issue on GitHub](https://github.com/SKO85/SE-Mods/issues)
- **Donations:** [Donate via PayPal](https://www.paypal.com/paypalme/SKO85GAMING)
