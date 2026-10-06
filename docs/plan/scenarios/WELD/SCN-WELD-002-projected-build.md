# SCN-WELD-002: Projected block build incl. DLC-missing skip and silent-fail blacklist
## Area: WELD
## Tier: 2
## Preconditions
- Survival or creative world. Rig with a BaR, stocked cargo and a large projector loading a 20-block
  armor blueprint at projection offset (0, 6, 0). `AllowBuild` on, `Welder.AllowBuildFixed` off,
  `SafeZoneAllowsBuildingProjections` true.
- Variant B: a blueprint containing a DLC block the server owner lacks.
- Variant C: a projected block whose placement is physically blocked (overlaps an existing block).
## Steps
1. Enable the BaR; watch `Blocks to build` list projected blocks (name suffix marks projected).
2. Let it run 120 s.
3. Variant B: repeat with the DLC blueprint. Variant C: repeat with the overlapping blueprint.
## Expected outcome
- All 20 projected blocks are materialised (`proj.Build`) then welded to full; the projector's
  remaining block count reaches 0; the rig grid gains 20 blocks.
- Projected → physical hand-over keeps the assignment (BUG-030) and re-resolves after the projector
  changes grid EntityId (lock-on retry on loss).
- B: the DLC block is skipped with a panel/log note, the rest builds (BUG-120).
- C: after `PROJ_BUILD_MAX_SILENT_FAILS = 3` the block key lands in `_BrokenProjBuildKeys` and is
  no longer retried; the BaR continues with other targets instead of spinning.
- Global `proj.Build` budget: no more than 3 builds per tick across all BaRs (BUG-107).
## Perf envelope
`ServerDoWeld` `buildMs` ≤ 40 per call (engine cost); `resolveMs` ≤ 5; steadyAvgMs ≤ 3.
## Coverage
planned: ingame:S03
## Last verified
never
## Related
BUG-030, BUG-107, BUG-108, BUG-120, BUG-260610.35, FEAT-260910.2
