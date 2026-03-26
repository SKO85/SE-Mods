# FEAT-037: ServerTryPushInventory Optimization During Grinding

## Status: Done
## Priority: Medium
## Version: v2.5.0

## Summary

Reduce `ServerTryPushInventory` cost during grind-heavy workloads where it increases 7x compared to weld-heavy scenarios.

## Motivation

Profiling across two 180s DS sessions shows a dramatic cost increase when grinding:

| Metric | Weld-heavy | Grind-heavy |
|--------|-----------|-------------|
| Total ms | 121ms (0.7ms/s) | **887ms (4.9ms/s)** |
| Per-call avg | 0.029ms | **0.209ms** |
| Max spike | 0.6ms | **16.1ms** |
| Push targets | N/A | 27 per grinding BaR |

Grinding produces components, ingots, and ores that must be pushed to cargo containers. Each grinding BaR pushes to 27 targets. The 16ms max spike suggests inventory transfers occasionally trigger expensive SE inventory operations (container full → search for next target).

### Profiling Evidence (Session 2)

```
method=ServerTryPushInventory;calls=4247;totalMs=886.886;avgMs=0.209;maxMs=16.130
```

Some push calls cost 0.001ms (no items to push), others cost 1.3ms (active material transfer). The variance correlates with whether `transporting=True` (has materials from grind) or not.

## Design

### Option A: Push Backoff on Full Targets

Track which push targets are full. When a push fails because the target inventory is full, mark it with a short cooldown (e.g., 5-10s). Skip full targets on subsequent push attempts until the cooldown expires.

### Option B: Batch Push Per Stagger Group

Instead of each BaR independently pushing materials, consolidate push operations per stagger tick. If multiple BaRs have the same push target set, batch their pushes.

### Option C: Throttle Push Frequency During Grinding

Reduce push frequency from every-tick to every-other-tick when the BaR's inventory isn't near-full. The inventory has a buffer capacity — it doesn't need to push after every single grind.

## Files Affected

| File | Change |
|------|--------|
| `NanobotSystem.Inventory.cs` | Add push-target cooldown tracking |
| `NanobotSystem.Operations.cs` | Adjust push frequency/throttle |

## Testing

1. Profile push cost before/after with 50+ BaRs grinding a large grid.
2. Verify materials still flow to cargo containers without significant delay.
3. Verify BaR inventory doesn't fill up and stall grinding.
4. Verify push-full backoff correctly resumes when target has space again.
