---
layout: default
title: Other Features
parent: Build and Repair System
nav_order: 9
has_children: true
---

# Other Features

Smaller features and behaviours that do not fit cleanly into Welding, Grinding, Collecting, or Effects & Sound. Most of them are server-side knobs that affect every Build and Repair block on the server.

The [Cluster Coordinator](Cluster-Coordinator/) — the automatic scan-sharing mechanism for co-located BaRs — is documented as a sub-page under this section because it sits in the same "set-and-forget background behaviour" category as the items below.

---

## Range & Work Area

The BaR's **work area** is the volume it scans for weld, grind, and collect targets. It is the same volume for all three operations — there is no separate "weld range" or "collect range." Each block configures its work area independently in the terminal:

- **Show Work Area** — toggles an in-world bounding box overlay so you can see exactly what the BaR can reach. Locked server-wide via `ShowAreaFixed`.
- **Area size sliders (X / Y / Z)** — set the dimensions of the work area in metres. Locked server-wide via `AreaSizeFixed`.
- **Area offset controls** — shift the work area away from the BaR's position so the BaR can cover an area to the side, above, or below itself. Locked server-wide via `AreaOffsetFixed`.

The size and offset are both bounded by server-wide caps:

| Setting | Default | Range | Description |
|---|---|---|---|
| `Range` | `100` | 2–2000 | Maximum half-extent of the work area in metres. The cap that the X / Y / Z sliders are scaled against. |
| `MaximumOffset` | `200` | 0–2000 | Maximum distance the work area can be shifted from the BaR block. |

Both are configured in `ModSettings.xml` (see [General Settings](../Config/general-settings#range--offset)).

If the BaR seems to ignore a target you expect it to handle, the first thing to check is **Show Work Area** — a wrong size or offset is the single most common cause of "the BaR is not doing anything."

---

## Bot Bodies

When `DeleteBotsWhenDead = true` in `ModSettings.xml` (the default), NPC bot bodies (wolves, spiders, encounter-spawned engineers) are deleted on death rather than left as floating-object clutter that the BaR or other welders would otherwise need to clean up. Set to `false` if you prefer to keep the bodies — they will then accumulate in the world as ordinary corpses subject to normal collection rules.

This is a server-side behaviour, independent of any specific BaR's collection or grinding settings — the deletion happens at the engine level on bot death, not as part of a BaR scan.

---

## Friendly Damage Tracking

The BaR tries to avoid grinding blocks that were *just damaged* by friendly weapons fire — the small window where you and a faction-mate are working on the same target and a stray hit lands on the block you are repairing. A short-lived record is kept of every friendly-damage event so the grind loop can skip blocks that are still in the friendly-damage window.

The window and cleanup interval are configurable:

| Setting | Default | Description |
|---|---|---|
| `FriendlyDamageTimeoutTicks` | `600000000` (60 s) | How long a friendly-damage record is kept, in TimeSpan ticks (1 tick = 100 ns). |
| `FriendlyDamageCleanupTicks` | `100000000` (10 s) | Interval between friendly-damage record cleanup passes. |

Both are server-wide settings in `ModSettings.xml`. The defaults are sensible for most servers — increase the timeout if grid-on-grid combat is common in your world and you are seeing friendly grids getting ground after combat ends.

---

## Faction Reputation

Grinding grids belonging to other factions or NPCs reduces reputation, mirroring the engine's behaviour when you grind such a grid by hand. This is on by default to keep automated grinding from being a "free" alternative to manual grinding for reputation-grinding farms.

| Setting | Default | Description |
|---|---|---|
| `DecreaseFactionReputationOnGrinding` | `true` | When `true`, BaR grinds against other factions or NPCs incur the same reputation penalty as manual grinding. Set to `false` to disable. |

Note that this only affects *reputation*; ownership rules (Grind Janitor relations, Safe Zone permissions, plugin protection) are evaluated independently and are not changed by this setting.

---

## Sim-Speed Adaptive Throttling

When the server's simulation speed drops below 1.0, the BaR system automatically throttles its work to help the server recover instead of compounding the problem. The throttle scales with how far below 1.0 the sim-speed has fallen and lifts automatically as soon as sim-speed returns to normal — admins do not need to flip a switch when the server is under load.

For testing the throttle behaviour, admins can override the perceived sim-speed at runtime via `/nanobars sim` — see [Debug & Diagnostics → Sim-Speed Override](../Debug-and-Diagnostics/#sim-speed-override).

---

## Localisation

The mod ships translated terminal text for **English**, **German**, **Polish**, and **Russian**. The active language follows the player's game language setting; languages other than these four fall back to English.

| Setting | Default | Description |
|---|---|---|
| `DisableLocalization` | `false` | When `true`, force English regardless of the player's language setting. Useful on shared servers where mixed languages in chat / logs cause confusion. |

---

## Reset All Settings

Each BaR's terminal has a **Reset All Settings** button that restores every per-block setting on that BaR to its default. This includes work mode, weld mode, search mode, color filters, push toggles, the Collect-If-Idle toggle, sound volume, area size and offset, and the priority lists.

It does not reset *server-wide* settings (anything in `ModSettings.xml`); only the per-block flags this BaR carries. Server admins keep control via the `*Fixed` flags — fields that are server-locked stay locked after a reset.

---

## Live Configuration

Most server-wide settings can be changed at runtime without restarting the world. See [Chat Commands → Configuration](../Chat-Commands/#configuration) for the full list:

- `/nanobars config list` — list every setting and its current value
- `/nanobars config get <setting>` — fetch one value
- `/nanobars config set <setting> <value>` — change a value (effective immediately for most settings)
- `/nanobars config save` — persist current values to `ModSettings.xml`
- `/nanobars config reload` — re-read `ModSettings.xml`
- `/nanobars config reset` — reset all settings to defaults

A handful of settings (range, power, welder-specific values) still require a session restart to take effect even when changed at runtime.

---

## Diagnostics & Profiling

For troubleshooting tools — `DebugMode`, the in-world Debug HUD, the built-in profiler, the `BuildId`, the sim-speed override, and the mod-integration check — see the dedicated [Debug & Diagnostics](../Debug-and-Diagnostics/) page.