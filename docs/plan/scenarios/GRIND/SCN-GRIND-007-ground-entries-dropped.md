# SCN-GRIND-007: Ground or removed blocks leave a BaR's grind list instead of being re-checked every tick

## Area: GRIND
## Tier: 2
## Preconditions
- Clusters of BaRs grinding derelicts (S66 / S67 / S70).
## Steps
1. Let the BaRs grind; blocks get dismounted and removed while they stay in the BaRs' target lists until the next scan.
## Expected outcome
- The first walk that meets a gone or fully dismounted entry drops all such entries from the list (dismounted ones
  queued for raze once); later walks only pass live entries. Partly ground blocks stay.
## Perf envelope
ServerTryGrinding `skipClosed` + `skipDestroyed` per call near 0 after the drop (was 13-36 in S66 / S67 / S70);
`deadRemoved` shows the drops.
## Coverage
unit:DeadGrindEntryTests
ingame:S66
ingame:S67
ingame:S70
## Last verified
v2.5.7 build 261123.36 2026-11-23 — in game: S66 / S67 / S70 pass; skipClosed 13-36 -> 1.3-2.1 per call; unit green.
## Related
BUG-261123.3, REVIEW-261123.1
