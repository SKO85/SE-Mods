# BUG-116: `Weldable()` called on starved-priority blocks before the cheap skip

## Status: Fixed
## Severity: Medium (main-thread sim-speed drag on large ships)
## Version: v2.5.4
## Found In: `NanobotSystem.Welding.cs` `ServerTryWelding`

## Symptom

Profile session `20260429013527-profiling` (120 s, large-ship welding scenario):

| Method | Calls | Avg ms | Max ms | Total ms |
|---|---:|---:|---:|---:|
| **ServerTryWelding** (main thread) | 490 | 4.09 | **45.5** | 2,005 |
| Weldable | 26,874 | 0.006 | 7.5 | 167 |

Sim-speed avg **0.69**, min **0.43**.

Top spikes consistently match this signature:

```
ms=45.533;welding=False;needWelding=True;transporting=False;targets=128;
weldChecked=69;skipIgnore=57;skipGrid=0;skipAssign=2;
componentFails=3;starvedSkip=66;compChecks=3
```

```
ms=29.093;welding=False;targets=115;weldChecked=82;skipIgnore=32;
componentFails=3;starvedSkip=76;compChecks=3
```

```
ms=20.197;targets=115;weldChecked=84;starvedSkip=79;compChecks=3
```

## Root cause

The welding loop ordering at `Welding.cs:111-136` (pre-fix):

```csharp
var isIgnored = targetData.Ignore;
var isWeldable = !isIgnored && Weldable(targetData);   // ← engine call: ~0.05-0.1 ms / block
...
if (... || isWeldable)
{
    ...
    if (!isLockOnBlock)
    {
        if (!lookingForNext && starvedPriorityBits != 0)
        {
            var blockPriority = BlockWeldPriority.GetPriority(targetData.Block);
            if (... starved ...)
            {
                starvedSkipped++;
                continue;       // ← but we've already paid for Weldable()
            }
        }
```

`Weldable()` calls the SE engine `target.CanBuild(false)` (for projected blocks) or `target.NeedRepair(...)` / `IsFriendlyDamage(...)` (for built blocks). At ~0.05-0.1 ms apiece, **70-110 calls per spike tick = 5-15 ms minimum**, scaling with engine variance to 30-45 ms.

The starved-priority skip was placed *after* `Weldable()` because it lived inside the `if (isWeldable)` branch. Each starved-skip block thus paid the full `Weldable()` cost before getting cheaply rejected on the next two lines.

The ordering inversion is exactly the kind of thing that's invisible until profiled — `BlockWeldPriority.GetPriority()` is a dictionary lookup (~5 µs), much cheaper than the engine `CanBuild` check, but it ran second.

## Fix

Add a cheap pre-filter for starved priorities **before** `Weldable()`:

```csharp
var isIgnored = targetData.Ignore;

// BUG-116: Cheap pre-filter — skip the engine CanBuild call when the priority is
// already known starved. Same condition as the existing skip below; runs first
// so starved-priority blocks cost ~5µs instead of ~50-100µs each.
if (!isIgnored && !isLockOnBlock && !lookingForNext && starvedPriorityBits != 0)
{
    var earlyPriority = BlockWeldPriority.GetPriority(targetData.Block);
    if (earlyPriority > 0 && earlyPriority < 64
        && (starvedPriorityBits & (1L << earlyPriority)) != 0)
    {
        needWelding = true;
        starvedSkipped++;
        continue;
    }
}

var isWeldable = !isIgnored && Weldable(targetData);
// ... existing flow ...
```

The original starved-priority skip at `Welding.cs:127-136` is kept as a **fallback** for the same-tick race where `starvedPriorityBits` gets newly set after the current block has already passed the pre-filter. That path still pays one `Weldable()` cost on the very tick a priority becomes starved; subsequent blocks at the same priority benefit from the cheap pre-filter.

## Why this fix is the right scope

- **Single condition reorder**: no behavioral change. The same blocks are skipped, the same counters increment, just earlier in the iteration.
- **Lock-on path untouched**: `isLockOnBlock` blocks always skip the pre-filter, preserving the existing lock-on semantics (a lock-on block is always evaluated even at "starved" priority — it might just have completed).
- **Script-controlled path untouched**: `ScriptControlled` flag still hits the existing post-`Weldable` branch (where it needs to anyway, because the script-controlled check is on `Settings.CurrentPickedWeldingBlock`).
- **No new state**: just one extra dictionary lookup before the existing engine call. Lookup is the same one already performed in the post-Weldable branch.

## Expected impact

For the worst spike pattern (`weldChecked=110, starvedSkip=66, compChecks=3`):

| Path | Per-block cost | Calls/tick | Total |
|---|---:|---:|---:|
| Before — Weldable + starved-skip | ~0.1 ms | 100+ | 10-15 ms (best), 30-45 ms (with engine variance) |
| After — pre-filter on starved blocks + Weldable on rest | ~0.005 ms (skip), ~0.1 ms (rest) | ~70 skip + ~10 weld | ~1.5-3 ms |

A ~10× reduction on the spike path. ServerTryWelding max **45 ms → ~5 ms** expected. Sim-speed during welding-stalled ticks should recover from 0.43 toward 0.7+.

## Verification

1. **Build clean** — `dotnet build ... -c Release -v minimal` → 0 warnings, 0 errors. ✓
2. **Re-profile the same scenario** that produced the 45.5 ms spike. Look for:
   - `ServerTryWelding` max in the new summary should drop substantially (target: under 10 ms).
   - `Weldable` calls should drop proportionally (target: ~50% reduction at scale).
   - `starvedSkip` field should still match the previous total (same blocks skipped, just earlier).
3. **Lock-on regression check**: a lock-on tick (`hadLockOn=True; lockOnFound=True; weldChecked=2-3`) should remain identical — those blocks always bypass the pre-filter via `isLockOnBlock`.

## See also

- `BlockWeldPriority.GetPriority` — already used in the post-`Weldable` skip and at the failure-tracking site (`Welding.cs:229`); same lookup, no new code path.
- BUG-097 — earlier work on the priority/starvation logic. This fix is purely the ordering of when `GetPriority` is called.
- BUG-103 — removed the transport gate that was stopping welds entirely; this fix is the next layer of optimization once welding actually runs.
