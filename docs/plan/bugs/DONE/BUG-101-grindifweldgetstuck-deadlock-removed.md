# BUG-101: `GrindIfWeldGetStuck` deadlocked into Idle when no weld targets — mode removed

## Status: Fixed
## Severity: High
## Version: v2.5.4
## Found In: `NanobotSystem.Operations.cs`, `NanobotSystem.Welding.cs`

## Description

A server admin reported that switching a BaR's WorkMode to **"Grind if weld get stuck"** immediately left the block in `State: Idle` — never welded, never grinded, never collected. They verified the issue is not block-, grid-, or install-specific (replaced blocks/grids and reinstalled the mod). The only fix was switching the WorkMode away from this option.

## Steps to Reproduce

1. On v2.5.3 or later (pre-fix), place a BaR on a grid that has grind-priority targets in range and **no** weld-priority targets (e.g., a grid that is fully built, with a few janitor-marked or floating-debris blocks around it).
2. Open the BaR terminal and set **Work mode** to *Grind if weld get stuck*.
3. Observe `State:` in the terminal info panel.

**Expected:** the BaR grinds the available grind targets (the player's prior workflow on pre-v2.5.3 builds).

**Actual:** `State: Idle` permanently. Welding never starts (no targets), grinding never starts (gate never opens), collecting never starts. Replacing the block, hacking, removing/replacing the grid, or reinstalling the mod has no effect — only switching to a different WorkMode resumes activity.

## Root Cause

Regression introduced by **BUG-097 section A** (v2.5.3). That fix narrowed the `GrindIfWeldGetStuck` fall-through gate from the long-standing `!(welding || transporting)` to:

```csharp
var weldStuck = needWelding && !welding && !transporting;
```

The intent was "only grind when welding has targets but is stuck on missing components / safe zone / priority starvation". But `needWelding` (`NanobotSystem.Welding.cs:25`) is initialized `false` and only flips `true` when the welding loop **iterates** a target and can't weld it. Two real cases leave `needWelding=false`:

1. `PossibleWeldTargets` is empty — loop body never runs.
2. `_weldLoopExhausted` is `true` and the target-list hash matches — early return at `Welding.cs:54-58` skips the loop without setting `needWelding`.

Both leave `weldStuck = false` → grinding never fires → `State: Idle` permanently. Pre-v2.5.3 the broader `!(welding || transporting)` gate masked the dependency.

## Fix

Removed the `GrindIfWeldGetStuck` work mode entirely — it was functionally redundant with `WeldBeforeGrind` (same fall-through semantics outside of the v2.5.3 narrowing) and the label "Grind if weld get stuck" admits both interpretations ("nothing to weld" vs "missing components only"), so removal was cleaner than litigating the definition.

- `Models/SyncBlockSettings.cs` — `WorkMode` setter and the `AssignReceived`-style merge path now silently rewrite `GrindIfWeldGetStuck` to `WeldBeforeGrind`. Catches XML load, ProtoBuf network sync, terminal writes, and chat-command writes at one chokepoint.
- `Models/SyncBlockSettings.cs` `CheckLimits()` — removed the `GrindIfWeldGetStuck` fallback in the AllowedWorkModes selection chain.
- `NanobotSystem.Operations.cs` — switch dispatch now stacks the deprecated value onto the `WeldBeforeGrind` case as defense-in-depth (any in-flight value that bypasses the setter still does the right thing).
- `Terminal/ComboBoxes.cs` — dropdown entry removed.
- `Terminal.cs` — `weldingAllowed` / `grindingAllowed` flag tests no longer reference `GrindIfWeldGetStuck`.
- `Models/SyncModSettings.cs` (Version<=4 default migration) and `Models/SyncModSettingsWelder.cs` (default field initializer) — `GrindIfWeldGetStuck` removed from default `AllowedWorkModes`.
- `Terminal.cs` enum — `GrindIfWeldGetStuck = 0x0004` value preserved with a deprecation docstring (so old XML/ProtoBuf payloads still deserialize). Bit `0x0004` reserved; do not reuse.
- `Handlers/HudHandler.cs:525` — left the `case WorkModes.GrindIfWeldGetStuck: s.ModeStuck++; break;` line in place. Dead but harmless; removing it would also require auditing the `ModeStuck` HUD field, which is out of scope.

## Verification

1. **Build clean** — `dotnet build ... -c Release -v minimal` → 0 warnings, 0 errors.
2. **Dropdown** — fresh world, place a BaR; the work-mode dropdown lists exactly four entries (no "Grind if weld get stuck").
3. **Migration** — load a saved world that previously had a BaR with `WorkMode=GrindIfWeldGetStuck` (serialized as `0x0004`). The terminal shows `Weld before grind` for that block. No console warnings.
4. **Original scenario** — set a BaR to `Weld before grind` in the same setup that broke (grind targets present, no weld targets). Confirm `State: Grinding` rather than `State: Idle`.
5. **Regression check on remaining modes** — `WeldBeforeGrind`, `GrindBeforeWeld`, `WeldOnly`, `GrindOnly` all behave identically to v2.5.3.

## See also

- **BUG-097** — the v2.5.3 ticket whose section A introduced this regression. Sections B (priority hash race) and C (GridSystemCount dip on same-grid lock-on) remain as fixed in v2.5.3 and are not affected by this change.
