# BUG-020: Floating objects collected outside working area
## Status: Fixed
## Severity: Medium
## Version: v2.5.0
## Found In: `NanobotSystem.Scanning.cs` — ApplyClusterResultToSelf
## Description
BaR collects floating objects (items, dead characters, inventory bags) that are outside its configured working area. The collection range effectively extends beyond the oriented bounding box (OBB) to the full axis-aligned bounding box (AABB) used by the cluster scan, which is always equal to or larger than the actual working area.

## Root Cause
In `ApplyClusterResultToSelf`, weld and grind candidates are filtered with `IsInRange(ref areaOrientedBox, ...)` to verify they intersect the BaR's OBB before being added to the target list. Floating candidates skip this check entirely — they only compute distance from the OBB center and are added unconditionally.

The cluster scan finds entities via `SharedEntityCache.GetEntitiesInBox(ref areaBoundingBox)` which uses an AABB. This AABB is always >= the actual OBB working area (larger at rotated orientations, and much larger in multi-BaR clusters). Any floating object inside the AABB but outside the OBB gets collected.

```
NanobotSystem.Scanning.cs:875-884 (before fix):
    var distance = (areaOrientedBox.Center - candidate.WorldPosition).Length();
    _TempPossibleFloatingTargets.Add(new TargetEntityData(candidate.Entity, distance));
    // ^ No containment check — always added
```

## Fix
`NanobotSystem.Scanning.cs` — `ApplyClusterResultToSelf`: Added point-in-OBB containment check for floating candidates. The candidate's world position is transformed into the OBB's local coordinate space (inverse rotation via transposed rotation matrix), then checked against the half-extents. Objects outside the working area are skipped.

```csharp
var invRotation = MatrixD.Transpose(MatrixD.CreateFromQuaternion(areaOrientedBox.Orientation));
var he = areaOrientedBox.HalfExtent;
// ...
var localPos = Vector3D.TransformNormal(candidate.WorldPosition - areaOrientedBox.Center, invRotation);
if (Math.Abs(localPos.X) > he.X || Math.Abs(localPos.Y) > he.Y || Math.Abs(localPos.Z) > he.Z)
    continue;
```
