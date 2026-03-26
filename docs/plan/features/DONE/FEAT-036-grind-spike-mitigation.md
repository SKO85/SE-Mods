# FEAT-036: ServerDoGrind Spike Mitigation for Mechanical/Complex Blocks

## Status: Done
## Priority: High
## Version: v2.5.0

## Summary

Mitigate single-frame spikes caused by grinding blocks that trigger expensive SE engine operations.

## Motivation

Profiling (180s DS, 118 BaRs, grind-heavy workload with large grid) shows `ServerDoGrind` averaging 0.5ms but spiking to 10ms+. The grind budget system caps total grinds per tick by count, but doesn't account for individual grind cost variance.

## Investigation Results (Option C — Done)

Sub-timing profiling added to `ServerDoGrind` reveals **three distinct spike sources**:

### 1. `DecreaseMountLevel` — most frequent (4 of 8 spikes >5ms)

| Block | Total ms | decreaseMs | Note |
|-------|----------|------------|------|
| `LargeBlockArmorSlope2Base` | 8.1 | **7.98** | Plain armor block |
| `SmallControlPanel` | 7.5 | **6.97** | Small block |
| `HalfSlopeArmorBlock` | 7.2 | **6.92** | Plain armor block |
| `SmallBlockArmorSlope2Tip` | 5.0 | **4.75** | Plain armor block |

Not block-type-specific — plain armor blocks spike too. Caused by SE recalculating grid structural integrity or triggering internal events. Unpredictable.

### 2. `RazeBlock` — mechanical blocks (1 spike)

| Block | Total ms | razeMs | Note |
|-------|----------|--------|------|
| `LargePistonBase` | 8.1 | **7.65** | Grid separation on piston dismount |

Mechanical block destruction triggers subgrid detachment. Already partially mitigated by `TryClaimMechanicalGrindSlot` (caps to 1 per tick), but the first one still fires.

### 3. `ServerEmptyTransportInventory` — inventory push (1 spike)

| Block | Total ms | transportMs | Note |
|-------|----------|-------------|------|
| `TimerBlockSmall` | 6.5 | **5.85** | Inventory push after dismount |

Full containers cause search for alternatives. Relates to FEAT-037.

### 4. Unaccounted (FriendlyDamage loop) — 2 spikes

| Block | Total ms | Sum of sub-timings | Gap |
|-------|----------|-------------------|-----|
| `LargePistonTop` | 10.3 | ~0.7ms | **~9.6ms** |
| `LargeBlockConveyor` | 9.2 | ~0.4ms | **~8.8ms** |

Sub-timings sum to <1ms but total is 9-10ms. The cost is in the **FriendlyDamage loop** (iterates all ~60 BaR systems calling `GetUserRelationToOwner`) or the `emptying` path check, which are between the sub-timing markers.

## Design — Option A: Time-Based Grind Budget (Next Step)

Add a cumulative time budget alongside the existing count budget in `Mod.TryClaimGrindSlot()`. After each `ServerDoGrind`, report elapsed time. If total grind time for the tick exceeds `MaxGrindMsPerTick` (default ~8ms), deny further grind slots.

This caps worst-case per-tick grind cost from "multiple 5-10ms grinds stacking to 20-40ms" down to "one expensive grind then stop."

### Limitation

Cannot prevent the first expensive grind — it fires before the budget can react. But prevents additional expensive grinds from piling on in the same tick.

## Files Affected

| File | Change | Status |
|------|--------|--------|
| `NanobotSystem.Grinding.cs` | Sub-timing profiling in ServerDoGrind | Done |
| `Mod.cs` | Add time accumulator to TryClaimGrindSlot + ReportGrindTime | Pending |
| `NanobotSystem.Grinding.cs` | Call ReportGrindTime after ServerDoGrind | Pending |

## Testing

1. Deploy with sub-timing profiling, grind large grid with mechanical blocks.
2. Verify sub-timing fields appear in `NanobotProfiler.ServerDoGrind.log`.
3. After Option A: verify max single-tick grind cost drops below 16ms.
