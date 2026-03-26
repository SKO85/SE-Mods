# BUG-025: Logging constructor uses _ModName before assignment
## Severity: Low
## Version: v2.5.0
## Status: TODO

## File
`Utils/Logging.cs:113-114`

## Description
The constructor writes `_ModName + " Create Log instance..."` on line 113, but `_ModName` is not assigned until line 114. At line 113, `_ModName` is still null, so the log message reads `" Create Log instance Utils=..."` instead of `"SKONanobotBuildAndRepairSystem Create Log instance Utils=..."`.

## Fix
Move the log write after `_ModName` assignment, or use the `modName` parameter directly.
