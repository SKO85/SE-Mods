---
layout: default
title: Collecting
parent: Build and Repair System
nav_order: 7
---

# Collecting

The Build and Repair block can pick up floating objects (loose components, ingots, ore, dropped items, etc.) within its work area and stow them in connected inventories.

---

## When the BaR Collects

The BaR is *always* willing to collect floating objects — the **Collect If Idle** toggle in the terminal does not enable or disable collection, it controls *when* the collect pass runs in the work cycle:

| Toggle state | When collection runs |
|---|---|
| **Off (default)** | The collect pass runs at the start of every work cycle, *before* the weld / grind loop. The BaR keeps picking up floating objects in between — and alongside — its welding and grinding operations. This is the recommended setting for most setups. |
| **On** | The collect pass runs only after the weld / grind loop, and only if the BaR did neither this tick (nothing welded, nothing ground, no transport in progress). The BaR will **not** collect while it is busy welding or grinding — only when there is genuinely nothing else to do. |

So "Collect If Idle" is best read as **"only collect when idle"** — turning it on *restricts* collection to idle frames; leaving it off (the default) allows collection to happen continuously alongside normal weld / grind work.

Per-block toggle in the terminal; locked server-wide via `CollectIfIdleFixed` in `ModSettings.xml`. The default for newly-placed blocks is set by `CollectIfIdleDefault` (default `false`).

### Turning Collection Off Completely

Collect If Idle is not the off-switch — even with it turned on, an idle BaR will still collect. To stop a BaR from collecting at all, **disable every entry in the Collect Priority List** in the terminal:

1. Open the BaR's terminal.
2. Find the **Collect Priority List**.
3. Click **Disable All** (or untick each entry individually).

With every entry disabled, the collect pass has nothing to consider and the BaR never picks up floating objects, regardless of whether Collect If Idle is on or off. To suppress only a specific item type (for example, ignore ore but still collect components), disable just that entry instead.

---

## Priority

The **Collect Priority List** in the terminal controls which item types are picked up first. Each entry can be enabled / disabled and reordered (`Up` / `Down` buttons). The system processes enabled entries from top to bottom. Locked server-wide via `CollectPriorityFixed`. There is no "Ignore Priority Order" toggle for collecting — that option exists only for grinding.

---

## Push Targets & Conveyor

Collected items are stored in the welder's own inventory and, when the corresponding **Push** option is enabled, immediately forwarded to connected push-target inventories over the conveyor network:

| Push toggle | Forwards |
|---|---|
| `Push Components Immediately` | Components only. |
| `Push Ingots/Ore Immediately` | Ingots and ore only. |
| `Push Items Immediately` | Other items (tools, ammo, gas bottles, consumables, etc.). |

Each toggle is independent and can be locked server-wide via `PushComponentImmediatelyFixed`, `PushIngotOreImmediatelyFixed`, and `PushItemsImmediatelyFixed`.

When all push toggles are disabled, items remain in the welder's inventory. As of v2.5.4 (BUG-114), no internal "safety" path can override this — disabling all three Push options now reliably stops the welder→push-target flow.

### Supported Block Types

Both source and push-target scans look for the same set of block types on the conveyor network:

- Cargo Containers
- Connectors
- Conveyor Sorters
- Assemblers
- Refineries
- Ship Grinders
- Ship Welders (other than other Build and Repair blocks — to prevent circular transfers)
- Cryo Chambers

Push-target and source inventories are rescanned every 30 seconds.

---

## Push-Target-Full Backoff

When all push targets are full the BaR enters a backoff for up to 60 seconds before retrying push operations, to avoid hammering full inventories every tick. Same-size container swaps (e.g. swapping out a full Small Cargo for an empty Small Cargo) clear the backoff immediately (BUG-090, v2.5.2).

### Newly Placed Cargo Takes a Moment to Be Detected

If you place a **brand-new** cargo container or other push target while the BaR is full and not pushing, it can feel as if pushing is broken — items keep piling up in the welder inventory and the new container never receives anything. This is not a bug.

The BaR maintains a list of source / push-target inventories on the conveyor network and only refreshes it on a periodic scan. A new container is *not* in that list until the next scan picks it up, which can take **up to 60 seconds** in the worst case. Until then the new container is invisible to the BaR even though you can see it conveyor-connected.

What to do: be patient for up to a minute after placing the new container. Once the next scan runs, the BaR will recognise the new push target and the backlog drains automatically. If you cannot wait, swapping a same-size full container for an empty one (instead of placing a new one) clears the backoff immediately because the existing container is already in the scanned list.

---

## Troubleshooting

<details>
<summary>Floating objects in range are not being collected.</summary>
<div>
<p>Check, in order:</p>
<ul>
<li>Is the BaR powered and enabled?</li>
<li>Is the floating object inside the visible work area? (<strong>Show Work Area</strong>.)</li>
<li>Is the relevant item type enabled in the <strong>Collect Priority List</strong>?</li>
<li>Is <strong>Collect If Idle</strong> turned on while the BaR is busy welding or grinding? With Collect If Idle on, the BaR only collects when it has nothing else to do — so a BaR that always has weld / grind work will never reach the collect pass. Turn it off to collect continuously.</li>
<li>Is the welder inventory full and all push targets full? Check the custom info panel.</li>
</ul>
</div>
</details>

<details>
<summary>Items are being pushed to other inventories even though I disabled all the Push options.</summary>
<div>
This was a real bug (BUG-114) in versions before v2.5.4 — a safety path could push items even when all three Push options were off. Fixed in v2.5.4. Update to v2.5.4 or later.
</div>
</details>

<details>
<summary>Items go into the welder inventory but never come back out.</summary>
<div>
<p>The welder inventory only forwards items when the corresponding <strong>Push</strong> option is enabled for that item type. Components, ingots / ore, and "other items" each have an independent toggle.</p>
<p>If the toggles are on but items still pile up, the conveyor connection to push targets may be broken — check that connectors are locked, sorters are not blocking the item type, and the destination inventories are not full.</p>
</div>
</details>

<details>
<summary>The BaR collects items even when I do not want it to.</summary>
<div>
Disable items individually in the <strong>Collect Priority List</strong> (each entry has an enable / disable button). Disabling Collect If Idle alone is not enough — items can still be collected during active welding / grinding.
</div>
</details>

<details>
<summary>The push-targets-full message persists after I added more storage.</summary>
<div>
<p>The backoff lasts up to 60 seconds. Swapping a full container for an empty one of the same size clears it immediately (v2.5.2+). Adding a brand-new container takes effect when the source / push-target rescan completes (every 30 seconds).</p>
</div>
</details>
