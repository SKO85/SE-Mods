# SCN-WELD-001: WeldOnly repairs damaged blocks and never grinds
## Area: WELD
## Tier: 1
## Preconditions
- One BaR, `WorkMode = WeldOnly`, `SearchMode = BoundingBox`, `UseGrindJanitorOn = NoOwnership`
  (a grind candidate deliberately in range), stocked cargo.
- Own grid with 10 damaged armor blocks; a second unowned grid (owner 0) 40 m away.
## Steps
1. Enable the BaR; watch `Blocks to build` and `Blocks to grind` on the panel for 60 s.
2. Switch to `WorkMode = GrindOnly` for comparison, then back.
## Expected outcome
- `WeldOnly`: `PossibleGrindTargets.CurrentCount == 0` (scan stops early per work mode, FEAT-007),
  the unowned grid is untouched, `ServerDoGrind` is never called; damaged blocks reach full integrity.
- `GrindOnly`: the reverse — no `ServerDoWeld`, the unowned grid is razed.
- Terminal `WeldOnly_On` / `GrindOnly_On` toolbar actions switch the mode and trigger an immediate rescan.
## Perf envelope
`ServerDoWeld` steadyAvgMs ≤ 2; `ServerDoGrind.calls == 0` in WeldOnly.
## Coverage
ingame:S01
## Last verified
never
## Related
FEAT-007 (work-mode-aware scan stop), BUG-018 (secondary task), BUG-097 (work-mode races), FEAT-260910.2
