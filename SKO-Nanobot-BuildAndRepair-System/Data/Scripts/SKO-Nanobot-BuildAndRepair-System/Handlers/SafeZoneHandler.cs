using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using SKONanobotBuildAndRepairSystem.Models;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
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
            GridIntersectingZones?.Entries?.Clear();

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

            foreach (var kv in Zones)
            {
                var zone = kv.Value;
                if (zone == null || zone.Closed || !zone.Enabled)
                {
                    continue;
                }

                BoundingBoxD targetBox = targetGrid.WorldAABB;
                var checkSphere = new BoundingSphereD(zone.PositionComp.GetPosition(), zone.Radius);
                var targetIntersects = checkSphere.Intersects(targetBox);

                if (targetIntersects)
                {
                    SetSubgridCache(targetGrid, zone.EntityId);
                    return zone;
                }
                else
                {
                    SetSubgridCache(targetGrid, 0);
                }
            }

            return null;
        }

        private static void SetSubgridCache(IMyCubeGrid targetGrid, long zoneId)
        {
            var groups = new List<IMyCubeGrid>();
            MyAPIGateway.GridGroups.GetGroup(targetGrid, GridLinkTypeEnum.Mechanical, groups);
            if (groups != null)
            {
                for (int i = 0; i < groups.Count; i++)
                {
                    GridIntersectingZones.Set(groups[i].EntityId, zoneId);
                }
            }
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

        public static bool IsActionAllowedForSystem(NanobotSystem system, SafeZoneAction action)
        {
            try
            {
                if (!Mod.Settings.SafeZoneCheckEnabled || Zones.Count == 0)
                    return true;

                if (system != null && system.Welder != null)
                {
                    var safeZone = GetIntersectingSafeZone(system.Welder.CubeGrid);
                    if (safeZone != null && safeZone.Enabled)
                    {
                        var isAllowed = safeZone.IsActionAllowed(CastProhibit(MySessionComponentSafeZones.AllowedActions, action), 0L);
                        return isAllowed;
                    }
                }
            }
            catch { }

            return true;
        }

        public static bool IsProtectedFromGrinding(IMySlimBlock targetBlock, IMyCubeBlock attackerBlock)
        {
            try
            {
                if (targetBlock == null) return false;
                if (attackerBlock == null) return false;
                if (!Mod.Settings.SafeZoneCheckEnabled) return false;

                // Try get a safe-zone intersecting with the blocks grid.
                var safeZone = GetIntersectingSafeZone(targetBlock.CubeGrid);

                if (safeZone == null || !safeZone.Enabled)
                    return false;

                // Check if grinding is allowed first.
                var isAllowed = safeZone.IsActionAllowed(CastProhibit(MySessionComponentSafeZones.AllowedActions, SafeZoneAction.Grinding), 0L);

                if (isAllowed)
                {
                    // Check relation between attacker and target.
                    var targetRelation = attackerBlock.GetUserRelationToOwner(targetBlock.OwnerId);

                    // If owner, faction member or not owned, then allow grinding within the safe-zone.
                    if (targetRelation == VRage.Game.MyRelationsBetweenPlayerAndBlock.Owner || targetRelation == VRage.Game.MyRelationsBetweenPlayerAndBlock.FactionShare)
                    {
                        return false;
                    }
                }

                // Cannot grind a protected target block.
                return true;
            }
            catch { }
            return false;
        }

        //public static bool IsProtectedFromWelding(IMySlimBlock targetBlock, IMyCubeBlock attackerBlock, bool needForProjector = false)
        //{
        //    try
        //    {
        //        if (targetBlock == null) return false;
        //        if (attackerBlock == null) return false;
        //        if (!Mod.Settings.SafeZoneCheckEnabled) return false;

        //        // Try get a safe-zone intersecting with the blocks grid.
        //        var safeZone = GetIntersectingSafeZone(targetBlock);
        //        if (safeZone != null && safeZone.Enabled)
        //        {
        //            // Check if welding is allowed first.
        //            var isAllowed = safeZone.IsActionAllowed(CastProhibit(MySessionComponentSafeZones.AllowedActions, needForProjector ? SafeZoneAction.BuildingProjections : SafeZoneAction.Welding), 0L);

        //            if (isAllowed)
        //            {
        //                return false;
        //            }

        //            // Cannot weld a protected target block.
        //            return true;
        //        }
        //    }
        //    catch { }
        //    return false;

        //}
    }
}