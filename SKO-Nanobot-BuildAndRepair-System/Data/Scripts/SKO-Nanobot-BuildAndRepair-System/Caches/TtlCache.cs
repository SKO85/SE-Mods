namespace SKONanobotBuildAndRepairSystem.Caches
{
    using Sandbox.ModAPI;
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;

    public class TtlCache<TKey, TValue>
    {
        /// <summary>
        /// Stored entry. Struct to avoid the per-Set heap allocation that the
        /// previous class-typed CacheItem incurred — assignment caches see
        /// thousands of writes per tick under load. Fields are readonly so the
        /// struct is effectively immutable.
        /// </summary>
        public struct CacheItem
        {
            public readonly TValue Value;
            public readonly TimeSpan Expiration;

            public CacheItem(TValue value, TimeSpan expiration)
            {
                Value = value;
                Expiration = expiration;
            }

            public bool IsExpired(TimeSpan now)
            { return now >= Expiration; }
        }

        public ConcurrentDictionary<TKey, CacheItem> Entries;

        private readonly TimeSpan _defaultTtl;

        // Basic constructor
        public TtlCache(TimeSpan defaultTtl) : this(defaultTtl, null, 4, 0) { }

        // Tunable constructor with comparer/capacity for high-load scenarios
        public TtlCache(TimeSpan defaultTtl, IEqualityComparer<TKey> comparer, int concurrencyLevel, int capacity)
        {
            if (defaultTtl <= TimeSpan.Zero)
                throw new Exception("TTL must be positive.");

            _defaultTtl = defaultTtl;
            Entries = comparer != null
                ? new ConcurrentDictionary<TKey, CacheItem>(concurrencyLevel, capacity, comparer)
                : new ConcurrentDictionary<TKey, CacheItem>(concurrencyLevel, capacity);
        }

        public int Count
        { get { return Entries.Count; } }

        /// <summary>
        /// Read the tick-cached play time, falling back to the live session
        /// accessor only on the very first ticks before Mod.UpdateBeforeSimulation
        /// has had a chance to publish a value. Returns false (with a zero
        /// fallback) when no session exists at all.
        /// </summary>
        private static bool TryGetNow(out TimeSpan now)
        {
            now = Mod.NowPlayTime;
            if (now != TimeSpan.Zero) return true;

            var session = MyAPIGateway.Session;
            if (session == null) { now = TimeSpan.Zero; return false; }
            now = session.ElapsedPlayTime;
            return true;
        }

        /// <summary>Adds or updates a value with the default TTL.</summary>
        public bool Set(TKey key, TValue value)
        {
            return Set(key, value, _defaultTtl);
        }

        /// <summary>Adds or updates a value with a specific TTL. Returns false if no session is available.</summary>
        public bool Set(TKey key, TValue value, TimeSpan ttl)
        {
            if (ttl <= TimeSpan.Zero)
                throw new Exception("TTL must be positive.");

            TimeSpan now;
            if (!TryGetNow(out now)) return false;
            Entries[key] = new CacheItem(value, now.Add(ttl));
            return true;
        }

        /// <summary>
        /// Attempts to get the value if it exists and is not expired.
        /// </summary>
        public bool TryGet(TKey key, out TValue value)
        {
            CacheItem item;
            if (Entries.TryGetValue(key, out item))
            {
                TimeSpan now;
                if (!TryGetNow(out now)) { value = default(TValue); return false; }
                if (!item.IsExpired(now))
                {
                    value = item.Value;
                    return true;
                }
            }

            value = default(TValue);
            return false;
        }

        /// <summary>Removes a value explicitly.</summary>
        public bool Remove(TKey key)
        {
            CacheItem removed;
            return Entries.TryRemove(key, out removed);
        }

        /// <summary>Clears the cache.</summary>
        public void Clear()
        {
            Entries.Clear();
        }

        /// <summary>Optional: remove all expired entries (call periodically if you skip purge-on-read).</summary>
        public void CleanupExpired()
        {
            TimeSpan now;
            if (!TryGetNow(out now)) return;
            var expiredKeys = new List<TKey>();
            foreach (var pair in Entries)
            {
                if (pair.Value.IsExpired(now))
                {
                    expiredKeys.Add(pair.Key);
                }
            }
            CacheItem removed;
            foreach (var key in expiredKeys)
            {
                Entries.TryRemove(key, out removed);
            }
        }
    }
}
