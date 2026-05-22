# Bugs

Track code bugs found during review or internal testing.

## Ticket ID format

- **New tickets**: `BUG-YYMMDD.N` where `YYMMDD` is the date the ticket was opened and `N` is an auto-incrementing sequence number for that day, starting at 1. Example: the third bug opened on 2026-05-12 is `BUG-260512.3`.
- **Older tickets** (pre-format-change): keep their original `BUG-NNN` IDs as-is. Do not renumber. Cross-references in code comments (`BUG-127`, `BUG-160`, etc.) must keep matching the ticket file name.
- Filename: `<id>-<short-name>.md`. Example: `BUG-260512.3-grind-loop-race.md`.

## Template

```
# <id>: [Title]
## Status: Open | In Progress | Fixed | Won't Fix
## Severity: Critical | High | Medium | Low
## Version: [target version]
## Found In: [Phase / File / Context]
## Description
[What's wrong]
## Steps to Reproduce
[If applicable]
## Root Cause
[Once identified]
## Fix
[Once resolved — file:line reference]
```

## Rules

- Fixes must be performance-conscious. This mod runs every tick in a game loop — avoid allocations, LINQ in hot paths, and unnecessary complexity.
- Do not over-engineer fixes. Fix the bug with the minimum change needed. No refactoring, no abstractions, no "while we're here" improvements.
- Move tickets from `TODO/` to `DONE/` when status becomes `Fixed` or `Won't Fix`. Delete tickets that are obsolete or no longer reproducible — do not park them in `DONE/` with a misleading status.
