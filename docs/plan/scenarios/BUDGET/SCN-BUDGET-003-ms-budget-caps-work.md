# SCN-BUDGET-003: Ms budget caps work even when count budget remains
## Area: BUDGET
## Tier: 1
## Preconditions
- `MaxWeldsPerTick = 20` (count budget generous), `MaxWeldMsPerTick = 2` (ms budget tight).
- Several BaRs welding projected blocks (`proj.Build` costs 5–10 ms each) in one stagger slot.
## Steps
1. Enable BaRs; watch the HUD `Weld Budget peak/max` and the profiler `ServerDoWeld` `buildMs`.
2. Raise `MaxWeldMsPerTick` to 30; repeat.
## Expected outcome
- With the tight ms cap, after the first expensive weld reports its time (`ReportTime`), further
  `TryClaim` calls in the same frame return false although the count budget has slots left — peak
  used stays well below 20.
- With the generous ms cap, peak used approaches the count cap.
- Same behaviour for grind via `MaxGrindMsPerTick`.
## Perf envelope
Mod tick cost peak ≈ `MaxWeldMsPerTick + MaxGrindMsPerTick` + engine overhead; never unbounded.
## Coverage
planned: unit:PerTickBudgetTests.MsCapWins_WhenCountBudgetRemains
## Last verified
never
## Related
FEAT-260910.3, BUG-105 / BUG-113 (`buildMs` root cause), CLAUDE.md "Performance Tuning"
