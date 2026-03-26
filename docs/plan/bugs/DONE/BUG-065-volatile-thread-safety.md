# BUG-065: Missing volatile on cross-thread flags
## Status: Fixed
## Severity: High
## Version: v2.5.0
## Found In: Code review round 5 — NanobotSystem.cs, ScanCluster.cs
## Description
Several boolean and reference fields are read/written across main and background threads without `volatile`, risking stale reads due to CPU cache coherency:
- `_AsyncUpdateSourcesAndTargetsRunning` — guards scan re-entry
- `_InitialScanCompleted` — gates first operation tick
- `_PushTargetsFull` — blocks push attempts
- `AssignedCluster` — cluster reference read by background scan
- `_sharedResult` in ScanCluster — published by coordinator, read by members
## Root Cause
Fields lacked `volatile` modifier, so cross-thread writes may not be visible to readers on other cores.
## Fix
Added `volatile` to all five fields:
- `NanobotSystem.cs:67-69` — `_AsyncUpdateSourcesAndTargetsRunning`, `_InitialScanCompleted`, `_PushTargetsFull`
- `NanobotSystem.cs:211` — `AssignedCluster`
- `ScanCluster.cs:20` — `_sharedResult`
