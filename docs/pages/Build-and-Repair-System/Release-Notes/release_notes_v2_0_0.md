---
layout: default
title: "Release Notes – v2.0.0 – v2.1.1 (Major Update)"
parent: Release Notes
grand_parent: Build and Repair System
nav_order: 20
---

# Release Notes – v2.0.0 – v2.1.1 (Major Update)

- Status: **Released** — September 2025
- Notes: Major rewrite based on the original mod. Adds Safe-Zone and shield mod support, reputation loss on grinding, and better multiplayer performance.

---

## New Features

### Safe-Zone support
The BaR now respects Safe-Zone rules for welding, grinding, and projection building. **For admins:** checks are on by default and can be turned off in `ModSettings.xml`.

### Shield mod support
The BaR now respects shields from the Shields mod. **For admins:** on by default, can be disabled in `ModSettings.xml`.

### Reputation loss on grinding
Grinding grids belonging to other factions or NPCs now costs reputation, just like grinding by hand. **For admins:** can be disabled in `ModSettings.xml`.

### Control panel warnings
- The block's info panel now tells you when you're inside a Safe-Zone or when a Safe-Zone option is disabled.
- The info panel also warns when the mod hasn't fully downloaded on a server — a common cause of weird behaviour on Dedicated Servers after a partial Steam download.

### `DeleteBotsWhenDead` server setting
**For admins:** controls whether bots (Wolves, Spiders) are deleted after their inventory is emptied. Default: `true`.

---

## Bug Fixes

- Enemies can no longer be ground inside Safe-Zones you or your faction don't own. Grinding inside your own Safe-Zone still works when the relevant Safe-Zone option is enabled.

---

## Performance

- Several heavy game checks are now reused instead of recalculated constantly, which noticeably reduces sim-speed impact in multiplayer. As a trade-off, the BaR may take 10–15 seconds to notice a grid entering or leaving a Safe-Zone.

---

## Known Limitations / Temporary Removals

- **Auto-Power-Off** has been temporarily removed. A rework is planned that will let players opt out of this behaviour.
- **Moving grid tracking** has been temporarily removed.
