---
layout: default
title: 'Release Notes – v2.5.2'
parent: Release Notes
grand_parent: Build and Repair System
nav_order: 3
---

# Release Notes – v2.5.2

- Status: **Released** — April 2026
- Notes: Bug fix and quality-of-life release. Fixes BaRs going idle when spread out across a large base, adds a `/nanobars version` command so anyone can check that client and server match, and gives the companion Programmable Block script a big usability upgrade — attach LCDs just by renaming them, customise each panel via Custom Data, and drive individual cockpit surfaces.

---

## Bug Fixes

### BaRs spread across a large base stopped welding

With several BaRs placed on the same large grid (an asteroid base, for example) with their working areas spread roughly 150 meters apart, only the BaRs near one of them would actually work — the rest sat idle with empty target lists, and the companion script's LCD showed `NULL` as the current welding target even with plenty of blocks to build nearby.

The shared scan that nearby BaRs use to save performance was keeping only the blocks closest to one system and throwing the rest away, so BaRs on the far side of the base got nothing. Fixed: every BaR in the group now receives targets in its own working area. The companion script displays real block names again on all systems — no script changes needed. Solo BaRs and small setups behave exactly as before.

### Auto-push stopped working when the BaR was idle

With any of the **Push ... Immediately** options enabled, leftover ore, ingots or components from the last grind could sit in the welder inventory forever once the BaR ran out of targets — they only moved to cargo when the BaR picked up new work. This was a side effect of an idle-state optimisation added in v2.5.0. The BaR now pushes leftovers out on its usual 5–10 second rhythm even while idle. BaRs without auto-push enabled are unaffected.

### Smallest-grid sort now picks the nearest equal-size grid first

With **Grind Smallest Grid First** enabled and two grids of the same block count in range (common with debris fields, drone swarms, or identical prefab stations), the BaR could fly off to a far grid while an identical one sat right next door — the tiebreaker was effectively arbitrary. Same-size grids are now taken nearest first. As introduced in v2.5.1, each grid is still finished completely before the BaR moves to the next one.

---

## New Features

### `/nanobars version` chat command

A new command helps diagnose version mismatches between client and server:

| Command | Description |
|---|---|
| `/nanobars version` | Show the mod version on your client, and — on dedicated servers — the server's version too |

On a **dedicated server** it prints two lines:

```
BaR Mod Client: v2.5.2
BaR Mod Server: v2.5.2
```

On a **local (single-player or listen) game** only the client line is shown. If the two lines differ, one side needs to update before reporting issues — version drift often causes glitches that look like bugs.

Unlike most `/nanobars` commands, `version` is **available to all players**, not just admins.

### Companion PB script: per-LCD configuration

The companion Programmable Block script can now be set up entirely from the terminal — no more editing the script's group array every time you add or change an LCD. Everything you don't configure falls back to the script defaults.

#### Attach displays by renaming them

Rename an LCD, cockpit, or any other text surface so its name contains a `[BaR:<group>]` tag, and the script picks it up automatically on the next reinit:

| CustomName | Attaches to |
|---|---|
| `Hangar LCD [BaR:1]` | Group 1 (by position in the script's group list) |
| `Status Panel [BaR:Hangar BaR Group1]` | Group with that name (case-insensitive) |
| `Cockpit [BaR:1@0]` | Surface 0 of the cockpit, group 1 |
| `Bridge Cockpit [BaR:1@0,1,2]` | Surfaces 0, 1 and 2 of the cockpit, each independent |

Tagged panels take priority over panels listed in the script config — if a panel is both, the tag wins and it's only added once.

#### Per-LCD Custom Data overrides

Each LCD can carry a small config block in its Custom Data that overrides the script settings for that panel only:

```
@BaR
Kinds=Status,WeldTargets,MissingItems
MaxLines=15
SwitchTime=4
FontSize=auto
@/BaR
```

| Key | Description |
|---|---|
| `Kinds` | Comma-separated list of pages to cycle through (`Status`, `ShortStatus`, `WeldTargets`, `GrindTargets`, `CollectTargets`, `MissingItems`, `BlockWeldPriority`, `BlockGrindPriority`). Short aliases (`weld`, `grind`, `missing`, ...) accepted. |
| `MaxLines` | Line cap for list pages. |
| `SwitchTime` | Seconds between page switches. `0` = no rotation. |
| `FontSize` | Explicit size (e.g. `1.2`) or `auto`/`fit` to pick the largest size that fits the panel. |

Anything outside the `@BaR` … `@/BaR` block is ignored, so your own notes in Custom Data are safe.

#### Different pages on each cockpit screen

Cockpits share one Custom Data across all their screens. Scoped blocks let each surface show something different — `@BaR` sets the cockpit-wide base, `@BaR@0`, `@BaR@1`, ... override individual surfaces:

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
@/BaR
```

#### Other script improvements

- **Template on first use:** if a tagged panel's Custom Data is empty, the script writes a commented `@BaR` template into it so you can see every available option. Non-empty Custom Data is never touched.
- **Columns line up now:** panels are switched to the built-in `Monospace` font, fixing the long-standing column drift on the `Status` and `ShortStatus` pages.
- **Auto-queuing indicator:** the Status page now says whether the script is actively queuing missing components into assemblers (`Enabled (3 assemblers)`, `Disabled (info-only)`, or `Disabled (no assemblers)`).
- **Faster pickup of changes:** the periodic rescan that notices renames, new LCDs and Custom Data edits now runs every **30 seconds** instead of every 2 minutes.

For the complete reference — every key, every alias, more examples — see the [Companion Script documentation](../Companion-Script/).
