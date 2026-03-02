---
layout: default
title: "Release Notes – v2.2.5"
parent: Release Notes
grand_parent: Build and Repair System
nav_exclude: true
---

# Release Notes – v2.2.5

- Release date: 14 October 2025
- Notes: N/A

---

## Bug Fixes

- Grids that are in preview mode (copy-paste placements or creative spawns) are now skipped and not scanned until they are fully placed in the world.
- Grids marked as non-editable are now correctly excluded from welding and grinding.
- Grids flagged as indestructible or immune to damage are now correctly excluded from grinding.

> **Note for server admins using ALE PCU Transferrer (Torch plugin):** The `!protect` command sets specific grid flags to prevent welding and grinding. The Build and Repair system previously ignored these flags and would still attempt to weld or grind protected grids. This update correctly respects them. Use `!unprotect` to make a grid accessible again.
