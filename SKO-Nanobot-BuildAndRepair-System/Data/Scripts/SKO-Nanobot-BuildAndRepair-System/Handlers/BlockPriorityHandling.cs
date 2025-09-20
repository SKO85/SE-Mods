using SKONanobotBuildAndRepairSystem.Models;
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
        public static readonly TtlCache<long, int> GetItemKeyCache = new TtlCache<long, int>(
          defaultTtl: TimeSpan.FromMinutes(5),
          concurrencyLevel: 4,
          comparer: null,
          capacity: 1024);

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

            if (block == null)
                return (int)BlockClass.ArmorBlock;

            // Check cache.
            int result = 14;
            if (GetItemKeyCache.TryGet(block.EntityId, out result))
            {
                return result;
            }

            var functionalBlock = block as Sandbox.ModAPI.IMyFunctionalBlock;
            if (!real && functionalBlock != null && !functionalBlock.Enabled) result = (int)BlockClass.ArmorBlock;
            else if (block is Sandbox.ModAPI.IMyShipWelder && block.BlockDefinition.SubtypeName.Contains("NanobotBuildAndRepairSystem")) result = (int)BlockClass.AutoRepairSystem;
            else if (block is Sandbox.ModAPI.IMyShipController) result = (int)BlockClass.ShipController;
            else if (block is Sandbox.ModAPI.IMyThrust || block is Sandbox.ModAPI.IMyWheel || block is Sandbox.ModAPI.IMyMotorRotor) result = (int)BlockClass.Thruster;
            else if (block is Sandbox.ModAPI.IMyGyro) result = (int)BlockClass.Gyroscope;
            else if (block is Sandbox.ModAPI.IMyCargoContainer) result = (int)BlockClass.CargoContainer;
            else if (block is Sandbox.ModAPI.IMyConveyor || a.FatBlock is Sandbox.ModAPI.IMyConveyorSorter || a.FatBlock is Sandbox.ModAPI.IMyConveyorTube) result = (int)BlockClass.Conveyor;
            else if (block is Sandbox.ModAPI.IMyUserControllableGun) result = (int)BlockClass.ControllableGun;
            else if (block is Sandbox.ModAPI.IMyWarhead) result = (int)BlockClass.ControllableGun;
            else if (block is Sandbox.ModAPI.IMyPowerProducer) result = (int)BlockClass.PowerBlock;
            else if (block is Sandbox.ModAPI.IMyProgrammableBlock) result = (int)BlockClass.ProgrammableBlock;
            else if (block is SpaceEngineers.Game.ModAPI.IMyTimerBlock) result = (int)BlockClass.ProgrammableBlock;
            else if (block is Sandbox.ModAPI.IMyProjector) result = (int)BlockClass.Projector;
            else if (block is Sandbox.ModAPI.IMyDoor) result = (int)BlockClass.Door;
            else if (block is Sandbox.ModAPI.IMyProductionBlock) result = (int)BlockClass.ProductionBlock;

            // Added.
            else if (block is Sandbox.ModAPI.IMyTextPanel || block is Sandbox.ModAPI.Ingame.IMyTextSurface) result = (int)BlockClass.DisplayPanel;
            else if (block is Sandbox.ModAPI.IMyLightingBlock) result = (int)BlockClass.Lighting;
            else if (block is Sandbox.ModAPI.IMySensorBlock || block is Sandbox.ModAPI.IMyCameraBlock) result = (int)BlockClass.SensorDevice;
            else if (block is Sandbox.ModAPI.IMyRadioAntenna || block is Sandbox.ModAPI.IMyLaserAntenna) result = (int)BlockClass.CommunicationBlock;
            else if (functionalBlock != null) result = (int)BlockClass.FunctionalBlock;
            else result = (int)BlockClass.ArmorBlock;

            GetItemKeyCache.Set(block.EntityId, result);
            return result;
        }

        public override string GetItemAlias(IMySlimBlock a, bool real)
        {
            var key = GetItemKey(a, real);
            return ((BlockClass)key).ToString();
        }
    }
}