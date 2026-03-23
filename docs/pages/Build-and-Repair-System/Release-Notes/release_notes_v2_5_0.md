---
layout: default
title: 'Release Notes – v2.5.0'
parent: Release Notes
grand_parent: Build and Repair System
nav_order: 1
---

# Release Notes – v2.5.0

- Release date: March 2026
- Notes: Major performance and stability release. Focused on server performance with many active Build and Repair systems, bug fixes, and quality-of-life improvements.

---

## Performance Improvements

### Cluster Scan Coordinator

Build and Repair blocks that share the same working area now elect a single coordinator to scan for targets. The coordinator scans once and shares the results with all members of the cluster. If the coordinator is disabled or removed, a new one is automatically elected. On a server with 10 co-located systems this eliminates roughly 80% of redundant scanning work.

### Grinding Performance

Grinding operations have been heavily optimised. Previously, grinding many blocks on large grids could cause frame-time spikes up to 379 ms. This has been reduced to a maximum of roughly 12 ms through a combination of mechanical throttling, staggered execution, a per-tick grind budget, sort optimisations, and improved cache lifetimes.

### Scan Budget per Grid

Previously, a single large grid could consume the entire scan budget, causing Build and Repair blocks targeting other grids to go idle. Scanning now distributes budget fairly across all grids in range, so blocks always make progress regardless of how large any individual grid is.

### Empty Grid Rescan Delay

When a grid is scanned and has no weld or grind targets, it is now skipped for a configurable period (default 30 seconds) before being scanned again. Sub-grid connections (connectors, pistons, rotors) are still traversed, so newly docked or spawned ships are never missed. This significantly reduces CPU usage on servers with many idle grids. Configurable via `EmptyGridRescanDelaySeconds` in `ModSettings.xml`.

### Cluster Stagger

When multiple Build and Repair clusters scan at the same time, their updates are now staggered across up to 3 groups with a gradual ramp (roughly 500 ms spacing). This prevents scan spikes when many systems activate simultaneously.

### Sim-Speed Adaptive Throttle

The system now monitors the server's simulation speed. When sim speed drops below 1.0, scan intervals and operation rates are automatically throttled to reduce load and help the server recover. When sim speed returns to normal, full operation resumes.

### Dynamic Per-Grid System Limit

The default value for `MaxSystemsPerTargetGrid` is now **20** in local and listen-server games and **10** on dedicated servers. This provides a better out-of-the-box experience for both single-player and multiplayer. Setting the value manually in `ModSettings.xml` overrides whichever default applies.

### Internal Optimisations

- The per-grid system count, previously recalculated on every operation call, is now cached once per tick and reused.
- Source and push-target scanning has been decoupled from target scanning and runs independently at its own interval (reduced from 60 s to 30 s).
- Scanning now stops early when disabled work modes are detected, instead of waiting for all target lists to complete.
- LINQ allocations in the Safe Zone range check have been replaced with a simple loop, removing per-block allocations during weld and grind operations.
- Cluster pre-sort optimisation eliminates redundant sorting across cluster members, reducing sort overhead by roughly 40%.
- `Mod.NanobotSystems` changed to a concurrent collection, removing inconsistent manual locking.

---

## New Features

### Cryo Chambers and Refineries as Inventory Sources

Cryo Chambers and Refineries are now included when scanning for source and push-target inventories. Components stored in these blocks can be pulled for welding, and excess items can be pushed into them.

### Debug Mode (Config)

A new `DebugMode` option can be set in `ModSettings.xml` to show additional diagnostic information in the terminal custom info panel, such as scan timings, target counts, and internal state. This is intended for testing and debugging purposes only — it is not a per-block setting and should not be left enabled in normal play.

### Work Mode Fallthrough

In **Weld Before Grind** and **Grind Before Weld** modes, if no actionable targets exist for the primary mode the system now falls through to the secondary mode instead of going idle. This means the block will always find work if any valid target exists, regardless of which mode has priority.

### Built-in Profiler (Admin)

Server admins can run a built-in profiler to measure the mod's performance impact. Profiling data is written to log files in the mod's storage folder. Commands (admin-only):

| Command | Description |
| --- | --- |
| `/nanobars profile start [seconds] [minDurationMs]` | Start a profiling session. Defaults to 120 seconds auto-stop. |
| `/nanobars profile stop` | Stop the current session and write the summary. |
| `/nanobars profile status` | Show whether profiling is active and current settings. |
| `/nanobars profile minduration <ms>` | Set the minimum method duration threshold for logging. |
| `/nanobars profile help` | Show profiling command help. |

### Sim-Speed Override (Admin)

Admins can temporarily override the reported simulation speed for testing purposes:

| Command | Description |
| --- | --- |
| `/nanobars sim <0.1–1.0>` | Force a specific sim-speed value. |
| `/nanobars sim reset` | Return to the real sim-speed value. |

### Custom Info Panel Refresh

The terminal custom info panel now refreshes every 2 seconds instead of only when state changes. This keeps the displayed status up to date even when the block is idle or waiting.

---

## Bug Fixes

### Build and Repair Blocks Going Idle When Other Grids Are Available

Fixed an issue where a block would go idle even though valid targets existed on other grids. This happened when all targets on the block's current grid had reached the `MaxSystemsPerTargetGrid` limit — the system would stop searching instead of moving to the next grid. The block now correctly releases its current target and continues searching other grids in the same tick.

### Inventory Circular Transfers Between Build and Repair Blocks

Fixed a bug where one Build and Repair block would push items into another Build and Repair block's inventory, which would then push them back — creating an endless loop that filled both inventories and prevented any actual work. Other Build and Repair block inventories are now excluded from the push-target list.

### Sound Effects Played at Player Position Instead of Block

Welding and grinding sounds were sometimes heard at the player's location rather than at the Build and Repair block. This was caused by accessing position data from destroyed or closed blocks. The system now checks block validity before updating sound positions, and properly cleans up effect counters when a block is removed.

### Safe Zone False Warnings on Block Placement

Newly placed Build and Repair blocks could briefly show "Warning: Safe Zone detected" or "Warning: Shield detected" in the info panel before the first Safe Zone check completed. The info panel now waits until the initial check has finished before displaying any Safe Zone or Shield warnings.

### Status Showing "Grinding (Transporting)" During Collection

When collecting floating objects, the info panel incorrectly showed "Grinding (Transporting)" instead of "Collecting (Transporting)". The status now correctly distinguishes between grinding and collection transport.

### Power Display Showing 350 kW Instead of 200 kW

The terminal info panel reported a maximum power draw of 350 kW, but the actual maximum is 200 kW. Power states are mutually exclusive (standby, transport, welding, or grinding — never combined). The formula has been corrected.

### "Missing Components" Before Initial Scan Completes

Immediately after a block was placed or a world was loaded, the system could attempt to weld or grind before the first background scan had completed. This produced false "Missing Components" messages and wasted operations. The system now waits for the initial scan to finish before starting any work.

### Grinding and Collecting with Full Inventory

The system could start grinding or collecting even when the welder's inventory was already full, because only transport capacity was checked. The inventory is now checked before grinding and collection operations as well.

### Block Stuck When Grid Limit Reached (Secondary Task)

In **Weld Before Grind** or **Grind Before Weld** mode, the system could get stuck when all primary targets hit the `MaxSystemsPerTargetGrid` limit. Instead of falling through to the secondary task, it would report the primary task as having work to do and skip the secondary entirely. The system now correctly detects when the primary task is fully blocked and proceeds to the secondary.

### Grind Priority Ordering Not Respected

The grind priority list was not being fully honoured due to two issues: an incorrect filter was applied when building the target list, and targets were truncated before sorting instead of after. Both have been fixed — grind targets now follow the configured priority order.

### Floating Objects Collected Outside Working Area

Floating objects outside the block's actual working area (but inside the larger bounding box used for scanning) could be collected. A containment check is now applied so only objects within the configured work area are collected.

### Safe Zone Protection Could Fail When Zone Not Loaded

If a Safe Zone entity was not fully loaded when checked, the system could incorrectly determine that a block was not protected and attempt to grind it. The system now defaults to "protected" when a Safe Zone entity cannot be resolved.

### Block Priority Classification Cache Error

The priority system could return the wrong block class for up to 5 minutes because the cache key did not account for all parameters. This has been fixed so priority lookups always return the correct classification.

### Distance Sorting Inconsistency

Distance-based target sorting could produce inconsistent results because position data was accessed from a background thread. The block's position is now captured on the main thread before sorting.

### Constant Push Attempts to Full Targets

When all push targets (cargo containers) were full, the system would continuously attempt to push items every cycle. A backoff mechanism now detects full push targets and reduces attempt frequency, saving CPU.

### Sort Toggle in Terminal Clearing All Options

Deselecting a sort option (Nearest, Farthest, Smallest Grid) in the terminal could unintentionally clear all sort options instead of cycling correctly. The toggle logic has been corrected.

---

## Stability Improvements

- **Thread safety:** Fixed several race conditions in background scanning, damage tracking, and system collection iteration that could cause intermittent crashes or dropped data on busy servers.
- **Memory:** Fixed a potential memory leak in Safe Zone tracking where removed zones were not always cleaned up.
- **Crash protection:** Dictionary lookups that could throw on missing keys have been replaced with safe `TryGetValue` patterns. Block assignment keys have been changed from object references to stable composite keys.
- **Deleted inventory guards:** Push and pull operations now check for deleted or closed inventory owners before accessing them, preventing errors when blocks are destroyed mid-operation.
- **Exception logging:** Silent `catch {}` blocks in sorting and other internal operations now log exceptions instead of swallowing them, making issues easier to diagnose.
- **Logging initialisation:** Fixed a startup log message that could show a null mod name.
