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
                BlockPriorityHandling priorityHandler;
                if (isGrinding)
                {
                    priorityHandler = system.BlockGrindPriority;
                }
                else
                {
                    priorityHandler = system.BlockWeldPriority;
                }

                // Remove from list when not enabled.
                list.RemoveAll(i => !priorityHandler.GetEnabled(i));

                var welderCenter = system.Welder.WorldAABB.Center;

                list.Sort((a, b) =>
                {
                    var priorityA = priorityHandler.GetPriority(a);
                    var priorityB = priorityHandler.GetPriority(b);

                    // If the priority is the same.
                    if (priorityA == priorityB)
                    {
                        BoundingBoxD bboxA;
                        a.GetWorldBoundingBox(out bboxA, false);
                        var distA = (welderCenter - bboxA.Center).Length();

                        BoundingBoxD bboxB;
                        b.GetWorldBoundingBox(out bboxB, false);
                        var distB = (welderCenter - bboxB.Center).Length();

                        // Welding.
                        if (!isGrinding)
                        {
                            return Utils.CompareDistance(distA, distB);
                        }

                        // Grinding.
                        else
                        {
                            // Check for smallest grid first.
                            if ((system.Settings.Flags & SyncBlockSettings.Settings.GrindSmallestGridFirst) != 0)
                            {
                                var res = ((MyCubeGrid)a.CubeGrid).BlocksCount - ((MyCubeGrid)b.CubeGrid).BlocksCount;
                                return res != 0 ? res : Utils.CompareDistance(distA, distB);
                            }

                            // Check for nearest grid blocks first.
                            if ((system.Settings.Flags & SyncBlockSettings.Settings.GrindNearFirst) != 0)
                            {
                                return Utils.CompareDistance(distA, distB);
                            }

                            // otherwise, farthest grid blocks first.
                            return Utils.CompareDistance(distB, distA);
                        }
                    }
                    else
                    {
                        return priorityA - priorityB;
                    }
                });
            }
            catch { }
        }
    }
}