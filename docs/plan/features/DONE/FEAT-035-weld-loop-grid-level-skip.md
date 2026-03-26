# FEAT-035: Weld Loop Grid-Level Skip for Saturated Grids

## Status: Done
## Priority: High
## Version: v2.5.0

## Summary

Skip all blocks from a grid that has already reached `MaxSystemsPerTargetGrid` during the weld loop, instead of checking each block individually.

## Motivation

Profiling (180s DS, 118 BaRs, weld-heavy workload) shows that BaRs without lock-on iterate the entire weld target list every tick. Late in the session with 318 targets:

- `skipGrid=246` out of 318 blocks — 77% of iterations are wasted grid-limit checks
- BaRs without lock-on cost **5-14ms each** (vs 0.5-2ms for lock-on BaRs)
- 10 such BaRs per stagger group = 50-140ms per stagger tick

The root cause: `ServerTryWelding` checks `MaxSystemsPerTargetGrid` per-block. When 58 BaRs target the same large grid, most blocks hit the limit, but the loop still visits each one individually.

### Profiling Evidence

Early session (40 targets):
```
weldChecked=7, skipGrid=5, skipAssign=33 → 0.2ms per BaR
```

Late session (318 targets):
```
weldChecked=260-292, skipGrid=226-246, skipAssign=12-25 → 5-14ms per BaR
```

BaRs WITH lock-on (`skipLock=284, weldChecked=1`) cost only 0.5ms.

## Design

At the start of each `ServerTryWelding` call (or once per stagger tick), build a small `HashSet<long>` of grid EntityIds that are already at or above `MaxSystemsPerTargetGrid`. During the target iteration loop, check the block's `CubeGrid.EntityId` against this set before doing the per-block `CountSystemsOnGrid` check.

This turns 246 individual grid-limit lookups into a single set membership test per block.

### Alternative

Use the existing `BuildActiveGridMap()` output — it already counts systems per grid. If the count >= limit, mark the grid as saturated. No new data structures needed if the grid map already has the counts.

## Files Affected

| File | Change |
|------|--------|
| `NanobotSystem.Operations.cs` | Add grid saturation set; skip blocks from saturated grids in weld loop |
| `NanobotSystem.Welding.cs` | Possibly move grid-limit check to use saturation set |

## Testing

1. Place 50+ BaRs targeting same grid with `MaxSystemsPerTargetGrid=10`. Verify BaRs that can't claim a slot exit the weld loop quickly.
2. Profile before/after: `ServerTryWelding` cost for non-lock-on BaRs should drop from 5-14ms to 1-3ms.
3. Verify BaRs still correctly find blocks on OTHER grids that aren't saturated.
4. Verify lock-on BaRs are unaffected (they skip via `skipLock` already).
