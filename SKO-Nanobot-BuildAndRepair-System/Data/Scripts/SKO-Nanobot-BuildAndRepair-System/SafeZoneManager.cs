using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using SpaceEngineers.Game.ModAPI;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRageMath;

namespace SKONanobotBuildAndRepairSystem
{
    /// <summary>
    /// Tracks MySafeZone entities in the world. Lightweight registry with event-driven updates.
    /// </summary>
    public static class SafeZoneManager
    {
        private static readonly ConcurrentDictionary<long, MySafeZone> _zones = new ConcurrentDictionary<long, MySafeZone>();
        private static bool _initialized;

        public static bool Initialized { get { return _initialized; } }

        public static int Count { get { return _zones.Count; } }

        public static void Init()
        {
            if (_initialized) { return; }
            if (MyAPIGateway.Session == null) { return; }

            try
            {
                // Seed from existing entities
                var set = new HashSet<IMyEntity>();
                MyAPIGateway.Entities.GetEntities(set, e => e is MySafeZone);

                foreach (var ent in set)
                {
                    var sz = ent as MySafeZone;
                    if (sz != null)
                    {
                        _zones[sz.EntityId] = sz;
                    }
                }

                // Register event listeners
                MyAPIGateway.Entities.OnEntityAdd += OnEntityAdd;
                MyAPIGateway.Entities.OnEntityRemove += OnEntityRemove;

                _initialized = true;
            }
            catch
            {
                // ignored
            }
        }

        public static void Unload()
        {
            if (!_initialized) { return; }
            try
            {
                MyAPIGateway.Entities.OnEntityAdd -= OnEntityAdd;
                MyAPIGateway.Entities.OnEntityRemove -= OnEntityRemove;
            }
            catch { }

            _zones.Clear();
            _initialized = false;
        }

    private static void OnEntityAdd(IMyEntity ent)
        {
            try
            {
                var sz = ent as MySafeZone;
                if (sz != null)
                {
                    _zones[sz.EntityId] = sz;
                }
            }
            catch { }
        }

    private static void OnEntityRemove(IMyEntity ent)
        {
            try
            {
                var sz = ent as MySafeZone;
                if (sz != null)
                {
                    MySafeZone removed;
                    _zones.TryRemove(sz.EntityId, out removed);
                }
            }
            catch { }
        }

        public static bool TryGet(long entityId, out MySafeZone zone)
        {
            return _zones.TryGetValue(entityId, out zone);
        }

        public static void CopyTo(List<MySafeZone> result)
        {
            if (result == null) { return; }
            result.Clear();
            foreach (var kv in _zones)
            {
                result.Add(kv.Value);
            }
        }

        /// <summary>
        /// Returns the first safe zone intersecting the grid's world AABB, or null if none.
        /// Uses a fast radius-distance precheck before the precise sphere vs AABB test.
        /// </summary>
        public static MySafeZone GetIntersectingSafeZone(VRage.Game.ModAPI.IMyCubeGrid grid)
        {
            if (grid == null)
            {
                return null;
            }

            if (_zones.IsEmpty)
            {
                return null;
            }

            var aabb = grid.WorldAABB;
            var gridCenter = aabb.Center;
            var halfDiag = (aabb.Max - aabb.Min) * 0.5;
            var gridRadius = halfDiag.Length();

            foreach (var kv in _zones)
            {
                var zone = kv.Value;
                if (zone == null || zone.Closed)
                {
                    continue;
                }

                var zoneCenter = zone.PositionComp.GetPosition();
                var maxDist = zone.Radius + gridRadius;
                var maxDistSq = maxDist * maxDist;
                var distSq = Vector3D.DistanceSquared(zoneCenter, gridCenter);

                if (distSq > maxDistSq)
                {
                    continue;
                }

                var sphere = new BoundingSphereD(zoneCenter, zone.Radius);
                if (sphere.Intersects(aabb))
                {
                    return zone;
                }
            }

            return null;
        }

        /// <summary>
        /// Compatibility wrapper: returns true if any safe zone intersects; prefer GetIntersectingSafeZone.
        /// </summary>
        public static bool IntersectsAnySafeZone(VRage.Game.ModAPI.IMyCubeGrid grid)
        {
            return GetIntersectingSafeZone(grid) != null;
        }

        public static T CastProhibit<T>(T ptr, object val) => (T)val;

        public static bool IsProtectedFromGrinding(IMySlimBlock targetBlock, IMyCubeBlock attackerBlock)
        {
            try
            {
                if (targetBlock == null) return false;
                if (attackerBlock == null) return false;
                if(!NanobotBuildAndRepairSystemMod.Settings.SafeZoneCheckEnabled) return false;

                // Try get a safe-zone intersecting with the blocks grid.
                var safeZone = GetIntersectingSafeZone(targetBlock.CubeGrid);
                if(safeZone != null && safeZone.Enabled)
                {
                    BoundingBoxD targetBox;
                    targetBlock.GetWorldBoundingBox(out targetBox, true);

                    BoundingBoxD attackerBox;
                    attackerBlock.SlimBlock.GetWorldBoundingBox(out attackerBox);

                    // Create a new sphere for the safe zone.
                    var checkSphere = new BoundingSphereD(safeZone.PositionComp.GetPosition(), safeZone.Radius);

                    // Get intersections checks..
                    var targetIntersects = checkSphere.Intersects(targetBox);
                    if (targetIntersects)
                    {
                        // Check if grinding is allowed first.
                        var isAllowed = safeZone.IsActionAllowed(CastProhibit(MySessionComponentSafeZones.AllowedActions, 16), 0L, targetBox);

                        if (isAllowed)
                        {
                            if(safeZone.SafeZoneBlockId > 0)
                            {
                                var safeZoneBlock = MyEntities.GetEntityByName(safeZone.SafeZoneBlockId.ToString()) as IMySafeZoneBlock;
                                if (safeZoneBlock != null && safeZoneBlock.Enabled && safeZoneBlock.IsSafeZoneEnabled())
                                {
                                    // Relation between safe-zone block and attacker block.
                                    var safeZoneRelation = safeZoneBlock.GetUserRelationToOwner(attackerBlock.OwnerId);

                                    // If the attacker is the owner or faction member of the safe-zone, allow grinding.
                                    if (safeZoneRelation == VRage.Game.MyRelationsBetweenPlayerAndBlock.Owner || safeZoneRelation == VRage.Game.MyRelationsBetweenPlayerAndBlock.FactionShare)
                                    {
                                        return false;
                                    }
                                }
                            }

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
                }
            }
            catch { }
            return false;
            
        }

        public static bool IsProtectedFromWelding(IMySlimBlock targetBlock, IMyCubeBlock attackerBlock, bool needForProjector = false)
        {
            try
            {
                if (targetBlock == null) return false;
                if (attackerBlock == null) return false;
                if (!NanobotBuildAndRepairSystemMod.Settings.SafeZoneCheckEnabled) return false;

                // Try get a safe-zone intersecting with the blocks grid.
                var safeZone = GetIntersectingSafeZone(targetBlock.CubeGrid);
                if (safeZone != null && safeZone.Enabled)
                {
                    BoundingBoxD targetBox;
                    targetBlock.GetWorldBoundingBox(out targetBox, true);

                    BoundingBoxD attackerBox;
                    attackerBlock.SlimBlock.GetWorldBoundingBox(out attackerBox);

                    // Create a new sphere for the safe zone.
                    var checkSphere = new BoundingSphereD(safeZone.PositionComp.GetPosition(), safeZone.Radius);

                    // Get intersections checks..
                    var targetIntersects = checkSphere.Intersects(targetBox);
                    if (targetIntersects)
                    {
                        // Check if welding is allowed first.
                        var isAllowed = safeZone.IsActionAllowed(CastProhibit(MySessionComponentSafeZones.AllowedActions, 8), 0L, targetBox);

                        if (isAllowed)
                        {
                            if (safeZone.SafeZoneBlockId > 0)
                            {
                                var safeZoneBlock = MyEntities.GetEntityByName(safeZone.SafeZoneBlockId.ToString()) as IMySafeZoneBlock;
                                if (safeZoneBlock != null && safeZoneBlock.Enabled && safeZoneBlock.IsSafeZoneEnabled())
                                {
                                    if (needForProjector)
                                    {
                                        var isBuildingFromProjectorAllowed = safeZone.IsActionAllowed(CastProhibit(MySessionComponentSafeZones.AllowedActions, 512), 0L, targetBox);

                                        if (isBuildingFromProjectorAllowed)
                                        {
                                            return false;
                                        }

                                        return true;
                                    }

                                    return false;
                                }
                            }

                            if (needForProjector)
                            {
                                var isBuildingFromProjectorAllowed = safeZone.IsActionAllowed(CastProhibit(MySessionComponentSafeZones.AllowedActions, 512), 0L, targetBox);

                                if (isBuildingFromProjectorAllowed)
                                {
                                    return false;
                                }

                                return true;
                            }
                        }

                        // Cannot weld a protected target block.
                        return true;
                    }
                }
            }
            catch { }
            return false;

        }

    }
}
