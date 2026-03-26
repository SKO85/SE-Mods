# BUG-031: Auto stagger counts disabled BaR blocks
## Status: Fixed
## Severity: Low
## Version: v2.5.0
## Found In: `Mod.cs` — `GetEffectiveStaggerGroupCount`

## Summary
The auto stagger calculation used `NanobotSystems.Count` which includes all placed BaR blocks regardless of enabled state. With 20 placed blocks but only 1 enabled, the auto value was 3 instead of 1.

## Fix
Changed `GetEffectiveStaggerGroupCount()` to iterate `NanobotSystems` and only count BaRs where `Welder.IsWorking` is true.

## Files Changed
- `Mod.cs`
