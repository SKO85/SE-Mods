# BUG-023: UtilsSorting silent catch{} swallows all exceptions
## Severity: Medium
## Version: v2.5.0
## Status: TODO

## File
`Utils/UtilsSorting.cs:108`

## Description
The outer `catch { }` in `SortWithPriorityAndDistance` silently swallows ALL exceptions. If any exception occurs during sorting (e.g., `NullReferenceException` from a deleted entity, `InvalidOperationException` from `List.Sort` when the comparison is inconsistent), it is silently discarded. The caller proceeds with a partially sorted or inconsistent list.

This makes bugs nearly impossible to diagnose — the BaR may weld/grind in wrong order or skip targets, with no log entry to explain why.

## Fix
Log the exception via `Logging.Instance.Error()` inside the catch block.
