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

---

## Late-Cycle Additions (Build 260511.x)

These landed after the initial v2.5.4 publish and are part of the same release line. `BuildId` shown in the debug HUD / `/nanobars version` indicates which build a given world is running.

### New: Configurable Per-Tick ms Budgets

Two new settings cap the **wall-clock time** the mod is allowed to spend on grind and weld work each tick, independent of the existing operation-count caps:

| Setting | Default | Range | Effect |
|---|---:|---:|---|
| `MaxGrindMsPerTick` | 8 | 1–100 | Total `ServerDoGrind` time per tick (across all systems) |
| `MaxWeldMsPerTick` | 8 | 1–100 | Total `ServerDoWeld` time per tick (across all systems) |

With many systems active the ms cap usually wins over `MaxGrindsPerTick` / `MaxWeldsPerTick` (count caps), so on busy worlds raising the ms budget is the lever that actually increases throughput. **Recommended starting point for fleets of 30+ systems: `30 ms` each.** Defaults are unchanged from before — admins who don't touch the new settings keep the existing behaviour.

Set via chat: `/nanobars config set MaxGrindMsPerTick 30` (and save).

### New: `/nanobars debug cluster-area`

Local wireframe overlay that draws every multi-system cluster's per-member working areas, plus a tall green pillar above each cluster's coordinator block. Up to **8 cluster colours** (yellow, pink, green, purple, cyan, orange, red, white), assigned deterministically from the cluster's hash. When toggled on, a chat line summarises cluster sizes labelled by colour:

```
4 cluster(s) · 47 systems total · sizes: [pink=20, yellow=12, cyan=9, green=6]
```

Useful for diagnosing "why is system X idle?" — if its working area doesn't visibly cover any reachable target grid, that's the answer. Only enabled / functional / ready systems are drawn. Listen-server / single-player only — rejected on dedicated servers because the draw is client-side.

### New: `/nanobars debug targets`

Local wireframe overlay around every system's current weld and grind target blocks:

- **Border = cluster colour** of the system that has the target in its list.
- **Solid red fill** for targets that are currently assigned to a system.
- **Wireframe only** for unassigned targets (visible but not yet claimed).

Lets you see at a glance how many of a grid's blocks are actually being worked vs. just discovered. Per-frame dedup means each block renders exactly once regardless of how many systems see it. Listen-server / single-player only.

### Performance: Cluster Discovery Now Covers All Members

Background scans in BoundingBox mode used to query only the **coordinator's** AABB for entities — so cluster members positioned outside the coordinator's box couldn't see external target grids and went idle even when targets were physically near them. Discovery now unions the AABBs of **every** cluster member before the spatial-index query, so all reachable grids are considered. Per-block range filtering is unchanged (each system still trims to its own oriented working area downstream), so coverage semantics are exactly the same — the change only affects which grids get a chance to be considered.

Trade-off: larger query → 30–60 ms `AsyncAddBlocksOfBox` on 58-member clusters (background thread). Solo clusters skip the union and behave exactly as before.

### Behaviour: Toggling Sort Drops In-Flight Claims Immediately

FEAT-080 (v2.5.4) made the next scan happen right away on a settings change, but in-flight block assignments were preserved — so the system kept chipping at its previously-claimed block for a couple of seconds before adopting the new sort order. Sort-relevant settings changes (Nearest/Farthest/Smallest Grid, priority lists, search mode, etc.) now also release the system's claimed blocks so the very next pick uses the freshly sorted list. Cosmetic toggles (sound volume, ShowArea, multipliers) don't trigger this — they don't affect the cluster key, so claims aren't churned.

---

## Late-Cycle Fixes (Build 260511.x)

### Grind Picker Skipped Wheels and Other 0 % Integrity Blocks With Components Remaining (BUG-260511.2)

Player report: wheels and other attachable blocks ended up at 0 % integrity but never finished — components still in the construction stockpile, block never razed, the skeleton stayed on the grid.

**Root cause:** the grind picker filtered on `IsDestroyed` (true as soon as integrity reaches 0), which doesn't account for components still in the stockpile. The system skipped those blocks before stripping the remaining components.

**Fix:** filter changed to `IsFullyDismounted` (true only when integrity is 0 **and** the stockpile is empty). The system now keeps stripping components after integrity hits 0; once truly dismounted, the block flows into the existing raze cleanup path.

### Orphan Skeleton Blocks Never Got Razed (BUG-260511.3)

Follow-up to the BUG-260511.2 fix: after the picker correctly skipped already-fully-dismounted blocks, those blocks were never queued for raze (the queue was only fed from inside the grind path). Skeleton orphans from earlier damage events or detached subgrids stayed on the grid forever.

**Fix:** the picker's skip branch now enqueues fully-dismounted blocks for raze. The queue itself was upgraded with a dedup so 58 systems all spotting the same orphan don't pile up redundant entries.

### Grind Picker Hoarded Block Assignments (BUG-260511.4)

The grind picker called `AssignToSystem` (a claim, with TTL refresh) on every block it iterated past before landing on a grindable target. So a single system's single picker cycle could leak claims onto many blocks, blocking other systems from grabbing them for the next `AssignmentTtlSeconds`. On large fleets work piled up on a few systems while others sat idle.

**Fix:** picker now uses `IsAssignedToOtherSystem` (pure read) during iteration and only claims the **chosen** target after the loop. Mirrors the welding picker's existing behaviour. Each system holds at most one assignment per cycle. Distribution across a 58-system fleet is now even.

### Cosmetic Grind Transport Timer Kept Restarting (BUG-260511.1)

The visual particle-trail timer for grind transport could get pinned to "now" under certain conditions (multi-block destructions in quick succession), so particles streamed out forever without "arriving" at the system.

**Fix:** the cosmetic transport is only seeded when one isn't already in flight; the inventory transfer still runs every gate-open. Functional behaviour unchanged.

### Solo / Small Fleet Felt Sluggish (BUG-260511.5, BUG-260511.6)

Two related issues. (1) A single active system in a world with many other placed systems inherited the mod-wide 3-way stagger and only fired every 3rd cycle — visibly slow at `WorkSpeed=10`. (2) The auto-staggering formula counted every placed Build and Repair block, including ones that were toggled **off**, so a world with 2 enabled + 58 disabled systems was scored as a 60-system world.

**Fix:** solo clusters now cap at 2-way stagger regardless of mod-wide value; the auto formula now counts only **enabled** systems. Toggling unused systems off restores responsiveness for the working fleet — no admin override needed. Explicit `/nanobars config set StaggerGroupCount N` still wins over both.

### Server Stability: Maintenance Tasks Off Main Thread (BUG-260511.7)

Three periodic maintenance tasks (friendly-BaR cache rebuild every 5 s, grid-ownership cache refresh every 10 s, safe-zone cleanup every 6 s) were being dispatched onto the background-thread pool, but each of them touched engine APIs that aren't thread-safe — `GetUserRelationToOwner`, `Entities.TryGetEntityById`, `MySafeZone.Closed`/`MarkedForClose`. Under load this is exactly the class of thing that produces intermittent crashes or state corruption that doesn't reliably reproduce.

**Fix:** those three tasks now run inline on the sim tick. Cost is sub-millisecond to a few milliseconds per fire, once every 5–10 s. The TTL-cache cleanup and the profiler-file flush continue to run on the background pool — those genuinely don't touch engine state. No player-facing behaviour change; this closes a latent stability risk.

### Multiplayer Sync: Lock-On Target Changes Lost (BUG-260511.8)

On dedicated servers, when a Build and Repair system changed its current weld or grind target (typical lock-on hand-off), clients sometimes kept showing the **previous** target's beam and weld/grind particles. The state transmission was being suppressed by a fingerprint compare that didn't include the lock-on target fields, so a target swap with no other accompanying state change got dropped.

**Fix:** the fingerprint now includes both current targets (keyed by grid + position). A true target change always shifts the fingerprint and triggers a send; a same-block reference refresh leaves it identical (still correctly suppressed). Clients now see the new target within the normal ~1–2 s sync interval.

---

## Late-Cycle Additions (Build 260511.x) — continued

### `--global` Flag for `/nanobars config save` (FEAT-260511.7)

Restores the legacy mod's `-cpsf` parity. Two save targets are now distinguished by flag:

| Command | Target |
|---|---|
| `/nanobars config save` | World folder (per-world settings — unchanged default) |
| `/nanobars config save --global` | PC-wide mod storage folder — acts as a default for every world on this machine |

Layering: the world file always wins on load. The PC-wide file is only used when a world has no `ModSettings.xml` of its own. `/nanobars config reload` now also tells you which source it read from, so you can confirm at a glance whether your PC-wide defaults are in effect.

`/nanobars config delete` is correspondingly scoped:

| Command | Effect |
|---|---|
| `/nanobars config delete` | Deletes only the world file (new default — was: both) |
| `/nanobars config delete --global` | Deletes only the PC-wide file |
| `/nanobars config delete --all` | Deletes both (matches old behaviour) |

**Breaking change:** the old `delete` wiped both files unconditionally. Admins who relied on that should add `--all`.
