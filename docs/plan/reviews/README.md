# Reviews

Code review notes organized by topic or scope.

## Ticket ID format

- **New tickets**: `REVIEW-YYMMDD.N` where `YYMMDD` is the date the review was opened and `N` is an auto-incrementing sequence number for that day, starting at 1. Example: the first review opened on 2026-05-12 is `REVIEW-260512.1`.
- **Older tickets** (pre-format-change): keep their original `REVIEW-<topic>` filenames as-is. Do not rename. Cross-references in other tickets must keep matching the existing filename.
- Filename: `<id>-<short-name>.md`. Example: `REVIEW-260512.1-async-scan-audit.md`.

## Template

```
# <id>: [Title]
## Status: Open | In Progress | Done
## Reviewer: [Name / AI]
## Date: [YYYY-MM-DD]
## Version: [target version]
## Findings
- [Finding 1]
- [Finding 2]
## Recommendations
- [Recommendation 1]
## Action Items
- [ ] [Item 1]
```

## Rules

- Recommendations must be performance-conscious. This mod runs every tick in a game loop — avoid suggesting changes that add allocations, LINQ in hot paths, or unnecessary complexity.
- Do not recommend over-engineering. Suggestions should target the minimum change needed. No refactoring for its own sake, no speculative abstractions.
- Move reviews from `TODO/` to `DONE/` when status becomes `Done`. Delete reviews that are superseded or no longer applicable.
