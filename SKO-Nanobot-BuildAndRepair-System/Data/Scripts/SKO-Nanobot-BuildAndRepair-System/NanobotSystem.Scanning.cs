using Sandbox.Definitions;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using SKONanobotBuildAndRepairSystem.Cluster;
using SKONanobotBuildAndRepairSystem.Handlers;
using SKONanobotBuildAndRepairSystem.Helpers;
using SKONanobotBuildAndRepairSystem.Caches;
using SKONanobotBuildAndRepairSystem.Models;
using SKONanobotBuildAndRepairSystem.Profiling;
using SKONanobotBuildAndRepairSystem.Utils;
using System;
using System.Collections.Generic;
using VRage.Game;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRageMath;
using static SKONanobotBuildAndRepairSystem.Utils.UtilsInventory;
using IMyShipWelder = Sandbox.ModAPI.IMyShipWelder;
using IMyTerminalBlock = Sandbox.ModAPI.IMyTerminalBlock;

namespace SKONanobotBuildAndRepairSystem
{
    public partial class NanobotSystem
    {
        public void UpdateSourcesAndTargetsTimer()
        {
            // Block is off — skip scanning. Reset initial-scan flag so a scan
            // triggers immediately when the block is re-enabled.
            if (!_Welder.Enabled || !_Welder.IsFunctional)
            {
                _InitialScanCompleted = false;
                return;
            }

            var playTime = MyAPIGateway.Session.ElapsedPlayTime;
            var updateTargets = playTime.Subtract(_LastTargetsUpdate) >= Mod.Settings.TargetsUpdateInterval;
            var updateSources = updateTargets && playTime.Subtract(_LastSourceUpdate) >= Mod.Settings.SourcesUpdateInterval;
            if (updateTargets)
            {
                // Capture cluster reference (atomic read)
                var cluster = AssignedCluster;
                if (cluster == null)
                {
                    // Not yet assigned to a cluster (first tick or system excluded from clustering) — skip this cycle
                    return;
                }

                if (cluster.IsCoordinator(this))
                {
                    StartAsyncClusterScan(cluster, updateSources);
                }
                else
                {
                    StartAsyncApplyClusterResults(cluster, updateSources);
                }
            }
        }

        /// <summary>
        /// Scans for inventory sources on the BaR's own grid and all connected grids.
        /// Uses raw (unsorted) block lists — no expensive sort needed for source scanning.
        /// Traverses mechanical connections and connectors via BFS.
        /// </summary>
        private void AsyncScanForSources(List<IMyInventory> possibleSources)
        {
            var profilerTs = MethodProfiler.Start();
            var visited = new HashSet<long>();
            var toVisit = new Queue<IMyCubeGrid>();
            toVisit.Enqueue(_Welder.CubeGrid);

            try
            {
                while (toVisit.Count > 0)
                {
                    var grid = toVisit.Dequeue();
                    if (grid == null || !visited.Add(grid.EntityId)) continue;

                    // Use shared raw block cache — no per-BaR sort needed for sources.
                    var blocks = SharedGridBlockCache.GetBlocks(grid);

                    foreach (var slimBlock in blocks)
                    {
                        var fatBlock = slimBlock.FatBlock;
                        if (fatBlock == null) continue;

                        // Follow mechanical connections and connectors to connected grids.
                        var mechanical = fatBlock as Sandbox.ModAPI.IMyMechanicalConnectionBlock;
                        if (mechanical != null)
                        {
                            if (mechanical.TopGrid != null)
                                toVisit.Enqueue(mechanical.TopGrid);
                            continue;
                        }

                        var attachable = fatBlock as Sandbox.ModAPI.IMyAttachableTopBlock;
                        if (attachable != null)
                        {
                            if (attachable.Base != null && attachable.Base.CubeGrid != null)
                                toVisit.Enqueue(attachable.Base.CubeGrid);
                            continue;
                        }

                        var connector = fatBlock as Sandbox.ModAPI.IMyShipConnector;
                        if (connector != null)
                        {
                            if (connector.Status == Sandbox.ModAPI.Ingame.MyShipConnectorStatus.Connected && connector.OtherConnector != null)
                                toVisit.Enqueue(connector.OtherConnector.CubeGrid);
                        }

                        // Check for source-eligible blocks.
                        var terminalBlock = fatBlock as IMyTerminalBlock;
                        if (terminalBlock != null && terminalBlock.EntityId != _Welder.EntityId && terminalBlock.IsFunctional)
                        {
                            var relation = terminalBlock.GetUserRelationToOwner(_Welder.OwnerId);
                            if (MyRelationsBetweenPlayerAndBlockExtensions.IsFriendly(relation))
                            {
                                try
                                {
                                    terminalBlock.AddIfConnectedToInventory(_Welder, possibleSources);
                                }
                                catch (Exception ex)
                                {
                                    Logging.Instance.Write(Logging.Level.Event, "BuildAndRepairSystemBlock {0}: AsyncScanForSources exception: {1}", Logging.BlockName(_Welder, Logging.BlockNameOptions.None), ex);
                                }
                            }
                        }
                    }
                }
            }
            finally
            {
                if (profilerTs != 0L)
                {
                    var _visitedCount = visited.Count;
                    var _sourceCount = possibleSources.Count;
                    MethodProfiler.StopAndLog("AsyncScanForSources", profilerTs, () =>
                        string.Format("entityId={0};gridsVisited={1};sourcesFound={2}",
                            _Welder.EntityId, _visitedCount, _sourceCount));
                }
            }
        }

        private List<IMySlimBlock> GetBlocksFromCache(IMyCubeGrid grid)
        {
            // Returns a fresh unsorted block list. Sorting is handled later by
            // PreSortClusterCandidates on just the target candidates, which is
            // far cheaper than sorting the entire grid (O(candidates) vs O(all blocks)).
            return SharedGridBlockCache.GetBlocks(grid);
        }

        /// <summary>
        /// Checks if the given slim block is a target (weld or grind). Supports skipping range checks
        /// and variable max target counts for cluster scanning.
        /// </summary>
        private void AsyncAddBlockIfTarget(ref MyOrientedBoundingBoxD areaBox, bool useIgnoreColor, ref uint ignoreColor, bool useGrindColor, ref uint grindColor, AutoGrindRelation autoGrindRelation, AutoGrindOptions autoGrindOptions, IMySlimBlock block, List<ClusterTargetCandidate> clusterWeldTargets, List<ClusterTargetCandidate> clusterGrindTargets, int maxWeld, int maxGrind, bool skipRangeCheck)
        {
            try
            {
                var added = false;

                if (clusterGrindTargets != null && (useGrindColor || autoGrindRelation != 0))
                {
                    if (State.SafeZoneAllowsGrinding)
                    {
                        if (clusterGrindTargets.Count < maxGrind)
                        {
                            added = AsyncAddBlockIfGrindTarget(ref areaBox, useGrindColor, ref grindColor, autoGrindRelation, autoGrindOptions, block, clusterGrindTargets, maxGrind, skipRangeCheck);
                        }
                    }
                }

                if (clusterWeldTargets != null && !added)
                {
                    if (clusterWeldTargets.Count < maxWeld)
                    {
                        AsyncAddBlockIfWeldTarget(ref areaBox, useIgnoreColor, ref ignoreColor, useGrindColor, ref grindColor, block, clusterWeldTargets, maxWeld, skipRangeCheck);
                    }
                }
            }
            catch (Exception ex)
            {
                Logging.Instance.Error("BuildAndRepairSystemBlock {0}: AsyncAddBlockIfTarget exception: {1}", Logging.BlockName(_Welder, Logging.BlockNameOptions.None), ex);
                throw;
            }
        }

        /// <summary>
        /// BUG-053: SE applies safe zone building restrictions at the grid level.
        /// If the projector's physical grid intersects a safe zone that blocks building,
        /// proj.Build() will fail for ALL projected blocks on that grid.
        /// </summary>
        private bool IsProjectorGridBuildBlocked(IMyProjector projector)
        {
            if (!Mod.Settings.SafeZoneCheckEnabled || SafeZoneHandler.Zones.Count == 0)
                return false;

            var projectorGrid = projector.CubeGrid;
            if (projectorGrid == null)
                return false;

            var zone = SafeZoneHandler.GetIntersectingSafeZone(projectorGrid);
            if (zone != null && zone.Enabled)
            {
                var buildAllowed = zone.IsActionAllowed(
                    SafeZoneHandler.CastProhibit(MySessionComponentSafeZones.AllowedActions, SafeZoneHandler.SafeZoneAction.BuildingProjections), 0L);
                if (!buildAllowed)
                    return true;
            }

            return false;
        }

        private bool AsyncAddBlockIfWeldTarget(ref MyOrientedBoundingBoxD areaBox, bool useIgnoreColor, ref uint ignoreColor, bool useGrindColor, ref uint grindColor, IMySlimBlock block, List<ClusterTargetCandidate> clusterWeldTargets, int maxTargets, bool skipRangeCheck)
        {
            if (clusterWeldTargets != null && clusterWeldTargets.Count >= maxTargets)
            {
                return false;
            }

            var colorMask = block.GetColorMask();
            IMyProjector projector;
            if (block.IsProjected(out projector))
            {
                if (!State.SafeZoneAllowsBuildingProjections)
                    return false;

                // BUG-053: SE applies safe zone building restrictions at the grid level.
                // If the projector's physical grid intersects a safe zone that blocks
                // building, proj.Build() fails for ALL projected blocks on that grid.
                if (IsProjectorGridBuildBlocked(projector))
                    return false;

                if (((Settings.Flags & SyncBlockSettings.Settings.AllowBuild) != 0) &&
                   (!useGrindColor || !IsColorNearlyEquals(grindColor, colorMask)) &&
                   DlcCheckHelper.IsBlockDlcAvailableForOwner(block, _Welder.OwnerId) &&
                   BlockWeldPriority.GetEnabled(block) &&
                   IsRelationAllowed4Welding(projector.SlimBlock) &&
                   block.CanBuild(false))
                {
                    double distance;
                    if (skipRangeCheck || block.IsInRange(ref areaBox, out distance))
                    {
                        if (clusterWeldTargets.Count < maxTargets)
                        {
                            clusterWeldTargets.Add(new ClusterTargetCandidate(block, TargetBlockData.AttributeFlags.Projected));
                            return true;
                        }
                    }
                }
            }
            else
            {
                if (!State.SafeZoneAllowsWelding)
                    return false;

                if ((!useIgnoreColor || !IsColorNearlyEquals(ignoreColor, colorMask)) && (!useGrindColor || !IsColorNearlyEquals(grindColor, colorMask)) &&
                   BlockWeldPriority.GetEnabled(block) &&
                   IsRelationAllowed4Welding(block) &&
                   block.NeedRepair(Settings.WeldOptions))
                {
                    double distance;
                    if (skipRangeCheck || block.IsInRange(ref areaBox, out distance))
                    {
                        if (clusterWeldTargets.Count < maxTargets)
                        {
                            clusterWeldTargets.Add(new ClusterTargetCandidate(block, 0));
                            return true;
                        }
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// Check if the given slim block is a grind target. When skipRangeCheck is true, skips IsInRange.
        /// </summary>
        private bool AsyncAddBlockIfGrindTarget(ref MyOrientedBoundingBoxD areaBox, bool useGrindColor, ref uint grindColor, AutoGrindRelation autoGrindRelation, AutoGrindOptions autoGrindOptions, IMySlimBlock block, List<ClusterTargetCandidate> clusterGrindTargets, int maxTargets, bool skipRangeCheck)
        {
            if (clusterGrindTargets != null && clusterGrindTargets.Count >= maxTargets)
            {
                return false;
            }

            // Scenario/destructible and grid immunity checks are pre-computed per-grid
            // in AsyncAddBlocksOfGrid (grindTargetsForGrid=null skips grind entirely).

            if (block.IsProjected())
                return false;

            var autoGrind = autoGrindRelation != 0 && BlockGrindPriority.GetEnabled(block);
            if (autoGrind)
            {
                if (block.CubeGrid.EntityId != Welder.CubeGrid.EntityId && State.IsShielded)
                {
                    return false;
                }

                var relation = block.GetUserRelationToOwner(_Welder.OwnerId);
                autoGrind =
                   (relation == MyRelationsBetweenPlayerAndBlock.NoOwnership && ((autoGrindRelation & AutoGrindRelation.NoOwnership) != 0)) ||
                   (relation == MyRelationsBetweenPlayerAndBlock.Enemies && ((autoGrindRelation & AutoGrindRelation.Enemies) != 0)) ||
                   (relation == MyRelationsBetweenPlayerAndBlock.Neutral && ((autoGrindRelation & AutoGrindRelation.Neutral) != 0));
            }

            if (autoGrind && ((autoGrindOptions & (AutoGrindOptions.DisableOnly | AutoGrindOptions.HackOnly)) != 0))
            {
                var criticalIntegrityRatio = ((MyCubeBlockDefinition)block.BlockDefinition).CriticalIntegrityRatio;
                var ownershipIntegrityRatio = ((MyCubeBlockDefinition)block.BlockDefinition).OwnershipIntegrityRatio > 0 ? ((MyCubeBlockDefinition)block.BlockDefinition).OwnershipIntegrityRatio : criticalIntegrityRatio;
                var integrityRatio = block.Integrity / block.MaxIntegrity;

                if (autoGrind && ((autoGrindOptions & AutoGrindOptions.DisableOnly) != 0))
                {
                    autoGrind = block.FatBlock != null && integrityRatio > criticalIntegrityRatio;
                }

                if (autoGrind && ((autoGrindOptions & AutoGrindOptions.HackOnly) != 0))
                {
                    autoGrind = block.FatBlock != null && integrityRatio > ownershipIntegrityRatio;
                }
            }

            if (autoGrind || (useGrindColor && IsColorNearlyEquals(grindColor, block.GetColorMask()) && BlockGrindPriority.GetEnabled(block)))
            {
                double distance;
                if (skipRangeCheck || block.IsInRange(ref areaBox, out distance))
                {
                    if (SafeZoneHandler.IsProtectedFromGrinding(block, Welder))
                    {
                        return false;
                    }

                    if (IsShieldProtected(block))
                    {
                        return false;
                    }

                    if (clusterGrindTargets.Count < maxTargets)
                    {
                        clusterGrindTargets.Add(new ClusterTargetCandidate(block, autoGrind ? TargetBlockData.AttributeFlags.Autogrind : 0));
                        return true;
                    }
                }
            }
            return false;
        }

        private bool ShouldStopScan(List<ClusterTargetCandidate> weldCandidates, List<ClusterTargetCandidate> grindCandidates, List<ClusterFloatingCandidate> floatingCandidates, int maxWeld, int maxGrind, int maxFloat)
        {
            var weldFull = weldCandidates == null || weldCandidates.Count >= maxWeld;
            var grindFull = grindCandidates == null || grindCandidates.Count >= maxGrind;
            var floatingFull = floatingCandidates == null || floatingCandidates.Count >= maxFloat;
            return weldFull && grindFull && floatingFull;
        }

        /// <summary>
        /// Scans a grid for target blocks, writing to cluster candidate lists.
        /// </summary>
        private void AsyncAddBlocksOfGrid(ref MyOrientedBoundingBoxD areaBox, bool useIgnoreColor, ref uint ignoreColor, bool useGrindColor, ref uint grindColor, AutoGrindRelation autoGrindRelation, AutoGrindOptions autoGrindOptions, IMyCubeGrid cubeGrid, List<IMyCubeGrid> grids, List<ClusterTargetCandidate> clusterWeldTargets, List<ClusterTargetCandidate> clusterGrindTargets, int maxWeld, int maxGrind, bool skipRangeCheck)
        {
            var profilerTs = MethodProfiler.Start();
            if (!State.Ready) return;
            if (grids.Contains(cubeGrid)) return;

            grids.Add(cubeGrid);

            var gridEntityId = cubeGrid.EntityId;
            var playTime = MyAPIGateway.Session.ElapsedPlayTime;

            // Check if this grid was recently scanned and found empty.
            // If so, skip expensive target checks and only traverse connections
            // so newly docked ships are still discovered.
            var emptyDelay = Mod.Settings.EmptyGridRescanDelaySeconds;
            if (emptyDelay > 0)
            {
                TimeSpan emptyTime;
                if (_EmptyGridCache.TryGetValue(gridEntityId, out emptyTime)
                    && playTime.Subtract(emptyTime).TotalSeconds < emptyDelay)
                {
                    var rawBlocks = SharedGridBlockCache.GetBlocks(cubeGrid);
                    foreach (var slimBlock in rawBlocks)
                    {
                        var fatBlock = slimBlock.FatBlock;
                        if (fatBlock == null) continue;
                        if (ShouldStopScan(clusterWeldTargets, clusterGrindTargets, null, maxWeld, maxGrind, 0)) break;

                        var mechanical = fatBlock as Sandbox.ModAPI.IMyMechanicalConnectionBlock;
                        if (mechanical != null)
                        {
                            if (mechanical.TopGrid != null)
                                AsyncAddBlocksOfGrid(ref areaBox, useIgnoreColor, ref ignoreColor, useGrindColor, ref grindColor, autoGrindRelation, autoGrindOptions, mechanical.TopGrid, grids, clusterWeldTargets, clusterGrindTargets, maxWeld, maxGrind, skipRangeCheck);
                            continue;
                        }

                        var attachable = fatBlock as Sandbox.ModAPI.IMyAttachableTopBlock;
                        if (attachable != null)
                        {
                            if (attachable.Base != null && attachable.Base.CubeGrid != null)
                                AsyncAddBlocksOfGrid(ref areaBox, useIgnoreColor, ref ignoreColor, useGrindColor, ref grindColor, autoGrindRelation, autoGrindOptions, attachable.Base.CubeGrid, grids, clusterWeldTargets, clusterGrindTargets, maxWeld, maxGrind, skipRangeCheck);
                            continue;
                        }

                        var connector = fatBlock as Sandbox.ModAPI.IMyShipConnector;
                        if (connector != null)
                        {
                            if (connector.Status == Sandbox.ModAPI.Ingame.MyShipConnectorStatus.Connected && connector.OtherConnector != null)
                                AsyncAddBlocksOfGrid(ref areaBox, useIgnoreColor, ref ignoreColor, useGrindColor, ref grindColor, autoGrindRelation, autoGrindOptions, connector.OtherConnector.CubeGrid, grids, clusterWeldTargets, clusterGrindTargets, maxWeld, maxGrind, skipRangeCheck);
                            continue;
                        }
                    }

                    if (profilerTs != 0L)
                    {
                        MethodProfiler.StopAndLog("AsyncAddBlocksOfGrid", profilerTs, () =>
                            string.Format("entityId={0};gridId={1};skippedEmpty=True", _Welder.EntityId, gridEntityId));
                    }
                    return;
                }
            }

            var weldBefore = clusterWeldTargets != null ? clusterWeldTargets.Count : 0;
            var grindBefore = clusterGrindTargets != null ? clusterGrindTargets.Count : 0;

            // BUG-096 (Codex follow-up to BUG-094): enforce per-type cluster caps at grid entry.
            // BUG-094 disabled the per-block cap gate on AsyncAddBlockIfTarget (int.MaxValue) so
            // the per-grid sort sees every qualifying block on a grid — correct for sort quality,
            // but ShouldStopScan only fires when BOTH lists are full, so when one type never
            // fills (e.g. grind-only workload with weld still enabled, or vice versa), the other
            // type grew unbounded across scanned grids (each grid contributed up to 256 via the
            // per-grid sort, times N grids). PreSortClusterCandidates and ApplyClusterResultToSelf
            // then paid O(n log n) on that unbounded list.
            //
            // Fix: at grid entry, null the per-type target list if that type's cluster cap is
            // already hit. The per-block path for this grid becomes a no-op for the capped type,
            // but the other type still gets full-grid input so its per-grid sort stays accurate.
            // Cap-skipped flags suppress the empty-grid-cache update below so we don't evict a
            // grid that we literally didn't look at.
            var weldCapSkipped = false;
            var weldTargetsForGrid = clusterWeldTargets;
            if (weldTargetsForGrid != null && weldTargetsForGrid.Count >= maxWeld)
            {
                weldTargetsForGrid = null;
                weldCapSkipped = true;
            }

            var grindCapSkipped = false;
            var grindTargetsForGrid = clusterGrindTargets;
            if (grindTargetsForGrid != null && grindTargetsForGrid.Count >= maxGrind)
            {
                grindTargetsForGrid = null;
                grindCapSkipped = true;
            }

            // Pre-compute grid-level grind eligibility so per-block checks are skipped entirely
            // when the grid can't be ground (scenario mode, indestructible, immune).
            if (grindTargetsForGrid != null)
            {
                if ((MyAPIGateway.Session.SessionSettings.Scenario || MyAPIGateway.Session.SessionSettings.ScenarioEditMode)
                    && !MyAPIGateway.Session.SessionSettings.DestructibleBlocks)
                {
                    grindTargetsForGrid = null;
                }
                else
                {
                    var myCubeGrid = cubeGrid as MyCubeGrid;
                    if (myCubeGrid != null && (!myCubeGrid.DestructibleBlocks || myCubeGrid.Immune))
                    {
                        grindTargetsForGrid = null;
                    }
                }
            }

            // FEAT-040: Grid-level containment pre-check.
            // If the grid's world AABB fits entirely inside the working area OBB,
            // all blocks are guaranteed in range — skip expensive per-block OBB checks.
            // Connected/projected grids still use the original skipRangeCheck (they get their own check).
            var blockSkipRange = skipRangeCheck;
            if (!blockSkipRange)
            {
                var gridAABB = cubeGrid.WorldAABB;
                if (areaBox.Contains(ref gridAABB) == ContainmentType.Contains)
                {
                    blockSkipRange = true;
                }
            }

            var newBlocks = GetBlocksFromCache(cubeGrid);

            foreach (var slimBlock in newBlocks)
            {
                if (ShouldStopScan(clusterWeldTargets, clusterGrindTargets, null, maxWeld, maxGrind, 0)) break;

                // BUG-094: Pass int.MaxValue for the per-block count gates so EVERY qualifying
                // block on this grid enters the candidate list. The global cap (maxWeld/maxGrind)
                // must not short-circuit the per-block add here, because on grids larger than
                // the cap the iteration would keep whatever blocks happened to be first in the
                // grid-cache order — not the true top-N by priority/distance.
                // The per-grid budget (MaxPossibleWeldTargets / MaxPossibleGrindTargets) is
                // enforced after the loop via SortAndCapGridCandidates (below), which sorts
                // the qualifying candidates and keeps the user's preferred top-N (nearest,
                // farthest, smallest-grid-first, etc.). Regression introduced in v2.5.0 when
                // the full-grid pre-sort was removed but the cap-gate short-circuit was kept.
                AsyncAddBlockIfTarget(ref areaBox, useIgnoreColor, ref ignoreColor, useGrindColor, ref grindColor, autoGrindRelation, autoGrindOptions, slimBlock, weldTargetsForGrid, grindTargetsForGrid, int.MaxValue, int.MaxValue, blockSkipRange);

                var fatBlock = slimBlock.FatBlock;
                if (fatBlock == null) continue;

                var mechanicalConnectionBlock = fatBlock as Sandbox.ModAPI.IMyMechanicalConnectionBlock;
                if (mechanicalConnectionBlock != null)
                {
                    if (mechanicalConnectionBlock.TopGrid != null && !ShouldStopScan(clusterWeldTargets, clusterGrindTargets, null, maxWeld, maxGrind, 0))
                    {
                        AsyncAddBlocksOfGrid(ref areaBox, useIgnoreColor, ref ignoreColor, useGrindColor, ref grindColor, autoGrindRelation, autoGrindOptions, mechanicalConnectionBlock.TopGrid, grids, clusterWeldTargets, clusterGrindTargets, maxWeld, maxGrind, skipRangeCheck);
                    }
                    continue;
                }

                var attachableTopBlock = fatBlock as Sandbox.ModAPI.IMyAttachableTopBlock;
                if (attachableTopBlock != null)
                {
                    if (attachableTopBlock.Base != null && attachableTopBlock.Base.CubeGrid != null && !ShouldStopScan(clusterWeldTargets, clusterGrindTargets, null, maxWeld, maxGrind, 0))
                    {
                        AsyncAddBlocksOfGrid(ref areaBox, useIgnoreColor, ref ignoreColor, useGrindColor, ref grindColor, autoGrindRelation, autoGrindOptions, attachableTopBlock.Base.CubeGrid, grids, clusterWeldTargets, clusterGrindTargets, maxWeld, maxGrind, skipRangeCheck);
                    }
                    continue;
                }

                var connector = fatBlock as Sandbox.ModAPI.IMyShipConnector;
                if (connector != null)
                {
                    if (connector.Status == Sandbox.ModAPI.Ingame.MyShipConnectorStatus.Connected && connector.OtherConnector != null && !ShouldStopScan(clusterWeldTargets, clusterGrindTargets, null, maxWeld, maxGrind, 0))
                    {
                        AsyncAddBlocksOfGrid(ref areaBox, useIgnoreColor, ref ignoreColor, useGrindColor, ref grindColor, autoGrindRelation, autoGrindOptions, connector.OtherConnector.CubeGrid, grids, clusterWeldTargets, clusterGrindTargets, maxWeld, maxGrind, skipRangeCheck);
                    }
                    continue;
                }

                if (weldTargetsForGrid != null && ((Settings.Flags & SyncBlockSettings.Settings.AllowBuild) != 0))
                {
                    var projector = fatBlock as Sandbox.ModAPI.IMyProjector;
                    if (projector != null)
                    {
                        if (projector.IsProjecting && projector.BuildableBlocksCount > 0 && IsRelationAllowed4Welding(slimBlock))
                        {
                            if (!State.SafeZoneAllowsBuildingProjections)
                            {
                                continue;
                            }

                            // BUG-053: SE applies safe zone building restrictions at the grid level.
                            // If the projector's physical grid intersects a build-blocked safe zone,
                            // proj.Build() fails for ALL projected blocks — skip this projector entirely.
                            if (IsProjectorGridBuildBlocked(projector))
                            {
                                continue;
                            }

                            var projectedCubeGrid = projector.ProjectedGrid;
                            if (projectedCubeGrid != null && !grids.Contains(projectedCubeGrid))
                            {
                                grids.Add(projectedCubeGrid);
                                var projectedBlocks = GetBlocksFromCache(projectedCubeGrid);

                                foreach (IMySlimBlock block in projectedBlocks)
                                {
                                    if (ShouldStopScan(clusterWeldTargets, clusterGrindTargets, null, maxWeld, maxGrind, 0))
                                    {
                                        break;
                                    }

                                    if (DlcCheckHelper.IsBlockDlcAvailableForOwner(block, _Welder.OwnerId) && BlockWeldPriority.GetEnabled(block) && block.CanBuild(false))
                                    {
                                        double distance;
                                        if (skipRangeCheck || block.IsInRange(ref areaBox, out distance))
                                        {
                                            // Cluster weld cap respected via weldTargetsForGrid.Count (not clusterWeldTargets.Count);
                                            // once this grid's per-block loop pushes it past maxWeld the guard stops further adds.
                                            if (weldTargetsForGrid.Count < maxWeld)
                                            {
                                                weldTargetsForGrid.Add(new ClusterTargetCandidate(block, TargetBlockData.AttributeFlags.Projected));
                                            }
                                        }
                                    }
                                }
                            }
                        }
                        continue;
                    }
                }
            }

            // Per-grid budget: if this grid contributed more candidates than the per-grid max,
            // sort the excess by priority+distance and keep only the best. This replaces the old
            // pre-sort on ALL grid blocks — sorting only qualifying candidates is much cheaper.
            var grindAdded = (clusterGrindTargets != null ? clusterGrindTargets.Count : 0) - grindBefore;
            if (grindAdded > MaxPossibleGrindTargets)
            {
                SortAndCapGridCandidates(clusterGrindTargets, grindBefore, grindAdded, MaxPossibleGrindTargets, true, ref areaBox);
            }
            var weldAdded = (clusterWeldTargets != null ? clusterWeldTargets.Count : 0) - weldBefore;
            if (weldAdded > MaxPossibleWeldTargets)
            {
                SortAndCapGridCandidates(clusterWeldTargets, weldBefore, weldAdded, MaxPossibleWeldTargets, false, ref areaBox);
            }

            // Update empty grid cache: remember grids that contributed no targets.
            // BUG-096: skip this update if we cap-skipped either type at grid entry — the grid
            // may still have valid targets we literally didn't evaluate, and marking it empty
            // would suppress it for EmptyGridRescanDelaySeconds even after the cluster cap frees.
            var weldAfter = clusterWeldTargets != null ? clusterWeldTargets.Count : 0;
            var grindAfter = clusterGrindTargets != null ? clusterGrindTargets.Count : 0;
            if (weldAfter == weldBefore && grindAfter == grindBefore && !weldCapSkipped && !grindCapSkipped)
            {
                _EmptyGridCache[gridEntityId] = playTime;
            }
            else if (weldAfter != weldBefore || grindAfter != grindBefore)
            {
                TimeSpan dummy;
                _EmptyGridCache.TryRemove(gridEntityId, out dummy);
            }

            if (profilerTs != 0L)
            {
                var _gridName = cubeGrid.DisplayName;
                var _gridEid = cubeGrid.EntityId;

                // Capture end timestamp before StopAndLog so grid cost doesn't include logging overhead.
                var endTs = System.Diagnostics.Stopwatch.GetTimestamp();

                var _clusterMembers = _ClusterMemberAreaCenters != null ? _ClusterMemberAreaCenters.Count : 0;
                // BUG-096 diagnostics: cap-skip flags + per-grid added counts so a scan that
                // contributed nothing can be attributed (cap at entry vs truly empty vs scenario/immune).
                var _weldCapSkipped = weldCapSkipped;
                var _grindCapSkipped = grindCapSkipped;
                var _weldAddedHere = weldAfter - weldBefore;
                var _grindAddedHere = grindAfter - grindBefore;
                MethodProfiler.StopAndLog("AsyncAddBlocksOfGrid", profilerTs, () =>
                    string.Format("entityId={0};gridId={1};blocks={2};weldTargets={3};grindTargets={4};weldAdded={5};grindAdded={6};weldCapSkip={7};grindCapSkip={8};skipRange={9};clusterMembers={10}",
                        _Welder.EntityId, _gridEid, newBlocks.Count,
                        clusterWeldTargets != null ? clusterWeldTargets.Count : -1,
                        clusterGrindTargets != null ? clusterGrindTargets.Count : -1,
                        _weldAddedHere, _grindAddedHere, _weldCapSkipped, _grindCapSkipped,
                        blockSkipRange, _clusterMembers));

                // Report per-grid scan cost for profile summary.
                var gridMs = (double)(endTs - profilerTs) / System.Diagnostics.Stopwatch.Frequency * 1000.0;
                MethodProfiler.ReportGridCost(_gridEid, _gridName, gridMs);
            }
        }

        /// <summary>
        /// Removes expired entries from the empty grid cache.
        /// Uses two-pass (collect keys, then remove) to avoid modifying the dictionary during enumeration.
        /// </summary>
        private void CleanupEmptyGridCache()
        {
            var emptyDelay = Mod.Settings.EmptyGridRescanDelaySeconds;
            if (emptyDelay <= 0 || _EmptyGridCache.Count == 0) return;

            var playTime = MyAPIGateway.Session.ElapsedPlayTime;
            List<long> expiredKeys = null;
            foreach (var kvp in _EmptyGridCache)
            {
                if (playTime.Subtract(kvp.Value).TotalSeconds >= emptyDelay)
                {
                    if (expiredKeys == null) expiredKeys = new List<long>();
                    expiredKeys.Add(kvp.Key);
                }
            }

            if (expiredKeys != null)
            {
                TimeSpan dummy;
                foreach (var key in expiredKeys)
                {
                    _EmptyGridCache.TryRemove(key, out dummy);
                }
            }
        }

        /// <summary>
        /// Scans entities inside the bounding box for target blocks and floating objects.
        /// </summary>
        private void AsyncAddBlocksOfBox(ref MyOrientedBoundingBoxD areaBox, bool useIgnoreColor, ref uint ignoreColor, bool useGrindColor, ref uint grindColor, AutoGrindRelation autoGrindRelation, AutoGrindOptions autoGrindOptions, List<IMyCubeGrid> grids, List<ClusterTargetCandidate> clusterWeldTargets, List<ClusterTargetCandidate> clusterGrindTargets, List<ClusterFloatingCandidate> clusterFloatingTargets, int maxWeld, int maxGrind, int maxFloat, bool skipRangeCheck)
        {
            var profilerTs = MethodProfiler.Start();
            var emitterMatrix = _Welder.WorldMatrix;
            emitterMatrix.Translation = Vector3D.Transform(Settings.CorrectedAreaOffset, emitterMatrix);
            var areaBoundingBox = Settings.CorrectedAreaBoundingBox.TransformFast(emitterMatrix);

            var entityInRange = SharedEntityCache.GetEntitiesInBox(ref areaBoundingBox);

            if (entityInRange != null)
            {
                if (clusterGrindTargets != null && (Settings.Flags & SyncBlockSettings.Settings.GrindSmallestGridFirst) != 0)
                {
                    var sortedGrids = new List<IMyEntity>(entityInRange.Count);
                    var nonGridEntities = new List<IMyEntity>();
                    foreach (var e in entityInRange)
                    {
                        if (ShouldStopScan(clusterWeldTargets, clusterGrindTargets, clusterFloatingTargets, maxWeld, maxGrind, maxFloat))
                        {
                            break;
                        }

                        if (e is MyCubeGrid)
                            sortedGrids.Add(e);
                        else
                            nonGridEntities.Add(e);
                    }

                    sortedGrids.Sort((a, b) => ((MyCubeGrid)a).BlocksCount - ((MyCubeGrid)b).BlocksCount);
                    sortedGrids.AddRange(nonGridEntities);
                    entityInRange = sortedGrids;
                }

                foreach (var entity in entityInRange)
                {
                    if (ShouldStopScan(clusterWeldTargets, clusterGrindTargets, clusterFloatingTargets, maxWeld, maxGrind, maxFloat))
                    {
                        break;
                    }

                    var grid = entity as IMyCubeGrid;
                    if (grid != null)
                    {
                        var cubeGrid = grid as MyCubeGrid;
                        if (cubeGrid != null && cubeGrid.Projector == null)
                        {
                            if (cubeGrid.IsPreview || !cubeGrid.Editable)
                            {
                                continue;
                            }
                        }

                        AsyncAddBlocksOfGrid(ref areaBox, useIgnoreColor, ref ignoreColor, useGrindColor, ref grindColor, autoGrindRelation, autoGrindOptions, grid, grids, clusterWeldTargets, clusterGrindTargets, maxWeld, maxGrind, skipRangeCheck);
                        continue;
                    }

                    if (clusterFloatingTargets != null)
                    {
                        if (clusterFloatingTargets.Count >= maxFloat)
                        {
                            continue;
                        }

                        var floating = entity as MyFloatingObject;
                        if (floating != null)
                        {
                            if (!floating.MarkedForClose && ComponentCollectPriority.GetEnabled(floating.Item.Content.GetObjectId()))
                            {
                                clusterFloatingTargets.Add(new ClusterFloatingCandidate(floating, floating.WorldMatrix.Translation));
                            }
                            continue;
                        }

                        var character = entity as IMyCharacter;
                        if (character != null)
                        {
                            if (character.IsDead && !character.InventoriesEmpty() && !((MyCharacterDefinition)character.Definition).EnableSpawnInventoryAsContainer)
                            {
                                clusterFloatingTargets.Add(new ClusterFloatingCandidate(character, character.WorldMatrix.Translation));
                            }
                            continue;
                        }

                        var inventoryBag = entity as IMyInventoryBag;
                        if (inventoryBag != null)
                        {
                            if (!inventoryBag.InventoriesEmpty())
                            {
                                clusterFloatingTargets.Add(new ClusterFloatingCandidate(inventoryBag, inventoryBag.WorldMatrix.Translation));
                            }
                            continue;
                        }
                    }
                }
            }
            if (profilerTs != 0L)
            {
                MethodProfiler.StopAndLog("AsyncAddBlocksOfBox", profilerTs, () =>
                    string.Format("entityId={0};entities={1};weldTargets={2};grindTargets={3};floatTargets={4}",
                        _Welder.EntityId,
                        entityInRange != null ? entityInRange.Count : 0,
                        clusterWeldTargets != null ? clusterWeldTargets.Count : -1,
                        clusterGrindTargets != null ? clusterGrindTargets.Count : -1,
                        clusterFloatingTargets != null ? clusterFloatingTargets.Count : -1));
            }
        }

        /// <summary>
        /// Starts the async cluster scan for the coordinator BaR.
        /// Guards against re-entry with _AsyncUpdateSourcesAndTargetsRunning flag.
        /// </summary>
        private void StartAsyncClusterScan(ScanCluster cluster, bool updateSource)
        {
            if (!_Welder.UseConveyorSystem)
            {
                lock (_PossibleSources) { _PossibleSources.Clear(); }
                lock (_PossiblePushTargets) { _PossiblePushTargets.Clear(); }
            }

            if (!_Welder.Enabled || !_Welder.IsFunctional || State.Ready == false)
            {
                // BUG-095: Defer cleanup if a scan enqueued on a prior tick is still
                // running. Force-clearing the flag here would let the next tick enqueue
                // a second concurrent scan on a disable→enable bounce, producing
                // interleaved target-list swaps. The background finally at line 961
                // clears the flag under the same lock; the next tick's early-exit
                // will do the cleanup then if we're still disabled.
                lock (_Welder)
                {
                    if (_AsyncUpdateSourcesAndTargetsRunning) return;
                }
                lock (State.PossibleWeldTargets) { State.PossibleWeldTargets.Clear(); State.PossibleWeldTargets.RebuildHash(); }
                lock (State.PossibleGrindTargets) { State.PossibleGrindTargets.Clear(); State.PossibleGrindTargets.RebuildHash(); }
                lock (State.PossibleFloatingTargets) { State.PossibleFloatingTargets.Clear(); State.PossibleFloatingTargets.RebuildHash(); }
                _InitialScanCompleted = false;
                _LastTargetsUpdate = MyAPIGateway.Session.ElapsedPlayTime;
                _LastSourceUpdate = _LastTargetsUpdate;
                return;
            }

            lock (_Welder)
            {
                if (_AsyncUpdateSourcesAndTargetsRunning) return;
                _AsyncUpdateSourcesAndTargetsRunning = true;
                Mod.AddAsyncAction(() => AsyncClusterScan(cluster, updateSource));
            }
        }

        /// <summary>
        /// Coordinator scan: performs the expensive scan once for the entire cluster.
        /// Produces a ScanClusterResult with candidates that passed all position-independent checks
        /// (but NOT IsInRange for multi-member clusters). After publishing shared results, applies own range/distance filtering.
        /// </summary>
        private void AsyncClusterScan(ScanCluster cluster, bool updateSource)
        {
            var profilerTs = MethodProfiler.Start();
            try
            {
                if (!State.Ready) return;

                var weldingEnabled = BlockWeldPriority.AnyEnabled && Settings.WorkMode != WorkModes.GrindOnly;

                var useGrindColor = (Settings.Flags & SyncBlockSettings.Settings.UseGrindColor) != 0;
                var autoGrindRelation = Settings.UseGrindJanitorOn;
                var grindingEnabled = BlockGrindPriority.AnyEnabled
                    && Settings.WorkMode != WorkModes.WeldOnly
                    && (useGrindColor || autoGrindRelation != 0);

                updateSource &= _Welder.UseConveyorSystem;

                // Collection budget: gather enough candidates so that SortAndCapGridCandidates
                // can select the BEST 256 per grid (nearest/farthest by user setting), rather
                // than keeping the first 256 in arbitrary grid iteration order.
                // Minimum 4x ensures solo BaRs scanning a grid with >256 qualifying blocks
                // still collect enough to find the truly nearest/farthest targets.
                // Multi-member clusters scale linearly with member count (capped at 16x) so
                // that members placed far from the coordinator still get candidates in their
                // own working area after the member-aware sort+truncate.
                var memberCount = cluster.Members.Count;
                var capMultiplier = Math.Max(4, Math.Min(memberCount * 4, 16));
                var maxWeld = MaxPossibleWeldTargets * capMultiplier;
                var maxGrind = MaxPossibleGrindTargets * capMultiplier;
                var maxFloat = MaxPossibleFloatingTargets * capMultiplier;

                var result = new ScanClusterResult();
                var clusterWeldTargets = weldingEnabled ? result.WeldCandidates : null;
                var clusterGrindTargets = grindingEnabled ? result.GrindCandidates : null;
                var clusterFloatingTargets = _ComponentCollectPriority.AnyEnabled ? result.FloatingCandidates : null;

                try
                {
                    var grids = new List<IMyCubeGrid>();

                    var ignoreColor = Settings.IgnoreColorPacked;
                    var grindColor = Settings.GrindColorPacked;
                    var emitterMatrix = _Welder.WorldMatrix;
                    emitterMatrix.Translation = Vector3D.Transform(Settings.CorrectedAreaOffset, emitterMatrix);
                    var areaOrientedBox = new MyOrientedBoundingBoxD(Settings.CorrectedAreaBoundingBox, emitterMatrix);

                    // Solo coordinators scan with range checks (same as legacy behavior).
                    // Multi-member coordinators skip range checks — members apply their own filtering.
                    var skipRangeCheck = cluster.Members.Count > 1;

                    // Snapshot each cluster member's working-area center so collect/sort
                    // comparators can score candidates by proximity to ANY member instead
                    // of just the coordinator. Without this, BaRs placed far from the
                    // coordinator on the same grid are starved of targets because the
                    // per-grid cap keeps only blocks near the coordinator.
                    if (memberCount > 1)
                    {
                        if (_ClusterMemberAreaCenters == null)
                            _ClusterMemberAreaCenters = new List<Vector3D>(memberCount);
                        else
                            _ClusterMemberAreaCenters.Clear();

                        if (_ClusterMemberAreaBoxes == null)
                            _ClusterMemberAreaBoxes = new List<MyOrientedBoundingBoxD>(memberCount);
                        else
                            _ClusterMemberAreaBoxes.Clear();

                        for (int i = 0; i < cluster.Members.Count; i++)
                        {
                            var member = cluster.Members[i];
                            if (member == null || member.Welder == null) continue;
                            var memberMatrix = member.Welder.WorldMatrix;
                            memberMatrix.Translation = Vector3D.Transform(member.Settings.CorrectedAreaOffset, memberMatrix);
                            var memberBox = new MyOrientedBoundingBoxD(member.Settings.CorrectedAreaBoundingBox, memberMatrix);
                            _ClusterMemberAreaCenters.Add(memberBox.Center);
                            _ClusterMemberAreaBoxes.Add(memberBox);
                        }
                    }

                    // Scan own grid
                    AsyncAddBlocksOfGrid(ref areaOrientedBox, ((Settings.Flags & SyncBlockSettings.Settings.UseIgnoreColor) != 0), ref ignoreColor, ((Settings.Flags & SyncBlockSettings.Settings.UseGrindColor) != 0), ref grindColor, Settings.UseGrindJanitorOn, Settings.GrindJanitorOptions, _Welder.CubeGrid, grids, clusterWeldTargets, clusterGrindTargets, maxWeld, maxGrind, skipRangeCheck);

                    // BoundingBox scan if applicable
                    if (Settings.SearchMode == SearchModes.BoundingBox)
                    {
                        AsyncAddBlocksOfBox(ref areaOrientedBox, ((Settings.Flags & SyncBlockSettings.Settings.UseIgnoreColor) != 0), ref ignoreColor, ((Settings.Flags & SyncBlockSettings.Settings.UseGrindColor) != 0), ref grindColor, Settings.UseGrindJanitorOn, Settings.GrindJanitorOptions, grids, clusterWeldTargets, clusterGrindTargets, clusterFloatingTargets, maxWeld, maxGrind, maxFloat, skipRangeCheck);
                    }

                    // Source scanning (once for the cluster)
                    if (updateSource)
                    {
                        var tempSources = new List<IMyInventory>();
                        AsyncScanForSources(tempSources);

                        Vector3D posWelder;
                        _Welder.SlimBlock.ComputeWorldCenter(out posWelder);
                        try
                        {
                            tempSources.Sort((a, b) =>
                            {
                                var blockA = a.Owner as IMyCubeBlock;
                                var blockB = b.Owner as IMyCubeBlock;
                                if (blockA != null && blockB != null)
                                {
                                    var welderA = blockA as IMyShipWelder;
                                    var welderB = blockB as IMyShipWelder;
                                    if ((welderA == null) == (welderB == null))
                                    {
                                        Vector3D posA;
                                        Vector3D posB;
                                        blockA.SlimBlock.ComputeWorldCenter(out posA);
                                        blockB.SlimBlock.ComputeWorldCenter(out posB);
                                        var distanceA = (int)Math.Abs((posWelder - posA).Length());
                                        var distanceB = (int)Math.Abs((posWelder - posB).Length());
                                        return distanceA - distanceB;
                                    }
                                    else if (welderA == null) return -1;
                                    else return 1;
                                }
                                else if (blockA != null) return -1;
                                else if (blockB != null) return 1;
                                else return 0;
                            });
                        }
                        catch (Exception ex)
                        {
                            Logging.Instance.Error("Error on .Sort for cluster sources. Exception: {0}", ex);
                        }

                        foreach (var inventory in tempSources)
                        {
                            if (inventory.Owner is IMyCargoContainer || inventory.Owner is IMyRefinery)
                            {
                                result.PushTargets.Add(inventory);
                            }
                        }

                        tempSources.RemoveAll(inv => inv.Owner is IMyRefinery);
                        result.Sources.AddRange(tempSources);
                        result.SourcesUpdated = true;
                    }

                    result.Timestamp = MyAPIGateway.Session.ElapsedPlayTime;

                    // Pre-sort candidates for multi-member clusters.
                    // Members reuse this order and skip their local sort.
                    if (memberCount > 1)
                    {
                        PreSortClusterCandidates(areaOrientedBox.Center, clusterGrindTargets, clusterWeldTargets);
                        result.PreSorted = true;
                    }
                }
                catch (Exception ex)
                {
                    Logging.Instance.Error("BuildAndRepairSystemBlock {0}: AsyncClusterScan exception: {1}", Logging.BlockName(_Welder, Logging.BlockNameOptions.None), ex);
                }

                // Clean expired entries from empty grid cache
                CleanupEmptyGridCache();

                // Publish shared result (atomic reference swap)
                cluster.SetResult(result);

                // Coordinator is also a member — apply own range/distance filtering
                ApplyClusterResultToSelf(result, updateSource);

                MissedResultCycles = 0;
                _InitialScanCompleted = true;

            }
            finally
            {
                // Release cluster member snapshot so stale member references don't leak
                // across scan cycles. Next multi-member scan will repopulate.
                if (_ClusterMemberAreaCenters != null)
                    _ClusterMemberAreaCenters.Clear();
                if (_ClusterMemberAreaBoxes != null)
                    _ClusterMemberAreaBoxes.Clear();

                _LastTargetsUpdate = MyAPIGateway.Session.ElapsedPlayTime;
                if (updateSource) _LastSourceUpdate = _LastTargetsUpdate;
                lock (_Welder)
                {
                    _AsyncUpdateSourcesAndTargetsRunning = false;
                }
                if (profilerTs != 0L)
                {
                    MethodProfiler.StopAndLog("AsyncClusterScan", profilerTs, () =>
                        string.Format("entityId={0};updateSource={1};clusterMembers={2}",
                            _Welder.EntityId, updateSource, cluster.Members.Count));
                }
            }
        }

        /// <summary>
        /// Pre-sorts cluster candidates using the coordinator's settings and position.
        /// Members reuse this sort order, avoiding redundant per-member sorts.
        /// For co-located BaRs the coordinator's distance order is a near-perfect
        /// approximation, so members can skip their expensive local sort.
        /// </summary>
        private void PreSortClusterCandidates(Vector3D coordCenter, List<ClusterTargetCandidate> grindCandidates, List<ClusterTargetCandidate> weldCandidates)
        {
            var profilerTs = MethodProfiler.Start();
            try
            {
                // BUG-058: Reuse one distance dictionary for both grind and weld sorts
                // to avoid two allocations per scan cycle.
                var maxCap = Math.Max(
                    grindCandidates != null ? grindCandidates.Count : 0,
                    weldCandidates != null ? weldCandidates.Count : 0);
                var distances = maxCap > 0 ? new Dictionary<IMySlimBlock, double>(maxCap) : null;

                if (grindCandidates != null && grindCandidates.Count > 1)
                {
                    var grindUsePriority = (Settings.Flags & SyncBlockSettings.Settings.GrindIgnorePriorityOrder) == 0;
                    var grindSmallestGridFirst = (Settings.Flags & SyncBlockSettings.Settings.GrindSmallestGridFirst) != 0;
                    var grindNearFirst = (Settings.Flags & SyncBlockSettings.Settings.GrindNearFirst) != 0;

                    distances.Clear();
                    foreach (var c in grindCandidates)
                    {
                        var blockPos = c.Block.CubeGrid.GridIntegerToWorld(c.Block.Position);
                        distances[c.Block] = MinSquaredDistanceToClusterMembers(ref blockPos, ref coordCenter);
                    }

                    // BUG-091: Build per-grid minimum-distance lookup so GrindSmallestGridFirst
                    // orders same-size grids by proximity of their nearest block instead of by
                    // arbitrary EntityId. Skip when not needed to keep the pre-pass cost zero
                    // for the common path.
                    if (grindSmallestGridFirst)
                    {
                        _gridMinDistLookup.Clear();
                        foreach (var c in grindCandidates)
                        {
                            double bd;
                            distances.TryGetValue(c.Block, out bd);
                            var gid = c.Block.CubeGrid.EntityId;
                            double existing;
                            if (_gridMinDistLookup.TryGetValue(gid, out existing))
                            {
                                if (bd < existing) _gridMinDistLookup[gid] = bd;
                            }
                            else
                            {
                                _gridMinDistLookup[gid] = bd;
                            }
                        }
                    }

                    grindCandidates.Sort((a, b) =>
                    {
                        var cmp = CompareGrindNonDistance(a.Block, a.Attributes, b.Block, b.Attributes,
                            grindUsePriority, grindSmallestGridFirst,
                            grindSmallestGridFirst ? _gridMinDistLookup : null);
                        if (cmp != 0) return cmp;

                        // Distance compare (squared, min-to-any-member via pre-built cache).
                        double distA, distB;
                        distances.TryGetValue(a.Block, out distA);
                        distances.TryGetValue(b.Block, out distB);
                        return grindNearFirst ? distA.CompareTo(distB) : distB.CompareTo(distA);
                    });
                }

                if (weldCandidates != null && weldCandidates.Count > 1)
                {
                    distances.Clear();
                    foreach (var c in weldCandidates)
                    {
                        var blockPos = c.Block.CubeGrid.GridIntegerToWorld(c.Block.Position);
                        distances[c.Block] = MinSquaredDistanceToClusterMembers(ref blockPos, ref coordCenter);
                    }

                    weldCandidates.Sort((a, b) =>
                    {
                        var cmp = CompareWeldPriority(a.Block, b.Block);
                        if (cmp != 0) return cmp;

                        double distA, distB;
                        distances.TryGetValue(a.Block, out distA);
                        distances.TryGetValue(b.Block, out distB);
                        var distCmp = distA.CompareTo(distB);
                        if (distCmp != 0) return distCmp;

                        return CompareBlockStableTiebreak(a.Block, b.Block);
                    });
                }
            }
            catch (Exception ex)
            {
                Logging.Instance.Error("PreSortClusterCandidates error: {0}", ex);
            }
            finally
            {
                var _grindCount = grindCandidates != null ? grindCandidates.Count : 0;
                var _weldCount = weldCandidates != null ? weldCandidates.Count : 0;
                var _clusterMembers = _ClusterMemberAreaCenters != null ? _ClusterMemberAreaCenters.Count : 0;
                if (profilerTs != 0L)
                {
                    MethodProfiler.StopAndLog("PreSortClusterCandidates", profilerTs, () =>
                        string.Format("entityId={0};grindCandidates={1};weldCandidates={2};clusterMembers={3}",
                            _Welder.EntityId, _grindCount, _weldCount, _clusterMembers));
                }
            }
        }

        /// <summary>
        /// Starts the async cluster result application for a non-coordinator member.
        /// </summary>
        private void StartAsyncApplyClusterResults(ScanCluster cluster, bool updateSource)
        {
            if (!_Welder.UseConveyorSystem)
            {
                lock (_PossibleSources) { _PossibleSources.Clear(); }
                lock (_PossiblePushTargets) { _PossiblePushTargets.Clear(); }
            }

            if (!_Welder.Enabled || !_Welder.IsFunctional || State.Ready == false)
            {
                // BUG-095: Defer cleanup if a scan enqueued on a prior tick is still
                // running. Force-clearing the flag here would let the next tick enqueue
                // a second concurrent scan on a disable→enable bounce, producing
                // interleaved target-list swaps. The background finally at line 1184
                // clears the flag under the same lock; the next tick's early-exit
                // will do the cleanup then if we're still disabled.
                lock (_Welder)
                {
                    if (_AsyncUpdateSourcesAndTargetsRunning) return;
                }
                lock (State.PossibleWeldTargets) { State.PossibleWeldTargets.Clear(); State.PossibleWeldTargets.RebuildHash(); }
                lock (State.PossibleGrindTargets) { State.PossibleGrindTargets.Clear(); State.PossibleGrindTargets.RebuildHash(); }
                lock (State.PossibleFloatingTargets) { State.PossibleFloatingTargets.Clear(); State.PossibleFloatingTargets.RebuildHash(); }
                _InitialScanCompleted = false;
                _LastTargetsUpdate = MyAPIGateway.Session.ElapsedPlayTime;
                _LastSourceUpdate = _LastTargetsUpdate;
                return;
            }

            lock (_Welder)
            {
                if (_AsyncUpdateSourcesAndTargetsRunning) return;
                _AsyncUpdateSourcesAndTargetsRunning = true;
                Mod.AddAsyncAction(() => AsyncApplyClusterResults(cluster, updateSource));
            }
        }

        /// <summary>
        /// Member (non-coordinator) applies shared cluster results with own range/distance filtering.
        /// Much cheaper than a full scan — just range checks + distance sort on pre-filtered candidates.
        /// Falls back to emergency coordinator scan after 3 consecutive missed results.
        /// </summary>
        private void AsyncApplyClusterResults(ScanCluster cluster, bool updateSource)
        {
            var profilerTs = MethodProfiler.Start();
            try
            {
                if (!State.Ready) return;

                var result = cluster.GetResult();
                if (result == null)
                {
                    MissedResultCycles++;
                    if (MissedResultCycles > 3)
                    {
                        // Fallback: act as emergency coordinator — scan and publish results
                        AsyncClusterScan(cluster, updateSource);
                    }
                    return;
                }

                MissedResultCycles = 0;
                ApplyClusterResultToSelf(result, updateSource);
                _InitialScanCompleted = true;

            }
            finally
            {
                _LastTargetsUpdate = MyAPIGateway.Session.ElapsedPlayTime;
                if (updateSource) _LastSourceUpdate = _LastTargetsUpdate;
                lock (_Welder)
                {
                    _AsyncUpdateSourcesAndTargetsRunning = false;
                }
                if (profilerTs != 0L)
                {
                    MethodProfiler.StopAndLog("AsyncApplyClusterResults", profilerTs, () =>
                        string.Format("entityId={0};updateSource={1};missed={2}",
                            _Welder.EntityId, updateSource, MissedResultCycles));
                }
            }
        }

        /// <summary>
        /// Applies a ScanClusterResult to this BaR by filtering candidates through own IsInRange,
        /// computing distances, sorting, and swapping into State. Used by both coordinator and members.
        /// </summary>
        /// <summary>
        /// Truncates a sorted target list to maxCount while preserving representation
        /// from multiple grids. Each grid gets a guaranteed minimum number of slots
        /// so that one dominant grid cannot crowd out all others after sorting.
        /// </summary>
        private void TruncateGridAware(List<TargetBlockData> list, int maxCount)
        {
            if (list.Count <= maxCount) return;

            // Count distinct grids (reuse pooled HashSet)
            _truncateGridIds.Clear();
            for (int i = 0; i < list.Count; i++)
            {
                _truncateGridIds.Add(list[i].Block.CubeGrid.EntityId);
            }

            // Single grid — simple truncation, no fairness needed
            if (_truncateGridIds.Count <= 1)
            {
                list.RemoveRange(maxCount, list.Count - maxCount);
                return;
            }

            int numGrids = _truncateGridIds.Count;
            int minPerGrid = Math.Max(maxCount / numGrids, 4);

            // Track how many blocks we've kept per grid (reuse pooled Dictionary)
            _truncateKeptPerGrid.Clear();
            foreach (var id in _truncateGridIds)
            {
                _truncateKeptPerGrid[id] = 0;
            }

            // First pass: walk sorted list, keep items until each grid hits minPerGrid
            // or we fill up. Items beyond minPerGrid go to overflow for second pass.
            _truncateKept.Clear();
            _truncateOverflow.Clear();

            for (int i = 0; i < list.Count; i++)
            {
                var item = list[i];
                var gid = item.Block.CubeGrid.EntityId;
                int count;
                _truncateKeptPerGrid.TryGetValue(gid, out count);

                if (count < minPerGrid)
                {
                    _truncateKept.Add(item);
                    _truncateKeptPerGrid[gid] = count + 1;
                    if (_truncateKept.Count >= maxCount) break;
                }
                else
                {
                    _truncateOverflow.Add(item);
                }
            }

            // Second pass: fill remaining slots from overflow (already in sort order)
            if (_truncateKept.Count < maxCount)
            {
                int remaining = maxCount - _truncateKept.Count;
                for (int i = 0; i < _truncateOverflow.Count && remaining > 0; i++)
                {
                    _truncateKept.Add(_truncateOverflow[i]);
                    remaining--;
                }
            }

            list.Clear();
            list.AddRange(_truncateKept);
        }

        /// <summary>
        /// Sorts a subrange of the candidate list and removes excess.
        /// Called after per-grid collection to enforce the per-grid budget while keeping
        /// the best candidates based on the user's sort settings.
        /// BUG-086: Must respect GrindIgnorePriorityOrder — when priority is disabled,
        /// the cap must select by distance/grid-size so it keeps the blocks the user
        /// actually wants (e.g., farthest), not the highest-priority ones.
        /// </summary>
        // ----------------------------------------------------------------------------------
        // Shared sort key helpers (FEAT-070 consolidation).
        //
        // The scan pipeline does weld/grind sorts in several places:
        //   1. SortAndCapGridCandidates         — per-grid sort on ClusterTargetCandidate.
        //   2. PreSortClusterCandidates         — cluster-wide pre-sort on ClusterTargetCandidate.
        //   3. ApplyClusterResultToSelf (pre)   — per-BaR pre-sort on TargetBlockData.
        //   4. ApplyClusterResultToSelf (post)  — post-truncate re-sort on TargetBlockData.
        //
        // Before consolidation each site open-coded the autogrind-first bucket, priority
        // check, GrindSmallestGridFirst + BUG-091 spatial tiebreaker, and (for weld) the
        // grid/position stable tiebreakers. BUG-086/BUG-091 fixes had to touch every copy;
        // one copy in ApplyClusterResultToSelf also had a subtle bug where farthest-first
        // + smallest-grid-first combined would fall back to nearest-first within same-sized
        // grids (see CompareGrindNonDistance below).
        //
        // These helpers centralize the non-distance key so each call site only owns:
        //   - distance metric (squared / non-squared / member-aware)
        //   - grindNearFirst direction
        //   - its own tiebreakers (if any)
        // Each helper is an instance method because the priority lookups (BlockWeldPriority /
        // BlockGrindPriority) are per-NanobotSystem fields. All are pure — no side effects.
        // ----------------------------------------------------------------------------------

        /// <summary>
        /// Grind sort key — everything except the final distance compare. Handles:
        ///   1. Autogrind-first bucket (autogrind blocks precede non-autogrind).
        ///   2. Per-type priority (skipped when user sets GrindIgnorePriorityOrder).
        ///   3. GrindSmallestGridFirst: smaller grid wins; equal-size grids use BUG-091's
        ///      per-grid nearest-block min-distance tiebreaker; equal-size-and-min-distance
        ///      fall back to a deterministic EntityId tiebreaker.
        /// Returns 0 when the caller should fall through to its distance compare (which
        /// MUST honor GrindNearFirst). <paramref name="perGridMinDist"/> may be null when
        /// smallestGridFirst is off or when BUG-091 tiebreaker data isn't applicable
        /// (per-grid sort over a single grid, for example).
        /// </summary>
        private int CompareGrindNonDistance(
            IMySlimBlock blockA, TargetBlockData.AttributeFlags attrsA,
            IMySlimBlock blockB, TargetBlockData.AttributeFlags attrsB,
            bool usePriority, bool smallestGridFirst,
            Dictionary<long, double> perGridMinDist)
        {
            var autogrindA = (attrsA & TargetBlockData.AttributeFlags.Autogrind) != 0;
            var autogrindB = (attrsB & TargetBlockData.AttributeFlags.Autogrind) != 0;
            if (autogrindA != autogrindB) return autogrindA ? -1 : 1;

            if (usePriority)
            {
                var priorityA = BlockGrindPriority.GetPriority(blockA);
                var priorityB = BlockGrindPriority.GetPriority(blockB);
                if (priorityA != priorityB) return priorityA - priorityB;
            }

            if (smallestGridFirst)
            {
                var gridRes = ((MyCubeGrid)blockA.CubeGrid).BlocksCount - ((MyCubeGrid)blockB.CubeGrid).BlocksCount;
                if (gridRes != 0) return gridRes;

                if (perGridMinDist != null)
                {
                    double minA, minB;
                    perGridMinDist.TryGetValue(blockA.CubeGrid.EntityId, out minA);
                    perGridMinDist.TryGetValue(blockB.CubeGrid.EntityId, out minB);
                    var minCmp = minA.CompareTo(minB);
                    if (minCmp != 0) return minCmp;
                }

                var gridCmp = blockA.CubeGrid.EntityId.CompareTo(blockB.CubeGrid.EntityId);
                if (gridCmp != 0) return gridCmp;
            }

            return 0;
        }

        /// <summary>
        /// Weld sort key — priority only. Weld has no smallest-grid-first / autogrind bucket.
        /// Caller follows with a distance compare and optional stable tiebreakers.
        /// </summary>
        private int CompareWeldPriority(IMySlimBlock blockA, IMySlimBlock blockB)
        {
            var priorityA = BlockWeldPriority.GetPriority(blockA);
            var priorityB = BlockWeldPriority.GetPriority(blockB);
            return priorityA - priorityB;
        }

        /// <summary>
        /// Stable deterministic tiebreaker used after priority+distance to keep sort order
        /// reproducible across identical-key ties (avoids output churn between scan cycles).
        /// Static because it touches no instance state.
        /// </summary>
        private static int CompareBlockStableTiebreak(IMySlimBlock blockA, IMySlimBlock blockB)
        {
            var gridCmp = blockA.CubeGrid.EntityId.CompareTo(blockB.CubeGrid.EntityId);
            if (gridCmp != 0) return gridCmp;
            var posA = blockA.Position;
            var posB = blockB.Position;
            if (posA.X != posB.X) return posA.X - posB.X;
            if (posA.Y != posB.Y) return posA.Y - posB.Y;
            return posA.Z - posB.Z;
        }

        /// <summary>
        /// Returns the minimum squared distance from <paramref name="blockPos"/> to any
        /// snapshotted cluster member area center. Falls back to <paramref name="fallbackCenter"/>
        /// when the snapshot is empty (solo cluster). Keeps multi-member clusters member-aware
        /// so distant members aren't starved of targets by a coordinator-centric sort.
        /// </summary>
        private double MinSquaredDistanceToClusterMembers(ref Vector3D blockPos, ref Vector3D fallbackCenter)
        {
            var centers = _ClusterMemberAreaCenters;
            if (centers == null || centers.Count == 0)
            {
                return (fallbackCenter - blockPos).LengthSquared();
            }

            var best = double.MaxValue;
            for (int i = 0; i < centers.Count; i++)
            {
                var c = centers[i];
                var d = (c - blockPos).LengthSquared();
                if (d < best) best = d;
            }
            return best;
        }

        private void SortAndCapGridCandidates(List<ClusterTargetCandidate> list, int startIndex, int count, int maxKeep, bool isGrinding, ref MyOrientedBoundingBoxD areaBox)
        {
            var profilerTs = MethodProfiler.Start();
            // Sub-timings so a single profile line reveals which phase (partition/sort) dominates.
            // Background-thread only; main-thread cost is zero (lazy log line inside profilerTs gate).
            var partitionTicks = 0L;
            var sortTicks = 0L;
            var tsFreq = System.Diagnostics.Stopwatch.Frequency;
            long tsMark;

            var center = areaBox.Center;
            var grindNearFirst = isGrinding && (Settings.Flags & SyncBlockSettings.Settings.GrindNearFirst) != 0;
            var grindSmallestFirst = isGrinding && (Settings.Flags & SyncBlockSettings.Settings.GrindSmallestGridFirst) != 0;
            var grindIgnorePriority = isGrinding && (Settings.Flags & SyncBlockSettings.Settings.GrindIgnorePriorityOrder) != 0;

            // Capture snapshot presence once — comparator runs many times and the field
            // is only written at scan start / cleared in finally (same background thread).
            var memberCenters = _ClusterMemberAreaCenters;
            var useMemberAware = memberCenters != null && memberCenters.Count > 0;

            // BUG-096: Multi-member cluster scans run with skipRangeCheck=true so the collection
            // loop doesn't filter out-of-range blocks per-member. If we sort that raw set with
            // farthest-first (largest min-distance wins), the kept top-N is *deliberately* the
            // blocks nobody can reach, and every member's own IsInRange filter in
            // ApplyClusterResultToSelf then throws them away — members go idle while the grid
            // still has plenty of in-range targets. Partition in-range-to-any-member candidates
            // to the front of the subrange before sorting so farthest-first picks the farthest
            // reachable block. Solo scans (useMemberAware=false) already filtered per-block
            // during collection and skip this step.
            var memberBoxes = _ClusterMemberAreaBoxes;
            var effectiveCount = count;
            var partitionRan = false;
            if (useMemberAware && memberBoxes != null && memberBoxes.Count > 0)
            {
                partitionRan = true;
                tsMark = System.Diagnostics.Stopwatch.GetTimestamp();
                var end = startIndex + count;
                var writeIdx = startIndex;
                for (int readIdx = startIndex; readIdx < end; readIdx++)
                {
                    var candidate = list[readIdx];
                    if (candidate.Block == null || candidate.Block.CubeGrid == null) continue;

                    Vector3 halfExtents;
                    candidate.Block.ComputeScaledHalfExtents(out halfExtents);
                    var blockMatrix = candidate.Block.CubeGrid.WorldMatrix;
                    blockMatrix.Translation = candidate.Block.CubeGrid.GridIntegerToWorld(candidate.Block.Position);
                    var blockObb = new MyOrientedBoundingBoxD(new BoundingBoxD(-(halfExtents), (halfExtents)), blockMatrix);

                    var inRange = false;
                    for (int mi = 0; mi < memberBoxes.Count; mi++)
                    {
                        var mb = memberBoxes[mi];
                        if (mb.Intersects(ref blockObb))
                        {
                            inRange = true;
                            break;
                        }
                    }

                    if (inRange)
                    {
                        if (writeIdx != readIdx)
                        {
                            var tmp = list[writeIdx];
                            list[writeIdx] = candidate;
                            list[readIdx] = tmp;
                        }
                        writeIdx++;
                    }
                }
                effectiveCount = writeIdx - startIndex;
                partitionTicks = System.Diagnostics.Stopwatch.GetTimestamp() - tsMark;
            }

            // Nothing reachable — drop everything this grid contributed. Member-level
            // IsInRange would have filtered them anyway; dropping here frees cluster slots
            // for other grids that may still have reachable blocks on subsequent iterations.
            if (effectiveCount == 0)
            {
                list.RemoveRange(startIndex, count);
                if (profilerTs != 0L)
                {
                    var _count = count;
                    var _isGrinding = isGrinding;
                    var _maxKeep = maxKeep;
                    var _partitionMs = partitionTicks * 1000.0 / tsFreq;
                    var _memberCount = memberBoxes != null ? memberBoxes.Count : 0;
                    MethodProfiler.StopAndLog("SortAndCapGridCandidates", profilerTs, () =>
                        string.Format("entityId={0};isGrinding={1};count={2};effectiveCount=0;maxKeep={3};partitionRan=True;members={4};partitionMs={5:F3};sortMs=0.000;action=dropAll",
                            _Welder.EntityId, _isGrinding, _count, _maxKeep, _memberCount, _partitionMs));
                }
                return;
            }

            tsMark = System.Diagnostics.Stopwatch.GetTimestamp();
            list.Sort(startIndex, effectiveCount, Comparer<ClusterTargetCandidate>.Create((a, b) =>
            {
                // Per-grid sort: all candidates share the same CubeGrid so grindSmallestFirst's
                // BlocksCount tiebreaker is a no-op here (same grid = same count), but we still
                // pass it through for consistency with the other call sites. perGridMinDist is
                // null because there's only one grid in play.
                int cmp;
                if (isGrinding)
                {
                    cmp = CompareGrindNonDistance(a.Block, a.Attributes, b.Block, b.Attributes,
                        !grindIgnorePriority, grindSmallestFirst, null);
                }
                else
                {
                    cmp = CompareWeldPriority(a.Block, b.Block);
                }
                if (cmp != 0) return cmp;

                // Distance compare: inline squared, member-aware for multi-member clusters.
                var posA = a.Block.CubeGrid.GridIntegerToWorld(a.Block.Position);
                var posB = b.Block.CubeGrid.GridIntegerToWorld(b.Block.Position);
                double distA, distB;
                if (useMemberAware)
                {
                    distA = double.MaxValue;
                    distB = double.MaxValue;
                    for (int i = 0; i < memberCenters.Count; i++)
                    {
                        var c = memberCenters[i];
                        var dA = (c - posA).LengthSquared();
                        if (dA < distA) distA = dA;
                        var dB = (c - posB).LengthSquared();
                        if (dB < distB) distB = dB;
                    }
                }
                else
                {
                    distA = (center - posA).LengthSquared();
                    distB = (center - posB).LengthSquared();
                }
                return (isGrinding && !grindNearFirst) ? distB.CompareTo(distA) : distA.CompareTo(distB);
            }));
            sortTicks = System.Diagnostics.Stopwatch.GetTimestamp() - tsMark;

            // Trim: drop the out-of-range tail (effectiveCount..count) and any overflow
            // beyond maxKeep from the sorted in-range prefix. keep = min(maxKeep, effectiveCount).
            var keep = effectiveCount < maxKeep ? effectiveCount : maxKeep;
            if (count > keep)
            {
                list.RemoveRange(startIndex + keep, count - keep);
            }

            if (profilerTs != 0L)
            {
                var _count = count;
                var _effective = effectiveCount;
                var _keep = keep;
                var _maxKeep = maxKeep;
                var _isGrinding = isGrinding;
                var _partitionRan = partitionRan;
                var _memberCount = memberBoxes != null ? memberBoxes.Count : 0;
                var _partitionMs = partitionTicks * 1000.0 / tsFreq;
                var _sortMs = sortTicks * 1000.0 / tsFreq;
                MethodProfiler.StopAndLog("SortAndCapGridCandidates", profilerTs, () =>
                    string.Format("entityId={0};isGrinding={1};count={2};effectiveCount={3};maxKeep={4};kept={5};partitionRan={6};members={7};partitionMs={8:F3};sortMs={9:F3}",
                        _Welder.EntityId, _isGrinding, _count, _effective, _maxKeep, _keep, _partitionRan, _memberCount, _partitionMs, _sortMs));
            }
        }

        private void ApplyClusterResultToSelf(ScanClusterResult result, bool updateSource)
        {
            var profilerTs = MethodProfiler.Start();
            var preTruncateWeld = 0;
            var preTruncateGrind = 0;
            try
            {
                var emitterMatrix = _Welder.WorldMatrix;
                emitterMatrix.Translation = Vector3D.Transform(Settings.CorrectedAreaOffset, emitterMatrix);
                var areaOrientedBox = new MyOrientedBoundingBoxD(Settings.CorrectedAreaBoundingBox, emitterMatrix);

                _TempPossibleWeldTargets.Clear();
                _TempPossibleGrindTargets.Clear();
                _TempPossibleFloatingTargets.Clear();

                // Filter weld candidates by own range (no pre-sort cap; truncated after sort)
                if (result.WeldCandidates != null)
                {
                    for (int i = 0; i < result.WeldCandidates.Count; i++)
                    {
                        var candidate = result.WeldCandidates[i];
                        double distance;
                        if (candidate.Block.IsInRange(ref areaOrientedBox, out distance))
                        {
                            _TempPossibleWeldTargets.Add(new TargetBlockData(candidate.Block, distance, candidate.Attributes));
                        }
                    }
                }

                // Filter grind candidates by own range (no pre-sort cap; truncated after sort)
                if (result.GrindCandidates != null)
                {
                    for (int i = 0; i < result.GrindCandidates.Count; i++)
                    {
                        var candidate = result.GrindCandidates[i];
                        double distance;
                        if (candidate.Block.IsInRange(ref areaOrientedBox, out distance))
                        {
                            _TempPossibleGrindTargets.Add(new TargetBlockData(candidate.Block, distance, candidate.Attributes));
                        }
                    }
                }

                // Filter floating candidates by own working area (OBB containment check)
                if (result.FloatingCandidates != null)
                {
                    var invRotation = MatrixD.Transpose(MatrixD.CreateFromQuaternion(areaOrientedBox.Orientation));
                    var he = areaOrientedBox.HalfExtent;
                    for (int i = 0; i < result.FloatingCandidates.Count; i++)
                    {
                        if (_TempPossibleFloatingTargets.Count >= MaxPossibleFloatingTargets) break;
                        var candidate = result.FloatingCandidates[i];
                        var localPos = Vector3D.TransformNormal(candidate.WorldPosition - areaOrientedBox.Center, invRotation);
                        if (Math.Abs(localPos.X) > he.X || Math.Abs(localPos.Y) > he.Y || Math.Abs(localPos.Z) > he.Z)
                            continue;
                        var distance = (areaOrientedBox.Center - candidate.WorldPosition).Length();
                        _TempPossibleFloatingTargets.Add(new TargetEntityData(candidate.Entity, distance));
                    }
                }

                // Pre-sort weld targets (skip if coordinator pre-sorted — truncation
                // only needs approximate order to select good candidates per grid).
                if (!result.PreSorted)
                {
                    try
                    {
                        _TempPossibleWeldTargets.Sort((a, b) =>
                        {
                            var cmp = CompareWeldPriority(a.Block, b.Block);
                            if (cmp != 0) return cmp;

                            var distCmp = Utils.Utils.CompareDistance(a.Distance, b.Distance);
                            if (distCmp != 0) return distCmp;

                            return CompareBlockStableTiebreak(a.Block, b.Block);
                        });
                    }
                    catch (Exception ex)
                    {
                        Logging.Instance.Error("Error on .Sort for cluster _TempPossibleWeldTargets. Exception: {0}", ex);
                    }
                }

                // Truncate after sorting, preserving blocks from multiple grids.
                preTruncateWeld = _TempPossibleWeldTargets.Count;
                TruncateGridAware(_TempPossibleWeldTargets, MaxPossibleWeldTargets);

                // BUG-086: Re-sort after truncation. TruncateGridAware enforces per-grid
                // minimum quotas by moving excess items to an overflow list appended at the
                // end, which disrupts the sort order. Also needed for pre-sorted results
                // to apply this member's own distances instead of the coordinator's.
                if (preTruncateWeld > MaxPossibleWeldTargets || result.PreSorted)
                {
                    try
                    {
                        _TempPossibleWeldTargets.Sort((a, b) =>
                        {
                            var cmp = CompareWeldPriority(a.Block, b.Block);
                            if (cmp != 0) return cmp;

                            var distCmp = Utils.Utils.CompareDistance(a.Distance, b.Distance);
                            if (distCmp != 0) return distCmp;

                            return CompareBlockStableTiebreak(a.Block, b.Block);
                        });
                    }
                    catch (Exception ex)
                    {
                        Logging.Instance.Error("Error on post-truncate .Sort for _TempPossibleWeldTargets. Exception: {0}", ex);
                    }
                }

                // Pre-sort grind targets (skip if coordinator pre-sorted).
                if (!result.PreSorted)
                {
                    try
                    {
                        var grindUsePriority = (Settings.Flags & SyncBlockSettings.Settings.GrindIgnorePriorityOrder) == 0;
                        var grindSmallestGridFirst = (Settings.Flags & SyncBlockSettings.Settings.GrindSmallestGridFirst) != 0;
                        var grindNearFirst = (Settings.Flags & SyncBlockSettings.Settings.GrindNearFirst) != 0;

                        // BUG-091: Build per-grid minimum-distance lookup from this member's
                        // own per-block distances so GrindSmallestGridFirst orders same-size
                        // grids by proximity of their nearest block instead of by arbitrary
                        // EntityId. Skip the pre-pass when the feature isn't enabled.
                        if (grindSmallestGridFirst)
                        {
                            _gridMinDistLookup.Clear();
                            for (int i = 0; i < _TempPossibleGrindTargets.Count; i++)
                            {
                                var c = _TempPossibleGrindTargets[i];
                                var gid = c.Block.CubeGrid.EntityId;
                                double existing;
                                if (_gridMinDistLookup.TryGetValue(gid, out existing))
                                {
                                    if (c.Distance < existing) _gridMinDistLookup[gid] = c.Distance;
                                }
                                else
                                {
                                    _gridMinDistLookup[gid] = c.Distance;
                                }
                            }
                        }

                        _TempPossibleGrindTargets.Sort((a, b) =>
                        {
                            var cmp = CompareGrindNonDistance(a.Block, a.Attributes, b.Block, b.Attributes,
                                grindUsePriority, grindSmallestGridFirst,
                                grindSmallestGridFirst ? _gridMinDistLookup : null);
                            if (cmp != 0) return cmp;

                            // FEAT-070 behavior fix: previously this site unconditionally used
                            // nearest-first distance after GrindSmallestGridFirst's tiebreakers,
                            // even when the user had GrindNearFirst off (farthest-first). The
                            // coordinator pre-sort already honored grindNearFirst here; now the
                            // member sort matches. CompareDistance preserves the existing epsilon.
                            return grindNearFirst
                                ? Utils.Utils.CompareDistance(a.Distance, b.Distance)
                                : Utils.Utils.CompareDistance(b.Distance, a.Distance);
                        });
                    }
                    catch (Exception ex)
                    {
                        Logging.Instance.Error("Error on .Sort for cluster _TempPossibleGrindTargets. Exception: {0}", ex);
                    }
                }

                // Truncate after sorting, preserving blocks from multiple grids.
                preTruncateGrind = _TempPossibleGrindTargets.Count;
                TruncateGridAware(_TempPossibleGrindTargets, MaxPossibleGrindTargets);

                // BUG-086: Re-sort after truncation (see weld comment above).
                if (preTruncateGrind > MaxPossibleGrindTargets || result.PreSorted)
                {
                    try
                    {
                        var grindUsePriority = (Settings.Flags & SyncBlockSettings.Settings.GrindIgnorePriorityOrder) == 0;
                        var grindSmallestGridFirst = (Settings.Flags & SyncBlockSettings.Settings.GrindSmallestGridFirst) != 0;
                        var grindNearFirst = (Settings.Flags & SyncBlockSettings.Settings.GrindNearFirst) != 0;

                        // BUG-091: Rebuild per-grid min-distance lookup from the (possibly
                        // truncated) list so equal-size grids are ordered by proximity.
                        // TruncateGridAware may have removed blocks, so the pre-sort dict
                        // can't be reused — rebuild fresh from what remains.
                        if (grindSmallestGridFirst)
                        {
                            _gridMinDistLookup.Clear();
                            for (int i = 0; i < _TempPossibleGrindTargets.Count; i++)
                            {
                                var c = _TempPossibleGrindTargets[i];
                                var gid = c.Block.CubeGrid.EntityId;
                                double existing;
                                if (_gridMinDistLookup.TryGetValue(gid, out existing))
                                {
                                    if (c.Distance < existing) _gridMinDistLookup[gid] = c.Distance;
                                }
                                else
                                {
                                    _gridMinDistLookup[gid] = c.Distance;
                                }
                            }
                        }

                        _TempPossibleGrindTargets.Sort((a, b) =>
                        {
                            var cmp = CompareGrindNonDistance(a.Block, a.Attributes, b.Block, b.Attributes,
                                grindUsePriority, grindSmallestGridFirst,
                                grindSmallestGridFirst ? _gridMinDistLookup : null);
                            if (cmp != 0) return cmp;

                            return grindNearFirst
                                ? Utils.Utils.CompareDistance(a.Distance, b.Distance)
                                : Utils.Utils.CompareDistance(b.Distance, a.Distance);
                        });
                    }
                    catch (Exception ex)
                    {
                        Logging.Instance.Error("Error on post-truncate .Sort for _TempPossibleGrindTargets. Exception: {0}", ex);
                    }
                }

                // Locality refinement: within distance bands, prefer blocks near the last
                // grind position. This keeps the primary nearest/farthest ordering intact
                // while adding spatial locality as a tiebreaker so the BaR grinds nearby
                // blocks in sequence instead of jumping across the grid.
                if (_HasLastGrindPosition && _TempPossibleGrindTargets.Count > 1)
                {
                    var lastPos = _LastGrindWorldPosition;
                    var bandSize = 2.0; // meters — blocks within this BaR-distance range are re-sorted by locality
                    int bandStart = 0;
                    while (bandStart < _TempPossibleGrindTargets.Count)
                    {
                        var baseDist = _TempPossibleGrindTargets[bandStart].Distance;
                        int bandEnd = bandStart + 1;
                        while (bandEnd < _TempPossibleGrindTargets.Count &&
                               Math.Abs(_TempPossibleGrindTargets[bandEnd].Distance - baseDist) < bandSize)
                        {
                            bandEnd++;
                        }
                        if (bandEnd - bandStart > 1)
                        {
                            var localLastPos = lastPos;
                            _TempPossibleGrindTargets.Sort(bandStart, bandEnd - bandStart,
                                Comparer<TargetBlockData>.Create((a, b) =>
                                {
                                    // Preserve autogrind-first ordering within the band.
                                    var autoA = (a.Attributes & TargetBlockData.AttributeFlags.Autogrind) != 0;
                                    var autoB = (b.Attributes & TargetBlockData.AttributeFlags.Autogrind) != 0;
                                    if (autoA != autoB) return autoA ? -1 : 1;

                                    var posA = a.Block.CubeGrid.GridIntegerToWorld(a.Block.Position);
                                    var posB = b.Block.CubeGrid.GridIntegerToWorld(b.Block.Position);
                                    var dA = (localLastPos - posA).LengthSquared();
                                    var dB = (localLastPos - posB).LengthSquared();
                                    return dA.CompareTo(dB);
                                }));
                        }
                        bandStart = bandEnd;
                    }
                }

                // Sort floating targets
                try
                {
                    _TempPossibleFloatingTargets.Sort((a, b) =>
                    {
                        var itemA = a.Entity;
                        var itemB = b.Entity;
                        var itemAFloating = itemA as MyFloatingObject;
                        var itemBFloating = itemB as MyFloatingObject;
                        if (itemAFloating != null && itemBFloating != null)
                        {
                            var priorityA = ComponentCollectPriority.GetPriority(itemAFloating.Item.Content.GetObjectId());
                            var priorityB = ComponentCollectPriority.GetPriority(itemBFloating.Item.Content.GetObjectId());
                            if (priorityA == priorityB)
                            {
                                return Utils.Utils.CompareDistance(a.Distance, b.Distance);
                            }
                            else return priorityA - priorityB;
                        }
                        else if (itemAFloating == null) return -1;
                        else if (itemBFloating == null) return 1;
                        return Utils.Utils.CompareDistance(a.Distance, b.Distance);
                    });
                }
                catch (Exception ex)
                {
                    Logging.Instance.Error("Error on .Sort for cluster _TempPossibleFloatingTargets. Exception: {0}", ex);
                }

                // Swap into State (same pattern as existing code)
                lock (State.PossibleWeldTargets)
                {
                    HashSet<IMySlimBlock> ignoredBlocks = null;
                    foreach (var old in State.PossibleWeldTargets)
                    {
                        if (old.Ignore)
                        {
                            if (ignoredBlocks == null)
                                ignoredBlocks = new HashSet<IMySlimBlock>();
                            ignoredBlocks.Add(old.Block);
                        }
                    }

                    State.PossibleWeldTargets.Clear();
                    State.PossibleWeldTargets.AddRange(_TempPossibleWeldTargets);

                    if (ignoredBlocks != null)
                    {
                        foreach (var target in State.PossibleWeldTargets)
                        {
                            if (ignoredBlocks.Contains(target.Block) && target.Block.IsFullIntegrity)
                                target.Ignore = true;
                        }
                    }

                    State.PossibleWeldTargets.RebuildHash();
                }
                _TempPossibleWeldTargets.Clear();

                lock (State.PossibleGrindTargets)
                {
                    State.PossibleGrindTargets.Clear();
                    State.PossibleGrindTargets.AddRange(_TempPossibleGrindTargets);
                    State.PossibleGrindTargets.RebuildHash();
                }
                _TempPossibleGrindTargets.Clear();

                lock (State.PossibleFloatingTargets)
                {
                    State.PossibleFloatingTargets.Clear();
                    State.PossibleFloatingTargets.AddRange(_TempPossibleFloatingTargets);
                    State.PossibleFloatingTargets.RebuildHash();
                }
                _TempPossibleFloatingTargets.Clear();

                // Apply sources whenever the cluster result contains them, regardless of this
                // BaR's own source timer. The coordinator's timer decides when to scan; members
                // should always consume available source data. Otherwise timer drift between
                // coordinator and members can cause members to miss source updates for minutes.
                if (result.SourcesUpdated)
                {
                    _TempPossibleSources.Clear();
                    _TempPossibleSources.AddRange(result.Sources);
                    _TempPossiblePushTargets.Clear();
                    _TempPossiblePushTargets.AddRange(result.PushTargets);

                    lock (_PossibleSources)
                    {
                        _PossibleSources.Clear();
                        _PossibleSources.AddRange(_TempPossibleSources);
                    }
                    lock (_PossiblePushTargets)
                    {
                        _PossiblePushTargets.Clear();
                        _PossiblePushTargets.AddRange(_TempPossiblePushTargets);
                    }
                    _TempPossibleSources.Clear();
                    _TempPossiblePushTargets.Clear();

                    // Only reset _PushTargetsFull when push targets actually changed
                    // (container added/removed) or after a 60s safety backoff.
                    // This prevents push retry storms where all BaRs simultaneously
                    // attempt expensive pushes into the same full containers on every
                    // 30s source rescan.
                    if (_PushTargetsFull)
                    {
                        var pushTargetsChanged = ComputePushTargetsSignature() != _PushTargetsFullSignature;
                        var backoffExpired = MyAPIGateway.Session.ElapsedPlayTime.Subtract(_PushTargetsFullSince).TotalSeconds >= 60;
                        if (pushTargetsChanged || backoffExpired)
                        {
                            _PushTargetsFull = false;
                        }
                    }
                }
            }
            finally
            {
                if (profilerTs != 0L)
                {
                    // Count unique grids for the profiler line. Iterate under the same locks
                    // as the swap above: StartAsyncClusterScan's early-exit path (BaR disabled /
                    // unpowered mid-scan) Clears these lists from the main thread, and this
                    // finally runs on the background thread without holding the swap lock.
                    _truncateGridIds.Clear();
                    lock (State.PossibleWeldTargets)
                    {
                        foreach (var t in State.PossibleWeldTargets) _truncateGridIds.Add(t.Block.CubeGrid.EntityId);
                    }
                    var weldGridCount = _truncateGridIds.Count;

                    _truncateGridIds.Clear();
                    lock (State.PossibleGrindTargets)
                    {
                        foreach (var t in State.PossibleGrindTargets) _truncateGridIds.Add(t.Block.CubeGrid.EntityId);
                    }
                    var grindGridCount = _truncateGridIds.Count;

                    MethodProfiler.StopAndLog("ApplyClusterResultToSelf", profilerTs, () =>
                        string.Format("entityId={0};weldTargets={1}(pre={2},grids={3});grindTargets={4}(pre={5},grids={6});floatingTargets={7}",
                            _Welder.EntityId,
                            State.PossibleWeldTargets.CurrentCount, preTruncateWeld, weldGridCount,
                            State.PossibleGrindTargets.CurrentCount, preTruncateGrind, grindGridCount,
                            State.PossibleFloatingTargets.CurrentCount));
                }
            }
        }
    }
}
