---
layout: default
title: 'Release Notes – v2.5.5'
parent: Release Notes
grand_parent: Build and Repair System
nav_order: 0
---

# Release Notes – v2.5.5

- Status: **Released** — May 2026
- Notes: Bug fix release. Resolves two scenarios where a Build and Repair system could go silently idle — once after the welder briefly hit capacity, and once after downstream cargo briefly filled up. Also halves the default target-scan interval (10 s → 5 s) so new work is picked up twice as fast, and lightens the **Show Area** overlay box so you can see through it.

---

## Bug Fixes

### Build and Repair Stuck "Inventory Full" After Welder Drains

A Build and Repair system that filled its onboard inventory to capacity could remain stuck thinking it was full **even after** the items had been pushed out to a connected cargo container and the inventory was actually empty. The block sat idle with no welding, grinding, or collecting — and a world reload didn't help, because the reload path also only knew how to set the flag, never clear it.

**Root cause:** the "inventory full" flag had a one-way state machine. It was set to **true** when the welder hit max capacity, but the only path that cleared it required an active transport cycle to also be running. When auto-push (Push Components / Push Ore / Push Items Immediately) drained the welder while the BaR was otherwise idle, no transport cycle existed, so the flag stayed armed forever — and all work paths (weld, grind, collect, idle fast-path) were gated off behind it.

**Fix:** the flag is now bidirectional with hysteresis. It sets at ≥100 % capacity (unchanged) and clears once the welder drains below 90 %. The 10 % hysteresis prevents flicker when a partial weld nudges the inventory just under max.

**Symptoms this resolves:**

- A BaR that was working fine, briefly filled up, the player added a cargo container, and the BaR never resumed.
- A BaR that says "Block inventory is full!" in the terminal info panel while its inventory is visibly empty.
- An entire fleet going silently idle after a busy grinding session, even after the cargo network is drained.

If you were working around this by toggling the BaR off and on, you no longer need to.

### Push Backoff Stuck for Up to a Minute After Freeing Cargo Space

When all downstream cargo containers were full and a Build and Repair system's push attempt moved zero items, the BaR set an internal "push targets are full" flag and stopped retrying. The flag only cleared on the next 30-second source rescan, and only if either a container had been added/removed **or** a 60-second safety backoff had expired.

If you instead made room **inside an existing container** (manually moved items, an Assembler consumed components, a refinery output was hauled away), neither condition fired and the BaR sat with a full welder for up to a full minute before trying to push again.

**Fix:** the safety backoff is shortened from 60 seconds to 15 seconds. The signature-based fast path (container added/removed) is unchanged — that still triggers an immediate retry. Combined with the inventory-full fix above, a BaR now recovers in seconds instead of minutes after you clear downstream cargo.

### Faster Target-Scan Cadence

The default target-scan interval — how often a Build and Repair system rebuilds its list of weld / grind targets — has been halved from **10 seconds to 5 seconds**. The idle-backoff interval (used after several consecutive empty scans) has been halved from **20 seconds to 10 seconds**.

**What changes for players:**

- A newly-pasted, newly-projected, or newly-damaged grid is picked up in roughly half the time.
- The lag between toggling a terminal setting (priority, work mode, area size) and the next scan-driven rebuild is shorter — though most of those toggles already trigger an immediate rescan via FEAT-080 (v2.5.4), so the visible improvement here is mostly for cases where the rescan trigger doesn't apply.
- Idle BaRs cost slightly more CPU than before — but only marginally, because the saturated-skip (FEAT-075), idle-backoff (FEAT-071), and dirty-cluster-key (FEAT-072) optimisations short-circuit most of the scan work when nothing has changed.

**What stays the same:**

- Source/push-target rescans still run on their own 30 s schedule (`SourcesUpdateInterval`).
- The immediate-rescan path (member sees a fresher coordinator result, settings change, weld/grind completes) is unchanged.
- The interval is internally tunable via `Mod.Settings.TargetsUpdateInterval` if a server admin wants to revert to the old behaviour — it's not exposed in `ModSettings.xml` because it has rarely needed adjustment in practice.

### Show Area Overlay Lighter and More Transparent

The terminal **Show Area** toggle (the box that visualises a BaR's working area in world) used a fully-opaque black colour. On smaller working areas this completely obscured the blocks underneath, making the toggle counter-productive — you turned it on to see where the BaR would work, and instead got a black box hiding everything.

**Fix:** the box is now a translucent grey. You can clearly see the working volume and the blocks inside it at the same time.

---

## Behind the Scenes

- `CheckAndUpdateInventoryFull` is now profiler-instrumented with `wasFull` / `nowFull` / `fillPct` so future "stuck full" reports are diagnosable from a single profiling session.
- BuildId convention continues — diagnostics from this release show **260526.x** in the debug HUD, `/nanobars version`, and profiler manifest header.

---

## Compatibility

- No save format changes.
- No settings changes — existing `ModSettings.xml` files load unchanged.
- No new chat commands.

Safe to drop in over v2.5.4. No world migration needed.
