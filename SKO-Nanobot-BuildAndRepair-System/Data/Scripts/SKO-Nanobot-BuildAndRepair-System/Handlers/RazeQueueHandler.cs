using SKONanobotBuildAndRepairSystem.Profiling;
using SKONanobotBuildAndRepairSystem.Utils;
using System;
using System.Collections.Generic;
using System.Diagnostics;
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
        /// BUG-145: per-call profiling. The actual block-removal cost (engine RazeBlocks
        /// → physics + integrity + conveyor topology recalc per grid) lives here, not in
        /// ServerDoGrind anymore (BUG-127 deferred it). Without this entry the cost was
        /// invisible. Logs drained/skipped counts, grids touched, sub-times for the
        /// drain loop vs the engine RazeBlocks calls, and per-grid raze ms breakdown.
        /// </summary>
        public static void Process()
        {
            if ((++_tickCounter % ProcessIntervalTicks) != 0) return;

            // Cheap exit if queue empty AT ENTRY — avoids profiler noise from no-op ticks.
            if (_queue.Count == 0) return;

            var profilerTs = MethodProfiler.Start();
            var queueDepthAtEntry = _queue.Count;
            var drained = 0;
            var skippedNullTarget = 0;
            var skippedNullGrid = 0;
            var skippedClosedFat = 0;
            var skippedNotDestroyed = 0;
            var tsDrainStart = profilerTs != 0L ? Stopwatch.GetTimestamp() : 0L;

            try
            {
                var processed = 0;
                while (processed < MaxRazesPerDrainDefault)
                {
                    if (_queue.Count == 0) break;
                    var target = _queue.Dequeue();
                    if (target == null) { skippedNullTarget++; continue; }
                    var grid = target.CubeGrid;
                    if (grid == null) { skippedNullGrid++; continue; }
                    // Block could have been welded back up or razed by another path before
                    // we drained it; in either case skip and keep budget for the next entry.
                    if (target.FatBlock != null && target.FatBlock.Closed) { skippedClosedFat++; continue; }
                    // Slim armor blocks have FatBlock == null, so the Closed check above
                    // never fires for them. IsDestroyed flips back to false the moment any
                    // BaR (or player hand-welder) welds the dismounted block above zero
                    // integrity, so razing here would silently undo legitimate weld work.
                    if (!target.IsDestroyed) { skippedNotDestroyed++; continue; }

                    List<Vector3I> positions;
                    if (!_batchByGrid.TryGetValue(grid, out positions))
                    {
                        positions = new List<Vector3I>(MaxRazesPerDrainDefault);
                        _batchByGrid[grid] = positions;
                    }
                    positions.Add(target.Position);
                    processed++;
                    drained++;
                }

                var tsBeforeRaze = profilerTs != 0L ? Stopwatch.GetTimestamp() : 0L;
                var gridsTouched = _batchByGrid.Count;
                var totalRazed = 0;
                var maxRazePerGridTicks = 0L;

                if (gridsTouched > 0)
                {
                    foreach (var kvp in _batchByGrid)
                    {
                        try
                        {
                            var perGridTs = profilerTs != 0L ? Stopwatch.GetTimestamp() : 0L;
                            kvp.Key.RazeBlocks(kvp.Value);
                            if (perGridTs != 0L)
                            {
                                var perGridTicks = Stopwatch.GetTimestamp() - perGridTs;
                                if (perGridTicks > maxRazePerGridTicks) maxRazePerGridTicks = perGridTicks;
                            }
                            totalRazed += kvp.Value.Count;
                        }
                        catch (Exception ex)
                        {
                            Logging.Instance.Error(ex);
                        }
                    }
                    _batchByGrid.Clear();
                }

                if (profilerTs != 0L)
                {
                    var tsFreq = Stopwatch.Frequency;
                    var _drainMs = (tsBeforeRaze - tsDrainStart) * 1000.0 / tsFreq;
                    var _razeMs = (Stopwatch.GetTimestamp() - tsBeforeRaze) * 1000.0 / tsFreq;
                    var _maxPerGridMs = maxRazePerGridTicks * 1000.0 / tsFreq;
                    var _queueDepthAtEntry = queueDepthAtEntry;
                    var _drained = drained;
                    var _skippedNullTarget = skippedNullTarget;
                    var _skippedNullGrid = skippedNullGrid;
                    var _skippedClosedFat = skippedClosedFat;
                    var _skippedNotDestroyed = skippedNotDestroyed;
                    var _gridsTouched = gridsTouched;
                    var _totalRazed = totalRazed;
                    var _queueDepthAfter = _queue.Count;
                    MethodProfiler.StopAndLog("RazeQueueHandler.Process", profilerTs, () =>
                        string.Format("queueAtEntry={0};drained={1};totalRazed={2};gridsTouched={3};queueAfter={4};skipNullTarget={5};skipNullGrid={6};skipClosedFat={7};skipNotDestroyed={8};drainMs={9:F3};razeMs={10:F3};maxPerGridMs={11:F3}",
                            _queueDepthAtEntry, _drained, _totalRazed, _gridsTouched, _queueDepthAfter,
                            _skippedNullTarget, _skippedNullGrid, _skippedClosedFat, _skippedNotDestroyed,
                            _drainMs, _razeMs, _maxPerGridMs));
                }
            }
            catch
            {
                if (profilerTs != 0L)
                {
                    MethodProfiler.StopAndLog("RazeQueueHandler.Process", profilerTs, () =>
                        string.Format("queueAtEntry={0};exception=true", queueDepthAtEntry));
                }
            }
        }
    }
}
