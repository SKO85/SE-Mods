using Sandbox.ModAPI;
using System;
using System.Collections.Concurrent;
using System.Linq;

namespace SKONanobotBuildAndRepairSystem.Cache
{
    internal class CacheHandler<T>
    {
        public ConcurrentDictionary<string, CacheItem<T>> CachedItems = new ConcurrentDictionary<string, CacheItem<T>>();
        public int CacheSeconds = 5;
        private TimeSpan LastCleanupTime;

        public CacheHandler(int cachedSeconds = 5)
        {
            CacheSeconds = cachedSeconds;
        }

        public bool IsCached(string key)
        {
            RemoveExpired();

            if (CachedItems.ContainsKey(key))
            {
                CacheItem<T> item = null;
                if (CachedItems.TryGetValue(key, out item))
                {
                    return MyAPIGateway.Session.ElapsedPlayTime.Subtract(item.CachedTime).TotalSeconds <= CacheSeconds;
                }
            }

            return false;
        }

        public T Get(string key)
        {
            RemoveExpired();

            if (CachedItems.ContainsKey(key))
            {
                CacheItem<T> item = null;
                if (CachedItems.TryGetValue(key, out item))
                {
                    if (item != null)
                    {
                        if (MyAPIGateway.Session.ElapsedPlayTime.Subtract(item.CachedTime).TotalSeconds <= CacheSeconds)
                        {
                            return item.Value;
                        }
                        else
                        {
                            // Cache expired. Remoe from cache.
                            CachedItems.TryRemove(key, out item);
                        }
                    }
                }
            }

            return default(T);
        }

        public T AddOrGetCache(string key, Func<T> producer)
        {
            RemoveExpired();

            if (CachedItems.ContainsKey(key))
            {
                CacheItem<T> item = null;
                if(CachedItems.TryGetValue(key, out item))
                {
                    if (item != null)
                    {
                        if(MyAPIGateway.Session.ElapsedPlayTime.Subtract(item.CachedTime).TotalSeconds <= CacheSeconds)
                        {
                            return item.Value;
                        }
                        else
                        {
                            // Cache expired. Remoe from cache.
                            CachedItems.TryRemove(key, out item);
                        }
                    }
                }
            }

            // Call producer and cache value.
            var itemValue = producer();
            var cacheItem = new CacheItem<T>(key, itemValue, MyAPIGateway.Session.ElapsedPlayTime);
            CachedItems.TryAdd(key, cacheItem);
            return itemValue;            
        }

        public void RemoveExpired()
        {
            try
            {
                var elapsedTime = MyAPIGateway.Session.ElapsedPlayTime;
                if (elapsedTime.Subtract(LastCleanupTime).TotalSeconds >= CacheSeconds * 6)
                {
                    lock (CachedItems)
                    {
                        var expired = CachedItems.Where(c => elapsedTime.Subtract(c.Value.CachedTime).TotalSeconds >= CacheSeconds * 4).ToList();
                        if (expired.Any())
                        {
                            foreach (var item in expired)
                            {
                                CacheItem<T> cachedItem = null;
                                CachedItems.TryRemove(item.Key, out cachedItem);
                            }
                        }
                    }

                    LastCleanupTime = MyAPIGateway.Session.ElapsedPlayTime;
                }
            }
            catch (Exception)
            {
            }
        }
    }
}
