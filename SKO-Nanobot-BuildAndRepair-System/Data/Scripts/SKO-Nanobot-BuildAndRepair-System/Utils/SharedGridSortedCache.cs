namespace SKONanobotBuildAndRepairSystem.Utils
{
    using Sandbox.ModAPI;
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using VRage.Game.ModAPI;

    /// <summary>
    /// Shared priority-sorted (distance-free) block list cache keyed by (gridId, sortSignature).
    /// One sorted list is shared across all NanobotSystems with the same priority configuration.
    /// Callers MUST NOT mutate the returned list.
    /// Primary eviction is event-driven (block add/remove); TTL is a safety net.
    /// </summary>
    internal static class SharedGridSortedCache
    {
        private const double TtlSeconds = 60.0;

        private struct CacheKey : IEquatable<CacheKey>
        {
            public readonly long GridId;
            public readonly int SortSig;

            public CacheKey(long gridId, int sortSig) { GridId = gridId; SortSig = sortSig; }

            public bool Equals(CacheKey other) => GridId == other.GridId && SortSig == other.SortSig;
            public override bool Equals(object obj) => obj is CacheKey && Equals((CacheKey)obj);
            public override int GetHashCode() => GridId.GetHashCode() * 397 ^ SortSig;
        }

        private struct CacheEntry
        {
            public List<IMySlimBlock> Blocks;
            public TimeSpan Timestamp;
        }

        private static readonly ConcurrentDictionary<CacheKey, CacheEntry> _cache
            = new ConcurrentDictionary<CacheKey, CacheEntry>();

        // Per-key lock objects prevent two BaRs from sorting the same list simultaneously.
        private static readonly ConcurrentDictionary<CacheKey, object> _locks
            = new ConcurrentDictionary<CacheKey, object>();

        /// <summary>
        /// Returns a shared sorted list for (gridId, sortSig). Creates+sorts on cache miss.
        /// sortFunc receives a raw-block copy and must sort/filter it in-place.
        /// Callers MUST NOT mutate the returned list.
        /// </summary>
        internal static List<IMySlimBlock> GetOrCreate(
            long gridId, int sortSig, List<IMySlimBlock> rawBlocks,
            Action<List<IMySlimBlock>> sortFunc)
        {
            var key = new CacheKey(gridId, sortSig);
            var playTime = MyAPIGateway.Session.ElapsedPlayTime;

            // Fast path: cache hit within TTL.
            CacheEntry entry;
            if (_cache.TryGetValue(key, out entry)
                && playTime.Subtract(entry.Timestamp).TotalSeconds < TtlSeconds)
            {
                return entry.Blocks;
            }

            // Slow path: acquire per-key lock and build sorted list.
            var keyLock = _locks.GetOrAdd(key, _ => new object());
            lock (keyLock)
            {
                // Double-check inside the lock.
                if (_cache.TryGetValue(key, out entry)
                    && playTime.Subtract(entry.Timestamp).TotalSeconds < TtlSeconds)
                {
                    return entry.Blocks;
                }

                var list = new List<IMySlimBlock>(rawBlocks);
                sortFunc(list);
                _cache[key] = new CacheEntry { Blocks = list, Timestamp = playTime };
                return list;
            }
        }

        /// <summary>
        /// Remove all entries for a grid (call on structural grid change).
        /// </summary>
        internal static void Invalidate(long gridId)
        {
            foreach (var key in _cache.Keys)
            {
                if (key.GridId == gridId)
                {
                    CacheEntry removed;
                    object removedLock;
                    _cache.TryRemove(key, out removed);
                    _locks.TryRemove(key, out removedLock);
                }
            }
        }

        /// <summary>
        /// Evict entries whose TTL has expired. Call from a periodic Mod cleanup.
        /// </summary>
        internal static void CleanupExpired()
        {
            var playTime = MyAPIGateway.Session.ElapsedPlayTime;
            foreach (var key in _cache.Keys)
            {
                CacheEntry entry;
                if (_cache.TryGetValue(key, out entry)
                    && playTime.Subtract(entry.Timestamp).TotalSeconds >= TtlSeconds)
                {
                    CacheEntry removed;
                    object removedLock;
                    _cache.TryRemove(key, out removed);
                    _locks.TryRemove(key, out removedLock);
                }
            }
        }

        /// <summary>
        /// Wipe the entire cache. Call from Mod.UnloadData().
        /// </summary>
        internal static void Clear()
        {
            _cache.Clear();
            _locks.Clear();
        }
    }
}
