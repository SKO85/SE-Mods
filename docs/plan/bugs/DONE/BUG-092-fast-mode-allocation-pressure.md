# BUG-092: Fast mode (WorkSpeed ≥ 5) triggers periodic main-thread pauses on large clusters

## Status: Won't Fix (workaround documented; stale; superseded by intervening allocation work)
## Severity: Medium (only at WorkSpeed ≥ 5 on large clusters — workaround is to use default WorkSpeed)
## Version: v2.5.2 (discovered)
## Found In: Profiling sessions `20260410221346`, `20260410222824`, `20260410223437` — 58-BaR cluster, Testing world

## Resolution: Won't Fix

After review, this ticket is closed without a code-level fix. The original repro data is from v2.5.2; substantial allocation-pressure work has landed since (BUG-098 hot-path allocations, BUG-110 cluster-scan collection pooling, BUG-111 candidate class→struct, BUG-117 grid-relation cache fast path, BUG-119 HashSet dedup + connection cache), much of which directly targets the per-tick allocation patterns this ticket flagged as suspects. The original repro can no longer be reasonably attributed to the same root causes without re-profiling, and the GC hypothesis was always indirect (System.GC.* is sandbox-prohibited so it can't be confirmed in-mod anyway).

The practical workaround — keep `WorkSpeed=1` on servers with 10+ BaRs — already exists and matches default settings. Players opting into `WorkSpeed ≥ 5` on large clusters are explicitly trading sim-stability for speed and accept the trade-off.

If a fresh fast-mode profile in a future version reproduces the same signature, file a new ticket with current data rather than re-opening this one.

## Original description (preserved for context)

## Description

On large clusters (≥50 BaRs) with `WorkSpeed` set to fast mode (`10` in these tests), the server exhibits periodic main-thread pauses ranging from ~100–170 ms. The pauses manifest as an anomalously high measured time on a **stagger-throttled** `UpdateBeforeSimulation10_100` call — a code path that normally takes under 0.1 ms because it exits early without running any work. The Stopwatch captures the pause because it was running across the thread suspension.

At the default `WorkSpeed=1`, the same world, same cluster, same mods produces a clean profile: `UpdateBeforeSimulation10_100` max 14.3 ms on genuine fired work, no stagger-path spikes, sim speed min 0.97.

The pattern is **recurring and slightly worsening** across repeated fast-mode sessions:

| Session | Sim min | `UpdateBeforeSimulation10_100` max (stagger) |
| --- | --- | --- |
| `20260410211525` (workSpeed=1) | 0.97 | ~0 — no stagger spike |
| `20260410221346` (workSpeed=10 dominant) | 0.88 | **119.3 ms** |
| `20260410222824` (workSpeed=10 dominant) | 0.83 | **168.1 ms** |
| `20260410223437` (workSpeed=1 only) | 0.97 | ~0 — no stagger spike |

## Steps to Reproduce

1. Load a test world with ≥50 BaR blocks in one cluster (same ownership/settings).
2. Set `WorkSpeed=10` via `/nanobars config set WorkSpeed 10`.
3. Run an active weld/grind workload that exercises most BaRs.
4. `/nanobars profile start 120`.
5. After the profile ends, grep `UpdateBeforeSimulation10_100.log` for lines where `throttle=stagger` and `ms > 50`.
6. Observe single-BaR stagger-path entries with 100–170 ms measured durations; sim speed min dips to 0.83–0.88.

Switch `WorkSpeed` back to `1` and re-run: the anomaly disappears.

## Root Cause (hypothesis)

The stagger-exit path inside `UpdateBeforeSimulation10_100` (`NanobotSystem.Update.cs`) is a handful of arithmetic ops and an early `return`. 119–168 ms on this path is physically impossible from user code — it has to be an external pause captured inside the Stopwatch interval.

Cross-method evidence from session `20260410222824` at the 168 ms timestamp (`22:30:02`):

- `Mod.UpdateBeforeSimulation` (the per-frame parent wrapper) at the same second: top entry **0.009 ms** — the frame loop itself was fine.
- `AsyncClusterScan` at the next second: 57 ms — normal for this cluster, nothing unusual on the background thread.
- Next `UpdateBeforeSimulation10_100` ticks in the same second: **2.5–5 ms each** (normal is 0.05–0.2 ms). Classic "CPU cache cold after a long stall" tail.

This isolated-single-BaR signature with a subsequent cold-cache tail is a textbook signature of a .NET gen2 GC pause (SE runs .NET 4.8 workstation GC, gen2 collections typically run 50–250 ms and freeze the entire main thread).

The fact that the pause signature only appears in fast-mode sessions strongly implicates allocation volume as the trigger: fast mode runs the hot update paths 10x more often per second, so any per-tick allocation on those paths becomes 10x more pressure on gen0, promotes to gen1/gen2 faster, and triggers gen2 collections sooner. Several scattered smaller spikes in `Weldable`, `ServerTryCollectingFloatingTargets`, `ServerFindMissingComponents`, etc. in the same sessions also show the same "impossible on fast-exit path" signature at smaller scales, consistent with multiple scattered GC events over the 120 s session.

We cannot confirm the GC hypothesis directly because `System.GC.*` is not on the SE sandbox whitelist. A Torch plugin (runs outside the sandbox) with `GC.CollectionCount` instrumentation correlated to mod log timestamps would give definitive proof.

## Observed Symptoms

- Single-BaR `UpdateBeforeSimulation10_100` entries with `throttle=stagger` and `ms > 50`.
- Brief sim speed dips (e.g. 1.01 → 0.83 → 0.90 → 1.01 within one or two frames).
- Per-method max times inflated across multiple methods at different timestamps: `Weldable` 10 ms for `result=False`, `ServerTryCollectingFloatingTargets` 9 ms for `targets=0`, `TryTransmitState` 3.8 ms (normally 0.06 ms max), etc.
- Impact on sim speed AVERAGE is small (sessions still avg 1.01) because the pauses are brief relative to the 120 s window, but players on a 0.83 tick will notice a visible hitch.

## Scope

Affected:
- Multiplayer / dedicated servers with `WorkSpeed ≥ 5` set.
- Large clusters (50+ BaRs observed; the threshold for triggering visible pauses is not known).

Not affected:
- Default `WorkSpeed=1` sessions — profile data is clean on the same world.
- Solo / small clusters (≤10 BaRs) — not reproduced; would need targeted testing.
- The BUG-088 / BUG-089 / BUG-091 fixes in v2.5.2 — profiling confirms these changes REDUCED per-call cost and do not contribute to allocation pressure.

## Fix direction (to do later)

Not attempting a fix in this ticket — tracking only. When picked up, the investigation should:

1. **Verify the GC hypothesis.** Easiest route: build a minimal Torch plugin that logs `GC.CollectionCount` per generation every second, run a fast-mode profiling session, correlate gen2 bumps against mod spike timestamps. If they line up, GC is confirmed.
2. **Audit per-tick allocations in the hot paths that run 10x more often in fast mode.** Candidates:
   - `NanobotSystem.Operations.cs` — `ServerTryWeldingGrindingCollecting` main dispatch.
   - `NanobotSystem.Welding.cs` — weld loop, any LINQ or `string.Format`, any `new List<>`.
   - `NanobotSystem.Grinding.cs` — grind loop.
   - `NanobotSystem.Inventory.cs` — inventory iteration, per-tick `_TempInventoryItems` handling (check whether `GetItems` allocates).
   - `NanobotSystem.Update.cs` — the tick dispatch itself.
   - `ServerFindMissingComponents` — shows inflated maxes, worth a read.
3. **Look for long-lived collections that grow slowly.** Monotonically-growing TTL caches or dictionaries can push objects into gen2 over a long session, explaining why the max spike gets WORSE between sessions (119 ms → 168 ms).
4. **Consider tightening the `WorkSpeed` documentation** to recommend `1–3` for multi-BaR servers and flag higher values as "single-BaR / small cluster only" until the allocation pressure is addressed.

## Workarounds until fixed

- Keep `WorkSpeed` at the default `1` on servers with 10+ BaRs.
- Players who want faster grinding/welding for themselves can still bump `WorkSpeed` on a small personal grid without noticeable impact.

## See also

- FEAT-066 — introduced the `WorkSpeed` multiplier that enables fast mode. BUG-092 is a latent issue in that feature, not a regression from v2.5.2 work.
- Profile sessions in SE storage: `20260410221346-profiling`, `20260410222824-profiling`, `20260410223437-profiling` at `%APPDATA%\SpaceEngineers\Storage\SKO-Nanobot-BuildAndRepair-System-Testing_SKO-Nanobot-BuildAndRepair-System\` for raw data.
