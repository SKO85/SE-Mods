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
        /// Gets the raw (unsorted) block list for a grid.
        /// Returns a NEW list that the caller can safely modify (filter, sort, etc).
        /// BUG-149: catches the InvalidOperationException raised when the main thread
        /// mutates the grid's internal block HashSet during our background enumeration.
        /// The engine's IMyCubeGrid.GetBlocks(List, Func) enumerates an internal HashSet;
        /// if a block is added/removed mid-enumeration the HashSet enumerator throws
        /// "Collection was modified". Pre-fix this aborted the entire AsyncClusterScan
        /// (server log: 2026-05-01 02:29:08Z). Now we return whatever blocks we collected
        /// before the throw — the caller proceeds with a partial result, and the next
        /// scan cycle (~6 s) will likely succeed with the now-stable grid.
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
                    // BUG-149: see method-level comment. Collection was mutated mid-enumeration.
                    // freshList may contain blocks collected before the throw — return them.
                    // No re-throw: an aborted scan loses ALL blocks for this cluster cycle,
                    // a partial scan only loses the few mutated late entries.
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
