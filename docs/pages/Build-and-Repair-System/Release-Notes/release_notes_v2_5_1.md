---
layout: default
title: 'Release Notes – v2.5.1'
parent: Release Notes
grand_parent: Build and Repair System
nav_order: 2
---

# Release Notes – v2.5.1

- Status: **Released** — April 2026
- Notes: Bug fix and quality-of-life release. Fixes grind sort ordering when targets exceed the per-grid scan cap, and adds distance-based culling for particle effects.

---

## Bug Fixes

### Grind Sort Order Ignored with Many Targets (BUG-086)

When grinding with "Ignore Priority Order" enabled, the farthest/nearest/smallest-grid sort settings were silently overridden when a single grid contributed more than 256 grind targets.

The per-grid scan cap (`SortAndCapGridCandidates`) always sorted by block type priority first, regardless of the `GrindIgnorePriorityOrder` setting. This caused the cap to discard blocks that were the farthest (or nearest) but had lower priority, keeping higher-priority blocks at incorrect distances instead.

Additionally, the grid-aware truncation step (`TruncateGridAware`) could disrupt the final sort order by appending overflow items at the end of the target list. A post-truncation re-sort now restores correct ordering.

**Symptoms:**
- BaR grinds in the correct distance order initially, then switches to random or middle-of-the-range blocks
- More likely to occur with large grids (>256 grindable blocks) and multiple BaRs in a cluster

**Fix:**
- `SortAndCapGridCandidates` now respects the `GrindIgnorePriorityOrder` flag, selecting blocks by distance when priority is disabled
- Post-truncation re-sort in `ApplyClusterResultToSelf` restores correct ordering after grid-aware truncation
- "Smallest Grid First" now groups all blocks from the same grid together and sorts by nearest within each grid, instead of interleaving blocks from different grids of the same size

---

## Quality of Life

### Effect Distance Culling (BUG-087)

Particle effects (transport traces, welding/grinding sparks) and sounds are now suppressed for Build and Repair blocks that are more than 1500 meters from the player's camera. Previously, effects were rendered at unlimited distance, wasting GPU resources on BaRs used by other players far away and consuming global effect cap slots that nearby BaRs needed.

Active effects are cleaned up when moving out of range and resume automatically when moving back in range.

### Increased Effect Caps

The global particle effect caps have been raised to support servers with many active Build and Repair systems:

| Setting | Old | New |
| --- | --- | --- |
| Max Transport Effects | 50 | 150 |
| Max Working Effects | 80 | 150 |

Combined with the distance culling above, this ensures nearby BaRs always have enough effect slots available while preventing distant BaRs from consuming them.
