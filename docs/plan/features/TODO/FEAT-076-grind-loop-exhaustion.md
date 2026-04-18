# FEAT-076: Grind loop exhaustion
## Status: In Progress
## Priority: Medium
## Version: v2.5.4
## Summary
Skip the 256-entry grind loop iteration for BaRs whose targets are all grid-limited, assigned, or destroyed. Same pattern as existing `_weldLoopExhausted`.
## Motivation
With 58 BaRs and `MaxSystemsPerTargetGrid=20`, 38 BaRs can't grind but still iterate all 256 targets every stagger cycle doing null checks and grid-limit checks. Profiling (0ms threshold) showed 0.15-4.3ms per grid-limited BaR per tick — wasted work.
## Design
- Add `_grindLoopExhausted`, `_grindExhaustedAtHash`, `_grindExhaustedSaturatedCount` fields
- At start of grind lock: if exhausted + hash unchanged + saturated count unchanged → skip (return)
- At end of loop: if `!grinding && !needGrinding` → mark exhausted with current hash and saturated count
- Resets when: target list hash changes (new scan), saturated grid count changes (BaR joins/leaves grid)
- Falls through correctly to welding in GrindBeforeWeld mode (returns grinding=false, needGrinding=false)
## Files Affected
- `NanobotSystem.cs` — new fields
- `NanobotSystem.Grinding.cs` — exhaustion check and set logic
