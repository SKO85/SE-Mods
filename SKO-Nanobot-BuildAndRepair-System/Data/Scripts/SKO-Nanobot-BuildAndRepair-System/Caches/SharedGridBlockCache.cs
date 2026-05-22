using SKONanobotBuildAndRepairSystem.Profiling;
using System;
using System.Collections.Generic;
using VRage.Game.ModAPI;

namespace SKONanobotBuildAndRepairSystem.Caches
{
    /// <summary>
    /// Shared static helper for raw block lists. Wraps grid.GetBlocks() with profiling.
    /// Each call returns a NEW list that the caller can safely modify (filter, sort, etc).
    /// </summary>
    public static class SharedGridBlockCache
    {
        /// <summary>
        /// Gets the raw block list for a grid (caller may freely modify the returned list).
        /// BUG-149: catches mid-enumeration mutations from the main thread; returns whatever
        /// was collected before the throw rather than aborting the entire scan.
        /// </summary>
        public static List<IMySlimBlock> GetBlocks(IMyCubeGrid grid)
        {
            var profilerTs = MethodProfiler.Start();
            var freshList = new List<IMySlimBlock>();
            var partial = false;
            try
            {
                try
                {
                    grid.GetBlocks(freshList);
                }
                catch (InvalidOperationException)
                {
                    // BUG-149: collection mutated mid-enumeration; return the partial list.
                    partial = true;
                }
                return freshList;
            }
            finally
            {
                if (profilerTs != 0L)
                {
                    var _partial = partial;
                    var _count = freshList.Count;
                    MethodProfiler.StopAndLog("SharedGridBlockCache.GetBlocks", profilerTs, () =>
                        string.Format("gridId={0};partial={1};count={2}", grid.EntityId, _partial, _count));
                }
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
