namespace SKONanobotBuildAndRepairSystem
{
    using System;
    using VRage.Game;
    using VRage.Game.ModAPI;

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

    public class NanobotBuildAndRepairSystemBlockPriorityHandling : PriorityHandling<PrioItem, IMySlimBlock>
    {
        private readonly System.Collections.Generic.Dictionary<long, int> _blockClassCache = new System.Collections.Generic.Dictionary<long, int>();

        public NanobotBuildAndRepairSystemBlockPriorityHandling()
        {
            foreach (var item in Enum.GetValues(typeof(BlockClass)))
            {
                Add(new PrioItemState<PrioItem>(new PrioItem((int)item, item.ToString()), true, true));
            }
        }

        public override int GetItemKey(IMySlimBlock a, bool real)
        {
            var fb = a.FatBlock;
            if (fb == null) return (int)BlockClass.ArmorBlock;
            var id = fb.EntityId;
            int cls;
            if (_blockClassCache.TryGetValue(id, out cls))
            {
                if (!real)
                {
                    var f = fb as Sandbox.ModAPI.IMyFunctionalBlock;
                    if (f != null && !f.Enabled) return (int)BlockClass.ArmorBlock;
                }
                return cls;
            }

            var block = fb;
            var functionalBlock = block as Sandbox.ModAPI.IMyFunctionalBlock;
            if (!real && functionalBlock != null && !functionalBlock.Enabled) return (int)BlockClass.ArmorBlock; // logical class treats disabled functionals as armor

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

            // Added.
            if (block is Sandbox.ModAPI.IMyTextPanel || block is Sandbox.ModAPI.Ingame.IMyTextSurface) return (int)BlockClass.DisplayPanel;
            if (block is Sandbox.ModAPI.IMyLightingBlock) return (int)BlockClass.Lighting;
            if (block is Sandbox.ModAPI.IMySensorBlock || block is Sandbox.ModAPI.IMyCameraBlock) return (int)BlockClass.SensorDevice;
            if (block is Sandbox.ModAPI.IMyRadioAntenna || block is Sandbox.ModAPI.IMyLaserAntenna) return (int)BlockClass.CommunicationBlock;

            if (functionalBlock != null) cls = (int)BlockClass.FunctionalBlock;
            else cls = (int)BlockClass.ArmorBlock;
            _blockClassCache[id] = cls;
            return cls;
        }

        public override string GetItemAlias(IMySlimBlock a, bool real)
        {
            var key = GetItemKey(a, real);
            return ((BlockClass)key).ToString();
        }
    }

    public class NanobotBuildAndRepairSystemComponentPriorityHandling : PriorityHandling<PrioItem, MyDefinitionId>
    {
        public NanobotBuildAndRepairSystemComponentPriorityHandling()
        {
            foreach (var item in Enum.GetValues(typeof(ComponentClass)))
            {
                Add(new PrioItemState<PrioItem>(new PrioItem((int)item, item.ToString()), true, true));
            }
        }

        public override int GetItemKey(MyDefinitionId a, bool real)
        {
            if (a.TypeId == typeof(MyObjectBuilder_Ingot))
            {
                if (a.SubtypeName == "Stone") return (int)ComponentClass.Gravel;
                return (int)ComponentClass.Ingot;
            }
            if (a.TypeId == typeof(MyObjectBuilder_Ore))
            {
                if (a.SubtypeName == "Stone") return (int)ComponentClass.Stone;
                return (int)ComponentClass.Ore;
            }
            return (int)ComponentClass.Material;
        }

        public override string GetItemAlias(MyDefinitionId a, bool real)
        {
            var key = GetItemKey(a, real);
            return ((ComponentClass)key).ToString();
        }
    }
}
