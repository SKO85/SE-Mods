---
layout: default
title: "Release Notes – v2.4.5"
parent: Release Notes
grand_parent: Build and Repair System
nav_order: 6
---

# Release Notes – v2.4.5

- Release date: 2 March 2026
- Notes: New convenience features, multiplayer fixes, and server admin controls.

---

## New Features

### Turn off the flying nanobot effects per block

You can now disable the flying nanobot trace animations for each block individually in its terminal. Handy when lots of systems are running and the visual clutter gets distracting. Welding and grinding sparks are not affected.

**For admins:** the `DisableParticleEffects` mod setting turns the effects off globally for all blocks.

### Priority lists: Enable All / Disable All

The Weld Priority and Grind Priority lists now have **Enable All** and **Disable All** buttons — no more toggling every entry one by one.

### Reset All Settings

A new **"Reset All Settings"** button in the block terminal puts everything for that block back to defaults in one click, including the priority lists.

### Companion script updated

The companion programmable block script has been updated (included in the repository). It handles automatic assembler queuing and multi-display status output for BaR groups.

---

## For Admins

### Limit how many systems work on the same grid

You can now cap how many BaR systems work on the same target grid at the same time, so dozens of systems don't pile onto one grid while ignoring others:

- `MaxSystemsPerTargetGrid` – maximum systems per target grid (default: **10**).
- `DisableLimitSystemsPerTargetGrid` – set to `true` to remove the limit entirely.

### Assign-to-system toggle

A new `AssignToSystemEnabled` setting (default: **true**) lets you disable the mechanism that stops two BaRs from working on the same block, if it causes issues in your setup.

---

## Fixes

- **Block settings lost when joining a server:** A joining client could overwrite the server's block settings with an older local copy. Server settings now apply straight away.
- **Stuck transport animation after login:** The flying nanobot animation could appear stuck or missing when joining a running session. It now reflects what's actually happening.
- **Less network traffic on busy servers:** Updates are now spaced out (at most every 1–2 seconds per system, staggered) and settings updates are sent at most once per second — less lag with many active systems.
- **Welding/grinding status not updating for other players:** Activity changes are now consistently shown to everyone.
- **Leftover animations when toggling effects:** Turning the nanobot effects off no longer leaves stray animations floating in the world.
- **DLC blocks:** The system no longer tries (and fails) to build projected DLC blocks for owners who don't own the required DLC.
- **Minor performance improvements:** Fewer floating objects tracked at once and snappier internal update intervals.
