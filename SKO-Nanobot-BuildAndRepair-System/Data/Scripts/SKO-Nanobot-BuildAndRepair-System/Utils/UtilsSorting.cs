using Sandbox.Game.Entities;
using SKONanobotBuildAndRepairSystem.Handlers;
using SKONanobotBuildAndRepairSystem.Models;
using SKONanobotBuildAndRepairSystem.Profiling;
using System;
using System.Collections.Generic;
using VRage.Game.ModAPI;
using VRageMath;

namespace SKONanobotBuildAndRepairSystem.Utils
{
    public static class UtilsSorting
    {
        public static void SortWithPriorityAndDistance(this List<IMySlimBlock> list, NanobotSystem system, bool isGrinding = false)
        {
            if (list == null)
                return;

            try
            {
                var profilerTs = MethodProfiler.Start();
                try
                {
                BlockPriorityHandling priorityHandler = isGrinding ? system.BlockGrindPriority : system.BlockWeldPriority;

                // Keep blocks enabled in either handler so grind targets aren't excluded
                // when sorting for welding (and vice versa) in mixed work modes.
                var weldPriority = system.BlockWeldPriority;
                var grindPriority = system.BlockGrindPriority;
                list.RemoveAll(i => !weldPriority.GetEnabled(i) && !grindPriority.GetEnabled(i));

                var welderCenter = system.Welder.WorldAABB.Center;

                // Reuse the system's distance dictionary to avoid allocating a fresh one each call.
                // Clear() resets count without deallocating internal arrays — zero GC pressure.
                var distances = system.SortDistanceCache;
                distances.Clear();
                foreach (var block in list)
                {
                    BoundingBoxD bbox;
                    block.GetWorldBoundingBox(out bbox, false);
                    distances[block] = (welderCenter - bbox.Center).Length();
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

                        double distA;
                        distances.TryGetValue(a, out distA);
                        double distB;
                        distances.TryGetValue(b, out distB);

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

                    double distA;
                    distances.TryGetValue(a, out distA);
                    double distB;
                    distances.TryGetValue(b, out distB);

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
                finally
                {
                    MethodProfiler.StopAndLog("SortWithPriorityAndDistance", profilerTs, () =>
                        string.Format("blockCount={0};isGrinding={1}", list.Count, isGrinding));
                }
            }
            catch (Exception ex)
            {
                Logging.Instance.Error("SortWithPriorityAndDistance: Exception during sorting: " + ex.Message);
            }
        }
    }
}
