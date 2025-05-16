using System;

namespace SKONanobotBuildAndRepairSystem.Cache
{
    internal class CacheItem<T>
    {
        public CacheItem(string key, T value, TimeSpan cachedTime)
        {
            Key = key;
            Value = value;
            CachedTime = cachedTime;
        }

        public string Key { get; set; }
        public T Value { get; set; }
        public TimeSpan CachedTime { get; set; }
    }
}
