using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using VRage.Game.ModAPI;

namespace SKONanobotBuildAndRepairSystem
{
    internal class BlockSystemAssignmentHandler
    {
        private readonly ConcurrentDictionary<IMySlimBlock, BlockSystemAssignment> _assignments = new ConcurrentDictionary<IMySlimBlock, BlockSystemAssignment>();
        private DateTime _lastCleanupTime = DateTime.MinValue;
        private readonly TimeSpan CleanupInterval = TimeSpan.FromSeconds(10);
        private readonly int MaxToKeepAssignedSeconds = 60;

        public bool TryAssign(IMySlimBlock block, long systemId)
        {
            try
            {
                CleanupIfNeeded();

                lock (_assignments)
                {
                    BlockSystemAssignment existing;
                    if (_assignments.TryGetValue(block, out existing))
                    {
                        return existing.SystemId == systemId;
                    }

                    _assignments[block] = new BlockSystemAssignment(block, systemId);
                    return true;
                }
            }
            catch (Exception ex)
            {
            }

            return false;            
        }

        public void ReleaseAll(long systemId)
        {
            lock (_assignments)
            {
                var toRemove = _assignments.Where(x => x.Value.SystemId == systemId).Select(x => x.Key).ToList();

                foreach (var block in toRemove)
                    _assignments.Remove(block);
            }
        }

        public void Cleanup(IMySlimBlock block)
        {
            lock (_assignments)
            {
                _assignments.Remove(block);
            }
        }

        private void CleanupIfNeeded()
        {
            if ((DateTime.UtcNow - _lastCleanupTime) < CleanupInterval)
                return;

            _lastCleanupTime = DateTime.UtcNow;

            lock (_assignments)
            {
                var toRemove = new List<IMySlimBlock>();
                var utcNow = DateTime.UtcNow;
                foreach (var kv in _assignments)
                {
                    if (kv.Key == null || kv.Key.CubeGrid == null || kv.Key.IsDestroyed || kv.Key.FatBlock == null || kv.Key.FatBlock.Closed)
                        toRemove.Add(kv.Key);

                    else if (utcNow.Subtract(kv.Value.DateTimeAssigned).TotalSeconds >= MaxToKeepAssignedSeconds)
                    {
                        toRemove.Add(kv.Key);
                    }
                }

                foreach (var block in toRemove)
                {
                    _assignments.Remove(block);
                    Deb.Write("Removed from assignment.");
                }
            }
        }
    }

    internal class BlockSystemAssignment
    {
        public IMySlimBlock Block { get; }
        public long SystemId { get; }
        public DateTime DateTimeAssigned { get; }

        public BlockSystemAssignment(IMySlimBlock block, long systemId)
        {
            Block = block;
            SystemId = systemId;
            DateTimeAssigned = DateTime.UtcNow;
        }
    }
}
