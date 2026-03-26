# FEAT-014: Sim-speed adaptive throttle & profiler sim-speed tracking
## Status: Done
## Priority: Medium
## Version: v2.5.0

## Summary
Three related changes: (1) BaR update stagger automatically increases when server sim-speed drops below 0.9, reducing mod load to help the server recover. (2) The method profiler now records sim-speed on every log entry and tracks min/max/avg sim-speed per session. (3) Sim-speed reading is centralized via `Mod.GetEffectiveSimSpeed()` to support the sim-speed override feature (FEAT-015).

## Motivation
On busy servers with many BaRs, the mod can contribute to sim-speed drops. Previously, BaRs would keep running at full cycle speed regardless of server health. Adding sim-speed awareness allows the mod to back off automatically when the server is struggling, and profiler sim-speed data enables correlating performance issues with server load during investigations.

## Design

### 1. Sim-speed adaptive throttle (`NanobotSystem.Update.cs`)

Uses `Mod.GetEffectiveSimSpeed()` which returns `MyAPIGateway.Physics.ServerSimulationRatio` (or the override if set via FEAT-015). When sim-speed drops below 0.9, a penalty is added to `effectiveGroups`, increasing the stagger and reducing how often each BaR runs `ServerTryWeldingGrindingCollecting()`.

**Formula:**
```csharp
var simSpeed = Mod.GetEffectiveSimSpeed();
if (simSpeed < 0.9f)
{
    var simPenalty = (int)Math.Ceiling((1.0 - simSpeed) * Mod.StaggerGroupCount);
    effectiveGroups = Math.Min(Mod.StaggerGroupCount, effectiveGroups + simPenalty);
}
```

**Behavior — how it affects cycle frequency:**

All BaRs in a cluster continue working, but each one's cycle speed is reduced. Stagger spreads their turns across more time slots so fewer `ServerTryWeldingGrindingCollecting` calls happen per tick. This reduces total weld/grind throughput proportionally but gives the server CPU headroom to recover.

**Penalty table** (StaggerGroupCount=3):

| Sim speed | Penalty | Description |
|---|---|---|
| >= 0.9 | 0 | No throttle — normal operation |
| 0.8 | +1 | Mild throttle |
| 0.5 | +2 | Moderate throttle |
| 0.3 | +3 | Maximum throttle (capped at StaggerGroupCount) |

**Combined effect with cluster-based stagger (FEAT-013):**

| Scenario | Sim 1.0 | Sim 0.8 | Sim 0.5 |
|---|---|---|---|
| 1 BaR alone (base=1) | Every cycle (~167ms) | Every 2nd (~333ms) | Every 3rd (~500ms) |
| 4 BaRs co-located (base=1) | Every cycle (~167ms) | Every 2nd (~333ms) | Every 3rd (~500ms, capped) |
| 6+ BaRs co-located (base=3) | Every 3rd (~500ms) | Every 3rd (~500ms, already capped) | Every 3rd (~500ms, already capped) |

Note: Large clusters (6+) are already at maximum stagger, so sim-speed penalty has no additional effect. The throttle primarily helps small clusters and isolated BaRs that would otherwise run at full speed during server stress.

**Key design points:**
- Threshold is 0.9 (not 1.0) to avoid triggering on minor fluctuations
- `effectiveGroups` is always capped at `StaggerGroupCount` (3) — the penalty cannot exceed maximum stagger
- No BaRs are disabled — all continue working, just at reduced frequency
- Recovery is immediate: when sim-speed returns above 0.9, penalty drops to 0 on the next cycle
- Read of `ServerSimulationRatio` is a simple property access, not a heavy API call

### 2. Profiler sim-speed tracking (`MethodProfiler.cs`)

**Per-entry logging:**
Every profiler log line now includes `simSpeed=X.XX` after the duration. This allows correlating individual method timing spikes with server sim-speed at that exact moment.

Log format changed from:
```
# utc;ms;details
2026-03-13 22:37:00Z;ms=0.456;entityId=12345;fast=True;ready=True;delay=0
```
To:
```
# utc;ms;simSpeed;details
2026-03-13 22:37:00Z;ms=0.456;simSpeed=1.00;entityId=12345;fast=True;ready=True;delay=0
```

**Session summary:**
The summary log now includes a sim-speed statistics line:
```
# simSpeed: min=0.85;max=1.00;avg=0.97;samples=15432
```

This shows the sim-speed range and average across the entire profiling session, making it easy to identify whether the server was under load during the profiling window.

**Tracking fields added:**
- `_minSimSpeed` — lowest sim-speed seen during session
- `_maxSimSpeed` — highest sim-speed seen during session
- `_sumSimSpeed` / `_simSpeedSamples` — for computing average
- All reset on `StartSession()`

### 3. Enhanced profiler details for UpdateBeforeSimulation10_100

The profiler details for the main update method now also include `clusterSize` and `effectiveGroups`, making it possible to verify stagger behavior in profiling logs:
```
entityId=12345;fast=True;ready=True;delay=0;clusterSize=4;effectiveGroups=1
```

## Files Affected
- `NanobotSystem.Update.cs` — sim-speed throttle in stagger logic; enhanced profiler details
- `Profiling/MethodProfiler.cs` — sim-speed tracking fields, per-entry logging, session summary stats
- `Mod.cs` — `GetEffectiveSimSpeed()` centralized helper

## API Reference
- `MyAPIGateway.Physics.ServerSimulationRatio` — returns `float`, 1.0 = normal speed, 0.5 = half speed. Available via `VRage.Game.ModAPI.IMyPhysics`.
