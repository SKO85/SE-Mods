# Reviews

Code review notes organized by phase and file.

## Template

Filename: `REVIEW-[phase]-[file-or-topic].md`

```
# Review: [File or Topic]
## Phase: [1-4]
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
