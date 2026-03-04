namespace SKONanobotBuildAndRepairSystem.Utils
{
    using Sandbox.Game.Entities;
    using Sandbox.ModAPI;
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using VRage.Game.ModAPI;

    /// <summary>
    /// Static thread-safe cache of raw (unsorted, unfiltered) block lists keyed by grid entity ID.
    /// Callers MUST copy the returned list before mutating it.
    /// </summary>
    internal static class SharedGridBlockCache
    {
        private const double TtlSeconds = 8.0;

        private static readonly ConcurrentDictionary<long, bool> _subscribedGrids
            = new ConcurrentDictionary<long, bool>();

        private static readonly ConcurrentDictionary<long, TimeSpan> _cacheTimestamps
            = new ConcurrentDictionary<long, TimeSpan>();

        private static readonly ConcurrentDictionary<long, List<IMySlimBlock>> _cacheBlocks
            = new ConcurrentDictionary<long, List<IMySlimBlock>>();

        // Per-grid lock objects to prevent duplicate GetBlocks() calls for the same grid.
        private static readonly ConcurrentDictionary<long, object> _locks
            = new ConcurrentDictionary<long, object>();

        /// <summary>
        /// Returns the shared raw block list for the given grid.
        /// Callers MUST copy before mutating (sorting/filtering).
        /// </summary>
        internal static List<IMySlimBlock> GetBlocks(IMyCubeGrid grid)
        {
            var playTime = MyAPIGateway.Session.ElapsedPlayTime;
            var gridId = grid.EntityId;

            // Fast path: cache hit within TTL.
            TimeSpan cachedTime;
            if (_cacheTimestamps.TryGetValue(gridId, out cachedTime)
                && playTime.Subtract(cachedTime).TotalSeconds < TtlSeconds)
            {
                List<IMySlimBlock> cached;
                if (_cacheBlocks.TryGetValue(gridId, out cached))
                    return cached;
            }

            // Slow path: acquire per-grid lock and populate.
            var gridLock = _locks.GetOrAdd(gridId, _ => new object());
            lock (gridLock)
            {
                // Double-check inside the lock.
                if (_cacheTimestamps.TryGetValue(gridId, out cachedTime)
                    && playTime.Subtract(cachedTime).TotalSeconds < TtlSeconds)
                {
                    List<IMySlimBlock> cached;
                    if (_cacheBlocks.TryGetValue(gridId, out cached))
                        return cached;
                }

                var blocks = new List<IMySlimBlock>();
                grid.GetBlocks(blocks);

                _cacheBlocks[gridId] = blocks;
                _cacheTimestamps[gridId] = playTime;
                return blocks;
            }
        }

        /// <summary>
        /// Remove a single grid entry (call when the grid closes).
        /// </summary>
        internal static void Invalidate(long gridEntityId)
        {
            TimeSpan ts;
            List<IMySlimBlock> blocks;
            _cacheTimestamps.TryRemove(gridEntityId, out ts);
            _cacheBlocks.TryRemove(gridEntityId, out blocks);
        }

        /// <summary>
        /// Evict entries whose TTL has expired. Call from a periodic Mod cleanup.
        /// </summary>
        internal static void CleanupExpired()
        {
            var playTime = MyAPIGateway.Session.ElapsedPlayTime;
            foreach (var key in _cacheTimestamps.Keys)
            {
                TimeSpan ts;
                if (_cacheTimestamps.TryGetValue(key, out ts)
                    && playTime.Subtract(ts).TotalSeconds >= TtlSeconds)
                {
                    TimeSpan removedTs;
                    List<IMySlimBlock> removedBlocks;
                    object removedLock;
                    _cacheTimestamps.TryRemove(key, out removedTs);
                    _cacheBlocks.TryRemove(key, out removedBlocks);
                    _locks.TryRemove(key, out removedLock);
                }
            }
        }

        /// <summary>
        /// Subscribe to structural change events for a non-projected grid.
        /// Safe to call multiple times — subsequent calls for the same grid are no-ops.
        /// </summary>
        internal static void EnsureSubscribed(IMyCubeGrid grid)
        {
            // Skip projected/ghost grids — they have no real block add/remove events.
            if (((MyCubeGrid)grid).Projector != null) return;

            if (!_subscribedGrids.TryAdd(grid.EntityId, true)) return;

            grid.OnBlockAdded   += b => InvalidateFull(b.CubeGrid.EntityId);
            grid.OnBlockRemoved += b => InvalidateFull(b.CubeGrid.EntityId);
            grid.OnMarkForClose += g =>
            {
                bool dummy;
                _subscribedGrids.TryRemove(g.EntityId, out dummy);
                InvalidateFull(g.EntityId);
            };
        }

        private static void InvalidateFull(long gridId)
        {
            Invalidate(gridId);
            SharedGridSortedCache.Invalidate(gridId);
        }

        /// <summary>
        /// Wipe the entire cache. Call from Mod.UnloadData().
        /// </summary>
        internal static void Clear()
        {
            _cacheTimestamps.Clear();
            _cacheBlocks.Clear();
            _locks.Clear();
            _subscribedGrids.Clear();
        }
    }
}
