using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace SKONanobotBuildAndRepairSystem.Cluster
{
    /// <summary>
    /// BUG-166: per-grid scan-result cache (3s TTL) so two clusters scanning the same
    /// grid don't redo the work. Keyed on (gridId, paramsHash); only valid when
    /// skipRangeCheck=true (multi-member clusters).
    /// </summary>
    internal static class GridScanCache
    {
        private const long TtlTicks = 3 * TimeSpan.TicksPerSecond;

        // Cleanup floor — only walk when the dict accumulated enough entries.
        private const int CleanupMinEntries = 32;
        private static long _lastCleanupTicks;
        private const long CleanupIntervalTicks = 5 * TimeSpan.TicksPerSecond;

        internal class Entry
        {
            public int ParamsHash;
            public long TimestampTicks;
            public List<ClusterTargetCandidate> WeldCandidates;
            public List<ClusterTargetCandidate> GrindCandidates;
        }

        private static readonly ConcurrentDictionary<long, Entry> _cache = new ConcurrentDictionary<long, Entry>();

        public static int Count { get { return _cache.Count; } }

        /// <summary>
        /// Returns the cached entry if present, fresh, and the params hash matches. Otherwise null.
        /// </summary>
        public static Entry TryGet(long gridId, int paramsHash)
        {
            Entry entry;
            if (!_cache.TryGetValue(gridId, out entry)) return null;
            if (entry.ParamsHash != paramsHash) return null;
            if (DateTime.UtcNow.Ticks - entry.TimestampTicks > TtlTicks) return null;
            return entry;
        }

        /// <summary>
        /// Stores the per-grid scan contribution. The provided lists are wrapped in fresh
        /// List<> instances so subsequent caller mutations don't affect the cache and vice
        /// versa. The struct elements (ClusterTargetCandidate) are copied by value.
        /// </summary>
        public static void Set(long gridId, int paramsHash, List<ClusterTargetCandidate> weld, List<ClusterTargetCandidate> grind)
        {
            var entry = new Entry
            {
                ParamsHash = paramsHash,
                TimestampTicks = DateTime.UtcNow.Ticks,
                WeldCandidates = weld != null && weld.Count > 0 ? new List<ClusterTargetCandidate>(weld) : null,
                GrindCandidates = grind != null && grind.Count > 0 ? new List<ClusterTargetCandidate>(grind) : null,
            };
            _cache[gridId] = entry;
            MaybeCleanup();
        }

        /// <summary>
        /// Removes a single grid's entry. Call when the grid is closed/destroyed.
        /// </summary>
        public static void Invalidate(long gridId)
        {
            Entry removed;
            _cache.TryRemove(gridId, out removed);
        }

        public static void Clear()
        {
            _cache.Clear();
        }

        private static void MaybeCleanup()
        {
            if (_cache.Count < CleanupMinEntries) return;
            var now = DateTime.UtcNow.Ticks;
            if (now - _lastCleanupTicks < CleanupIntervalTicks) return;
            _lastCleanupTicks = now;

            // Two-pass: collect expired keys, then remove. Avoids modifying dict during enumeration.
            List<long> expired = null;
            foreach (var kvp in _cache)
            {
                if (now - kvp.Value.TimestampTicks > TtlTicks)
                {
                    if (expired == null) expired = new List<long>();
                    expired.Add(kvp.Key);
                }
            }
            if (expired != null)
            {
                Entry dummy;
                for (int i = 0; i < expired.Count; i++) _cache.TryRemove(expired[i], out dummy);
            }
        }
    }
}
