# SCN-BUDGET-008: Many BaRs on target ships of mixed sizes: weld, grind, mixed; large vs small clusters

## Area: BUDGET
## Tier: 3
## Preconditions
- Target ships (dynamic) of 25 / 150 / 600 blocks in a row: own damaged (weld) and/or derelict intact (grind,
  janitor NoOwnership). BaR rigs on two lines around them, every BaR's 200 m area covering every target.
- Large clusters: 2 rigs x 10 BaRs; small clusters: 10 rigs x 2 BaRs; stress: 4 rigs x 10 BaRs, mixed work.
## Steps
1. `test run S64-S70` (75 s each, or until all work is done); `x3` for stable numbers.
2. Compare throughput per size (welded / ground per second), BaR utilisation, cluster count, tick cost
   (avg / peak), sim speed, and the profiled weld / grind / scan / cluster methods between cluster layouts.
## Expected outcome
- Every target ship gets work (no starvation of small or large ships); at least 75 % of the BaRs work.
- Tick avg <= 2 ms, peak <= 50 ms, sim >= 0.5 (per-tick weld / grind budgets hold under load).
- Small clusters scan more (one scanner per cluster) but must not multiply tick cost; differences become tickets.
## Perf envelope
harness metrics; thresholds above are sanity bounds, tighten from the first green runs.
## Coverage
ingame:S64
ingame:S65
ingame:S66
ingame:S67
ingame:S68
ingame:S69
ingame:S70
## Last verified
v2.5.6 build 261122.16 2026-11-22 — in game (SP): S64-S70 green. Tick avg 0.03-0.07 ms, peak <= 7.8 ms, sim >= 0.98,
100 % of BaRs worked, every ship finished. Large clusters slower than small (weld 35.9 vs 55.7 /s, grind 20.4 vs
37.5 /s, mixed 13.6 vs 21.0 /s) — BUG-261122.14. weldBudgetPeak 2, ServerDoWeld max 9-15 ms.
v2.5.6 build 261122.27 2026-11-22 — after BUG-261122.14: 2 x 10 = 10 x 2 (weld 59.8 / 62.1 /s, grind 38.4 / 36.7 /s),
tick avg <= 0.08 ms, sim 1.00.
## Related
FEAT-260910.2, BUG-260909.1 (#143), SCN-BUDGET-006, SCN-CLUSTER-001
