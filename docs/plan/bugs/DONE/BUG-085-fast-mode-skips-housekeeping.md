# BUG-085: Fast mode (multiplier > 10) skips critical housekeeping

## Status: Fixed
## Severity: High
## Version: v2.5.0
## Found In: NanobotSystem.Update.cs — `if (!fast)` guards in UpdateBeforeSimulation10_100

## Description

When `WeldingMultiplier` or `GrindingMultiplier` exceeds 10, the system switches from `UpdateBeforeSimulation100` to `UpdateBeforeSimulation10` and passes `fast=true`. The `if (!fast)` guards then skip four housekeeping operations permanently:

1. **`Settings.TrySave()`** — block settings never persist to storage. Players lose terminal changes on world reload.
2. **`CleanupFriendlyDamage()`** — stale entries in the `FriendlyDamage` dictionary accumulate indefinitely (memory leak).
3. **`resourceSink.Update()`** — power draw display goes stale, never recalculated.
4. **`TryTransmitState()`** (v2.4.4 only) — state never syncs to clients; players see no welding/grinding progress. Main branch mitigated this by also calling `TryTransmitState()` from `ServerTryWeldingGrindingCollecting()`.

All four operations already have internal time-based throttles (elapsed timers, dirty flags, cooldowns), so the `!fast` guard was redundant. Running them on every Update10 tick just means the cheap time-check runs 10x more often; the actual work fires at the same real-time rate regardless.

## Steps to Reproduce

1. Set `WeldingMultiplier` to any value > 10 (e.g., 10.1).
2. Change a block setting in the terminal (e.g., toggle a priority).
3. Reload the world — setting is lost.
4. (v2.4.4) Observe that clients never see BaR state updates.

## Root Cause

The `fast` flag was added as a quick optimization to avoid running housekeeping 10x more often when on Update10, but the developers didn't account for the fact that these operations were already self-throttled internally.

## Fix

Removed the `fast` parameter entirely as part of FEAT-066. All housekeeping now runs unconditionally — their internal throttles handle the rate limiting. See FEAT-066 for full details.
