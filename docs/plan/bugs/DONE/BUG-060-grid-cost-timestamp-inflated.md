# BUG-060: Grid cost timestamp inflated by StopAndLog overhead
## Status: Fixed
## Severity: Low
## Version: v2.5.0
## Found In: Review / NanobotSystem.Scanning.cs
## Description
After `MethodProfiler.StopAndLog("AsyncAddBlocksOfGrid", ...)` was called, the code re-read `Stopwatch.GetTimestamp()` to compute `gridMs` for `ReportGridCost`. This included the overhead of StopAndLog itself (lock acquisition, string formatting, file I/O), systematically inflating per-grid cost values in the profile summary.
## Root Cause
The end timestamp was captured after StopAndLog instead of before it.
## Fix
Captured `endTs = Stopwatch.GetTimestamp()` before calling StopAndLog and used it for the grid cost calculation. `NanobotSystem.Scanning.cs:582`.
