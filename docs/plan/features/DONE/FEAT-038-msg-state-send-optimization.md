# FEAT-038: MsgBlockStateSend Optimization — Reduce Network Sync Cost

## Status: Done
## Priority: Medium
## Version: v2.5.0

## Summary

Reduce `MsgBlockStateSend` main-thread cost (5-8ms/s) by skipping or throttling state sends for idle BaRs and reducing payload size.

## Motivation

Profiling across two 180s DS sessions shows consistent network sync cost:

| Metric | Weld-heavy | Grind-heavy |
|--------|-----------|-------------|
| Total ms | 1,361ms (7.6ms/s) | 1,039ms (5.8ms/s) |
| Calls | 2,039 (11.3/s) | 1,949 (10.8/s) |
| Avg per call | 0.67ms | 0.53ms |
| Max spike | 9.2ms | **12.9ms** |
| Bytes per msg | 4,917-6,539 | Similar |

This represents 5-13% of the mod's main-thread budget across both workloads. Each message serializes ~5KB via ProtoBuf and broadcasts to all connected clients (`steamId=0`).

### Profiling Evidence

```
# Session 1 (weld-heavy)
method=MsgBlockStateSend;calls=2039;totalMs=1360.599;avgMs=0.667;maxMs=9.169

# Session 2 (grind-heavy)
method=MsgBlockStateSend;calls=1949;totalMs=1039.165;avgMs=0.533;maxMs=12.915
```

## Design

### Option A: Skip Idle BaRs (Quick Win)

BaRs with no targets (`weldTargets=0, grindTargets=0, floatingTargets=0`) and no state change should send state much less frequently — e.g., every 10-15s instead of every 1-2s. Their state is static.

### Option B: Increase Interval for Unchanged State

Track whether `SyncBlockState` has actually changed since the last send. If nothing changed, extend the interval progressively (2s → 4s → 8s, capped). Reset to 1-2s on state change.

### Option C: Reduce Payload

The 5-6KB payload includes weld/grind target lists. For the network sync, consider:
- Truncating target lists to top-N items (clients only need a display preview)
- Omitting target lists entirely when the BaR isn't actively working

## Files Affected

| File | Change |
|------|--------|
| `NanobotSystem.State.cs` | Add idle detection, adaptive interval |
| `Models/SyncBlockState.cs` | Possibly add change-detection hash |
| `NanobotSystem.Update.cs` | Adjust transmit timing logic |

## Testing

1. Verify idle BaRs send state less frequently (check profiler call count reduction).
2. Verify actively working BaRs still update clients at 1-2s intervals.
3. Verify client terminal UI still shows correct state for idle BaRs (just updated less often).
4. Verify state changes (e.g., BaR starts working after being idle) are reflected promptly.
5. Profile before/after: target is 50%+ reduction in total MsgBlockStateSend cost.
