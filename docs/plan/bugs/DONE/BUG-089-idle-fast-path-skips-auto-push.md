# BUG-089: Idle fast-path skips auto-push, leaving items stuck in the welder inventory

## Status: Fixed
## Severity: Medium
## Version: v2.5.2
## Found In: `NanobotSystem.Operations.cs:68-75` — `ServerTryWeldingGrindingCollecting` idle fast-path (FEAT-039)

## Description

When a BaR has any of the "Push items immediately" options enabled and goes idle with leftover items in its welder inventory (typically after a grind cycle finishes or the area is cleaned up), the auto-push never runs. Items remain in the welder inventory indefinitely until the BaR re-acquires a target and takes the non-idle branch again.

On large bases where most BaRs are idle most of the time, this looks like the auto-push setting is broken.

## Steps to Reproduce

1. Enable `Push Components Immediately` (or `Push Ore/Ingots Immediately`, or `Push Items Immediately`) in the BaR terminal.
2. Have the BaR grind something so items accumulate in its welder inventory.
3. Let the BaR finish grinding and go idle (no weld/grind/collect targets remaining).
4. Observe: the items remain in the welder inventory indefinitely. `ServerTryPushInventory` is never called, so the items never transfer to the configured push targets.

## Root Cause

`FEAT-039` added an "idle fast-path" to `ServerTryWeldingGrindingCollecting` that skips all sub-method dispatch (including `ServerTryPushInventory`) when the BaR has nothing actively to do. The condition was:

```csharp
var isIdleNoWork = State.PossibleWeldTargets.CurrentCount == 0
    && State.PossibleGrindTargets.CurrentCount == 0
    && State.PossibleFloatingTargets.CurrentCount == 0
    && State.CurrentTransportStartTime <= TimeSpan.Zero
    && _TransportInventory.CurrentVolume == 0
    && !State.InventoryFull;

if (!isIdleNoWork)
{
    ServerTryPushInventory();
    ...
}
```

The check on the last line uses `State.InventoryFull` — but that flag is only true when the welder inventory is **completely full**. A welder with leftover items but plenty of free space has `InventoryFull == false`, so the fast-path engages and `ServerTryPushInventory` is never called. Auto-push silently stops working while the BaR is idle.

`ServerTryPushInventory` itself (`NanobotSystem.Inventory.cs:22`) has the right enablement guards and a 5–10s cooldown, so calling it on every idle tick would not be expensive — it just never gets called at all.

## Fix

Add a guarded check to the idle fast-path: if any push flag is set **and** the welder inventory is non-empty, bail out of the fast-path and run the normal dispatch (which calls `ServerTryPushInventory`). The `_Welder.GetInventory(0)` read is gated on the push-flag check so the hot path (BaRs with active targets) pays nothing extra.

### Code change (NanobotSystem.Operations.cs)

After computing `isIdleNoWork`:

```csharp
// BUG-089: Don't take the idle fast-path when auto-push is enabled and
// the welder still has leftover items — otherwise ServerTryPushInventory
// never runs and items from the last grind cycle pile up indefinitely.
if (isIdleNoWork
    && (Settings.Flags & (SyncBlockSettings.Settings.PushIngotOreImmediately | SyncBlockSettings.Settings.PushComponentImmediately | SyncBlockSettings.Settings.PushItemsImmediately)) != 0)
{
    var welderInv = _Welder.GetInventory(0);
    if (welderInv != null && !welderInv.Empty())
        isIdleNoWork = false;
}
```

## Verification

1. Enable one of the `Push ... Immediately` flags on a BaR.
2. Let the BaR grind a small target pile, stop, and go idle.
3. Wait past the 5–10s push cooldown and confirm the leftover items drain to the configured push target.
4. Regression check: a BaR with push flags disabled and no targets should still take the idle fast-path — `ServerTryPushInventory` is not expected to run in that case.
