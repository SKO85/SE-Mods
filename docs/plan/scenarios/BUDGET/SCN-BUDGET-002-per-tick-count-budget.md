# SCN-BUDGET-002: Per-tick weld/grind count budget refuses beyond cap, resets next frame
## Area: BUDGET
## Tier: 1
## Preconditions
- `MaxGrindsPerTick` / `MaxWeldsPerTick` explicit (e.g. 3) or auto (0 → 5..10 scaled by BaR count).
- Enough BaRs with work in the same stagger slot to exceed the cap (e.g. 6 BaRs, `StaggerGroupCount = 1`).
## Steps
1. Enable all BaRs; open the debug HUD (`/nanobars debug on`, `debug show`).
2. Read `Weld Budget peak/max` and `Grind Budget peak/max` over 30 s; note the `CAPPED` flag.
3. Set the cap to 1; observe; set back.
## Expected outcome
- `PerTickBudget.TryClaim` allows at most `EffectiveMax` claims per `GameplayFrameCounter` value,
  then refuses; the next frame starts at 0. `PeakUsed` holds the high-water mark until `ResetStats`
  (the HUD resets it every 2 s window).
- `Refund` returns one slot and never goes below zero (deferred proj-build / dismount gate refunds).
- Auto mode: `GetEffectiveMaxGrindsPerTick()` = `max(5, min(10, BaR count))`; explicit value wins.
- BaRs refused this tick keep their lock-on and retry next cycle (no assignment leak).
## Perf envelope
n/a (the budget *is* the perf control).
## Coverage
planned: unit:PerTickBudgetTests.ClaimsUpToMax_ThenRefuses
planned: unit:PerTickBudgetTests.ResetsWhenFrameAdvances
planned: unit:PerTickBudgetTests.Refund_ReturnsOneSlot_NotBelowZero
planned: unit:ModResolverTests.GetEffectiveMaxGrindsPerTick_AutoClampsBetween5And10
## Last verified
never
## Related
FEAT-260910.3, BUG-106 / BUG-107 (dismount / proj-build budgets), BUG-260610.35 (refund on defer), BUG-260612.11
