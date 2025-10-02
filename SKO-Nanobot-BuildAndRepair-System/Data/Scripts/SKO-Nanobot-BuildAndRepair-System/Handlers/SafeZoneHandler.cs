using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using SKONanobotBuildAndRepairSystem.Models;
using SKONanobotBuildAndRepairSystem.Utils;
using SpaceEngineers.Game.ModAPI;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using VRage;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRageMath;

namespace SKONanobotBuildAndRepairSystem.Handlers
{
    public static class SafeZoneHandler
    {
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

        private static readonly TtlCache<MyTuple<long, long>, bool> ProtectedFromGindingCache = new TtlCache<MyTuple<long, long>, bool>(
           defaultTtl: TimeSpan.FromSeconds(15),
           concurrencyLevel: 4,
           comparer: new MyTupleComparer<long, long>(),
           capacity: 100);

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
            try
            {
                HashSet<IMyEntity> safeZones = new HashSet<IMyEntity>();
                MyAPIGateway.Entities.GetEntities(safeZones, e => e is MySafeZone);

                foreach (var entity in safeZones)
                {
                    Zones[entity.EntityId] = entity as MySafeZone;
                }

                GridIntersectingZones.CleanupExpired();
                ProtectedFromGindingCache.CleanupExpired();
                BlockIntersectingZones.CleanupExpired();
            }
            catch { }
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
            ProtectedFromGindingCache.Clear();
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
            catch { }
        }

        private static void OnEntityRemove(IMyEntity ent)
        {
            try
            {
                if (ent is MySafeZone)
                {
                    var sz = ent as MySafeZone;
                    if (sz != null)
                    {
                        MySafeZone removed;
                        Zones.TryRemove(sz.EntityId, out removed);
                    }
                }
            }
            catch { }
        }

        /// <summary>
        /// Returns the first safe zone intersecting the grid's world AABB, or null if none.
        /// Uses a fast radius-distance precheck before the precise sphere vs AABB test.
        /// </summary>

        public static List<MySafeZone> GetSafeZonesInRange(IMyCubeGrid targetGrid, int range, int take = 2)
        {
            if (Zones.Count == 0) return new List<MySafeZone>();

            var zones = Zones.Values.Where(z =>
            {
                if (z != null && !z.Closed && !z.MarkedForClose && z.Enabled)
                {
                    double distance = Vector3D.Distance(targetGrid.WorldAABB.Center, z.PositionComp.WorldAABB.Center);
                    if (distance <= range)
                    {
                        return true;
                    }
                }

                return false;
            }).Take(take).ToList();

            return zones;
        }

        public static MySafeZone GetIntersectingSafeZone(IMyCubeGrid targetGrid)
        {
            if (targetGrid == null || Zones.Count == 0)
            {
                return null;
            }

            long zoneId = 0;
            if (GridIntersectingZones.TryGet(targetGrid.EntityId, out zoneId))
            {
                // No zone intersection.
                if (zoneId == 0)
                    return null;

                // Try get the zone.
                MySafeZone zone;
                if (Zones.TryGetValue(zoneId, out zone))
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
            catch { }

            return response;
        }

        private static void SetIsProtectedFromGrinding(IMySlimBlock targetBlock, long attackerBlockId, bool isProtected)
        {
            if (targetBlock != null && targetBlock.FatBlock != null)
            {
                ProtectedFromGindingCache.Set(new MyTuple<long, long>(targetBlock.FatBlock.EntityId, attackerBlockId), isProtected);
            }
        }

        public static bool IsProtectedFromGrinding(IMySlimBlock targetBlock, IMyCubeBlock attackerBlock)
        {
            try
            {
                if (targetBlock == null) return false;
                if (attackerBlock == null) return false;
                if (!Mod.Settings.SafeZoneCheckEnabled) return false;
                if (Zones.Count == 0) return false;

                if (targetBlock.FatBlock != null)
                {
                    var isProtected = false;
                    if (ProtectedFromGindingCache.TryGet(new MyTuple<long, long>(targetBlock.FatBlock.EntityId, attackerBlock.EntityId), out isProtected))
                    {
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
                    //if (!BlockIntersects(targetBlock, safeZone))
                    //{
                    //    SetIsProtectedFromGrinding(targetBlock, attackerBlock.EntityId, false);
                    //    return false;
                    //}

                    if (safeZone.SafeZoneBlockId > 0)
                    {
                        var safeZoneBlock = MyEntities.GetEntityByName(safeZone.SafeZoneBlockId.ToString()) as IMySafeZoneBlock;

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
            catch { }

            SetIsProtectedFromGrinding(targetBlock, attackerBlock.EntityId, false);
            return false;
        }
    }
}