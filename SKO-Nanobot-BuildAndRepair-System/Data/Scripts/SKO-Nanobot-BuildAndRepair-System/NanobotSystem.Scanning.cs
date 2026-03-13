using Sandbox.Definitions;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using SKONanobotBuildAndRepairSystem.Cluster;
using SKONanobotBuildAndRepairSystem.Handlers;
using SKONanobotBuildAndRepairSystem.Helpers;
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
                var _visitedCount = visited.Count;
                var _sourceCount = possibleSources.Count;
                MethodProfiler.StopAndLog("AsyncScanForSources", profilerTs, () =>
                    string.Format("entityId={0};gridsVisited={1};sourcesFound={2}",
                        _Welder.EntityId, _visitedCount, _sourceCount));
            }
        }

        private List<IMySlimBlock> GetBlocksFromCache(IMyCubeGrid grid, bool isGrinding = false)
        {
            var profilerTs = MethodProfiler.Start();
            var gridId = grid.EntityId;
            var cacheHit = false;

            try
            {
                var playTime = MyAPIGateway.Session.ElapsedPlayTime;
                TimeSpan cachedTime;
                List<IMySlimBlock> list;
                if (CachedBlocksTime.TryGetValue(gridId, out cachedTime))
                {
                    if (playTime.Subtract(cachedTime).TotalSeconds < SortedCacheTtlSeconds)
                    {
                        if (CachedBlocks.TryGetValue(gridId, out list))
                        {
                            cacheHit = true;
                            return list;
                        }
                    }
                }

                // Get raw blocks from the shared cache (avoids redundant grid.GetBlocks() calls across BaRs).
                list = SharedGridBlockCache.GetBlocks(grid);

                // Sort the copy per this BaR's priority and distance settings.
                list.SortWithPriorityAndDistance(this, isGrinding);

                CachedBlocksTime[gridId] = playTime;
                CachedBlocks[gridId] = list;

                return list;
            }
            finally
            {
                var _gridId = gridId;
                var _hit = cacheHit;
                MethodProfiler.StopAndLog("GetBlocksFromCache", profilerTs, () =>
                    string.Format("entityId={0};gridId={1};cacheHit={2}", _Welder.EntityId, _gridId, _hit));
            }
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
        /// Check if the given slim block is a weld target. When skipRangeCheck is true, skips IsInRange and sets distance to 0.
        /// </summary>
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
                   block.NeedRepair((Settings.WeldOptions & AutoWeldOptions.FunctionalOnly) != 0))
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

            if ((MyAPIGateway.Session.SessionSettings.Scenario || MyAPIGateway.Session.SessionSettings.ScenarioEditMode) && !MyAPIGateway.Session.SessionSettings.DestructibleBlocks)
            {
                return false;
            }

            if (block.IsProjected())
                return false;

            var cubeGrid = block.CubeGrid as MyCubeGrid;
            if (cubeGrid != null && (!cubeGrid.DestructibleBlocks || cubeGrid.Immune))
            {
                return false;
            }

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
                var integrityRation = block.Integrity / block.MaxIntegrity;

                if (autoGrind && ((autoGrindOptions & AutoGrindOptions.DisableOnly) != 0))
                {
                    autoGrind = block.FatBlock != null && integrityRation > criticalIntegrityRatio;
                }

                if (autoGrind && ((autoGrindOptions & AutoGrindOptions.HackOnly) != 0))
                {
                    autoGrind = block.FatBlock != null && integrityRation > ownershipIntegrityRatio;
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

            var isGrindingMode = Settings.WorkMode == WorkModes.GrindOnly;
            var isGrinding = State.Grinding || State.NeedGrinding || (State.Transporting && isGrindingMode) || isGrindingMode;
            var newBlocks = GetBlocksFromCache(cubeGrid, isGrinding);

            foreach (var slimBlock in newBlocks)
            {
                if (ShouldStopScan(clusterWeldTargets, clusterGrindTargets, null, maxWeld, maxGrind, 0)) break;

                AsyncAddBlockIfTarget(ref areaBox, useIgnoreColor, ref ignoreColor, useGrindColor, ref grindColor, autoGrindRelation, autoGrindOptions, slimBlock, clusterWeldTargets, clusterGrindTargets, maxWeld, maxGrind, skipRangeCheck);

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

                if (clusterWeldTargets != null && ((Settings.Flags & SyncBlockSettings.Settings.AllowBuild) != 0))
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

                            var projectedCubeGrid = projector.ProjectedGrid;
                            if (projectedCubeGrid != null && !grids.Contains(projectedCubeGrid))
                            {
                                grids.Add(projectedCubeGrid);
                                var projectedBlocks = GetBlocksFromCache(projectedCubeGrid, isGrinding);

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
                                            if (clusterWeldTargets.Count < maxWeld)
                                            {
                                                clusterWeldTargets.Add(new ClusterTargetCandidate(block, TargetBlockData.AttributeFlags.Projected));
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
            MethodProfiler.StopAndLog("AsyncAddBlocksOfGrid", profilerTs, () =>
                string.Format("entityId={0};gridId={1};blocks={2};weldTargets={3};grindTargets={4}",
                    _Welder.EntityId, cubeGrid.EntityId, newBlocks.Count,
                    clusterWeldTargets != null ? clusterWeldTargets.Count : -1,
                    clusterGrindTargets != null ? clusterGrindTargets.Count : -1));
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
            MethodProfiler.StopAndLog("AsyncAddBlocksOfBox", profilerTs, () =>
                string.Format("entityId={0};entities={1};weldTargets={2};grindTargets={3};floatTargets={4}",
                    _Welder.EntityId,
                    entityInRange != null ? entityInRange.Count : 0,
                    clusterWeldTargets != null ? clusterWeldTargets.Count : -1,
                    clusterGrindTargets != null ? clusterGrindTargets.Count : -1,
                    clusterFloatingTargets != null ? clusterFloatingTargets.Count : -1));
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
                lock (State.PossibleWeldTargets) { State.PossibleWeldTargets.Clear(); State.PossibleWeldTargets.RebuildHash(); }
                lock (State.PossibleGrindTargets) { State.PossibleGrindTargets.Clear(); State.PossibleGrindTargets.RebuildHash(); }
                lock (State.PossibleFloatingTargets) { State.PossibleFloatingTargets.Clear(); State.PossibleFloatingTargets.RebuildHash(); }
                _AsyncUpdateSourcesAndTargetsRunning = false;
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

                // Increased caps for cluster scan: serve multiple members
                var memberCount = cluster.Members.Count;
                var capMultiplier = memberCount < 4 ? memberCount : 4;
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
                }
                catch (Exception ex)
                {
                    Logging.Instance.Error("BuildAndRepairSystemBlock {0}: AsyncClusterScan exception: {1}", Logging.BlockName(_Welder, Logging.BlockNameOptions.None), ex);
                }

                // Publish shared result (atomic reference swap)
                cluster.SetResult(result);

                // Coordinator is also a member — apply own range/distance filtering
                ApplyClusterResultToSelf(result, updateSource);

                MissedResultCycles = 0;
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
                MethodProfiler.StopAndLog("AsyncClusterScan", profilerTs, () =>
                    string.Format("entityId={0};updateSource={1};clusterMembers={2}",
                        _Welder.EntityId, updateSource, cluster.Members.Count));
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
                lock (State.PossibleWeldTargets) { State.PossibleWeldTargets.Clear(); State.PossibleWeldTargets.RebuildHash(); }
                lock (State.PossibleGrindTargets) { State.PossibleGrindTargets.Clear(); State.PossibleGrindTargets.RebuildHash(); }
                lock (State.PossibleFloatingTargets) { State.PossibleFloatingTargets.Clear(); State.PossibleFloatingTargets.RebuildHash(); }
                _AsyncUpdateSourcesAndTargetsRunning = false;
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
                MethodProfiler.StopAndLog("AsyncApplyClusterResults", profilerTs, () =>
                    string.Format("entityId={0};updateSource={1};missed={2}",
                        _Welder.EntityId, updateSource, MissedResultCycles));
            }
        }

        /// <summary>
        /// Applies a ScanClusterResult to this BaR by filtering candidates through own IsInRange,
        /// computing distances, sorting, and swapping into State. Used by both coordinator and members.
        /// </summary>
        private void ApplyClusterResultToSelf(ScanClusterResult result, bool updateSource)
        {
            var profilerTs = MethodProfiler.Start();
            try
            {
                var emitterMatrix = _Welder.WorldMatrix;
                emitterMatrix.Translation = Vector3D.Transform(Settings.CorrectedAreaOffset, emitterMatrix);
                var areaOrientedBox = new MyOrientedBoundingBoxD(Settings.CorrectedAreaBoundingBox, emitterMatrix);

                _TempPossibleWeldTargets.Clear();
                _TempPossibleGrindTargets.Clear();
                _TempPossibleFloatingTargets.Clear();

                // Filter weld candidates by own range
                if (result.WeldCandidates != null)
                {
                    for (int i = 0; i < result.WeldCandidates.Count; i++)
                    {
                        if (_TempPossibleWeldTargets.Count >= MaxPossibleWeldTargets) break;
                        var candidate = result.WeldCandidates[i];
                        double distance;
                        if (candidate.Block.IsInRange(ref areaOrientedBox, out distance))
                        {
                            _TempPossibleWeldTargets.Add(new TargetBlockData(candidate.Block, distance, candidate.Attributes));
                        }
                    }
                }

                // Filter grind candidates by own range
                if (result.GrindCandidates != null)
                {
                    for (int i = 0; i < result.GrindCandidates.Count; i++)
                    {
                        if (_TempPossibleGrindTargets.Count >= MaxPossibleGrindTargets) break;
                        var candidate = result.GrindCandidates[i];
                        double distance;
                        if (candidate.Block.IsInRange(ref areaOrientedBox, out distance))
                        {
                            _TempPossibleGrindTargets.Add(new TargetBlockData(candidate.Block, distance, candidate.Attributes));
                        }
                    }
                }

                // Filter floating candidates by distance to own area center
                if (result.FloatingCandidates != null)
                {
                    for (int i = 0; i < result.FloatingCandidates.Count; i++)
                    {
                        if (_TempPossibleFloatingTargets.Count >= MaxPossibleFloatingTargets) break;
                        var candidate = result.FloatingCandidates[i];
                        var distance = (areaOrientedBox.Center - candidate.WorldPosition).Length();
                        _TempPossibleFloatingTargets.Add(new TargetEntityData(candidate.Entity, distance));
                    }
                }

                // Sort weld targets by priority then distance
                try
                {
                    _TempPossibleWeldTargets.Sort((a, b) =>
                    {
                        var priorityA = BlockWeldPriority.GetPriority(a.Block);
                        var priorityB = BlockWeldPriority.GetPriority(b.Block);
                        if (priorityA != priorityB) return priorityA - priorityB;

                        var distCmp = Utils.Utils.CompareDistance(a.Distance, b.Distance);
                        if (distCmp != 0) return distCmp;

                        var gridCmp = a.Block.CubeGrid.EntityId.CompareTo(b.Block.CubeGrid.EntityId);
                        if (gridCmp != 0) return gridCmp;
                        var posA = a.Block.Position;
                        var posB = b.Block.Position;
                        if (posA.X != posB.X) return posA.X - posB.X;
                        if (posA.Y != posB.Y) return posA.Y - posB.Y;
                        return posA.Z - posB.Z;
                    });
                }
                catch (Exception ex)
                {
                    Logging.Instance.Error("Error on .Sort for cluster _TempPossibleWeldTargets. Exception: {0}", ex);
                }

                // Sort grind targets
                try
                {
                    var grindUsePriority = (Settings.Flags & SyncBlockSettings.Settings.GrindIgnorePriorityOrder) == 0;
                    var grindSmallestGridFirst = (Settings.Flags & SyncBlockSettings.Settings.GrindSmallestGridFirst) != 0;
                    var grindNearFirst = (Settings.Flags & SyncBlockSettings.Settings.GrindNearFirst) != 0;
                    _TempPossibleGrindTargets.Sort((a, b) =>
                    {
                        if ((a.Attributes & TargetBlockData.AttributeFlags.Autogrind) == (b.Attributes & TargetBlockData.AttributeFlags.Autogrind))
                        {
                            if (grindUsePriority)
                            {
                                var priorityA = BlockGrindPriority.GetPriority(a.Block);
                                var priorityB = BlockGrindPriority.GetPriority(b.Block);
                                if (priorityA != priorityB)
                                    return priorityA - priorityB;
                            }

                            if (grindSmallestGridFirst)
                            {
                                var res = ((MyCubeGrid)a.Block.CubeGrid).BlocksCount - ((MyCubeGrid)b.Block.CubeGrid).BlocksCount;
                                return res != 0 ? res : Utils.Utils.CompareDistance(a.Distance, b.Distance);
                            }
                            if (grindNearFirst) return Utils.Utils.CompareDistance(a.Distance, b.Distance);
                            return Utils.Utils.CompareDistance(b.Distance, a.Distance);
                        }
                        else if ((a.Attributes & TargetBlockData.AttributeFlags.Autogrind) != 0) return -1;
                        else if ((b.Attributes & TargetBlockData.AttributeFlags.Autogrind) != 0) return 1;
                        return 0;
                    });
                }
                catch (Exception ex)
                {
                    Logging.Instance.Error("Error on .Sort for cluster _TempPossibleGrindTargets. Exception: {0}", ex);
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

                if (updateSource && result.SourcesUpdated)
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
                    _PushTargetsFull = false;
                }
            }
            finally
            {
                MethodProfiler.StopAndLog("ApplyClusterResultToSelf", profilerTs, () =>
                    string.Format("entityId={0};weldTargets={1};grindTargets={2};floatingTargets={3}",
                        _Welder.EntityId,
                        State.PossibleWeldTargets.CurrentCount,
                        State.PossibleGrindTargets.CurrentCount,
                        State.PossibleFloatingTargets.CurrentCount));
            }
        }
    }
}
