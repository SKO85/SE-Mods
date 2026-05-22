---
layout: default
title: 'Release Notes – v2.5.3'
parent: Release Notes
grand_parent: Build and Repair System
nav_order: 1
---

# Release Notes – v2.5.3

- Status: **Released** — April 2026
- Notes: Bug fix release. Restores correct farthest-first grinding on large ships and auto-heals worlds with a corrupted grind-janitor-relations setting.

---

## Bug Fixes

### Farthest-First Grinding on Large Ships (BUG-094)

When grinding a large ship (roughly 7000+ blocks) with **Grind Near First** disabled — the "farthest first" mode — the BaR would start grinding somewhere in the middle or beginning of the ship instead of at the far end.

This was a regression introduced alongside a v2.5.0 performance optimization. The scan was truncating its candidate list to the first N qualifying blocks in arbitrary grid-cache order, then sorting those by distance and keeping the top 256. The top 256 of an arbitrary prefix is not the same as the top 256 of the full grid — so far-end blocks were never even considered. Smaller ships weren't affected because they never hit the cap.

**Symptoms:**

- Farthest-first grinding starts in the middle/beginning of a large ship (~7000+ blocks).
- Nearest-first grinding (the other option) still works correctly — the bug only affected the farthest-first case.
- Increasing the internal grind target cap (not a terminal option) made the bug disappear because the cap stopped being a gate.

**Fix:** the per-block count gate during grid collection no longer short-circuits based on the global scan budget. Every qualifying block on the grid is considered, then the existing per-grid sort-and-cap selects the true top-N by the player's chosen sort (farthest, nearest, smallest-grid-first, priority, etc.). The post-loop sort already existed — it just needed to be fed the complete candidate set.

On large ships this adds a few milliseconds of background-thread work per scan cycle. No impact on simulation speed because the scan runs on a background thread. Small ships are unaffected.

### Auto-Push Target Signature (BUG-090)

*(Shipped in v2.5.2 but worth re-mentioning for players who missed the earlier note.)* The push-targets-full backoff now detects same-size container swaps and clears the flag immediately instead of waiting 60 seconds.

### `AllowedGrindJanitorRelations` Empty Config Break (BUG-093)

A `ModSettings.xml` with an empty `<AllowedGrindJanitorRelations></AllowedGrindJanitorRelations>` element silently disabled grinding on every BaR in the world. Discovered after a player shared their world's config file and reproduced on a second machine. The broken state could arise from hand-editing the file or from a settings-save pathway that cleared the value without running the v5 migration that normally repopulates it.

The issue was in how per-block janitor settings are loaded:

1. Each BaR block's `UseGrindJanitorOn` is AND-masked against the mod-wide `AllowedGrindJanitorRelations` at load time.
2. If the mod-wide value is `0` (empty), the mask zeroes every BaR's janitor setting regardless of what the player chose in the terminal.
3. The migration that would have repopulated the default was gated on `Version <= 5`, so worlds already at `Version = 7` never got fixed up.

**Fix:** the mod settings loader now applies the default (`NoOwnership | Enemies | Neutral`) unconditionally whenever `AllowedGrindJanitorRelations == 0`, regardless of the file's version. **Worlds with this broken state auto-heal on first load after the update** — no manual edit or save deletion needed. If an admin genuinely wants to disable janitor grinding for all BaRs, they should set `UseGrindJanitorFixed=true` with `UseGrindJanitorDefault=None` instead of leaving the allowed-relations list empty.
