# FEAT-052: Remove "Help Others" option from terminal
## Status: Done
## Priority: Low
## Version: v2.5.0
## Summary
Hide the native "Help Others" checkbox from the BaR terminal and force the value to `false`. The option has no meaningful function for the mod.
## Motivation
The "Help Others" checkbox is a native SE welder property that was carried over but adds no value to the BaR system. When enabled it bypassed the block assignment system (allowing multiple BaRs to target the same block) and changed neutral/no-ownership block filtering. These behaviors are undesirable — the assignment system exists to distribute work efficiently.
## Design
- `NanobotSystem.Init.cs`: Force `_Welder.HelpOthers = false` on initialization.
- `Terminal.cs`: Hide the native checkbox by setting `Visible = (block) => false`. Remove the `_HelpOthers` field and the `CustomControlGetter` move logic.
- `NanobotSystem.Welding.cs`: Remove all `!_Welder.HelpOthers` guards from assignment checks (always true now). Replace `_Welder.HelpOthers` in `IncreaseMountLevel` calls with `false`.
- `NanobotSystem.State.cs`: Simplify `IsRelationAllowed4Welding` — neutral/no-ownership blocks are always rejected.
- `ScanClusterCoordinator.cs`: Remove `HelpOthers` from cluster key (always same value).
## Files Affected
- `NanobotSystem.Init.cs` — force `HelpOthers = false`
- `Terminal.cs` — hide checkbox, remove field and move logic
- `NanobotSystem.Welding.cs` — simplify assignment checks, hardcode `false` in weld calls
- `NanobotSystem.State.cs` — simplify relation check
- `Cluster/ScanClusterCoordinator.cs` — remove from cluster key
## Testing
- Terminal should not show the "Help Others" checkbox.
- Block assignment system always active (no bypass).
- Welding/grinding behavior unchanged from default (HelpOthers was typically off).
