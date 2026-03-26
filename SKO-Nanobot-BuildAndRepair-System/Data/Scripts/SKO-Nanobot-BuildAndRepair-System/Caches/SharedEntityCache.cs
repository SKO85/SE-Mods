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
        /// Gets entities in the given bounding box, using the shared cache with quantized position keys.
        /// </summary>
        public static List<IMyEntity> GetEntitiesInBox(ref BoundingBoxD areaBoundingBox)
        {
            var profilerTs = MethodProfiler.Start();
            var session = MyAPIGateway.Session;
            if (session == null) return new List<IMyEntity>();
            var center = areaBoundingBox.Center;
            var key = QuantizePosition(center);
            var now = session.ElapsedPlayTime;
            var cacheHit = false;

            try
            {
                CachedEntityEntry entry;
                if (_cache.TryGetValue(key, out entry)
                    && (now - entry.Timestamp).TotalSeconds < CacheTtlSeconds)
                {
                    cacheHit = true;
                    // Return a copy — caller may modify the list (sorting, etc).
                    return new List<IMyEntity>(entry.Entities);
                }

                // Cache miss — call the API.
                List<IMyEntity> entities;
                lock (MyAPIGateway.Entities)
                {
                    entities = MyAPIGateway.Entities.GetTopMostEntitiesInBox(ref areaBoundingBox);
                }

                var newEntry = new CachedEntityEntry();
                newEntry.Timestamp = now;
                newEntry.Entities = entities ?? new List<IMyEntity>();

                _cache[key] = newEntry;

                // Return a copy for the caller.
                return new List<IMyEntity>(newEntry.Entities);
            }
            finally
            {
                var _key = key;
                var _hit = cacheHit;
                MethodProfiler.StopAndLog("SharedEntityCache.GetEntitiesInBox", profilerTs, () =>
                    string.Format("quantizedKey={0};cacheHit={1}", _key, _hit));
            }
        }

        /// <summary>
        /// Quantizes a 3D position to a grid of QuantizeSize metres, producing a stable hash key.
        /// Nearby BaRs (within 50m) will get the same key.
        /// </summary>
        private static long QuantizePosition(Vector3D pos)
        {
            // Quantize each axis to the nearest grid cell.
            int qx = (int)Math.Floor(pos.X / QuantizeSize);
            int qy = (int)Math.Floor(pos.Y / QuantizeSize);
            int qz = (int)Math.Floor(pos.Z / QuantizeSize);

            // Pack into a single long using bit shifts. Each coord gets ~21 bits.
            // This handles coords up to ~1,000,000m which is more than sufficient.
            unchecked
            {
                long hash = 17;
                hash = hash * 31 + qx;
                hash = hash * 31 + qy;
                hash = hash * 31 + qz;
                return hash;
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
            public List<IMyEntity> Entities;
        }
    }
}
