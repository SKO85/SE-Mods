# BUG-056: Reset All Settings does not restore priority list order
## Status: Fixed
## Severity: Low
## Version: v2.5.0
## Found In: NanobotSystem.cs ResetSettings()
## Description
The "Reset All Settings" button in the terminal enables all priority items but does not restore the default sort order. If a player reorders the weld, grind, or collect priority lists and then presses reset, the custom ordering is preserved instead of reverting to the default enum order.
## Root Cause
`ResetSettings()` called `SetAllEnabled(true)` which only flips the `Enabled` flag on each entry without changing list positions. No method existed to restore the default key-based ordering.
## Fix
Added `ResetToDefaultOrder()` to `PriorityHandling` (`PriorityHandling.cs`) which sorts entries by `PrioItem.Key` (the original enum value order). `ResetSettings()` in `NanobotSystem.cs` now calls `ResetToDefaultOrder()` on all three priority lists before `SetAllEnabled(true)`.
