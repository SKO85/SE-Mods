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
        // Default 15s. This branch only fires on genuine starvation — a block
        // whose missing components are unavailable from every source (partial
        // pulls start a transport and never reach the failure path). The loop
        // welds one block per tick and re-iterates the priority list from the
        // top each tick, so the cooldown must outlast a full traversal; with a
        // short 4s park a large set of same-component-starved high-priority
        // blocks (e.g. armour all needing one exhausted component) expires
        // faster than the BaR can pass them, re-failing the top every tick and
        // never reaching lower-priority blocks that do have components. 15s
        // covers a traversal plus the inter-scan window while staying well
        // short of the player noticing a stall once components arrive.
        public const int CooldownSecondsDefault = 15;
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
