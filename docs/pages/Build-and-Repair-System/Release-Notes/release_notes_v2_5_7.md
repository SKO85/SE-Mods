---
layout: default
title: 'Release Notes – v2.5.7'
parent: Release Notes
grand_parent: Build and Repair System
nav_order: 0
---

# Release Notes – v2.5.7

- Status: **In development**
- Notes: Follow-up to v2.5.6.

---

## Display

- **Show Area is lighter on your graphics card.** When you're inside a BaR's working area (for example flying a ship full of BaRs), only thin edges are drawn instead of a tinted box filling your whole screen. From outside it looks the same as before. Boxes more than 1 km away aren't drawn at all.

## Welding

- **BaRs on different ships help each other again.** A BaR that ran out of components used to make every other BaR skip its damaged blocks for a while, so a well-stocked BaR on another ship would barely repair it. Now only BaRs sharing the same supply (docked or connected) skip those blocks.
- **Big BaR setups no longer stall when components are spread over many containers.** BaRs used to give up on blocks too early when the first containers they looked in were empty, then sat idle for a while, getting slower as the base's stock spread out. Now they keep looking through the rest first. In our test with 50 BaRs and 300 containers a job that took four minutes now finishes in ten seconds.
- A BaR that was handed over to another player no longer refuses to build projections because of the **original builder's** block or PCU limits. The game now decides by the limits of the BaR's owner, who also pays for the blocks, and the terminal says so when those limits are reached.

- A BaR no longer keeps "welding" a block the game won't let it weld (for example in a safe zone on a server): it skips that grid for a while and its terminal says **Welding blocked by a safe zone / protection**.
- When a **shield or protection mod** stops a BaR from grinding a grid, its terminal now says so ("Grinding blocked by a shield / protection mod") instead of the BaR just quietly skipping it.
- A BaR whose inventory is practically full (it can't fit the next load of ore) now says **Inventory full** instead of looking idle.

## Docs

- New FAQ entry: why BaRs stop building a projection when you log off or fly away (the game hides projections when no player is near).
- New FAQ entries from Discord questions: projections after the Warfare 3 update, BaRs skipping whole block types, half-built blocks with several BaRs, and companion script setup / "nothing gets queued". Plus: missing direction arrows, repair-only BaRs, the ignore colour, and not collecting stone.

---

## Compatibility

- No save changes — safe to drop straight in over v2.5.6.
