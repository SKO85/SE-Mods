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

                if (isGrinding)
                {
                    bool grindSmallestFirst = (system.Settings.Flags & SyncBlockSettings.Settings.GrindSmallestGridFirst) != 0;
                    bool grindNearFirst = (system.Settings.Flags & SyncBlockSettings.Settings.GrindNearFirst) != 0;

                    list.Sort((a, b) =>
                    {
                        var priorityA = priorityHandler.GetPriority(a);
                        var priorityB = priorityHandler.GetPriority(b);
                        if (priorityA != priorityB)
                            return priorityA - priorityB;

                        BoundingBoxD bboxA;
                        a.GetWorldBoundingBox(out bboxA, false);
                        var distA = (welderCenter - bboxA.Center).Length();

                        BoundingBoxD bboxB;
                        b.GetWorldBoundingBox(out bboxB, false);
                        var distB = (welderCenter - bboxB.Center).Length();

                        if (grindSmallestFirst)
                        {
                            var gridRes = ((MyCubeGrid)a.CubeGrid).BlocksCount - ((MyCubeGrid)b.CubeGrid).BlocksCount;
                            return gridRes != 0 ? gridRes : Utils.CompareDistance(distA, distB);
                        }

                        return grindNearFirst ? Utils.CompareDistance(distA, distB) : Utils.CompareDistance(distB, distA);
                    });
                    return;
                }

                // Welding: priority first, then nearest distance as tiebreaker.
                list.Sort((a, b) =>
                {
                    var priorityA = priorityHandler.GetPriority(a);
                    var priorityB = priorityHandler.GetPriority(b);
                    if (priorityA != priorityB)
                        return priorityA - priorityB;

                    BoundingBoxD bboxA;
                    a.GetWorldBoundingBox(out bboxA, false);
                    var distA = (welderCenter - bboxA.Center).Length();

                    BoundingBoxD bboxB;
                    b.GetWorldBoundingBox(out bboxB, false);
                    var distB = (welderCenter - bboxB.Center).Length();

                    return Utils.CompareDistance(distA, distB);
                });
            }
            catch { }
        }
    }
}
