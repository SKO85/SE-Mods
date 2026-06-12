---
layout: default
title: 'Release Notes – v2.5.0'
parent: Release Notes
grand_parent: Build and Repair System
nav_order: 5
---

# Release Notes – v2.5.0

- Status: **Released** — April 2026
- Notes: Major performance and stability release. Servers with many active BaRs run dramatically smoother, plus a long list of bug fixes, new weld modes, and live admin configuration via chat.

> **Note:** This release also includes everything from [v2.4.5](release_notes_v2_4_5), which was not separately published.

---

## Performance

The headline of this release: servers with many Build and Repair blocks run much, much smoother.

- **Shared scanning.** BaRs covering the same area now let one of them scan for targets and share the results with the rest, instead of every block scanning on its own. With 10 BaRs in one spot, that's roughly 80% less scanning work. If the scanning block is turned off or removed, another takes over automatically.
- **Grinding no longer spikes the server.** Grinding many blocks on large grids could cause lag spikes of up to 379 ms. That's down to roughly 12 ms.
- **Fair scanning across grids.** One huge grid can no longer hog all the scanning time and leave BaRs aimed at other grids sitting idle. Every grid in range gets its share.
- **Empty grids are skipped.** Grids with nothing to weld or grind are left alone for 20 seconds (configurable) before being checked again. Newly docked or spawned ships are still picked up right away.
- **Automatic slow-down under load.** When the server's sim speed drops below 1.0, BaRs automatically scan and work less to help the server recover, then resume at full speed.
- **Roughly 80% less network traffic.** The mod now only sends clients what actually changed, and slows its updates further while a block's status is stable.
- **Idle blocks cost almost nothing.** BaRs with no targets and nothing to transport now skip all their internal processing.
- **Many smaller optimisations** for large servers (100+ blocks), plus the terminal info panel now refreshes at most every 2 seconds.

**Heads up:** target scanning now runs roughly every 10 seconds. When you first switch on a projector there can be a short delay (up to about 10 seconds) before BaRs notice the new blocks and start welding — after that it ramps up quickly. Faster projection detection is planned.

**For admins:** the per-grid system limit `MaxSystemsPerTargetGrid` now defaults to **20** in local and listen-server games and **10** on dedicated servers; setting it in `ModSettings.xml` overrides the default. The empty-grid skip is `EmptyGridRescanDelaySeconds` (default 20).

---

## New Features

### Weld mode: Full / Functional / Skeleton

The old "Weld to functional only" checkbox is now a three-option dropdown:

| Mode | Behaviour |
| --- | --- |
| **Weld to full** (default) | Welds blocks to 100% integrity. Same as before. |
| **Weld to functional** | Welds blocks until they work (lights turn on, doors open, thrusters fire), then stops — saves components and time. |
| **Skeleton only** | Only places projected blocks (first component); doesn't weld or repair anything. The fastest way to lay out a large structure from a projection — place everything first, then switch to Full or Functional. |

Existing saves that used "Weld to functional only" automatically map to "Weld to functional".

### More inventory sources and push targets

The following blocks on the conveyor network can now be used to pull components from (for welding) and push items into (after grinding):

- Cargo Containers, Connectors, Conveyor Sorters
- Assemblers, Refineries
- Ship Grinders, Ship Welders (excluding other BaR blocks)
- Cryo Chambers

**New in v2.5.0:** Cryo Chambers and Refineries. Blocks on connected grids (connectors, pistons, rotors) are included too. Sources and push targets are rescanned every 30 seconds.

### No more going idle in mixed work modes

In **Weld Before Grind** and **Grind Before Weld** modes, if there's nothing left for the primary task the BaR now switches to the secondary one instead of going idle. If any valid target exists, the block finds it.

### "Help Others" option removed

The native **Help Others** checkbox is gone from the BaR terminal. It never did anything useful for the mod, and could interfere with how work is shared between multiple systems.

### Context-sensitive help

`/nanobars` now shows the mod version and useful links (GitHub, Wiki, FAQ, Discord) to everyone; admins additionally see the full command reference.

---

## For Admins

### Live configuration via chat

Most settings can now be viewed and changed at runtime, no restart needed:

| Command | Description |
| --- | --- |
| `/nanobars config list` | List all settings with their current values. |
| `/nanobars config get <setting>` | Get a specific setting's current value. |
| `/nanobars config set <setting> <value>` | Change a setting immediately. |
| `/nanobars config save` | Save current settings to `ModSettings.xml`. |
| `/nanobars config create` | Alias for `config save` (creates the file if it doesn't exist). |
| `/nanobars config reload` | Reload settings from `ModSettings.xml`. |
| `/nanobars config reset` | Reset all settings to defaults. |
| `/nanobars config delete` | Reset all settings to defaults and delete `ModSettings.xml`. |

**Examples:**
```
/nanobars config set MaxSystemsPerTargetGrid 15
/nanobars config set StaggerGroupCount 3
/nanobars config set MaxGrindsPerTick 8
/nanobars config set AssignmentTtlSeconds 5
/nanobars config save
```

### Work Speed setting

A new `WorkSpeed` setting (1–10, default 1) controls how often BaRs do work, separately from the weld/grind multipliers. Previously the only way to speed up the work rhythm was pushing a multiplier above 10, which caused a sudden 10x jump with no middle ground. Now multipliers control how much work is done per operation, and `WorkSpeed` controls how often operations run (transport animations speed up to match):

| WorkSpeed | Update interval | Relative frequency |
|---|---|---|
| 1 (default) | ~1.67 seconds | 1x |
| 5 | ~0.33 seconds | 5x |
| 10 | ~0.17 seconds | 10x |

Set via `ModSettings.xml` (`<WorkSpeed>5</WorkSpeed>` inside `<Welder>`) or `/nanobars config set WorkSpeed 5`. Existing worlds with a multiplier above 10 automatically get `WorkSpeed` 10 on first load, preserving their previous speed.

### Debug mode and HUD overlay

A new `DebugMode` setting shows extra diagnostic info in the terminal panel (local and listen-server games only). With debug mode or profiling active, admins also get a real-time HUD overlay with server-wide BaR statistics — system activity, targets, performance, and more. The overlay needs the [BuildInfo](https://steamcommunity.com/sharedfiles/filedetails/?id=514062285) mod installed (optional — without it the mod still works, the overlay just won't appear). Debug mode is server-wide; the HUD is a per-admin local toggle, and debug data is only sent to admins.

| Command | Description |
| --- | --- |
| `/nanobars debug on` (or `true`) | Enable debug mode (server-wide). |
| `/nanobars debug off` (or `false`) | Disable debug mode (server-wide). |
| `/nanobars debug show` | Show the debug HUD on your screen. |
| `/nanobars debug hide` | Hide the debug HUD on your screen. |
| `/nanobars debug left` | Set HUD position to left side and show. |
| `/nanobars debug right` | Set HUD position to right side and show. |
| `/nanobars debug` | Show current debug status. |

Don't leave `DebugMode` enabled in normal play.

### Built-in profiler

Admins can measure the mod's performance impact. Results are written to log files in the mod's storage folder; multiple named sessions can be kept for comparison.

| Command | Description |
| --- | --- |
| `/nanobars profile start [seconds] [minDurationMs] [sessionName]` | Start a profiling session. |
| `/nanobars profile stop` | Stop the session and write the summary. |
| `/nanobars profile status` | Show whether profiling is active and current settings. |
| `/nanobars profile summary` | Toggle the live summary HUD panel (top-right). |
| `/nanobars profile list` | List all stored sessions. |
| `/nanobars profile clear <sessionName\|all>` | Delete files for one session or all sessions. |
| `/nanobars profile minduration <ms>` | Set the minimum duration threshold for logging. |
| `/nanobars profile help` | Show profiling command help. |

### Sim-speed override

Temporarily override the reported simulation speed for testing:

| Command | Description |
| --- | --- |
| `/nanobars sim` | Show current override status. |
| `/nanobars sim <0.1–1.0>` | Force a specific sim-speed value. |
| `/nanobars sim reset` | Return to the real sim-speed. |

### Mod integration status

| Command | Description |
| --- | --- |
| `/nanobars mods` | Show status of the TextHudAPI (BuildInfo) and DefenseShields integrations. |

### Remote system management

List, count, enable and disable BaR blocks across the whole server. Grid and player filters are case-insensitive and support partial matches.

| Command | Description |
| --- | --- |
| `/nanobars systems list` | List all BaR blocks on the server. |
| `/nanobars systems list --owner <player>` | List BaR blocks owned by a player. |
| `/nanobars systems count` | Show BaR count per player and faction. |
| `/nanobars systems enable all` | Enable all BaR blocks. |
| `/nanobars systems disable all` | Disable all BaR blocks. |
| `/nanobars systems enable --grid <name>` | Enable BaR blocks on a matching grid. |
| `/nanobars systems disable --grid <name>` | Disable BaR blocks on a matching grid. |
| `/nanobars systems enable --owner <player>` | Enable BaR blocks owned by a matching player. |
| `/nanobars systems disable --owner <player>` | Disable BaR blocks owned by a matching player. |

---

## Bug Fixes

- **High multipliers broke saving and more.** Setting `WeldingMultiplier` or `GrindingMultiplier` above 10 could stop block settings from saving on world reload, freeze the power display, and slowly waste memory. Resolved by the new `WorkSpeed` setting, which replaced that whole speed-up path.
- **BaRs going idle when other grids had work.** A block would sit idle once all targets on its current grid hit the per-grid system limit, instead of moving on. It now switches to the next grid right away.
- **Endless item ping-pong between BaRs.** Two BaR blocks could push items back and forth into each other's inventories forever, filling both up and doing no actual work. BaR inventories are no longer push targets.
- **Sounds at the wrong spot.** Welding and grinding sounds were sometimes heard at your position instead of at the BaR block.
- **False Safe Zone warnings on placement.** Freshly placed BaRs could briefly show "Safe Zone detected" or "Shield detected" before the first real check finished.
- **Wrong status while collecting.** Collecting floating objects showed "Grinding (Transporting)" instead of "Collecting (Transporting)".
- **Power display corrected.** The terminal claimed a maximum draw of 350 kW; the real maximum is 200 kW.
- **False "Missing Components" on startup.** Right after placing a block or loading a world, the BaR could start working before its first scan finished, producing bogus "Missing Components" messages. It now waits for the scan.
- **Grinding/collecting with a full inventory.** The BaR could start grinding or collecting with nowhere to put the results. It now checks its inventory first.
- **Stuck in mixed work modes at the grid limit.** In Weld Before Grind / Grind Before Weld, the BaR could get stuck when all primary targets were taken, instead of switching to the secondary task. Fixed.
- **Grind priority order not respected.** Grind targets now actually follow the configured priority order.
- **Collecting outside the working area.** Floating objects slightly outside the configured work area could be collected. Only objects inside the area are collected now.
- **Safe Zone protection could fail.** If a Safe Zone wasn't fully loaded when checked, a protected block could be ground down. The BaR now assumes "protected" when in doubt.
- **Wrong block priority for up to 5 minutes.** The priority system could classify a block wrongly for a while. Lookups are always correct now.
- **Inconsistent distance sorting.** Distance-based target ordering could occasionally come out wrong. Fixed.
- **Constant pushing against full containers.** When every push target was full, the BaR kept retrying every cycle. It now backs off and retries less often.
- **Sort toggle clearing all options.** Deselecting a sort option (Nearest, Farthest, Smallest Grid) in the terminal could clear all of them instead of cycling.
- **Grids straddling a Safe Zone boundary.** When some BaRs on a grid were inside a Safe Zone and others outside, the ones outside could be blocked from finding targets. Each side now scans independently.
- **Per-grid limit could be exceeded.** With a limit of 20, 30+ BaRs could end up welding the same grid. The limit is now enforced exactly, and blocks over the limit move to another grid.
- **Targets stayed "reserved" too long.** In two situations a BaR could keep a finished or lost target reserved for up to 8 seconds, blocking other BaRs from picking it up. Reservations are now released immediately.
- **Weld search giving up too early.** On grids with many already-completed blocks, the search for the next weld target could give up before reaching real work, leaving the BaR idle. Completed blocks no longer count against the search.
- **New small grids not welded until powered.** Blocks placed on a brand-new grid (e.g. armor plus a landing gear, no battery yet) weren't welded until a power block was added. The BaR now also checks who placed the blocks — if it's the BaR's owner or a faction member, welding proceeds.
- **For admins:** the debug HUD's "SafeZone Blocked" counter now also counts systems blocked from building projections, not just from welding/grinding.

---

## Stability

- Fixed several rare crashes and intermittent dropped data on busy servers.
- Removed Safe Zones are now always cleaned up properly (previously they could slowly waste memory).
- Better protection when blocks, grids, or inventories are removed mid-operation — prevents rare crashes.
- Errors that were previously swallowed silently are now logged, making issues easier to diagnose and report.
- Fixed a "file in use" log error after Torch hot-reloads the mod.
- Saved block settings from older versions are migrated automatically (the old "Weld to functional only" checkbox maps to the new weld mode dropdown).
