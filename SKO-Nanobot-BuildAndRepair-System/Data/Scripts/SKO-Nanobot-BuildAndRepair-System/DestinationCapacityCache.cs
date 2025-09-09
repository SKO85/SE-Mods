using System;
using System.Collections.Generic;
using VRage.Game.ModAPI;

namespace SKONanobotBuildAndRepairSystem
{
    internal static class DestinationCapacityCache
    {
        private class Entry
        {
            public double FreeVolume;
            public TimeSpan LastCheck;
        }

        private static readonly Dictionary<IMyInventory, Entry> _cache = new Dictionary<IMyInventory, Entry>();
        private static TimeSpan _lastEvict;
        private static readonly TimeSpan Ttl = TimeSpan.FromSeconds(2);
        private static readonly TimeSpan EvictEvery = TimeSpan.FromSeconds(10);

        public static bool AnyDestinationHasFree(IList<IMyInventory> inventories, TimeSpan now)
        {
            if (inventories == null || inventories.Count == 0) return false;
            EvictOld(now);
            for (var i = 0; i < inventories.Count; i++)
            {
                var inv = inventories[i];
                if (inv == null) continue;

                Entry entry;
                if (_cache.TryGetValue(inv, out entry))
                {
                    if (now - entry.LastCheck <= Ttl)
                    {
                        if (entry.FreeVolume > 0) return true;
                        continue;
                    }
                }

                var free = inv.MaxVolume - inv.CurrentVolume;
                _cache[inv] = new Entry { FreeVolume = (double)free, LastCheck = now };
                if (free > 0) return true;
            }
            return false;
        }

        private static void EvictOld(TimeSpan now)
        {
            if (now - _lastEvict < EvictEvery) return;
            _lastEvict = now;

            var toRemove = new List<IMyInventory>();
            foreach (var kv in _cache)
            {
                if (now - kv.Value.LastCheck > TimeSpan.FromSeconds(30))
                {
                    toRemove.Add(kv.Key);
                }
            }
            for (var i = 0; i < toRemove.Count; i++)
            {
                _cache.Remove(toRemove[i]);
            }
            toRemove.Clear();
        }
    }
}
