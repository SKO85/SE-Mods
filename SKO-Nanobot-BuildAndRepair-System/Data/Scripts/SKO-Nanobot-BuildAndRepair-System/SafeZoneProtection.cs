using System;
using System.Collections.Concurrent;
using System.Linq;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using SpaceEngineers.Game.ModAPI;
using VRage.Game.ModAPI;
using VRageMath;

namespace SKONanobotBuildAndRepairSystem
{
    public static class SafeZoneProtection
    {
        private static readonly int IntervalToCacheSeconds = 15;

        public struct EntityProtectedState
        {
            public bool IsGrindingAllowed;
            public TimeSpan Checked;
        }

        public static ConcurrentDictionary<long, EntityProtectedState> GrindingNotAllowedCache = new ConcurrentDictionary<long, EntityProtectedState>();

        public static T CastProhibit<T>(T ptr, object val) => (T)val;

        private static bool? IsGrindingAllowed(long entityId)
        {
            try
            {
                if (GrindingNotAllowedCache.ContainsKey(entityId))
                {
                    if (MyAPIGateway.Session.ElapsedPlayTime.Subtract(GrindingNotAllowedCache[entityId].Checked).TotalSeconds > IntervalToCacheSeconds)
                    {
                        EntityProtectedState state;
                        GrindingNotAllowedCache.TryRemove(entityId, out state);
                    }
                    else
                    {
                        return GrindingNotAllowedCache[entityId].IsGrindingAllowed;
                    }
                }
            }
            catch
            {
                // ignored
            }

            return null;
        }

        public static void SetIsGrindingAllowed(long entityId, bool isGrindingAllowed)
        {
            try
            {
                GrindingNotAllowedCache[entityId] = new EntityProtectedState()
                {
                    IsGrindingAllowed = isGrindingAllowed,
                    Checked = MyAPIGateway.Session.ElapsedPlayTime
                };
            }
            catch
            {
                // ignored
            }
        }

        //public static bool IsGridAllowedGrinding(MyCubeGrid grid)
        //{
        //    try
        //    {
        //        var isGrindingAllowed = IsGrindingAllowed(grid.EntityId);
        //        if (isGrindingAllowed.HasValue)
        //        {
        //            return isGrindingAllowed.Value;
        //        }
        //        else
        //        {
        //            isGrindingAllowed = MySessionComponentSafeZones.IsActionAllowed(grid, CastProhibit(MySessionComponentSafeZones.AllowedActions, 16));
        //            SetIsProtected(grid.EntityId, isGrindingAllowed.Value);
        //            return isGrindingAllowed.Value;
        //        }
        //    }
        //    catch
        //    {
        //        // ignored
        //    }

        //    return true;
        //}

        public static bool IsProtectedFromGrinding(IMySlimBlock targetBlock, IMyCubeBlock attackerBlock)
        {
            try
            {
                if (targetBlock != null && attackerBlock != null)
                {
                    long fatBlockId = 0;
                    if (targetBlock.FatBlock != null)
                    {
                        fatBlockId = targetBlock.FatBlock.EntityId;
                        var cached = IsGrindingAllowed(fatBlockId);
                        if (cached != null)
                        {
                            return !cached.Value;
                        }
                    }

                    var sphere = new BoundingSphereD(attackerBlock.GetPosition(), 500);
                    var list = MyAPIGateway.Entities.GetEntitiesInSphere(ref sphere);
                    var safeZones = list.OfType<MySafeZone>().ToList();

                    if (safeZones.Any())
                    {
                        BoundingBoxD targetBox;
                        targetBlock.GetWorldBoundingBox(out targetBox, true);

                        BoundingBoxD attackerBox;
                        attackerBlock.SlimBlock.GetWorldBoundingBox(out attackerBox);

                        foreach (var safeZone in safeZones)
                        {
                            // Create a new sphere for the safe zone.
                            var checkSphere = new BoundingSphereD(safeZone.PositionComp.GetPosition(), safeZone.Radius);

                            // Get intersections checks..
                            var targetIntersects = checkSphere.Intersects(targetBox);
                            if (targetIntersects)
                            {
                                // If it is a safe-zone block.
                                if (safeZone.SafeZoneBlockId > 0)
                                {
                                    var safeZoneBlock = MyEntities.GetEntityByName(safeZone.SafeZoneBlockId.ToString()) as IMySafeZoneBlock;
                                    
                                    
                                    if (safeZoneBlock != null && safeZoneBlock.Enabled && safeZoneBlock.IsSafeZoneEnabled())
                                    {
                                        var safeZoneRelation = safeZoneBlock.GetUserRelationToOwner(attackerBlock.OwnerId);

                                        var isAllowed = safeZone.IsActionAllowed(CastProhibit(MySessionComponentSafeZones.AllowedActions, 16), 0L, targetBox);
                                        if(isAllowed)
                                        {
                                            var targetBlockOwnerId = targetBlock.OwnerId;

                                            // Check relation between owners.
                                            var relation = attackerBlock.GetUserRelationToOwner(targetBlockOwnerId);
                                            if (relation == VRage.Game.MyRelationsBetweenPlayerAndBlock.Owner || relation == VRage.Game.MyRelationsBetweenPlayerAndBlock.FactionShare)
                                            {
                                                if (fatBlockId > 0)
                                                {
                                                    SetIsGrindingAllowed(fatBlockId, true);
                                                }

                                                return false;
                                            }

                                            if(safeZoneRelation == VRage.Game.MyRelationsBetweenPlayerAndBlock.Owner || safeZoneRelation == VRage.Game.MyRelationsBetweenPlayerAndBlock.FactionShare)
                                            {
                                                if (fatBlockId > 0)
                                                {
                                                    SetIsGrindingAllowed(fatBlockId, true);
                                                }

                                                return false;
                                            }
                                        }

                                        if (fatBlockId > 0)
                                        {
                                            SetIsGrindingAllowed(fatBlockId, false);
                                        }

                                        return true;
                                    }
                                }

                                if (fatBlockId > 0)
                                {
                                    SetIsGrindingAllowed(fatBlockId, false);
                                }

                                return true;
                            }
                        }
                    }
                }
            }
            catch
            {
                // ignored
            }

            return false;
        }

        public static bool IsProtectedFromWelding(IMySlimBlock targetBlock, IMyCubeBlock attackerBlock, bool needForProjector = false)
        {
            try
            {
                if (targetBlock != null && attackerBlock != null)
                {
                    long fatBlockId = 0;
                    if (targetBlock.FatBlock != null)
                    {
                        fatBlockId = targetBlock.FatBlock.EntityId;                       
                    }

                    var sphere = new BoundingSphereD(attackerBlock.GetPosition(), 500);
                    var list = MyAPIGateway.Entities.GetEntitiesInSphere(ref sphere);
                    var safeZones = list.OfType<MySafeZone>().ToList();

                    if (safeZones.Any())
                    {
                        BoundingBoxD targetBox;
                        targetBlock.GetWorldBoundingBox(out targetBox, true);

                        BoundingBoxD attackerBox;
                        attackerBlock.SlimBlock.GetWorldBoundingBox(out attackerBox);

                        foreach (var safeZone in safeZones)
                        {
                            // Create a new sphere for the safe zone.
                            var checkSphere = new BoundingSphereD(safeZone.PositionComp.GetPosition(), safeZone.Radius);

                            // Get intersections checks..
                            var targetIntersects = checkSphere.Intersects(targetBox);
                            if (targetIntersects)
                            {
                                // If it is a safe-zone block.
                                if (safeZone.SafeZoneBlockId > 0)
                                {
                                    var safeZoneBlock = MyEntities.GetEntityByName(safeZone.SafeZoneBlockId.ToString()) as IMySafeZoneBlock;

                                    if (safeZoneBlock != null && safeZoneBlock.Enabled && safeZoneBlock.IsSafeZoneEnabled())
                                    {
                                        var isAllowed = safeZone.IsActionAllowed(CastProhibit(MySessionComponentSafeZones.AllowedActions, 8), 0L, targetBox);
                                        if (isAllowed)
                                        {
                                            if(needForProjector)
                                            {
                                                var isBuildingFromProjectorAllowed = safeZone.IsActionAllowed(CastProhibit(MySessionComponentSafeZones.AllowedActions, 512), 0L, targetBox);
                                                if(isBuildingFromProjectorAllowed)
                                                {
                                                    return false;
                                                }

                                                return true;
                                            }

                                            return false;
                                        }

                                        return true;
                                    }
                                }

                                if (fatBlockId > 0)
                                {
                                    SetIsGrindingAllowed(fatBlockId, false);
                                }

                                return true;
                            }
                        }
                    }
                }
            }
            catch
            {
                // ignored
            }

            return false;
        }
    }
}
