# SCN-CORE-003: No power / power restored / insufficient power

## Area: CORE
## Tier: 2
## Preconditions
- One BaR with 5 damaged armor blocks and stock. A) rig battery off; B) welding power demand set above
  what the battery delivers (mod setting `Welder.MaximumRequiredElectricPowerWelding`).
## Steps
1. A: run 10 s, then switch the battery on. B: run 30 s.
## Expected outcome
- A: while unpowered the BaR shows unpowered and welds nothing; after power returns every block is repaired.
- B: the BaR reports unpowered (block not working) instead of welding as if powered; no exceptions.
## Perf envelope
harness metrics; thresholds from the first green runs.
## Coverage
ingame:S19
ingame:S20
## Last verified
never
## Related
FEAT-260910.2
