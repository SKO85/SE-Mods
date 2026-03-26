# Review: Profiling Analysis — 120s session, 118 BaRs
## Phase: Post-implementation
## Reviewer: AI
## Date: 2026-03-18
## Version: v2.5.0

## Session Overview

| Metric | Value |
|--------|-------|
| Duration | 120s |
| Total BaR entities | 118 |
| Active per stagger group | ~40 |
| Stagger groups (effective) | 3 |
| Sim speed | avg 1.01 (min 0.97, max 1.05) — **healthy** |
| Weld targets | 32–57 (growing over session) |
| Grind/float targets | 0 |

## Overall Verdict: Healthy

Sim speed is rock-solid at 1.0. No stutters or degradation over the 120s session. The mod is operating well within performance budget at this scale (118 BaRs, ~50 targets). No critical issues found.

The lock-on fix is confirmed working: no `lockOnPreserved` in the logs, `lockOnLost=True` count of 23 shows retry mechanism firing correctly, zero component failures (test had sufficient resources).

## Method Breakdown (sorted by total time)

| Method | Total ms | Avg ms | Calls | Notes |
|--------|----------|--------|-------|-------|
| UpdateBeforeSimulation10_100 | 2746 | 0.321 | 8499 | Envelope for all sub-methods |
| ServerTryWeldingGrindingCollecting | 1965 | 0.688 | 2833 | Main decision tree |
| **ServerTryWelding** | **918** | **0.324** | **2833** | **See Finding 1** |
| ServerDoWeld | 343 | 0.732 | 469 | SE API cost, irreducible |
| AsyncApplyClusterResults | 206 | 0.153 | 1344 | See Finding 3 |
| AsyncClusterScan | 205 | 9.318 | 22 | Background thread, fine |
| MsgBlockStateSend | 170 | 0.122 | 1392 | Network sync, proportional |
| BuildGridSystemCountCache | 118 | 0.047 | 2545 | Tick-guarded, acceptable |
| ServerFindMissingComponents | 115 | 0.245 | 469 | Component sourcing |
| Weldable | 36 | 0.003 | 13646 | Very cheap per call |

## Findings

### Finding 1: Idle BaRs iterate full target list (low impact)

- 2833 total `ServerTryWelding` calls
- 469 actually welded (16.5%) — only 30 of 118 BaRs ever welded
- 923 idle despite having targets (32.6%)
- 795 blocked primarily by assignment (28.1%)
- 1441 had zero targets — instant exit (50.9%, ~0.002ms each)

BaRs without lock-on iterate all 32-57 targets. Typically 9 blocks pass the assignment pre-check and get `Weldable()` calls (all already complete), while 22 blocks are skipped by `IsAssignedToOtherSystem()`. Cost: 0.2-0.5ms per idle call.

Total waste: The 219 worst cases (skipAssign >= 20) collectively cost ~73ms over 120s = **0.6ms/s**. Negligible at current scale.

Duration histogram for ServerTryWelding:
- <0.1ms: 1470 calls (51.9%) — no targets, instant exit
- 0.1-0.5ms: 842 calls (29.7%) — idle BaRs checking targets
- 0.5-2.0ms: 505 calls (17.8%) — active welding
- 2.0-6.0ms: 16 calls (0.6%) — occasional spikes

**Assessment:** Not worth optimizing yet. Per-call cost is low and sim speed unaffected. If BaR counts exceed 200+ with large target lists, could become relevant. Future optimization: early-exit counter for consecutive assignment skips (similar to `componentFailures >= 3`).

### Finding 2: BuildGridSystemCountCache — efficient

Called 2545 times at 0.047ms average (118ms total). Tick guard ensures rebuild only once per tick (~0.09ms). Subsequent same-tick calls are guard-check-only (~0.04ms). Only 1 call out of 2545 exceeded 1ms.

**Assessment:** Working as designed. No action needed.

### Finding 3: AsyncApplyClusterResults spikes with updateSource=True

34 of 1344 calls (2.5%) take >= 1ms. Worst spikes (3-4ms) during `updateSource=True` — source inventory list updates on main thread when background scan completes.

**Assessment:** Expected behavior during scan-result handoff. Spikes infrequent, no sim speed impact. No action needed.

### Finding 4: String allocation in IsAssignedToOtherSystem (future concern)

Each `IsAssignedToOtherSystem()` call allocates a string via `string.Format("{0}:{1}", gridEntityId, position)` for cache key lookup. ~22 calls per idle BaR × ~30 idle BaRs per tick = ~660 small string allocations per tick contributing to GC pressure.

**Assessment:** Not impactful at current scale. If scaling to 300+ BaRs, consider switching to a struct key (`long gridId` + `Vector3I position` composite) or pre-computed hash in `BlockSystemAssigningHandler.cs`.

## Recommendations

- No changes needed at current scale (118 BaRs, sim speed 1.0)
- Run next profiling test with component-starvation scenario to validate lock-on fix under load
- Monitor Finding 1 and Finding 4 if scaling beyond 200+ BaRs

## Action Items

- None — system performing within acceptable bounds
