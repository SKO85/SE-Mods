---
layout: default
title: 'Release Notes – v2.5.4'
parent: Release Notes
grand_parent: Build and Repair System
nav_order: 1
---

# Release Notes – v2.5.4

- Status: **Released** — May 2026
- Notes: Big performance release. Large bases and busy fleets run much smoother, terminal toggles apply right away, welding is a lot faster, and offline worlds no longer get stuck on certain blocks.

---

## Performance

The biggest performance pass since v2.5.0, driven by reports of stutter and dropped sim speed on worlds with many BaRs and big damaged ships.

On our test server (58 BaRs, three large ships of ~11,000 blocks each):

- Sim speed went from **0.66 to a steady ~1.00**
- The worst single-frame spike dropped from **1.4 seconds to ~15–20 ms**

In plain terms: nearby BaRs now share their scanning work instead of each doing it all alone, heavy jobs are spread out over time instead of landing in a single frame, and idle BaRs cost next to nothing. If you've been limiting how many BaRs you place to protect your sim speed, try lifting that limit.

---

## Fixes

### Terminal toggles apply right away

Changing things like **Grind Near/Far First**, **Work Mode**, **Allow Build**, priorities, or the area size used to take up to 10 seconds to kick in — long enough to look broken. It now takes effect within a second or two. (If the BaR is mid-grind on a block when you toggle, it finishes that block first — that's normal lock-on behavior, not a delay.)

### Welding is much faster

An internal timer that was only meant to drive the visual "transport" particles was also slowing down the actual welding and grinding. It's gone. Long welding jobs now go roughly **3–4× faster**. The particle visuals look the same as before.

### Offline worlds: BaR stuck "welding" forever

In offline mode (no Steam connection), the game itself chokes on certain projected blocks — usually DLC armor variants. The BaR used to get stuck on such a block and never move on. It now skips the problem block and keeps welding everything else. Related fix: projected DLC blocks are no longer skipped when the BaR's owner is offline.

### "Push Immediately" off now means off

Even with all three push options disabled, the BaR could still sneak items into your cargo through a leftover safety path. Disabled now really means disabled.

### Lots of small smoothness fixes

Wheels and similar blocks no longer get stuck half-grinded at 0%; leftover block "skeletons" get cleaned up properly; fleets share the work evenly instead of a few BaRs hogging everything while the rest idle; small fleets feel snappier; changing server limits mid-game no longer freezes the fleet; and in multiplayer, clients now reliably show which block a BaR is actually working on.

---

## Removed

### "Grind If Weld Get Stuck" work mode

This mode was unreliable and rarely used, so it's been removed from the Work Mode dropdown. Worlds that used it are switched to **Weld Before Grind** automatically — nothing to do on your end.

---

## Nice to Have

- When a BaR is idle, the terminal info panel now shows a **"Next target scan: Xs"** countdown, so you can tell "waiting for the next scan" apart from "genuinely out of work".
- `/nanobars version` now shows a build number (like `260501.3`) on both the client and server lines — handy when reporting an issue, to confirm both sides run the same build.

---

## For Admins

- **New time budgets:** `MaxGrindMsPerTick` and `MaxWeldMsPerTick` (default 8, range 1–100) cap how much time per tick the mod may spend grinding / welding in total. On busy servers (30+ BaRs) this is the lever that actually raises throughput — try `30` each: `/nanobars config set MaxGrindMsPerTick 30`, then `config save`.
- **New debug overlays:** `/nanobars debug cluster-area` draws each BaR group's working areas in colour (coordinator marked with a green pillar); `/nanobars debug targets` outlines the blocks currently being worked. Both are single-player / listen-server only.
- **PC-wide settings:** `/nanobars config save --global` saves your settings as a default for every world on this machine (a world's own file still wins). **Heads-up:** `/nanobars config delete` now deletes only the world file — use `delete --all` if you want the old wipe-everything behavior.
- Changing settings like `MaxSystemsPerTargetGrid` mid-game now takes effect cleanly within a tick or two, in both directions — no more stuck fleets after lowering a limit.
