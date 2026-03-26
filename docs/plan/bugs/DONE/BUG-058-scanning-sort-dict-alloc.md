# BUG-058: PreSortClusterCandidates allocates distance dictionaries per scan cycle
## Status: Open
## Severity: Medium
## Version: v2.5.0
## Found In: Code review — NanobotSystem.Scanning.cs:926,964
## Description
`PreSortClusterCandidates()` allocates two `new Dictionary<IMySlimBlock, double>()` — one for grind candidates (line 926) and one for weld candidates (line 964) — every scan cycle.

These run on background threads (~every 2s per cluster coordinator), but with many BaRs the allocation pressure adds up, especially since the dictionaries are sized to candidate count and immediately discarded after sorting.

## Root Cause
Distance dictionaries are created as local variables inside the method instead of being reused across calls.

## Fix
Add instance-level `Dictionary<IMySlimBlock, double>` fields to `NanobotSystem` (or use `[ThreadStatic]` static fields since this runs on background threads). Clear and reuse instead of allocating new dictionaries each scan. File: `NanobotSystem.Scanning.cs`.
