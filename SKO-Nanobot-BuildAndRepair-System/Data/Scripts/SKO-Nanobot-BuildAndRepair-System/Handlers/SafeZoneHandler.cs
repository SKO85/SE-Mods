using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using SKONanobotBuildAndRepairSystem.Caches;
using SKONanobotBuildAndRepairSystem.Extensions;
using SKONanobotBuildAndRepairSystem.Profiling;
using SKONanobotBuildAndRepairSystem.Utils;
using SpaceEngineers.Game.ModAPI;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

using VRage;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRageMath;

namespace SKONanobotBuildAndRepairSystem.Handlers
{
    public static class SafeZoneHandler
    {
        // Sentinel returned by GetSafeZonesInRange when no zone matches; callers MUST NOT mutate.
        private static readonly List<MySafeZone> EmptyZoneList = new List<MySafeZone>();

        // PERF-9: pooled scratch reused by GetSafeZones at Register/seeding time. Avoids
        // a HashSet allocation per call. GetSafeZones runs on the main thread only.
        private static readonly HashSet<IMyEntity> _seedZonesScratch = new HashSet<IMyEntity>();

        // REF-2: pooled scratch for CleanupStaleZones. Maintenance runs on the main thread
        // only (Mod.RebuildSourcesAndTargetsTimer + Register), so a single shared buffer
        // is safe and avoids the per-call List allocation.
        private static readonly List<long> _staleZoneKeys = new List<long>();

        public static readonly ConcurrentDictionary<long, MySafeZone> Zones = new ConcurrentDictionary<long, MySafeZone>();

        private static readonly TtlCache<long, long> GridIntersectingZones = new TtlCache<long, long>(
           defaultTtl: TimeSpan.FromSeconds(15),
           concurrencyLevel: 4,
           comparer: null,
           capacity: 100);

        private static readonly TtlCache<long, long> BlockIntersectingZones = new TtlCache<long, long>(
           defaultTtl: TimeSpan.FromSeconds(15),
           concurrencyLevel: 4,
           comparer: null,
           capacity: 100);

        private static readonly TtlCache<MyTuple<long, long>, bool> ProtectedFromGrindingCache = new TtlCache<MyTuple<long, long>, bool>(
           defaultTtl: TimeSpan.FromSeconds(15),
           concurrencyLevel: 4,
           comparer: new MyTupleComparer<long, long>(),
           capacity: 100);

        public static int GridCacheCount { get { return GridIntersectingZones.Count; } }
        public static int BlockCacheCount { get { return BlockIntersectingZones.Count; } }
        public static int GrindCacheCount { get { return ProtectedFromGrindingCache.Count; } }

        #region Registration

        private static bool _registered = false;

        public static void Register()
        {
            if (_registered || MyAPIGateway.Session == null)
                return;

            if (MyAPIGateway.Session.IsServer)
            {
                // Seed from existing entities
                GetSafeZones();

                // Register event listeners
                MyAPIGateway.Entities.OnEntityAdd += OnEntityAdd;
                MyAPIGateway.Entities.OnEntityRemove += OnEntityRemove;
            }

            _registered = true;
        }

        public static void GetSafeZones()
        {
            var profilerTs = MethodProfiler.Start();
            try
            {
                _seedZonesScratch.Clear();
                MyAPIGateway.Entities.GetEntities(_seedZonesScratch, e => e is MySafeZone);

                foreach (var entity in _seedZonesScratch)
                {
                    Zones[entity.EntityId] = entity as MySafeZone;
                }
                _seedZonesScratch.Clear();

                CleanupStaleZones();
                GridIntersectingZones.CleanupExpired();
                ProtectedFromGrindingCache.CleanupExpired();
                BlockIntersectingZones.CleanupExpired();
            }
            catch (Exception ex) { Logging.Instance.Write(Logging.Level.Error, "SafeZoneHandler.GetSafeZones: {0}", ex.Message); }
            finally
            {
                if (profilerTs != 0L)
                {
                    var _zoneCount = Zones.Count;
                    MethodProfiler.StopAndLog("SafeZoneHandler.GetSafeZones", profilerTs, () =>
                        string.Format("zones={0}", _zoneCount));
                }
            }
        }

        /// <summary>
        /// BUG-143: lightweight periodic cleanup; OnEntityAdd/OnEntityRemove keep
        /// Zones live, so we skip the full GetSafeZones entity walk.
        /// </summary>
        public static void CleanupSafeZones()
        {
            var profilerTs = MethodProfiler.Start();
            try
            {
                CleanupStaleZones();
                GridIntersectingZones.CleanupExpired();
                ProtectedFromGrindingCache.CleanupExpired();
                BlockIntersectingZones.CleanupExpired();
            }
            catch (Exception ex) { Logging.Instance.Write(Logging.Level.Error, "SafeZoneHandler.CleanupSafeZones: {0}", ex.Message); }
            finally
            {
                if (profilerTs != 0L)
                {
                    var _zoneCount = Zones.Count;
                    MethodProfiler.StopAndLog("SafeZoneHandler.CleanupSafeZones", profilerTs, () =>
                        string.Format("zones={0}", _zoneCount));
                }
            }
        }

        public static void Unregister()
        {
            if (!_registered || MyAPIGateway.Session == null)
                return;

            if (MyAPIGateway.Session.IsServer)
            {
                MyAPIGateway.Entities.OnEntityAdd -= OnEntityAdd;
                MyAPIGateway.Entities.OnEntityRemove -= OnEntityRemove;
            }

            Zones?.Clear();
            GridIntersectingZones.Clear();
            ProtectedFromGrindingCache.Clear();
            BlockIntersectingZones.Clear();

            _registered = false;
        }

        #endregion Registration

        private static void OnEntityAdd(IMyEntity ent)
        {
            try
            {
                var sz = ent as MySafeZone;
                if (sz != null)
                {
                    Zones[sz.EntityId] = sz;
                }
            }
            catch (Exception ex) { Logging.Instance.Write(Logging.Level.Error, "SafeZoneHandler.OnEntityAdd: {0}", ex.Message); }
        }

        private static void OnEntityRemove(IMyEntity ent)
        {
            try
            {
                var sz = ent as MySafeZone;
                if (sz != null)
                {
                    MySafeZone removed;
                    Zones.TryRemove(sz.EntityId, out removed);
                }
            }
            catch (Exception ex) { Logging.Instance.Write(Logging.Level.Error, "SafeZoneHandler.OnEntityRemove: {0}", ex.Message); }
        }

        /// <summary>
        /// Removes stale entries from the Zones dictionary (closed or marked-for-close).
        /// Call periodically (e.g., every 100 ticks) to guard against missed OnEntityRemove events.
        /// </summary>
        public static void CleanupStaleZones()
        {
            // REF-2: reuse the pooled buffer instead of allocating per call.
            _staleZoneKeys.Clear();
            foreach (var pair in Zones)
            {
                if (pair.Value == null || pair.Value.MarkedForClose || pair.Value.Closed)
                {
                    _staleZoneKeys.Add(pair.Key);
                }
            }
            MySafeZone removed;
            for (int i = 0; i < _staleZoneKeys.Count; i++)
            {
                Zones.TryRemove(_staleZoneKeys[i], out removed);
            }
            _staleZoneKeys.Clear();
        }

        /// <summary>
        /// Returns the first safe zone intersecting the grid's world AABB, or null if none.
        /// Uses a fast radius-distance precheck before the precise sphere vs AABB test.
        /// </summary>

        public static List<MySafeZone> GetSafeZonesInRange(IMyCubeGrid targetGrid, int range, int take = 2)
        {
            if (Zones.Count == 0) return EmptyZoneList;

            List<MySafeZone> result = null;
            var gridCenter = targetGrid.WorldAABB.Center;
            var count = 0;

            foreach (var kvp in Zones)
            {
                var z = kvp.Value;
                if (z == null || z.Closed || z.MarkedForClose || !z.Enabled)
                    continue;

                var distance = Vector3D.Distance(gridCenter, z.PositionComp.WorldAABB.Center);
                if (distance > range)
                    continue;

                if (result == null)
                    result = new List<MySafeZone>(take);

                result.Add(z);
                count++;

                if (count >= take)
                    break;
            }

            return result ?? EmptyZoneList;
        }

        public static MySafeZone GetIntersectingSafeZone(IMyCubeGrid targetGrid)
        {
            var profilerTs = MethodProfiler.Start();
            var cacheHit = false;
            try
            {
                if (targetGrid == null || Zones.Count == 0)
                {
                    return null;
                }

                long zoneId = 0;
                if (GridIntersectingZones.TryGet(targetGrid.EntityId, out zoneId))
                {
                    cacheHit = true;

                    // No zone intersection.
                    if (zoneId == 0)
                        return null;

                    // Try get the zone.
                    MySafeZone zone;
                    if (Zones.TryGetValue(zoneId, out zone) && !zone.Closed && !zone.MarkedForClose && zone.Enabled)
                    {
                        return zone;
                    }
                }

                // Get safe-zones within 300m range.
                var zones = GetSafeZonesInRange(targetGrid, 300);

                // CON-4 / PERF-2: fetch the mechanical group ONCE per call. Pre-fix, the
                // subgrid-intersect / no-intersect / target-intersect paths each issued their
                // own GridGroups.GetGroup engine call (target-intersects also reissued via
                // CacheZoneForSubGrids), allocating two lists and walking the conveyor topology
                // up to three times for the same target grid. Lazy-fetched so calls that exit
                // before the foreach (no zones in range) pay nothing.
                List<IMyCubeGrid> groups = null;

                foreach (var zone in zones)
                {
                    if (zone == null || zone.Closed || zone.MarkedForClose || !zone.Enabled)
                    {
                        continue;
                    }

                    if (groups == null)
                    {
                        groups = new List<IMyCubeGrid>();
                        MyAPIGateway.GridGroups.GetGroup(targetGrid, GridLinkTypeEnum.Mechanical, groups);
                    }

                    var targetIntersects = GridIntersects(targetGrid, zone);
                    if (targetIntersects)
                    {
                        // It intersects, so cache this and also for its subgrids.
                        GridIntersectingZones.Set(targetGrid.EntityId, zone.EntityId);
                        CacheZoneForSubGrids(targetGrid, zone.EntityId, groups);

                        // Return the intersecting zone.
                        return zone;
                    }
                    else
                    {
                        var subGridIntersects = false;
                        for (int i = 0; i < groups.Count; i++)
                        {
                            var subGrid = groups[i];
                            if (subGrid.EntityId == targetGrid.EntityId)
                                continue;

                            // If a subgrid intersects, then mark the parent too and all other subgrids.
                            if (GridIntersects(subGrid, zone))
                            {
                                subGridIntersects = true;
                                GridIntersectingZones.Set(subGrid.EntityId, zone.EntityId);
                                GridIntersectingZones.Set(targetGrid.EntityId, zone.EntityId);

                                CacheZoneForSubGrids(subGrid, zone.EntityId, groups);
                                return zone;
                            }
                        }

                        // If no subgrid intersection found, then nothing intersects.
                        if (!subGridIntersects)
                        {
                            GridIntersectingZones.Set(targetGrid.EntityId, 0);
                            CacheZoneForSubGrids(targetGrid, 0, groups);
                        }
                    }
                }

                return null;
            }
            finally
            {
                if (profilerTs != 0L)
                {
                    var _hit = cacheHit;
                    MethodProfiler.StopAndLog("SafeZoneHandler.GetIntersectingSafeZone", profilerTs, () =>
                        string.Format("cacheHit={0}", _hit));
                }
            }
        }

        // CON-4 / PERF-2: accepts an already-fetched mechanical group list (the caller
        // typically has one because it just walked the same group). Falls back to fetching
        // its own list when called without one — preserves the existing public surface.
        private static void CacheZoneForSubGrids(IMyCubeGrid targetGrid, long zoneId, List<IMyCubeGrid> groups = null)
        {
            if (groups == null)
            {
                groups = new List<IMyCubeGrid>();
                MyAPIGateway.GridGroups.GetGroup(targetGrid, GridLinkTypeEnum.Mechanical, groups);
            }

            foreach (var subGrid in groups)
            {
                if (subGrid.EntityId == targetGrid.EntityId)
                    continue;
                GridIntersectingZones.Set(subGrid.EntityId, zoneId);
            }
        }

        private static bool GridIntersects(IMyCubeGrid targetGrid, MySafeZone zone)
        {
            BoundingBoxD targetBox = targetGrid.WorldAABB;
            var checkSphere = new BoundingSphereD(zone.PositionComp.WorldAABB.Center, zone.Radius);
            var targetIntersects = checkSphere.Intersects(targetBox);

            return targetIntersects;
        }

        private static bool BlockIntersects(IMySlimBlock targetBlock, MySafeZone zone, bool cache = true)
        {
            if (targetBlock == null) return false;

            if (targetBlock.FatBlock != null && cache)
            {
                long zoneId = 0;
                if (BlockIntersectingZones.TryGet(targetBlock.FatBlock.EntityId, out zoneId))
                {
                    if (zoneId > 0)
                    {
                        return true;
                    }

                    return false;
                }
            }

            BoundingBoxD targetBox;
            targetBlock.GetWorldBoundingBox(out targetBox);

            var checkSphere = new BoundingSphereD(zone.PositionComp.GetPosition(), zone.Radius);
            var targetIntersects = targetBox.Intersects(checkSphere);

            if (targetBlock.FatBlock != null && cache)
            {
                if (targetIntersects)
                {
                    BlockIntersectingZones.Set(targetBlock.FatBlock.EntityId, zone.EntityId);
                }
                else
                {
                    BlockIntersectingZones.Set(targetBlock.FatBlock.EntityId, 0);
                }
            }

            return targetIntersects;
        }

        /// <summary>
        /// Compatibility wrapper: returns true if any safe zone intersects; prefer GetIntersectingSafeZone.
        /// </summary>

        public static T CastProhibit<T>(T ptr, object val) => (T)val;

        public enum SafeZoneAction
        {
            Welding = 8,
            Grinding = 16,
            BuildingProjections = 512
        }

        public struct ActionsState
        {
            public bool IsGrindingAllowed;
            public bool IsWeldingAllowed;
            public bool IsBuildingProjectionsAllowed;
        }

        public static MySafeZone GetIntersectingAttackerSafeZone(NanobotSystem system)
        {
            var safeZones = GetSafeZonesInRange(system.Welder.CubeGrid, 300);
            foreach (var zone in safeZones)
            {
                if (BlockIntersects(system.Welder.SlimBlock, zone, false))
                {
                    return zone;
                }
            }

            return null;
        }

        public static ActionsState GetActionsAllowedForSystem(NanobotSystem system)
        {
            var response = new ActionsState()
            {
                IsGrindingAllowed = true,
                IsWeldingAllowed = true,
                IsBuildingProjectionsAllowed = true,
            };

            try
            {
                if (!Mod.Settings.SafeZoneCheckEnabled || Zones.Count == 0)
                    return response;

                if (system != null && system.Welder != null)
                {
                    var safeZone = GetIntersectingAttackerSafeZone(system);
                    if (safeZone != null && safeZone.Enabled)
                    {
                        response.IsGrindingAllowed = safeZone.IsActionAllowed(CastProhibit(MySessionComponentSafeZones.AllowedActions, SafeZoneAction.Grinding), 0L);
                        response.IsWeldingAllowed = safeZone.IsActionAllowed(CastProhibit(MySessionComponentSafeZones.AllowedActions, SafeZoneAction.Welding), 0L);
                        response.IsBuildingProjectionsAllowed = safeZone.IsActionAllowed(CastProhibit(MySessionComponentSafeZones.AllowedActions, SafeZoneAction.BuildingProjections), 0L);
                        return response;
                    }
                }

                // No intersecting safe zone → permissive (BaR is not inside any zone).
                return response;
            }
            catch (Exception ex)
            {
                Logging.Instance.Write(Logging.Level.Error, "SafeZoneHandler.GetActionsAllowedForSystem: {0}", ex.Message);
                // BUG-260502.1: fail closed. A transient engine exception during
                // safe-zone evaluation must not silently unlock actions that
                // should be restricted. The caller retries on the next tick;
                // by then the engine state has typically stabilised.
                return new ActionsState()
                {
                    IsGrindingAllowed = false,
                    IsWeldingAllowed = false,
                    IsBuildingProjectionsAllowed = false,
                };
            }
        }

        private static void SetIsProtectedFromGrinding(IMySlimBlock targetBlock, long attackerBlockId, bool isProtected)
        {
            if (targetBlock != null && targetBlock.FatBlock != null)
            {
                ProtectedFromGrindingCache.Set(new MyTuple<long, long>(targetBlock.FatBlock.EntityId, attackerBlockId), isProtected);
            }
        }

        public static bool IsProtectedFromGrinding(IMySlimBlock targetBlock, IMyCubeBlock attackerBlock)
        {
            var profilerTs = MethodProfiler.Start();
            var cacheHit = false;
            try
            {
                if (targetBlock == null) return false;
                if (attackerBlock == null) return false;
                if (!Mod.Settings.SafeZoneCheckEnabled) return false;
                if (Zones.Count == 0) return false;

                if (targetBlock.FatBlock != null)
                {
                    var isProtected = false;
                    if (ProtectedFromGrindingCache.TryGet(new MyTuple<long, long>(targetBlock.FatBlock.EntityId, attackerBlock.EntityId), out isProtected))
                    {
                        cacheHit = true;
                        return isProtected;
                    }
                }

                // Try get a safe-zone intersecting with the blocks grid.
                var safeZone = GetIntersectingSafeZone(targetBlock.CubeGrid);

                if (safeZone == null || !safeZone.Enabled)
                {
                    SetIsProtectedFromGrinding(targetBlock, attackerBlock.EntityId, false);
                    return false;
                }

                // Check if grinding is allowed first.
                var isAllowed = safeZone.IsActionAllowed(CastProhibit(MySessionComponentSafeZones.AllowedActions, SafeZoneAction.Grinding), 0L);

                if (isAllowed)
                {
                    if (safeZone.SafeZoneBlockId > 0)
                    {
                        var safeZoneBlock = MyEntities.GetEntityByName(safeZone.SafeZoneBlockId.ToString()) as IMySafeZoneBlock;

                        if (safeZoneBlock == null)
                        {
                            // Entity not loaded or cast failed — default to protected to be safe.
                            SetIsProtectedFromGrinding(targetBlock, attackerBlock.EntityId, true);
                            return true;
                        }

                        // Relation between safeZone owner and attacker.
                        var relationSafeZoneAttacker = attackerBlock.CubeGrid.GetRelationBetweenGridAndPlayer(safeZoneBlock.OwnerId);
                        if (relationSafeZoneAttacker != VRage.Game.MyRelationsBetweenPlayerAndBlock.Owner && relationSafeZoneAttacker != VRage.Game.MyRelationsBetweenPlayerAndBlock.FactionShare)
                        {
                            SetIsProtectedFromGrinding(targetBlock, attackerBlock.EntityId, true);
                            return true;
                        }

                        if (targetBlock.OwnerId == attackerBlock.OwnerId)
                        {
                            SetIsProtectedFromGrinding(targetBlock, attackerBlock.EntityId, false);
                            return false;
                        }

                        // Relation attacker grid and target block.
                        var relationAttackerTarget = targetBlock.CubeGrid.GetRelationBetweenGridAndPlayer(attackerBlock.OwnerId);
                        if (relationAttackerTarget == VRage.Game.MyRelationsBetweenPlayerAndBlock.Owner || relationAttackerTarget == VRage.Game.MyRelationsBetweenPlayerAndBlock.FactionShare || relationAttackerTarget == VRage.Game.MyRelationsBetweenPlayerAndBlock.NoOwnership)
                        {
                            SetIsProtectedFromGrinding(targetBlock, attackerBlock.EntityId, false);
                            return false;
                        }
                    }
                    else
                    {
                        if (targetBlock.OwnerId == attackerBlock.OwnerId)
                        {
                            SetIsProtectedFromGrinding(targetBlock, attackerBlock.EntityId, false);
                            return false;
                        }

                        // Relation between target block and attacker grid.
                        var relationAttackerTarget = targetBlock.CubeGrid.GetRelationBetweenGridAndPlayer(attackerBlock.OwnerId);
                        if (relationAttackerTarget == VRage.Game.MyRelationsBetweenPlayerAndBlock.Owner || relationAttackerTarget == VRage.Game.MyRelationsBetweenPlayerAndBlock.FactionShare)
                        {
                            SetIsProtectedFromGrinding(targetBlock, attackerBlock.EntityId, false);
                            return false;
                        }
                    }

                    // OwnerId equality already covered by both branches above; both early-return
                    // before reaching here. Falling through to the user-relation check.
                    var targetRelation = targetBlock.GetUserRelationToOwner(attackerBlock.OwnerId);

                    // If owner, faction member or not owned, then allow grinding within the safe-zone.
                    if (targetRelation == VRage.Game.MyRelationsBetweenPlayerAndBlock.Owner || targetRelation == VRage.Game.MyRelationsBetweenPlayerAndBlock.FactionShare)
                    {
                        SetIsProtectedFromGrinding(targetBlock, attackerBlock.EntityId, false);
                        return false;
                    }
                }

                // Cannot grind a protected target block.
                SetIsProtectedFromGrinding(targetBlock, attackerBlock.EntityId, true);
                return true;
            }
            catch (Exception ex)
            {
                // BUG-260502.2: fail closed. The original catch wrote false (not
                // protected) into the 15 s TTL cache and returned false, so a
                // single transient exception would let the BaR grind a
                // protected block for up to 15 s. Now: log, return true (treat
                // as protected), and SKIP the cache write so the next call
                // retries clean against live engine state.
                Logging.Instance.Write(Logging.Level.Error, "SafeZoneHandler.IsProtectedFromGrinding: {0}", ex.Message);
                return true;
            }
            finally
            {
                if (profilerTs != 0L)
                {
                    var _hit = cacheHit;
                    MethodProfiler.StopAndLog("SafeZoneHandler.IsProtectedFromGrinding", profilerTs, () =>
                        string.Format("cacheHit={0}", _hit));
                }
            }
        }
    }
}