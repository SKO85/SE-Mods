# BUG-080: GridOwnershipCacheHandler swallows all exceptions silently
## Status: Fixed
## Severity: Medium
## Version: v2.5.0
## Found In: Code review round 6 — Handlers/GridOwnershipCacheHandler.cs (multiple locations)
## Description
Four `catch { }` blocks in `GetRelationBetweenGridAndPlayer`, `GetRelationBetweenGridAndPlayerInternal`, `CheckOwners`, and `RefreshExpiredEntries` silently swallow all exceptions. Ownership lookup failures return `NoOwnership` with no indication of error. This can cause BaRs to refuse to work on valid friendly targets or incorrectly process enemy blocks, with no diagnostic trail.
## Root Cause
Defensive exception handling with no logging.
## Fix
- `GridOwnershipCacheHandler.cs:87-89` — Changed to `catch (Exception ex)` with `Logging.Instance.Error()` in `GetRelationBetweenGridAndPlayer`.
- `GridOwnershipCacheHandler.cs:117-119` — Same in `GetRelationBetweenGridAndPlayerInternal`.
- `GridOwnershipCacheHandler.cs:153-154` — Same in `CheckOwners`.
- `GridOwnershipCacheHandler.cs:226-228` — Same in `RefreshExpiredEntries`.
- Added `using SKONanobotBuildAndRepairSystem.Utils;` for `Logging` access.
