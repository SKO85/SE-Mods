# BUG-114: Gate the safety overflow push on the per-BaR Push* flags

## Status: Fixed
## Severity: Medium (player-facing semantics)
## Version: v2.5.4
## Found In: `NanobotSystem.Inventory.cs`

## Description

Audit triggered by player question: "is pushing inventories only done when those options are enabled per BaR?"

Two distinct push paths existed in the inventory code, with different gating:

### Path A — `ServerTryPushInventory` (user-controlled)

`NanobotSystem.Inventory.cs:37-136`. Called from `Operations.cs:90`. Already correctly gated:
- Early return at line 42-43 if all three Push* flags are off:
  ```csharp
  if ((Settings.Flags & (PushIngotOreImmediately | PushComponentImmediately | PushItemsImmediately)) == 0)
      return;
  ```
- Per-item-type checks at lines 78 / 87 / 97 (ingot / component / other) gate per item.
- The idle fast-path at `Operations.cs:80-86` also gates `_Welder.GetInventory(0)` access on the same mask.

### Path B — `ServerEmptyTransportInventory(push: true)` overflow safety push

`NanobotSystem.Inventory.cs:152-169`. Called as `(true)` from grinding (`Grinding.cs:335`), collecting (`Collecting.cs:126`), the operations cleanup branch (`Operations.cs:213`), and the in-flight transport check (`Inventory.cs:348`). Welding calls it as `(false)`.

The push fires `welderInventory.PushComponents(_PossiblePushTargets, null)` (no item-type filter — pushes everything) when:
- caller passes `push=true`
- welder non-empty
- `!_PushTargetsFull`
- >5 s since last push
- welder remaining space < 1.5× incoming transport volume (overflow imminent)

**This branch was NOT gated by any Push* flag.** A player who turned all three "Push immediately" flags off would still see the welder dump items into push-target containers whenever grinding/collecting was about to overflow it.

## Reported intent

Player wanted "no push enabled = no push, period." When all three flags are off, the welder is allowed to fill, `State.InventoryFull` trips (existing handler), and grinding pauses until the player makes room manually or re-enables a flag. The safety push is no longer a hidden override of the player's preference.

## Fix

Single-condition change in `ServerEmptyTransportInventory` at `NanobotSystem.Inventory.cs:152`:

```csharp
var pushFlagsActive = (Settings.Flags & (
    SyncBlockSettings.Settings.PushIngotOreImmediately |
    SyncBlockSettings.Settings.PushComponentImmediately |
    SyncBlockSettings.Settings.PushItemsImmediately)) != 0;
if (push && pushFlagsActive && !welderInventory.Empty() && !_PushTargetsFull)
{
    // ... existing time-and-volume threshold check + PushComponents call ...
}
```

Mirrors the idiom already used at the top of `ServerTryPushInventory` (line 42). The rest of the method (transport→welder transfer at lines 171-189) is unchanged — that path is internal staging movement, not a push to external containers, and must always run for grinding/welding to function.

## Why this is the right scope

- **No effect on welding**: welding calls `ServerEmptyTransportInventory(false)` (`Welding.cs:195`) — the `push` parameter is already `false`, so this branch never ran for welds.
- **No effect on `ServerTryPushInventory`**: the explicit user-facing push, already correctly gated. Untouched.
- **Existing inventory-full handling kicks in cleanly**: `CheckAndUpdateInventoryFull` (called from `Operations.cs:96`) detects a full welder and sets `State.InventoryFull = true`. The grind/collect paths use that to stop work. No new state machinery needed.
- **Behavior unchanged when any flag is on**: if at least one of the three flags is set, the safety push fires exactly as before.

## Verification

1. **Build clean** — `dotnet build ... -c Release -v minimal` → 0 warnings, 0 errors. ✓
2. **Flags OFF, grinding fills welder**:
   - Place a BaR with cargo containers as push targets.
   - Disable all three Push* flags in the terminal.
   - Have the BaR grind blocks until the welder fills up.
   - **Expected**: welder fills, terminal shows `State: InventoryFull`, grinding pauses, cargo containers stay empty.
3. **At least one flag ON — regression check**:
   - Re-enable `PushComponentImmediately`.
   - Welder drains into push targets as before; grinding continues.
   - Behavior identical to v2.5.4 pre-change.

## See also

- `ServerTryPushInventory` (`Inventory.cs:42`) — same flag-mask idiom reused.
- BUG-016 — `_PushTargetsFull` cooldown introduced after a different push-related issue (full push targets being retried every tick).
- FEAT-037 — extended push interval for adaptive batching; orthogonal to this fix.
