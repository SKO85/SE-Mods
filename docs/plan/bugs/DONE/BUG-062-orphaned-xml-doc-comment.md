# BUG-062: Orphaned XML doc comment — double summary on IsProjectorGridBuildBlocked
## Status: Fixed
## Severity: Low
## Version: v2.5.0
## Found In: Review / NanobotSystem.Scanning.cs
## Description
When `IsProjectorGridBuildBlocked` was inserted between the `<summary>` comment for `AsyncAddBlockIfWeldTarget` and the method itself, it resulted in `IsProjectorGridBuildBlocked` having two consecutive `<summary>` blocks and `AsyncAddBlockIfWeldTarget` losing its doc comment.
## Fix
Removed the orphaned first `<summary>` block. `NanobotSystem.Scanning.cs:184`.
