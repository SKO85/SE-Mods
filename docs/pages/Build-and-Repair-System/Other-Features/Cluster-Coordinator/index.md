---
layout: default
title: Cluster Coordinator
parent: Other Features
grand_parent: Build and Repair System
nav_order: 1
---

# Cluster Coordinator

When several Build and Repair blocks share the same working area, scanning the same grids and floating objects from each one separately is wasted work. The **cluster coordinator** is an automatic mechanism that elects a single BaR per group to do the scan, then shares the result with every other BaR in the group.

You do not have to configure anything — clustering is automatic, runs on the server, and updates itself when terminal settings change.

---

## Why It Exists

A single background scan touches every grid in range, iterates blocks, applies priority and color filters, and sorts the result. With ten BaRs co-located on the same base, this work used to run ten times — once per BaR. The coordinator collapses that to one scan plus nine cheap range/distance filters, with a roughly **80 % reduction in scan CPU usage at ~10 co-located BaRs**.

This is the single largest contributor to the v2.5.0+ performance improvements on bases with many BaRs.

---

## How Clustering Works

The coordinator is built around a **cluster key** — a deterministic fingerprint of every setting that affects what a scan finds. Two BaRs with the same key would produce the same candidate list (ignoring per-block range and distance), so they can safely share one scan.

The cluster key is derived from:

- The BaR's grid (`CubeGrid.EntityId`).
- Owner ID and conveyor-system flag.
- Work Mode and Search Mode.
- The relevant flags: `Use Ignore Color`, `Use Grind Color`, `Build Projections` (`AllowBuild` internally), `Grind Smallest Grid First`, `Grind Near First`, `Ignore Priority Order`.
- Weld / Grind / Collect priority lists (their serialized order + enabled state).
- Ignore Color and Grind Color HSV (when those flags are on).
- Grind Janitor relations and options.
- Weld options (e.g. `Weld to Functional` vs `Weld to Full`).
- Safe-Zone permission state (whether welding / grinding is currently allowed at the BaR's position).

Once per cycle, every BaR's settings are hashed into a numeric cluster-key hash. If no hash has changed since the last cycle, the rebuild is skipped entirely (FEAT-072 fast path — no string allocation, no dictionary churn). When something does change, the full string keys are recomputed and BaRs are grouped by exact key match.

Every group becomes one cluster. **Even single-BaR groups are tracked as clusters** — the per-BaR code path is identical whether the BaR is alone or in a group of fifty.

---

## Coordinator Election

For each cluster, the coordinator is elected as the **member with the lowest Welder EntityId** — a deterministic, parameter-free choice that gives stable results across rebuilds. The current coordinator is preferred if it is still a valid member, so the role does not flap when other members join or leave.

The coordinator runs the next scan; the other members read the shared result. The coordinator-only fields the result holds (target lists, source lists, projector state) are republished atomically when the scan finishes, so members never see a half-built result.

---

## Per-Member Filtering

The shared scan produces a *superset* — every block that **any** member could care about. Each member then applies its own:

- **Range** — the BaR's own `Range` and work area.
- **Offset** — the work area's terminal offset.
- **Distance sort tie-breakers** — distance values are computed relative to each BaR's own position.
- **MaxSystemsPerTargetGrid** — each BaR independently respects the per-grid limit.

So even though the scan is shared, each BaR still has its own targets, its own lock-on, and its own distance ordering.

---

## Re-Election & Stability

The cluster set is rebuilt periodically on the main thread (called from `Mod.RebuildSourcesAndTargetsTimer()`). Re-election happens when:

- A BaR is added (placed) or removed (deleted, disabled, unfunctional).
- A relevant terminal setting changes — the BaR's hash differs from its last hash.
- The current coordinator stops being a valid member (turned off, disabled, ownership changed).

If the rebuild detects no changes (system count unchanged, every hash matches), it returns early — this is the common case once a base has stabilised, and costs essentially nothing per cycle.

When a BaR's setting toggle reshuffles it into a different cluster, any pending forced-rescan flag on the toggling BaR is propagated to the new cluster's coordinator so the new sort order takes effect on the next scan rather than waiting up to 60 s for the saturated-skip gate to expire (BUG-260501.1, v2.5.4).

---

## What Forms a Cluster

Two BaRs share a cluster when:

1. They are mounted on the **same logical grid** (connected via merge / connector / piston / rotor — anything that resolves to the same `CubeGrid.EntityId` after `MyAPIGateway.Multiplayer` connects).
2. They have the **same owner ID** and the same `Use Conveyor System` flag.
3. They have **identical scan-relevant settings** (work mode, search mode, the cluster-relevant flags listed above, priority lists, colors, grind janitor, weld options).
4. They are at the **same Safe-Zone permission state** — a BaR inside a no-weld Safe Zone gets a separate cluster from one outside, because their weld permissions differ.

Two BaRs that look "the same" in casual inspection but have, say, different priority list ordering, different paint filters, or one with **Build Projections** toggled differently, will land in **different clusters** and each get their own scan.

The simplest way to see clustering at work: enable **Debug Mode** in `ModSettings.xml`. Each BaR's terminal info panel shows its **cluster ID**, **member count**, and whether it is the **coordinator**.

---

## Performance Impact

Realistic numbers from a 58-BaR / three-large-ship test world (v2.5.4 release notes):

| Metric | Before clustering / pre-tuning | After clustering / v2.5.4 |
|---|---|---|
| Average sim-speed | 0.66 | ~1.00 |
| Maximum BaR update spike | 1433 ms | ~15–20 ms |
| Background scan time per cycle | ~200 ms | ~75 ms |

Clustering is one piece of the picture, alongside saturation skipping, per-tick weld / grind budgets, and the empty-grid backoff — but it is the contribution that scales fastest with BaR count.

---

## Configuration

There is **no terminal control** for clustering — it is automatic. The relevant `ModSettings.xml` knobs are not cluster-specific but affect cluster behaviour:

| Setting | Effect on clustering |
|---|---|
| `MaxBackgroundTasks` | Caps how many cluster scans run in parallel. |
| `EmptyGridRescanDelaySeconds` | When all grids in a cluster's range are empty, the cluster's next scan is skipped until the backoff expires. |
| `DebugMode` | Surfaces cluster ID, member count, and coordinator role in each BaR's info panel. |

---

## Troubleshooting

<details>
<summary>I built ten BaRs next to each other but each one shows a different cluster ID.</summary>
<div>
<p>Their settings differ in some scan-relevant way. Common causes:</p>
<ul>
<li>Different priority list ordering on one or more BaRs.</li>
<li>Different color filter settings (one has <strong>Use Ignore Color</strong> on, another off, or different HSV values).</li>
<li>Different work modes or search modes.</li>
<li>Different owners (a BaR transferred to another player or faction does not cluster with yours).</li>
<li>One straddles a Safe-Zone boundary that changes its weld / grind permission state.</li>
</ul>
<p>Use <strong>Reset All Settings</strong> on the outliers, or copy settings from one BaR to the rest, to bring them into the same cluster.</p>
</div>
</details>

<details>
<summary>Cluster ID changes every few seconds.</summary>
<div>
<p>A relevant setting is being toggled — by a script, a connected projector adding / removing a sub-grid, or a Safe Zone whose permissions are flipping. Check the cluster member's settings against its neighbours; the field that differs each cycle is the one driving the churn.</p>
<p>Re-clustering every cycle is correct behaviour, but it is wasted work. Stabilise the toggle and the cluster will settle.</p>
</div>
</details>

<details>
<summary>I changed a setting and the new sort order took up to a minute to apply.</summary>
<div>
This was a real bug (BUG-260501.1) in versions before v2.5.4. When a setting toggle reshuffled the BaR into a different cluster, the new cluster's coordinator was not told to do an immediate rescan and the saturated-skip gate could suppress the scan for up to 60 seconds. Fixed in v2.5.4 — the forced-rescan flag is now propagated across cluster reshuffle. Update to v2.5.4 or later.
</div>
</details>

<details>
<summary>One BaR in my cluster is doing all the scanning and seems to lag more.</summary>
<div>
<p>That is the elected coordinator — it runs the scan for the entire cluster. The cost is on the background scan thread, not the main game thread, so it should not affect sim-speed. If it does, lower <code>MaxBackgroundTasks</code> in <code>ModSettings.xml</code> to limit parallel scans across the server.</p>
<p>The coordinator role rotates only when the current coordinator stops being a valid member (turned off, disabled, removed). To rotate manually, briefly disable the current coordinator — the next-lowest-EntityId member is elected.</p>
</div>
</details>

<details>
<summary>Disabling a BaR causes the cluster to fall apart for a moment.</summary>
<div>
A BaR that is disabled (turned off or unfunctional) is removed from its cluster on the next rebuild. If it was the coordinator, a new coordinator is elected from the remaining members. There can be a single scan cycle where members fall through to their own scans before the new coordinator takes over — this is brief (~1 s) and not visible in normal play.
</div>
</details>
