---
layout: default
title: 'Release Notes – v2.5.7'
parent: Release Notes
grand_parent: Build and Repair System
nav_order: 0
---

# Release Notes – v2.5.7

- Status: **Released** — November 2026
- Notes: Performance and polish after v2.5.6. Big BaR setups no longer stall when components are spread out, servers send much less network traffic, BaRs tell you when a shield, safe zone or protection blocks them, and a range setting below 100 m no longer fills the log with errors.

---

## Display

- **Show Area is lighter on your graphics card.** When you're inside a BaR's working area (for example flying a ship full of BaRs), only thin edges are drawn instead of a tinted box filling your whole screen. From outside it looks the same as before. Boxes more than 1 km away aren't drawn at all.

## Welding & Grinding

- **BaRs on different ships help each other again.** A BaR that ran out of components used to make every other BaR skip its damaged blocks for a while, so a well-stocked BaR on another ship would barely repair it. Now only BaRs sharing the same supply (docked or connected) skip those blocks.
- **Big BaR setups no longer stall when components are spread over many containers.** BaRs used to give up on blocks too early when the first containers they looked in were empty, then sat idle for a while, getting slower as the base's stock spread out. Now they keep looking through the rest first. In our test with 50 BaRs and 300 containers a job that took four minutes now finishes in ten seconds.
- BaRs fetch components for the next couple of blocks in one go when a container has plenty, so repairing uses **about 40 % fewer conveyor transfers** (less load on big bases). Containers that are running low are left alone.
- **Grinding with big groups of BaRs uses less CPU:** BaRs no longer keep re-checking blocks that are already ground away.
- A BaR that was handed over to another player no longer refuses to build projections because of the **original builder's** block or PCU limits. The game now decides by the limits of the BaR's owner, who also pays for the blocks, and the terminal says so when those limits are reached.
- A BaR no longer keeps "welding" a block the game won't let it weld (for example in a safe zone on a server): it skips that grid for a while and its terminal says **Welding blocked by a safe zone / protection**.
- When a **shield or protection mod** stops a BaR from grinding a grid, its terminal now says so ("Grinding blocked by a shield / protection mod") instead of the BaR just quietly skipping it.
- A BaR whose inventory is practically full (it can't fit the next load of ore) now says **Inventory full** instead of looking idle.
- Servers that set the BaR **Range below 100 m** no longer get an error in the log (and possibly an error popup) on every weld or grind. Ranges above 100 m now also scale the nanobot speed smoothly (150 m is really 1.5x, not 1x).

## Servers

- **Less network traffic on servers.** A BaR now only sends its status to players who are near it (within the world's sync distance). Players at another base no longer receive updates for BaRs they can't even see. A working BaR costs each nearby player about 0.5 KB/s; idle BaRs send nothing.
- Dedicated servers no longer build BaR terminal texts nobody reads, and reputation loss from grinding other factions is applied in small batches instead of on every grind (same total).

## Docs

- New FAQ entry: why BaRs stop building a projection when you log off or fly away (the game hides projections when no player is near).
- New FAQ entries from Discord questions: projections after the Warfare 3 update, BaRs skipping whole block types, half-built blocks with several BaRs, and companion script setup / "nothing gets queued". Plus: missing direction arrows, repair-only BaRs, the ignore colour, and not collecting stone.
- New FAQ entries: what "Grinding blocked" / "Welding blocked" mean, why only 10 of your BaRs work on one big ship in multiplayer (and how admins change it), and why turning off the safe zone check doesn't let BaRs weld in a no-weld zone on a server.

---

## Compatibility

- No save changes — safe to drop straight in over v2.5.6.
