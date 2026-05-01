# BUG-126: Welder-full state triggers constant CPU spikes — `PullComponents` walks all sources, `ServerEmptyTransportInventory` iterates transport items, both useless when welder cannot receive more

## Status: Open
## Severity: High (constant main-thread spikes when welder inventories are full, observed in 3-large-ship 58-BaR scenario)
## Version: v2.5.5
## Found In: `NanobotSystem.Inventory.cs:280-339` (`PullComponents`), `NanobotSystem.Inventory.cs:181-200` (`ServerEmptyTransportInventory` transport-drain loop)

## Description

Player-reported: with 58 BaRs grinding 3 large ships, when BaR welder
inventories filled up, "peak CPU load spiked constantly" (not periodic). After
removing items from cargos and adding new push containers, "the load got down
and back to regular interval spikes every few seconds like before."

Two hot loops fire every weld/transport iteration even when the welder cannot
receive any more items.

### Loop 1 — `PullComponents` source walk (`Inventory.cs:280-339`)

```csharp
var welderInventory = _Welder.GetInventory(0);
var maxpossibleAmount = Math.Min(neededAmount, (int)Math.Ceiling(remainingVolume / volume));
if (maxpossibleAmount <= 0) return false;
...
lock (_PossibleSources)
{
    foreach (var srcInventory in _PossibleSources)        // walks all 94 sources
    {
        ...
        if (srcInventory.FindItem(componentId) != null    // engine call per source
            && srcInventory.CanTransferItemTo(welderInventory, componentId))  // engine call per source
        {
            ...
            amountMoveable = (int)welderInventory.MaxItemsAddable(amountPossible, componentId);
            if (amountMoveable > 0) { /* transfer */ }
            else
            {
                // No (more) space in welder — but we already paid for the source walk to get here
                neededAmount -= availAmount;
                remainingVolume -= availAmount * volume;
                return picked;
            }
        }
    }
}
```

`remainingVolume` is the transport-inventory budget (passed in by caller), NOT
the welder volume. So `maxpossibleAmount > 0` even when welder is at 100%.
The function then walks sources, finds one with the component, calls
`MaxItemsAddable` against the welder, gets 0, and bails — but the source walk
already happened.

Per-call cost when welder full: up to 94 × `FindItem`/`CanTransferItemTo`
engine calls (~5-50 µs each). Multiply by missing components per weld target
(~1-3) × weld targets per tick (~3-10 before global cap) × 58 BaRs (staggered
to ~19 per tick) = potentially **multi-millisecond cost per tick of pure
wasted source-walk work**.

### Loop 2 — `ServerEmptyTransportInventory` transport-drain (`Inventory.cs:181-200`)

```csharp
_TempInventoryItems.Clear();
_TransportInventory.GetItems(_TempInventoryItems);

for (int srcItemIndex = _TempInventoryItems.Count - 1; srcItemIndex >= 0; srcItemIndex--)
{
    var item = _TempInventoryItems[srcItemIndex];
    if (item == null) continue;

    var amount = item.Amount;
    var moveableAmount = welderInventory.MaxItemsAddable(amount, item.Type);   // returns 0 when welder full
    if (moveableAmount > 0)
    {
        if (welderInventory.TransferItemFrom(_TransportInventory, srcItemIndex, null, true, moveableAmount, false))
        {
            amount -= moveableAmount;
        }
    }
}
```

When welder is full, every `MaxItemsAddable` returns 0, every transfer is
skipped, but the loop still iterates all transport items and does an engine
call per iteration. Called from `ServerTryWelding` after every weld attempt
(line 218), from `ServerDoGrind`'s transport block, and from `IsTransportRunning`
on every grind tick where transport is active.

## Why this isn't visible without profiling

These engine calls are individually cheap (~5-50 µs) but compound under
heavy state where many BaRs are simultaneously in welder-full mode. The
existing per-call profilers (`PullComponents`, `ServerEmptyTransportInventory`)
do see this when profiling is on, but the user's observation explicitly
came from running **without** profiling, where the cost is real but
attributable only via behavioural reasoning (load spikes correlate with
welder-full state).

## Fix

Two minimal early-outs — pure performance optimizations, identical behaviour
to existing code (transfers always fail when welder full anyway):

### Fix 1 — `PullComponents` welder-capacity gate

At the top of `PullComponents`, before the source walk:

```csharp
var welderInventory = _Welder.GetInventory(0);
if (welderInventory == null) return false;

// BUG-126: Welder-full early-out. PullComponents transfers source -> welderInventory.
// When welder is at capacity, every TransferItemFrom will fail with amountMoveable=0
// and we'll uselessly walk all 94 sources via FindItem + CanTransferItemTo. Bail
// immediately. The 0.99 threshold accounts for floating-point drift; semantically this
// matches the existing maxpossibleAmount<=0 short-circuit on the next line for
// transport volume, just applied to welder volume.
if ((float)welderInventory.CurrentVolume >= (float)welderInventory.MaxVolume * 0.99f)
{
    return false;
}

var maxpossibleAmount = Math.Min(neededAmount, (int)Math.Ceiling(remainingVolume / volume));
if (maxpossibleAmount <= 0) return false;
```

### Fix 2 — `ServerEmptyTransportInventory` transport-drain gate

Wrap the transport-drain loop in a welder-volume check:

```csharp
_TempInventoryItems.Clear();
_TransportInventory.GetItems(_TempInventoryItems);

// BUG-126: skip transport->welder iteration when welder cannot accept more items.
// Every MaxItemsAddable returns 0; iterating just burns CPU.
var welderFreeVolume = (float)welderInventory.MaxVolume - (float)welderInventory.CurrentVolume;
if (welderFreeVolume > 0f)
{
    for (int srcItemIndex = _TempInventoryItems.Count - 1; srcItemIndex >= 0; srcItemIndex--)
    {
        ...
    }
}
_TempInventoryItems.Clear();
```

## Verification

1. **Build clean** — `dotnet build ... -c Release -v minimal`.
2. **In-game smoke**:
   - Reproduce the welder-full state (let BaRs grind until inventories fill).
   - Without profiling, observe sim-speed/CPU pattern.
   - Expected: spikes drop from "constant" back to "regular interval every few
     seconds" matching the user's "after removing items" baseline.
3. **No behaviour change** — when welder has space, both fixes are no-ops.
   When welder is full, the existing code's outcome is identical (transfer
   fails) but reached without the source/item iteration.
4. **Profile session** to quantify if needed — `PullComponents` and
   `ServerEmptyTransportInventory` totalMs should drop substantially in
   welder-full scenarios; per-call max should drop because the early-out
   replaces the source/item walk.

## Why this is safe

- `PullComponents`: returns `false` (no pick happened). Same as existing
  return at the loop's else branch. Caller (`ServerFindMissingComponents`)
  handles `false` correctly — it logs the missing component and the weld
  loop's `componentFails`/starvation logic kicks in.
- `ServerEmptyTransportInventory`: skips the drain loop, but the function
  still updates `State.InventoryFull = !empty` at the bottom (transport
  unchanged → still non-empty → `InventoryFull = true`). Identical
  observable state.

## Related (not in scope)

- A separate concern: `CheckAndUpdateInventoryFull` only ever sets
  `State.InventoryFull = true`, never clears it. Clearing happens via
  `ServerEmptyTransportInventory`'s `State.InventoryFull = !empty;`. In
  scenarios where neither welding nor grinding is firing, the flag could
  theoretically stay sticky after the welder drains via `ServerTryPushInventory`.
  Not addressed here; revisit if a separate "BaRs stay idle after recovery"
  symptom is reported.

## See also

- BUG-016 / BUG-090 — push-target full backoff and signature tracking.
- BUG-114 — gate the safety-overflow push on Push* flags.
- BUG-119 — `AddIfConnectedToInventory` HashSet dedup (related hot-path
  cleanup in the same file).
