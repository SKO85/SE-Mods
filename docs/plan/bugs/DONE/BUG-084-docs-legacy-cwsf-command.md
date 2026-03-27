# BUG-084: Documentation references removed `-cwsf` command
## Status: Fixed
## Severity: Low
## Version: v2.5.0
## Found In: Documentation — FAQ and Config pages
## Description
The legacy `/nanobars -cwsf` chat command was removed from the codebase but still referenced in the documentation (FAQ and Config pages). This causes confusion for users who try to use the command and get no response.
## Root Cause
Documentation was not updated when the `-cwsf` command was replaced by `/nanobars config create` and `/nanobars config save`.
## Fix
- `docs/pages/Build-and-Repair-System/FAQ/index.md`: Replaced two references to `-cwsf` with `/nanobars config create`
- `docs/pages/Build-and-Repair-System/Config/index.md`: Replaced `-cwsf` with `/nanobars config create`
- Release notes left unchanged (historical records)
