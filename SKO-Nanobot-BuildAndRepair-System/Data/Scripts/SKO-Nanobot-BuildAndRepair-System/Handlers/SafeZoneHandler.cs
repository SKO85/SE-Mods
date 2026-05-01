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
        private static readonly List<MySafeZone> EmptyZoneList = new List<MySafeZone>();

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
                HashSet<IMyEntity> safeZones = new HashSet<IMyEntity>();
                MyAPIGateway.Entities.GetEntities(safeZones, e => e is MySafeZone);

                foreach (var entity in safeZones)
                {
                    Zones[entity.EntityId] = entity as MySafeZone;
                }

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
        /// BUG-143: lightweight periodic alternative to GetSafeZones. Skips the full entity walk
        /// (MyAPIGateway.Entities.GetEntities filtered for MySafeZone) since OnEntityAdd /
        /// OnEntityRemove already maintain the Zones dictionary in real time. The full walk
        /// in GetSafeZones was responsible for 1-3 ms per periodic tick (every 6 s on background)
        /// — pure waste with the events registered. CleanupStaleZones still runs as a guard
        /// against missed events, plus the three TtlCache cleanups that GetSafeZones used to do.
        /// GetSafeZones() is still used at Register() time as the initial seed.
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
            var staleKeys = new List<long>();
            foreach (var pair in Zones)
            {
                if (pair.Value == null || pair.Value.MarkedForClose || pair.Value.Closed)
                {
                    staleKeys.Add(pair.Key);
                }
            }
            MySafeZone removed;
            foreach (var key in staleKeys)
            {
                Zones.TryRemove(key, out removed);
            }
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

                foreach (var zone in zones)
                {
                    if (zone == null || zone.Closed || zone.MarkedForClose || !zone.Enabled)
                    {
                        continue;
                    }

                    var targetIntersects = GridIntersects(targetGrid, zone);
                    if (targetIntersects)
                    {
                        // It intersects, so cache this and also for its subgrids.
                        GridIntersectingZones.Set(targetGrid.EntityId, zone.EntityId);
                        CacheZoneForSubGrids(targetGrid, zone.EntityId);

                        // Return the intersecting zone.
                        return zone;
                    }
                    else
                    {
                        // Get the subgrids.
                        var groups = new List<IMyCubeGrid>();
                        MyAPIGateway.GridGroups.GetGroup(targetGrid, GridLinkTypeEnum.Mechanical, groups);

                        var subGridIntersects = false;
                        foreach (var subGrid in groups)
                        {
                            if (subGrid.EntityId == targetGrid.EntityId)
                                continue;

                            // If a subgrid intersects, then mark the parent too and all other subgrids.
                            if (GridIntersects(subGrid, zone))
                            {
                                subGridIntersects = true;
                                GridIntersectingZones.Set(subGrid.EntityId, zone.EntityId);
                                GridIntersectingZones.Set(targetGrid.EntityId, zone.EntityId);

                                CacheZoneForSubGrids(subGrid, zone.EntityId);
                                return zone;
                            }
                        }

                        // If no subgrid intersection found, then nothing intersects.
                        if (!subGridIntersects)
                        {
                            GridIntersectingZones.Set(targetGrid.EntityId, 0);
                            CacheZoneForSubGrids(targetGrid, 0);
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

        private static void CacheZoneForSubGrids(IMyCubeGrid targetGrid, long zoneId)
        {
            var groups = new List<IMyCubeGrid>();
            MyAPIGateway.GridGroups.GetGroup(targetGrid, GridLinkTypeEnum.Mechanical, groups);

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
            }
            catch (Exception ex) { Logging.Instance.Write(Logging.Level.Error, "SafeZoneHandler.GetActionsAllowedForSystem: {0}", ex.Message); }

            return response;
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

                    if (targetBlock.OwnerId == attackerBlock.OwnerId)
                    {
                        SetIsProtectedFromGrinding(targetBlock, attackerBlock.EntityId, false);
                        return false;
                    }

                    // Check relation between attacker and target.
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
            catch
            {
                SetIsProtectedFromGrinding(targetBlock, attackerBlock.EntityId, false);
                return false;
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