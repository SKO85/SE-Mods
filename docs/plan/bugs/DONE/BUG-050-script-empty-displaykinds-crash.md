# BUG-050: Script crashes with DivideByZero on empty DisplayKinds array
## Status: Fixed
## Severity: Medium
## Version: v2.5.0
## Found In: Code review — SKO-Nanobot-BuildAndRepair-System-Script/Script.cs

## Description

In `RefreshDisplay()` at line 510:
```csharp
DisplayKindIdx[idx] = (DisplayKindIdx[idx] + 1) % settings.DisplayKinds.Length;
```

If a user configures `DisplayKinds = new DisplayKind[0]` (or `new DisplayKind[] { }`), the modulo is `% 0` which throws `DivideByZeroException`. The null check at line 506 does not guard against empty arrays.

## Fix

Add length > 0 check alongside null check:
```csharp
if (settings.DisplayKinds != null && settings.DisplayKinds.Length > 0 && RepairSystems != null)
```
