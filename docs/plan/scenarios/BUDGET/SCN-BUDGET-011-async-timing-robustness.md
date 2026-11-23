# SCN-BUDGET-011: Async and timing robustness

## Area: BUDGET
## Tier: 2
## Preconditions
- Background scans run on BackgroundTaskQueue (MyAPIGateway.Parallel); results are swapped into the BaRs' lists on
  the main thread; claims, cooldowns and caches are time-based.
## Steps
1. Unit: queue under concurrent enqueue, worker cap, failing tasks, world unload; cluster result handover.
2. In game: `/nanobars test run @races` (S94-S98 claim robustness, S127-S129).
## Expected outcome
- Every queued task runs exactly once; never more workers than MaxBackgroundTasks; a failing task does not stop the
  queue; unload drops queued work and rejects new work until reset; idle workers exit.
- Readers never see a half-built cluster result.
- Grids deleted, settings flipped and BaRs toggled mid-scan cause no mod errors, no stuck BaRs, no claims left behind.
## Perf envelope
tick avg <= 1 ms during the churn.
## Coverage
unit:AsyncHandlingTests
ingame:S127
ingame:S128
ingame:S129
## Last verified
never
## Related
BUG-260610.15 (unload drain), BUG-260511.15 (fresh cluster results), FEAT-261122.4
