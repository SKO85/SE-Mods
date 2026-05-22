---
layout: default
title: Chat Commands
parent: Build and Repair System
nav_order: 3
---

# Chat Commands

All chat commands start with `/nanobars` (or the alias `/nanoboars`). Most commands are **admin-only** — they require Admin, SpaceMaster, or Owner permissions. A few player-facing commands (such as `version`) are available to everyone.

On dedicated servers, commands are automatically forwarded to the server and responses are sent back to the client.

Type `/nanobars` or `/nanobars -help` in the chat to see the help overview in-game.

---

## Help

| Command | Description |
|---|---|
| `/nanobars` | Show the help overview |
| `/nanobars -help` | Same as above |

---

## Version

Available to **all players** (not just admins). Used to diagnose version mismatches between your client and the server you are connected to.

| Command | Description |
|---|---|
| `/nanobars version` | Show the client version and, on dedicated servers, also the server version |

On a dedicated server the response is delivered as **two separate chat messages** — the client version is shown locally first, and the server version arrives as a follow-up message once the server responds:

```
Nanobars: Client: v2.5.4 (build 260501.3)
```

```
Nanobars: Server: v2.5.4 (build 260501.3)
```

On a local (single-player or listen) game session only the client line is shown, since there is no separate server process.

The trailing `(build YYMMDD.N)` is the `BuildId` — versions that share the same `2.5.4` mod version may differ by `BuildId` between dev/preview/release builds. Include it when reporting issues so the exact build can be identified.

If the two lines differ, one side should update before reporting issues — version drift commonly causes subtle sync glitches or missing features that look like bugs.

---

## Configuration

Manage the `ModSettings.xml` server settings at runtime. Changes take effect immediately (no restart needed for most settings).

| Command | Description |
|---|---|
| `/nanobars config help` | Show config command syntax |
| `/nanobars config list` | List all settings with current values |
| `/nanobars config get <setting>` | Get a specific setting value |
| `/nanobars config set <setting> <value>` | Set a setting (takes effect immediately) |
| `/nanobars config save` | Save current settings to `ModSettings.xml` in the world folder |
| `/nanobars config save --global` | Save current settings to `ModSettings.xml` in the **PC-wide** mod storage folder (`%AppData%\SpaceEngineers\Storage\<mod>`). Acts as a default for every world on this machine. The world-storage file (if present) always wins on load. |
| `/nanobars config create` | Alias for `config save` (also accepts `--global`) |
| `/nanobars config reload` | Reload settings from `ModSettings.xml` |
| `/nanobars config reset` | Reset all settings to defaults |
| `/nanobars config delete` | Reset to defaults and delete the **world** `ModSettings.xml`. Add `--global` to delete only the PC-wide file, or `--all` to delete both. |

### Examples

```
/nanobars config set MaxSystemsPerTargetGrid 15
/nanobars config set DebugMode true
/nanobars config get AssignmentTtlSeconds
/nanobars config list
```

---

## Debug

Toggle debug diagnostics and the debug HUD overlay.

| Command | Description |
|---|---|
| `/nanobars debug` | Show current debug status |
| `/nanobars debug on` | Enable debug mode (server-wide) |
| `/nanobars debug off` | Disable debug mode (server-wide) |
| `/nanobars debug show` | Show the debug HUD overlay locally |
| `/nanobars debug hide` | Hide the debug HUD overlay locally |
| `/nanobars debug left` | Position the debug HUD on the left side and show it |
| `/nanobars debug right` | Position the debug HUD on the right side and show it |
| `/nanobars debug cluster-area` | Toggle a local wireframe overlay showing every cluster's per-member working areas plus a green pillar above the coordinator block. Up to 8 cluster colours (yellow, pink, green, purple, cyan, orange, red, white). Only enabled / functional / ready blocks count. Lists per-cluster sizes in chat when shown. Listen-server / single-player only. |
| `/nanobars debug targets` | Toggle a local wireframe overlay around every Build and Repair system's current weld and grind targets. Border = the cluster that discovered the target; solid red fill = the target is currently assigned to a system. Listen-server / single-player only. |

> **Note:** The debug HUD overlay (`show/hide/left/right`) requires the [TextHudAPI](https://steamcommunity.com/sharedfiles/filedetails/?id=758597413) (BuildInfo) mod to be installed. The `on/off` commands control the server-wide debug mode. The cluster-area and targets overlays are drawn directly through SE's transparent-box renderer and do not require TextHudAPI; they only render on the local client (not on a dedicated server).

---

## Profiling

Built-in performance profiler for diagnosing sim-speed issues. Produces per-method log files for analysis.

| Command | Description |
|---|---|
| `/nanobars profile help` | Show profiling command syntax |
| `/nanobars profile start [seconds] [minDurationMs] [sessionName]` | Start a profiling session |
| `/nanobars profile stop` | Stop the current profiling session and write logs |
| `/nanobars profile status` | Show whether profiling is running |
| `/nanobars profile summary` | Toggle the live profile summary HUD (top-right) |
| `/nanobars profile list` | List all stored profiling sessions |
| `/nanobars profile clear <sessionName\|all>` | Delete log files for a session or all sessions |
| `/nanobars profile minduration <ms>` | Set minimum duration threshold for log entries |

### Examples

```
/nanobars profile start 120
/nanobars profile start 60 2 my-test
/nanobars profile summary
/nanobars profile clear all
```

---

## Sim-Speed Override

Override the simulation speed value used by BaR for internal calculations. Useful for testing throttle behavior.

| Command | Description |
|---|---|
| `/nanobars sim <0.1-1.0>` | Override sim-speed to a fixed value |
| `/nanobars sim reset` | Remove the override and use actual sim-speed |

---

## Systems Management

Remotely list, count, enable, or disable BaR blocks on the server.

| Command | Description |
|---|---|
| `/nanobars systems help` | Show systems command syntax |
| `/nanobars systems list` | List all BaR blocks on the server |
| `/nanobars systems list --owner <player-name>` | List BaR blocks owned by a specific player |
| `/nanobars systems count` | Show BaR count per player and per faction |
| `/nanobars systems enable all` | Enable all BaR blocks on the server |
| `/nanobars systems disable all` | Disable all BaR blocks on the server |
| `/nanobars systems enable --grid <grid-name>` | Enable BaR blocks on a matching grid |
| `/nanobars systems disable --grid <grid-name>` | Disable BaR blocks on a matching grid |
| `/nanobars systems enable --owner <player-name>` | Enable BaR blocks owned by a matching player |
| `/nanobars systems disable --owner <player-name>` | Disable BaR blocks owned by a matching player |

Grid and player name matching is **case-insensitive** and supports **partial matches**.

### Examples

```
/nanobars systems list
/nanobars systems list --owner John
/nanobars systems count
/nanobars systems disable --grid "Pirate Base"
/nanobars systems enable --owner Steve
/nanobars systems disable all
```

---

## Mod Integrations

| Command | Description |
|---|---|
| `/nanobars mods` | Show status of mod integrations (TextHudAPI, DefenseShields) |
