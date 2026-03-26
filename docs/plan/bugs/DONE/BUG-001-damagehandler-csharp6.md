# BUG-001: GetValueOrDefault() not available in C# 6

## Status: Fixed
## Severity: High
## Version: v2.5.0
## Found In: Code Review / Handlers/DamageHandler.cs:60

## Description

`DamageHandler.OnBeforeDamage()` calls `Mod.NanobotSystems.GetValueOrDefault(info.AttackerId)` at line 60. The `GetValueOrDefault()` extension method for `Dictionary<TKey, TValue>` was introduced in .NET Core 2.0 / C# 7.x and is **not available** in C# 6 / .NET Framework 4.8.

This will cause a **compilation error** if the method is not provided by a custom extension elsewhere in the codebase. If it compiles due to an extension method in scope, verify that extension behaves identically to the standard one (returns `default(TValue)` on miss).

```csharp
// DamageHandler.cs:60
var logicalComponent = Mod.NanobotSystems.GetValueOrDefault(info.AttackerId);
```

## Steps to Reproduce

1. Attempt to compile the project targeting .NET Framework 4.8 with C# 6 language version.
2. If no custom `GetValueOrDefault` extension is in scope, the compiler will emit error CS1061.

## Root Cause

Use of a C# 7+ / .NET Core API in a project constrained to C# 6 / .NET Framework 4.8 (Space Engineers modding requirement).

## Fix

Replace with the C# 6-compatible `TryGetValue` pattern:

```csharp
// Replace line 60:
NanobotSystem logicalComponent;
Mod.NanobotSystems.TryGetValue(info.AttackerId, out logicalComponent);
```

Alternatively, verify if a project-level extension method already provides `GetValueOrDefault` — if so, this bug can be closed as "Won't Fix" with a note explaining the extension.
