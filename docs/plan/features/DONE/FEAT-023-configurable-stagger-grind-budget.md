# FEAT-023: Configurable stagger, grind budget, and assignment TTL

## Status: Done
## Priority: High
## Version: v2.5.0

## Summary
Made performance tuning settings configurable via chat commands and `ModSettings.xml`, with auto-scaling defaults based on active BaR count.

## New Settings

| Setting | Default | Range | Description |
|---------|---------|-------|-------------|
| `StaggerGroupCount` | 0 (auto) | 0-10 | Max stagger groups. Auto: 1 for ≤4 active BaRs, 2 for 5-10, 3 for 11+ |
| `MaxGrindsPerTick` | 0 (auto) | 0-100 | Global grind budget per tick. Auto: scales with BaR count (min 5, max 10) |
| `AssignmentTtlSeconds` | 8 | 2-30 | Block assignment reservation TTL in seconds |

## Chat Commands
```
/nanobars config set StaggerGroupCount 2
/nanobars config set MaxGrindsPerTick 20
/nanobars config set AssignmentTtlSeconds 5
/nanobars config save
/nanobars config reload
/nanobars config reset
```

## Debug Panel
Terminal info panel now shows:
```
Total BaRs: 25 | Stagger: 1/3 (auto) | GrindBudget: 10 (auto)
```
Format: `effective/cap` for stagger, with `(auto)` indicator when using auto-scaling.

## Files Changed
- `Models/SyncModSettings.cs` — new properties, validation, defaults
- `Mod.cs` — `GetEffectiveStaggerGroupCount()`, `GetEffectiveMaxGrindsPerTick()`
- `NanobotSystem.Update.cs` — uses dynamic stagger value
- `NanobotSystem.CustomInfo.cs` — debug panel display
- `Handlers/BlockSystemAssigningHandler.cs` — configurable TTL
- `Chat/Commands/ConfigCommand.cs` — new settings registered
