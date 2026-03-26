# FEAT-020: ServerTryWeldingGrindingCollecting main-thread cost reduction
## Status: Done (Won't Fix — Deferred)
## Priority: Low
## Resolution: Profiling (120s, 2026-03-17) shows 0.876ms steady avg with sim speed stable at 1.0. Lock contention exists but not impacting gameplay. Can revisit if BaR counts increase or sim speed degrades.
## Version: v2.5.0

## Summary
Investigate reducing the main-thread cost of `ServerTryWeldingGrindingCollecting` (641ms total, 0.431ms avg, 1488 calls).

## Motivation
Profiling (120s, 60 BaRs) shows this method is the largest main-thread consumer after the update loop itself. With 60 BaRs and 3-group staggering, ~20 BaRs run this per tick at 0.431ms avg = ~8.6ms/tick of main-thread time in this method alone. At sim speed 1.0 this is fine, but with more BaRs or lower sim speed it becomes a bottleneck.

## Design
Options to investigate:
1. **Profile sub-methods** — break down where the 0.431ms is spent (BuildActiveGridMap, lock acquisition, target iteration, grind/weld calls).
2. **Reduce lock contention** — minimize time spent holding locks on `PossibleWeldTargets` / `PossibleGrindTargets`.
3. **Skip idle BaRs faster** — early-out for BaRs with no targets before entering the full decision tree.
4. **Adaptive stagger group count** — increase stagger groups beyond 3 when system count is high.

## Profiling baseline (120s, 60 BaRs)
| Metric | Value |
|--------|-------|
| ServerTryWeldingGrindingCollecting calls | 1488 |
| ServerTryWeldingGrindingCollecting total | 641ms |
| ServerTryWeldingGrindingCollecting avg | 0.431ms |
| ServerTryGrinding total | 235ms |
| ServerDoGrind total | 193ms |

## Files Affected
- `NanobotSystem.Operations.cs` — `ServerTryWeldingGrindingCollecting`, `BuildActiveGridMap`
- `NanobotSystem.Grinding.cs` — `ServerTryGrinding`
- `NanobotSystem.Welding.cs` — `ServerTryWelding`

## Testing
1. Deploy 40+ BaRs, enable grinding on nearby grids
2. Run `/nanobars profile start 120`
3. Compare `ServerTryWeldingGrindingCollecting` avg with baseline (0.431ms)
4. Monitor sim speed — should remain at 1.0
