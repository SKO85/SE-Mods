# SCN-BUDGET-006: Large-grid performance envelope (3000+ blocks, 5 BaRs)
## Area: BUDGET
## Tier: 4
## Preconditions
- Empty creative world (no other BaRs). A ~3000-block ship (`NanobotTest_BigShip` prefab once it
  exists; until then a 500-block code-built rig) with 500 blocks set to construction site.
- Rig with 5 large BaRs, `SearchMode = BoundingBox`, area 200 m, 60 m from the ship; cargo heavily
  stocked (SteelPlate, Construction, InteriorPlate, Computer, Motor, LargeTube, SmallTube, Girder).
- Default budgets (`MaxWeldMsPerTick 8`, `MaxGrindMsPerTick 8`, `StaggerGroupCount 0`).
## Steps
1. `/nanobars profile start 300 0 bigship` (or harness `test run S08 x5`).
2. Let the BaRs weld for 300 s; note sim speed on the HUD throughout.
3. Read `Summary.log`: `UpdateBeforeSimulation10_100`, `ServerDoWeld`, `ServerTryWelding`,
   `RebuildMissingComponentsAggregate`, `AsyncClusterScan`, `ApplyClusterResultToSelf`; and the HUD
   `Mod Tick Cost avg/peak`.
## Expected outcome
- ≥ 500 blocks welded within the run or a clear budget-limited rate (welds/s ≈ budget × 60 / stagger).
- No single-BaR spike pattern "on every new block selection" beyond the engine `proj.Build`/
  `IncreaseMountLevel` cost already characterised in BUG-105/108/122.
- `RebuildMissingComponentsAggregate` (new in v2.5.6, walks up to 1024 targets every 3 s per BaR)
  stays within its threshold below — this is the specific check for BUG-260909.1 (#143).
## Perf envelope
- `UpdateBeforeSimulation10_100` steadyAvgMs ≤ 1.5 per BaR; `ServerDoWeld` steadyAvgMs ≤ 3, maxMs ≤ 40.
- `RebuildMissingComponentsAggregate` maxMs ≤ 5 (per BaR, 1024-target cap).
- Mod tick cost avg ≤ 0.5 ms, peak ≤ 15 ms; sim speed min ≥ 0.8; `WeldBudgetPeakUsed ≤ GetEffectiveMaxWeldsPerTick()`.
- Thresholds are starting points — replace with the `agg=` min/avg/max from the first five green runs.
## Coverage
planned: ingame:S08
## Last verified
never
## Related
BUG-260909.1 (GitHub #143), BUG-105, BUG-108, BUG-122, BUG-126, BUG-129, BUG-260824.2 (aggregate), FEAT-260910.2
