using Sandbox.ModAPI;
using SKONanobotBuildAndRepairSystem.Extensions;
using SKONanobotBuildAndRepairSystem.Profiling;
using System;
using System.Collections.Generic;
using VRage.Game.ModAPI;

namespace SKONanobotBuildAndRepairSystem.Handlers
{
    /// <summary>
    /// BUG-260827.1: main-thread skeleton-reset queue for deformation-only blocks
    /// (integrity fine, block dented). These must NOT become weld targets —
    /// MaxDeformation is bugged in-game and never resets, so flagging them flooded
    /// the target list with unweldable phantoms. The background scan enqueues here
    /// instead; the drain resets a few per tick on the main thread (skeleton engine
    /// calls are main-thread only, BUG-260610.5). A per-block cooldown keeps
    /// permanently-bugged blocks from being reset every scan cycle.
    /// </summary>
    public static class SkeletonResetHandler
    {
        private const int MaxResetsPerTick = 2;
        private const int CooldownCleanupThreshold = 512;
        private static readonly TimeSpan PerBlockCooldown = TimeSpan.FromSeconds(60);

        // Enqueue runs on background scan threads; Process on the main thread.
        private static readonly object _lock = new object();
        private static readonly Queue<QueueEntry> _queue = new Queue<QueueEntry>();
        private static readonly HashSet<BlockSystemAssigningHandler.BlockKey> _pendingKeys =
            new HashSet<BlockSystemAssigningHandler.BlockKey>();
        private static readonly Dictionary<BlockSystemAssigningHandler.BlockKey, TimeSpan> _lastReset =
            new Dictionary<BlockSystemAssigningHandler.BlockKey, TimeSpan>();
        private static readonly List<BlockSystemAssigningHandler.BlockKey> _reapBuffer =
            new List<BlockSystemAssigningHandler.BlockKey>();

        private struct QueueEntry
        {
            public BlockSystemAssigningHandler.BlockKey Key;
            public IMySlimBlock Block;
        }

        public static void Enqueue(IMySlimBlock block)
        {
            if (block == null) return;
            var grid = block.CubeGrid;
            if (grid == null) return;
            var session = MyAPIGateway.Session;
            if (session == null) return;
            var key = new BlockSystemAssigningHandler.BlockKey(grid.EntityId, block.Position);
            var now = session.ElapsedPlayTime;
            lock (_lock)
            {
                TimeSpan last;
                if (_lastReset.TryGetValue(key, out last) && now - last < PerBlockCooldown) return;
                if (!_pendingKeys.Add(key)) return;
                _queue.Enqueue(new QueueEntry { Key = key, Block = block });
            }
        }

        /// <summary>Main-thread drain (call once per tick; cheap when queue empty).</summary>
        public static void Process()
        {
            // Unsynchronized fast-path read is fine: main thread is the only consumer.
            if (_queue.Count == 0) return;

            var profilerTs = MethodProfiler.Start();
            var resets = 0;
            var dequeued = 0;
            var now = MyAPIGateway.Session.ElapsedPlayTime;
            try
            {
                while (resets < MaxResetsPerTick)
                {
                    QueueEntry entry;
                    lock (_lock)
                    {
                        if (_queue.Count == 0) break;
                        entry = _queue.Dequeue();
                        _pendingKeys.Remove(entry.Key);
                    }
                    dequeued++;

                    var block = entry.Block;
                    if (block == null || block.IsDestroyed) continue;
                    var grid = block.CubeGrid;
                    if (grid == null || grid.Closed) continue;

                    block.ResetSkeleton();
                    lock (_lock) { _lastReset[entry.Key] = now; }
                    resets++;
                }

                lock (_lock)
                {
                    if (_lastReset.Count > CooldownCleanupThreshold)
                    {
                        _reapBuffer.Clear();
                        foreach (var kvp in _lastReset)
                        {
                            if ((now - kvp.Value).Ticks > PerBlockCooldown.Ticks * 2) _reapBuffer.Add(kvp.Key);
                        }
                        for (var i = 0; i < _reapBuffer.Count; i++) _lastReset.Remove(_reapBuffer[i]);
                        _reapBuffer.Clear();
                    }
                }
            }
            finally
            {
                if (profilerTs != 0L)
                {
                    var _resets = resets;
                    var _dequeued = dequeued;
                    MethodProfiler.StopAndLog("SkeletonResetHandler.Process", profilerTs, () =>
                        string.Format("dequeued={0};resets={1};queueAfter={2}", _dequeued, _resets, _queue.Count));
                }
            }
        }

        /// <summary>World unload — drop block references and session-time state.</summary>
        public static void Clear()
        {
            lock (_lock)
            {
                _queue.Clear();
                _pendingKeys.Clear();
                _lastReset.Clear();
                _reapBuffer.Clear();
            }
        }
    }
}
