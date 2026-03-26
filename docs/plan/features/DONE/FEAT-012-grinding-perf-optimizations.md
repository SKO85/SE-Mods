# FEAT-012: Grinding performance optimizations
## Status: Done
## Priority: High
## Version: v2.5.0
## Summary
Five optimizations to reduce main-thread grinding lag with many BaRs (60+) targeting large ships.
## Motivation
Profiling 60 BaRs grinding a large ship for 120s revealed:
- ServerDoGrind spikes of 100-380ms on mechanical blocks (pistons, rotors, hinges)
- All 60 BaRs firing ServerTryWeldingGrindingCollecting on the same game tick
- Redundant weld sorts in GrindOnly mode
- Sorted block cache TTL shorter than scan interval (100% cache miss)

## Design

| Opt | Name | Location | Impact |
|-----|------|----------|--------|
| 1 | Mechanical block grind throttle | `Mod.TryClaimMechanicalGrindSlot()`, `NanobotSystem.Grinding.cs` | Caps mechanical destructions to 1/tick globally |
| 2 | BaR update staggering | `NanobotSystem.Update.cs`, `Mod.ClaimStaggerSlot()`, `NanobotSystem.Init.cs` | Distributes BaRs across 5 groups |
| 3 | Global grind budget per tick | `Mod.TryClaimGrindSlot()`, `NanobotSystem.Grinding.cs` | Max 10 ServerDoGrind calls/tick |
| 4 | Skip weld sort in GrindOnly mode | `NanobotSystem.Scanning.cs` (isGrinding short-circuit) | Avoids 2x-slower weld sort |
| 5 | Increased sorted block cache TTL | `NanobotSystem.cs` (30+rand(10)s) | Reduces sort calls ~50% |

### Additional fix during implementation
**Source update timing bug** (`NanobotSystem.Scanning.cs`): Cluster members only applied sources when both their own 30s timer AND the coordinator's `result.SourcesUpdated` flag were true. Timer drift caused members to miss source updates for minutes. Fixed: members now apply sources whenever `result.SourcesUpdated` is true.

## Files Affected
- `Mod.cs` — throttle/budget/stagger statics
- `NanobotSystem.cs` — `_staggerSlot` field, cache TTL change
- `NanobotSystem.Init.cs` — stagger slot assignment
- `NanobotSystem.Update.cs` — stagger gate on ServerTryWeldingGrindingCollecting
- `NanobotSystem.Grinding.cs` — mechanical throttle + grind budget checks
- `NanobotSystem.Scanning.cs` — isGrinding short-circuit + source update fix

## Profiler Results (180s, 60 BaRs, post-optimization)

### Before (120s baseline)
| Method | Calls | Total ms | Avg ms | Max ms |
|--------|-------|----------|--------|--------|
| ServerDoGrind | 1,399 | 1,801 | 1.28 | **379** |
| ServerTryGrinding | 3,515 | 1,984 | 0.56 | — |
| SortWithPriorityAndDistance | 314 | 114 | 0.37 | — |

### After (180s verified)
| Method | Calls | Total ms | Avg ms | Max ms |
|--------|-------|----------|--------|--------|
| ServerDoGrind | 863 | 437 | 0.506 | **12.4** |
| ServerTryGrinding | 1,295 | 516 | 0.398 | 12.6 |
| ServerTryWeldingGrindingCollecting | 1,295 | 974 | 0.752 | 13.0 |
| UpdateBeforeSimulation10_100 | 6,477 | 1,367 | 0.211 | 14.8 |
| SortWithPriorityAndDistance | 168 | 64 | 0.382 | 12.0 |

### Key improvements
- **Max grind spike: 379ms → 12.4ms** (30x reduction, Opt 1)
- **Grind calls/sec: 29.3 → 7.2** (75% reduction, Opt 2)
- **Sort calls: 314 → 168** (46% fewer, Opt 4+5)
- **Total main-thread usage: 0.78% over 180s** — no further optimization needed
