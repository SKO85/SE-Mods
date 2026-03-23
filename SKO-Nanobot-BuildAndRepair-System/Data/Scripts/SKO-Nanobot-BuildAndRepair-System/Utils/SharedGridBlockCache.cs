using Sandbox.ModAPI;
using SKONanobotBuildAndRepairSystem.Profiling;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using VRage.Game.ModAPI;

namespace SKONanobotBuildAndRepairSystem.Utils
{
    /// <summary>
    /// Shared static cache for raw block lists. All BaR systems share one grid.GetBlocks()
    /// API call per grid instead of each calling it independently.
    /// Thread-safe via ConcurrentDictionary for use from background scan threads.
    /// </summary>
    public static class SharedGridBlockCache
    {
        private const double CacheTtlSeconds = 0.5;

        private static readonly ConcurrentDictionary<long, TimeSpan> _timestamps =
            new ConcurrentDictionary<long, TimeSpan>();

        private static readonly ConcurrentDictionary<long, List<IMySlimBlock>> _blocks =
            new ConcurrentDictionary<long, List<IMySlimBlock>>();

        /// <summary>
        /// Gets the raw (unsorted) block list for a grid, using the shared cache.
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
        /// Evicts entries older than 2x the TTL. Call periodically from Mod.cs cleanup.
        /// </summary>
        public static void Cleanup()
        {
            var now = MyAPIGateway.Session.ElapsedPlayTime;
            var evictThreshold = CacheTtlSeconds * 2.0;

            // Two-pass: collect stale keys, then remove.
            var staleKeys = new List<long>();
            foreach (var kvp in _timestamps)
            {
                if ((now - kvp.Value).TotalSeconds > evictThreshold)
                {
                    staleKeys.Add(kvp.Key);
                }
            }

            foreach (var key in staleKeys)
            {
                TimeSpan removed;
                List<IMySlimBlock> removedList;
                _timestamps.TryRemove(key, out removed);
                _blocks.TryRemove(key, out removedList);
            }
        }

        /// <summary>
        /// Clears all cached data. Called on session unload.
        /// </summary>
        public static void Clear()
        {
            _timestamps.Clear();
            _blocks.Clear();
        }
    }
}
