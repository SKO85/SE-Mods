using VRage.Game.ModAPI;
using Sandbox.Game.Entities;
using System.Collections.Generic;
using System;
using System.Linq;

namespace SKONanobotBuildAndRepairSystem
{
    public static class WeldManager
    {
        private static readonly Dictionary<IMySlimBlock, long> _assignments = new Dictionary<IMySlimBlock, long>();
        private static DateTime _lastCleanupTime = DateTime.MinValue;
        private static readonly TimeSpan CleanupInterval = TimeSpan.FromSeconds(10);

        public static bool TryAssign(IMySlimBlock block, long systemId)
        {
            CleanupIfNeeded();

            lock (_assignments)
            {
                long existing;
                if (_assignments.TryGetValue(block, out existing))
                {
                    return existing == systemId;
                }
                _assignments[block] = systemId;
                return true;
            }
        }

        public static void ReleaseAll(long systemId)
        {
            lock (_assignments)
            {
                var toRemove = _assignments.Where(x => x.Value == systemId).Select(x => x.Key).ToList();
                foreach (var block in toRemove)
                    _assignments.Remove(block);
            }
        }

        public static void Cleanup(IMySlimBlock block)
        {
            lock (_assignments)
            {
                _assignments.Remove(block);
            }
        }

        private static void CleanupIfNeeded()
        {
            if ((DateTime.UtcNow - _lastCleanupTime) < CleanupInterval)
                return;

            _lastCleanupTime = DateTime.UtcNow;

            lock (_assignments)
            {
                var toRemove = new List<IMySlimBlock>();
                foreach (var kv in _assignments)
                {
                    if (kv.Key == null || kv.Key.CubeGrid == null || kv.Key.IsDestroyed || kv.Key.FatBlock == null || kv.Key.FatBlock.Closed)
                        toRemove.Add(kv.Key);
                }

                foreach (var block in toRemove)
                {
                    Deb.Write("Removed welded block from dictionary.");
                    _assignments.Remove(block);
                }
                    
            }
        }

        public static void TryWelding(
            NanobotBuildAndRepairSystemBlock block,
            out bool welding,
            out bool needWelding,
            out bool transporting,
            out IMySlimBlock currentWeldingBlock)
        {
            welding = false;
            needWelding = false;
            transporting = false;
            currentWeldingBlock = null;

            var hasRequiredPower = PowerManager.HasRequiredElectricPower(block);
            if (!hasRequiredPower) return;

            lock (block.State.PossibleWeldTargets)
            {
                foreach (var targetData in block.State.PossibleWeldTargets)
                {
                    if ((block.Settings.Flags & SyncBlockSettings.Settings.ScriptControlled) != 0 &&
                        targetData.Block != block.Settings.CurrentPickedWeldingBlock)
                        continue;

                    if ((block.Settings.Flags & SyncBlockSettings.Settings.ScriptControlled) != 0 ||
                        (!targetData.Ignore && IsWeldable(block, targetData)))
                    {
                        if (!TryAssign(targetData.Block, block.Entity.EntityId))
                        {
                            Deb.Write("Already assigned to a system.");
                            continue;
                        }

                        needWelding = true;

                        //if (powerForWeldingAndTransport && !transporting)
                        //{
                            transporting = block.ServerFindMissingComponents(targetData);
                        //}

                        welding = block.ServerDoWeld(targetData);
                        InventoryManager.EmptyTransportInventory(block, false);
                        if (targetData.Ignore)
                            block.State.PossibleWeldTargets.ChangeHash();

                        if (welding)
                        {
                            currentWeldingBlock = targetData.Block;
                            break;
                        }
                    }
                }
            }
        }

        public static bool IsWeldable(NanobotBuildAndRepairSystemBlock block, TargetBlockData targetData)
        {
            var target = targetData.Block;
            if ((targetData.Attributes & TargetBlockData.AttributeFlags.Projected) != 0)
            {
                if (target.CanBuild(true))
                {
                    return true;
                }

                var cubeGridProjected = target.CubeGrid as MyCubeGrid;
                if (cubeGridProjected != null && cubeGridProjected.Projector != null)
                {
                    var cubeGrid = cubeGridProjected.Projector.CubeGrid;
                    var blockPos = cubeGrid.WorldToGridInteger(cubeGridProjected.GridIntegerToWorld(target.Position));
                    target = cubeGrid.GetCubeBlock(blockPos);

                    if (target != null)
                    {
                        targetData.Block = target;
                        targetData.Attributes &= ~TargetBlockData.AttributeFlags.Projected;
                        return IsWeldable(block, targetData);
                    }
                }

                Deb.Write($"Is not weldable");

                targetData.Ignore = true;
                return false;
            }

            var weld = (block.IsWeldIntegrityReached(target) || target.NeedRepair(block.GetIntegrityLevel())) && !block.IsFriendlyDamage(target);

            Deb.Write($"Should weld: {weld}");

            targetData.Ignore = !weld;
            return weld;
        }
    }
}