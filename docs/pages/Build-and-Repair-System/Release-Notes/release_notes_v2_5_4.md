---
layout: default
title: 'Release Notes – v2.5.4'
parent: Release Notes
grand_parent: Build and Repair System
nav_order: 0
---

# Release Notes – v2.5.4

- Status: **Released** — May 2026
- Notes: Major performance release for large bases and busy fleets, plus fixes for terminal-toggle latency, offline worlds getting stuck on certain blocks, and a redundant transport gate that was throttling welding to a fraction of its real speed.

---

## Performance

This is the largest performance pass since v2.5.0. The work was driven by player reports of stuttering and dropped sim-speed on bases with many Build and Repair systems and large grids needing repair.

**Headline numbers** (server with 58 BaRs, three large ships, ~11 000 blocks each):

- Average sim-speed: **0.66 → ~1.00**
- Maximum BaR update spike: **1433 ms → ~15–20 ms**
- Background scan time per cycle: **~200 ms → ~75 ms**

What changed under the hood (all invisible to players, but you should *feel* it):

- Background scans now share work between nearby BaRs instead of repeating it per-system.
- Per-grid limits (`MaxSystemsPerTargetGrid`) are now enforced correctly for projected blocks too — previously, large fleets all converged on the same projection.
- Heavy operations (full block dismounts, projector-build calls, push-to-cargo) are now budgeted across ticks instead of compounding into single-frame spikes.
- Idle BaRs cost almost nothing per tick.
- Welding loop skips blocks already known to be unweldable for a few seconds (shared across all BaRs), so 50 BaRs don't all bounce off the same broken block on the same tick.
- Projector cold-start: when a projection becomes buildable, BaRs pick it up within 1–2 seconds instead of waiting up to 20 seconds for the idle backoff to expire.

If you previously had to limit your BaR count or cap target-grid sizes to keep sim-speed up, try raising those limits and see how it goes.

---

## Bug Fixes

### Terminal Setting Changes Now Apply Immediately (FEAT-080, BUG-260501.1)

Toggling **Grind Near First / Far First / Smallest Grid First**, **Work Mode**, **Allow Build**, **Use Color Filter**, the priority lists, area size, etc. used to wait up to 10 seconds before the BaR actually started using the new setting — long enough that players reported it as "the toggle doesn't work."

The fix: the BaR now triggers an immediate rescan when terminal settings change, so the new sort order / target list takes effect within ~1–2 seconds. A follow-up fix (BUG-260501.1) handles the case where a setting toggle reshuffles the BaR into a different cluster — without it, the new cluster's coordinator wasn't told to rescan and the toggle was still slow.

Note: if a BaR is already grinding a specific block when you toggle, it finishes that block before picking the next one from the new sort order. That's not a delay in the toggle — it's lock-on behavior.

### Welding Throttled to ~15–20% Duty Cycle (BUG-103)

A cosmetic transport timer (5–6 seconds per "trip", controlling the visual particle effect) was also gating real weld and grind work for its full duration. Items are actually picked up synchronously when needed, so the gate had no functional purpose — it was just slowing the BaR down.

**Fix:** the gate is removed. Welding and grinding now run at full pace. Visual transport particles are unaffected. Expected speedup: 3–4× on long-running welds.

### Offline Worlds: BaR Locks Onto a Broken Block Forever (BUG-115)

In **offline mode** (no Steam connection), Space Engineers' DLC entitlement table is empty, which causes the engine's `proj.Build()` to throw an internal exception on certain projected blocks. The exception was being swallowed at the top of the BaR's update loop, but the BaR's lock-on stayed on the broken block — so it would re-target the same block every tick forever and never weld anything else.

**Symptoms:**

- BaR shows "Welding" but never actually completes a block.
- Switching the same world to online mode fixes it without any other change.
- Affects projected blocks of certain types (commonly DLC armor variants).

**Fix:** the BaR now catches the engine exception, marks the offending block as broken (skipped permanently for this session), and keeps welding the rest. A diagnostic warning is logged once per broken block per session — admins can use it to identify which blocks tripped it.

### Offline Owner Filtered Out All DLC Blocks (BUG-104)

A separate offline-mode issue: when the BaR's owner was offline, the mod-side DLC entitlement check returned an empty set and filtered out *every* DLC-tagged block at scan time — so projected DLC armor (etc.) was never targeted.

**Fix:** the BaR-side DLC check is removed entirely. The projector already enforces DLC at projection time, so a projected DLC-tagged block is by definition allowed to be built.

### Auto-Push Could Not Be Fully Disabled (BUG-114)

A safety overflow path could still push items from the welder inventory to push-targets even when **all three** `Push ... Immediately` options were disabled.

**Fix:** the safety push now checks the same flags. Disabling all three push options actually stops the welder→push-target flow now.

### Idle BaRs On Disabled Worlds Caused Update Spikes (BUG-102)

A bug in the per-BaR stagger formula made all isolated and disabled BaRs fire on the same tick, producing 5–15 ms compounding spikes and occasional 1+ second tail events. Discovered on a world with 31 disabled BaRs.

**Fix:** stagger now correctly accounts for both isolated single-BaR systems and disabled BaRs. On the same world: maximum spike dropped from 1433 ms to ~15 ms, sim-speed minimum recovered from 0.02 to 0.15.

---

## Removed

### `Grind If Weld Get Stuck` Work Mode (BUG-101)

The `GrindIfWeldGetStuck` work mode is **removed** from the Work Mode dropdown. A v2.5.3 fix to its fall-through logic introduced a deadlock that the mode could not be cleanly recovered from, and the mode's intent (grind blockers when a weld is stuck) was poorly defined and rarely used.

**For admins:** worlds that previously had a BaR set to this mode are silently migrated to **Weld Before Grind** on first load. No manual edit needed.

### Welder Transport Speed Tuned Back Down

The welder transport speed (used by the visual transport timer) was bumped to 80 m/s during the BUG-103 work and is now set to **50 m/s**. This is purely a tuning change — duty cycle is no longer gated by the transport timer either way.

---

## Diagnostics

### `BuildId` In Diagnostic Output

Every build now carries a `BuildId` of the form `YYMMDD.N` (e.g. `260501.3`) that surfaces in:

- The debug HUD header (`--- BAR SYSTEMS --- v2.5.4 (260501.3)`).
- `/nanobars version` (client and server lines both include it).
- `/nanobars -help` dialog header.
- Profiler summary log header.

Useful when reporting issues — admins can confirm which build produced a given log and that the same build is loaded on both client and server. Versions that share the same `2.5.4` modVersion may differ by `BuildId` between dev/preview/release; this number disambiguates them.

### Custom Info Panel: Next-Scan Countdown (FEAT-079)

When a BaR is idle, the terminal info panel now shows a `Next target scan: Xs` countdown so you can tell whether it's between scan cycles vs. genuinely out of work. Debug mode adds extra scan-state info for diagnostics.

### `/nanobars version` Output Cleaned Up

The output no longer repeats the redundant `BaR Mod` prefix on each line. Format is now:

```
v2.5.4 (260501.3) — Client
v2.5.4 (260501.3) — Server
```
