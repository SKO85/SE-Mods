# Review: Full Code Review Round 4
## Phase: Pre-merge review of fix/new_v2.5.0 branch
## Reviewer: Claude
## Date: 2026-03-26
## Version: v2.5.0
## Findings
- BUG-059: `isDedicated` computed as `!IsServer` — shows DS note to listen-server clients
- BUG-060: Grid cost timestamp inflated by StopAndLog overhead in AsyncAddBlocksOfGrid
- BUG-061: Dead code `IsBuildingBlockedAtPosition` in SafeZoneHandler
- BUG-062: Orphaned XML doc comment — double `<summary>` on IsProjectorGridBuildBlocked
- BUG-063: Indentation mismatch in Mod.RebuildSourcesAndTargetsTimer try block
- FEAT-059: Duplicated admin broadcast logic with per-call player list allocations
## Recommendations
- All 6 issues fixed in same session
## Action Items
- [x] BUG-059: Remove isDedicated parameter, drop DS note
- [x] BUG-060: Capture end timestamp before StopAndLog
- [x] BUG-061: Remove dead IsBuildingBlockedAtPosition method
- [x] BUG-062: Remove orphaned summary XML comment
- [x] BUG-063: Re-indent try block body
- [x] FEAT-059: Extract SendToAdmins helper with reusable player list
- [x] FEAT-060: Add sim-speed min/avg to profile summary
- [x] FEAT-061: Debug HUD hint when local HUD is hidden
