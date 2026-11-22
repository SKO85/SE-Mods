---
layout: default
title: "Release Notes – v2.2.5"
parent: Release Notes
grand_parent: Build and Repair System
nav_order: 14
---

# Release Notes – v2.2.5

- Release date: 14 October 2025
- Notes: Bug fix release. The BaR now leaves alone grids it shouldn't touch — previews, non-editable grids and indestructible grids.

---

## Bug Fixes

- Ships still in preview (copy-paste placements or creative spawns) are now ignored until they're actually placed in the world.
- Grids marked as non-editable are no longer welded or ground.
- Grids marked as indestructible or immune to damage are no longer ground.

> **For admins using ALE PCU Transferrer (Torch plugin):** the `!protect` command flags a grid so it can't be welded or ground. The BaR used to ignore those flags and work on protected grids anyway — it now respects them. Use `!unprotect` to make a grid accessible again.
