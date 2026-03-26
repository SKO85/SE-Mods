# BUG-071: PullComponents allocates new List per call + LINQ .Count() on dictionary
## Status: Fixed
## Severity: Low
## Version: v2.5.0
## Found In: Code review round 5 — NanobotSystem.Inventory.cs:253, UtilsInventory.cs:286
## Description
Two minor performance issues:
1. `PullComponents` allocates `new List<MyInventoryItem>()` on every call (line 253). This method is called per-component per weld tick, generating GC pressure. A separate field-level list (`_TempPullInventoryItems`) cannot share `_TempInventoryItems` because `ServerPickFromWelder` uses it within the same call chain.
2. `UtilsInventory.GetMissingComponents` uses `.Count()` LINQ extension (line 286) instead of `.Count` property on a `Dictionary`, causing an unnecessary delegate allocation.
## Root Cause
Local allocation instead of pooled field; LINQ extension instead of property access.
## Fix
- `NanobotSystem.cs:110` — Added `_TempPullInventoryItems` field.
- `NanobotSystem.Inventory.cs:253` — Replaced `new List<MyInventoryItem>()` with `_TempPullInventoryItems.Clear()`.
- `NanobotSystem.Init.cs:198` — Added cleanup in `Close()`.
- `UtilsInventory.cs:286` — Changed `.Count()` to `.Count`.
