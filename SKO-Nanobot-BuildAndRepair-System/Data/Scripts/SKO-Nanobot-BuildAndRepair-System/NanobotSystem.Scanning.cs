using Sandbox.Definitions;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using SKONanobotBuildAndRepairSystem.Handlers;
using SKONanobotBuildAndRepairSystem.Helpers;
using SKONanobotBuildAndRepairSystem.Models;
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
    partial class NanobotSystem
    {
        private bool SetSafeZoneAndShieldStates()
        {
            var safeZoneActionsState = SafeZoneHandler.GetActionsAllowedForSystem(this);

            var safezoneAllowsWelding = safeZoneActionsState.IsWeldingAllowed;
            var safeZoneAllowsBuildingProjections = safeZoneActionsState.IsBuildingProjectionsAllowed;
            var safeZoneAllowsGrinding = safeZoneActionsState.IsGrindingAllowed;
            var welderIsShielded = IsWelderShielded();

            var changed = false;

            if (State.SafeZoneAllowsWelding != safezoneAllowsWelding)
            {
                State.SafeZoneAllowsWelding = safezoneAllowsWelding;
                changed = true;
            }

            if (State.SafeZoneAllowsBuildingProjections != safeZoneAllowsBuildingProjections)
            {
                State.SafeZoneAllowsBuildingProjections = safeZoneAllowsBuildingProjections;
                changed = true;
            }

            if (State.SafeZoneAllowsGrinding != safeZoneAllowsGrinding)
            {
                State.SafeZoneAllowsGrinding = safeZoneAllowsGrinding;
                changed = true;
            }

            if (State.IsShielded != welderIsShielded)
            {
                State.IsShielded = welderIsShielded;
                changed = true;
            }

            return changed;
        }

        public void UpdateSourcesAndTargetsTimer(List<NanobotSystem> systemsSnapshot)
        {
            var playTime = MyAPIGateway.Session.ElapsedPlayTime;
            var updateTargets = playTime.Subtract(_LastTargetsUpdate) >= Mod.Settings.TargetsUpdateInterval;
            var updateSources = updateTargets && playTime.Subtract(_LastSourceUpdate) >= Mod.Settings.SourcesUpdateInterval;
            if (updateTargets)
            {
                StartAsyncUpdateSourcesAndTargets(updateSources, systemsSnapshot);
            }
        }

        /// <summary>
        /// Parse all the connected blocks and find the possible targets and sources of components
        /// </summary>
        private void StartAsyncUpdateSourcesAndTargets(bool updateSource, List<NanobotSystem> systemsSnapshot)
        {
            if (!_Welder.UseConveyorSystem)
            {
                lock (_PossibleSources)
                {
                    _PossibleSources.Clear();
                }
            }

            if (!_Welder.Enabled || !_Welder.IsFunctional || State.Ready == false)
            {
                lock (State.PossibleWeldTargets)
                {
                    State.PossibleWeldTargets.Clear();
                    State.PossibleWeldTargets.RebuildHash();
                }

                lock (State.PossibleGrindTargets)
                {
                    State.PossibleGrindTargets.Clear();
                    State.PossibleGrindTargets.RebuildHash();
                }

                lock (State.PossibleFloatingTargets)
                {
                    State.PossibleFloatingTargets.Clear();
                    State.PossibleFloatingTargets.RebuildHash();
                }

                _AsyncUpdateSourcesAndTargetsRunning = false;
                _LastTargetsUpdate = MyAPIGateway.Session.ElapsedPlayTime;
                _LastSourceUpdate = _LastTargetsUpdate;

                return;
            }

            // Register with scan coordinator — WorldMatrix is safe on main thread.
            // The cluster key is always computed (needed for push-coalescing), but only
            // BoundingBox (Fly) mode BaRs participate in the union-bbox accumulation and
            // coordinator election.  Walk (Grids) mode BaRs never call AsyncAddBlocksOfBox
            // so they neither produce nor consume the coordinator's cached entity list;
            // including their work area in the union bbox would needlessly widen the query.
            {
                var emitterMatrix = _Welder.WorldMatrix;
                emitterMatrix.Translation = Vector3D.Transform(Settings.CorrectedAreaOffset, emitterMatrix);
                var worldAABB = Settings.CorrectedAreaBoundingBox.TransformFast(emitterMatrix);

                // Refresh cluster key at most once per ClusterKeyRefreshInterval to avoid
                // a GetGroup() call every tick.
                var now = MyAPIGateway.Session.ElapsedPlayTime;
                if (System.Threading.Interlocked.Read(ref _ClusterKey) == 0L
                    || now - _ClusterKeyLastRefreshTime >= ClusterKeyRefreshInterval)
                {
                    var computedKey = ScanCoordinator.ComputeClusterKey(_Welder.CubeGrid);
                    System.Threading.Interlocked.Exchange(ref _ClusterKey, computedKey);
                    _ClusterKeyLastRefreshTime = now;
                }

                if (Settings.SearchMode == SearchModes.BoundingBox)
                {
                    ScanCoordinator.AccumulateAndElect(
                        System.Threading.Interlocked.Read(ref _ClusterKey),
                        _Welder.CubeGrid, worldAABB, systemsSnapshot);
                }
            }

            // Cache session-level grind-block flag on main thread before launching the async task
            _scanGrindBlockedByScenario = (MyAPIGateway.Session.SessionSettings.Scenario || MyAPIGateway.Session.SessionSettings.ScenarioEditMode)
                && !MyAPIGateway.Session.SessionSettings.DestructibleBlocks;

            // Use dedicated lock object instead of a game entity
            lock (_asyncUpdateLock)
            {
                if (_AsyncUpdateSourcesAndTargetsRunning) return;

                _AsyncUpdateSourcesAndTargetsRunning = true;
                Mod.AddAsyncAction(() => AsyncUpdateSourcesAndTargets(updateSource));
            }
        }

        public void AsyncUpdateSourcesAndTargets(bool updateSource)
        {
            try
            {
                if (!State.Ready) return;

                var weldingEnabled = BlockWeldPriority.AnyEnabled && Settings.WorkMode != WorkModes.GrindOnly;
                var grindingEnabled = BlockGrindPriority.AnyEnabled && Settings.WorkMode != WorkModes.WeldOnly;

                updateSource &= _Welder.UseConveyorSystem;
                int pos = 0;

                try
                {
                    pos = 1;

                    // HashSet gives O(1) Contains/Add vs O(N) for List — avoids O(N²) in deep sub-grid trees
                    _TempGrids.Clear();
                    _TempPossibleWeldTargets.Clear();
                    _TempPossibleGrindTargets.Clear();
                    _TempPossibleFloatingTargets.Clear();
                    _TempPossibleSources.Clear();
                    _TempPossibleSourcesSet.Clear();
                    _TempPossiblePushTargets.Clear();
                    _TempIgnore4Ingot.Clear();
                    _TempIgnore4Components.Clear();
                    _TempIgnore4Items.Clear();

                    var ignoreColor = Settings.IgnoreColorPacked;
                    var grindColor = Settings.GrindColorPacked;
                    var emitterMatrix = _Welder.WorldMatrix;
                    emitterMatrix.Translation = Vector3D.Transform(Settings.CorrectedAreaOffset, emitterMatrix);
                    var areaOrientedBox = new MyOrientedBoundingBoxD(Settings.CorrectedAreaBoundingBox, emitterMatrix);

                    // Fast path: use the coordinator's cached mechanical grid snapshot to avoid
                    // per-BaR DFS traversal of piston/rotor chains.  Each grid is scanned with
                    // skipMechanicalTraversal=true so recursive piston/rotor walking is suppressed;
                    // connector-linked grids are still discovered inside each call as before.
                    // Slow path (null list): first cycle after startup, Walk-mode-only clusters, or
                    // any BaR whose election hasn't run yet — fall back to full DFS from home grid.
                    var mechanicalGridIds = ScanCoordinator.TryGetMechanicalGridIds(
                        System.Threading.Interlocked.Read(ref _ClusterKey));
                    if (mechanicalGridIds != null)
                    {
                        // Always scan own grid first so sources on the home grid are discovered.
                        AsyncAddBlocksOfGrid(ref areaOrientedBox, ((Settings.Flags & SyncBlockSettings.Settings.UseIgnoreColor) != 0), ref ignoreColor, ((Settings.Flags & SyncBlockSettings.Settings.UseGrindColor) != 0), ref grindColor, Settings.UseGrindJanitorOn, Settings.GrindJanitorOptions, _Welder.CubeGrid, _TempGrids, updateSource ? _TempPossibleSources : null, updateSource ? _TempPossibleSourcesSet : null, weldingEnabled ? _TempPossibleWeldTargets : null, grindingEnabled ? _TempPossibleGrindTargets : null, skipMechanicalTraversal: true);

                        foreach (var mechGridId in mechanicalGridIds)
                        {
                            if (mechGridId == _Welder.CubeGrid.EntityId) continue;
                            var subGrid = MyAPIGateway.Entities.GetEntityById(mechGridId) as IMyCubeGrid;
                            if (subGrid == null) continue;
                            AsyncAddBlocksOfGrid(ref areaOrientedBox, ((Settings.Flags & SyncBlockSettings.Settings.UseIgnoreColor) != 0), ref ignoreColor, ((Settings.Flags & SyncBlockSettings.Settings.UseGrindColor) != 0), ref grindColor, Settings.UseGrindJanitorOn, Settings.GrindJanitorOptions, subGrid, _TempGrids, updateSource ? _TempPossibleSources : null, updateSource ? _TempPossibleSourcesSet : null, weldingEnabled ? _TempPossibleWeldTargets : null, grindingEnabled ? _TempPossibleGrindTargets : null, skipMechanicalTraversal: true);
                        }
                    }
                    else
                    {
                        // Slow path: full DFS — first cycle, Walk-mode-only cluster, or no election yet.
                        AsyncAddBlocksOfGrid(ref areaOrientedBox, ((Settings.Flags & SyncBlockSettings.Settings.UseIgnoreColor) != 0), ref ignoreColor, ((Settings.Flags & SyncBlockSettings.Settings.UseGrindColor) != 0), ref grindColor, Settings.UseGrindJanitorOn, Settings.GrindJanitorOptions, _Welder.CubeGrid, _TempGrids, updateSource ? _TempPossibleSources : null, updateSource ? _TempPossibleSourcesSet : null, weldingEnabled ? _TempPossibleWeldTargets : null, grindingEnabled ? _TempPossibleGrindTargets : null);
                    }

                    switch (Settings.SearchMode)
                    {
                        case SearchModes.Grids:
                            break;

                        case SearchModes.BoundingBox:
                            AsyncAddBlocksOfBox(ref areaOrientedBox, ((Settings.Flags & SyncBlockSettings.Settings.UseIgnoreColor) != 0), ref ignoreColor, ((Settings.Flags & SyncBlockSettings.Settings.UseGrindColor) != 0), ref grindColor, Settings.UseGrindJanitorOn, Settings.GrindJanitorOptions, _TempGrids, weldingEnabled ? _TempPossibleWeldTargets : null, grindingEnabled ? _TempPossibleGrindTargets : null, _ComponentCollectPriority.AnyEnabled ? _TempPossibleFloatingTargets : null);
                            break;
                    }

                    pos = 2;
                    if (updateSource)
                    {
                        Vector3D posWelder;
                        _Welder.SlimBlock.ComputeWorldCenter(out posWelder);
                        try
                        {
                            // Pre-compute squared distances once to avoid O(N log N) ComputeWorldCenter calls inside the comparator
                            _TempSourceDistances.Clear();
                            foreach (var src in _TempPossibleSources)
                            {
                                var blk = src.Owner as IMyCubeBlock;
                                if (blk != null)
                                {
                                    Vector3D blkPos;
                                    blk.SlimBlock.ComputeWorldCenter(out blkPos);
                                    _TempSourceDistances[src] = (posWelder - blkPos).LengthSquared();
                                }
                            }

                            _TempPossibleSources.Sort((a, b) =>
                            {
                                var blockA = a.Owner as IMyCubeBlock;
                                var blockB = b.Owner as IMyCubeBlock;
                                if (blockA != null && blockB != null)
                                {
                                    var welderA = blockA as IMyShipWelder;
                                    var welderB = blockB as IMyShipWelder;
                                    if ((welderA == null) == (welderB == null))
                                    {
                                        double distA, distB;
                                        _TempSourceDistances.TryGetValue(a, out distA);
                                        _TempSourceDistances.TryGetValue(b, out distB);
                                        return distA.CompareTo(distB);
                                    }
                                    else if (welderA == null)
                                    {
                                        return -1;
                                    }
                                    else
                                    {
                                        return 1;
                                    }
                                }
                                else if (blockA != null) return -1;
                                else if (blockB != null) return 1;
                                else return 0;
                            });
                        }
                        catch (Exception ex)
                        {
                            Logging.Instance.Error("Error on .Sort for _TempPossibleSources. Exception: {0}", ex);
                        }

                        foreach (var inventory in _TempPossibleSources)
                        {
                            // Only cargo containers are valid push destinations.
                            // Assemblers, refineries, welders, etc. are pull sources only.
                            if (inventory.Owner is IMyCargoContainer)
                            {
                                _TempPossiblePushTargets.Add(inventory);
                                continue;
                            }

                            var block = inventory.Owner as IMyShipWelder;
                            if (block != null && block.BlockDefinition.SubtypeName.Contains("NanobotBuildAndRepairSystem") && block.GameLogic != null)
                            {
                                var bar = block.GameLogic.GetAs<NanobotSystem>();

                                //Don't use Bar's as destination that would push immediately
                                if (bar != null)
                                {
                                    if ((bar.Settings.Flags & SyncBlockSettings.Settings.PushIngotOreImmediately) != 0)
                                    {
                                        _TempIgnore4Ingot.Add(inventory);
                                    }
                                    if ((bar.Settings.Flags & SyncBlockSettings.Settings.PushComponentImmediately) != 0)
                                    {
                                        _TempIgnore4Components.Add(inventory);
                                    }
                                    if ((bar.Settings.Flags & SyncBlockSettings.Settings.PushItemsImmediately) != 0)
                                    {
                                        _TempIgnore4Items.Add(inventory);
                                    }
                                }
                            }
                        }
                    }

                    pos = 3;
                    try
                    {
                        _TempPossibleWeldTargets.Sort((a, b) =>
                        {
                            var priorityA = BlockWeldPriority.GetPriority(a.Block);
                            var priorityB = BlockWeldPriority.GetPriority(b.Block);
                            if (priorityA != priorityB) return priorityA - priorityB;

                            var distCmp = Utils.Utils.CompareDistance(a.Distance, b.Distance);
                            if (distCmp != 0) return distCmp;

                            // Stable tiebreaker: grid entity ID then block grid position
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
                        Logging.Instance.Error("Error on .Sort for _TempPossibleWeldTargets. Exception: {0}", ex);
                    }

                    pos = 4;
                    try
                    {
                        var grindUsePriority = (Settings.Flags & SyncBlockSettings.Settings.GrindIgnorePriorityOrder) == 0;
                        var grindSmallestGridFirst = (Settings.Flags & SyncBlockSettings.Settings.GrindSmallestGridFirst) != 0;
                        var grindNearFirst = (Settings.Flags & SyncBlockSettings.Settings.GrindNearFirst) != 0;
                        _TempPossibleGrindTargets.Sort((a, b) =>
                        {
                            if ((a.Attributes & TargetBlockData.AttributeFlags.Autogrind) == (b.Attributes & TargetBlockData.AttributeFlags.Autogrind))
                            {
                                if ((a.Attributes & TargetBlockData.AttributeFlags.Autogrind) != 0)
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
                        Logging.Instance.Error("Error on .Sort for _TempPossibleGrindTargets. Exception: {0}", ex);
                    }

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
                        Logging.Instance.Error("Error on .Sort for _TempPossibleFloatingTargets. Exception: {0}", ex);
                    }

                    pos = 5;
                    // Removed logging.

                    pos = 6;
                    lock (State.PossibleWeldTargets)
                    {
                        State.PossibleWeldTargets.Clear();
                        State.PossibleWeldTargets.AddRange(_TempPossibleWeldTargets);
                        State.PossibleWeldTargets.RebuildHash();
                    }
                    _TempPossibleWeldTargets.Clear();
                    pos = 7;
                    lock (State.PossibleGrindTargets)
                    {
                        State.PossibleGrindTargets.Clear();
                        State.PossibleGrindTargets.AddRange(_TempPossibleGrindTargets);
                        State.PossibleGrindTargets.RebuildHash();
                    }
                    _TempPossibleGrindTargets.Clear();
                    pos = 8;
                    lock (State.PossibleFloatingTargets)
                    {
                        State.PossibleFloatingTargets.Clear();
                        State.PossibleFloatingTargets.AddRange(_TempPossibleFloatingTargets);
                        State.PossibleFloatingTargets.RebuildHash();
                    }
                    _TempPossibleFloatingTargets.Clear();

                    pos = 9;
                    if (updateSource)
                    {
                        lock (_PossibleSources)
                        {
                            _PossibleSources.Clear();
                            _PossibleSources.AddRange(_TempPossibleSources);
                            _PossiblePushTargets.Clear();
                            _PossiblePushTargets.AddRange(_TempPossiblePushTargets);
                            _Ignore4Ingot.Clear();
                            _Ignore4Ingot.UnionWith(_TempIgnore4Ingot);
                            _Ignore4Components.Clear();
                            _Ignore4Components.UnionWith(_TempIgnore4Components);
                            _Ignore4Items.Clear();
                            _Ignore4Items.UnionWith(_TempIgnore4Items);
                        }
                        _TempPossibleSources.Clear();
                        _TempPossiblePushTargets.Clear();
                        _TempIgnore4Ingot.Clear();
                        _TempIgnore4Components.Clear();
                        _TempIgnore4Items.Clear();
                    }

                    _ContinuouslyError = 0;
                }
                catch (Exception ex)
                {
                    _ContinuouslyError++;
                    if (_ContinuouslyError > 10 || Logging.Instance.ShouldLog(Logging.Level.Info) || Logging.Instance.ShouldLog(Logging.Level.Verbose))
                    {
                        Logging.Instance.Error("BuildAndRepairSystemBlock {0}: AsyncUpdateSourcesAndTargets exception at {1}: {2}", Logging.BlockName(_Welder, Logging.BlockNameOptions.None), pos, ex);
                    }
                }
            }
            finally
            {
                _LastTargetsUpdate = MyAPIGateway.Session.ElapsedPlayTime;
                if (updateSource) _LastSourceUpdate = _LastTargetsUpdate;
                _AsyncUpdateSourcesAndTargetsRunning = false;
            }
        }

        /// <summary>
        /// Search for grids inside bounding box and add their damaged block also
        /// </summary>
        private void AsyncAddBlocksOfBox(ref MyOrientedBoundingBoxD areaBox, bool useIgnoreColor, ref uint ignoreColor, bool useGrindColor, ref uint grindColor, AutoGrindRelation autoGrindRelation, AutoGrindOptions autoGrindOptions, HashSet<IMyCubeGrid> grids, List<TargetBlockData> possibleWeldTargets, List<TargetBlockData> possibleGrindTargets, List<TargetEntityData> possibleFloatingTargets)
        {
            var emitterMatrix = _Welder.WorldMatrix;
            emitterMatrix.Translation = Vector3D.Transform(Settings.CorrectedAreaOffset, emitterMatrix);
            var areaBoundingBox = Settings.CorrectedAreaBoundingBox.TransformFast(emitterMatrix);

            var now = MyAPIGateway.Session.ElapsedPlayTime;
            var bboxCenter = areaBoundingBox.Center;
            List<IMyEntity> entityInRange = null;

            long clusterKey = System.Threading.Interlocked.Read(ref _ClusterKey);
            bool isCoordinator = ScanCoordinator.IsCoordinator(clusterKey, _Welder.EntityId);

            if (isCoordinator)
            {
                entityInRange = ScanCoordinator.CoordinatorFetchEntities(clusterKey);
                // null = cluster not ready yet; fall through to per-BaR fallback below
            }
            else
            {
                entityInRange = ScanCoordinator.TryGetCachedEntities(clusterKey);
                // null = cache miss; fall through to per-BaR fallback below
            }

            if (entityInRange == null)
            {
                var cacheStale = _CachedEntitiesInRange == null
                    || (now - _CachedEntitiesInRangeTime).TotalSeconds > EntityCacheTtlSeconds
                    || (bboxCenter - _CachedEntitiesInRangeBBoxCenter).LengthSquared()
                       > EntityCachePositionTolerance * EntityCachePositionTolerance;

                if (cacheStale)
                {
                    lock (_entityQueryLock)
                    {
                        _CachedEntitiesInRange =
                            MyAPIGateway.Entities.GetTopMostEntitiesInBox(ref areaBoundingBox);
                    }
                    _CachedEntitiesInRangeTime = now;
                    _CachedEntitiesInRangeBBoxCenter = bboxCenter;
                }
                entityInRange = _CachedEntitiesInRange;
            }
            // All code below (GrindSmallestGridFirst, foreach loop) is unchanged.

            if (entityInRange != null)
            {
                // When grinding smallest grid first, pre-sort only MyCubeGrid entities so
                // smaller grids fill the candidate list before large grids can crowd them out.
                // Build a sorted copy to avoid mutating the cached list.
                if (possibleGrindTargets != null && (Settings.Flags & SyncBlockSettings.Settings.GrindSmallestGridFirst) != 0)
                {
                    // Try to reuse the coordinator's pre-sorted list (built once per cluster per TTL).
                    var coordSorted = ScanCoordinator.TryGetSizeSortedEntities(clusterKey);
                    if (coordSorted != null)
                    {
                        entityInRange = coordSorted;
                    }
                    else
                    {
                        // Fallback: sort locally — coordinator not ready or TTL expired.
                        // Reuse pre-allocated instance lists to avoid per-scan heap allocations.
                        _TempSortedGridEntities.Clear();
                        _TempNonGridEntities.Clear();
                        foreach (var e in entityInRange)
                        {
                            if (ShouldStopScan(possibleWeldTargets, possibleGrindTargets, possibleFloatingTargets))
                            {
                                break;
                            }

                            if (e is MyCubeGrid)
                                _TempSortedGridEntities.Add(e);
                            else
                                _TempNonGridEntities.Add(e);
                        }

                        _TempSortedGridEntities.Sort((a, b) => ((MyCubeGrid)a).BlocksCount - ((MyCubeGrid)b).BlocksCount);
                        _TempSortedGridEntities.AddRange(_TempNonGridEntities);
                        entityInRange = _TempSortedGridEntities;
                    }
                }

                foreach (var entity in entityInRange)
                {
                    if (ShouldStopScan(possibleWeldTargets, possibleGrindTargets, possibleFloatingTargets))
                    {
                        break;
                    }

                    var grid = entity as IMyCubeGrid;
                    if (grid != null)
                    {
                        var cubeGrid = grid as MyCubeGrid;
                        if (cubeGrid != null && cubeGrid.Projector == null)
                        {
                            // IsPreview: Skip if grid is in preview-mode (copy-paste and not yet placed).
                            // Editable: Editable == false means that a player cannot target the grid anymore. Cannot add new blocks, weld stuff, or grind stuff.
                            if (cubeGrid.IsPreview || !cubeGrid.Editable)
                            {
                                continue;
                            }
                        }

                        // Quick pre-filter: skip grids whose AABB doesn't intersect this BaR's work
                        // area. areaBoundingBox is already computed above. Avoids full block
                        // iteration for out-of-range grids that entered the list via the
                        // coordinator's union bbox.
                        var gridAABB = grid.WorldAABB;
                        if (!areaBoundingBox.Intersects(ref gridAABB))
                            continue;

                        // Snapshot count before scanning so we can cap this grid's contribution to
                        // grind targets.  Without this, a single large grid can fill all 256 slots
                        // and leave no room for other target grids.  BaRs blocked by the per-grid
                        // BaR limit would then have no fallback targets and go idle.
                        var grindCountBefore = possibleGrindTargets?.Count ?? 0;

                        // Scan for target blocks of grid.
                        AsyncAddBlocksOfGrid(ref areaBox, useIgnoreColor, ref ignoreColor, useGrindColor, ref grindColor, autoGrindRelation, autoGrindOptions, grid, grids, null, null, possibleWeldTargets, possibleGrindTargets);

                        // Per-grid grind-candidate cap: each external grid may contribute at most
                        // MaxSystemsPerTargetGrid * 3 entries.  This ensures all grids in range are
                        // represented in the candidate list so that BaRs which are over the limit on
                        // one grid can always find work on another.  The 3× factor gives a generous
                        // buffer for AssignToSystem round-trips without monopolising the 256-slot list.
                        // The cap is skipped when the per-grid BaR limit is disabled.
                        if (possibleGrindTargets != null
                            && !Mod.Settings.DisableLimitSystemsPerTargetGrid
                            && Mod.Settings.MaxSystemsPerTargetGrid > 0)
                        {
                            var grindAdded = possibleGrindTargets.Count - grindCountBefore;
                            var maxGrindPerGrid = Mod.Settings.MaxSystemsPerTargetGrid * 3;
                            if (grindAdded > maxGrindPerGrid)
                                possibleGrindTargets.RemoveRange(grindCountBefore + maxGrindPerGrid, grindAdded - maxGrindPerGrid);
                        }

                        continue;
                    }

                    if (possibleFloatingTargets != null)
                    {
                        var hasReachedMaxFloatingObjects = possibleFloatingTargets.Count >= MaxPossibleFloatingTargets;
                        if (hasReachedMaxFloatingObjects)
                        {
                            continue;
                        }

                        var floating = entity as MyFloatingObject;
                        if (floating != null)
                        {
                            if (!floating.MarkedForClose && ComponentCollectPriority.GetEnabled(floating.Item.Content.GetObjectId()))
                            {
                                var floatingPos = floating.WorldMatrix.Translation;
                                if (areaBox.Contains(ref floatingPos))
                                {
                                    var distance = (areaBox.Center - floatingPos).Length();
                                    possibleFloatingTargets.Add(new TargetEntityData(floating, distance));
                                }
                            }
                            continue;
                        }

                        var character = entity as IMyCharacter;
                        if (character != null)
                        {
                            if (character.IsDead && !character.InventoriesEmpty() && !((MyCharacterDefinition)character.Definition).EnableSpawnInventoryAsContainer)
                            {
                                var charPos = character.WorldMatrix.Translation;
                                if (areaBox.Contains(ref charPos))
                                {
                                    var distance = (areaBox.Center - charPos).Length();
                                    possibleFloatingTargets.Add(new TargetEntityData(character, distance));
                                }
                            }
                            continue;
                        }

                        var inventoryBag = entity as IMyInventoryBag;
                        if (inventoryBag != null)
                        {
                            if (!inventoryBag.InventoriesEmpty())
                            {
                                var bagPos = inventoryBag.WorldMatrix.Translation;
                                if (areaBox.Contains(ref bagPos))
                                {
                                    var distance = (areaBox.Center - bagPos).Length();
                                    possibleFloatingTargets.Add(new TargetEntityData(inventoryBag, distance));
                                }
                            }
                            continue;
                        }
                    }
                }
            }
        }

        private void AsyncAddBlocksOfGrid(ref MyOrientedBoundingBoxD areaBox, bool useIgnoreColor, ref uint ignoreColor, bool useGrindColor, ref uint grindColor, AutoGrindRelation autoGrindRelation, AutoGrindOptions autoGrindOptions, IMyCubeGrid cubeGrid, HashSet<IMyCubeGrid> grids, List<IMyInventory> possibleSources, HashSet<IMyInventory> seenSources, List<TargetBlockData> possibleWeldTargets, List<TargetBlockData> possibleGrindTargets, bool skipMechanicalTraversal = false)
        {
            if (!State.Ready) return; //Block not ready
            if (grids.Contains(cubeGrid)) return; //Allready parsed

            grids.Add(cubeGrid);
            SharedGridBlockCache.EnsureSubscribed(cubeGrid);

            // Empty-grid ignore: skip non-own grids confirmed empty by any BaR in this cluster.
            // We set a flag rather than doing an early return so that sub-grid connections
            // (connectors, pistons, hinges, rotors) are still traversed — a newly docked/spawned
            // ship past an "empty" intermediate grid must not be missed.
            var emptyGridIgnoreSeconds = Mod.Settings.EmptyGridScanIgnoreSeconds;
            var isOwnGrid = cubeGrid.EntityId == _Welder.CubeGrid.EntityId;
            var clusterKey = System.Threading.Interlocked.Read(ref _ClusterKey);
            var isIgnored = false;
            if (!isOwnGrid && emptyGridIgnoreSeconds > 0 && clusterKey != 0)
            {
                isIgnored = ScanCoordinator.IsGridIgnored(clusterKey, cubeGrid.EntityId, MyAPIGateway.Session.ElapsedPlayTime);
            }

            var isGrindingMode = Settings.WorkMode == WorkModes.GrindOnly || Settings.WorkMode == WorkModes.GrindBeforeWeld;
            var isGrinding = State.Grinding || State.NeedGrinding || (State.Transporting && isGrindingMode) || isGrindingMode;

            // Use a cached list to avoid many GetBlocks calls from the API.
            var newBlocks = GetBlocksFromCache(cubeGrid, isGrinding);

            // The shared cache provides priority-only ordering.  For grind scans with NearFirst
            // or FarFirst (default) settings, iterate blocks in distance order so the 256-slot
            // candidate cap fills with the right blocks before pos=4 sorts the final sequence.
            // GrindSmallestFirst is handled by the entity-level pre-sort in AsyncAddBlocksOfBox.
            var blocksToIterate = newBlocks;
            var emptyIgnoreWeldBefore = possibleWeldTargets?.Count ?? 0;
            var emptyIgnoreGrindBefore = possibleGrindTargets?.Count ?? 0;
            if (!isIgnored && possibleGrindTargets != null && isGrinding)
            {
                var _grindSmallestFirst = (Settings.Flags & SyncBlockSettings.Settings.GrindSmallestGridFirst) != 0;
                if (!_grindSmallestFirst)
                {
                    var _grindNearFirst = (Settings.Flags & SyncBlockSettings.Settings.GrindNearFirst) != 0;
                    var _ignPriority = (Settings.Flags & SyncBlockSettings.Settings.GrindIgnorePriorityOrder) != 0;
                    var _areaCenter = areaBox.Center;

                    // Pre-compute squared distances O(N) — avoids repeated world-transforms inside the comparator.
                    _TempBlockDistances.Clear();
                    foreach (var blk in newBlocks)
                    {
                        var blkPos = cubeGrid.GridIntegerToWorld(blk.Position);
                        _TempBlockDistances[blk] = (_areaCenter - blkPos).LengthSquared();
                    }

                    var sortedCopy = new List<IMySlimBlock>(newBlocks);
                    sortedCopy.Sort((sortA, sortB) =>
                    {
                        if (!_ignPriority)
                        {
                            var pa = BlockGrindPriority.GetPriority(sortA);
                            var pb = BlockGrindPriority.GetPriority(sortB);
                            if (pa != pb) return pa - pb;
                        }
                        double da, db;
                        _TempBlockDistances.TryGetValue(sortA, out da);
                        _TempBlockDistances.TryGetValue(sortB, out db);
                        return _grindNearFirst ? da.CompareTo(db) : db.CompareTo(da);
                    });
                    blocksToIterate = sortedCopy;
                }
            }

            foreach (var slimBlock in blocksToIterate)
            {
                if (ShouldStopScan(possibleWeldTargets, possibleGrindTargets, null))
                {
                    break;
                }

                // For ignored grids, skip block target/source checks — only sub-grid connections matter.
                if (!isIgnored)
                    AsyncAddBlockIfTargetOrSource(ref areaBox, useIgnoreColor, ref ignoreColor, useGrindColor, ref grindColor, autoGrindRelation, autoGrindOptions, slimBlock, possibleSources, seenSources, possibleWeldTargets, possibleGrindTargets);

                var fatBlock = slimBlock.FatBlock;
                if (fatBlock == null) continue;

                var mechanicalConnectionBlock = fatBlock as Sandbox.ModAPI.IMyMechanicalConnectionBlock;
                if (mechanicalConnectionBlock != null)
                {
                    if (!skipMechanicalTraversal && mechanicalConnectionBlock.TopGrid != null && !ShouldStopScan(possibleWeldTargets, possibleGrindTargets, null))
                    {
                        AsyncAddBlocksOfGrid(ref areaBox, useIgnoreColor, ref ignoreColor, useGrindColor, ref grindColor, autoGrindRelation, autoGrindOptions, mechanicalConnectionBlock.TopGrid, grids, possibleSources, seenSources, possibleWeldTargets, possibleGrindTargets);
                    }
                    continue;
                }

                var attachableTopBlock = fatBlock as Sandbox.ModAPI.IMyAttachableTopBlock;
                if (attachableTopBlock != null)
                {
                    if (!skipMechanicalTraversal && attachableTopBlock.Base != null && attachableTopBlock.Base.CubeGrid != null && !ShouldStopScan(possibleWeldTargets, possibleGrindTargets, null))
                    {
                        AsyncAddBlocksOfGrid(ref areaBox, useIgnoreColor, ref ignoreColor, useGrindColor, ref grindColor, autoGrindRelation, autoGrindOptions, attachableTopBlock.Base.CubeGrid, grids, possibleSources, seenSources, possibleWeldTargets, possibleGrindTargets);
                    }
                    continue;
                }

                var connector = fatBlock as Sandbox.ModAPI.IMyShipConnector;
                if (connector != null)
                {
                    if (connector.Status == Sandbox.ModAPI.Ingame.MyShipConnectorStatus.Connected && connector.OtherConnector != null && !ShouldStopScan(possibleWeldTargets, possibleGrindTargets, null))
                    {
                        AsyncAddBlocksOfGrid(ref areaBox, useIgnoreColor, ref ignoreColor, useGrindColor, ref grindColor, autoGrindRelation, autoGrindOptions, connector.OtherConnector.CubeGrid, grids, possibleSources, seenSources, possibleWeldTargets, possibleGrindTargets);
                    }
                    continue;
                }

                if (possibleWeldTargets != null && ((Settings.Flags & SyncBlockSettings.Settings.AllowBuild) != 0)) //If projected blocks should be build
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

                            //Add buildable blocks
                            var projectedCubeGrid = projector.ProjectedGrid;
                            if (projectedCubeGrid != null && !grids.Contains(projectedCubeGrid))
                            {
                                grids.Add(projectedCubeGrid);
                                // Do NOT call EnsureSubscribed — projected grids are ghost grids.

                                // Fetch only the buildable subset — avoids iterating the full projection.
                                var buildableBlocks = new List<IMySlimBlock>();
                                projectedCubeGrid.GetBlocks(buildableBlocks, b => b.CanBuild(false));

                                foreach (IMySlimBlock block in buildableBlocks)
                                {
                                    if (ShouldStopScan(possibleWeldTargets, possibleGrindTargets, null)) break;

                                    double distance;
                                    if (DlcCheckHelper.IsBlockDlcAvailableForOwner(block, _Welder.OwnerId)
                                        && BlockWeldPriority.GetEnabled(block)
                                        && block.IsInRange(ref areaBox, out distance))
                                    {
                                        if (possibleWeldTargets.Count < MaxPossibleWeldTargets)
                                            possibleWeldTargets.Add(new TargetBlockData(block, distance, TargetBlockData.AttributeFlags.Projected));
                                    }
                                }
                            }
                        }
                        continue;
                    }
                }
            }

            // Empty-grid ignore: update the cluster-wide ignore entry for this non-own grid.
            // We only WRITE when both possibleWeldTargets and possibleGrindTargets were provided
            // (i.e. this BaR checked both), so we never mark a grid empty based on a partial check.
            // A GrindOnly or WeldOnly BaR can still READ the shared ignore (set by a full-mode BaR)
            // but cannot contaminate it with a one-sided verdict.
            if (!isIgnored && !isOwnGrid && emptyGridIgnoreSeconds > 0 && clusterKey != 0
                && possibleWeldTargets != null && possibleGrindTargets != null)
            {
                var anyTargets = possibleWeldTargets.Count > emptyIgnoreWeldBefore
                              || possibleGrindTargets.Count > emptyIgnoreGrindBefore;
                if (anyTargets)
                    ScanCoordinator.ClearGridIgnored(clusterKey, cubeGrid.EntityId);
                else
                    ScanCoordinator.SetGridIgnored(clusterKey, cubeGrid.EntityId,
                        MyAPIGateway.Session.ElapsedPlayTime + TimeSpan.FromSeconds(emptyGridIgnoreSeconds));
            }
        }

        private bool ShouldStopScan(List<TargetBlockData> possibleWeldTargets, List<TargetBlockData> possibleGrindTargets, List<TargetEntityData> possibleFloatingTargets)
        {
            var weldFull = possibleWeldTargets == null || possibleWeldTargets.Count >= MaxPossibleWeldTargets;
            var grindFull = possibleGrindTargets == null || possibleGrindTargets.Count >= MaxPossibleGrindTargets;
            var floatingFull = possibleFloatingTargets == null || possibleFloatingTargets.Count >= MaxPossibleFloatingTargets;
            return weldFull && grindFull && floatingFull;
        }

        private void AsyncAddBlockIfTargetOrSource(ref MyOrientedBoundingBoxD areaBox, bool useIgnoreColor, ref uint ignoreColor, bool useGrindColor, ref uint grindColor, AutoGrindRelation autoGrindRelation, AutoGrindOptions autoGrindOptions, IMySlimBlock block, List<IMyInventory> possibleSources, HashSet<IMyInventory> seenSources, List<TargetBlockData> possibleWeldTargets, List<TargetBlockData> possibleGrindTargets)
        {
            try
            {
                if (ShouldStopScan(possibleWeldTargets, possibleGrindTargets, null))
                {
                    return;
                }

                if (possibleSources != null)
                {
                    //Search for sources of components (Container, Assembler, Welder, Grinder, ?)
                    var terminalBlock = block.FatBlock as IMyTerminalBlock;

                    if (terminalBlock != null && terminalBlock.EntityId != _Welder.EntityId && terminalBlock.IsFunctional) //Own inventory is no external source (handled internally)
                    {
                        var relation = terminalBlock.GetUserRelationToOwner(_Welder.OwnerId);

                        if (MyRelationsBetweenPlayerAndBlockExtensions.IsFriendly(relation))
                        {
                            try
                            {
                                terminalBlock.AddIfConnectedToInventory(_Welder, possibleSources, seenSources);
                            }
                            catch (Exception ex)
                            {
                                Logging.Instance.Write(Logging.Level.Event, "BuildAndRepairSystemBlock {0}: AsyncAddBlockIfTargetOrSource1 exception: {1}", Logging.BlockName(_Welder, Logging.BlockNameOptions.None), ex);
                            }
                        }
                    }
                }

                var added = false;

                if (possibleGrindTargets != null && (useGrindColor || autoGrindRelation != 0))
                {
                    if (State.SafeZoneAllowsGrinding)
                    {
                        if (possibleGrindTargets.Count < MaxPossibleGrindTargets)
                        {
                            added = AsyncAddBlockIfGrindTarget(ref areaBox, useGrindColor, ref grindColor, autoGrindRelation, autoGrindOptions, block, possibleGrindTargets);
                        }
                    }
                }

                if (possibleWeldTargets != null && !added) //Do not weld if in grind list (could happen if auto grind neutrals is enabled and "HelpOthers" is active)
                {
                    if (possibleWeldTargets.Count < MaxPossibleWeldTargets)
                    {
                        AsyncAddBlockIfWeldTarget(ref areaBox, useIgnoreColor, ref ignoreColor, useGrindColor, ref grindColor, block, possibleWeldTargets);
                    }
                }
            }
            catch (Exception ex)
            {
                Logging.Instance.Error("BuildAndRepairSystemBlock {0}: AsyncAddBlockIfTargetOrSource2 exception: {1}", Logging.BlockName(_Welder, Logging.BlockNameOptions.None), ex);
                throw;
            }
        }

        /// <summary>
        /// Check if the given slim block is a weld target (in range, owned, damaged, new, ..)
        /// </summary>
        private bool AsyncAddBlockIfWeldTarget(ref MyOrientedBoundingBoxD areaBox, bool useIgnoreColor, ref uint ignoreColor, bool useGrindColor, ref uint grindColor, IMySlimBlock block, List<TargetBlockData> possibleWeldTargets)
        {
            if (possibleWeldTargets != null && possibleWeldTargets.Count >= MaxPossibleWeldTargets)
            {
                return false;
            }

            double distance;
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
                   block.IsInRange(ref areaBox, out distance) &&
                   IsRelationAllowed4Welding(projector.SlimBlock) &&
                   block.CanBuild(false))
                {
                    if (possibleWeldTargets.Count < MaxPossibleWeldTargets)
                    {
                        possibleWeldTargets.Add(new TargetBlockData(block, distance, TargetBlockData.AttributeFlags.Projected));
                        return true;
                    }
                }
            }
            else
            {
                if (!State.SafeZoneAllowsWelding)
                    return false;

                if ((!useIgnoreColor || !IsColorNearlyEquals(ignoreColor, colorMask)) && (!useGrindColor || !IsColorNearlyEquals(grindColor, colorMask)) &&
                   BlockWeldPriority.GetEnabled(block) &&
                   block.IsInRange(ref areaBox, out distance) &&
                   IsRelationAllowed4Welding(block) &&
                   block.NeedRepair(Settings.WeldOptions))
                {
                    if (possibleWeldTargets.Count < MaxPossibleWeldTargets)
                    {
                        possibleWeldTargets.Add(new TargetBlockData(block, distance, 0));
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// Check if the given slim block is a grind target (in range, color )
        /// </summary>
        private bool AsyncAddBlockIfGrindTarget(ref MyOrientedBoundingBoxD areaBox, bool useGrindColor, ref uint grindColor, AutoGrindRelation autoGrindRelation, AutoGrindOptions autoGrindOptions, IMySlimBlock block, List<TargetBlockData> possibleGrindTargets)
        {
            if (possibleGrindTargets != null && possibleGrindTargets.Count >= MaxPossibleGrindTargets)
            {
                return false;
            }

            // Use value cached on main thread before the scan started — avoids per-block session property access
            if (_scanGrindBlockedByScenario)
            {
                return false;
            }

            if (block.IsProjected())
                return false;

            // Skip checks for grinding if destructible blocks is disabled or if grid is Immune. We cannot grind anytihng on the target grid then.
            var cubeGrid = block.CubeGrid as MyCubeGrid;
            if (cubeGrid != null && (!cubeGrid.DestructibleBlocks || cubeGrid.Immune))
            {
                return false;
            }

            var autoGrind = autoGrindRelation != 0 && BlockGrindPriority.GetEnabled(block);
            if (autoGrind)
            {
                // Do not allow grinding if our shields are up.
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
                if (block.IsInRange(ref areaBox, out distance))
                {
                    // Is protected by SafeZone?
                    if (SafeZoneHandler.IsProtectedFromGrinding(block, Welder))
                    {
                        return false;
                    }

                    // Is protected by shields.
                    if (IsShieldProtected(block))
                    {
                        return false;
                    }

                    if (possibleGrindTargets.Count < MaxPossibleGrindTargets)
                    {
                        possibleGrindTargets.Add(new TargetBlockData(block, distance, autoGrind ? TargetBlockData.AttributeFlags.Autogrind : 0));
                        return true;
                    }
                }
            }
            return false;
        }

        private List<IMySlimBlock> GetBlocksFromCache(IMyCubeGrid grid, bool isGrinding = false)
        {
            // Fetch raw blocks from the global shared cache (eliminates N×M GetBlocks() calls).
            var rawBlocks = SharedGridBlockCache.GetBlocks(grid);

            // Route through the shared sorted cache so multiple BaRs with the same priority
            // configuration share one sorted list instead of each copying and re-sorting.
            // The sort signature encodes the handler's enabled/order state plus the grind flag.
            var handler = isGrinding ? (BlockPriorityHandling)BlockGrindPriority : BlockWeldPriority;
            var sortSig = unchecked(handler.GetStateHash() * 397 ^ (isGrinding ? 1 : 0));
            return SharedGridSortedCache.GetOrCreate(
                grid.EntityId, sortSig, rawBlocks,
                list => list.SortByPriorityOnly(handler, false));
        }
    }
}
