---
layout: default
title: "Release Notes – v2.2.3"
parent: Release Notes
grand_parent: Build and Repair System
nav_order: 11
---

# Release Notes – v2.2.3

- Release date: 13 October 2025
- Notes: N/A

---

## Changes

- All checkboxes in the terminal (except **Help Others**) have been replaced with On/Off switches to provide more room for translated text.
- All tooltip and label text now wraps at a maximum line width for better readability.
- Fixed an issue where welding and grinding would still be attempted on grids protected by server plugins or in preview mode during copy-paste or admin creative placement. Those grids are now properly skipped and excluded from scanning.

  > This also applies to the `!protect` command from the **ALE-PcuTransferrer** Torch plugin.
