# BUG-035: Busy-wait infinite loop in Mod.UnloadData with no timeout
## Status: Fixed
## Severity: High
## Version: v2.5.0
## Found In: Mod.cs:204-213

## Description

`UnloadData()` uses a `while (true)` busy-wait loop to drain background tasks:

```csharp
while (true)
{
    int actualBackgroundTaskCount;
    lock (AsynActions)
    {
        actualBackgroundTaskCount = ActualBackgroundTaskCount;
    }
    if (actualBackgroundTaskCount <= 0) break;
}
```

If any background task hangs or never completes, this loop spins forever on the main thread with no timeout, no yield, and no escape. This could freeze the game during world unload or session exit.

## Root Cause

No timeout or iteration limit on the drain loop.

## Fix

Add a timeout (e.g. 5 seconds) so the loop gives up and proceeds with cleanup even if tasks are still running:

```csharp
var deadline = DateTime.UtcNow.AddSeconds(5);
while (DateTime.UtcNow < deadline)
{
    int actualBackgroundTaskCount;
    lock (AsynActions)
    {
        actualBackgroundTaskCount = ActualBackgroundTaskCount;
    }
    if (actualBackgroundTaskCount <= 0) break;
}
```
