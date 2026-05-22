# BUG-105: ServerDoGrind / ServerDoWeld 19 ms spike has unprofiled cost — diagnostic instrumentation added

## Status: Fixed
## Severity: Medium (perf diagnosis)
## Version: v2.5.4
## Found In: `NanobotSystem.Grinding.cs`, `NanobotSystem.Welding.cs`

## Resolution

Profile session `20260428202503-profiling` (58 BaRs, active welding+grinding, with the new instrumentation) ruled out the friendly-damage-loop hypothesis and surfaced the real bottlenecks:

- **`friendlyMs` aggregate over 427 grinds**: total 31 ms, avg 0.073 ms, max 0.791 ms (`friendlyIter=58` confirmed). 58 `GetUserRelationToOwner` calls cost ~70 µs total, not 17 ms. Hypothesis busted.
- **Real grind bottleneck**: `decreaseMs` (5–12 ms on full-dismount armor blocks). SE engine cascade for grid integrity recalc, conveyor refresh, block-removal events.
- **Real weld bottleneck**: `buildMs` (7–9 ms on `proj.Build()` for projected armor/conveyor blocks). SE engine materialization + topology update.
- **Hidden cost surfaced**: `tsResolve` caught a 2.5 ms projected→physical block resolution on a `SmallHydrogenThrust`. Smaller magnitude but real.

The 19 ms spike from the original `20260428200417` profile was a one-off (likely GC pause); with comprehensive instrumentation in place no comparable spike recurs. ServerDoGrind histogram in the new profile: 99.5 % of calls under 2 ms, only 2 calls in the 5–20 ms band, none ≥20 ms.

## Follow-up tickets opened

- **BUG-106** — global dismount budget (cap 3/tick) to spread the `decreaseMs` cost.
- **BUG-107** — global `proj.Build` budget (cap 3/tick) to spread the `buildMs` cost.
- **BUG-108** — finer `tsResolve` instrumentation (split into `resolveCoordMs` / `resolveLookupMs`) to identify which step inside the resolution drives the 2.5 ms cost; cache strategy to be decided after the next profile.

## Description

Profile session `20260428200417-profiling` (clean isolated test world, 58 BaRs, 120 s, sim-speed avg 1.01) showed a single `ServerDoGrind` call taking **19.592 ms** while the existing sub-timers accounted for only ~0.4 ms:

```
2026-04-28 20:05:02Z  ServerDoGrind  total=19.592ms
  block=ConveyorTube  autoGrind=True  dismounted=True  integrity=0.0
  emptyMs=0.001  decreaseMs=0.234  razeMs=0.099  transportMs=0.101
  → ∑ profiled = 0.435 ms,  unprofiled = 19.157 ms (98%)
```

Same shape on a `ServerTryWeldingGrindingCollecting` welding spike (19.067 ms) but `ServerDoWeld` max was only 2.4 ms — so the welding spike lives in `ServerTryWelding`'s loop iteration, not `ServerDoWeld`. The grinding spike, however, is fully inside `ServerDoGrind`.

## Primary suspect: friendly-damage loop

`NanobotSystem.Grinding.cs:236-248` (pre-fix):

```csharp
if (target.UseDamageSystem)
{
    foreach (var entry in Mod.NanobotSystems)        // 58 systems in this profile
    {
        var relation = entry.Value.Welder
            .GetUserRelationToOwner(_Welder.OwnerId);  // SE engine call: faction lookup
        if (MyRelationsBetweenPlayerAndBlockExtensions.IsFriendly(relation))
        {
            entry.Value.FriendlyDamage[target] = MyAPIGateway.Session.ElapsedPlayTime
                + Mod.Settings.FriendlyDamageTimeout;
        }
    }
}
```

This loop iterates **every BaR system in the world** for every grind call, performing a `GetUserRelationToOwner` engine call per entry. With 58 systems, 58 engine calls per grind. Even at ~0.3 ms per engine call (plausible for faction-relation lookups), that's ~17 ms per grind — matching the observed unprofiled time.

## Secondary suspects (instrumentation added so we can rule them out)

- `Mod.TryClaimMechanicalGrindSlot()` for mechanical-block dismount path — could contend on shared state.
- Setup before `tsEmpty` (cube grid checks, integrity ratio computations, autogrind/janitor checks) — all light but unmeasured.
- `ServerDoWeld` deformation `IncreaseMountLevel` (`Welding.cs:514`) — unprofiled, only fires on full-integrity deformation repair.
- `ServerDoWeld` projected→physical block resolution (`Welding.cs:426-455`) — coordinate transforms + `GetCubeBlock` on potentially large grids.

## Fix (instrumentation only — no behavioral change)

### `NanobotSystem.Grinding.cs` — `ServerDoGrind`

Added three new sub-timers. Field names appear in the log line so future profiles will show exactly where the time goes.

| Timer | Wraps | Why |
|---|---|---|
| `tsFriendly` (+ `friendlyIter` count) | `foreach (var entry in Mod.NanobotSystems)` loop | Primary suspect |
| `tsMechCheck` | `target.FatBlock is IMyMechanicalConnectionBlock / IMyAttachableTopBlock` + `Mod.TryClaimMechanicalGrindSlot()` | Secondary; caps mechanical destruction per tick |

Also added an `earlyExit=mechSlot` profile entry on the mechanical-slot rejection path so spikes that early-return don't disappear from the log.

### `NanobotSystem.Welding.cs` — `ServerDoWeld`

Added two new sub-timers:

| Timer | Wraps | Why |
|---|---|---|
| `tsDeform` | Deformation `IncreaseMountLevel` (line 514) | Was the only `IncreaseMountLevel` path with no timer |
| `tsResolve` | Projected→physical block resolution (lines 426-455) | `WorldToGridInteger` + `GetCubeBlock` on large grids |

## Verification

1. **Build clean** — `dotnet build ... -c Release -v minimal` → 0 warnings, 0 errors. ✓
2. **No behavioral change** — all changes are wraps around existing operations with `Stopwatch.GetTimestamp()`. Logic is identical.
3. **Re-profile** — run another session in the same 58-BaR test world. The `ServerDoGrind` log line now includes `friendlyMs`, `friendlyIter`, `mechCheckMs`. Expected outcomes:
   - **If `friendlyMs` ≈ 19 ms**: confirmed friendly-damage loop is the culprit. Follow-up fix: cache or skip the loop when no friendly relations exist; iterate factions instead of BaRs (1 lookup vs N); only run the loop when grinding a block actually owned by another player.
   - **If `mechCheckMs` ≈ 19 ms**: lock contention or grid-tree walk in mechanical-block detection. Follow-up: cache the type check result on `TargetBlockData`.
   - **If neither accounts for the spike**: the cost is in setup or a sub-call we still haven't covered. Add another iteration of instrumentation around the candidates.
4. **Welding side** — when a deformation-repair spike or projected-build spike occurs, `deformMs` and `resolveMs` will reveal whether either is the source.

## Follow-up

Once the next profile identifies the dominant contributor, file a separate bug ticket for the actual fix. This ticket can move to Done after the diagnostic data is collected and the follow-up ticket is opened.

## See also

- Profile session: `20260428200417-profiling` (2026-04-28, 58 BaRs, clean test world, sim avg 1.01).
- Earlier finding (recommendation #2 from the original thenebula analysis): main-thread spikes lacking instrumentation in `UpdateBeforeSimulation10_100` orchestration. This ticket addresses the corresponding gap inside the weld/grind core.
