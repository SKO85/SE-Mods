# SCN-CFG-002: Legacy settings migrate to version 7; deprecated GrindIfWeldGetStuck folded
## Area: CFG
## Tier: 1
## Preconditions
- A `ModSettings.xml` from an old release: `Version` 0 (or absent), welder section with zeroed fields,
  `AllowedWorkModes` containing only the deprecated `GrindIfWeldGetStuck` bit, `WorkModeDefault =
  GrindIfWeldGetStuck`, `AllowedGrindJanitorRelations = 0`, a `WeldingMultiplier` > 10.
- Optionally a per-block saved `SyncBlockSettings` with `WorkMode = GrindIfWeldGetStuck`.
## Steps
1. Load the world; `/nanobars config list`; open a BaR terminal.
2. `/nanobars config reload` a second time.
## Expected outcome
- `SyncModSettings.AdjustSettings` returns `true`, `Version` becomes `CurrentSettingsVersion` (7),
  zeroed welder fields are restored to defaults, `WorkSpeed` is set to 10 when a multiplier exceeded 10.
- `GrindIfWeldGetStuck` folds to `WeldBeforeGrind` in both the allowed mask and `WorkModeDefault`;
  `AllowedGrindJanitorRelations == 0` is restored to the default mask (BUG-093).
- The per-block `WorkMode` migrates the same way (`SyncBlockSettings` migration).
- Second reload: `AdjustSettings` returns `false`; nothing changes.
## Perf envelope
n/a
## Coverage
planned: unit:SyncModSettingsTests.AdjustSettings_MigratesVersion0To7
planned: unit:SyncModSettingsTests.ValidateAndClamp_FoldsDeprecatedGrindIfWeldGetStuck
## Last verified
never
## Related
FEAT-260910.3, BUG-101 (GrindIfWeldGetStuck deadlock removed), BUG-260511.12, BUG-093
