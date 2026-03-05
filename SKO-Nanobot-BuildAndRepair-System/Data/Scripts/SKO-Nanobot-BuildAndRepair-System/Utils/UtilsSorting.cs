using Sandbox.Game.Entities;
using SKONanobotBuildAndRepairSystem.Handlers;
using SKONanobotBuildAndRepairSystem.Models;
using System.Collections.Generic;
using VRage.Game.ModAPI;
using VRageMath;

namespace SKONanobotBuildAndRepairSystem.Utils
{
    public static class UtilsSorting
    {
        /// <summary>
        /// Priority-only sort used by SharedGridSortedCache — no distance/GetWorldBoundingBox calls.
        /// Callers MUST NOT mutate the returned list after storing it in the shared cache.
        /// </summary>
        public static void SortByPriorityOnly(
            this List<IMySlimBlock> list, BlockPriorityHandling handler, bool ignorePriority)
        {
            list.RemoveAll(b => !handler.GetEnabled(b));
            if (!ignorePriority)
                list.Sort((a, b) => handler.GetPriority(a) - handler.GetPriority(b));
        }

        public static void SortWithPriorityAndDistance(this List<IMySlimBlock> list, NanobotSystem system, bool isGrinding = false)
        {
            if (list == null)
                return;

            try
            {
                BlockPriorityHandling priorityHandler = isGrinding ? system.BlockGrindPriority : system.BlockWeldPriority;

                // Filter in-place so the caller's list is actually modified.
                list.RemoveAll(i => !priorityHandler.GetEnabled(i));

                var welderCenter = system.Welder.WorldAABB.Center;

                // Pre-compute distances once (O(N)) so the sort comparator is O(1) per comparison
                // instead of calling GetWorldBoundingBox inside the comparator (O(N log N) API calls).
                var distCache = new Dictionary<IMySlimBlock, double>(list.Count);
                foreach (var blk in list)
                {
                    BoundingBoxD bbox;
                    blk.GetWorldBoundingBox(out bbox, false);
                    distCache[blk] = (welderCenter - bbox.Center).Length();
                }

                if (isGrinding)
                {
                    bool grindSmallestFirst = (system.Settings.Flags & SyncBlockSettings.Settings.GrindSmallestGridFirst) != 0;
                    bool grindNearFirst = (system.Settings.Flags & SyncBlockSettings.Settings.GrindNearFirst) != 0;
                    bool ignorePriorityOrder = (system.Settings.Flags & SyncBlockSettings.Settings.GrindIgnorePriorityOrder) != 0;

                    list.Sort((a, b) =>
                    {
                        if (!ignorePriorityOrder)
                        {
                            var priorityA = priorityHandler.GetPriority(a);
                            var priorityB = priorityHandler.GetPriority(b);
                            if (priorityA != priorityB)
                                return priorityA - priorityB;
                        }

                        double distA, distB;
                        distCache.TryGetValue(a, out distA);
                        distCache.TryGetValue(b, out distB);

                        if (grindSmallestFirst)
                        {
                            var gridRes = ((MyCubeGrid)a.CubeGrid).BlocksCount - ((MyCubeGrid)b.CubeGrid).BlocksCount;
                            return gridRes != 0 ? gridRes : Utils.CompareDistance(distA, distB);
                        }

                        return grindNearFirst ? Utils.CompareDistance(distA, distB) : Utils.CompareDistance(distB, distA);
                    });
                    return;
                }

                // Welding: priority first, then nearest distance, then stable tiebreaker.
                list.Sort((a, b) =>
                {
                    var priorityA = priorityHandler.GetPriority(a);
                    var priorityB = priorityHandler.GetPriority(b);
                    if (priorityA != priorityB)
                        return priorityA - priorityB;

                    double distA, distB;
                    distCache.TryGetValue(a, out distA);
                    distCache.TryGetValue(b, out distB);

                    var distCmp = Utils.CompareDistance(distA, distB);
                    if (distCmp != 0) return distCmp;

                    // Stable tiebreaker: grid entity ID then block grid position
                    var gridCmp = a.CubeGrid.EntityId.CompareTo(b.CubeGrid.EntityId);
                    if (gridCmp != 0) return gridCmp;
                    var posA = a.Position;
                    var posB = b.Position;
                    if (posA.X != posB.X) return posA.X - posB.X;
                    if (posA.Y != posB.Y) return posA.Y - posB.Y;
                    return posA.Z - posB.Z;
                });
            }
            catch { }
        }
    }
}
