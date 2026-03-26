# Features

Track feature requests and designs for v2.5.0+.

## Template

Filename: `FEAT-[number]-[short-name].md`

```
# FEAT-[number]: [Title]
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
