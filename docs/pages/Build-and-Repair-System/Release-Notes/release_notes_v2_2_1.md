---
layout: default
title: "Release Notes – v2.2.1"
parent: Release Notes
grand_parent: Build and Repair System
nav_order: 18
---

# Release Notes – v2.2.1

- Status: **Released** — 2 October 2025
- Notes: Two small fixes for Admin Safe-Zones and reconnecting to servers.

---

## Bug Fixes

- Inside Admin Safe-Zones, the BaR wrongly refused to grind your own grids when the target blocks had no owner. It now correctly checks how the BaR's owner relates to the target block.
- Fixed an initialisation error that showed up in the block's info panel after reconnecting to a server.
