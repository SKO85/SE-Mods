# Features

Track feature requests and designs.

## Ticket ID format

- **New tickets**: `FEAT-YYMMDD.N` where `YYMMDD` is the date the ticket was opened and `N` is an auto-incrementing sequence number for that day, starting at 1. Example: the second feature opened on 2026-05-12 is `FEAT-260512.2`.
- **Older tickets** (pre-format-change): keep their original `FEAT-NNN` IDs as-is. Do not renumber. Cross-references in code comments (`FEAT-040`, `FEAT-073`, etc.) must keep matching the ticket file name.
- Filename: `<id>-<short-name>.md`. Example: `FEAT-260512.2-priority-presets.md`.

## Template

```
# <id>: [Title]
## Status: Proposed | Approved | In Progress | Done | Rejected
## Priority: High | Medium | Low
## Version: [target version]
## Summary
[One-line description]
## Motivation
[Why is this needed?]
## Design
[How it works, settings, UI changes]
## Files Affected
[List of files that need changes]
## Testing
[How to verify]
```

## Rules

- Implementations must be performance-conscious. This mod runs every tick in a game loop — avoid allocations, LINQ in hot paths, and unnecessary complexity.
- Do not over-engineer. Implement the minimum needed to deliver the feature. No speculative abstractions or premature configurability.
- Move tickets from `TODO/` to `DONE/` when status becomes `Done` or `Rejected`. Delete tickets that are obsolete or superseded — do not park them in `DONE/` with a misleading status.
