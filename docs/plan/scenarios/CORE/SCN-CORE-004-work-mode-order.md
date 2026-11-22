# SCN-CORE-004: Work-mode order when weld and grind work exist together

## Area: CORE
## Tier: 2
## Preconditions
- 1 BaR (BoundingBox, janitor NoOwnership) with damaged own blocks on its grid and a derelict grid in range.
## Steps
1. `test run S77,S78`.
## Expected outcome
- WeldBeforeGrind: no grinding while own blocks still need welding (at most one hand-over cycle), then the derelict is ground.
- GrindBeforeWeld: no welding while the derelict still has blocks, then the own blocks are welded.
- WeldOnly / GrindOnly never do the other (S26, S48 / GrindOnly scenarios).
## Coverage
ingame:S77
ingame:S78
## Last verified
v2.5.6 build 261122.28 2026-11-22 — in game: S77 / S78 green, no out-of-order work.
## Related
FEAT-261122.4 (item: GrindBeforeWeld)
