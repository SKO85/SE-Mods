# SCN-WELD-002: Projected block build incl. DLC-missing skip and silent-fail blacklist
## Area: WELD
## Tier: 2
## Preconditions
- Survival or creative world. Rig with a BaR, stocked cargo and a large projector loading a 20-block
  armor blueprint at projection offset (0, 6, 0). `AllowBuild` on, `Welder.AllowBuildFixed` off,
  `SafeZoneAllowsBuildingProjections` true.
- Variant B: a blueprint containing a DLC block the server owner lacks.
- Variant C: a projected block whose placement is physically blocked (overlaps an existing block).
- Variant E (game >= 1.211.018, survival): `PushComponentImmediately` on, BaR block inventory empty, stock only
  in the cargo. Optional: blueprint already loaded while the BaR sits idle (cold start).
## Steps
1. Enable the BaR; watch `Blocks to build` list projected blocks (name suffix marks projected).
2. Let it run 120 s.
3. Variant B: repeat with the DLC blueprint. Variant C: repeat with the overlapping blueprint.
4. Variant D (survival, component accounting): note the cargo totals, let the BaR build and fully
   weld a 1-block heavy-armor projection, then hand-grind the block and collect the components.
5. Variant E: run 120 s with DebugMode on (SP) and the default log level; then switch off the stock for one
   component type, wait for the skip, restore it and wait past the skip expiry (BUG-261122.2).
## Expected outcome
- All 20 projected blocks are materialised (`proj.Build`) then welded to full; the projector's
  remaining block count reaches 0; the rig grid gains 20 blocks.
- Projected → physical hand-over keeps the assignment (BUG-030) and re-resolves after the projector
  changes grid EntityId (lock-on retry on loss).
- B: the DLC block is skipped with a panel/log note, the rest builds (BUG-120).
- C: after `PROJ_BUILD_MAX_SILENT_FAILS = 3` the block key lands in `_BrokenProjBuildKeys` and is
  no longer retried; the BaR continues with other targets instead of spinning.
- Global `proj.Build` budget: no more than 3 builds per tick across all BaRs (BUG-107).
- D: components returned by grinding = components taken from cargo — no free creation component
  (BUG-261023.2 / PR #148). The `ServerDoWeld` profile line of the build shows
  `creationCharged=True` (False in creative, or when the engine took it from the welder inventory).
- E: every projected block is built although the block inventory starts empty (BUG-261122.1: one creation
  component is placed in the block inventory for `proj.Build`, the game takes it). Debug panel
  `ProjBuild: noBlock=0`; no `projected build left no block; reason=builderInventoryEmpty` in the log;
  `ServerDoWeld` profile `welderCompAtBuild>=1` on builds. Accounting as in D (no free and no double charge).
  Skipped blocks are retried after the expiry, not only after a power cycle (BUG-261122.2). A new blueprint on
  an idle BaR is picked up within one scan interval (BUG-261122.3).
## Perf envelope
`ServerDoWeld` `buildMs` ≤ 40 per call (engine cost); `resolveMs` ≤ 5; steadyAvgMs ≤ 3.
## Coverage
ingame:S114 (small-grid projection, large BaR), ingame:S115 (small BaR on the small ship) — Steam report 2026-11-23
unit:ProjectedBuildComponentTests.Lend_MovesOnePieceFromTheTransport_WhenTheBlockInventoryHasNone
unit:ProjectedBuildComponentTests.Lend_FullBlockInventory_FailsWithoutLosingThePiece
unit:ProjectedBuildComponentTests.Return_MovesTheUntakenPieceBackToTheTransport
unit:ProjectedBuildComponentTests.SilentFailSkip_Expires_NreSkipStays_CountIsPublished
unit:ProjectorScanCacheTests.ColdStart_DropsEmptyAndScanCacheEntriesOfTheFiringGrids
ingame:S03
ingame:S27
ingame:S42
ingame:S46
unit:LimitsPreCheckTests
## Last verified
v2.5.6 build 261122.3 2026-11-22 — in game (01_211_018, SP survival): variant E steps 1-3 green (239/239 built, lent+engineTook on every build, no silent fails); D accounting by hand-grind, cold-start timing and DS not yet run. Unit: lend/return, skip expiry, cold-start cache drop
## Related
BUG-030, BUG-107, BUG-108, BUG-120, BUG-260610.35, BUG-261023.2 (GitHub PR #148), BUG-261122.1..3 (GitHub #149, #150, PR #151), FEAT-260910.2
