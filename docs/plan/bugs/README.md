# Bugs

Track code bugs found during review or internal testing.

## Template

Filename: `BUG-[number]-[short-name].md`

```
# BUG-[number]: [Title]
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
