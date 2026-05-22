# BUG-108: Projected→physical block resolution can spike to 2.5 ms — diagnostic split added

## Status: Fixed
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

## Resolution: Diagnostic complete; no fix planned

After review, this ticket is closed at the diagnostic-only deliverable. The instrumentation is in (`NanobotSystem.Welding.cs:600-606` splits `tsResolve` into `tsResolveCoord` + `tsResolveLookup`; line 728 logs both `resolveCoordMs` and `resolveLookupMs`) and is permanent — any future projected-block weld will populate it automatically.

### Why no fix is warranted at the data we have

- **Frequency is negligible**: one 2.5 ms outlier in **649** `ServerDoWeld` calls = 0.15 % rate. Amortized contribution to the weld budget is ~3.7 µs per call, which is below noise.
- **Severity was Low at filing** and nothing has changed that would raise it.
- **No data has accumulated** to narrow which sub-step dominates. The latest profile session (`20260429145732`) had **zero `ServerDoWeld` calls** — all `ServerTryWelding` invocations early-exited (max 0.011 ms). Recent scenarios have been grind-heavy. Older sessions with projected-weld activity have been cleared.
- **Intervening edits don't change this code path**: BUG-115 (proj.Build NRE catch) returns *before* the resolve path, so success-path resolve cost is unchanged. BUG-117 (OwnerId fast path) is in scan code, not weld. BUG-113 confirmed `proj.Build` itself is 99 % of every weld spike — the resolve path was always a single rare tail event, not a systemic cost.

### Why the candidate fixes wouldn't pay back even with data

The original "Verification of instrumentation" section listed three fix branches based on which sub-timer dominates. Each is unattractive at the observed rate:

1. **`resolveLookupMs` dominant → `GetCubeBlock` cost.** The ticket itself notes that "each projected block resolves only once (then becomes physical), so cache hit rate is zero — caching won't help." The alternative — relying on a hypothetical newer SE API where `proj.Build()` returns the physical block directly — depends on engine version assumptions and may not exist.
2. **`resolveCoordMs` dominant → coordinate transform.** A per-projector cached world↔grid transform matrix could help, but adds invalidation logic on projector movement. Maximum saving bounded by the engine call cost we'd avoid (microseconds), against a 0.15 % rate. Not justified.
3. **Both small, `resolveMs` large → surrounding logic.** Ticket flagged this as "unlikely given timer placement." Even if true, the candidate cost is in our own already-tight code.

For all three branches, the cost of the fix (added complexity, cache-invalidation, or engine-version coupling) is hard to justify against a 0.15 % rate × ~3 ms tail.

### What stays in the codebase

- The diagnostic split (`tsResolveCoord` / `tsResolveLookup`) is permanent.
- Future profiles that exercise projected welds will automatically capture per-sub-step data.

### When to revisit

If a future profile shows the resolve path becoming a meaningful cost — e.g., spikes recurring at >1 % rate, or sustained multi-ms per-call cost on common projected block types — re-open with the new data. The follow-up should reference the new session ID and quote the breakdown of `resolveCoordMs` vs `resolveLookupMs` from the captured logs.

## See also

- BUG-105 — added the original `tsResolve` timer that caught this spike. Same diagnostic-complete close pattern.
- BUG-107 — caps the upstream `proj.Build()` call; the post-build resolve runs only after a successful build.
- BUG-113 — also closed as "diagnostic complete" once `proj.Build` was confirmed as the 99 % cost source.
- Profile session: `20260428202503-profiling` (the original 2.5 ms outlier capture).
