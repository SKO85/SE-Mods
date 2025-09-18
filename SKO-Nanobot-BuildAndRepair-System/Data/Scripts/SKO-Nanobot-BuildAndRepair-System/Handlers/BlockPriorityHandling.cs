using System;
using VRage.Game.ModAPI;

namespace SKONanobotBuildAndRepairSystem.Handlers
{
    public enum BlockClass
    {
        AutoRepairSystem = 1,
        ShipController,
        Thruster,
        Gyroscope,
        CargoContainer,
        Conveyor,
        ControllableGun,
        PowerBlock,
        ProgrammableBlock,
        Projector,
        FunctionalBlock,
        ProductionBlock,
        Door,
        ArmorBlock,
        DisplayPanel,
        Lighting,
        SensorDevice,
        CommunicationBlock
    }

    public enum ComponentClass
    {
        Material = 1,
        Ingot,
        Ore,
        Stone,
        Gravel
    }

    public class BlockPriorityHandling : PriorityHandling<PrioItem, IMySlimBlock>
    {
        public BlockPriorityHandling()
        {
            foreach (var item in Enum.GetValues(typeof(BlockClass)))
            {
                Add(new PrioItemState<PrioItem>(new PrioItem((int)item, item.ToString()), true, true));
            }
        }

        /// <summary>
        /// Get the Block class
        /// </summary>
        /// <param name="a"></param>
        /// <returns></returns>
        public override int GetItemKey(IMySlimBlock a, bool real)
        {
            var block = a.FatBlock;
            if (block == null) return (int)BlockClass.ArmorBlock;
            var functionalBlock = block as Sandbox.ModAPI.IMyFunctionalBlock;
            if (!real && functionalBlock != null && !functionalBlock.Enabled) return (int)BlockClass.ArmorBlock; //Switched off -> handle as structural block (if logical class is asked)

            if (block is Sandbox.ModAPI.IMyShipWelder && block.BlockDefinition.SubtypeName.Contains("NanobotBuildAndRepairSystem")) return (int)BlockClass.AutoRepairSystem;
            if (block is Sandbox.ModAPI.IMyShipController) return (int)BlockClass.ShipController;
            if (block is Sandbox.ModAPI.IMyThrust || block is Sandbox.ModAPI.IMyWheel || block is Sandbox.ModAPI.IMyMotorRotor) return (int)BlockClass.Thruster;
            if (block is Sandbox.ModAPI.IMyGyro) return (int)BlockClass.Gyroscope;
            if (block is Sandbox.ModAPI.IMyCargoContainer) return (int)BlockClass.CargoContainer;
            if (block is Sandbox.ModAPI.IMyConveyor || a.FatBlock is Sandbox.ModAPI.IMyConveyorSorter || a.FatBlock is Sandbox.ModAPI.IMyConveyorTube) return (int)BlockClass.Conveyor;
            if (block is Sandbox.ModAPI.IMyUserControllableGun) return (int)BlockClass.ControllableGun;
            if (block is Sandbox.ModAPI.IMyWarhead) return (int)BlockClass.ControllableGun;
            if (block is Sandbox.ModAPI.IMyPowerProducer) return (int)BlockClass.PowerBlock;
            if (block is Sandbox.ModAPI.IMyProgrammableBlock) return (int)BlockClass.ProgrammableBlock;
            if (block is SpaceEngineers.Game.ModAPI.IMyTimerBlock) return (int)BlockClass.ProgrammableBlock;
            if (block is Sandbox.ModAPI.IMyProjector) return (int)BlockClass.Projector;
            if (block is Sandbox.ModAPI.IMyDoor) return (int)BlockClass.Door;
            if (block is Sandbox.ModAPI.IMyProductionBlock) return (int)BlockClass.ProductionBlock;
            if (functionalBlock != null) return (int)BlockClass.FunctionalBlock;

            return (int)BlockClass.ArmorBlock;
        }

        public override string GetItemAlias(IMySlimBlock a, bool real)
        {
            var key = GetItemKey(a, real);
            return ((BlockClass)key).ToString();
        }
    }
}