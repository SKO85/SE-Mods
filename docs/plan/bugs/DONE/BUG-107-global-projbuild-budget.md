# BUG-107: Global proj.Build budget — cap projected-block materializations per tick

## Status: Fixed
## Severity: Medium
## Version: v2.5.4
## Found In: `NanobotSystem.Welding.cs`, `Mod.cs`

## Description

`ServerDoWeld` profile data from session `20260428202503-profiling` (58 BaRs, active welding, 120 s) shows `proj.Build()` calls spiking at 7–9 ms on materialization of projected blocks. Top samples:

```
total=9.389 ms  block=LargeBlockArmorBlock     buildMs=9.371  resolveMs=0.009
total=6.956 ms  block=ConveyorTube             buildMs=6.945  resolveMs=0.005
total=4.410 ms  block=SmallBlockSmallHydrogenThrust  buildMs=1.310  resolveMs=2.474
```

`proj.Build()` is the SE engine call that materializes a projected block — converting a blueprint slot into a real grid block. Cost varies by block type and grid topology (the new block triggers integrity, conveyor, and block-event updates on the parent grid).

Sibling situation to BUG-106 (`decreaseMs` on dismount): cost is engine-side, unavoidable per-call, but compounds when multiple BaRs build simultaneously. A grid scaffold being welded by 6 BaRs in parallel can produce a 6 × 8 ms = 48 ms compound stall on a single tick.

## Fix

`Mod.cs` — add `TryClaimProjBuildSlot()` mirroring the mechanical/dismount slot patterns:

```csharp
public const int MaxProjBuildsPerTickDefault = 3;
private static int _projBuildsThisTick;
private static int _lastProjBuildTick = -1;

public static bool TryClaimProjBuildSlot()
{
    var tick = MyAPIGateway.Session.GameplayFrameCounter;
    if (tick != _lastProjBuildTick)
    {
        _lastProjBuildTick = tick;
        _projBuildsThisTick = 0;
    }
    if (_projBuildsThisTick >= MaxProjBuildsPerTickDefault) return false;
    _projBuildsThisTick++;
    return true;
}
```

`NanobotSystem.Welding.cs` `ServerDoWeld` — gate the `proj.Build()` call. **Skip both build AND the post-build resolve** when the slot is exhausted; the resolve looks up the physical block by position and would null-out the target (and set `targetData.Ignore = true`) when called against a projected block that wasn't actually built.

```csharp
if (!cubeGridProjected.Projector.Closed && !cubeGridProjected.Projector.CubeGrid.Closed && (target.FatBlock == null || !target.FatBlock.Closed))
{
    if (!Mod.TryClaimProjBuildSlot())
    {
        if (profilerTs != 0L) { /* log earlyExit=projBuildSlot */ }
        return false;
    }

    tsBuild = Stopwatch.GetTimestamp();
    var proj = cubeGridProjected.Projector as Sandbox.ModAPI.IMyProjector;
    proj.Build(target, _Welder.OwnerId, _Welder.EntityId, ...);
    tsBuild = Stopwatch.GetTimestamp() - tsBuild;
}
```

Returning `false` keeps the projected block in `PossibleWeldTargets` (no `Ignore=true` set) so the next work cycle retries it.

## Cap rationale

- **3 builds/tick × 60 ticks/sec = 180 builds/sec** — well above any realistic player blueprint throughput.
- Single-BaR effective rate is unchanged: a BaR spends most of its work cycle welding existing blocks, not materializing new ones.
- The cap matters in **multi-BaR projection-rebuild** scenarios — e.g., 6 BaRs converging on a freshly-projected blueprint to scaffold the structure. Old behavior: 6 × 8 ms = 48 ms tick stall. New behavior: 3 builds go through, 3 retry next tick.
- Hard-coded for now. Could be exposed as a setting in a follow-up.

## Verification

1. **Build clean** — `dotnet build ... -c Release -v minimal` → 0 warnings, 0 errors. ✓
2. **Re-profile** — same 58-BaR scenario with active projection welding. Expected:
   - `ServerDoWeld` log entries with `earlyExit=projBuildSlot` appear when many BaRs converge on builds.
   - Per-tick `Update`-domain max drops on build-heavy ticks.
   - Total builds-per-session unchanged in steady state (deferred builds complete next tick).
3. **Regression — single-BaR projection welding** — one BaR scaffolding a blueprint should still progress at the natural cycle rate (3/tick is way above its 1/cycle rate).
4. **Regression — non-projected welding** — only the projected-block path is gated; deformation/full-integrity welding is untouched.

## See also

- BUG-106 — sibling fix capping full dismounts on the grinding side.
- BUG-105 — diagnostic instrumentation that surfaced `buildMs` as the dominant projected-weld cost.
- BUG-108 — splits `tsResolve` into `resolveCoordMs` + `resolveLookupMs` to identify what drives the 2.5 ms post-build resolve cost (separate from the build cost capped by this ticket).
- Profile session: `20260428202503-profiling`.
