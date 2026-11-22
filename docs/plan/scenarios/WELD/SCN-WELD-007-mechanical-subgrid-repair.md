# SCN-WELD-007: Mechanical subgrid (rotor head) is repaired in Grids mode, not ground by the janitor

## Area: WELD
## Tier: 2
## Preconditions
- One BaR, Grids mode, WeldBeforeGrind, janitor NoOwnership on. A rotor on the BaR grid with its head
  subgrid carrying 3 damaged armor blocks built by the BaR owner (the subgrid has no owner).
## Steps
1. Enable the BaR.
## Expected outcome
- The subgrid is reached through the mechanical connection and its blocks are welded to full.
- None of them is ground (BUG-261122.7: unowned blocks the owner built are not janitor targets).
## Perf envelope
harness metrics; thresholds from the first green runs.
## Coverage
ingame:S18
ingame:S34
ingame:S35
ingame:S36
ingame:S37
ingame:S38
ingame:S44
## Last verified
never
## Related
BUG-261122.7
