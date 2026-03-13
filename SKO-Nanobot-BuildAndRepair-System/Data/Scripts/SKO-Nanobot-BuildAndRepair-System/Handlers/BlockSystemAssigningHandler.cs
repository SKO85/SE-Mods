using SKONanobotBuildAndRepairSystem.Models;
using SKONanobotBuildAndRepairSystem.Profiling;
using System;
using VRage.Game.ModAPI;

namespace SKONanobotBuildAndRepairSystem.Handlers
{
    public static class BlockSystemAssigningHandler
    {
        private static TtlCache<string, long> Cache = new TtlCache<string, long>(TimeSpan.FromSeconds(8));

        private static string GetBlockKey(IMySlimBlock block)
        {
            return string.Format("{0}:{1}", block.CubeGrid.EntityId, block.Position);
        }

        public static bool IsAssignedToOtherSystem(this IMySlimBlock block, long systemId)
        {
            var key = GetBlockKey(block);
            long assignedSystemId;
            if (Cache.TryGet(key, out assignedSystemId))
            {
                return assignedSystemId != systemId;
            }
            return false;
        }

        public static bool AssignToSystem(this IMySlimBlock block, long systemId)
        {
            var key = GetBlockKey(block);
            long assignedSystemId;
            if (Cache.TryGet(key, out assignedSystemId))
            {
                // Already assigned to this system.
                if (assignedSystemId == systemId)
                {
                    return true;
                }

                // Already assigned to another system.
                return false;
            }

            // Assign to this system.
            Cache.Set(key, systemId);
            return true;
        }

        public static void ReleaseFromSystem(this IMySlimBlock block)
        {
            Cache.Remove(GetBlockKey(block));
        }

        public static void Cleanup()
        {
            var profilerTs = MethodProfiler.Start();
            Cache.CleanupExpired();
            MethodProfiler.StopAndLog("BlockSystemAssigningHandler.Cleanup", profilerTs);
        }

        public static void Clear()
        {
            Cache.Clear();
        }
    }
}