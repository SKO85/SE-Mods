# BUG-030: Assignment gap during projected-to-physical block transition
## Status: Fixed
## Severity: Medium
## Version: v2.5.0
## Found In: `NanobotSystem.Welding.cs` — `ServerDoWeld`

## Summary
When a BaR places a projected block, `BlockSystemAssigningHandler` keys assignments by `"GridEntityId:Position"`. The projected block's key (`ProjectedGridId:Pos`) differs from the physical block's key (`RealGridId:Pos`). During the BaR's stagger wait (~0.5s), another BaR could scan, find the unassigned physical block, and steal it.

## Fix
After `ServerDoWeld` places a projected block and obtains the physical block reference, immediately call `target.AssignToSystem(_Welder.EntityId)` on the physical block to close the gap.

## Files Changed
- `NanobotSystem.Welding.cs`
