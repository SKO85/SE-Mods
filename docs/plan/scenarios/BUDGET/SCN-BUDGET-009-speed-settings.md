# SCN-BUDGET-009: Speed settings and throughput floors

## Area: BUDGET
## Tier: 2
## Preconditions
- One BaR with plenty of weld (own damaged) or grind (derelict) work.
## Steps
1. `test run S79,S80` (three phases each, settings switched live).
2. Throughput floors run with S08, S43, S64-S67.
## Expected outcome
- WorkSpeed 10 vs 1: ~10x the work rate (actions per second); WeldingMultiplier / GrindingMultiplier 0.2 vs 0.1: ~2x
  (work per action). Absolute rates recorded as a baseline.
- Load / perf scenarios stay above ~70 % of the 2026-11-22 throughput (dev PC): S08 11, S43 10, S64 42, S65 43,
  S66 27, S67 26 blocks per second — a throughput regression (like BUG-261122.14) fails a test.
## Coverage
ingame:S79
ingame:S80
## Last verified
v2.5.6 build 261122.28 2026-11-22 — in game: weld x9.4 / x2.0 (1562 integrity/s at WorkSpeed 10, multiplier 0.1), grind x9.5 / x1.9 (919/s).
## Related
BUG-261122.14, SCN-BUDGET-006, SCN-BUDGET-008
