# BUG-123: Friendly-damage relation loop runs 58 GetUserRelationToOwner queries per grind, max 4.81 ms outliers

## Status: Open
## Severity: Medium (recurring outlier on grinding spikes)
## Version: v2.5.5
## Found In: `NanobotSystem.Grinding.cs:266-277`

## Description

`ServerDoGrind` iterates `Mod.NanobotSystems` (58 entries on the test cluster)
on every grind tick and calls
`entry.Value.Welder.GetUserRelationToOwner(_Welder.OwnerId)` per entry,
recording each friendly BaR's `FriendlyDamage[target]` timeout.

Profile session `20260429181044-profiling` (welding-active 58-BaR scenario):

```
2026-04-29 18:10:57Z;ms=5.414;block=LargeHalfSlopeArmorBlock;dismounted=True;
                     friendlyMs=4.813;friendlyIter=58;decreaseMs=0.344;razeMs=0.143
2026-04-29 18:11:07Z;ms=4.201;block=LargeBlockArmorBlock;dismounted=True;
                     friendlyMs=3.897;friendlyIter=58;decreaseMs=0.061;razeMs=0.129
```

Avg `friendlyMs` is low (~0.06 ms — 58 SE engine calls amortized) but the
worst-case outliers are 4-5 ms — substantial chunks of the per-grind cost on
spike ticks.

## Root Cause

`GetUserRelationToOwner(otherOwnerId)` is purely a function of
`(_Welder.OwnerId, otherBaR.OwnerId)` — the result does not depend on the block
being ground. The loop re-runs all 58 engine queries on every grind tick.

Original investigation (BUG-105) measured this loop at ~70 µs total in a
typical session and dismissed it. The current data shows that under SE engine
state where the relation lookup happens to take 50-100 µs each (vs. the typical
1 µs), the loop blows up to multi-millisecond. Caching survives both regimes.

## Fix

Add a relation cache to `Mod.cs` (lives next to `NanobotSystems`,
`GridOwnershipCacheHandler`, and the existing per-second housekeeping
infrastructure):

```
private readonly ConcurrentDictionary<long, List<NanobotSystem>>
    _FriendlyBaRsByOwner = new ConcurrentDictionary<long, List<NanobotSystem>>();
private TimeSpan _FriendlyBaRsLastRebuild;
private const int FRIENDLY_BARS_REFRESH_SECONDS = 5;
```

`Mod.UpdateBeforeSimulation` (already runs every frame) refreshes
`_FriendlyBaRsByOwner` every 5 s by walking `NanobotSystems` once and
populating the dict keyed by candidate-BaR's owner — `O(N)` rebuild, no
per-tick scan.

`ServerDoGrind`'s loop becomes:

```
List<NanobotSystem> friendlies;
if (Mod.TryGetFriendlyBaRsForOwner(_Welder.OwnerId, out friendlies))
{
    for (var i = 0; i < friendlies.Count; i++)
    {
        friendlies[i].FriendlyDamage[target] =
            MyAPIGateway.Session.ElapsedPlayTime + Mod.Settings.FriendlyDamageTimeout;
    }
}
```

- 5 s staleness is acceptable: `Mod.Settings.FriendlyDamageTimeout` is 30 s by
  default (the existing window over which "friendly" is meaningful), so a 5 s
  refresh window is well inside that envelope.
- Ownership changes (admin grant, faction transfer) are rare and not safety-
  critical for this path — friendly-damage tagging is a UX courtesy, not a
  security gate. Worst case: a former-friendly BaR gets one extra timeout
  written for ~5 s after the relation flips, which is harmless.
- `ConcurrentDictionary` chosen for the dict because the rebuild happens on
  the main thread but `Close()` on a NanobotSystem can also touch the dict
  if we add invalidation later; keeps the contract simple.

`friendlyIter` log field stays — counts iterated friendlies (now small,
no longer 58 indiscriminately).

## Verification

1. **Build clean**.
2. **Re-profile** the same welding-grinding scenario.
3. **Expected**:
   - `ServerDoGrind` `friendlyMs` outliers drop from 4-5 ms to sub-millisecond
     across all samples.
   - `friendlyIter` reflects only actually-friendly BaRs (typically 0 or
     small N for non-faction BaRs).
   - `ServerDoGrind` total / max drops by the corresponding 4-5 ms in worst-
     case samples; avg unchanged in nominal samples (was already cheap).
4. **No behavioral change** — `FriendlyDamage[target]` is still written for
   every friendly BaR, just sourced from a cached list.

## See also

- Profile session: `20260429181044-profiling` (max `friendlyMs` 4.813 ms).
- BUG-105 — original instrumentation that surfaced and initially dismissed
  this loop.
- `iterative-questing-oasis.md` plan file recommendation #1 — this ticket
  realizes that recommendation.
