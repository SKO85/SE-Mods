# SCN-CFG-001: ModSettings.xml out-of-range values are clamped on load
## Area: CFG
## Tier: 1
## Preconditions
- A `ModSettings.xml` in the world storage folder with deliberately bad values, e.g.
  `MaxBackgroundTasks=99`, `MaxSystemsPerTargetGrid=0`, `MaxGrindMsPerTick=0`, `AssignmentTtlSeconds=1`,
  `StaggerGroupCount=50`, `Range=5000`, `WeldingMultiplier=0`, `LogLevel` garbage.
## Steps
1. Load the world (or `/nanobars config reload`).
2. `/nanobars config list`.
## Expected outcome
- Every bounded field is inside its documented range (CLAUDE.md "Key Settings"):
  `MaxBackgroundTasks` 1–10, `MaxSystemsPerTargetGrid` ≥ 1, `MaxGrindMsPerTick` / `MaxWeldMsPerTick`
  1–100 (a value < 1 becomes the default 8, not 1), `AssignmentTtlSeconds` 2–30, `StaggerGroupCount` 0–10,
  `Range` ≤ `WELDER_RANGE_MAX_IN_M`, multipliers within `WELDING_GRINDING_MULTIPLIER_MIN/MAX`.
- `SyncModSettings.ValidateAndClamp` returns `true` (adjusted) and the log carries one Event line per
  clamped field; a second reload returns `false` (nothing to adjust).
- Clients receive the clamped copy (clamp also runs in `SyncModSettingsReceived`).
## Perf envelope
n/a
## Coverage
planned: unit:SyncModSettingsTests.ValidateAndClamp_ClampsEveryBoundedField
## Last verified
never
## Related
FEAT-260910.3, BUG-076 (config no bounds), BUG-260610.30 / .42 (range bounds aligned with clamps)
