---
layout: default
title: Welding
parent: Build and Repair System
nav_order: 5
---

# Welding

The Build and Repair block welds and builds projected blocks within its work area. This page describes how welding works, what controls its speed and order, and how to troubleshoot common issues.

---

## Search Mode — Walk vs. Fly

Before anything else, the BaR's **Search Mode** decides *which grids* it is willing to scan for weld targets. This is the upstream gate every other welding rule depends on — if a block is not visible to the scan, no weld mode, priority list, or color filter can pull it in.

The terminal **Search Mode** dropdown offers two modes; pick the one that matches what you want welded:

| Mode | Internal name | What gets scanned |
|---|---|---|
| **Walk mode** (default) | `Grids` | Only the grid the BaR is mounted on, plus any sub-grids connected via connectors, pistons, rotors, or mergers (anything in the same logical grid group). Free-floating ships nearby are ignored. |
| **Fly mode** | `BoundingBox` | Same as Walk mode, *plus* every other grid whose blocks fall inside the BaR's work area — even unconnected ships, enemy grids, projected grids on a separate vehicle, or NPC drops drifting past. |

The default is **Walk** because it is what most players want: a base-mounted BaR that repairs *its own* base and the docked ships connected to it, but ignores hostile or unrelated grids that happen to fly through. **Fly** is for repair platforms that should weld whatever shows up in front of them — projector pads, repair docks, station-mounted welders that service ships parked nearby without a connector lock, multi-grid projector builds that span multiple unconnected vehicles.

Things to keep in mind when switching to Fly:

- The work area's **size and offset** become much more important — Fly mode scans every grid intersecting the box, so a wide work area can pull in a lot of grids at once and grow scan cost.
- If you have ownership conflicts in range (your own ship docked next to a salvaged hostile grid), Fly mode will see *both*. Use the priority list, weld mode, or move the work area to scope what gets welded.
- Fly mode does not bypass safety checks — Safe Zones, shields, plugin protection (`!protect`), and `MaxSystemsPerTargetGrid` still apply per scanned grid.

Server admins can hide either mode globally via `AllowedSearchModes` in `ModSettings.xml`. The default for newly-placed blocks is set by `SearchModeDefault`.

---

## Weld Modes

The terminal **Weld Mode** dropdown controls how far each block is welded:

| Mode | Behaviour |
|---|---|
| `Weld to Full` | Welds blocks all the way to 100% integrity. The default. |
| `Weld to Functional` | Welds blocks until they become functional (lights on, doors work, thrusters fire). Saves components and time when full integrity is not needed. |
| `Weld to Skeleton` | Only places projected blocks (the first component). Does not weld or repair existing blocks. Pair with **Build Projections** to rapidly stub out a projection, then switch to a full weld mode to finish. |

There is no server-side override for the Weld Mode — it is always controlled per block in the terminal.

---

## Build Projections

The **Build Projections** toggle controls whether projected blocks may be built by this BaR. When disabled, the BaR only repairs existing blocks — projector blueprints are ignored. The default is enabled.

> **Renamed from "Allow Build."** The terminal label was changed to **Build Projections** to make its purpose obvious at a glance — the previous "Allow Build" wording was easily misread as a master on/off for the whole system. The behaviour is identical; only the label changed.

Server-side, the corresponding setting is `AllowBuildDefault` (default for newly-placed BaRs) and `AllowBuildFixed` (locks the toggle so players cannot change it from the terminal). Both live in `ModSettings.xml`. The internal flag name is still `AllowBuild` — that name is what the scripting API exposes (`BuildAndRepair.AllowBuild`) and what older docs may refer to.

---

## Sort Order

Welding targets are sorted before the loop runs. Order is controlled by the **Weld Priority** list in the terminal: each block class (Armor, Thrusters, Reactors, …) is processed in list order, top to bottom. Use the `Up` / `Down` buttons to reorder, and the per-entry checkbox to enable or disable a class. The list can be locked server-wide via `PriorityFixed`.

There is **no "Ignore Priority Order"** option for welding — the toggle in the terminal applies only to grinding. Welding always uses the priority list when picking the next target.

The native **Help Others** checkbox is hidden in the terminal and forced off internally — the option is not used by this mod. Multiple BaRs sharing one target is handled by the assignment system below, not by Help Others.

---

## Color Filter (Ignore Color)

The **Use Ignore Color** terminal toggle, with its associated HSV color picker, lets you mark blocks the BaR must skip. When enabled, any block painted with the configured Ignore Color HSV is excluded from the weld list and never welded — useful when you want a specific block to remain unfinished or partially ground for a paint scheme.

| Setting | Default | Description |
|---|---|---|
| `UseIgnoreColorDefault` | `true` | Default state of the per-block toggle. |
| `IgnoreColorDefault` | `321, 100, 51` | Default HSV (close to but distinct from the default Grind Color). |
| `UseIgnoreColorFixed` | `false` | When `true`, the toggle is locked server-wide. |

The companion **Use Grind Color** is the inverse direction: blocks painted with the Grind Color are routed to the *grind* list, not skipped — see the [Grinding](../Grinding/) page. Because the routing happens at scan time, the welder never sees Grind-Color blocks at all.

---

## Block Assignment

When more than one BaR can reach the same target block, the **assignment system** stops them all converging on the same block at once. As a BaR picks a block to weld it claims a reservation through `BlockSystemAssigningHandler`, keyed by `GridEntityId:Position` and held for `AssignmentTtlSeconds` (default `8 s`). Other BaRs treat reserved blocks as if they were already being worked and pass them by, so neighbouring BaRs spread out across the available targets instead of bunching up.

The reservation is released when the BaR finishes the block, when it abandons the lock-on, or when the TTL expires (whichever comes first), so a disconnected or destroyed BaR's claims free up automatically within a few seconds.

| Setting | Default | Effect |
|---|---|---|
| `AssignToSystemEnabled` | `true` | Master switch. Set to `false` to disable assignment server-wide. |
| `AssignmentTtlSeconds` | `8` | How long a reservation is held. Lower = faster recycling. Range 2–30. |

The assignment system runs alongside `MaxSystemsPerTargetGrid`, the per-grid cap that limits how many BaRs may target the *same grid* at once. Both are server-wide knobs; see the [Configuration File](../Config/general-settings#system-limits) page for the full picture.

---

## Lock-On Behaviour

When the BaR starts welding a block it "locks on" to that block until the block is welded to the chosen Weld Mode (or becomes invalid). Lock-on is identified by `CubeGrid.EntityId + Position`, so the same physical block is recognised across rescans even if its `IMySlimBlock` reference changes (which happens after every background scan).

If a locked-on block becomes temporarily unweldable due to a missing component, the lock-on is preserved — the BaR resumes the same block as soon as components arrive. If the block disappears from the target list entirely (for example after a projector update reassigns grid IDs), the stale lock-on is cleared and the BaR finds a new target on the same tick.

---

## Speed

Welding speed is controlled by two independent settings:

- `WeldingMultiplier` (`ModSettings.xml`) — per-tick weld amount. `1` = default, `2` = double, `0.5` = half. Range: 0.1–100.
- `WorkSpeed` (`ModSettings.xml`) — how often welding ticks fire. `1` = roughly every 1.67 s, `10` = roughly every 0.17 s. Range: 1–10.

Effective speed is `WorkSpeed × WeldingMultiplier`. See [Welder Settings → Update Speed](../Config/welder-settings#update-speed) for the full table.

The transport timer no longer gates welding (BUG-103, fixed in v2.5.4) — the previously-required cosmetic transport "trip" is decoupled from the weld loop. Welding now runs at full pace.

---

## Components

Components are pulled on demand from connected source inventories (Cargo Containers, Connectors, Sorters, Assemblers, Refineries, Ship Grinders, Ship Welders other than other BaRs, Cryo Chambers). Source inventories are rescanned every 30 seconds. Items are picked up synchronously when the welder needs them — no queueing or pre-staging is required.

In Creative mode the BaR welds and builds without consuming components, matching the original mod's behaviour.

---

## Performance & Safeguards

Welding includes several safeguards to keep BaRs from compounding into frame spikes:

- **Component starvation early-exit** — the welding loop breaks after 3 consecutive blocks that cannot be welded due to missing components, so when the world is component-starved BaRs do not iterate every target every tick.
- **Block fail cooldown** — when a block fails to weld (no components, projector exception, etc.) it is placed on a global cooldown. Other BaRs and this BaR's later ticks skip the cooldowned block. Tuned via `BlockFailureCooldownSeconds` (default `4` seconds, `0` disables).
- **Per-tick weld budget** — `MaxWeldsPerTick` caps the global number of weld operations per tick. `0` (auto) scales with BaR count.
- **Per-grid system limit** — `MaxSystemsPerTargetGrid` caps how many BaRs may weld the same target grid simultaneously. Prevents many BaRs piling onto one grid while neighbours are ignored.
- **Cluster scan coordinator** — co-located BaRs share a single scan cycle, eliminating redundant scanning across the cluster.

---

## Projector Cold-Start

A fresh projection takes a moment to "ramp up." This is intentional — and worth understanding so you do not mistake it for the BaR being broken.

When a projector first activates with a blueprint, the projected grid contains only the **first** block (typically the armour block the projector itself is anchored to). At the moment the BaR runs its next scan, that single block is the *entire* grid target. The BaR welds it. Once that first block is built, the next ring of blocks around it appears in the projection — those new blocks become visible to the BaR on the **following** scan, and the BaR welds one of them, then the next, and so on. Each completed block reveals the next batch of buildable neighbours, and the weld pace visibly accelerates.

So a fresh projection looks like this:

1. **First scan after projector activation** — one block visible. BaR welds it.
2. **Next scan** — a handful of new blocks visible. BaR welds one, then the next.
3. **Subsequent scans** — many blocks visible at once. BaR works at full pace.

This stair-step pattern is how Space Engineers' projector exposes blocks to the build pipeline: a block has to be *buildable* before any tool (hand welder, ship welder, BaR) can see it, and only the first block plus the immediate neighbours of already-built blocks are buildable at any moment. The BaR cannot "look ahead" at the rest of the blueprint.

As of v2.5.4 (FEAT-077), the *first* of those scans now happens within ~1–2 seconds of the projector activating instead of waiting up to 20 seconds for the empty-grid backoff (`EmptyGridRescanDelaySeconds`) to expire. After that, the cadence between scans is the normal background-scan interval.

> **Be patient on a fresh projection.** The first block or two will look slow. Once the projection grows past a handful of blocks, the BaR has plenty to work on and welding speed catches up to what you would see on an existing damaged grid.

This behaviour is intentional in the current mod — improving the "look-ahead" so a BaR pre-fetches the next ring of buildable blocks may be revisited in a future release.

---

## Troubleshooting

<details>
<summary>The BaR is enabled and powered but never welds anything.</summary>
<div>
<p>Work through this checklist:</p>
<ul>
<li>Is <strong>Build Projections</strong> on (for projections) and the <strong>Weld Mode</strong> set correctly?</li>
<li>Is the inventory full, or are required components missing? The custom info panel lists missing components.</li>
<li>Is the target inside a <strong>Safe Zone</strong> that does not allow welding?</li>
<li>Is the target protected by an active <strong>Defence Shield</strong>?</li>
<li>Is the BaR's <strong>work area</strong> actually covering the target? Toggle <strong>Show Work Area</strong> to verify.</li>
<li>Is the target an a grid currently at the <code>MaxSystemsPerTargetGrid</code> limit? Other nearby BaRs may already be saturating it.</li>
<li>Are you on a server running <strong>BuildLimiter</strong> or a similar Torch plugin? These silently block welds without an error.</li>
</ul>
</div>
</details>

<details>
<summary>The BaR will not weld a specific block, but welds everything around it.</summary>
<div>
<ul>
<li>Try welding the same block manually with your hand torch. If that fails too, something is physically obstructing the block (a wheel, a misaligned subgrid component) or the block requires a DLC the projector did not allow at projection time.</li>
<li>If you are using the <strong>Multi-Grid-Projector</strong> plugin, the plugin patches game API calls and can prevent welding of certain blocks. Try without the plugin to confirm.</li>
<li>The block may be on the global fail cooldown (<code>BlockFailureCooldownSeconds</code>). Wait a few seconds and try again, or set the value to <code>0</code> to disable the feature.</li>
</ul>
</div>
</details>

<details>
<summary>Welding feels slower than it used to (~15-20% duty cycle).</summary>
<div>
This was a real bug (BUG-103) in versions before v2.5.4 — the cosmetic transport timer was gating real weld work. Update to v2.5.4 or later for a 3–4× speedup on long-running welds.
</div>
</details>

<details>
<summary>Welding is stuck on a single projected block in offline mode.</summary>
<div>
<p>In offline mode (no Steam connection), the engine's DLC entitlement table is empty and projecting certain blocks (commonly DLC armour variants) caused <code>proj.Build()</code> to throw an internal exception, leaving the BaR locked on the broken block forever (BUG-115).</p>
<p>Fixed in v2.5.4 — the BaR now catches the engine exception, marks the block as broken (skipped permanently for this session), and keeps welding the rest. A diagnostic warning is logged once per broken block per session.</p>
</div>
</details>

<details>
<summary>Welding speed multipliers above 10 do nothing.</summary>
<div>
Before v2.5.0, multipliers above 10 also changed the update frequency. Multipliers now only affect the per-tick amount; use <code>WorkSpeed</code> (1–10) for update frequency. Existing worlds with a multiplier above 10 are auto-migrated to <code>WorkSpeed = 10</code> on first load.
</div>
</details>

<details>
<summary>The lock-on does not release when I change a terminal setting.</summary>
<div>
Lock-on is preserved until the current block is finished, even if you toggle terminal options that change the sort order or the priority list. This is intentional — interrupting a partially-welded block leaves it as scrap. The new sort order applies to the next block the BaR picks.
</div>
</details>

<details>
<summary>I cannot find the "Help Others" checkbox in the terminal.</summary>
<div>
It is intentionally hidden. The native "Help Others" option is not used by this mod — multiple BaRs working the same area are coordinated through the <a href="#block-assignment">block assignment system</a> instead. There is nothing to toggle.
</div>
</details>

<details>
<summary>Two BaRs in range of the same block — only one is welding it.</summary>
<div>
That is the assignment system at work: as soon as one BaR claims the block, others reserve it as taken for <code>AssignmentTtlSeconds</code> (default 8 s) and look for a different target. To force every BaR to focus on the same target, disable the system server-wide with <code>AssignToSystemEnabled = false</code> in <code>ModSettings.xml</code>. To loosen reservations more aggressively when BaRs come and go (without disabling the system), lower <code>AssignmentTtlSeconds</code>.
</div>
</details>
