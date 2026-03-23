using SKONanobotBuildAndRepairSystem.Profiling;
using System.Collections.Generic;
using VRage.Game.ModAPI;

namespace SKONanobotBuildAndRepairSystem.Utils
{
    /// <summary>
    /// Shared static helper for raw block lists. Wraps grid.GetBlocks() with profiling.
    /// Each call returns a NEW list that the caller can safely modify (filter, sort, etc).
    /// </summary>
    public static class SharedGridBlockCache
    {
        /// <summary>
        /// Gets the raw (unsorted) block list for a grid.
        /// Returns a NEW list that the caller can safely modify (filter, sort, etc).
        /// </summary>
        public static List<IMySlimBlock> GetBlocks(IMyCubeGrid grid)
        {
            var profilerTs = MethodProfiler.Start();
            try
            {
                var freshList = new List<IMySlimBlock>();
                grid.GetBlocks(freshList);
                return freshList;
            }
            finally
            {
                MethodProfiler.StopAndLog("SharedGridBlockCache.GetBlocks", profilerTs, () =>
                    string.Format("gridId={0}", grid.EntityId));
            }
        }

        /// <summary>
        /// No-op. Retained for API compatibility with Mod.cs cleanup calls.
        /// </summary>
        public static void Cleanup()
        {
        }

        /// <summary>
        /// No-op. Retained for API compatibility with Mod.cs unload calls.
        /// </summary>
        public static void Clear()
        {
        }
    }
}
