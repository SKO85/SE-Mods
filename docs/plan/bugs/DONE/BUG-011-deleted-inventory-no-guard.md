# BUG-011: No Guard for Deleted Inventory Owners in Push/Pull
## Status: Fixed
## Severity: Medium
## Version: v2.5.0
## Found In: Utils/UtilsInventory.cs, NanobotSystem.Inventory.cs
## Description
When a cargo container (or other inventory block) is deleted while the BaR still holds a reference to its inventory in `_PossibleSources` or `_PossiblePushTargets`, subsequent push/pull operations attempt to interact with a dead entity's inventory. This could cause exceptions or wasted work until the next source scan refreshes the lists.
## Steps to Reproduce
1. Place a BaR and several cargo containers
2. Let the BaR start welding (pulling components from cargo)
3. Grind down or delete a cargo container that the BaR is using as a source/push target
4. Observe potential errors or failed transfers for up to 30 seconds
## Root Cause
`_PossibleSources` and `_PossiblePushTargets` hold `IMyInventory` references that go stale when the owner entity is destroyed. No validity check was performed before calling `FindItem`, `CanTransferItemTo`, `TransferItemTo`, etc.
## Fix
- `TryTransferItemTo` (UtilsInventory.cs): Added `destInventory.Owner` null/`MarkedForClose` check at the start of each destination loop iteration
- `PullComponents` (NanobotSystem.Inventory.cs): Added `srcInventory.Owner` null/`MarkedForClose` check before accessing each source inventory
