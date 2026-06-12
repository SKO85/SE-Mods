---
layout: default
title: "Release Notes – v2.2.0"
parent: Release Notes
grand_parent: Build and Repair System
nav_order: 19
---

# Release Notes – v2.2.0

- Status: **Released** — 1 October 2025
- Notes: Multigrid projection welding, a higher target scan cap, and a batch of smaller fixes.

---

## New Features

### Multigrid projection support
Welding projected grids now works when using the multigrid-projection plugin.

### Block scan cap raised to 256
Each BaR now scans up to **256** welding and grinding targets. Once the cap is reached it stops looking for more, which keeps big jobs from dragging down server performance.

### `DeleteBotsWhenDead` server setting
**For admins:** new option in `ModSettings.xml`. When `true` (default), bots such as Wolves and Spiders are deleted after their inventory is emptied, matching the original mod. Set to `false` to keep them in the world.

---

## Bug Fixes

- **Farthest/Nearest** target modes and priority sorting now actually sort targets by type and distance correctly.
- Deformations on damaged blocks are reset properly when detected.
- The block's info panel now warns when the mod hasn't fully downloaded on a server — this can happen on Dedicated Servers when Steam only partially downloads an update.
- Fixed the typo _further_ → _farther_ in several UI texts.
- `/nanobars -help` now shows just the essentials plus links to the GitHub help page, issue tracker, and Discord.
- **For admins:** the `-cpsf` command was removed. Use `-cwsf` to generate a local `ModSettings.xml`, then copy it to the server, edit it, and restart.

---

## Performance

- More heavy game checks are now reused instead of recalculated every time, including block lookups on grids. Trade-off: Safe-Zone detection for grids entering or leaving a zone may lag by 10–15 seconds.
