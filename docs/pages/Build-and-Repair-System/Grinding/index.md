---
layout: default
title: Grinding
parent: Build and Repair System
nav_order: 6
---

# Grinding

The Build and Repair block can grind enemy, neutral, unowned, or color-tagged blocks within its work area. This page describes how grinding decisions are made and how to troubleshoot common issues.

---

## Search Mode — Walk vs. Fly

Before anything else, the BaR's **Search Mode** decides *which grids* it is willing to scan for grind targets. This is the upstream gate every other grinding rule depends on — Grind Color, Grind Janitor, and the priority list all only operate on grids that the search has actually returned. Pick the wrong mode and the rest of your settings have nothing to act on.

The terminal **Search Mode** dropdown offers two modes:

| Mode | Internal name | What gets scanned |
|---|---|---|
| **Walk mode** (default) | `Grids` | Only the grid the BaR is mounted on and its connected sub-grids (via locked connectors, pistons, rotors, mergers). Unconnected hostile or wreckage grids in the work area are ignored. |
| **Fly mode** | `BoundingBox` | Same as Walk mode, *plus* every other grid whose blocks fall inside the BaR's work area — including unconnected enemy ships, drifting wreckage, NPC grids, and salvage targets that are not docked to your station. |

For grinding the choice matters more than for welding, because grinding is the operation most often used on grids the BaR is *not* attached to. Some common patterns:

- **Salvage / wreckage cleanup** — the grinding target is a free-floating piece of wreckage near a station. Walk mode will not see it (no connector lock); switch the BaR to **Fly mode** so the area scan picks it up.
- **Auto-disposal of NPC drops** — pirate drones, wolves' scrap, encounter wrecks. These are unconnected to your base. **Fly mode** with the Grind Janitor set to `No Ownership` and `Enemies` automates the cleanup.
- **Painting your own grid for selective grinding** — you only need **Walk mode** for this. The grind targets are blocks on your *own* connected grid, which Walk already covers.
- **Ship docked at a connector but not yet locked** — a grid that is "near" but technically unconnected (e.g. mid-docking-approach) is invisible in Walk mode. Either complete the connector lock so it joins the grid group, or switch to Fly mode for a scan that catches it regardless.

Things to keep in mind when using Fly mode for grinding:

- **Reputation**: grinding a faction-owned grid you happen to fly past still triggers `DecreaseFactionReputationOnGrinding`. Fly mode makes it easy to pick up unintended faction grids.
- **Safe Zones, shields, plugin protection** (`!protect`) all still apply — Fly mode does not bypass them. A protected grid in your work area is still skipped.
- **Per-grid limit**: every grid Fly mode pulls in counts against `MaxSystemsPerTargetGrid`. With several BaRs in Fly mode covering the same area you can saturate a grid faster than expected.
- **Scan cost** scales with the number of grids inside the work area. A wide work area in Fly mode in a busy region (asteroid base, NPC pirate hub) is the most expensive configuration. Tighten the work area or switch to Walk if scan cost gets out of hand.

Server admins can hide either mode globally via `AllowedSearchModes` in `ModSettings.xml`. The default for newly-placed blocks is set by `SearchModeDefault`.

---

## When the BaR Grinds

A block becomes a grind target when **either** of the following is true:

- It is painted with the configured **Grind Color**, and the **Use Grind Color** option is enabled on this block.
- It belongs to an ownership category enabled in the **Grind Janitor** (`No Ownership`, `Neutral`, or `Enemies`).

In both cases the block's type must also be **enabled** in the Grind Priority list — disabled entries are never ground, by either route. Grinding only runs at all when the Work Mode allows it (anything except `Welding only`).

When working through the target list the BaR also skips:

- blocks another BaR is already working on (see block assignment on the [Welding](../Welding/#block-assignment) page),
- grids that have hit the `MaxSystemsPerTargetGrid` limit,
- protected grids (Safe Zones, shields, plugin protection — see below),
- everything, while the BaR's own inventory is full (grinding pauses until items are pushed out).

### Grind Order

Targets are worked in this order:

1. **Janitor targets first** — blocks matched by the Grind Janitor are always ground before Grind-Color targets.
2. **Priority list order** — unless the **Ignore priority order** toggle is checked (it is checked by default on newly-placed BaRs), blocks are processed by the Grind Priority list, top to bottom.
3. **Smallest grid first** — when checked, blocks on the grid with the fewest blocks are ground first, so small wreckage is finished and removed before large hulls are started.
4. **Nearest First / Farthest first** — distance breaks all remaining ties. Farthest-first is useful when grinding a structure toward the BaR so dropped items stay in collection range.

---

## Grind Color

Blocks painted with the **Grind Color** HSV are treated as grind targets when **Use Grind Color** is enabled on a BaR.

- Default HSV: `321, 100, 50`.
- Locked server-wide via `UseGrindColorFixed`.
- Per-block toggle in the terminal.

The default Grind Color is *very* close to the default Ignore Color (`321, 100, 51`). Choose distinctly different shades when painting your own grids — accidentally matching the Grind Color is a common cause of self-grinding.

---

## Grind Janitor

The Grind Janitor automatically grinds blocks belonging to specific ownership categories. Configure it from the terminal:

| Relation | Description |
|---|---|
| `No Ownership` | Blocks with no owner — typically wreckage or razed grids. |
| `Neutral` | Blocks owned by neutral players or factions (not at war). |
| `Enemies` | Blocks owned by enemy players or factions. |

The available categories are filtered by `AllowedGrindJanitorRelations` in `ModSettings.xml` — admins can hide categories from the terminal.

The **Grind Janitor Options** sub-flags adjust how far each block is ground:

| Flag | Behaviour |
|---|---|
| _(none)_ | Grind blocks all the way down (full grind). |
| `Disable Only` | Grind functional blocks only until they stop working, then move on. |
| `Hack Only` | Grind functional blocks only until they can be hacked, then move on. |

Locked server-wide via `UseGrindJanitorFixed`.

> **Note (fixed in v2.5.3):** if `AllowedGrindJanitorRelations` was empty in `ModSettings.xml`, the loader would silently disable janitor grinding on every BaR. The settings loader now auto-heals an empty value to `NoOwnership | Enemies | Neutral` regardless of the file's version. To genuinely disable janitor grinding for all BaRs, set `UseGrindJanitorFixed = true` with `UseGrindJanitorDefault = None` instead.

---

## Work Modes

The terminal **Work mode** dropdown controls the order in which welding and grinding are tackled:

| Mode | Behaviour |
|---|---|
| `Weld before grind` | Weld first; grind only when there is nothing to weld this cycle. |
| `Grind before weld` | Grind first; weld only when there is nothing to grind this cycle. |
| `Welding only` | Welding only. |
| `Grinding only` | Grinding only. |

The legacy `Grind if weld get stuck` mode was removed in v2.5.4. Worlds that previously had a BaR set to it are silently migrated to `Weld before grind`.

---

## Sticking With a Block

The BaR keeps grinding its current block until the block is fully dismantled (integrity *and* leftover components stripped), then moves to the next target in sort order. Razed blocks are removed from the target list immediately so other BaRs nearby do not waste time on them.

If the current block vanishes (grid deleted, block destroyed by something else), the BaR picks a new target on the same tick — no waiting for the next scan.

---

## Speed

Grinding speed is controlled by:

- `GrindingMultiplier` (`ModSettings.xml`) — per-tick grind amount. `1` = default, `2` = double, `0.5` = half. Range: 0.1–100.
- `WorkSpeed` (`ModSettings.xml`) — how often grinding ticks fire. `1` = roughly every 1.67 s, `10` = roughly every 0.17 s. Range: 1–10.
- `MaxGrindsPerTick` (`ModSettings.xml`) — global cap on grind operations per tick across all BaRs. `0` (auto) scales with BaR count (min 5, max 10). Range: 0–100. A companion time budget, `MaxGrindMsPerTick` (default `8` ms), caps how much time per tick is spent grinding across all BaRs.

---

## Reputation

Grinding grids belonging to other factions or NPCs reduces reputation, matching the behaviour of manual grinding. Disable this with `DecreaseFactionReputationOnGrinding = false` in `ModSettings.xml`.

---

## Safe Zones & Shields

Grinding respects Safe Zones and Defence Shields:

- **Safe Zone** — grinding a target inside a Safe Zone is only allowed when the Safe Zone permits it, and even then only for your own, faction, or unowned blocks. Grinding enemy grids inside a zone you do not own is blocked.
- **Defence Shields** — grids protected by an active shield are skipped. If your *own* shield is active, the janitor will not grind grids outside the shield (preventing shield-abuse). Disable your shield first to grind external targets.

Both checks can be turned off via `SafeZoneCheckEnabled` and `ShieldCheckEnabled` in `ModSettings.xml`.

---

## Plugin Protection

Grids protected by server plugins (e.g. the `!protect` command from ALE PCU Transferrer for Torch), and grids the game itself marks indestructible, are detected and skipped. Use `!unprotect` on Torch to make the grid grindable again.

---

## Troubleshooting

<details>
<summary>The BaR is grinding my own grid.</summary>
<div>
<p>Two common causes:</p>
<ul>
<li>You painted a block with a color that matches the configured <strong>Grind Color</strong>. Either change your build color or change the Grind Color setting in the terminal.</li>
<li>Some blocks on your grid have been transferred to a neutral or enemy owner (a known issue with welder blocks acquiring different ownership after certain operations). Check ownership in the terminal info panel.</li>
</ul>
</div>
</details>

<details>
<summary>The BaR will not grind anything despite enabling Grind Janitor.</summary>
<div>
<p>Most often a config issue:</p>
<ul>
<li>The server may have <code>UseGrindJanitorFixed = true</code> in <code>ModSettings.xml</code>, locking the option off globally.</li>
<li>If you are running v2.5.2 or earlier with an empty <code>&lt;AllowedGrindJanitorRelations&gt;&lt;/AllowedGrindJanitorRelations&gt;</code> element, every BaR's janitor settings were wiped. Update to v2.5.3 or later — the loader auto-heals on first load.</li>
<li>Check that the targeted blocks actually fall into the selected ownership categories. Hostile-faction blocks owned by your own faction (after a hack) no longer count as <code>Enemies</code>.</li>
<li>Check that the block types are enabled in the <strong>Grind Priority</strong> list — disabled entries are never ground.</li>
<li>Is the target an unconnected grid while the BaR is in <strong>Walk mode</strong>? Switch to <strong>Fly mode</strong>.</li>
</ul>
</div>
</details>

<details>
<summary>Farthest-first grinding starts somewhere in the middle of a large ship.</summary>
<div>
This was a regression on grids of roughly 7000+ blocks introduced alongside a v2.5.0 performance optimization. Fixed in v2.5.3 — every qualifying block is now considered before the per-grid selection picks the best candidates. Update to v2.5.3 or later.
</div>
</details>

<details>
<summary>The BaR grinds, but the inventory fills up and grinding stops.</summary>
<div>
<p>Grinding pauses while the welder's own inventory is full. The BaR pushes ground items to connected Cargo Containers (ore and ingots can also land in Refineries) when one or more of the <strong>Push</strong> options are enabled. If all three Push options are disabled, items accumulate in the welder's own inventory until full — empty it manually or enable a Push option.</p>
<p>If all push targets are full, the BaR pauses pushing for about 15 seconds before retrying. Swapping or adding containers is picked up by the periodic inventory rescan (every 30 seconds).</p>
</div>
</details>

<details>
<summary>The Grind Color and Ignore Color HSV defaults look identical.</summary>
<div>
The defaults differ by 1 in the Value channel (<code>321, 100, 50</code> vs. <code>321, 100, 51</code>). This is intentional — they remain visually similar but distinct enough that a deliberate paint operation picks one. We recommend repainting your own grid with a clearly different shade for both to avoid mistakes.
</div>
</details>

<details>
<summary>I see a 1+ second frame spike on a world full of disabled BaRs.</summary>
<div>
A bug in the update scheduling made all isolated and disabled BaRs fire on the same tick. Fixed in v2.5.4 — on the affected world the maximum spike dropped from 1433 ms to ~15 ms.
</div>
</details>
