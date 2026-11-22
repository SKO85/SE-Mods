using Sandbox.ModAPI;
using SKONanobotBuildAndRepairSystem.Profiling;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using VRage.ModAPI;
using VRageMath;

namespace SKONanobotBuildAndRepairSystem.Caches
{
    /// <summary>
    /// Shared static cache for GetTopMostEntitiesInBox results. BaRs with nearby
    /// bounding box centers share a single API call instead of each calling independently.
    /// Uses quantized position keys (50m grid) to merge nearby BaRs.
    /// Thread-safe via ConcurrentDictionary for use from background scan threads.
    /// </summary>
    public static class SharedEntityCache
    {
        private const double CacheTtlSeconds = 4.0;
        private const double QuantizeSize = 50.0;

        private static readonly ConcurrentDictionary<long, CachedEntityEntry> _cache =
            new ConcurrentDictionary<long, CachedEntityEntry>();

        /// <summary>
        /// Gets entities in the given bounding box, using the shared cache with quantized
        /// position + extent keys. BUG-260610.8/BUG-260824.6: a hit requires the cached
        /// box to fully CONTAIN the requested box — the quantized-key check alone let
        /// boxes differing by up to 50 m per axis share results, hiding entities in the
        /// difference shell. A containing box yields a superset list; callers filter by
        /// their precise area downstream, so a superset is always safe.
        /// </summary>
        public static List<IMyEntity> GetEntitiesInBox(ref BoundingBoxD areaBoundingBox)
        {
            var profilerTs = MethodProfiler.Start();
            var session = MyAPIGateway.Session;
            if (session == null) return new List<IMyEntity>();
            var posKey = QuantizeToCells(areaBoundingBox.Center);
            var extentKey = QuantizeToCells(areaBoundingBox.HalfExtents);
            long key;
            unchecked
            {
                // Slot key mixes position and extents; exactness is guaranteed by the
                // containment verification on hit, not by this mix.
                key = posKey * 0x100000001B3L ^ extentKey;
            }
            var now = session.ElapsedPlayTime;
            var cacheHit = false;

            try
            {
                CachedEntityEntry entry;
                if (_cache.TryGetValue(key, out entry)
                    && (now - entry.Timestamp).TotalSeconds < CacheTtlSeconds
                    && entry.Box.Contains(areaBoundingBox) == ContainmentType.Contains)
                {
                    cacheHit = true;
                    // Return a copy — caller may modify the list (sorting, etc).
                    return new List<IMyEntity>(entry.Entities);
                }

                // Cache miss — call the API. The new (possibly larger) box overwrites the
                // slot, so same-cell smaller requests hit it afterwards.
                List<IMyEntity> entities;
                lock (MyAPIGateway.Entities)
                {
                    entities = MyAPIGateway.Entities.GetTopMostEntitiesInBox(ref areaBoundingBox);
                }

                var newEntry = new CachedEntityEntry();
                newEntry.Timestamp = now;
                newEntry.Box = areaBoundingBox;
                newEntry.Entities = entities ?? new List<IMyEntity>();

                _cache[key] = newEntry;

                // Return a copy for the caller.
                return new List<IMyEntity>(newEntry.Entities);
            }
            finally
            {
                if (profilerTs != 0L)
                {
                    var _key = key;
                    var _hit = cacheHit;
                    MethodProfiler.StopAndLog("SharedEntityCache.GetEntitiesInBox", profilerTs, () =>
                        string.Format("quantizedKey={0};cacheHit={1}", _key, _hit));
                }
            }
        }

        /// <summary>
        /// Quantizes a 3D vector to QuantizeSize-metre cells and bit-packs the three cell
        /// coords into one long (21 bits each — exact for |coord| up to ~52,000 km, far
        /// beyond any SE world). BUG-260610.8: the previous multiplicative hash collided
        /// systematically (e.g. cells (qx, qy+1, qz-31) and (qx, qy, qz) shared a key).
        /// Nearby positions (within one 50 m cell) still share a key by design.
        /// </summary>
        private static long QuantizeToCells(Vector3D v)
        {
            int qx = (int)Math.Floor(v.X / QuantizeSize);
            int qy = (int)Math.Floor(v.Y / QuantizeSize);
            int qz = (int)Math.Floor(v.Z / QuantizeSize);

            unchecked
            {
                return ((long)((uint)qx & 0x1FFFFF) << 42)
                     | ((long)((uint)qy & 0x1FFFFF) << 21)
                     | (long)((uint)qz & 0x1FFFFF);
            }
        }

        /// <summary>
        /// Evicts entries older than 2x the TTL.
        /// </summary>
        public static void Cleanup()
        {
            var session = MyAPIGateway.Session;
            if (session == null) return;
            var now = session.ElapsedPlayTime;
            var evictThreshold = CacheTtlSeconds * 2.0;

            var staleKeys = new List<long>();
            foreach (var kvp in _cache)
            {
                if ((now - kvp.Value.Timestamp).TotalSeconds > evictThreshold)
                {
                    staleKeys.Add(kvp.Key);
                }
            }

            foreach (var key in staleKeys)
            {
                CachedEntityEntry removed;
                _cache.TryRemove(key, out removed);
            }
        }

        /// <summary>
        /// Clears all cached data. Called on session unload.
        /// </summary>
        public static void Clear()
        {
            _cache.Clear();
        }

        internal class CachedEntityEntry
        {
            public TimeSpan Timestamp;
            // BUG-260824.6: the exact queried box; a hit requires it to contain the
            // requested box (slot collisions and smaller cached boxes are misses).
            public BoundingBoxD Box;
            public List<IMyEntity> Entities;
        }
    }
}
