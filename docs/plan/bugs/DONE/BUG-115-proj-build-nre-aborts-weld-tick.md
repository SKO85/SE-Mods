# BUG-115: `proj.Build` NRE aborts entire weld tick (DLC armor validation)

## Status: Fixed
## Severity: High (BaR welds nothing on certain large worlds)
## Version: v2.5.4
## Found In: `NanobotSystem.Welding.cs` `ServerDoWeld`

## Symptoms

Player report: "BaRs in a large world try to weld but they do not actually weld anything. On the smaller world they do work correctly. Seems like something happens with inventories. Could be another mod pulling items right at the moment when it tries to weld."

Profile session `notworking` (60 s, 39 513 sim samples, 3 active BaRs welding projected blocks):

| Metric | Value |
|---|---|
| `ServerTryWelding` calls | **363** |
| `ServerTryWelding` entries with `welding=True` | **0** |
| `ServerTryWelding` entries with `transporting=True` | 88 |
| `ServerFindMissingComponents` calls | 88 |
| `ServerDoWeld` calls in summary | **0 (file missing entirely)** |

The `ServerDoWeld` profile log file did not exist for the session — meaning `MethodProfiler.StopAndLog` was never reached for that method. The 88 `ServerFindMissingComponents` calls completed (returned `transporting=true`), so the welding loop reached line `welding = ServerDoWeld(targetData);`, but `ServerDoWeld` never logged.

## Root cause (confirmed in-field)

**SE in offline mode does not populate `MySessionComponentGameInventory`'s DLC entitlement state**, so any call into `HasArmor(armorId, steamId)` dereferences null internal-state. `MyProjectorBase.BuildInternal` calls `ValidateArmor` → `HasArmor` unconditionally, so every `proj.Build()` for a projected armor block on an offline world hits this.

**Player-confirmed reproduction**: setting the same world to **online mode** made the NRE stop and BaR welding succeed. No code change required to fix the NRE itself when online — Steam populates the DLC table and `HasArmor` succeeds. The persistent skip + try/catch in this fix is the **defense-in-depth** for offline-mode play and for any future SE state where the entitlement table happens to be empty.

The original engine exception captured in the mod log:

```
2026-04-29 02:06:59Z: BuildAndRepairSystemBlock Static Grid 3878.BuildAndRepairSystem 8:
UpdateBeforeSimulation10 Exception: System.NullReferenceException: Object reference not set to an instance of an object.
   at Sandbox.Game.SessionComponents.MySessionComponentGameInventory.HasArmor(MyStringHash armorId, UInt64 steamId)
   at Sandbox.Game.SessionComponents.MySessionComponentGameInventory.ValidateArmor(MyStringHash armorId, UInt64 steamId)
   at Sandbox.Game.Entities.Blocks.MyProjectorBase.BuildInternal(Vector3I cubeBlockPosition, ...)
   at Sandbox.Game.Entities.Blocks.MyProjectorBase.Build(MySlimBlock cubeBlock, Int64 owner, Int64 builder, Boolean requestInstant, Int64 builtBy)
   at SKONanobotBuildAndRepairSystem.NanobotSystem.ServerDoWeld(TargetBlockData targetData)
   at SKONanobotBuildAndRepairSystem.NanobotSystem.ServerTryWelding(...)
   at SKONanobotBuildAndRepairSystem.NanobotSystem.ServerTryWeldingGrindingCollecting()
   at SKONanobotBuildAndRepairSystem.NanobotSystem.UpdateBeforeSimulation10_100()
```

**SE's `MyProjectorBase.BuildInternal` calls `ValidateArmor(armorId, steamId)` → `HasArmor(armorId, steamId)`, and `HasArmor` dereferences something keyed on `steamId` that is `null`.** The `builtBy` argument passed to `proj.Build` is `_Welder.SlimBlock.BuiltBy` — for grids imported from another world, copy-pasted from blueprints, or where the original builder no longer has a resolvable Steam profile, the engine fails to look up the armor entitlement and throws NRE.

Cascade:

1. `ServerDoWeld` calls `proj.Build(...)` — throws NRE before `MethodProfiler.StopAndLog` runs.
2. Exception propagates through `ServerTryWelding` (the outer `try/finally` runs `StopAndLog` for `ServerTryWelding`, which is why we see `welding=False;transporting=True` 88 times).
3. Exception propagates through `ServerTryWeldingGrindingCollecting` (same pattern — its `finally` runs the profiler log too).
4. Exception is silently caught at `NanobotSystem.Update.cs:198` `catch (Exception ex)` inside `UpdateBeforeSimulation10_100`. The error is written to `NanobotBuildAndRepairSystem.log` but no in-game indication.
5. The lock-on block is preserved across ticks (it didn't get marked `Ignore`). Next tick, the loop re-locks the same block, calls `proj.Build`, throws again — forever.

This is not "another mod pulling items." It is the SE engine failing the DLC armor entitlement check for the projected block's armor variant, with our `proj.Build` call as the trigger.

## Why it didn't show on the smaller test world

The smaller test world was loaded in **online mode**; the affected one was offline. Earlier hypotheses (BuiltBy resolution, modded armor variants, large-world player-table state) were ruled out by the in-field test: setting the same world to online mode made the NRE stop without any code change. Offline mode is the only differentiator.

## Fix (two layers)

### Layer 1 — `NanobotSystem.Welding.cs` `ServerDoWeld`: wrap `proj.Build()` in `try/catch (NullReferenceException)`

```csharp
tsBuild = Stopwatch.GetTimestamp();
var proj = cubeGridProjected.Projector as Sandbox.ModAPI.IMyProjector;
try
{
    proj.Build(target, _Welder.OwnerId, _Welder.EntityId,
               Settings.WeldOptions == AutoWeldOptions.WeldFull,
               _Welder.SlimBlock.BuiltBy);
}
catch (NullReferenceException ex)
{
    tsBuild = Stopwatch.GetTimestamp() - tsBuild;
    targetData.Ignore = true;          // don't retry next tick
    if (Logging.Instance.ShouldLog(Logging.Level.Error))
    {
        Logging.Instance.Write(Logging.Level.Error,
            "BuildAndRepairSystemBlock {0}: proj.Build threw NRE for {1} (likely SE DLC armor validation with unresolved BuiltBy={2}); marking block ignored. {3}",
            Logging.BlockName(_Welder, Logging.BlockNameOptions.None),
            target != null ? target.BlockDefinition.Id.SubtypeName : "null",
            _Welder.SlimBlock.BuiltBy,
            ex.Message);
    }
    if (profilerTs != 0L)
    {
        // Emit the existing ServerDoWeld log line with earlyExit=projBuildNRE
        // so the profile summary now counts these calls and the failure is visible.
        ...
    }
    return false;
}
tsBuild = Stopwatch.GetTimestamp() - tsBuild;
```

Three properties of layer 1:

1. **Per-block scope**: only this one block's `proj.Build` call is bypassed. The welding loop at `Welding.cs:193` still gets `welding=false; created=false` and falls through to the "no components / can't weld now" branch at `Welding.cs:218`, which releases the assignment, clears the lock-on, and lets the BaR pick a different target on the next iteration of the same tick.
2. **Block ignored**: `targetData.Ignore = true` prevents the same broken projected block from being re-locked next tick.
3. **Profiler now sees it**: the `earlyExit=projBuildNRE` log line ensures `ServerDoWeld` shows up in the profiler summary with a meaningful exit reason, so future profiles can quantify how often this happens.

### Layer 2 — persistent broken-block set on the BaR

Layer 1 alone is not enough. `targetData.Ignore` lives on the `TargetBlockData` instance, and the background scan **rebuilds those instances each refresh** (~every 2 s). After a rebuild, `Ignore` is back to `false`, the BaR re-locks the same broken projected block, throws NRE again, and the mod log fills up with the same warning every scan cycle. Confirmed in-field by the user: "The errors still appear in the log file. I am able to weld them by hand in admin mode." (Admin/creative mode bypasses SE's DLC entitlement check, so manual welding succeeds — corroborating that the problem is the engine validation, not the block itself.)

`NanobotSystem.cs` adds a per-BaR field:

```csharp
private readonly HashSet<string> _BrokenProjBuildKeys = new HashSet<string>();
```

`NanobotSystem.Welding.cs` adds a stable key helper that mirrors the `BlockSystemAssigningHandler` convention:

```csharp
private static string GetBrokenBlockKey(IMySlimBlock block)
    => block?.CubeGrid == null ? null
       : block.CubeGrid.EntityId.ToString() + ":" + block.Position.ToString();
```

`Weldable()` short-circuits on the projected branch when the block's key is already in the set:

```csharp
if (isProjected)
{
    if (target != null
        && _BrokenProjBuildKeys.Count > 0
        && _BrokenProjBuildKeys.Contains(GetBrokenBlockKey(target)))
    {
        targetData.Ignore = true;
        return false;
    }
    // ... existing CanBuild flow ...
}
```

The `ServerDoWeld` NRE catch adds the key to the set and **only logs on the first failure per block**, so the warning appears once instead of every 2 s:

```csharp
var brokenKey = GetBrokenBlockKey(target);
var firstFailure = brokenKey != null && _BrokenProjBuildKeys.Add(brokenKey);
if (firstFailure && Logging.Instance.ShouldLog(Logging.Level.Error))
{
    Logging.Instance.Write(Logging.Level.Error,
        "BuildAndRepairSystemBlock {0}: proj.Build threw NRE for {1} (likely SE DLC armor validation with unresolved BuiltBy={2}); marking block permanently ignored for this BaR. Player can still weld it manually in admin/creative mode. {3}",
        ...);
}
```

Properties of layer 2:

- **Per-BaR scope**: each BaR has its own set. One BaR's failure doesn't affect a neighbor BaR's view of the same block (each one will independently fail once, log once, then skip).
- **Lifetime = BaR session**: cleared on BaR re-init (block placed/loaded). Acceptable — if the SE engine state ever heals, restarting the world or replacing the BaR re-evaluates. The user can still build the block manually in the meantime.
- **Bounded growth**: only entries are projected blocks that actually failed `proj.Build`. In a normal world this is zero; on the affected world it caps at the number of broken blocks (small, since the user reported "by hand they work" = small set).
- **Single warning per block**: the `HashSet.Add` return value gates the log write. The first occurrence is recorded with full context; subsequent skips happen silently in `Weldable`.

## Why catch only `NullReferenceException`

We deliberately do not blanket-catch `Exception` here:

- The known SE failure mode is NRE inside `HasArmor`. Catching only NRE keeps unrelated exceptions (e.g. an `InvalidOperationException` from a closed projector mid-call) propagating, so other latent bugs are still surfaced via the existing top-level catch.
- A blanket catch would mask future SE engine changes whose stack traces we'd want to see.

## What this does NOT fix

- The SE engine bug itself. That's Keen's territory; we just stop letting it abort our entire weld tick.
- Non-projected welding paths (`target.MoveItemsToConstructionStockpile`, `target.IncreaseMountLevel`). Those are not in the reported stack trace and have not been observed throwing NRE; if a similar engine-side failure appears there in the future, file a follow-up rather than preemptively wrapping every engine call.

## Verification

1. **Build clean** — `dotnet build ... -c Release -v minimal` → 0 warnings, 0 errors. ✓
2. **Re-profile the `notworking` world**:
   - `/nanobars profile start 60 0 nrefix`
   - Run for 60 s, stop.
   - **Expected**: `ServerDoWeld` now appears in the summary; `welding=True` entries appear in `ServerTryWelding.log`; mod log shows the `proj.Build threw NRE` warning **once per broken block** for the entire session, not once per scan cycle. BaR welds non-broken targets normally.
3. **Regression check on a fresh single-player world** (where the bug never reproduced): welding behavior is unchanged. The `try/catch` is on the single-statement `proj.Build` invocation only, the `Weldable()` short-circuit is gated on `_BrokenProjBuildKeys.Count > 0`, and the helper allocates no objects on the hot path.

## See also

- BUG-104 — earlier "DLC check fails for offline owners". Related root cause (DLC entitlement lookups against unresolved player IDs). That fix removed our own DLC pre-check; this one closes the corresponding hole inside SE's own validation that runs during `proj.Build`.
- BUG-107 — global per-tick `proj.Build` budget. Orthogonal: BUG-107 caps how many `proj.Build` calls fire per tick; BUG-115 makes each call survive engine-side NREs.
- BUG-113 — `ServerDoWeld` sub-timer instrumentation. The `earlyExit=projBuildNRE` log uses the same field set so summaries stay consistent.
- BUG-120 — companion fix for the **silent-fail** DLC case (online owner without DLC, where `proj.Build` returns cleanly without throwing NRE). Reuses this ticket's `_BrokenProjBuildKeys` set + `GetBrokenBlockKey` helper, and adds owner-scope invalidation so the cache resets when `_Welder.OwnerId` changes (the new owner's DLC entitlements may differ).
