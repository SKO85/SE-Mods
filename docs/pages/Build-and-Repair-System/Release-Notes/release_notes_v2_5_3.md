---
layout: default
title: 'Release Notes – v2.5.3'
parent: Release Notes
grand_parent: Build and Repair System
nav_order: 2
---

# Release Notes – v2.5.3

- Status: **Released** — April 2026
- Notes: Small bug fix release. "Farthest first" grinding works properly on big ships again, and worlds where janitor grinding silently broke now fix themselves.

---

## Bug Fixes

### "Farthest first" grinding on big ships

With **Grind Near First** turned off, the BaR is supposed to start at the far end of the ship and grind its way toward you. On big ships (roughly 7000+ blocks) it instead started somewhere in the middle or near the front.

This was a side effect of a performance change in v2.5.0 — the BaR was only looking at part of the ship when deciding what counted as "farthest". It now considers the whole ship again. Small ships were never affected, and "nearest first" always worked fine.

### Swapped containers are noticed right away

(Shipped in v2.5.2 — mentioned here for anyone who missed it.) If the BaR thought all your cargo was full, swapping a container for another one of the same size used to go unnoticed for up to a minute. It now notices immediately.

### Janitor grinding silently disabled in some worlds

Some worlds ended up with a broken value in their settings file that quietly turned off janitor grinding for every BaR — no error message, the terminal options just did nothing. This could happen after hand-editing `ModSettings.xml`, or from an unlucky save.

These worlds **fix themselves automatically** the first time they load with this update. Nothing to edit, nothing to delete.

**For admins:** if you genuinely want janitor grinding off server-wide, don't blank out `AllowedGrindJanitorRelations` — set `UseGrindJanitorFixed=true` together with `UseGrindJanitorDefault=None` instead.
