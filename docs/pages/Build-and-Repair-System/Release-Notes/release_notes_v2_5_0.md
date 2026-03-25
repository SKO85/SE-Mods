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

> **Note:** This release also includes all changes from [v2.4.5](release_notes_v2_4_5), which was not separately published. Please review those notes as well for a complete picture of what changed since v2.4.4.

---

## Performance Improvements

### Cluster Scan Coordinator

Build and Repair blocks that share the same working area now elect a single coordinator to scan for targets. The coordinator scans once and shares the results with all members of the cluster. If the coordinator is disabled or removed, a new one is automatically elected. On a server with 10 co-located systems this eliminates roughly 80% of redundant scanning work.

### Grinding Performance

Grinding operations have been heavily optimised. Previously, grinding many blocks on large grids could cause frame-time spikes up to 379 ms. This has been reduced to a maximum of roughly 12 ms through a combination of mechanical throttling, staggered execution, a per-tick grind budget, sort optimisations, and improved cache lifetimes.

### Scan Budget per Grid

Previously, a single large grid could consume the entire scan budget, causing Build and Repair blocks targeting other grids to go idle. Scanning now distributes budget fairly across all grids in range, so blocks always make progress regardless of how large any individual grid is.

### Empty Grid Rescan Delay

When a grid is scanned and has no weld or grind targets, it is now skipped for a configurable period (default 20 seconds) before being scanned again. Sub-grid connections (connectors, pistons, rotors) are still traversed, so newly docked or spawned ships are never missed. This significantly reduces CPU usage on servers with many idle grids. Configurable via `EmptyGridRescanDelaySeconds` in `ModSettings.xml`.

### Cluster Stagger

When multiple Build and Repair clusters scan at the same time, their updates are now staggered across up to 3 groups with a gradual ramp (roughly 500 ms spacing). This prevents scan spikes when many systems activate simultaneously.

### Sim-Speed Adaptive Throttle

The system now monitors the server's simulation speed. When sim speed drops below 1.0, scan intervals and operation rates are automatically throttled to reduce load and help the server recover. When sim speed returns to normal, full operation resumes.

### Dynamic Per-Grid System Limit

The default value for `MaxSystemsPerTargetGrid` is now **20** in local and listen-server games and **10** on dedicated servers. This provides a better out-of-the-box experience for both single-player and multiplayer. Setting the value manually in `ModSettings.xml` overrides whichever default applies.

### Network Sync Optimisations

State synchronisation between server and clients has been significantly reduced:

- **Delta sync:** Only changed data is sent. Target lists, missing components, and floating objects are tracked by hash — if a list hasn't changed since the last send, it is omitted from the message. A full sync is sent periodically to prevent drift.
- **Progressive backoff:** When a block's working state hasn't changed (same welding/grinding status, same target counts), the sync interval is progressively extended from 1–2 seconds up to 4–8 seconds. The interval resets immediately when state changes.
- **Reduced payload size:** The maximum number of synced target items per list has been reduced from 64 to 24, which is more than enough for the terminal display.

Together these changes reduce network bandwidth by roughly 80% during stable operation.

### Scanning Interval and Projection Cold Start

The target scanning interval has been tuned for server performance. The cluster coordinator scans for new targets roughly every 10 seconds. This means that when a projector is first activated, there can be a short delay (up to about 10 seconds) before the Build and Repair blocks detect the new projected blocks and begin welding. Once the first blocks are placed and subsequent scans complete, more projected blocks become visible and welding ramps up quickly. Improving the cold-start detection speed is planned for a future update.

### Idle Block Optimisation

Build and Repair blocks with no targets, no transport in progress, and no full inventory now skip all internal processing (welding, grinding, collecting, inventory push). Previously, each idle block still called into several methods that would immediately exit. With many idle blocks, this overhead added up.

### Large-Scale Server Optimisations (100+ blocks)

- **Cluster rebuild caching:** The cluster coordinator assignment now caches each block's configuration key. If no settings have changed and no blocks were added or removed, the full cluster rebuild is skipped entirely. This eliminates 20–30 ms spikes that occurred every 2 seconds on servers with 200+ blocks.
- **Grid count cache throttle:** The per-grid system count cache now rebuilds at most every 5 frames instead of every frame, reducing steady-state CPU cost by roughly 80%.
- **Grid containment pre-check:** Before scanning a grid's blocks for targets, the system checks whether the grid's bounding box fits entirely inside the working area. If it does, expensive per-block range checks are skipped for every block on that grid.

### Internal Optimisations

- The per-grid system count, previously recalculated on every operation call, is now cached once per tick and reused.
- Source and push-target scanning has been decoupled from target scanning and runs independently at its own interval (reduced from 60 s to 30 s).
- Scanning now stops early when disabled work modes are detected, instead of waiting for all target lists to complete.
- LINQ allocations in the Safe Zone range check have been replaced with a simple loop, removing per-block allocations during weld and grind operations.
- Cluster pre-sort optimisation eliminates redundant sorting across cluster members, reducing sort overhead by roughly 40%.
- `Mod.NanobotSystems` changed to a concurrent collection, removing inconsistent manual locking.
- Inventory push uses an adaptive interval — when the welder inventory is less than 75% full, the push interval is extended from 5 to 10 seconds, batching transfers and reducing overhead during grinding.

---

## New Features

### Weld Mode: Full / Functional / Skeleton

The old "Weld to functional only" checkbox has been replaced with a three-option dropdown:

| Mode | Behaviour |
| --- | --- |
| **Weld to full** (default) | Welds blocks to 100% integrity. Same as the previous default. |
| **Weld to functional** | Welds blocks until they become functional (lights turn on, doors open, thrusters fire). Stops at the functional threshold to save components and time. |
| **Skeleton only** | Only places projected blocks (first component). Does not weld or repair existing blocks. This is the fastest way to lay out a large structure from a projection — place all blocks first, then switch to Full or Functional to weld them. |

Existing saves that used "Weld to functional only" will automatically map to the new "Weld to functional" option.

### Live Configuration via Chat Commands

Server admins can now view and change most settings at runtime without restarting, using the `/nanobars config` command:

| Command | Description |
| --- | --- |
| `/nanobars config list` | List all settings with their current values. |
| `/nanobars config get <setting>` | Get a specific setting's current value. |
| `/nanobars config set <setting> <value>` | Change a setting immediately. |
| `/nanobars config save` | Save current settings to `ModSettings.xml`. |
| `/nanobars config reload` | Reload settings from `ModSettings.xml`. |
| `/nanobars config reset` | Reset all settings to defaults. |

**Examples:**
```
/nanobars config set MaxSystemsPerTargetGrid 15
/nanobars config set StaggerGroupCount 3
/nanobars config set MaxGrindsPerTick 8
/nanobars config set AssignmentTtlSeconds 5
/nanobars config save
```

### Inventory Sources and Push Targets

The system now supports a wider range of block types as inventory sources (to pull components for welding) and push targets (to offload items after grinding). Any of the following blocks connected via the conveyor network can be used:

- Cargo Containers, Connectors, Conveyor Sorters
- Assemblers, Refineries
- Ship Grinders, Ship Welders (excluding other Build and Repair blocks)
- Cryo Chambers

**New in v2.5.0:** Cryo Chambers and Refineries have been added to this list. Blocks on connected grids (via connectors, pistons, rotors) are also included. The system rescans for sources and push targets every 30 seconds.

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

The terminal custom info panel now updates at most every 2 seconds when something has changed, instead of immediately on every state change. This reduces unnecessary refreshes while still keeping the displayed status current. On dedicated servers, the expensive terminal redraw is skipped entirely since no local terminal is rendered — only connected clients see the panel.

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

### Safe Zone Cluster Split

When a grid straddles a Safe Zone boundary, some Build and Repair blocks may be inside the zone and others outside. Previously, all blocks on the same grid shared a single scan coordinator. If the coordinator happened to be inside the Safe Zone, it could prevent the other blocks from finding valid targets. The system now creates separate clusters for blocks inside and outside Safe Zones, so each group has a coordinator that matches its permissions.

---

## Stability Improvements

- **Thread safety:** Fixed several race conditions in background scanning, damage tracking, and system collection iteration that could cause intermittent crashes or dropped data on busy servers.
- **Memory:** Fixed a potential memory leak in Safe Zone tracking where removed zones were not always cleaned up.
- **Crash protection:** Dictionary lookups that could throw on missing keys have been replaced with safe `TryGetValue` patterns. Block assignment keys have been changed from object references to stable composite keys.
- **Deleted inventory guards:** Push and pull operations now check for deleted or closed inventory owners before accessing them, preventing errors when blocks are destroyed mid-operation.
- **Exception logging:** Silent `catch {}` blocks in sorting and other internal operations now log exceptions instead of swallowing them, making issues easier to diagnose.
- **Logging initialisation:** Fixed a startup log message that could show a null mod name.
- **Null guards:** Added safety checks for blocks on grids being unloaded, missing item definitions, and projector state changes during build operations. These prevent rare crashes on busy servers.
- **Log file lock on Torch reload:** The logging system now properly releases its file handle when the mod is unloaded, preventing "file in use" errors after Torch hot-reloads.
- **Settings backward compatibility:** Saved block settings from older versions are automatically migrated to the new format (e.g. the old "Weld to functional only" checkbox maps correctly to the new weld mode dropdown).
