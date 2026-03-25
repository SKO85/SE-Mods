namespace SKONanobotBuildAndRepairSystem.Caches
{
    using Sandbox.ModAPI;
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;

    public class TtlCache<TKey, TValue>
    {
        public class CacheItem
        {
            public TValue Value { get; private set; }
            public TimeSpan Expiration { get; private set; }

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

        /// <summary>Adds or updates a value with the default TTL.</summary>
        public void Set(TKey key, TValue value)
        {
            Set(key, value, _defaultTtl);
        }

        /// <summary>Adds or updates a value with a specific TTL.</summary>
        public void Set(TKey key, TValue value, TimeSpan ttl)
        {
            if (ttl <= TimeSpan.Zero)
                throw new Exception("TTL must be positive.");

            var session = MyAPIGateway.Session;
            if (session == null) return;
            var now = session.ElapsedPlayTime;
            var expiration = now.Add(ttl);
            var item = new CacheItem(value, expiration);
            Entries[key] = item;
        }

        /// <summary>
        /// Attempts to get the value if it exists and is not expired.
        /// Expired items are removed.
        /// </summary>
        public bool TryGet(TKey key, out TValue value)
        {
            CacheItem item;
            if (Entries.TryGetValue(key, out item))
            {
                var session = MyAPIGateway.Session;
                if (session == null) { value = default(TValue); return false; }
                var now = session.ElapsedPlayTime;
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
            var session = MyAPIGateway.Session;
            if (session == null) return;
            var now = session.ElapsedPlayTime;
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