using VRage.Game.ModAPI;
using Sandbox.Game.Entities;

namespace SKONanobotBuildAndRepairSystem
{
    public static class WeldManager
    {
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

            var powerForWeldingAndTransport = PowerManager.HasRequiredElectricPower(block);
            var powerForWeldingOnly = powerForWeldingAndTransport || PowerManager.HasRequiredElectricPower(block);

            if (!powerForWeldingOnly) return;

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
                        needWelding = true;

                        if (powerForWeldingAndTransport && !transporting)
                        {
                            transporting = block.ServerFindMissingComponents(targetData);
                        }

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
                if (target.CanBuild(true)) return true;

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
                targetData.Ignore = true;
                return false;
            }

            var functionalOnly = (block.Settings.WeldOptions & AutoWeldOptions.FunctionalOnly) != 0;
            var weld = target.NeedRepair(functionalOnly) && !block.IsFriendlyDamage(target);
            targetData.Ignore = !weld;
            return weld;
        }
    }
}