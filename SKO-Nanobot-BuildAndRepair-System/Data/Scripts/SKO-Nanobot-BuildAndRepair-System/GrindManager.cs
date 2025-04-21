using VRage.Game.ModAPI;
using Sandbox.Game.Entities;
using System.Collections.Generic;
using System.Linq;
using System;

namespace SKONanobotBuildAndRepairSystem
{
    public static class GrindManager
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
                    Deb.Write("Removed grinded block from dictionary.");
                    _assignments.Remove(block);
                }
                    
            }
        }

        public static void TryGrinding(
            NanobotBuildAndRepairSystemBlock block,
            out bool grinding,
            out bool needGrinding,
            out bool transporting,
            out IMySlimBlock currentGrindingBlock)
        {
            grinding = false;
            needGrinding = false;
            transporting = false;
            currentGrindingBlock = null;

            var hasRequiredPower = PowerManager.HasRequiredElectricPower(block);
            if (!hasRequiredPower) return;

            lock (block.State.PossibleGrindTargets)
            {
                MyCubeGrid cubeGrid = null;

                foreach (var targetData in block.State.PossibleGrindTargets)
                {
                    if (!TryAssign(targetData.Block, block.Entity.EntityId))
                    {
                        continue;
                    }

                    if (cubeGrid == null)
                    {
                        cubeGrid = targetData.Block.CubeGrid as MyCubeGrid;
                        if (cubeGrid != null && !cubeGrid.IsStatic)
                        {
                            cubeGrid.Physics.ClearSpeed();
                        }
                    }

                    if ((block.Settings.Flags & SyncBlockSettings.Settings.ScriptControlled) != 0 &&
                        targetData.Block != block.Settings.CurrentPickedGrindingBlock)
                        continue;

                    if (!targetData.Block.IsDestroyed)
                    {
                        needGrinding = true;
                        grinding = block.ServerDoGrind(targetData, out transporting);
                        if (grinding)
                        {
                            currentGrindingBlock = targetData.Block;
                            break;
                        }
                    }
                }
            }

            if (currentGrindingBlock != null)
            {
                var ownerId = UtilsPlayer.GetOwner(currentGrindingBlock.CubeGrid as MyCubeGrid);
                if (ownerId > 0)
                {
                    UtilsFaction.DamageReputationWithPlayerFaction(block.Welder.OwnerId, ownerId);
                }
            }
        }

        public static bool IsShieldProtected(NanobotBuildAndRepairSystemBlock block, IMySlimBlock slimBlock)
        {
            try
            {
                if (slimBlock != null && NanobotBuildAndRepairSystemBlock.Shield != null && NanobotBuildAndRepairSystemBlock.Shield.IsReady)
                {
                    var isProtected = NanobotBuildAndRepairSystemBlock.Shield.ProtectedByShield(slimBlock.CubeGrid);

                    if (!isProtected)
                        return false;

                    if (slimBlock.CubeGrid.EntityId == block.Welder.CubeGrid.EntityId)
                        return false;

                    return NanobotBuildAndRepairSystemBlock.Shield.IsBlockProtected(slimBlock);
                }
            }
            catch { }

            return false;
        }
    }
}