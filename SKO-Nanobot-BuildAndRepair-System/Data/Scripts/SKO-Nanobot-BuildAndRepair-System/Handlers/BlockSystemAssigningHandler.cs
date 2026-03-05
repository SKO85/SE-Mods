using SKONanobotBuildAndRepairSystem.Models;
using System;
using VRage.Game.ModAPI;

namespace SKONanobotBuildAndRepairSystem.Handlers
{
    public static class BlockSystemAssigningHandler
    {
        private static TtlCache<IMySlimBlock, long> Cache = new TtlCache<IMySlimBlock, long>(TimeSpan.FromSeconds(8));

        public static bool AssignToSystem(this IMySlimBlock block, long systemId)
        {
            long assignedSystemId;
            if (Cache.TryGet(block, out assignedSystemId))
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
            Cache.Set(block, systemId);
            return true;
        }

        public static void ReleaseFromSystem(this IMySlimBlock block)
        {
            Cache.Remove(block);
        }

        /// <summary>
        /// Releases this block's assignment only if it is currently assigned to the given system.
        /// Safe to call when another system may own the assignment.
        /// </summary>
        public static void ReleaseIfMine(this IMySlimBlock block, long systemId)
        {
            long assignedSystemId;
            if (Cache.TryGet(block, out assignedSystemId) && assignedSystemId == systemId)
                Cache.Remove(block);
        }

        public static void Cleanup()
        {
            Cache.CleanupExpired();
        }

        public static void Clear()
        {
            Cache.Clear();
        }
    }
}