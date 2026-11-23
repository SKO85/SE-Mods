# SCN-WELD-009: Components spread thin over many containers do not stall a BaR group

## Area: WELD
## Tier: 2
## Preconditions
- Base with many containers (S105: 50 BaRs, 302 containers, 351 sources incl. BaRs), SteelPlate spread a few per
  container; target ships around the base.
## Steps
1. Enable the BaRs; let them weld until the containers near them run dry.
## Expected outcome
- A BaR whose capped pull (16 sources) finds nothing does not park the block for the whole supply group; it parks it
  only after looking through all its sources without a pick. Throughput stays steady to the end instead of decaying.
## Perf envelope
harness metrics (weldedPerSecond, doneS); pull cost per attempt unchanged (cap 16).
## Coverage
unit:PullRoundTests
ingame:S105
ingame:S86 (true starvation still parks)
## Last verified
v2.5.7 build 261123.34 2026-11-23 — in game: S105 200/200 in 10.3 s (was 248 s); S86 still passes; unit green.
## Related
BUG-261123.6, BUG-261123.1, SCN-WELD-008
