# SCN-GRIND-002: Janitor relation matrix × DisableOnly / HackOnly
## Area: GRIND
## Tier: 2
## Preconditions
- One BaR, `GrindOnly`, BoundingBox. Five target grids 40 m away, one per relation: unowned,
  own, faction-mate, neutral faction, enemy faction (set war). Each grid: 5 armor + 1 functional
  block (e.g. `LargeBlockSmallContainer`).
- `Welder.AllowedGrindJanitorRelations` = all; `DecreaseFactionReputationOnGrinding` on for the last run.
## Steps
| Run | `UseGrindJanitorOn` | `GrindJanitorOptions` | Expected |
|---|---|---|---|
| 1 | `Enemies` | none | only the enemy grid razed |
| 2 | `Neutral` | none | only the neutral grid |
| 3 | `NoOwnership \| Enemies` | none | unowned + enemy |
| 4 | `Enemies` | `DisableOnly` | enemy functional block ground to non-functional, then stops (`_grindJanitorDone`) |
| 5 | `Enemies` | `HackOnly` | enemy functional block ground to the hack threshold (ownership change), then stops |
| 6 | `Enemies` + reputation on | none | reputation with the enemy faction decreases per ground block |
## Expected outcome
- Relation resolved through `GridOwnershipCacheHandler` (10 s cache) — changing faction standing takes
  effect within one cache refresh; `Owner` / `FactionShare` never grind own/faction grids unless selected.
- With a `*Fixed` relation the terminal switch is disabled and the setting cannot be changed from a PB.
- DisableOnly/HackOnly: armor blocks are ignored (nothing to disable/hack); the BaR reports Idle after.
## Perf envelope
`ServerDoGrind` `friendlyMs` ≤ 0.1 per call (relation lookup cached).
## Coverage
manual
## Last verified
never
## Related
BUG-260824.4 (friendly grind fallback), BUG-093, BUG-260612.30, BUG-260502.4 (friendly rebuild scaling)
