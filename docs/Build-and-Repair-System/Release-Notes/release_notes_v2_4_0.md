---
layout: default
title: "Release Notes – v2.4.0"
parent: Release Notes
grand_parent: Build and Repair System
nav_exclude: true
---

# Release Notes – v2.4.0

- Release date: 19 February 2026
- Notes: N/A

---

## New Features

### Ignore Priority Order (Grinding)
A new terminal option lets you bypass the grind priority list entirely when selecting grind targets. Useful when you want the system to grind whatever is nearest without being restricted by block class order.

<img height="300" alt="image" src="https://github.com/user-attachments/assets/57d713f3-fea4-4fff-b509-ed48c067a012" />

### Ticking Sound Toggle
A new on/off switch in the block terminal lets you silence the ticking sound that plays when the Build and Repair system is unable to weld or grind (e.g. blocked action, full inventory).

<img height="300" alt="image" src="https://github.com/user-attachments/assets/aee2d133-a046-4734-9a76-73adb81a4f8f" />

### `DisableTickingSound` Config Option (server setting)
Server admins can set `DisableTickingSound` to `true` in `ModSettings.xml` to globally disable the ticking sound for all players at all times.

---

## Bug Fixes

- Fixed a crash that occurred because the log file was not closed properly on shutdown.
- Fixed incorrect sorting behaviour for **Farthest/Nearest** target modes.

---

## Performance

- Improved target scanning efficiency to reduce lag on multiplayer servers.
