---
layout: default
title: 'Release Notes – v2.5.5'
parent: Release Notes
grand_parent: Build and Repair System
nav_order: 0
---

# Release Notes – v2.5.5

- Status: **Released** — May 2026
- Notes: Bug fix release. Fixes two ways a BaR could quietly stop working, makes target scanning twice as fast, and makes the Show Area box see-through.

---

## Bug Fixes

### Stuck on "inventory full"

If a BaR filled its own inventory completely, it could stay convinced it was full forever — even after everything had been pushed out to cargo and the inventory was visibly empty. It just sat there doing nothing, and reloading the world didn't help.

Fixed: the BaR now notices when its inventory drains and gets back to work on its own. If you've been toggling BaRs off and on to un-stick them, you can stop.

### Slow to notice freed-up cargo space

When all your cargo was full, the BaR stopped trying to push items — fair enough. But if you made room *inside* an existing container (hauled the ingots away, an assembler used up some components), it could take up to a minute to notice. It now retries within about 15 seconds. Adding or removing a container still triggers an immediate retry, same as before.

### Faster target scans

BaRs now look for new work every **5 seconds** instead of 10 (idle BaRs check every 10 instead of 20). Freshly pasted ships, new projections and new damage get picked up about twice as fast. The extra CPU cost is tiny — idle BaRs still skip nearly all the work when nothing around them has changed.

### Show Area box is see-through now

The **Show Area** box used to be solid black — which neatly hid exactly the thing you turned it on to look at. It's now a light transparent grey, so you can see the working area and the blocks inside it at the same time.

---

## Compatibility

- No save or settings changes — safe to drop straight in over v2.5.4.
