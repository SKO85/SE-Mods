# BUG-044: Effects.cs uses System.Threading.Interlocked (sandbox violation)
## Status: Fixed
## Severity: Medium
## Version: v2.5.0
## Found In: Code review — Effects.cs

## Description

`Effects.cs` line 7 imports `using System.Threading;` and uses `Interlocked.Increment/Decrement` at lines 121, 140, and 340. The `System.Threading` namespace is prohibited by the SE sandbox.

This may currently work if the SE whitelist permits `Interlocked` specifically, but it violates the stated project constraint and could break if Keen tightens the sandbox whitelist.

**Affected lines:**
- Line 7: `using System.Threading;`
- Line 121: `Interlocked.Decrement(ref _ActiveWorkingEffects);`
- Line 140: `Interlocked.Increment(ref _ActiveWorkingEffects);`
- Line 340: `Interlocked.Decrement(ref _ActiveTransportEffects);` / `Interlocked.Increment(ref _ActiveTransportEffects);`

## Root Cause

The static counters `_ActiveTransportEffects` and `_ActiveWorkingEffects` use `Interlocked` for thread-safe increment/decrement. However, effects are only managed on the main thread (UpdateEffects is called from the main update loop), so atomic operations are unnecessary.

## Fix

Replace `Interlocked.Increment/Decrement` with plain `++`/`--` and remove `using System.Threading;`:
```csharp
// Before:
Interlocked.Increment(ref _ActiveWorkingEffects);
// After:
_ActiveWorkingEffects++;
```

All effect management runs on the main thread, so no synchronization is needed.
