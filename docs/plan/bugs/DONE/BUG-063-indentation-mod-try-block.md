# BUG-063: Indentation mismatch in Mod.RebuildSourcesAndTargetsTimer try block
## Status: Fixed
## Severity: Low
## Version: v2.5.0
## Found In: Review / Mod.cs
## Description
The BUG-053 safe zone refresh code and the existing `ScanClusterCoordinator.RebuildClusters()` call inside the `try` block in `RebuildSourcesAndTargetsTimer` used 4-level indentation instead of the expected 5 levels. This made the code appear to be outside the try block, reducing readability.
## Fix
Re-indented the try block body to use consistent 5-level indentation. `Mod.cs:490-510`.
