# BUG-106: Global dismount budget — cap full-dismount grinds per tick to spread engine cascade cost

## Status: Fixed
## Severity: Medium
## Version: v2.5.4
## Found In: `NanobotSystem.Grinding.cs`, `Mod.cs`

## Description

`ServerDoGrind` profile data from session `20260428202503-profiling` (58 BaRs, active grinding, 120 s) consistently shows full-dismount grinds spiking at 5–12 ms with `decreaseMs` accounting for ~95 % of the total. Top samples:

```
total=12.468 ms  block=LargeBlockArmorBlock  dismounted=True  decreaseMs=11.841
total= 6.047 ms  block=LargeBlockArmorBlock  dismounted=True  decreaseMs= 5.440
total= 2.762 ms  block=LargeBlockArmorBlock  dismounted=True  decreaseMs= 1.489
total= 2.142 ms  block=LargeBlockArmorBlock  dismounted=True  decreaseMs= 1.144
```

`decreaseMs` wraps `target.DecreaseMountLevel()` + `target.MoveItemsFromConstructionStockpile()`. On full integrity drop, the SE engine cascades through grid integrity recalc, conveyor network refresh, and block-removal events — all charged to the `DecreaseMountLevel` call.

The cost itself is unavoidable (engine-side). The risk is **multiple BaRs dismounting simultaneously**: a 12 ms spike on a single BaR is fine, but 5 BaRs all dismounting on the same tick produce a 60 ms compound stall.

The existing `Mod.TryClaimMechanicalGrindSlot()` (1/tick, BUG-097-era) protects against piston/rotor/hinge sub-grid detachment spikes. This ticket extends the same pattern to all full dismounts.

## Fix

`Mod.cs` — add `TryClaimDismountSlot()` mirroring the mechanical-slot pattern:

```csharp
public const int MaxDismountsPerTickDefault = 3;
private static int _dismountsThisTick;
private static int _lastDismountTick = -1;

public static bool TryClaimDismountSlot()
{
    var tick = MyAPIGateway.Session.GameplayFrameCounter;
    if (tick != _lastDismountTick)
    {
        _lastDismountTick = tick;
        _dismountsThisTick = 0;
    }
    if (_dismountsThisTick >= MaxDismountsPerTickDefault) return false;
    _dismountsThisTick++;
    return true;
}
```

`NanobotSystem.Grinding.cs` `ServerDoGrind` — gate **before** `DecreaseMountLevel`. The integrity-after-grind ratio is already computed by line 190 (with janitor adjustments at 192-208). When `integrityRatio <= 0f`, the block is predicted to fully dismount; check the slot at that point.

```csharp
if (integrityRatio <= 0f && !Mod.TryClaimDismountSlot())
{
    // Skip this grind to spread dismount cost across ticks.
    // Block stays in PossibleGrindTargets, retried next tick.
    if (profilerTs != 0L) { /* log earlyExit=dismountSlot */ }
    return false;
}
```

Cap = 3/tick. The mechanical-slot cap (1/tick) remains as a stricter sub-budget that fires inside the `IsFullyDismounted` branch — a mechanical block must claim **both** slots.

## Cap rationale

- **3 dismounts/tick × 60 ticks/sec = 180 dismounts/sec** — well above any realistic player throughput (a player wielding a grinder finishes maybe 1 block/sec).
- Single-BaR effective rate is unchanged for normal grinding (one weld cycle ≈ 1.6 s, one full dismount per BaR per cycle is well within the 3/tick cap).
- The cap matters in **multi-BaR convergence** scenarios — e.g., 8 BaRs grinding the same scrap pile, all reaching final integrity on the same tick. Old behavior: 8 × 12 ms = 96 ms tick stall. New behavior: 3 dismounts go through, 5 retry next tick = 36 ms + 36 ms across two ticks.
- Hard-coded for now. Could be exposed as a setting in a follow-up if admins need tuning.

## Verification

1. **Build clean** — `dotnet build ... -c Release -v minimal` → 0 warnings, 0 errors. ✓
2. **Re-profile** — same 58-BaR scenario. Expected outcomes:
   - `ServerDoGrind` log entries with `earlyExit=dismountSlot` should appear when many BaRs converge on dismounts.
   - Per-tick max for the `Update` domain should drop on dismount-heavy ticks (no more 60+ ms compound stalls).
   - Total grinds-per-session is unchanged in steady state (skipped grinds are retried next tick).
3. **Regression — single-BaR throughput** — one BaR grinding a long row of armor blocks should still progress at the same rate (3/tick is way above its 1/cycle natural rate).
4. **Regression — mechanical blocks** — mechanical blocks now check **two** slots (mechanical 1/tick AND dismount 3/tick). They were already capped to 1/tick by the mechanical slot, so the dismount slot adds no new restriction in practice.

## See also

- BUG-097 (mechanical grind slot) — the original 1/tick cap for mechanical-block destruction.
- BUG-105 — diagnostic instrumentation that surfaced `decreaseMs` as the dominant grind cost.
- BUG-107 — sibling fix applying the same pattern to `proj.Build()` on the welding side.
- Profile session: `20260428202503-profiling`.
