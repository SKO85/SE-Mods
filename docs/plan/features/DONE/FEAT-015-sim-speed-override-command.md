# FEAT-015: Sim-speed override command for testing
## Status: Done
## Priority: Medium
## Version: v2.5.0

## Summary
Admin-only chat command `/nanobars sim <value>` to force a simulated sim-speed for the mod, enabling testing of throttle behavior without needing to actually degrade server performance.

## Motivation
The sim-speed adaptive throttle (FEAT-014) changes BaR behavior under low sim-speed, but reproducing low sim-speed in a test environment is difficult. This command lets admins force the mod to behave as if sim-speed is at a specific value, making it possible to verify throttle behavior and tune performance on a healthy server.

## Design

### Command syntax
- `/nanobars sim` — Shows current override value and real server sim-speed
- `/nanobars sim <0.1-1.0>` — Sets the override (e.g. `/nanobars sim 0.5`)
- `/nanobars sim reset` — Removes the override, returns to using real server sim-speed

### Access control
- Admin-only (Admin, SpaceMaster, or Owner promote level)
- Server-side only (not allowed on clients)
- Not listed in the help dialog (admin-only testing tool)

### Implementation
- `Mod.SimSpeedOverride` (`float?`, default null) — stores the override value
- `Mod.GetEffectiveSimSpeed()` — returns the override if set, otherwise `MyAPIGateway.Physics.ServerSimulationRatio`
- All sim-speed reads in the update loop go through `GetEffectiveSimSpeed()`

### Tick-rate simulation
When the override is active and below 1.0, an additional turn-skip simulates the reduced game tick rate that would naturally occur at low sim-speed. At real low sim-speed the game itself runs fewer ticks, but with the override ticks run at normal rate — so the mod must skip turns to compensate.

```csharp
if (isMyTurn && Mod.SimSpeedOverride.HasValue && Mod.SimSpeedOverride.Value < 1.0f)
{
    var skipInterval = (int)Math.Round(1.0 / Mod.SimSpeedOverride.Value);
    if (skipInterval > 1)
        isMyTurn = (cycle % skipInterval) == 0;
}
```

This only applies when the override is active. Real low sim-speed already reduces tick rate naturally, so no additional skip is needed there.

| Override value | skipInterval | Effect |
|---|---|---|
| 1.0 | 1 | No skip (normal) |
| 0.5 | 2 | Every 2nd cycle |
| 0.33 | 3 | Every 3rd cycle |
| 0.2 | 5 | Every 5th cycle |
| 0.1 | 10 | Every 10th cycle |

## Files Affected
- `Mod.cs` — `SimSpeedOverride` field, `GetEffectiveSimSpeed()` helper
- `NanobotSystem.Update.cs` — tick-rate simulation skip when override is active
- `Handlers/ChatHandler.cs` — `sim` command handler, `IsAdminOnServer()` helper

## Testing
1. `/nanobars sim` — should show "off" and real sim-speed
2. `/nanobars sim 0.5` — BaRs should visibly slow down (roughly half speed)
3. `/nanobars sim 0.1` — BaRs should barely operate
4. `/nanobars sim reset` — BaRs should return to normal speed
5. Non-admin player should get "Command requires admin permissions"
