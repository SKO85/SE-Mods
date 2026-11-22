---
layout: default
title: "Release Notes – v2.4.0"
parent: Release Notes
grand_parent: Build and Repair System
nav_order: 10
---

# Release Notes – v2.4.0

- Release date: 19 February 2026
- Notes: Two new terminal options (skip the grind priority order, silence the ticking sound), a couple of bug fixes, and lighter target scanning on servers.

---

## New Features

### Ignore Priority Order (Grinding)

A new terminal option lets the BaR grind whatever is nearest, skipping the grind priority list entirely. Handy when you just want a wreck gone and don't care about block class order.

<img height="300" alt="image" src="https://github.com/user-attachments/assets/57d713f3-fea4-4fff-b509-ed48c067a012" />

### Ticking Sound Toggle

A new on/off switch in the block terminal silences the ticking sound the BaR makes when it can't weld or grind (blocked action, full inventory, and so on).

<img height="300" alt="image" src="https://github.com/user-attachments/assets/aee2d133-a046-4734-9a76-73adb81a4f8f" />

**For admins:** set `DisableTickingSound` to `true` in `ModSettings.xml` to turn the ticking sound off for all players, all the time.

---

## Bug Fixes

- Fixed a crash that could happen on shutdown.
- **Farthest/Nearest** target modes now actually sort farthest and nearest correctly.

---

## Performance

- Target scanning is more efficient, reducing lag on multiplayer servers.
