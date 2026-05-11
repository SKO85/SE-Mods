using Sandbox.Definitions;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using SKONanobotBuildAndRepairSystem.Cluster;
using SKONanobotBuildAndRepairSystem.Extensions;
using SKONanobotBuildAndRepairSystem.Handlers;
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

namespace SKONanobotBuildAndRepairSystem
{
    // Block-level scanning primitives split out of NanobotSystem.Scanning.cs.
    public partial class NanobotSystem
    {
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
        /// BUG-053: returns true when the projector's grid intersects a safe zone
        /// that blocks building (proj.Build fails for every projected block on it).
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

        /// <summary>
        /// Projector cold-start detection: checks if any projector on the BaR's
        /// own grid, connected grids, or nearby grids (BoundingBox mode) has
        /// BuildableBlocksCount > 0.
        /// Called once per second on the main thread, only when idle.
        /// </summary>
        private static readonly List<Sandbox.ModAPI.IMyProjector> _projectorScratch = new List<Sandbox.ModAPI.IMyProjector>();
        private static readonly Func<Sandbox.ModAPI.IMyProjector, bool> _buildableProjectorFilter =
            p => p != null && p.IsProjecting && p.BuildableBlocksCount > 0;

        private bool HasBuildableProjectorOnGrid()
        {
            try
            {
                // BUG-132: use logical grid-group's typed projector lookup instead of per-block BFS.
                var helper = MyAPIGateway.TerminalActionsHelper;
                if (helper != null && _Welder.CubeGrid != null)
                {
                    var terminalSystem = helper.GetTerminalSystemForGrid(_Welder.CubeGrid);
                    if (terminalSystem != null)
                    {
                        _projectorScratch.Clear();
                        terminalSystem.GetBlocksOfType<Sandbox.ModAPI.IMyProjector>(_projectorScratch, _buildableProjectorFilter);
                        var found = _projectorScratch.Count > 0;
                        _projectorScratch.Clear();
                        if (found) return true;
                    }
                }

                // Phase 2: BoundingBox mode — also check unconnected grids in working area.
                // The terminal-system path above only covers Logically-linked grids; nearby
                // free-floating grids share no terminal system, so we still need an entity walk.
                if (Settings.SearchMode == SearchModes.BoundingBox)
                {
                    var ownGroupGridIds = new HashSet<long>();
                    if (helper != null && _Welder.CubeGrid != null)
                    {
                        var ownGroup = _Welder.CubeGrid.GetGridGroup(GridLinkTypeEnum.Logical);
                        if (ownGroup != null)
                        {
                            var groupGrids = new List<IMyCubeGrid>();
                            ownGroup.GetGrids(groupGrids);
                            for (var i = 0; i < groupGrids.Count; i++)
                            {
                                if (groupGrids[i] != null) ownGroupGridIds.Add(groupGrids[i].EntityId);
                            }
                        }
                    }

                    var emitterMatrix = _Welder.WorldMatrix;
                    emitterMatrix.Translation = Vector3D.Transform(Settings.CorrectedAreaOffset, emitterMatrix);
                    var areaBox = new MyOrientedBoundingBoxD(Settings.CorrectedAreaBoundingBox, emitterMatrix);
                    var aabb = areaBox.GetAABB();

                    List<IMyEntity> entities;
                    lock (MyAPIGateway.Entities)
                    {
                        entities = MyAPIGateway.Entities.GetTopMostEntitiesInBox(ref aabb);
                    }
                    if (entities != null)
                    {
                        foreach (var entity in entities)
                        {
                            var nearbyGrid = entity as IMyCubeGrid;
                            if (nearbyGrid == null || ownGroupGridIds.Contains(nearbyGrid.EntityId)) continue;

                            if (helper != null)
                            {
                                var nearbyTerminal = helper.GetTerminalSystemForGrid(nearbyGrid);
                                if (nearbyTerminal != null)
                                {
                                    _projectorScratch.Clear();
                                    nearbyTerminal.GetBlocksOfType<Sandbox.ModAPI.IMyProjector>(_projectorScratch, _buildableProjectorFilter);
                                    var found = _projectorScratch.Count > 0;
                                    _projectorScratch.Clear();
                                    if (found) return true;
                                }
                            }
                        }
                    }
                }
            }
            catch { }
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

                // BUG-053: skip if the projector's grid is in a building-blocked safe zone.
                if (IsProjectorGridBuildBlocked(projector))
                    return false;

                if (((Settings.Flags & SyncBlockSettings.Settings.AllowBuild) != 0) &&
                   (!useGrindColor || !IsColorNearlyEquals(grindColor, colorMask)) &&
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

                // BUG-112: NeedRepair first short-circuits for full-integrity blocks
                // before the more expensive IsRelationAllowed4Welding engine call.
                if (block.NeedRepair(Settings.WeldOptions) &&
                   (!useIgnoreColor || !IsColorNearlyEquals(ignoreColor, colorMask)) && (!useGrindColor || !IsColorNearlyEquals(grindColor, colorMask)) &&
                   BlockWeldPriority.GetEnabled(block) &&
                   IsRelationAllowed4Welding(block))
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
        /// BUG-166: GridScanCache key hash; excludes home-grid EntityId so clusters
        /// with identical scan settings on different home grids share cache entries.
        /// </summary>
        private int ComputeScanParamsHash(bool useIgnoreColor, uint ignoreColor, bool useGrindColor, uint grindColor, AutoGrindRelation autoGrindRelation, AutoGrindOptions autoGrindOptions)
        {
            unchecked
            {
                int hash = (int)2166136261;
                hash = (hash ^ (useIgnoreColor ? 1 : 0)) * 16777619;
                if (useIgnoreColor) hash = (hash ^ (int)ignoreColor) * 16777619;
                hash = (hash ^ (useGrindColor ? 1 : 0)) * 16777619;
                if (useGrindColor) hash = (hash ^ (int)grindColor) * 16777619;
                hash = (hash ^ (int)autoGrindRelation) * 16777619;
                hash = (hash ^ (int)autoGrindOptions) * 16777619;
                hash = (hash ^ (int)Settings.WeldOptions) * 16777619;
                hash = (hash ^ ((Settings.Flags & SyncBlockSettings.Settings.AllowBuild) != 0 ? 1 : 0)) * 16777619;
                hash = (hash ^ (int)(_Welder.OwnerId >> 32)) * 16777619;
                hash = (hash ^ (int)_Welder.OwnerId) * 16777619;
                // Priority strings — hash their .NET hash codes (stable within session).
                hash = (hash ^ (Settings.WeldPriority != null ? Settings.WeldPriority.GetHashCode() : 0)) * 16777619;
                hash = (hash ^ (Settings.GrindPriority != null ? Settings.GrindPriority.GetHashCode() : 0)) * 16777619;
                // Safe zone state — different gates produce different filter results.
                hash = (hash ^ (State.SafeZoneAllowsWelding ? 1 : 0)) * 16777619;
                hash = (hash ^ (State.SafeZoneAllowsGrinding ? 1 : 0)) * 16777619;
                hash = (hash ^ (State.SafeZoneAllowsBuildingProjections ? 1 : 0)) * 16777619;
                return hash;
            }
        }
        // CON-1: shared connection-traversal core for AsyncAddBlocksOfGrid. Examines the
        // passed slim block and, if it is a mechanical-connection / attachable-top /
        // ship-connector fat block, recurses into AsyncAddBlocksOfGrid for the connected
        // grid (guarded against ShouldStopScan). Returns true when the block matched one
        // of those types so callers in the slow path can `continue;` past the projector
        // branch. Replaces four near-identical traversal copies that lived in
        // AsyncAddBlocksOfGrid's empty-grid fast path, both-caps fast path, cache-hit
        // fast path, and main slow path.
        private bool TraverseConnectedGrid(
            IMySlimBlock slimBlock,
            ref MyOrientedBoundingBoxD areaBox,
            bool useIgnoreColor, ref uint ignoreColor,
            bool useGrindColor, ref uint grindColor,
            AutoGrindRelation autoGrindRelation, AutoGrindOptions autoGrindOptions,
            List<IMyCubeGrid> grids,
            List<ClusterTargetCandidate> clusterWeldTargets,
            List<ClusterTargetCandidate> clusterGrindTargets,
            int maxWeld, int maxGrind,
            bool skipRangeCheck)
        {
            var fatBlock = slimBlock.FatBlock;
            if (fatBlock == null) return false;

            var mechanical = fatBlock as Sandbox.ModAPI.IMyMechanicalConnectionBlock;
            if (mechanical != null)
            {
                if (mechanical.TopGrid != null
                    && !ShouldStopScan(clusterWeldTargets, clusterGrindTargets, null, maxWeld, maxGrind, 0))
                {
                    AsyncAddBlocksOfGrid(ref areaBox, useIgnoreColor, ref ignoreColor, useGrindColor, ref grindColor, autoGrindRelation, autoGrindOptions, mechanical.TopGrid, grids, clusterWeldTargets, clusterGrindTargets, maxWeld, maxGrind, skipRangeCheck);
                }
                return true;
            }

            var attachable = fatBlock as Sandbox.ModAPI.IMyAttachableTopBlock;
            if (attachable != null)
            {
                if (attachable.Base != null && attachable.Base.CubeGrid != null
                    && !ShouldStopScan(clusterWeldTargets, clusterGrindTargets, null, maxWeld, maxGrind, 0))
                {
                    AsyncAddBlocksOfGrid(ref areaBox, useIgnoreColor, ref ignoreColor, useGrindColor, ref grindColor, autoGrindRelation, autoGrindOptions, attachable.Base.CubeGrid, grids, clusterWeldTargets, clusterGrindTargets, maxWeld, maxGrind, skipRangeCheck);
                }
                return true;
            }

            var connector = fatBlock as Sandbox.ModAPI.IMyShipConnector;
            if (connector != null)
            {
                if (connector.Status == Sandbox.ModAPI.Ingame.MyShipConnectorStatus.Connected
                    && connector.OtherConnector != null
                    && !ShouldStopScan(clusterWeldTargets, clusterGrindTargets, null, maxWeld, maxGrind, 0))
                {
                    AsyncAddBlocksOfGrid(ref areaBox, useIgnoreColor, ref ignoreColor, useGrindColor, ref grindColor, autoGrindRelation, autoGrindOptions, connector.OtherConnector.CubeGrid, grids, clusterWeldTargets, clusterGrindTargets, maxWeld, maxGrind, skipRangeCheck);
                }
                return true;
            }

            return false;
        }

        // PERF-1: per-instance free-list of fatBlocks scratch buffers. Per-BaR scan
        // dispatch is serialised, but AsyncAddBlocksOfGrid recurses into itself for each
        // connected grid — the outer call's list is mid-foreach when the inner call
        // borrows. A single instance field would be clobbered, so the pool acts as a
        // recursion-safe stack. Lazy-initialised; steady-state size = max recursion
        // depth (typically 1-3 for connected mechanical chains).
        private List<List<IMySlimBlock>> _fatBlocksPool;

        private List<IMySlimBlock> RentFatBlocksList()
        {
            if (_fatBlocksPool != null && _fatBlocksPool.Count > 0)
            {
                var idx = _fatBlocksPool.Count - 1;
                var list = _fatBlocksPool[idx];
                _fatBlocksPool.RemoveAt(idx);
                list.Clear();
                return list;
            }
            return new List<IMySlimBlock>();
        }

        private void ReturnFatBlocksList(List<IMySlimBlock> list)
        {
            if (list == null) return;
            list.Clear();
            if (_fatBlocksPool == null) _fatBlocksPool = new List<List<IMySlimBlock>>();
            _fatBlocksPool.Add(list);
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
                    // FEAT-073: Only iterate fat blocks for connection discovery.
                    // Connection blocks (mechanical, attachable, connector) always have
                    // a FatBlock. Skipping slim-only blocks (armor etc.) avoids iterating
                    // hundreds of blocks on large grids just to find a few connections.
                    // PERF-1: rent a buffer from the recursion-safe pool instead of
                    // allocating a fresh List per call. CON-1: per-block traversal lifted
                    // into TraverseConnectedGrid to remove the four-way duplication.
                    var fatBlocks = RentFatBlocksList();
                    try
                    {
                        cubeGrid.GetBlocks(fatBlocks, _fatBlockFilter);
                        for (int i = 0; i < fatBlocks.Count; i++)
                        {
                            if (ShouldStopScan(clusterWeldTargets, clusterGrindTargets, null, maxWeld, maxGrind, 0)) break;
                            TraverseConnectedGrid(fatBlocks[i], ref areaBox, useIgnoreColor, ref ignoreColor, useGrindColor, ref grindColor, autoGrindRelation, autoGrindOptions, grids, clusterWeldTargets, clusterGrindTargets, maxWeld, maxGrind, skipRangeCheck);
                        }
                    }
                    finally { ReturnFatBlocksList(fatBlocks); }

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

            // BUG-096: enforce per-type cluster caps at grid entry. When one type's cluster
            // cap is hit, null its list so the per-block path no-ops for that type while the
            // other type's per-grid sort still sees full input. Cap-skipped flags suppress
            // the empty-grid-cache update below.
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

            // FEAT-040: grid-level containment fast-path skips per-block OBB checks
            // when the grid's world AABB fits inside the working area.
            var blockSkipRange = skipRangeCheck;
            if (!blockSkipRange)
            {
                var gridAABB = cubeGrid.WorldAABB;
                if (areaBox.Contains(ref gridAABB) == ContainmentType.Contains)
                {
                    blockSkipRange = true;
                }
            }

            // BUG-161: when both target lists are nulled, iterate only fat blocks
            // for connection traversal (skip the full slim-block walk).
            if (weldTargetsForGrid == null && grindTargetsForGrid == null)
            {
                // PERF-1 + CON-1: rent pooled buffer; traversal extracted into helper.
                var fatBlocks = RentFatBlocksList();
                int fatNewCount;
                try
                {
                    cubeGrid.GetBlocks(fatBlocks, _fatBlockFilter);
                    fatNewCount = fatBlocks.Count;
                    for (int i = 0; i < fatBlocks.Count; i++)
                    {
                        if (ShouldStopScan(clusterWeldTargets, clusterGrindTargets, null, maxWeld, maxGrind, 0)) break;
                        TraverseConnectedGrid(fatBlocks[i], ref areaBox, useIgnoreColor, ref ignoreColor, useGrindColor, ref grindColor, autoGrindRelation, autoGrindOptions, grids, clusterWeldTargets, clusterGrindTargets, maxWeld, maxGrind, skipRangeCheck);
                    }
                }
                finally { ReturnFatBlocksList(fatBlocks); }

                if (profilerTs != 0L)
                {
                    var _gridEidFast = cubeGrid.EntityId;
                    var _fatCount = fatNewCount;
                    var _clusterMembersFast = _ClusterMemberAreaCenters != null ? _ClusterMemberAreaCenters.Count : 0;
                    MethodProfiler.StopAndLog("AsyncAddBlocksOfGrid", profilerTs, () =>
                        string.Format("entityId={0};gridId={1};blocks={2};weldTargets={3};grindTargets={4};weldAdded=0;grindAdded=0;weldCapSkip={5};grindCapSkip={6};skipRange={7};clusterMembers={8};fastPath=bothCapped",
                            _Welder.EntityId, _gridEidFast, _fatCount,
                            clusterWeldTargets != null ? clusterWeldTargets.Count : -1,
                            clusterGrindTargets != null ? clusterGrindTargets.Count : -1,
                            weldCapSkipped, grindCapSkipped, blockSkipRange, _clusterMembersFast));
                }
                return;
            }

            // BUG-166: per-grid scan cache; only enabled for skipRangeCheck=true
            // (solo BaRs have position-dependent output that can't be cached).
            var paramsHash = 0;
            var cacheEligible = skipRangeCheck;
            if (cacheEligible)
            {
                paramsHash = ComputeScanParamsHash(useIgnoreColor, ignoreColor, useGrindColor, grindColor, autoGrindRelation, autoGrindOptions);
                var cached = GridScanCache.TryGet(gridEntityId, paramsHash);
                if (cached != null)
                {
                    var cacheWeldAdded = 0;
                    var cacheGrindAdded = 0;
                    if (weldTargetsForGrid != null && cached.WeldCandidates != null)
                    {
                        for (int i = 0; i < cached.WeldCandidates.Count; i++)
                        {
                            if (weldTargetsForGrid.Count >= maxWeld) break;
                            weldTargetsForGrid.Add(cached.WeldCandidates[i]);
                            cacheWeldAdded++;
                        }
                    }
                    if (grindTargetsForGrid != null && cached.GrindCandidates != null)
                    {
                        for (int i = 0; i < cached.GrindCandidates.Count; i++)
                        {
                            if (grindTargetsForGrid.Count >= maxGrind) break;
                            grindTargetsForGrid.Add(cached.GrindCandidates[i]);
                            cacheGrindAdded++;
                        }
                    }

                    // Update empty-grid cache the same way the slow path would have.
                    var weldAfterCache = clusterWeldTargets != null ? clusterWeldTargets.Count : 0;
                    var grindAfterCache = clusterGrindTargets != null ? clusterGrindTargets.Count : 0;
                    if (weldAfterCache == weldBefore && grindAfterCache == grindBefore && !weldCapSkipped && !grindCapSkipped)
                    {
                        _EmptyGridCache[gridEntityId] = playTime;
                    }
                    else if (weldAfterCache != weldBefore || grindAfterCache != grindBefore)
                    {
                        TimeSpan dummy;
                        _EmptyGridCache.TryRemove(gridEntityId, out dummy);
                    }

                    // Cache only covers this grid's targets; still traverse connections.
                    var fatBlocksHit = RentFatBlocksList();
                    try
                    {
                        cubeGrid.GetBlocks(fatBlocksHit, _fatBlockFilter);
                        for (int i = 0; i < fatBlocksHit.Count; i++)
                        {
                            if (ShouldStopScan(clusterWeldTargets, clusterGrindTargets, null, maxWeld, maxGrind, 0)) break;
                            TraverseConnectedGrid(fatBlocksHit[i], ref areaBox, useIgnoreColor, ref ignoreColor, useGrindColor, ref grindColor, autoGrindRelation, autoGrindOptions, grids, clusterWeldTargets, clusterGrindTargets, maxWeld, maxGrind, skipRangeCheck);
                        }
                    }
                    finally { ReturnFatBlocksList(fatBlocksHit); }

                    if (profilerTs != 0L)
                    {
                        var _gridEid = cubeGrid.EntityId;
                        var _cacheW = cacheWeldAdded;
                        var _cacheG = cacheGrindAdded;
                        var _clusterMembersHit = _ClusterMemberAreaCenters != null ? _ClusterMemberAreaCenters.Count : 0;
                        MethodProfiler.StopAndLog("AsyncAddBlocksOfGrid", profilerTs, () =>
                            string.Format("entityId={0};gridId={1};blocks=cached;weldTargets={2};grindTargets={3};weldAdded={4};grindAdded={5};weldCapSkip={6};grindCapSkip={7};skipRange={8};clusterMembers={9};fastPath=cached",
                                _Welder.EntityId, _gridEid,
                                clusterWeldTargets != null ? clusterWeldTargets.Count : -1,
                                clusterGrindTargets != null ? clusterGrindTargets.Count : -1,
                                _cacheW, _cacheG, weldCapSkipped, grindCapSkipped, blockSkipRange, _clusterMembersHit));
                    }
                    return;
                }
            }

            var newBlocks = GetBlocksFromCache(cubeGrid);

            foreach (var slimBlock in newBlocks)
            {
                if (ShouldStopScan(clusterWeldTargets, clusterGrindTargets, null, maxWeld, maxGrind, 0)) break;

                // BUG-094: int.MaxValue lets every qualifying block enter the list;
                // the per-grid budget is applied later by SortAndCapGridCandidates.
                AsyncAddBlockIfTarget(ref areaBox, useIgnoreColor, ref ignoreColor, useGrindColor, ref grindColor, autoGrindRelation, autoGrindOptions, slimBlock, weldTargetsForGrid, grindTargetsForGrid, int.MaxValue, int.MaxValue, blockSkipRange);

                // CON-1: shared connection traversal. Returns true when the slim block
                // is a mechanical / attachable / connector fat block (regardless of
                // whether it actually recursed) so we skip the projector branch below.
                if (TraverseConnectedGrid(slimBlock, ref areaBox, useIgnoreColor, ref ignoreColor, useGrindColor, ref grindColor, autoGrindRelation, autoGrindOptions, grids, clusterWeldTargets, clusterGrindTargets, maxWeld, maxGrind, skipRangeCheck))
                    continue;

                var fatBlock = slimBlock.FatBlock;
                if (fatBlock == null) continue;

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

                            // BUG-053: skip if the projector's grid is in a building-blocked safe zone.
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

                                    if (BlockWeldPriority.GetEnabled(block) && block.CanBuild(false))
                                    {
                                        double distance;
                                        if (skipRangeCheck || block.IsInRange(ref areaBox, out distance))
                                        {
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

            // Per-grid budget: sort + keep top-N when this grid contributed more than max.
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

            // BUG-096: don't mark cap-skipped grids empty — we didn't evaluate them.
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

            // BUG-166: cache this grid's contribution; skip if cap-skipped (incomplete).
            if (cacheEligible && !weldCapSkipped && !grindCapSkipped)
            {
                List<ClusterTargetCandidate> weldSlice = null;
                List<ClusterTargetCandidate> grindSlice = null;
                if (clusterWeldTargets != null && weldAfter > weldBefore)
                {
                    weldSlice = new List<ClusterTargetCandidate>(weldAfter - weldBefore);
                    for (int i = weldBefore; i < weldAfter; i++) weldSlice.Add(clusterWeldTargets[i]);
                }
                if (clusterGrindTargets != null && grindAfter > grindBefore)
                {
                    grindSlice = new List<ClusterTargetCandidate>(grindAfter - grindBefore);
                    for (int i = grindBefore; i < grindAfter; i++) grindSlice.Add(clusterGrindTargets[i]);
                }
                GridScanCache.Set(gridEntityId, paramsHash, weldSlice, grindSlice);
            }

            if (profilerTs != 0L)
            {
                var _gridName = cubeGrid.DisplayName;
                var _gridEid = cubeGrid.EntityId;

                // Capture end timestamp before StopAndLog so grid cost doesn't include logging overhead.
                var endTs = System.Diagnostics.Stopwatch.GetTimestamp();

                var _clusterMembers = _ClusterMemberAreaCenters != null ? _ClusterMemberAreaCenters.Count : 0;
                // BUG-096 diagnostics: cap-skip flags + per-grid added counts.
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
        /// Scans entities inside the bounding box for target blocks and floating objects.
        /// </summary>
        private void AsyncAddBlocksOfBox(ref MyOrientedBoundingBoxD areaBox, bool useIgnoreColor, ref uint ignoreColor, bool useGrindColor, ref uint grindColor, AutoGrindRelation autoGrindRelation, AutoGrindOptions autoGrindOptions, List<IMyCubeGrid> grids, List<ClusterTargetCandidate> clusterWeldTargets, List<ClusterTargetCandidate> clusterGrindTargets, List<ClusterFloatingCandidate> clusterFloatingTargets, int maxWeld, int maxGrind, int maxFloat, bool skipRangeCheck)
        {
            var profilerTs = MethodProfiler.Start();
            var emitterMatrix = _Welder.WorldMatrix;
            emitterMatrix.Translation = Vector3D.Transform(Settings.CorrectedAreaOffset, emitterMatrix);
            var areaBoundingBox = Settings.CorrectedAreaBoundingBox.TransformFast(emitterMatrix);

            // Multi-member cluster discovery: union the coordinator's AABB with every
            // member's AABB so external grids visible to ANY member are found by the
            // entity query. Per-member range filtering still happens downstream
            // (skipRangeCheck=true here, ApplyClusterResultToSelf trims per-member).
            // Without this, only grids inside the coordinator's box are visible —
            // members positioned far from the coordinator can't see their own targets.
            var discoveryBox = areaBoundingBox;
            var clusterUnionUsed = false;
            if (_ClusterMemberAreaBoxes != null && _ClusterMemberAreaBoxes.Count > 1)
            {
                for (int i = 0; i < _ClusterMemberAreaBoxes.Count; i++)
                {
                    var memberAABB = _ClusterMemberAreaBoxes[i].GetAABB();
                    discoveryBox = BoundingBoxD.CreateMerged(discoveryBox, memberAABB);
                }
                clusterUnionUsed = true;
            }

            var entityInRange = SharedEntityCache.GetEntitiesInBox(ref discoveryBox);

            if (entityInRange != null)
            {
                if (clusterGrindTargets != null && (Settings.Flags & SyncBlockSettings.Settings.GrindSmallestGridFirst) != 0)
                {
                    // BUG-110: reusable scan-thread sort buffers.
                    if (_ScanSortedGrids == null) _ScanSortedGrids = new List<IMyEntity>(entityInRange.Count);
                    else _ScanSortedGrids.Clear();
                    if (_ScanNonGridEntities == null) _ScanNonGridEntities = new List<IMyEntity>();
                    else _ScanNonGridEntities.Clear();
                    var sortedGrids = _ScanSortedGrids;
                    var nonGridEntities = _ScanNonGridEntities;
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
                var _unionUsed = clusterUnionUsed;
                var _memberBoxes = _ClusterMemberAreaBoxes != null ? _ClusterMemberAreaBoxes.Count : 0;
                MethodProfiler.StopAndLog("AsyncAddBlocksOfBox", profilerTs, () =>
                    string.Format("entityId={0};entities={1};weldTargets={2};grindTargets={3};floatTargets={4};clusterUnion={5};memberBoxes={6}",
                        _Welder.EntityId,
                        entityInRange != null ? entityInRange.Count : 0,
                        clusterWeldTargets != null ? clusterWeldTargets.Count : -1,
                        clusterGrindTargets != null ? clusterGrindTargets.Count : -1,
                        clusterFloatingTargets != null ? clusterFloatingTargets.Count : -1,
                        _unionUsed, _memberBoxes));
            }
        }
        // FEAT-070: shared sort key helpers used by per-grid, cluster-wide, and per-BaR sorts.
        // Each call site adds its own distance metric and direction.

        /// <summary>
        /// Grind sort key (autogrind, priority, smallest-grid-first + BUG-091 spatial tiebreak).
        /// Returns 0 when the caller should fall through to its distance compare.
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

        /// <summary>Weld sort key — priority only.</summary>
        private int CompareWeldPriority(IMySlimBlock blockA, IMySlimBlock blockB)
        {
            var priorityA = BlockWeldPriority.GetPriority(blockA);
            var priorityB = BlockWeldPriority.GetPriority(blockB);
            return priorityA - priorityB;
        }

        /// <summary>Stable tiebreaker (gridId, position) to keep sort output reproducible.</summary>
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
        /// Min squared distance from blockPos to any cluster member area center;
        /// falls back to fallbackCenter for solo clusters.
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

        // FEAT-074: Quickselect (Hoare). Partially reorders list[left..right] so the
        // k smallest by comparator land in [left..left+k-1]. O(n) avg vs O(n log n) sort.
        // Median-of-three pivot; insertion sort for partitions <= 16.
        private static void QuickSelect(List<ClusterTargetCandidate> list, int left, int right, int k, IComparer<ClusterTargetCandidate> comparer)
        {
            var targetPos = left + k - 1;
            while (left < right)
            {
                // Insertion sort for small partitions
                if (right - left < 16)
                {
                    for (int i = left + 1; i <= right; i++)
                    {
                        var key = list[i];
                        int j = i - 1;
                        while (j >= left && comparer.Compare(list[j], key) > 0)
                        {
                            list[j + 1] = list[j];
                            j--;
                        }
                        list[j + 1] = key;
                    }
                    return;
                }

                // Median-of-three pivot selection
                int mid = left + (right - left) / 2;
                if (comparer.Compare(list[mid], list[left]) < 0)
                {
                    var t = list[left]; list[left] = list[mid]; list[mid] = t;
                }
                if (comparer.Compare(list[right], list[left]) < 0)
                {
                    var t = list[left]; list[left] = list[right]; list[right] = t;
                }
                if (comparer.Compare(list[right], list[mid]) < 0)
                {
                    var t = list[mid]; list[mid] = list[right]; list[right] = t;
                }

                // Place pivot (median value) at right-1
                var tmp = list[mid]; list[mid] = list[right - 1]; list[right - 1] = tmp;
                var pivot = list[right - 1];

                // Partition: elements < pivot to the left, > pivot to the right
                int lo = left;
                int hi = right - 1;
                while (true)
                {
                    while (comparer.Compare(list[++lo], pivot) < 0) { }
                    while (comparer.Compare(list[--hi], pivot) > 0) { }
                    if (lo >= hi) break;
                    var swap = list[lo]; list[lo] = list[hi]; list[hi] = swap;
                }
                // Restore pivot to its final position
                var restore = list[lo]; list[lo] = list[right - 1]; list[right - 1] = restore;

                // lo is the pivot's sorted position. Narrow the search.
                if (targetPos <= lo)
                    right = lo - 1;
                else
                    left = lo + 1;
            }
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

            // BUG-096: partition in-range-to-any-member candidates to the front so
            // farthest-first picks the farthest *reachable* block (multi-member clusters).
            var memberBoxes = _ClusterMemberAreaBoxes;
            var effectiveCount = count;
            var partitionRan = false;
            if (useMemberAware && memberBoxes != null && memberBoxes.Count > 0)
            {
                partitionRan = true;
                tsMark = System.Diagnostics.Stopwatch.GetTimestamp();

                // BUG-099/100: populate distance + priority caches during the partition pass
                // so the comparator can lookup per compare instead of recomputing.
                _sortCandidateDistances.Clear();
                _sortCandidatePriorities.Clear();

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
                        var blockPos = blockMatrix.Translation;
                        var minDist = double.MaxValue;
                        for (int ci = 0; ci < memberCenters.Count; ci++)
                        {
                            var d = (memberCenters[ci] - blockPos).LengthSquared();
                            if (d < minDist) minDist = d;
                        }
                        _sortCandidateDistances[candidate.Block] = minDist;

                        // BUG-100: pre-fetch priority for the sort comparator.
                        var priority = isGrinding
                            ? BlockGrindPriority.GetPriority(candidate.Block)
                            : BlockWeldPriority.GetPriority(candidate.Block);
                        _sortCandidatePriorities[candidate.Block] = priority;

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

            // Nothing reachable — drop everything this grid contributed.
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
            // BUG-100: per-grid sort uses inline cached priority lookup; no smallest-grid
            // tiebreak needed (all candidates share the same CubeGrid).
            var distCache = useMemberAware ? _sortCandidateDistances : null;
            var priCache = useMemberAware ? _sortCandidatePriorities : null;
            var comparator = Comparer<ClusterTargetCandidate>.Create((a, b) =>
            {
                if (isGrinding)
                {
                    // Autogrind-first bucket (inline — no method call).
                    var autoMask = TargetBlockData.AttributeFlags.Autogrind;
                    var autoA = (a.Attributes & autoMask) != 0;
                    var autoB = (b.Attributes & autoMask) != 0;
                    if (autoA != autoB) return autoA ? -1 : 1;

                    // Priority check (unless user disabled). Cached when member-aware,
                    // else fall back to the shared helper for the uncached solo path.
                    if (!grindIgnorePriority)
                    {
                        int priA, priB;
                        if (priCache != null)
                        {
                            priCache.TryGetValue(a.Block, out priA);
                            priCache.TryGetValue(b.Block, out priB);
                        }
                        else
                        {
                            priA = BlockGrindPriority.GetPriority(a.Block);
                            priB = BlockGrindPriority.GetPriority(b.Block);
                        }
                        if (priA != priB) return priA - priB;
                    }

                    // grindSmallestFirst is a no-op for per-grid sort (same grid for all).
                }
                else
                {
                    // Weld: priority-only compare.
                    int priA, priB;
                    if (priCache != null)
                    {
                        priCache.TryGetValue(a.Block, out priA);
                        priCache.TryGetValue(b.Block, out priB);
                    }
                    else
                    {
                        priA = BlockWeldPriority.GetPriority(a.Block);
                        priB = BlockWeldPriority.GetPriority(b.Block);
                    }
                    if (priA != priB) return priA - priB;
                }

                // Distance compare. Member-aware path reads from the pre-computed cache
                // populated during the partition pass above (BUG-099). Solo path uses the
                // inline single-center squared distance — no cache needed because it's a
                // single op per block, not memberCount ops.
                double distA, distB;
                if (distCache != null)
                {
                    distCache.TryGetValue(a.Block, out distA);
                    distCache.TryGetValue(b.Block, out distB);
                }
                else
                {
                    var posA = a.Block.CubeGrid.GridIntegerToWorld(a.Block.Position);
                    var posB = b.Block.CubeGrid.GridIntegerToWorld(b.Block.Position);
                    distA = (center - posA).LengthSquared();
                    distB = (center - posB).LengthSquared();
                }
                return (isGrinding && !grindNearFirst) ? distB.CompareTo(distA) : distA.CompareTo(distB);
            });

            // FEAT-074: quickselect top-k then sort the k; full sort below the 4× threshold.
            var keep = effectiveCount < maxKeep ? effectiveCount : maxKeep;
            if (effectiveCount > maxKeep * 4 && keep < effectiveCount)
            {
                QuickSelect(list, startIndex, startIndex + effectiveCount - 1, keep, comparator);
                list.Sort(startIndex, keep, comparator);
            }
            else
            {
                list.Sort(startIndex, effectiveCount, comparator);
            }
            sortTicks = System.Diagnostics.Stopwatch.GetTimestamp() - tsMark;

            // Trim: drop the out-of-range tail (effectiveCount..count) and any overflow
            // beyond maxKeep from the sorted in-range prefix. keep = min(maxKeep, effectiveCount).
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
    }
}
