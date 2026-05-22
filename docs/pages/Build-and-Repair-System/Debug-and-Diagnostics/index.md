---
layout: default
title: Debug & Diagnostics
parent: Build and Repair System
nav_order: 10
---

# Debug & Diagnostics

When a Build and Repair block is not behaving as you expect, the mod offers several layers of diagnostic information you can turn on to see what is going on inside it. This page collects everything debug-related in one place: the server-wide `DebugMode` flag, the terminal info-panel diagnostics, the in-world Debug HUD overlay, the `BuildId`, the sim-speed override, the mod-integration check, and pointers to the built-in profiler.

None of these tools are required for normal play. Leave them off in production worlds — they trade a small amount of performance and screen clutter for visibility into the BaR's internal state, which is only useful when you are actively troubleshooting.

---

## Debug Mode

`DebugMode` is a server-wide setting in `ModSettings.xml`. When enabled, every BaR's terminal **custom info panel** shows extra diagnostic information beyond the normal status text:

- Source / push-target inventory counts.
- Cluster info (cluster ID, member count, whether this BaR is the coordinator).
- Scan timings.
- Internal scan state (last scan tick, saturation flags, idle reasons).

| Setting | Default | Description |
|---|---|---|
| `DebugMode` | `false` | When `true`, the custom info panel shows extra diagnostic data on every BaR. |

Turn it on either by editing `ModSettings.xml` directly or via chat:

```
/nanobars config set DebugMode true
/nanobars debug on              # alias for the above
```

Turn it off again:

```
/nanobars config set DebugMode false
/nanobars debug off
```

> **Note:** On dedicated servers, some of the extra info is rendered only on the connected client — the DS console may not show every field. Open the terminal in-game on a client to see the full panel.

---

## Custom Info Panel

Even with `DebugMode` off, the BaR's terminal custom info panel surfaces several diagnostic fields that are useful when troubleshooting why a BaR is not working:

- **Current weld / grind / collect target** — what the BaR is locked on to right now.
- **Missing components** — when the BaR cannot weld a block, the missing component list is shown so you know what to bring.
- **Idle reason** — when the BaR is not working, a short text reason ("safe zone blocks welding," "all push targets full," "no targets in range," etc.).
- **Next target scan: Xs** — when the BaR is idle, a countdown to the next scan cycle. Helps distinguish "between scans" from "genuinely out of work" (FEAT-079).

Turning `DebugMode` on adds the cluster, source / push-target, and scan-timing fields on top of these basics.

---

## Debug HUD

The **debug HUD** is an in-world screen overlay showing per-BaR diagnostics for the BaRs around you, without having to open each terminal. It is admin-only and controlled by chat commands:

```
/nanobars debug show          — show the HUD overlay locally
/nanobars debug hide          — hide the HUD overlay locally
/nanobars debug left|right    — position the HUD on the left / right side and show
```

The HUD requires the [TextHudAPI (BuildInfo)](https://steamcommunity.com/sharedfiles/filedetails/?id=758597413) mod. Without it the chat command will report "Text HUD API (BuildInfo mod) not detected" and the HUD will not appear.

### Turning Debug On Does Not Show the HUD

A common point of confusion on dedicated servers: running `/nanobars debug on` (or setting `DebugMode = true`) **does not** make the in-world HUD appear. Those toggles enable the *server-wide* debug data feed — they tell the mod to start collecting and sending the diagnostics. To actually see the HUD overlay on your screen you must additionally run:

```
/nanobars debug show
```

on your own client. This second step is intentional and is **per-admin**: each admin connected to the server can independently choose whether they want the HUD up. One admin can keep the HUD visible to investigate something while other connected admins see a clean screen — toggling it on the server-wide flag would force the overlay onto everyone, which is rarely what anybody wants.

The split also means an admin can keep the HUD up locally even when the server-wide `DebugMode` flag is off (the HUD still renders whatever live data the server is willing to send), and conversely, an admin can leave `DebugMode` on without having any HUD visible to themselves. The two switches are independent on purpose.

The HUD reads live data from whatever BaRs the server reports; it does not alter mod behaviour.

| Goal | Command(s) |
|---|---|
| Show extra fields in the terminal info panel for everyone | `/nanobars debug on` (server-wide) |
| Show the in-world HUD overlay on **your** client | `/nanobars debug show` (local) |
| Show both | run both, in either order |
| Hide the HUD on your client only | `/nanobars debug hide` |
| Stop the server-wide debug data feed | `/nanobars debug off` |

---

## Cluster-Area Overlay (build 260511.x+)

`/nanobars debug cluster-area` toggles an in-world wireframe overlay that visualises every multi-system cluster directly in 3D space:

- A **per-member working-area box** for each system in a cluster — one of 8 distinct colours (yellow, pink, green, purple, cyan, orange, red, white), assigned deterministically per cluster.
- A **tall green pillar** above each cluster's *coordinator* block — the system that performs the scan on behalf of the cluster.
- Drawn with `PostPP` blend so wireframes are visible **through** other blocks; you don't have to crawl into the ship's interior to spot them.

When toggled on, a chat line summarises the cluster sizes labelled by their overlay colour:

```
4 cluster(s) · 47 systems total · sizes: [pink=20, yellow=12, cyan=9, green=6]
```

Solo clusters (single-system) aren't drawn — they're already visualised by the per-block **Show area** terminal toggle.

The overlay matches the server-side cluster filter exactly: only systems that are **enabled, functional, and ready** participate. A disabled or broken system won't appear in any cluster.

**Use case:** if a system is reported as idle, this overlay shows you immediately whether the cluster's reach actually covers any reachable target grid. If the wireframes don't visibly contain any target grid, the answer is "geometry" — reposition the system or enlarge its area.

Client-side only. Rejected on dedicated servers — the draw runs on the local renderer; on a DS the command prints a message and doesn't enable the toggle.

---

## Targets Overlay (build 260511.x+)

`/nanobars debug targets` toggles an in-world wireframe overlay around every system's current weld and grind target blocks:

- **Border colour** = the cluster colour of the system that has the target in its scan list (matches the cluster-area palette above).
- **Solid red fill** (semi-transparent) = the target is currently **assigned** to a system — i.e., a system has claimed it via the assignment handler.
- **Wireframe only, no fill** = the target is **discovered** by at least one cluster but no system has claimed it yet.

Same `PostPP` blend as the cluster overlay — visible through walls.

Lets you see at a glance:

- Which blocks are actually being worked on vs. just discovered.
- How target work is distributed between clusters (different border colours).
- Whether the per-grid limit (`MaxSystemsPerTargetGrid`) is keeping systems off a grid (filled blocks span multiple cluster colours rather than concentrating in one).

Per-frame dedup ensures each block renders exactly once regardless of how many systems list it as a target. Stale targets (already welded to full integrity, already razed) are filtered out so the overlay tracks live work rather than the last scan's snapshot.

Client-side only. Same DS rejection as the cluster-area overlay.

---

## BuildId

Every build of the mod ships with a `BuildId` of the form `YYMMDD.N` (e.g. `260501.3`) that surfaces in:

- The debug HUD header — `--- BAR SYSTEMS --- v2.5.4 (260501.3)`.
- `/nanobars version` — both client and server lines include it.
- `/nanobars -help` — the dialog header.
- The profiler summary log header.

When reporting issues, include the `BuildId` so the exact build can be identified. Two installs that share the same `2.5.4` mod version may differ by `BuildId` between dev / preview / release builds — the version number alone is not enough to tell them apart.

---

## Sim-Speed Override

For testing the BaR's automatic throttling behaviour without actually slowing down the server, admins can override the sim-speed value the BaR uses for its internal calculations:

```
/nanobars sim 0.5     — pretend sim-speed is 0.5 for BaR calculations
/nanobars sim reset   — remove the override and use the actual sim-speed
```

The override is purely a diagnostic — it does **not** change the actual server sim-speed, only what the BaR's throttling logic sees. Useful for verifying the throttle activates correctly, or for testing performance in a low-sim-speed scenario without having to load the server up.

---

## Mod Integration Check

```
/nanobars mods
```

Reports whether the BaR has detected each of the optional mods it integrates with:

- **TextHudAPI (BuildInfo)** — required for the Debug HUD.
- **Defence Shields** — used to skip targets protected by an active shield.

If either is missing, the relevant feature is silently disabled rather than throwing errors. Run this command first when a debug HUD does not appear or when shield-protected grids are still being targeted — it confirms whether the mod is loaded.

---

## Built-in Profiler

For performance troubleshooting (low sim-speed, frame spikes, dropped ticks), the mod ships a per-method profiler. It is admin-only and produces one log file per profiled method, plus a manifest listing the files relevant to the latest run.

```
/nanobars profile start [seconds] [minDurationMs] [sessionName]
/nanobars profile stop
/nanobars profile status
/nanobars profile summary
/nanobars profile list
/nanobars profile clear <sessionName|all>
/nanobars profile minduration <ms>
```

Log files are saved to the world's storage folder. Share the relevant files (and the manifest) when reporting a performance issue.

See [Chat Commands → Profiling](../Chat-Commands/#profiling) for full command reference.

---

## Reporting Issues

When reporting a bug or performance problem, include:

1. **Mod version + BuildId** — get both from `/nanobars version` (client and server lines on a dedicated server).
2. **A summary from the BaR's custom info panel** — turn on `DebugMode` first so the cluster / source / scan-timing fields are visible.
3. **A profiler session covering the issue** — `/nanobars profile start 120` then reproduce, then `/nanobars profile stop`. Attach the manifest + the listed log files.
4. **Mod integration status** — `/nanobars mods` output, so the maintainers can see whether TextHudAPI / Defence Shields are detected.
5. **Optionally, a debug HUD screenshot** — covers the cluster state and active BaRs at a glance.

### Where to Send It

Two channels are available, both fine — pick whichever fits the kind of conversation you want:

- **Discord (preferred for interactive help)** — [Join the server](https://discord.gg/5XkQW5tdQM) and post in **#help-topics**. Discord is the better channel when you would benefit from back-and-forth — the developer is reachable directly, other community members often jump in with workarounds, and screenshots / log files paste inline. Ideal for "is this a bug or am I configuring it wrong?" situations and for problems that need a few rounds of triage to pin down.
- **GitHub Issues** — [github.com/SKO85/SE-Mods/issues](https://github.com/SKO85/SE-Mods/issues). Best for confirmed bugs and well-scoped feature requests where the conversation is going to be tracked over a longer period (across mod versions, with a reproducible repro and attached logs). GitHub gives the issue a permanent identifier and ties it cleanly to commits and release notes.

If you are not sure which to use, start in Discord — it is faster, and a maintainer will move the report to GitHub if the issue turns out to need long-term tracking.

---

## Troubleshooting

<details>
<summary>The debug HUD overlay does not appear when I run /nanobars debug show.</summary>
<div>
<ul>
<li>The HUD requires the <strong>TextHudAPI (BuildInfo)</strong> mod. Subscribe and add it to your world's mod list. Run <code>/nanobars mods</code> to confirm the mod is detected.</li>
<li>The HUD is admin-only — your player must have <strong>Admin</strong>, <strong>SpaceMaster</strong>, or <strong>Owner</strong> permissions.</li>
</ul>
</div>
</details>

<details>
<summary>I turned on DebugMode but the terminal info panel does not show extra fields.</summary>
<div>
<ul>
<li>Confirm the setting actually applied — run <code>/nanobars config get DebugMode</code>. If it returns <code>false</code>, the change did not take effect (e.g. you set it on the wrong session).</li>
<li>On dedicated servers, some debug fields are rendered only on the connected client. Open the terminal in-game from a client; the DS console may show fewer fields.</li>
<li>Reopen the terminal panel — info panel content is recomputed when the panel is opened.</li>
</ul>
</div>
</details>

<details>
<summary>/nanobars debug on does nothing visible.</summary>
<div>
<p><code>/nanobars debug on</code> is a server-wide flag — it only enables the extra info-panel fields described in <strong>Debug Mode</strong> above. To get the in-world HUD overlay, you also need <code>/nanobars debug show</code>. The two are independent: the server-wide flag controls what the info panel renders; <code>show / hide / left / right</code> control the local HUD overlay.</p>
</div>
</details>

<details>
<summary>The profiler runs but no log files appear.</summary>
<div>
<ul>
<li>Profiler files are saved to the world's mod storage folder, not the world save folder. The path differs by environment — see <code>/nanobars profile list</code> for the resolved path.</li>
<li>If the session was very short or the BaR did nothing during it, only methods that exceeded <code>minDurationMs</code> are logged. Lower the threshold with <code>/nanobars profile minduration 0</code> for the next run.</li>
</ul>
</div>
</details>

<details>
<summary>My BuildId on client and server differ.</summary>
<div>
<p>Mod versions are correct (<code>v2.5.4</code> on both lines) but BuildIds differ: the two installs are built from the same source-version tag but on different days or different release channels (dev / preview / release). Subscribe both client and server to the same Workshop entry and let Steam refresh — the BuildIds should match after a clean download. If they keep diverging on a Torch server, check that the workshop content directory was actually re-downloaded; a stale local copy will keep its old BuildId.</p>
</div>
</details>
