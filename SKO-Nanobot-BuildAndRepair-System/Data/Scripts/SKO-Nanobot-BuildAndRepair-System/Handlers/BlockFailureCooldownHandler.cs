using SKONanobotBuildAndRepairSystem.Caches;
using SKONanobotBuildAndRepairSystem.Profiling;
using System;
using VRage.Game.ModAPI;

namespace SKONanobotBuildAndRepairSystem.Handlers
{
    /// <summary>
    /// Short-lived per-block fail cooldown shared across all BaRs. Sibling to
    /// BlockSystemAssigningHandler — that handler controls "this block is mine
    /// right now"; this handler controls "this block was just tried and could
    /// not be welded — every BaR should skip it for a few seconds".
    ///
    /// Why both: the welding loop releases the assignment when no components
    /// are available (see Welding.cs failure branch), so the very next BaR
    /// (or the same BaR's next tick) re-iterates and re-claims the same block,
    /// does the same inventory pull, and fails the same way. Without a cooldown,
    /// N BaRs all bounce off the same un-weldable target inside a single tick.
    /// A short cooldown breaks that cascade without holding a long-lived
    /// assignment that would block other targets.
    ///
    /// Reuses BlockSystemAssigningHandler.BlockKey (gridId + Vector3I) so the
    /// physical-block identity matches across both caches and call sites that
    /// already build a key for the assigning cache can reuse the value.
    /// </summary>
    public static class BlockFailureCooldownHandler
    {
        // Default 4s — long enough to cover one scan cycle (~2s) plus the
        // cluster apply-result swap, short enough that components arriving
        // mid-cooldown only delay welding by a few seconds.
        public const int CooldownSecondsDefault = 4;
        public const int CooldownSecondsMin = 0;     // 0 disables the feature
        public const int CooldownSecondsMax = 30;

        // byte payload: ConcurrentDictionary requires a value type and one byte
        // is the smallest practical choice. The actual stored value is irrelevant —
        // only key presence matters for the IsOnCooldown check.
        private static readonly TtlCache<BlockSystemAssigningHandler.BlockKey, byte> Cache =
            new TtlCache<BlockSystemAssigningHandler.BlockKey, byte>(
                TimeSpan.FromSeconds(CooldownSecondsDefault), null, 4, 256);

        public static int CooldownCount { get { return Cache.Count; } }

        public static bool IsEnabled
        {
            get { return Mod.Settings != null && Mod.Settings.BlockFailureCooldownSeconds > 0; }
        }

        public static bool IsOnCooldown(IMySlimBlock block)
        {
            if (!IsEnabled) return false;
            if (block == null || block.CubeGrid == null) return false;
            byte ignored;
            var key = new BlockSystemAssigningHandler.BlockKey(block.CubeGrid.EntityId, block.Position);
            return Cache.TryGet(key, out ignored);
        }

        public static void MarkFailed(IMySlimBlock block)
        {
            if (!IsEnabled) return;
            if (block == null || block.CubeGrid == null) return;
            var key = new BlockSystemAssigningHandler.BlockKey(block.CubeGrid.EntityId, block.Position);
            Cache.Set(key, (byte)1, TimeSpan.FromSeconds(Mod.Settings.BlockFailureCooldownSeconds));
        }

        public static void Cleanup()
        {
            var profilerTs = MethodProfiler.Start();
            Cache.CleanupExpired();
            if (profilerTs != 0L)
            {
                MethodProfiler.StopAndLog("BlockFailureCooldownHandler.Cleanup", profilerTs);
            }
        }

        public static void Clear()
        {
            Cache.Clear();
        }
    }
}
