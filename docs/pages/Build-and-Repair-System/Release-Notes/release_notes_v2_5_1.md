---
layout: default
title: 'Release Notes – v2.5.1'
parent: Release Notes
grand_parent: Build and Repair System
nav_order: 4
---

# Release Notes – v2.5.1

- Status: **Released** — April 2026
- Notes: Bug fix and quality-of-life release. Grind sort order works properly on grids with lots of targets, and effects from far-away BaRs no longer waste your GPU.

---

## Bug Fixes

### Grind sort order ignored on grids with many targets

With **Ignore Priority Order** enabled, the farthest/nearest/smallest-grid sort settings could be quietly ignored once a single grid had more than 256 grindable blocks — the BaR would grind in the right order for a while, then suddenly jump to seemingly random blocks in the middle of the range. Big grids and several BaRs working together made it more likely.

Fixed: your chosen distance order is now respected all the way through. As part of the same fix, **Smallest Grid First** now finishes all blocks on one grid before moving to the next, instead of hopping between same-size grids block by block.

---

## Quality of Life

### No more effects from far-away BaRs

Particle effects (transport traces, welding/grinding sparks) and sounds are now switched off for BaR blocks more than 1500 meters from your camera. Previously, BaRs on the other side of the map — typically other players' bases — were still rendered, wasting GPU power and using up effect slots that nearby BaRs needed. Effects stop when you move out of range and come back automatically when you return.

### Room for more effects

The global limits on simultaneous effects have been raised for servers with many active BaRs:

| Setting | Old | New |
| --- | --- | --- |
| Max Transport Effects | 50 | 150 |
| Max Working Effects | 80 | 150 |

Together with the distance cut-off above, nearby BaRs should always have effect slots available.
