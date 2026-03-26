# FEAT-011: Add Cryo, Connector, Sorter as Source targets and Refinery as Push target
## Status: Done
## Priority: Medium
## Version: v2.5.0
## Summary
Expand the set of block types recognized as inventory source and push targets.
## Motivation
Player request (Discord). Cryo Chambers contain suits/items players want BaR to pull from. Refineries are natural destinations for pushing collected ore/components.
## Design
| Block Type    | Source | Push Target |
|---------------|--------|-------------|
| Cryo Chamber  | Yes (new) | No       |
| Connector     | Yes (already) | No   |
| Sorter        | Yes (already) | No   |
| Refinery      | No     | Yes (new)   |

**`InventoryHelper.cs`**: Added `IMyCryoChamber` and `IMyRefinery` to the valid block type filter.

**`NanobotSystem.Scanning.cs`**:
1. Added `IMyRefinery` alongside `IMyCargoContainer` in the push target selection loop.
2. Added `RemoveAll` to strip refinery inventories from the source list after push target selection (refineries are push-only).
## Files Affected
- `InventoryHelper.cs`
- `NanobotSystem.Scanning.cs`
## Testing
- Place BaR near cryo chambers and refineries
- Verify cryo shows as source, refinery shows as push target
