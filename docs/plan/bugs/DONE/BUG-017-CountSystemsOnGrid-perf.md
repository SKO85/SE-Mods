# BUG-017: CountSystemsOnGrid main-thread performance bottleneck
## Status: Fixed
## Severity: High
## Version: v2.5.0
## Found In: Profiler analysis — 120s grinding session with ~50+ BaR systems
## Description
`CountSystemsOnGrid` was called **197,742 times** (2,297ms total), iterating all ~110 NanobotSystems on every call. It was invoked per-target-block inside the grinding and welding loops, meaning each system re-counted the same grid dozens of times per tick.
## Steps to Reproduce
1. Place 50+ BaR systems targeting grids with 500+ blocks
2. Enable grinding mode
3. Run profiler for 120s
4. Observe CountSystemsOnGrid dominating main-thread cost
## Root Cause
The result of `CountSystemsOnGrid(gridEntityId)` is identical for all blocks on the same grid within a single tick, yet it was recomputed for every target block in the weld/grind loops. With 512 targets and 110 systems, that's ~56,000 iterations per system per tick.
## Fix
Replaced per-block `CountSystemsOnGrid()` with a per-tick cache pattern:

1. **`BuildGridSystemCountCache()`** — called once per tick before the weld/grind switch. Iterates all systems once, builds `Dictionary<long, int>` mapping gridEntityId → count.
2. **`GetCachedSystemCountOnGrid()`** — O(1) dictionary lookup replacing the O(N) iteration.
3. **Grid-level skip** — `lastRejectedGridId` in both grinding and welding loops skips consecutive blocks on the same rejected grid without even hitting the dictionary.

### Files Changed
- `Mod.cs` — added `GridSystemCountCache`, `BuildGridSystemCountCache()`
- `NanobotSystem.Operations.cs` — `GetCachedSystemCountOnGrid()`
- `NanobotSystem.Grinding.cs` — cached lookup + grid-level skip
- `NanobotSystem.Welding.cs` — cached lookup + grid-level skip

### Profiler After
| Method | Calls | Total ms | Avg ms |
|--------|-------|----------|--------|
| BuildGridSystemCountCache | 2,171 | 50.7 | 0.023 |
| ServerTryGrinding | 2,171 | 550 | 0.253 |
| CountSystemsOnGrid | 0 | 0 | — |
