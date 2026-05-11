using SKONanobotBuildAndRepairSystem.Caches;
using SKONanobotBuildAndRepairSystem.Profiling;
using System;
using VRage.Game.ModAPI;
using VRageMath;

namespace SKONanobotBuildAndRepairSystem.Handlers
{
    public static class BlockSystemAssigningHandler
    {
        /// <summary>
        /// Struct key identifying a physical block across scan cycles. Background scans
        /// produce fresh IMySlimBlock references for the same block every cycle, so we
        /// can't key on reference identity. Prior implementation used
        /// string.Format("{0}:{1}", gridId, position) which allocated a new string on
        /// EVERY IsAssignedToOtherSystem / AssignToSystem / ReleaseFromSystem call in the
        /// main-thread weld/grind loops — ~768 string allocations per weld call per BaR
        /// in the hot path. Replaced with a value-type key that hashes and compares
        /// without any heap allocation. IEquatable&lt;BlockKey&gt; is implemented so
        /// ConcurrentDictionary's EqualityComparer&lt;T&gt;.Default avoids boxing.
        /// </summary>
        public struct BlockKey : IEquatable<BlockKey>
        {
            public readonly long GridEntityId;
            public readonly Vector3I Position;

            public BlockKey(long gridEntityId, Vector3I position)
            {
                GridEntityId = gridEntityId;
                Position = position;
            }

            public bool Equals(BlockKey other)
            {
                return GridEntityId == other.GridEntityId
                    && Position.X == other.Position.X
                    && Position.Y == other.Position.Y
                    && Position.Z == other.Position.Z;
            }

            public override bool Equals(object obj)
            {
                if (!(obj is BlockKey)) return false;
                return Equals((BlockKey)obj);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    var hash = 17;
                    hash = hash * 31 + GridEntityId.GetHashCode();
                    hash = hash * 31 + Position.X;
                    hash = hash * 31 + Position.Y;
                    hash = hash * 31 + Position.Z;
                    return hash;
                }
            }
        }

        // Pre-sized for high-density BaR clusters: this cache can hold thousands of
        // entries (one per actively-claimed block), so starting at the default
        // ~31 buckets would force many rehashes during a server's first minutes
        // under load.
        private static readonly TtlCache<BlockKey, long> Cache = new TtlCache<BlockKey, long>(
            TimeSpan.FromSeconds(8), null, 4, 256);

        public static int AssignmentCount { get { return Cache.Count; } }

        private static BlockKey GetBlockKey(IMySlimBlock block)
        {
            return new BlockKey(block.CubeGrid.EntityId, block.Position);
        }

        public static bool IsAssignedToOtherSystem(this IMySlimBlock block, long systemId)
        {
            var key = GetBlockKey(block);
            long assignedSystemId;
            if (Cache.TryGet(key, out assignedSystemId))
            {
                return assignedSystemId != systemId;
            }
            return false;
        }

        public static bool AssignToSystem(this IMySlimBlock block, long systemId)
        {
            var key = GetBlockKey(block);
            long assignedSystemId;
            // Held by another system — can't claim.
            if (Cache.TryGet(key, out assignedSystemId) && assignedSystemId != systemId)
            {
                return false;
            }

            // Free, or already ours: (re)assign and refresh TTL. Refresh on re-claim
            // is required — the welding loop calls this every tick on the lock-on
            // block to keep the assignment alive while welding takes >TTL seconds.
            // Set returns false if there's no session yet; propagate that so callers
            // don't think they own a block whose claim was never written.
            return Cache.Set(key, systemId, TimeSpan.FromSeconds(Mod.Settings.AssignmentTtlSeconds));
        }

        public static void ReleaseFromSystem(this IMySlimBlock block)
        {
            Cache.Remove(GetBlockKey(block));
        }

        /// <summary>
        /// Drops every assignment currently held by the given system. Used on
        /// sort-relevant settings changes so the BaR can re-pick from the freshly
        /// sorted target list without waiting for phantom claims (from the
        /// multi-action wrapper that may have assigned several blocks in one
        /// cycle) to TTL-expire. Returns the number of entries removed.
        /// </summary>
        public static int ReleaseAllForSystem(long systemId)
        {
            var removed = 0;
            TtlCache<BlockKey, long>.CacheItem dummy;
            // ConcurrentDictionary enumerators tolerate concurrent modification,
            // so a single-pass remove is safe and avoids the extra key-buffer.
            foreach (var pair in Cache.Entries)
            {
                if (pair.Value.Value == systemId)
                {
                    if (Cache.Entries.TryRemove(pair.Key, out dummy)) removed++;
                }
            }
            return removed;
        }

        public static void Cleanup()
        {
            var profilerTs = MethodProfiler.Start();
            Cache.CleanupExpired();
            if (profilerTs != 0L)
            {
                MethodProfiler.StopAndLog("BlockSystemAssigningHandler.Cleanup", profilerTs);
            }
        }

        public static void Clear()
        {
            Cache.Clear();
        }
    }
}