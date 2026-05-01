using Sandbox.Definitions;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using SKONanobotBuildAndRepairSystem.Cluster;
using SKONanobotBuildAndRepairSystem.Extensions;
using SKONanobotBuildAndRepairSystem.Handlers;
using SKONanobotBuildAndRepairSystem.Helpers;
using SKONanobotBuildAndRepairSystem.Caches;
using SKONanobotBuildAndRepairSystem.Models;
using SKONanobotBuildAndRepairSystem.Profiling;
using SKONanobotBuildAndRepairSystem.Utils;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using VRage.Game;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRageMath;
using static SKONanobotBuildAndRepairSystem.Utils.UtilsInventory;

namespace SKONanobotBuildAndRepairSystem
{
    // STR-1: split out of NanobotSystem.Scanning.cs (which became 2.7 KLOC after
    // BUG-094/BUG-096/CON-1+PERF-1 layering). This partial holds the block-level
    // scanning primitives — per-block target tests, the recursive grid walk, and
    // the per-grid sort/cap utilities. The orchestration (timer, dispatch,
    // AsyncClusterScan, ApplyClusterResultToSelf) stays in NanobotSystem.Scanning.cs
    // alongside the source/push-target scan and the result-publishing path.
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
                // BUG-132: replaced manual BFS through Mechanical/Attachable/Connector edges
                // + per-fatblock cast with the engine's logical grid-group terminal system,
                // which maintains a type-indexed projector list. On a 5000-block grid with
                // 1 projector this drops from ~5000 casts to a single typed lookup.
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

                // BUG-053: SE applies safe zone building restrictions at the grid level.
                // If the projector's physical grid intersects a safe zone that blocks
                // building, proj.Build() fails for ALL projected blocks on that grid.
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

                // BUG-112: NeedRepair moved to first in the && chain. For a stable base where
                // most blocks are at full integrity (the common case on 11k-block bases),
                // NeedRepair returns false in ~50-200 ns. The remaining checks include
                // IsRelationAllowed4Welding which calls block.GetUserRelationToOwner — an SE
                // engine call costing 1-10 µs per block. Per-block reorder saves ~3 µs × N
                // (~30 ms on an 11k-block grid scan when most blocks are full integrity).
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
        /// BUG-166: hash of all scan parameters that affect per-grid output, used as the
        /// GridScanCache key alongside gridId. The cluster key in ScanClusterCoordinator
        /// includes the BaR's home grid EntityId; this hash deliberately excludes it so two
        /// clusters on different home grids with the same scan settings hit the same cache
        /// entry. For multi-member clusters skipRangeCheck=true and the per-grid output is
        /// position-independent, so this hash fully captures everything that determines the
        /// candidate list.
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

            // BUG-161: when both target types are nulled (cluster caps already saturated, or
            // grind disabled and weld cap hit), the slim-block loop has no targets to add but
            // still walks every block on the grid for connection/projector traversal. On a
            // 7,600-block grid this costs 50–55 ms per cluster scan with weldAdded=0,
            // grindAdded=0. Iterate only fat blocks (which is what connection traversal needs)
            // — same shape as the empty-grid-cache fast path above. Projector traversal is
            // already gated on weldTargetsForGrid != null, so it's a no-op here anyway.
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

            // BUG-166: per-grid scan cache. When two cluster coordinators target the same
            // grid in the same scan window, the second one hits the cache and skips the
            // 8K-block iteration. Only enabled for skipRangeCheck=true (multi-member
            // clusters) — for solo BaRs the per-block IsInRange check makes the output
            // position-dependent and the cache is invalid.
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

                    // Connection traversal still needs to happen — cache only covers slim-block
                    // target collection for THIS grid, not recursion into mechanical/connector
                    // children (those have their own cache entries keyed on their own gridId).
                    // PERF-1 + CON-1: rent pooled buffer; traversal extracted into helper.
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

                                    if (BlockWeldPriority.GetEnabled(block) && block.CanBuild(false))
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

            // BUG-166: populate the per-grid scan cache so a second cluster scanning the
            // same grid in the next ~3 s hits the fast path instead of re-iterating slim
            // blocks. Slice the per-grid contribution out of the cluster lists. Only cache
            // when both target types were active at this grid (no cap-skip) so the cached
            // entry represents a complete contribution; otherwise the second cluster might
            // be missing one type's data.
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

        // ----------------------------------------------------------------------------------
        // FEAT-074: Quickselect (Hoare's selection algorithm).
        //
        // Partially reorders list[left..right] so that list[left..left+k-1] contains
        // the k smallest elements according to the comparator (unordered among
        // themselves). Average O(n), vs O(n log n) for a full sort.
        //
        // Used by SortAndCapGridCandidates when the candidate count far exceeds
        // maxKeep, to avoid sorting thousands of items only to discard most of them.
        // After quickselect, only the top-k are sorted with list.Sort().
        //
        // Uses median-of-three pivot selection for robustness against sorted/reverse
        // inputs. Falls back to insertion sort for small partitions (<=16 elements).
        // ----------------------------------------------------------------------------------
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

                // BUG-099: populate the sort distance cache while we already have each
                // block's world position and are iterating every candidate for the OBB
                // partition check. The sort comparator can then do a dict lookup per
                // compare instead of recomputing 2 * memberCount squared distances per
                // compare — profiling on a 58-member cluster showed the inline recompute
                // cost 70-125 ms per sort on 11k candidates; this drops it to ~6-10 ms.
                //
                // BUG-100: also populate the priority cache so the comparator can skip the
                // per-compare GetPriority lookups (previously ~34 ms / sort for 9.9k-cand
                // grind sort after BUG-099). Pre-fetched once per block (~130 ns) then read
                // from the cache per compare (~40 ns) for a ~2-3x speedup on the sort.
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
                        // Compute min-squared-distance to any member center while the
                        // block position is in L1. Used by the sort comparator below.
                        var blockPos = blockMatrix.Translation;
                        var minDist = double.MaxValue;
                        for (int ci = 0; ci < memberCenters.Count; ci++)
                        {
                            var d = (memberCenters[ci] - blockPos).LengthSquared();
                            if (d < minDist) minDist = d;
                        }
                        _sortCandidateDistances[candidate.Block] = minDist;

                        // BUG-100: pre-fetch priority for the sort comparator. One GetPriority
                        // call per block here replaces 2 calls per comparison × ~132k
                        // comparisons in the sort.
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
            // Snapshot cache references for the closure — fields are never reassigned during
            // a sort so local-capture keeps the comparator body branch-free. The per-grid sort
            // doesn't need the smallest-grid BlocksCount tiebreaker (all candidates are on the
            // same CubeGrid so the compare is always 0) so we inline a minimal autogrind +
            // priority + distance compare that reads both caches directly instead of going
            // through CompareGrindNonDistance / CompareWeldPriority + their internal GetPriority
            // lookups. That saves ~130 ns per GetPriority × 2 per compare × ~132k compares =
            // ~34 ms per 9.9k-candidate sort on a 58-member cluster (BUG-100).
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

            // FEAT-074: For large candidate sets, use quickselect O(n) to find
            // the top-k candidates, then sort only those k items O(k log k).
            // This replaces the full O(n log n) sort which on an 11,732-candidate
            // grid costs 20-33ms just to keep 256 items. Threshold: only use
            // quickselect when candidates exceed 4× maxKeep (below that the
            // full sort is fast enough and quickselect overhead isn't worth it).
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
