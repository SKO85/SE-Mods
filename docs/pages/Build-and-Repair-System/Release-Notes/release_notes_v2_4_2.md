---
layout: default
title: "Release Notes – v2.4.2 — Hotfix #2"
parent: Release Notes
grand_parent: Build and Repair System
nav_order: 8
---

# Release Notes – v2.4.2 — Hotfix #2

- Release date: 19 February 2026
- Notes: Small hotfix for two grind-related terminal issues.

---

## Bug Fixes

- **Grind priority list ignored in mixed work modes**
  The grind priority list (which block types to grind) only worked in _Grinding only_ mode. In _Weld before grind_, _Grind before weld_ and _Grind if welding gets stuck_ it was ignored and every block type was treated as enabled. Fixed — the list is now respected in every work mode.

- **Grind order toggle could leave nothing selected**
  Switching off _Smallest grid first_ or _Farthest first_ left none of the three grind order options selected. Both now correctly fall back to _Nearest first_ when turned off.
