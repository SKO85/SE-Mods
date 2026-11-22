---
layout: default
title: 'Release Notes – v2.5.6'
parent: Release Notes
grand_parent: Build and Repair System
nav_order: 0
---

# Release Notes – v2.5.6

- Status: **Released** — November 2026
- Notes: Big fix release. Projected blueprints build again on the latest game version, BaR farms work a lot faster, damage gets picked up in seconds, and shields and protection plugins are respected. Plus a long list of smaller fixes for multiplayer, inventories and stability.

---

## Highlights

### Projected blueprints build again

Since the Warfare 3 update (Space Engineers 1.211) BaRs couldn't build new blocks from a projection any more — they just stood there, or stopped after a few blocks until you switched them off and on. The game now wants the first component of a block in the builder's own inventory, and the BaR now puts it there.

While we were at it: building a projected block no longer gives you its first component for free, and a block the game refuses to build (missing DLC, block limits) is retried once every few minutes instead of over and over, without filling your server log.

### BaR farms are a lot faster

Put six or more BaRs next to each other and they quietly worked at about a third of their speed — the same BaRs spread out in pairs were much faster. That's fixed: big groups of BaRs now weld and grind about **1.7× faster**, at a tiny extra CPU cost.

### Damage and new blocks are noticed in seconds

When everything was repaired, a BaR could take up to 20–30 seconds to notice new damage or a block you just placed. It now reacts within a few seconds.

### Shields and protection are respected

- **DefenseShields:** an enemy ship whose shield is switched off or drained is no longer treated as shielded — your BaRs grind it. A raised shield still protects it, also when it goes up mid-grind.
- **Protection plugins and mods** that block grinding (for example admin or safe-base protection) now stop the BaR too, just like they stop hand grinders. Indestructible grids were and are left alone.
- **Safe zones:** box-shaped zones, big zones and overlapping zones are now handled correctly.

### No more working from afar

A ship flying out of the BaR's working area was still welded or ground until the next scan — sometimes from kilometres away. The BaR now checks the target is still in range before every weld and grind.

---

## Welding & Grinding

- The janitor no longer grinds blocks **you** built that just have no owner (armor-only hulls and the like) — it welds them instead.
- Dented blocks (full integrity but deformed) get fixed properly again.
- Big components (radio communication, detector parts) are no longer stuck in small BaRs — they get welded in again.
- Missing components are reported for **all** blocks waiting to be welded, not just the current one — so assembler queuing asks for the right amounts.
- Several fixes so two BaRs don't fight over the same block, and a BaR doesn't hang on to a block it can't work.

## Inventories

- Grinding a container no longer destroys the loot inside when the BaR's own inventory is full.
- Items in flight are no longer lost when a BaR is destroyed.
- Overflow pushing no longer pushes out the components needed for welding.
- Collecting floating items can't lose items any more.

## Multiplayer & Servers

- Other players can no longer change the settings of **your** BaRs, and fake network messages are ignored.
- When two players change a BaR's settings at nearly the same time, both changes now stick.
- Config commands read decimal numbers correctly on every server language.
- Turning shield checks on at runtime works immediately (no restart needed).
- Locked priority lists (admin setting) are now also respected by scripts.
- New FAQ entry about BaR power use and what the power settings do.

## Stability & Performance

- Lots of fixes for rare errors and crashes when many BaRs work at once.
- Nothing from the previous world sticks around after loading another one (no more stale timers, claims or caches).
- Working effects no longer stop showing after a long session, and lights are cleaned up properly.
- Busy servers: the missing-components overview is spread over several ticks, and needless rescans after settings changes are gone.

## Language

- **Korean** translation, thanks to najeong2! Space Engineers has no Korean UI language, so server admins switch it on with `LanguageOverride` set to `ko` in `ModSettings.xml`.

## Companion Script

Update the script too — this release comes with a new version:

- Auto-queuing in assemblers asks for the right amounts again (it used to under-queue).
- `info-only` mode stays on (it used to switch back off by itself).
- Displays start on their first page, and every page shows up — long page times used to skip the last pages.
- Auto-detection no longer picks up vanilla welders, and the script no longer crashes on servers that lock script control.
- The script no longer takes over cockpit screens on its own. The old example setup listed "Cockpit[0]", which grabbed any cockpit still called "Cockpit" on all connected grids. To show it on a cockpit screen, add a tag to the cockpit's name, e.g. `Cockpit [BaR:1@0]`.
- Display titles now count groups from 1 ("BaR Group 1"), the same as the `[BaR:1]` tags.

---

## Compatibility

- No save changes — safe to drop straight in over v2.5.5.
- One new optional setting: `LanguageOverride`.
- Tested on Space Engineers 1.211 (Warfare 3); projection building works on older game versions too.
- Update the Companion Script together with the mod.
