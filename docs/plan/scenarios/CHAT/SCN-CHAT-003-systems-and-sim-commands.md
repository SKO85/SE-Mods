# SCN-CHAT-003: systems enable / disable and sim commands act on exactly the matching BaRs

## Area: CHAT
## Tier: 2
## Preconditions
- Several BaRs on different grids and owners (some offline), some already switched off.
## Steps
1. `/nanobars systems disable all`, `... --grid <part of name>`, `... --owner <part of name>` (also `Nobody`).
2. `/nanobars sim 0.5`, `sim 1.5`, `sim 0,5`, `sim reset`.
## Expected outcome
- Matching is case-insensitive on part of the grid / owner name, names with spaces work, offline owners are found by
  identity, only blocks that change state are counted; no match / missing argument / unknown target is a clear error
  and changes nothing.
- `sim` accepts 0.1-1.0 with an invariant decimal point, rejects everything else, `reset` removes the override.
## Perf envelope
n/a
## Coverage
unit:SystemsCommandTests
## Last verified
v2.5.7 build 261123.63 2026-11-23 — unit green.
## Related
REVIEW-261122.2 #11, FEAT-261122.4 follow-up list
