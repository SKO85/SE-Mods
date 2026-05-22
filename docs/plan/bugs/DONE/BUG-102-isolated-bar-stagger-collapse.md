# BUG-102: Isolated BaRs (clusterSize=1) collapse to effectiveGroups=1, defeating stagger

## Status: Fixed
## Severity: Medium
## Version: v2.5.4
## Found In: `NanobotSystem.Update.cs`, `Mod.cs`

## Description

`UpdateBeforeSimulation10_100` decides which stagger slot a BaR fires on. The per-BaR `effectiveGroups` formula treated *isolated* BaRs (`clusterSize == 1`, no cluster assigned) the same as *small clusters* (`clusterSize` 2–5) and forced both to 1 group — i.e. all such BaRs fire on every cycle. Discovered while analyzing the `thenebula` v2.5.3 profile (31 BaRs, all unclustered): every log entry showed `clusterSize=1; effectiveGroups=1`, meaning all 31 BaRs fired on the same tick.

The stagger collapse is correct for small *clusters* — the cluster-coordinator scan amortizes work across members so internal staggering would produce no benefit. It's wrong for *isolated* BaRs: there is no shared scan, so bunching them onto the same tick just multiplies per-tick CPU.

The reactive sim-penalty path (lines 95-99) does lift `effectiveGroups` once sim-speed drops below 0.9, but only after sim is already degraded. By that point the spike has already happened.

## Steps to Reproduce

1. Place 11+ BaRs spaced far apart (each in `clusterSize=1`).
2. Enable `MethodProfiler` for `UpdateBeforeSimulation10_100`.
3. Inspect the log entries: `effectiveGroups=1` for every entry, `throttle=fired/workCycle` for every BaR on the same ticks.

In `thenebula` (31 isolated BaRs, sim-speed avg 0.66, min 0.02), the steady-state distribution showed `<1ms: 14049, 5-10: 19, 10-50: 8, ≥50: 1` — most ticks are fine, but spikes hit when many BaRs land on the same tick.

## Root Cause

`NanobotSystem.Update.cs:91-92` (pre-fix):

```csharp
var clusterSize = AssignedCluster != null ? AssignedCluster.Members.Count : 1;
var effectiveGroups = clusterSize < 6 ? 1 : Math.Min(Mod.GetEffectiveStaggerGroupCount(), clusterSize - 3);
```

The `clusterSize < 6` gate conflates two distinct cases. The original intent ("small cluster doesn't need internal stagger because the shared scan amortizes") is correct, but `clusterSize == 1` means *no cluster* — there is no shared scan to amortize.

The profiler log statement at `Update.cs:195-198` had two further bugs that masked the issue:

1. It recomputed `effectiveGroups` inline using `Members.Count < 5` (off-by-one against the actual logic's `< 6`).
2. The recomputation dropped the sim-penalty entirely, so the logged value was always the pre-penalty figure. The actual `effectiveGroups` during sim-degraded ticks was higher than the log claimed.

## Fix

The bug has two halves; both are required for the fix to engage in the discovered scenario.

### Half A — `NanobotSystem.Update.cs`: split the cluster-size gate

Three-way branch on `clusterSize` and route the log through the actual computed value:

```csharp
clusterSize = AssignedCluster != null ? AssignedCluster.Members.Count : 1;
var modWideStagger = Mod.GetEffectiveStaggerGroupCount();
if (clusterSize == 1)
{
    // Isolated BaR: no shared scan amortization. Use mod-wide stagger directly.
    effectiveGroups = modWideStagger;
}
else if (clusterSize < 6)
{
    // Small cluster: shared scan amortizes; collapse to 1 group.
    effectiveGroups = 1;
}
else
{
    effectiveGroups = Math.Min(modWideStagger, clusterSize - 3);
}
```

`clusterSize` and `effectiveGroups` are hoisted to method scope so the `finally` log block reads the actual post-sim-penalty value rather than recomputing a stale version. The off-by-one (`< 5` vs `< 6`) is removed by virtue of using the same variable.

### Half B — `Mod.cs`: count placed BaRs, not just `IsWorking`

Half A alone was inert against the actual reported scenario: re-profiled session `thenebula2` (v2.5.4 with Half A applied, all 31 BaRs disabled) still showed `effectiveGroups=1` for every entry and zero `throttle=stagger` events.

Root cause: `Mod.GetEffectiveStaggerGroupCount` was counting only `IsWorking` BaRs:

```csharp
foreach (var sys in NanobotSystems.Values)
    if (sys.Welder != null && sys.Welder.IsWorking) active++;
```

When all BaRs are disabled (or unpowered), `active = 0` → returns 1 → `modWideStagger = 1` → Half A's isolated-BaR branch sets `effectiveGroups = 1`. The fix doesn't engage.

The original comment ("Auto: scale with active (enabled + working) BaR count, not total placed blocks") reasoned that disabled BaRs don't generate weld/grind load. But the per-BaR update orchestration (`CleanupFriendlyDamage`, `Settings.TrySave`, `TryTransmitState`) **runs unconditionally** for any initialized BaR — disabled BaRs cost ~0.1-0.2 ms steady, and 31 of them firing on the same tick produced 5-15 ms spikes in the profile.

Fix:

```csharp
public static int GetEffectiveStaggerGroupCount()
{
    var configured = Settings.StaggerGroupCount;
    if (configured > 0) return configured;
    var total = NanobotSystems.Count;  // count all placed BaRs
    if (total <= 5) return 1;
    if (total <= 10) return 2;
    return StaggerGroupCountDefault;
}
```

Mixed-scenario trade-off: 5 working + 6 disabled was returning 1 (active≤5), now returns 3 (total≥11). Working BaRs see a slightly larger stagger window — at most ~1.6 s delay between fires in the worst case (cycle = 100 ticks ≈ 1.6 s at 60 Hz, divided by stagger-3). Negligible vs. the per-tick CPU savings from spreading the disabled-BaR overhead.

## Expected impact (thenebula / thenebula2 scenario, all 31 BaRs disabled)

- With Half B, `Mod.GetEffectiveStaggerGroupCount()` returns 3 (total > 10) regardless of working state.
- With Half A, isolated BaRs (`clusterSize == 1`) use `modWideStagger` directly → `effectiveGroups = 3`.
- **Pre-fix**: all 31 fire each cycle; per-tick steady-state cost ≈ 31 × 0.15 ms ≈ 4.7 ms.
- **Post-fix**: ~10 BaRs per tick distributed across 3 ticks; steady-state per-tick cost ≈ ~1.5 ms.
- Single-BaR spikes (5-15 ms range) share the tick with ~10 others instead of ~30, reducing tail compounding.
- The reactive sim-penalty (`Update.cs:95-99`) still applies on top, so under degraded sim-speed the stagger remains capped at `modWideStagger` either way.

## Verification

1. **Build clean** — `dotnet build ... -c Release -v minimal` → 0 warnings, 0 errors. ✓
2. **Re-profile (`thenebula3` or similar) with disabled BaRs** — repeat the 120 s session with 31 isolated, disabled BaRs. Compare against `thenebula2` (Half A only):
   - `clusterSize=1; effectiveGroups=1` should now read `clusterSize=1; effectiveGroups=3` for steady-state ticks.
   - Throttle distribution should show ~2/3 of entries as `stagger` (zero in `thenebula2`).
   - Per-tick aggregate cost in the histogram should drop in the 5-15 ms band.
3. **Regression — clustered BaRs** — on a tight cluster of 4 BaRs, confirm `effectiveGroups=1` (small-cluster path unchanged).
4. **Regression — large cluster** — on a cluster of 8+ BaRs, confirm `effectiveGroups = min(modWideStagger, clusterSize - 3)` (existing behavior preserved).
5. **Sim-speed override** — `/nanobars sim 0.5` with isolated BaRs. The simPenalty should still lift `effectiveGroups` up to `modWideStagger`. Logged `effectiveGroups` reflects the post-penalty value (was always pre-penalty before).
6. **Mixed enabled/disabled scenario** — 3 working + 8 disabled (total=11). `modWideStagger=3`. Confirm working BaRs still fire roughly every workCycle but spread across 3 ticks; disabled BaR overhead also distributes across 3 ticks.

## See also

- `Mod.GetEffectiveStaggerGroupCount` — auto-scaling formula. Now uses placed-count (Half B) instead of working-count.
- FEAT-023 (`docs/plan/features/DONE/`) — original stagger feature; this fix corrects two interactions the original didn't anticipate (isolated-BaR cluster gate, working-only count).
- Profile sessions:
  - `thenebula` (2026-04-28, v2.5.3, 120 s, 31 disabled BaRs, sim-speed avg 0.66, max 1433 ms spike) — discovery.
  - `thenebula2` (2026-04-28, v2.5.4 with Half A only, 120 s, 31 disabled BaRs, sim-speed avg 0.58, max 218 ms) — proved Half A alone is inert when all BaRs are disabled, motivating Half B.
