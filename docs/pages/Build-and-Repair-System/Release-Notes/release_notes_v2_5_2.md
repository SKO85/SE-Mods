---
layout: default
title: 'Release Notes – v2.5.2'
parent: Release Notes
grand_parent: Build and Repair System
nav_order: 0
---

# Release Notes – v2.5.2

- Status: **Released** — April 2026
- Notes: Bug fix and quality-of-life release. Fixes a scenario where multiple Build and Repair systems spread out across a large base stop welding, adds a new `/nanobars version` command so players can verify client and server versions match, and gives the companion Programmable Block script a big usability upgrade: LCDs can be attached just by renaming them, Custom Data can override what each panel shows, and multiple cockpit surfaces can be configured individually.

---

## Bug Fixes

### Distant BaRs Idle on Large Bases with Multiple Systems (BUG-088)

When several Build and Repair systems were placed on the same large grid (for example, an asteroid base) with their working areas roughly 150 meters apart, the systems further away from one "coordinator" would stop welding and grinding entirely. The companion Programmable Block script's display would show `NULL` as the current welding target on those systems, even though there were plenty of blocks to build nearby.

This was not a sync issue — the distant systems genuinely had no targets. The shared cluster scan that all nearby BaRs use to save performance was centering its candidate collection on one system's position only. When a grid had more than a few hundred targets, the scan kept only the blocks closest to that one system and threw the rest away, so BaRs on the far side of the base received nothing.

**Symptoms:**

- Multiple BaRs on one large grid, spread out so their working areas do not overlap.
- Only the BaR closest to the "coordinator" (elected by internal rules) welds anything; the others sit idle with an empty target list.
- The companion script's LCD shows `CurrentWelding: NULL` on the idle systems.
- Worse on bases with thousands of blocks needing build or repair.

**Fix:**

- The shared cluster scan now takes a snapshot of every cluster member's working-area center at the start of each scan cycle.
- Candidate sorting and per-grid truncation now score blocks by **minimum distance to any member**, instead of the distance to a single anchor position. Distant members receive targets in their own working area again.
- The candidate collection cap scales with cluster size (up to 16x for 4+ members), so large clusters on big grids have enough headroom to actually reach every member's locale.
- Solo BaRs and small clusters are unaffected — existing behavior is preserved.

### Companion PB Script Display Shows Real Targets Again

This was a symptom of BUG-088 rather than a separate bug. The companion Programmable Block script reads the BaR's current welding target through the terminal API; when the BaR had no target, the script's fallback printed `NULL`. With the starvation fix above, distant BaRs now have targets and the script displays real block names on all working systems. No script changes were needed.

### Auto-Push Stops Working When BaR Is Idle (BUG-089)

When any of the `Push ... Immediately` options (components, ore/ingots, items) were enabled and the BaR went idle with leftover items in its welder inventory, the auto-push never fired. Items from the last grind cycle would sit in the welder inventory indefinitely until the BaR re-acquired a target.

The cause was an internal "idle fast-path" (added in v2.5.0 as a performance optimization) that skipped all sub-method dispatch — including the auto-push logic — when there was nothing actively to weld, grind, or collect. The fast-path only checked whether the inventory was *completely full*, not whether it was non-empty, so a welder with leftover items but plenty of free space still triggered the fast-path and auto-push never ran.

**Symptoms:**

- One of the `Push ... Immediately` terminal options is enabled.
- Leftover ore, ingots, components or items remain in the welder inventory after the BaR finishes grinding or runs out of weld targets.
- The items do not transfer to cargo containers or refineries until the BaR picks up a new target.

**Fix:** the idle fast-path now also checks whether the welder inventory is non-empty when an auto-push option is enabled. If items are sitting there waiting, the normal dispatch runs and auto-push fires on its usual 5–10 second cadence. BaRs with auto-push disabled take the fast-path exactly as before — no regression on the hot path.

### Smallest-Grid Sort Picks Nearest Equal-Size Grid First (BUG-091)

With the **Grind Smallest Grid First** option enabled, the BaR could travel to a farther equal-size grid before a nearby one. If two grids had the same block count, the internal tiebreaker was an arbitrary grid-creation number rather than distance — so the grid that was spawned first was always chosen first, even if it was 10× farther away.

This was a follow-up to the v2.5.1 BUG-086 fix, which introduced the "group by grid" behavior to stop same-size grids from being interleaved block-by-block. The grouping behavior was the right call; the choice of tiebreaker was not.

**Symptoms:**

- **Grind Smallest Grid First** is enabled.
- Two or more grids within the working area have the same block count (common with debris fields, drone swarms, or identical prefab stations).
- The BaR appears to pick the "wrong" equal-size grid first — processing a far one while a close one sits untouched.

**Fix:** same-size grids are now ordered by their nearest block's distance to the BaR, so the closest equal-size grid is processed first. The "group by grid" behavior from v2.5.1 is preserved: once a grid is selected, all its blocks are still processed before the BaR moves on to the next grid (no interleaving). Only BaRs with **Grind Smallest Grid First** enabled see any change.

---

## New Features

### `/nanobars version` Chat Command (FEAT-067)

A new player-facing command has been added to help diagnose version mismatches between the client and the server:

| Command | Description |
|---|---|
| `/nanobars version` | Show the mod version running on the local client, and — on dedicated servers — also the server's mod version |

On a **dedicated server** the command prints two lines, one from the local client and one returned by the server over the network:

```
BaR Mod Client: v2.5.2
BaR Mod Server: v2.5.2
```

On a **local (single-player or listen) game session** there is only one process, so only the client line is shown:

```
BaR Mod Client: v2.5.2
```

If the two lines differ, one side needs to update before reporting issues — version drift commonly causes subtle sync glitches, missing features, or unexpected behavior that looks like a bug.

Unlike most `/nanobars` commands, `version` is **available to all players**, not just admins, so anyone on a server can check whether their client is out of sync.

### Companion PB Script: Per-LCD Configuration (FEAT-068)

The companion Programmable Block script now lets you configure status displays directly from the terminal UI — no more editing the script's `BuildAndRepairSystemQueuingGroups` array every time you add or change an LCD. The three existing options (script `DisplayNames`, `@BaR` Custom Data, name tag) can all be mixed and matched; anything you don't configure falls back to the script defaults.

#### Auto-discovery via name tag

Rename an LCD, cockpit, programmable block or any other text surface so its CustomName contains a `[BaR:<group>]` tag and the script will auto-attach it to that BaR group on the next reinit — no script edits needed.

| CustomName | Attaches to |
|---|---|
| `Hangar LCD [BaR:1]` | Group 1 (1-based index into `BuildAndRepairSystemQueuingGroups`) |
| `Status Panel [BaR:Hangar BaR Group1]` | Group with `Name = "Hangar BaR Group1"` (case-insensitive) |
| `Cockpit [BaR:1@0]` | Surface 0 of the cockpit, attached to group 1 |
| `Bridge Cockpit [BaR:1@0,1,2]` | Surfaces 0, 1 and 2 of the cockpit — each as an independent entry |

Tagged panels take **priority** over explicit `DisplayNames`: if the same panel is both tagged and listed in the script config, the tag wins and the panel is only added once.

#### Per-LCD Custom Data overrides

Every LCD (and cockpit whose surfaces are referenced) can carry a small config block in its Custom Data that overrides the script-level `DisplayDefinition` for that panel only:

```
@BaR
Kinds=Status,WeldTargets,MissingItems
MaxLines=15
SwitchTime=4
FontSize=auto
@/BaR
```

| Key | Description |
|---|---|
| `Kinds` | Comma-separated list of pages to cycle through (`Status`, `ShortStatus`, `WeldTargets`, `GrindTargets`, `CollectTargets`, `MissingItems`, `BlockWeldPriority`, `BlockGrindPriority`). Short aliases (`weld`, `grind`, `missing`, ...) accepted. |
| `MaxLines` | Positive integer — line cap for list pages. |
| `SwitchTime` | Seconds between page switches. `0` = no rotation. |
| `FontSize` | Explicit font size (e.g. `1.2`) or `auto`/`fit` to measure the rendered text once and pick the largest scale that fits the surface. |

Anything outside the `@BaR` … `@/BaR` block is ignored, so you can keep your own notes in Custom Data too.

#### Scoped per-surface Custom Data for cockpits

Cockpits share one Custom Data across all their surfaces. New **scoped** blocks let each surface of the same cockpit show a different page:

```
@BaR
MaxLines=15
@/BaR

@BaR@0
Kinds=Status
SwitchTime=0
@/BaR

@BaR@1
Kinds=WeldTargets,MissingItems
@/BaR

@BaR@2
Kinds=GrindTargets
SwitchTime=0
@/BaR
```

Precedence per attached surface: script config → unscoped `@BaR` (cockpit-wide base) → `@BaR@<surfaceIndex>` (per-surface override).

#### Auto-seeded default Custom Data

When a tagged block's Custom Data is **empty**, the script writes a commented `@BaR` template into it on the first init. Open the LCD's terminal → Custom Data panel to see the full list of knobs, remove or comment lines to fall back to script defaults, or edit them to override for that panel. Non-empty Custom Data is **never** touched.

#### Forced Monospace font + column alignment fix

Panels the script writes to are automatically switched to the built-in `Monospace` font. The existing status pages pad labels with spaces to line up the `:` and values in a column — which only actually aligns in a monospaced font. This fixes the long-standing column drift in the `Status` and `ShortStatus` pages.

#### Auto-queuing indicator on the Status page

The full Status page now shows whether the script is actively queuing missing components into assemblers:

```
Auto-queuing      : Enabled (3 assemblers)
Auto-queuing      : Disabled (info-only)
Auto-queuing      : Disabled (no assemblers)
```

If you started the PB with the `info-only` argument (display-only, no queuing) the Status page says so explicitly, and if a group has no assemblers configured it tells you that instead.

#### Faster reinit (120 s → 30 s)

The periodic full rescan that picks up renames, new LCDs, Custom Data edits and group changes now runs every **30 seconds** instead of every 2 minutes, so terminal-only edits take effect quickly without a PB recompile.

For the complete reference — every key, every alias, more examples and the common setups — see the [Companion Script documentation](../Companion-Script/).
