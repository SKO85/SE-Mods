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

        private static TtlCache<BlockKey, long> Cache = new TtlCache<BlockKey, long>(TimeSpan.FromSeconds(8));

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
            if (Cache.TryGet(key, out assignedSystemId))
            {
                // Already assigned to this system.
                if (assignedSystemId == systemId)
                {
                    return true;
                }

                // Already assigned to another system.
                return false;
            }

            // Assign to this system.
            Cache.Set(key, systemId, TimeSpan.FromSeconds(Mod.Settings.AssignmentTtlSeconds));
            return true;
        }

        public static void ReleaseFromSystem(this IMySlimBlock block)
        {
            Cache.Remove(GetBlockKey(block));
        }

        public static void Cleanup()
        {
            var profilerTs = MethodProfiler.Start();
            Cache.CleanupExpired();
            MethodProfiler.StopAndLog("BlockSystemAssigningHandler.Cleanup", profilerTs);
        }

        public static void Clear()
        {
            Cache.Clear();
        }
    }
}