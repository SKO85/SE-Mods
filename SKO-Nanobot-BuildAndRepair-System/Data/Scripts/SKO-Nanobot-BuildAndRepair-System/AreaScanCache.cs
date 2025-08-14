namespace SKONanobotBuildAndRepairSystem
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using Sandbox.ModAPI;
    using VRage.Game.ModAPI;
    using VRage.ModAPI;
    using VRageMath;

    /// <summary>
    /// Shared cache for area/entity scans and grid block listings to avoid duplicate scanning across multiple BAR blocks.
    /// Note: Uses short TTLs and is fully server-side. Callers must treat returned lists as read-only.
    /// </summary>
    public static class AreaScanCache
    {
        private struct AabbKey : IEquatable<AabbKey>
        {
            public readonly long X0;
            public readonly long Y0;
            public readonly long Z0;
            public readonly long X1;
            public readonly long Y1;
            public readonly long Z1;
            public readonly int Cell;

            public AabbKey(ref BoundingBoxD aabb, int cell)
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

            public bool Equals(AabbKey other)
            {
                return X0 == other.X0 && Y0 == other.Y0 && Z0 == other.Z0 && X1 == other.X1 && Y1 == other.Y1 && Z1 == other.Z1 && Cell == other.Cell;
            }

            public override bool Equals(object obj)
            {
                return obj is AabbKey && Equals((AabbKey)obj);
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

        private class EntitiesEntry
        {
            public List<IMyEntity> Entities;
            public TimeSpan CachedAt;
            public TimeSpan LastAccessAt;
        }

        private class GridBlocksEntry
        {
            public List<IMySlimBlock> Blocks;
            public TimeSpan CachedAt;
            public TimeSpan LastAccessAt;
        }

        private static readonly ConcurrentDictionary<AabbKey, EntitiesEntry> _entitiesInBox = new ConcurrentDictionary<AabbKey, EntitiesEntry>();
        private static readonly ConcurrentDictionary<long, GridBlocksEntry> _gridBlocks = new ConcurrentDictionary<long, GridBlocksEntry>();

        // Defaults tuned low to ensure freshness while still deduplicating bursts across many BARs.
        public static readonly TimeSpan DefaultEntitiesTtl = TimeSpan.FromMilliseconds(400);
        public static readonly TimeSpan DefaultGridBlocksTtl = TimeSpan.FromMilliseconds(800);

        private const int AabbCellSizeMeters = 25; // spatial quantization cell size

        public static List<IMyEntity> GetEntitiesInAabbCached(ref BoundingBoxD aabb, TimeSpan now)
        {
            var key = new AabbKey(ref aabb, AabbCellSizeMeters);
            EntitiesEntry entry;
            if (_entitiesInBox.TryGetValue(key, out entry))
            {
                if (now - entry.CachedAt <= DefaultEntitiesTtl)
                {
                    entry.LastAccessAt = now;
                    return entry.Entities;
                }
            }

            List<IMyEntity> entities;
            lock (MyAPIGateway.Entities)
            {
                entities = MyAPIGateway.Entities.GetElementsInBox(ref aabb);
            }
            entry = new EntitiesEntry { Entities = entities, CachedAt = now, LastAccessAt = now };
            _entitiesInBox[key] = entry;
            return entities;
        }

        public static IReadOnlyList<IMySlimBlock> GetGridBlocksCached(IMyCubeGrid grid, TimeSpan now)
        {
            if (grid == null) return EmptySlimBlocks;

            GridBlocksEntry entry;
            if (_gridBlocks.TryGetValue(grid.EntityId, out entry))
            {
                if (now - entry.CachedAt <= DefaultGridBlocksTtl)
                {
                    entry.LastAccessAt = now;
                    return entry.Blocks;
                }
            }

            var blocks = new List<IMySlimBlock>();
            grid.GetBlocks(blocks);
            entry = new GridBlocksEntry { Blocks = blocks, CachedAt = now, LastAccessAt = now };
            _gridBlocks[grid.EntityId] = entry;
            return blocks;
        }

        public static void EvictOld(TimeSpan now)
        {
            // Simple time-based eviction to keep memory bounded; called opportunistically by callers.
            foreach (var kvp in _entitiesInBox)
            {
                var age = now - kvp.Value.LastAccessAt;
                if (age.TotalSeconds > 5)
                {
                    EntitiesEntry removed;
                    _entitiesInBox.TryRemove(kvp.Key, out removed);
                }
            }

            foreach (var kvp in _gridBlocks)
            {
                var age = now - kvp.Value.LastAccessAt;
                if (age.TotalSeconds > 5)
                {
                    GridBlocksEntry removed;
                    _gridBlocks.TryRemove(kvp.Key, out removed);
                }
            }
        }

        private static readonly IReadOnlyList<IMySlimBlock> EmptySlimBlocks = new List<IMySlimBlock>(0);
    }
}
