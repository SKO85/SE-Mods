# SCN-BUDGET-005: Failure cooldown: many BaRs do not bounce off one unweldable block
## Area: BUDGET
## Tier: 3
## Preconditions
- `BlockFailureCooldownSeconds = 4` (default; 0 disables).
- 5 BaRs (BoundingBox, overlapping areas) and one target grid where exactly one damaged block needs a
  component that no source has (e.g. a Gravity Generator needing `GravityGenerator` components), plus
  20 ordinary damaged armor blocks with stock available.
## Steps
1. Enable all BaRs; profile 60 s (`/nanobars profile start 60 0`).
2. Read `ServerTryWelding` details (`skipFailCooldown`, `componentFails`) and `BlockFailCooldowns` on the HUD.
3. Add the missing component to cargo.
## Expected outcome
- The unweldable block is tried by one BaR, fails, and is parked in `BlockFailureCooldownHandler`
  (`CooldownCount == 1`); the other BaRs skip it (`skipFailCooldown` > 0) instead of each spending a
  cycle on it. The 20 armor blocks still get welded — the cooldown never starves the good targets.
- After the TTL the block is retried; once the component arrives it is welded and the entry expires.
- With `BlockFailureCooldownSeconds = 0` every BaR retries the block each cycle (`componentFails` spread).
## Perf envelope
`ServerTryWelding` steadyAvgMs ≤ 1 with 5 BaRs; no `MissingComponents` flapping on the panel.
## Coverage
manual
## Last verified
never
## Related
BUG-116 (Weldable on starved blocks), BUG-029 / BUG-048 (assignment leaks on failure), BUG-260824.1 (oversized components)
