# BUG-108: Projected→physical block resolution can spike to 2.5 ms — diagnostic split added

## Status: In Progress
## Severity: Low (perf diagnosis)
## Version: v2.5.4
## Found In: `NanobotSystem.Welding.cs`

## Description

After `proj.Build()` materializes a projected block, `ServerDoWeld` switches the welding target from the projected block reference to the newly-materialized physical block. This requires:

1. Reading the projector's parent (physical) grid: `cubeGridProjected.Projector.CubeGrid`.
2. Coordinate transform — local position in projection grid → world coords → local position in parent grid: `projectorGrid.WorldToGridInteger(cubeGridProjected.GridIntegerToWorld(target.Position))`.
3. Block lookup on the parent grid: `projectorGrid.GetCubeBlock(blockPos)`.

Profile session `20260428202503-profiling` showed an outlier where this entire block ran for 2.5 ms on a `SmallBlockSmallHydrogenThrust`:

```
total=4.410 ms  block=SmallBlockSmallHydrogenThrust  buildMs=1.310  resolveMs=2.474
```

The build itself was 1.3 ms — the resolve dominated. We don't yet know whether step 2 (coordinate transform) or step 3 (block lookup) is the dominant cost; without that we can't choose the right cache strategy.

## Fix (this ticket: instrumentation only)

`NanobotSystem.Welding.cs` `ServerDoWeld` — split `tsResolve` into two sub-timers:

```csharp
var tsResolveMark = Stopwatch.GetTimestamp();
var blockPos = projectorGrid.WorldToGridInteger(cubeGridProjected.GridIntegerToWorld(target.Position));
tsResolveCoord = Stopwatch.GetTimestamp() - tsResolveMark;

tsResolveMark = Stopwatch.GetTimestamp();
target = projectorGrid.GetCubeBlock(blockPos);
tsResolveLookup = Stopwatch.GetTimestamp() - tsResolveMark;
```

The profiler log line now includes `resolveCoordMs` and `resolveLookupMs` alongside the existing `resolveMs`.

## Verification of instrumentation

1. **Build clean** — 0 warnings, 0 errors. ✓
2. **Re-profile** — run a session that includes projected-block welds. Inspect `ServerDoWeld.log` entries with `created=true` and look for spikes:
   - If `resolveLookupMs` ≈ `resolveMs`: the cost is in `GetCubeBlock`. Likely a hash collision or grid-internal lock. Cache strategy: per-projector `(blockPos → IMySlimBlock?)` cache, but since each projected block resolves only once (then becomes physical), the hit rate is zero — **caching won't help**. Alternative: skip the lookup and use a different API to retrieve the physical block (e.g., `proj.Build()` may return it directly in newer SE versions).
   - If `resolveCoordMs` ≈ `resolveMs`: the cost is in the coordinate transform. Cache strategy: per-projector cached transformation matrix (projector vs parent grid). Apply once per build instead of two engine calls.
   - If both are small but `resolveMs` is large: the cost is in the surrounding logic (assignment release/re-assignment, attribute flag clearing). Unlikely given the timer placement, but possible if an Assert/sync overhead intervenes.

## Follow-up

This ticket stays **In Progress** until the next profile reveals which sub-step dominates. Then a follow-up ticket (BUG-109 or similar) will implement the targeted fix based on that data — or close as Won't-Fix if the cost is purely engine-side and the workaround would be more complex than the savings justify.

The 2.5 ms spike is rare (one occurrence in 649 `ServerDoWeld` calls) and limited to projected-block creation, so the priority is **Low**. The instrumentation is cheap and stays in regardless.

## See also

- BUG-105 — added the original `tsResolve` timer that caught this spike.
- BUG-107 — caps the upstream `proj.Build()` call; the post-build resolve runs only after a successful build.
- Profile session: `20260428202503-profiling`.
