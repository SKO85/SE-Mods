# BUG-032: ConfigCommand uses System.Reflection (prohibited by SE sandbox)
## Status: Fixed
## Severity: Critical
## Version: v2.5.0
## Found In: `Chat/Commands/ConfigCommand.cs`

## Summary
`ConfigCommand` used `PropertyInfo`, `Type.GetProperty()`, `Type.IsEnum`, and `prop.SetValue()` / `prop.GetValue()` from `System.Reflection`. The SE mod sandbox blocks these types, causing runtime sandbox violations.

## Fix
Rewrote `ConfigCommand` to use a delegate-based registry. Each setting is registered with explicit `Func<string> Get` and `Func<string, string> Set` delegates via typed builders (`BoolSetting`, `IntSetting`, `FloatSetting`, `EnumSetting`). No reflection used.

## Files Changed
- `Chat/Commands/ConfigCommand.cs`
