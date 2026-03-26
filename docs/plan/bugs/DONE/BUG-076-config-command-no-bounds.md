# BUG-076: ConfigCommand accepts out-of-range values for integer/float settings
## Status: Fixed
## Severity: High
## Version: v2.5.0
## Found In: Code review round 6 — Chat/Commands/ConfigCommand.cs:313-327
## Description
The `IntSetting()` and `FloatSetting()` builder methods only validate that the input parses as the correct type. No range checking is performed. Commands like `/nanobars config set MaxBackgroundTasks 99999` or `/nanobars config set WeldingMultiplier -5` are silently accepted, potentially destabilizing the server.
## Root Cause
Missing bounds validation in the setting builder helpers.
## Fix
- `ConfigCommand.cs:313-327` — Added `min`/`max` parameters to `IntSetting()` and `FloatSetting()` with range check before calling the setter. Returns error message with valid range on violation.
- Applied bounds to all settings: Range (1-1000), MaximumOffset (0-1000), MaxBackgroundTasks (1-10), MaxSystemsPerTargetGrid (1-100), EmptyGridRescanDelaySeconds (0-300), StaggerGroupCount (0-10), MaxGrindsPerTick (0-100), AssignmentTtlSeconds (2-30), WeldingMultiplier (0.1-1000), GrindingMultiplier (0.1-1000).
- TypeLabel in `config list` now shows the valid range (e.g., `int, 1..10`).
