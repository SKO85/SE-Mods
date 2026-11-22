---
layout: default
title: "Release Notes – v2.2.3"
parent: Release Notes
grand_parent: Build and Repair System
nav_order: 16
---

# Release Notes – v2.2.3

- Status: **Released** — 13 October 2025
- Notes: Terminal polish and respect for grids protected by server plugins.

---

## Changes

- All checkboxes in the terminal (except **Help Others**) are now On/Off switches, leaving more room for translated text.
- Tooltip and label text now wraps instead of running off the edge.
- The BaR no longer tries to weld or grind grids that are protected by server plugins, or grids still in preview during copy-paste or admin creative placement — those are skipped entirely.

  > **For admins:** this also covers grids protected with the `!protect` command from the **ALE-PcuTransferrer** Torch plugin.
