---
layout: default
title: Grinding
parent: Build and Repair System
nav_order: 6
---

# Grinding

The Build and Repair block can grind enemy, neutral, color-tagged, or hand-picked blocks within its work area. This page describes how grinding decisions are made and how to troubleshoot common issues.

---

## Search Mode — Walk vs. Fly

Before anything else, the BaR's **Search Mode** decides *which grids* it is willing to scan for grind targets. This is the upstream gate every other grinding rule depends on — Grind Color, Grind Janitor, and the priority list all only operate on grids that the search has actually returned. Pick the wrong mode and the rest of your settings have nothing to act on.

The terminal **Search Mode** dropdown offers two modes:

| Mode | Internal name | What gets scanned |
|---|---|---|
| **Walk mode** (default) | `Grids` | Only the grid the BaR is mounted on and its connected sub-grids (via connectors, pistons, rotors, mergers). Unconnected hostile or wreckage grids in the work area are ignored. |
| **Fly mode** | `BoundingBox` | Same as Walk mode, *plus* every other grid whose blocks fall inside the BaR's work area — including unconnected enemy ships, drifting wreckage, NPC grids, and salvage targets that are not docked to your station. |

For grinding the choice matters more than for welding, because grinding is the operation most often used on grids the BaR is *not* attached to. Some common patterns:

- **Salvage / wreckage cleanup** — the grinding target is a free-floating piece of wreckage near a station. Walk mode will not see it (no connector lock); switch the BaR to **Fly mode** so the bounding-box scan picks it up.
- **Auto-disposal of NPC drops** — pirate drones, wolves' scrap, encounter wrecks. These are unconnected to your base. **Fly mode** with the Grind Janitor set to `No Ownership` and `Enemies` automates the cleanup.
- **Painting your own grid red for selective grinding** — you only need **Walk mode** for this. The grind targets are blocks on your *own* connected grid, which Walk already covers.
- **Ship docked at a connector but not yet locked** — a grid that is "near" but technically unconnected (e.g. mid-docking-approach) is invisible in Walk mode. Either complete the connector lock so it joins the logical grid group, or switch to Fly mode for a scan that catches it regardless.

Things to keep in mind when using Fly mode for grinding:

- **Reputation**: grinding a faction-owned grid you happen to fly past still triggers `DecreaseFactionReputationOnGrinding`. Fly mode makes it easy to pick up unintended faction grids.
- **Safe Zones, shields, plugin protection** (`!protect`) all still apply — Fly mode does not bypass them. A protected grid in your work area is still skipped.
- **Per-grid limit**: every grid Fly mode pulls in counts against `MaxSystemsPerTargetGrid`. With several BaRs in Fly mode covering the same area you can saturate a grid faster than expected.
- **Scan cost** scales with the number of grids inside the work area. A wide work area in Fly mode in a busy region (asteroid base, NPC pirate hub) is the most expensive configuration. Tighten the work area or switch to Walk if scan cost gets out of hand.

Server admins can hide either mode globally via `AllowedSearchModes` in `ModSettings.xml`. The default for newly-placed blocks is set by `SearchModeDefault`.

---

## When the BaR Grinds

Once Search Mode has produced the candidate set, a block in that set is considered for grinding when **any** of the following is true:

- It is painted with the configured **Grind Color**, and the **Use Grind Color** option is enabled on this block.
- It belongs to an ownership category enabled in the **Grind Janitor** (`No Ownership`, `Neutral`, or `Enemies`).
- It is added to the priority list and the BaR is in `Grind Only` or `Grind Before Weld` mode.

The combined target list is sorted by the priority list, then by distance (configurable via `Grind Near First` / `Far First` / `Smallest Grid First`).

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
| `Neutral` | Blocks owned by neutral players or factions. |
| `Enemies` | Blocks owned by enemy players or factions. |

The available categories are filtered by `AllowedGrindJanitorRelations` in `ModSettings.xml` — admins can hide categories from the terminal.

The **Grind Janitor Options** sub-flags adjust how far each block is ground:

| Flag | Behaviour |
|---|---|
| _(none)_ | Grind blocks all the way down (full grind). |
| `Disable Only` | Grind only until the block becomes non-functional. |
| `Hack Only` | Grind only until the block becomes hackable, then stop. |

Locked server-wide via `UseGrindJanitorFixed`.

> **Note (BUG-093, fixed in v2.5.3):** if `AllowedGrindJanitorRelations` was empty in `ModSettings.xml`, the loader would silently disable janitor grinding on every BaR. The settings loader now auto-heals an empty value to `NoOwnership | Enemies | Neutral` regardless of the file's version. To genuinely disable janitor grinding for all BaRs, set `UseGrindJanitorFixed = true` with `UseGrindJanitorDefault = None` instead.

---

## Work Modes

The terminal **Work Mode** dropdown controls the order in which welding and grinding are tackled:

| Mode | Behaviour |
|---|---|
| `Weld Before Grind` | Weld first; if no weldable target is available, grind. |
| `Grind Before Weld` | Grind first; if no grind target is available, weld. |
| `Weld Only` | Welding only. |
| `Grind Only` | Grinding only. |

The legacy `Grind If Weld Get Stuck` mode was removed in v2.5.4. Worlds that previously had a BaR set to it are silently migrated to `Weld Before Grind`.

---

## Lock-On Behaviour

Like welding, grinding locks on to its current block (`CurrentGrindingBlock`) until the block is razed or becomes invalid. Lock-on uses `CubeGrid.EntityId + Position` so re-scans that produce new `IMySlimBlock` references for the same physical block keep the lock.

If the locked block disappears (grid deleted, projector update reassigns IDs), the lock is cleared and the loop finds a new target on the same tick.

---

## Speed

Grinding speed is controlled by:

- `GrindingMultiplier` (`ModSettings.xml`) — per-tick grind amount. `1` = default, `2` = double, `0.5` = half. Range: 0.1–100.
- `WorkSpeed` (`ModSettings.xml`) — how often grinding ticks fire. `1` = roughly every 1.67 s, `10` = roughly every 0.17 s. Range: 1–10.
- `MaxGrindsPerTick` (`ModSettings.xml`) — global cap on grind operations per tick across all BaRs. `0` (auto) scales with BaR count (min 5, max 10).

---

## Reputation

Grinding grids belonging to other factions or NPCs reduces reputation, matching the behaviour of manual grinding. Disable this with `DecreaseFactionReputationOnGrinding = false` in `ModSettings.xml`.

---

## Safe Zones & Shields

Grinding respects Safe Zones and Defence Shields:

- **Safe Zone** — grinding a target inside a Safe Zone is only allowed when the Safe Zone permits it. Grinding enemy grids inside a zone you do not own is always blocked.
- **Defence Shields** — grids protected by an active shield are skipped. If your *own* shield is active, you cannot grind grids outside the shield (preventing shield-abuse). Disable your shield first to grind external targets.

Both checks can be turned off via `SafeZoneCheckEnabled` and `ShieldCheckEnabled` in `ModSettings.xml`.

---

## Plugin Protection

Grids protected by server plugins (e.g. the `!protect` command from ALE PCU Transferrer for Torch) are detected and skipped. Use `!unprotect` on Torch to make the grid grindable again.

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
<li>If you are running v2.5.2 or earlier with an empty <code>&lt;AllowedGrindJanitorRelations&gt;&lt;/AllowedGrindJanitorRelations&gt;</code> element, every BaR's janitor mask was zeroed (BUG-093). Update to v2.5.3 or later — the loader auto-heals on first load.</li>
<li>Check that the targeted blocks actually fall into the selected ownership categories. Hostile-faction blocks owned by your own faction (after a hack) no longer count as <code>Enemies</code>.</li>
</ul>
</div>
</details>

<details>
<summary>Farthest-first grinding starts somewhere in the middle of a large ship.</summary>
<div>
This was a regression on grids of roughly 7000+ blocks (BUG-094) introduced alongside a v2.5.0 performance optimization. Fixed in v2.5.3 — every qualifying block is now considered before the per-grid sort-and-cap selects the true top-N. Update to v2.5.3 or later.
</div>
</details>

<details>
<summary>The BaR grinds, but the inventory fills up and grinding stops.</summary>
<div>
<p>The BaR pushes ground items to connected push-target inventories (Cargo Containers, Connectors, Sorters, etc. on the conveyor network) when one or more of the <strong>Push</strong> options are enabled. If all three Push options are disabled, items accumulate in the welder's own inventory until full.</p>
<p>If push targets are full, the BaR enters a "push-targets-full" backoff for up to 60 seconds before retrying. Same-size container swaps clear this backoff immediately (fixed in v2.5.2 — BUG-090).</p>
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
A bug in the per-BaR stagger formula made all isolated and disabled BaRs fire on the same tick (BUG-102). Fixed in v2.5.4 — stagger now correctly accounts for both isolated single-BaR systems and disabled BaRs. On the affected world the maximum spike dropped from 1433 ms to ~15 ms.
</div>
</details>
