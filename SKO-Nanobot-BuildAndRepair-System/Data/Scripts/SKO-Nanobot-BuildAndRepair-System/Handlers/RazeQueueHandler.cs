using SKONanobotBuildAndRepairSystem.Utils;
using System;
using System.Collections.Generic;
using VRage.Game.ModAPI;
using VRageMath;

namespace SKONanobotBuildAndRepairSystem.Handlers
{
    /// <summary>
    /// BUG-127: Deferred + batched block-removal queue.
    ///
    /// Decouples "block hit 0 integrity" (cheap, ~µs to enqueue) from "engine fully
    /// removes block" (5-8 ms on conveyor-connected blocks: physics + integrity +
    /// conveyor topology recalc). Two compounding mitigations:
    ///   1. Process() drains only when (tickCounter % ProcessIntervalTicks) == 0,
    ///      i.e. every ~10 server ticks rather than every tick.
    ///   2. Drained entries are grouped by CubeGrid and a single
    ///      IMyCubeGrid.RazeBlocks(positions) call is issued per grid → SE engine
    ///      collapses N recalcs into 1.
    ///
    /// Block reference safety: Enqueue (called from grinding) and Process (called
    /// from Mod.UpdateBeforeSimulation) both run on the main thread, same thread as
    /// grid mutations. No locks are needed on the queue.
    /// </summary>
    public static class RazeQueueHandler
    {
        public const int MaxRazesPerDrainDefault = 5;
        public const int ProcessIntervalTicks = 10;

        private static int _tickCounter;
        private static readonly Queue<IMySlimBlock> _queue = new Queue<IMySlimBlock>();

        // Scratch buffer reused across drains. Cleared at the end of Process() so
        // it doesn't keep grid references alive between drains.
        private static readonly Dictionary<IMyCubeGrid, List<Vector3I>> _batchByGrid =
            new Dictionary<IMyCubeGrid, List<Vector3I>>();

        public static void Enqueue(IMySlimBlock block)
        {
            if (block == null) return;
            _queue.Enqueue(block);
        }

        public static int GetQueueDepth()
        {
            return _queue.Count;
        }

        /// <summary>
        /// Tick-gated batched drain. Call once per server tick from the main thread;
        /// internally throttles to one drain per ProcessIntervalTicks ticks.
        /// </summary>
        public static void Process()
        {
            if ((++_tickCounter % ProcessIntervalTicks) != 0) return;

            var processed = 0;
            while (processed < MaxRazesPerDrainDefault)
            {
                if (_queue.Count == 0) break;
                var target = _queue.Dequeue();
                if (target == null) continue;
                var grid = target.CubeGrid;
                if (grid == null) continue;
                // Block could have been welded back up or razed by another path before
                // we drained it; in either case skip and keep budget for the next entry.
                if (target.FatBlock != null && target.FatBlock.Closed) continue;
                // Slim armor blocks have FatBlock == null, so the Closed check above
                // never fires for them. IsDestroyed flips back to false the moment any
                // BaR (or player hand-welder) welds the dismounted block above zero
                // integrity, so razing here would silently undo legitimate weld work.
                if (!target.IsDestroyed) continue;

                List<Vector3I> positions;
                if (!_batchByGrid.TryGetValue(grid, out positions))
                {
                    positions = new List<Vector3I>(MaxRazesPerDrainDefault);
                    _batchByGrid[grid] = positions;
                }
                positions.Add(target.Position);
                processed++;
            }

            if (_batchByGrid.Count == 0) return;

            foreach (var kvp in _batchByGrid)
            {
                try
                {
                    kvp.Key.RazeBlocks(kvp.Value);
                }
                catch (Exception ex)
                {
                    Logging.Instance.Error(ex);
                }
            }
            _batchByGrid.Clear();
        }
    }
}
