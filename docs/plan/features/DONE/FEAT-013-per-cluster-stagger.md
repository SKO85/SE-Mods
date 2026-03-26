# FEAT-013: Per-cluster stagger with gradual ramp
## Status: Done
## Priority: Medium
## Version: v2.5.0
## Summary
Stagger is now scoped to the cluster (co-located BaRs) instead of global BaR count, with a gradual ramp-up so small clusters aren't penalized. Max stagger capped at 3 groups (~500ms) instead of 5 (~833ms).

## Motivation
FEAT-012 introduced global staggering using `Mod.NanobotSystems.Count`. This caused two problems:
1. **Isolated BaRs penalized**: A server with 30 grids each having 1-2 BaRs would stagger all of them as if they were 30-60 co-located, throttling each to ~8.3s cycles despite no load contention.
2. **Small clusters over-staggered**: 4 co-located BaRs got `effectiveGroups=4`, firing only every 4th cycle (~667ms+), when 4 BaRs is too few to cause meaningful main-thread load.

## Design

### Per-cluster scope
Changed `effectiveGroups` from `Math.Min(StaggerGroupCount, Mod.NanobotSystems.Count)` to use `AssignedCluster.Members.Count`. BaRs not in a cluster (solo) default to clusterSize=1 (no stagger).

### Gradual ramp (StaggerGroupCount=3)
Instead of linear scaling, stagger only engages at 5+ BaRs with a gradual ramp to full stagger at 6+:

| Cluster size | effectiveGroups | Update frequency |
|---|---|---|
| 1-4 | 1 (no stagger) | Every cycle (~167ms) |
| 5 | 2 | Every 2nd (~333ms) |
| 6+ | 3 (full) | Every 3rd (~500ms) |

Formula: `clusterSize < 5 ? 1 : Math.Min(Mod.StaggerGroupCount, clusterSize - 3)`

Max interval capped at ~500ms. When server sim-speed drops, the game engine itself reduces tick rate — the mod doesn't need to throttle beyond ~500ms.

### Reactive delay reduction
Reduced max reactive delay from `Next(1, 20)` to `Next(1, 5)` since staggering is now the primary load-spreading mechanism. Worst-case reactive delay: ~833ms instead of ~3.3s.

## Files Affected
- `NanobotSystem.Update.cs` — `effectiveGroups` calculation changed to per-cluster with gradual ramp; reactive delay max reduced
- `Mod.cs` — `StaggerGroupCount` reduced from 5 to 3
