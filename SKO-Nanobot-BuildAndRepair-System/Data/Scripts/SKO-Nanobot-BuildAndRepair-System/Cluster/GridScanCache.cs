using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace SKONanobotBuildAndRepairSystem.Cluster
{
    /// <summary>
    /// BUG-166: Per-grid scan-result cache to deduplicate work when multiple cluster
    /// coordinators scan the same target grid in the same scan window. The user's profile
    /// showed two clusters of 58 BaRs each scanning the same 8K-block grid in parallel
    /// (~89 ms each = ~178 ms total wasted bg-thread time per scan cycle).
    ///
    /// The cluster key in ScanClusterCoordinator includes the BaR's HOME grid EntityId, so
    /// BaRs on different home grids form different clusters even when they target the same
    /// grid. For multi-member clusters skipRangeCheck=true, so the per-grid scan output is
    /// purely a function of (target gridId, scan parameters) — independent of the cluster.
    /// Cache it.
    ///
    /// The cache stores the weld + grind candidates contributed by ONE grid's scan.
    /// On hit, the second cluster appends them (respecting its own per-cluster caps) and
    /// runs only the cheap fat-block iteration for connection traversal.
    ///
    /// TTL is short (3 s) so block-state changes (raze, projector update, integrity rise)
    /// self-correct within one scan cycle. The cache is keyed on (gridId, paramsHash);
    /// any setting change that would alter the scan output produces a different hash and
    /// misses, so settings tweaks don't see stale results.
    /// </summary>
    internal static class GridScanCache
    {
        // ~3 second TTL. Short enough that block changes show up in the next scan; long
        // enough that two cluster scans launched in the same scan-trigger second hit each
        // other's freshly-written entry.
        private const long TtlTicks = 3 * TimeSpan.TicksPerSecond;

        // Stale-cleanup floor: only walk the dictionary when it has accumulated enough
        // entries to be worth scanning. With per-grid keys and a 3 s TTL, real workload
        // sizes stay small (10-50 entries). The cleanup walk is O(n).
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
