# SCN-WELD-008: A starved BaR does not stop a stocked BaR elsewhere from repairing the same grid

## Area: WELD
## Tier: 2
## Preconditions
- Ship A: one BaR (Grids), 10 damaged armor blocks, empty cargo. Ship B, 25 m away, not connected: one BaR
  (BoundingBox), 500 SteelPlate in cargo, no damage. Both WeldOnly, WorkSpeed 10.
## Steps
1. Enable both BaRs.
## Expected outcome
- Ship B repairs all of ship A's blocks; ship A's BaR finding no components does not make B skip them
  (the "failed, skip for 15 s" mark is shared only between BaRs on the same connected grid group).
## Perf envelope
harness metrics (doneS); no threshold yet.
## Coverage
ingame:S86
unit:BlockFailureCooldownTests
## Last verified
v2.5.7 build 261123.7 2026-11-23 — in game: S86 10/10 in 3.4 s; unit green.
## Related
BUG-261123.1; Discord #help-topics "One BAR will not Help another BAR" (2026-10-04)
