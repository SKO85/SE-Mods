# FEAT-003: Welding Loop Performance — AssignToSystem Early-Out + Ignore Preservation
## Status: Done
## Priority: Medium
## Version: v2.5.0
## Summary
Reduce unnecessary Weldable() evaluations in the welding loop by (1) adding a cheap read-only AssignToSystem pre-filter and (2) preserving Ignore flags across scan refreshes.
## Motivation
Profiler data from a 50+ BaR session showed two waste patterns in `ServerTryWelding`:

1. **Weldable() called before AssignToSystem check**: Weldable() costs ~4us/call and ran on every candidate before AssignToSystem rejected blocks claimed by other systems. With 50+ BaRs, 5-26 blocks/tick were rejected after paying the Weldable cost — ~40,000+ unnecessary evaluations over 120s.

2. **Ignore flags reset every scan refresh**: Background scanner creates new TargetBlockData every ~2s, resetting Ignore=false. Blocks already confirmed fully welded got re-evaluated via Weldable() after each refresh. Profiler showed `skippedByIgnore=0` consistently.
## Design
### 1. IsAssignedToOtherSystem (read-only check)
Added `IsAssignedToOtherSystem(block, systemId)` extension method to `BlockSystemAssigningHandler`. Pure read — one dictionary lookup, no side effects. Returns true only if the block is claimed by a different system.

### 2. Early-out in welding loop
Inserted the read-only check between the script-controlled check and the Weldable() check. Blocks already claimed by other systems skip Weldable() entirely. The existing AssignToSystem claim call remains for blocks that pass all checks.

Conditions match the existing AssignToSystem guard: `AssignToSystemEnabled`, `!HelpOthers`, `CurrentPickedWeldingBlock == null`.

### 3. Ignore preservation across scan refresh
Before clearing the old weld target list, snapshot which blocks had `Ignore=true`. After adding new targets, restore the flag for blocks still at `IsFullIntegrity`. This prevents fully-welded blocks from being re-evaluated by Weldable() every ~2s.

Safety: IsFullIntegrity re-validation ensures damaged blocks are not kept Ignored. Not applied to grinding (no equivalent pre-filter benefit).
## Files Affected
| File | Change |
|------|--------|
| `Handlers/BlockSystemAssigningHandler.cs` | Added `IsAssignedToOtherSystem()` read-only extension method |
| `NanobotSystem.Welding.cs` | Added early AssignToSystem read-only check before Weldable() |
| `NanobotSystem.Scanning.cs` | Preserve Ignore flags across weld target list swap with IsFullIntegrity safety check |
## Expected Impact
- Weldable() calls per tick: ~40 → ~15-20 (assigned blocks skipped)
- Weldable() calls after scan refresh: further reduced (completed blocks stay Ignored)
- No functional behavior change: claiming still via AssignToSystem; Ignore re-validated via IsFullIntegrity
## Testing
1. Build: `msbuild SKO-SE-Mods.sln /p:Configuration=Release`
2. In-game: Place 50+ BaR systems with weld targets, run 120s profiler session
3. Check `NanobotProfiler.ServerTryWelding.log`: `skipAssign` should include blocks previously showing as `weldChecked`; total `weldChecked` should decrease
4. Check `skippedByIgnore` — should now be >0 after scan refreshes (Ignore flags preserved)
5. Functional: All blocks still get welded; damaged blocks get re-evaluated after taking damage
## Profiler Results (120s session, ~50 BaRs, 266 entries)
### ServerTryWelding timing
| Metric | Value |
|--------|-------|
| Median | 1.4ms |
| P90 | 2.5ms |
| P95 | 5.0ms |
| Active BaRs avg (welding=True) | 1.6ms |
| Idle BaRs avg (welding=False) | 5.0ms |

82% of calls complete under 2ms. One 14.3ms outlier attributed to game-engine stall (had skipAssign=46, weldChecked=1).

### skipAssign (early AssignToSystem check)
- Active in 78% of entries (208/266)
- Range: 0–55, median 4, P90=36
- **Dominant fast-path** — most BaRs skip Weldable() entirely for already-claimed blocks

### weldChecked (Weldable() calls)
- Median: **2** (down from ~40 pre-optimization), P90=2
- High outliers (up to 53) only on idle BaRs exhausting the full list

### skipIgnore (Ignore preservation)
- Active in 12/266 entries (4.5%), values up to 31
- Only fires on idle BaRs (welding=False) scanning the full list
- When it fires, significant: 4–31 blocks skipped per entry

### Observations
- `skipAssign` is the big win — keeps the vast majority of ticks cheap
- `skipIgnore` provides secondary benefit for idle BaRs iterating the full target list
- Idle BaRs (welding=False, 18 entries) are the remaining expensive cases — they iterate the full list hitting skipAssign + skipIgnore + skipGrid limits
