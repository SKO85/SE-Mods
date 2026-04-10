# BUG-088: Distant cluster members starved of targets on large single-grid bases

## Status: Fixed
## Severity: High
## Version: v2.5.2
## Found In: `NanobotSystem.Scanning.cs` — `AsyncClusterScan`, `SortAndCapGridCandidates`, `PreSortClusterCandidates`

## Description

When several BaR blocks are placed on a single large grid (e.g. a big asteroid base) with their working areas ~150m apart, distant BaRs stop welding/grinding anything. The companion PB script's LCD shows `NULL` as the welding target — a symptom, not a sync bug: `State.CurrentWeldingBlock` is genuinely null because the BaR has no candidates.

Distinct from BUG-027 (which was about a single large grid consuming the global scan cap and starving *other* grids). This bug starves members on the **same grid** because the shared-scan geometry is centered on the coordinator only.

## Steps to Reproduce

1. Build a single large grid / asteroid base with ≥ 5000 blocks and weldable or grindable blocks across multiple zones ≥ 150m apart.
2. Place 5+ BaR blocks so each BaR's working area covers a different zone.
3. Give all BaRs matching ownership / settings / work mode so they share one `ScanCluster`.
4. Damage (or project) blocks in each zone.
5. Observe: only the BaR closest to the elected coordinator welds. Others sit idle and the companion script reports `CurrentWelding: NULL` on them.

## Root Cause

Same-grid BaRs with identical settings form one `ScanCluster`. `ComputeClusterKey` (`Cluster/ScanClusterCoordinator.cs:139`) has no spatial component — every BaR on the grid joins regardless of distance. Only the elected coordinator runs `AsyncClusterScan`; other members read the shared result.

Three coordinator-centric biases in the scan starve distant members:

1. **Collection cap** (`NanobotSystem.Scanning.cs:802-806`): `capMultiplier = Math.Max(memberCount < 4 ? memberCount : 4, 4)` → global cap ≈ 1024 candidates regardless of member count. On a 5000-block grid, `AsyncAddBlocksOfGrid` stops early via `ShouldStopScan` (line 463) after 1024 candidates iterated in arbitrary cache order. Blocks in far corners may never be collected.

2. **Per-grid sort + truncation** (`SortAndCapGridCandidates`, line ~1206, called from 561/566): When one grid contributes more than `MaxPossibleGrindTargets/WeldTargets = 256` candidates, the comparator sorts by distance from `areaBox.Center` — the **coordinator's** welder area box — and keeps the closest 256. `PreSortClusterCandidates` (line ~941, called from ~896) does the same using `coordCenter = areaOrientedBox.Center`.

3. **Per-member range filter** (`ApplyClusterResultToSelf`, line ~1258): each member keeps only blocks inside its own area box. Distant members see zero blocks in range because the shared result is geometrically clustered near the coordinator.

Result: `_TempPossibleWeldTargets` / `_TempPossibleGrindTargets` end up empty on distant members → `ServerTryWelding` / `ServerTryGrinding` find nothing → `CurrentWeldingBlock` stays null → nothing gets built, and the companion script prints `NULL` via the fallback in `SKO-Nanobot-BuildAndRepair-System-Script/Script.cs:2047`.

## Fix

Make the collection and truncation **member-aware** instead of coordinator-centric. No clustering-semantics change, no new thread sync.

### Fix 1 — Snapshot cluster member area centers at scan start (`NanobotSystem.cs`, `NanobotSystem.Scanning.cs`)

Added instance field `_ClusterMemberAreaCenters` (`NanobotSystem.cs:92`). Populated in `AsyncClusterScan` (`NanobotSystem.Scanning.cs:834-857`) for multi-member clusters by building each member's oriented bounding box (`_Welder.WorldMatrix` + `Settings.CorrectedAreaOffset` + `CorrectedAreaBoundingBox`) and taking its `Center`. Cleared in the `finally` block to avoid stale references across cycles. Solo clusters skip the snapshot entirely — legacy path stays byte-identical.

### Fix 2 — Scale collection cap with cluster size (`NanobotSystem.Scanning.cs:802-810`)

Changed `capMultiplier` from `Math.Max(memberCount < 4 ? memberCount : 4, 4)` to `Math.Max(4, Math.Min(memberCount * 4, 16))`. Solo and small clusters unchanged; 4-member clusters collect 4x more candidates (4096 total), 5+ clusters capped at 16x. Combined with Fix 3, the shared list now contains blocks for every member's locale.

### Fix 3 — Sort by min-distance-to-any-member (`NanobotSystem.Scanning.cs`)

Added private helper `MinSquaredDistanceToClusterMembers` (line ~1211). Returns the minimum squared distance from a block position to any snapshotted member center; falls back to `fallbackCenter` when the snapshot is empty.

- `SortAndCapGridCandidates` (line ~1234): comparator now uses min-to-any-member distance for multi-member clusters. Priority / near-first / smallest-grid branches unchanged. Solo path (`useMemberAware == false`) falls back to coordinator-center distance byte-identically.
- `PreSortClusterCandidates` (line ~982, ~1021): grind and weld distance dictionaries populated via the helper. `coordCenter` parameter retained for the solo fallback inside the helper.

### Fix 4 — Profiling coverage for cluster-size attribution (`NanobotSystem.Scanning.cs`)

- `AsyncAddBlocksOfGrid` log (line ~590-596) now includes `clusterMembers`, so per-grid iteration cost and `SortAndCapGridCandidates` rolled-up cost can be correlated with cluster size.
- `PreSortClusterCandidates` log (line ~1067-1074) now includes `clusterMembers`, so the O(members × compares) sort cost can be attributed.

## Risks / Notes

- `_ClusterMemberAreaCenters` is written and read from the same background scan thread (no main-thread access). Clearing in `finally` prevents stale data across cycles.
- `result.PreSorted = true` path: members skip their local sort and see candidates ordered by cluster-min-member-distance. Acceptable because `ApplyClusterResultToSelf` range-filters first (lines ~1264/1278) and the surviving list is short (≤256) so approximate order is fine downstream.
- Member position freshness: snapshot is captured once per scan; scans run every ~2s. `ApplyClusterResultToSelf` still does an authoritative `IsInRange` against each member's live box, so a one-cycle stale snapshot can only mis-order, never miss targets.
- Allocation: `List<Vector3D>` allocated lazily, reused via clear+refill — no per-scan GC pressure after the first cycle.
