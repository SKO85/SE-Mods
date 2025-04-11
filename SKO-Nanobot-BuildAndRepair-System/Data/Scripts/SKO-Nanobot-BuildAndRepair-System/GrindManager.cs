﻿using VRage.Game.ModAPI;
using Sandbox.Game.Entities;

namespace SKONanobotBuildAndRepairSystem
{
    public static class GrindManager
    {
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

            if (!PowerManager.HasRequiredElectricPower(block))
                return;

            lock (block.State.PossibleGrindTargets)
            {
                MyCubeGrid cubeGrid = null;

                foreach (var targetData in block.State.PossibleGrindTargets)
                {
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