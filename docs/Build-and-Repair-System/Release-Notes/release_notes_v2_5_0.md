---
layout: default
title: "Release Notes – v2.5.0"
parent: Release Notes
grand_parent: Build and Repair System
nav_order: 1
---

# Release Notes – v2.5.0

- Release date: 2 March 2026
- Notes: N/A

## New Features

### Disable Flying Nanobot Effects (per-block)

You can now turn off the flying nanobot trace animations individually per block via the block terminal. Useful on servers or builds where many systems are active and the visual clutter becomes distracting. Welding and grinding sparks are not affected. Server admins can also disable the effects globally for all blocks via the `DisableParticleEffects` mod setting.

### Priority List – Enable All / Disable All

The Weld Priority and Grind Priority lists now have **Enable All** and **Disable All** buttons, so you no longer have to toggle every entry one by one.

### Reset All Settings

A new **"Reset All Settings"** button is available in the block terminal. It resets everything for that block back to defaults in one click, including the priority list states.

### Limit How Many Systems Work on the Same Grid (server setting)

Server admins can now cap how many Build and Repair systems are allowed to work on the same target grid at the same time. This helps prevent situations where dozens of systems pile onto a single grid while ignoring others. The following mod settings control this behaviour:

- `MaxSystemsPerTargetGrid` – the maximum number of systems per target grid (default: **10**).
- `DisableLimitSystemsPerTargetGrid` – set to `true` to remove the limit entirely.

### Assign-To-System Toggle (server setting)

A new `AssignToSystemEnabled` mod setting (default: **true**) lets server admins disable the exclusive block-ownership mechanism if it causes issues in their setup.

### Companion Script Updated

The companion programmable block script has been updated and is included in the repository. It handles automatic assembler queuing and multi-display status output for Build and Repair System groups.

---

## Fixes

### Block Settings Could Be Lost When Joining a Server

Fixed a bug where a client joining a server would sometimes overwrite the server-sent block settings with an older locally stored version. Settings received from the server are now correctly applied straight away.

### Transport Animation Not Showing Correctly After Login

The transport (flying nanobot) animation could appear stuck or missing when a player joined a running session. The transport state is now properly synced to clients so the animation reflects the actual situation.

### Multiplayer Performance – Reduced Network Updates

On busy servers with many active systems, the mod could send too many network updates at once. State updates are now spaced out (at most every 1–2 seconds per system, staggered to avoid spikes), and settings updates are sent at most once per second. This should reduce lag on servers running large numbers of systems.

### Welding / Grinding Status Not Updating Reliably in Multiplayer

Changes in welding and grinding activity were sometimes not broadcast to other players. This has been fixed so status changes are consistently synced.

### Toggling Particle Effects Could Leave Orphaned Animations

When the "Disable flying nanobot effects" option was toggled, the running animation was sometimes not properly stopped, leaving visual artefacts floating in the world. This is now cleaned up immediately on toggle.

### DLC Blocks No Longer Built for Players Who Don't Own the Required DLC

The system now checks whether the owner of a Build and Repair block actually owns the DLC required to build a projected block before attempting to weld it. Previously, the system could try and fail to build DLC blocks on behalf of players who don't own the relevant DLC. Results are cached to avoid any performance impact.

### Minor Performance Improvements

Reduced the number of floating objects the system tracks at once, and tightened up several internal update intervals for slightly snappier response.
