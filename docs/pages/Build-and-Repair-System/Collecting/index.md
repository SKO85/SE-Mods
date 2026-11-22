---
layout: default
title: Collecting
parent: Build and Repair System
nav_order: 7
---

# Collecting

The Build and Repair block can pick up floating objects within its work area and stow them in its inventory and connected containers.

---

## Fly Mode Required

Collecting only works when the BaR's **Search Mode** is set to **Fly mode**. Walk mode scans connected grids only and never looks for loose objects, so a BaR in Walk mode collects nothing — the Collect Priority list is even hidden from the terminal in Walk mode. See the Search Mode section on the [Welding](../Welding/#search-mode--walk-vs-fly) page.

## What Gets Collected

Inside the work area the BaR picks up:

- **Floating items** — loose components, ingots, ore, dropped tools, ammo, bottles, etc., as long as the item's type is enabled in the **Collect Priority** list.
- **Dead bodies with inventory** — the contents of dead engineers' and creatures' inventories are looted. Emptied wolf/spider corpses are then deleted when `DeleteBotsWhenDead` is enabled in `ModSettings.xml` (the default).
- **Dropped backpacks** — inventory bags left behind by deaths are emptied.

Up to 50 objects can be gathered in one collection sweep; the flying-nanobot effect then carries the haul back to the block. Collection pauses while the welder's inventory is full and resumes once items have been pushed out.

---

## When the BaR Collects

The BaR is *always* willing to collect floating objects — the **Collect only if idle** toggle in the terminal does not enable or disable collection, it controls *when* the collect pass runs in the work cycle:

| Toggle state | When collection runs |
|---|---|
| **Off (default)** | The collect pass runs at the start of every work cycle, *before* the weld / grind loop. The BaR keeps picking up floating objects in between — and alongside — its welding and grinding operations. This is the recommended setting for most setups. |
| **On** | The collect pass runs only after the weld / grind loop, and only if the BaR did neither this tick (nothing welded, nothing ground, no transport in progress). The BaR will **not** collect while it is busy welding or grinding — only when there is genuinely nothing else to do. |

So the toggle is best read literally as **"only collect when idle"** — turning it on *restricts* collection to idle moments; leaving it off (the default) allows collection to happen continuously alongside normal weld / grind work.

Per-block toggle in the terminal; locked server-wide via `CollectIfIdleFixed` in `ModSettings.xml`. The default for newly-placed blocks is set by `CollectIfIdleDefault` (default `false`).

### Turning Collection Off Completely

Collect only if idle is not the off-switch — even with it turned on, an idle BaR will still collect. To stop a BaR from collecting at all, **disable every entry in the Collect Priority list** in the terminal:

1. Open the BaR's terminal.
2. Find the **Collect Priority** list.
3. Click **Disable All** (or disable each entry individually).

With every entry disabled, the collect pass has nothing to consider and the BaR never picks up floating objects, regardless of the Collect only if idle setting. To suppress only a specific item type (for example, ignore ore but still collect components), disable just that entry instead. Switching the BaR to Walk mode also stops collecting, but changes what is scanned for welding and grinding too.

---

## Priority

The **Collect Priority** list in the terminal controls which item types are picked up first. Each entry can be enabled / disabled and reordered (`Priority Up` / `Priority Down` buttons). The system processes enabled entries from top to bottom. Locked server-wide via `CollectPriorityFixed`. There is no "Ignore priority order" toggle for collecting — that option exists only for grinding.

---

## Push Targets & Conveyor

Collected items are stored in the welder's own inventory and, when the corresponding **Push** option is enabled, forwarded over the conveyor network:

| Push toggle | Forwards |
|---|---|
| `Push components immediately` | Components only. |
| `Push ingot/ore immediately` | Ingots and ore only. |
| `Push items immediately` | Other items (tools, ammo, gas bottles, consumables, etc.). |

Each toggle is independent and can be locked server-wide via `PushComponentImmediatelyFixed`, `PushIngotOreImmediatelyFixed`, and `PushItemsImmediatelyFixed`.

Items are pushed into connected **Cargo Containers**; ore and ingots can also land in **Refineries**. Other conveyor blocks (connectors, sorters, assemblers, …) pass the items along but are not used as storage destinations.

When all push toggles are disabled, items remain in the welder's inventory. As of v2.5.4, no internal "safety" path can override this — disabling all three Push options reliably stops the welder→container flow.

### Component Sources

The reverse direction — pulling weld components *into* the BaR — uses Cargo Containers, Connectors, Conveyor Sorters, Assemblers, Ship Grinders, other Ship Welders, and Cryo Chambers on the conveyor network. Source and push-target lists are both refreshed by a periodic rescan, every 30 seconds.

---

## Push-Target-Full Pause

When all push targets are full the BaR pauses push attempts for about 15 seconds before retrying, to avoid hammering full inventories every tick. Freeing space in an existing container is therefore picked up within ~15 seconds; adding, removing, or swapping containers is noticed by the periodic inventory rescan (every 30 seconds).

### Newly Placed Cargo Takes a Moment to Be Detected

If you place a **brand-new** cargo container while the BaR is full and not pushing, it can feel as if pushing is broken — items keep piling up in the welder inventory and the new container never receives anything. This is not a bug.

The BaR maintains a list of source / push-target inventories on the conveyor network and only refreshes it on the periodic rescan. A new container is *not* in that list until the next rescan picks it up — usually within 30 seconds. Until then the new container is invisible to the BaR even though you can see it conveyor-connected.

What to do: wait up to half a minute after placing the new container. Once the rescan runs, the BaR recognises the new push target and the backlog drains automatically.

---

## Troubleshooting

<details>
<summary>Floating objects in range are not being collected.</summary>
<div>
<p>Check, in order:</p>
<ul>
<li>Is the BaR's <strong>Search Mode</strong> set to <strong>Fly mode</strong>? Walk mode never collects.</li>
<li>Is the BaR powered and enabled?</li>
<li>Is the floating object inside the visible work area? (<strong>Show Area</strong>.)</li>
<li>Is the relevant item type enabled in the <strong>Collect Priority</strong> list?</li>
<li>Is <strong>Collect only if idle</strong> turned on while the BaR is busy welding or grinding? With the toggle on, the BaR only collects when it has nothing else to do — so a BaR that always has weld / grind work will never reach the collect pass. Turn it off to collect continuously.</li>
<li>Is the welder inventory full and all push targets full? Check the custom info panel.</li>
</ul>
</div>
</details>

<details>
<summary>Items are being pushed to other inventories even though I disabled all the Push options.</summary>
<div>
This was a real bug in versions before v2.5.4 — a safety path could push items even when all three Push options were off. Fixed in v2.5.4. Update to v2.5.4 or later.
</div>
</details>

<details>
<summary>Items go into the welder inventory but never come back out.</summary>
<div>
<p>The welder inventory only forwards items when the corresponding <strong>Push</strong> option is enabled for that item type. Components, ingots / ore, and "other items" each have an independent toggle.</p>
<p>If the toggles are on but items still pile up, the conveyor connection to push targets may be broken — check that connectors are locked, sorters are not blocking the item type, and the destination cargo containers are not full.</p>
</div>
</details>

<details>
<summary>The BaR collects items even when I do not want it to.</summary>
<div>
Disable items individually in the <strong>Collect Priority</strong> list (each entry has an enable / disable button). Turning on Collect only if idle alone is not enough — items can still be collected while the BaR is otherwise idle.
</div>
</details>

<details>
<summary>The push-targets-full message persists after I added more storage.</summary>
<div>
<p>Push retries happen every ~15 seconds, and a brand-new container is only seen after the next inventory rescan (every 30 seconds). Give it up to half a minute; if items still do not move, check the conveyor path and sorter filters.</p>
</div>
</details>
