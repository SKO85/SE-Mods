namespace SKONanobotBuildAndRepairSystem
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using VRage.ModAPI;
    using VRageMath;

    internal static class ScanCoordinator
    {
        private struct AreaKey : IEquatable<AreaKey>
        {
            public readonly long X0;
            public readonly long Y0;
            public readonly long Z0;
            public readonly long X1;
            public readonly long Y1;
            public readonly long Z1;
            public readonly int Cell;

            public AreaKey(ref BoundingBoxD aabb, int cell)
            {
                Cell = cell;
                var min = aabb.Min; var max = aabb.Max;
                X0 = (long)Math.Floor(min.X / cell);
                Y0 = (long)Math.Floor(min.Y / cell);
                Z0 = (long)Math.Floor(min.Z / cell);
                X1 = (long)Math.Ceiling(max.X / cell);
                Y1 = (long)Math.Ceiling(max.Y / cell);
                Z1 = (long)Math.Ceiling(max.Z / cell);
            }

            public bool Equals(AreaKey other)
            {
                return X0 == other.X0 && Y0 == other.Y0 && Z0 == other.Z0 && X1 == other.X1 && Y1 == other.Y1 && Z1 == other.Z1 && Cell == other.Cell;
            }

            public override bool Equals(object obj)
            {
                return obj is AreaKey && Equals((AreaKey)obj);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    var h = 17;
                    h = h * 31 + X0.GetHashCode();
                    h = h * 31 + Y0.GetHashCode();
                    h = h * 31 + Z0.GetHashCode();
                    h = h * 31 + X1.GetHashCode();
                    h = h * 31 + Y1.GetHashCode();
                    h = h * 31 + Z1.GetHashCode();
                    h = h * 31 + Cell.GetHashCode();
                    return h;
                }
            }
        }

        private class AreaSnapshot
        {
            public List<IMyEntity> Entities;
            public TimeSpan CachedAt;
            public TimeSpan LastAccessAt;
        }

        private static readonly ConcurrentDictionary<AreaKey, AreaSnapshot> _areas = new ConcurrentDictionary<AreaKey, AreaSnapshot>();
        private const int CellSize = 25;
        private static readonly TimeSpan AreaTtl = TimeSpan.FromMilliseconds(350);

        public static List<IMyEntity> GetEntities(ref BoundingBoxD aabb, TimeSpan now)
        {
            var key = new AreaKey(ref aabb, CellSize);
            AreaSnapshot snap;
            if (_areas.TryGetValue(key, out snap))
            {
                if (now - snap.CachedAt <= AreaTtl)
                {
                    snap.LastAccessAt = now;
                    return snap.Entities;
                }
            }

            var entities = AreaScanCache.GetEntitiesInAabbCached(ref aabb, now);
            snap = new AreaSnapshot { Entities = entities, CachedAt = now, LastAccessAt = now };
            _areas[key] = snap;
            return entities;
        }

        public static void EvictOld(TimeSpan now)
        {
            foreach (var kvp in _areas)
            {
                if (now - kvp.Value.LastAccessAt > TimeSpan.FromSeconds(5))
                {
                    AreaSnapshot removed;
                    _areas.TryRemove(kvp.Key, out removed);
                }
            }
        }
    }
}
