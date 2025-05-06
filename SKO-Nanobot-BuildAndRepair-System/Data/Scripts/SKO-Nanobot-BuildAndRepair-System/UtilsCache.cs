using Sandbox.ModAPI;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace SKONanobotBuildAndRepairSystem
{
   internal static class UtilsCache
    {
        private static readonly ConcurrentDictionary<string, ICachedList> CachedItems = new ConcurrentDictionary<string, ICachedList>();

        public static List<T> GetOrAdd<T>(string cacheId, int cacheSeconds, Func<List<T>> producer) where T : class
        {
                ICachedList raw;
                if (CachedItems.TryGetValue(cacheId, out raw))
                {
                    var elapsed = MyAPIGateway.Session.ElapsedPlayTime - raw.CacheTime;
                    var typed = raw as CachedList<T>;
                    if (typed != null && elapsed.TotalSeconds <= cacheSeconds)
                    {
                        return typed.Items;
                    }

                    ICachedList removed;
                    CachedItems.TryRemove(cacheId, out removed);
                }

                var items = producer();
                var newCache = new CachedList<T>(cacheId, items);
                CachedItems[cacheId] = newCache;
                return items;
        }

        public static void Clear(string cacheId = null)
        {
            if (cacheId == null)
            {
                CachedItems.Clear();
            }
            else
            {
                ICachedList removed;
                CachedItems.TryRemove(cacheId, out removed);
            }
        }
    }

    internal interface ICachedList
    {
        string CacheId { get; }
        TimeSpan CacheTime { get; }
        Type ItemType { get; }
    }

    internal class CachedList<T> : ICachedList where T : class
    {
        private readonly string _cacheId;
        private readonly TimeSpan _cacheTime;
        private readonly List<T> _items;

        public CachedList(string cacheId, List<T> items)
        {
            _cacheId = cacheId;
            _cacheTime = MyAPIGateway.Session.ElapsedPlayTime;
            _items = items;
        }

        public string CacheId { get { return _cacheId; } }
        public TimeSpan CacheTime { get { return _cacheTime; } }
        public List<T> Items { get { return _items; } }
        public Type ItemType { get { return typeof(T); } }
    }
}
