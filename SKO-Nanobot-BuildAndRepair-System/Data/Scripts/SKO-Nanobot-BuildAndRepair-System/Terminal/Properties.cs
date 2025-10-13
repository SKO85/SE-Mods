using Sandbox.ModAPI;
using SKONanobotBuildAndRepairSystem.Utils;
using System;
using System.Collections.Generic;
using VRage;
using VRage.Scripting.MemorySafeTypes;
using VRageMath;

namespace SKONanobotBuildAndRepairSystem.Terminal
{
    public static class Properties
    {
        public static void IgnoreColor()
        {
            var propertyIC = MyAPIGateway.TerminalControls.CreateProperty<Vector3, IMyShipWelder>("BuildAndRepair.IgnoreColor");
            propertyIC.SupportsMultipleBlocks = false;

            propertyIC.Getter = (block) =>
            {
                var system = NanobotTerminal.GetSystem(block);
                return system != null ? NanobotTerminal.ConvertFromHSVColor(system.Settings.IgnoreColor) : Vector3.Zero;
            };

            propertyIC.Setter = (block, value) =>
            {
                var system = NanobotTerminal.GetSystem(block);
                if (system != null && !Mod.Settings.Welder.UseIgnoreColorFixed)
                {
                    system.Settings.IgnoreColor = NanobotTerminal.CheckConvertToHSVColor(value);
                }
            };

            MyAPIGateway.TerminalControls.AddControl<IMyShipWelder>(propertyIC);
        }

        public static void GrindColor()
        {
            var propertyGC = MyAPIGateway.TerminalControls.CreateProperty<Vector3, IMyShipWelder>("BuildAndRepair.GrindColor");
            propertyGC.SupportsMultipleBlocks = false;

            propertyGC.Getter = (block) =>
            {
                var system = NanobotTerminal.GetSystem(block);
                return system != null ? NanobotTerminal.ConvertFromHSVColor(system.Settings.GrindColor) : Vector3.Zero;
            };

            propertyGC.Setter = (block, value) =>
            {
                var system = NanobotTerminal.GetSystem(block);
                if (system != null && !Mod.Settings.Welder.UseGrindColorFixed)
                {
                    system.Settings.GrindColor = NanobotTerminal.CheckConvertToHSVColor(value);
                }
            };

            MyAPIGateway.TerminalControls.AddControl<IMyShipWelder>(propertyGC);
        }

        public static void WeldPriorityList()
        {
            var propertyWeldPriorityList = MyAPIGateway.TerminalControls.CreateProperty<MemorySafeList<string>, IMyShipWelder>("BuildAndRepair.WeldPriorityList");
            propertyWeldPriorityList.SupportsMultipleBlocks = false;
            propertyWeldPriorityList.Getter = (block) =>
            {
                var system = NanobotTerminal.GetSystem(block);
                return system != null ? system.BlockWeldPriority.GetList() : null;
            };
            MyAPIGateway.TerminalControls.AddControl<IMyShipWelder>(propertyWeldPriorityList);
        }

        public static void SetWeldPriority()
        {
            var propertySWP = MyAPIGateway.TerminalControls.CreateProperty<Action<int, int>, IMyShipWelder>("BuildAndRepair.SetWeldPriority");
            propertySWP.SupportsMultipleBlocks = false;
            propertySWP.Getter = (block) =>
            {
                var system = NanobotTerminal.GetSystem(block);
                if (system != null)
                {
                    return system.BlockWeldPriority.SetPriority;
                }
                return null;
            };
            MyAPIGateway.TerminalControls.AddControl<IMyShipWelder>(propertySWP);
        }

        public static void GetWeldPriority()
        {
            var propertyGWP = MyAPIGateway.TerminalControls.CreateProperty<Func<int, int>, IMyShipWelder>("BuildAndRepair.GetWeldPriority");
            propertyGWP.SupportsMultipleBlocks = false;
            propertyGWP.Getter = (block) =>
            {
                var system = NanobotTerminal.GetSystem(block);
                if (system != null)
                {
                    return system.BlockWeldPriority.GetPriority;
                }
                return null;
            };
            MyAPIGateway.TerminalControls.AddControl<IMyShipWelder>(propertyGWP);
        }

        public static void SetWeldEnabled()
        {
            var propertySWE = MyAPIGateway.TerminalControls.CreateProperty<Action<int, bool>, IMyShipWelder>("BuildAndRepair.SetWeldEnabled");
            propertySWE.SupportsMultipleBlocks = false;
            propertySWE.Getter = (block) =>
            {
                var system = NanobotTerminal.GetSystem(block);
                if (system != null)
                {
                    return system.BlockWeldPriority.SetEnabled;
                }
                return null;
            };
            MyAPIGateway.TerminalControls.AddControl<IMyShipWelder>(propertySWE);
        }

        public static void GetWeldEnabled()
        {
            var propertyGWE = MyAPIGateway.TerminalControls.CreateProperty<Func<int, bool>, IMyShipWelder>("BuildAndRepair.GetWeldEnabled");
            propertyGWE.SupportsMultipleBlocks = false;
            propertyGWE.Getter = (block) =>
            {
                var system = NanobotTerminal.GetSystem(block);
                if (system != null)
                {
                    return system.BlockWeldPriority.GetEnabled;
                }
                return null;
            };
            MyAPIGateway.TerminalControls.AddControl<IMyShipWelder>(propertyGWE);
        }

        public static void GrindPriorityList()
        {
            var propertyGrindPriorityList = MyAPIGateway.TerminalControls.CreateProperty<MemorySafeList<string>, IMyShipWelder>("BuildAndRepair.GrindPriorityList");
            propertyGrindPriorityList.SupportsMultipleBlocks = false;
            propertyGrindPriorityList.Getter = (block) =>
            {
                var system = NanobotTerminal.GetSystem(block);
                return system?.BlockGrindPriority.GetList();
            };
            MyAPIGateway.TerminalControls.AddControl<IMyShipWelder>(propertyGrindPriorityList);
        }

        public static void SetGrindPriority()
        {
            var propertySGP = MyAPIGateway.TerminalControls.CreateProperty<Action<int, int>, IMyShipWelder>("BuildAndRepair.SetGrindPriority");
            propertySGP.SupportsMultipleBlocks = false;
            propertySGP.Getter = (block) =>
            {
                var system = NanobotTerminal.GetSystem(block);
                if (system != null)
                {
                    return system.BlockGrindPriority.SetPriority;
                }
                return null;
            };
            MyAPIGateway.TerminalControls.AddControl<IMyShipWelder>(propertySGP);
        }

        public static void GetGrindPriority()
        {
            var propertyGGP = MyAPIGateway.TerminalControls.CreateProperty<Func<int, int>, IMyShipWelder>("BuildAndRepair.GetGrindPriority");
            propertyGGP.SupportsMultipleBlocks = false;
            propertyGGP.Getter = (block) =>
            {
                var system = NanobotTerminal.GetSystem(block);
                if (system != null)
                {
                    return system.BlockGrindPriority.GetPriority;
                }
                return null;
            };
            MyAPIGateway.TerminalControls.AddControl<IMyShipWelder>(propertyGGP);
        }

        public static void SetGrindEnabled()
        {
            var propertySGE = MyAPIGateway.TerminalControls.CreateProperty<Action<int, bool>, IMyShipWelder>("BuildAndRepair.SetGrindEnabled");
            propertySGE.SupportsMultipleBlocks = false;
            propertySGE.Getter = (block) =>
            {
                var system = NanobotTerminal.GetSystem(block);
                if (system != null)
                {
                    return system.BlockGrindPriority.SetEnabled;
                }
                return null;
            };
            MyAPIGateway.TerminalControls.AddControl<IMyShipWelder>(propertySGE);
        }

        public static void GetGrindEnabled()
        {
            var propertyGGE = MyAPIGateway.TerminalControls.CreateProperty<Func<int, bool>, IMyShipWelder>("BuildAndRepair.GetGrindEnabled");
            propertyGGE.SupportsMultipleBlocks = false;
            propertyGGE.Getter = (block) =>
            {
                var system = NanobotTerminal.GetSystem(block);
                if (system != null)
                {
                    return system.BlockGrindPriority.GetEnabled;
                }
                return null;
            };
            MyAPIGateway.TerminalControls.AddControl<IMyShipWelder>(propertyGGE);
        }

        public static void ComponentClassList()
        {
            var propertyComponentClassList = MyAPIGateway.TerminalControls.CreateProperty<MemorySafeList<string>, IMyShipWelder>("BuildAndRepair.ComponentClassList");
            propertyComponentClassList.SupportsMultipleBlocks = false;
            propertyComponentClassList.Getter = (block) =>
            {
                var system = NanobotTerminal.GetSystem(block);
                return system != null ? system.ComponentCollectPriority.GetList() : null;
            };
            MyAPIGateway.TerminalControls.AddControl<IMyShipWelder>(propertyComponentClassList);
        }

        public static void SetCollectPriority()
        {
            var propertySPC = MyAPIGateway.TerminalControls.CreateProperty<Action<int, int>, IMyShipWelder>("BuildAndRepair.SetCollectPriority");
            propertySPC.SupportsMultipleBlocks = false;
            propertySPC.Getter = (block) =>
            {
                var system = NanobotTerminal.GetSystem(block);
                if (system != null)
                {
                    return system.ComponentCollectPriority.SetPriority;
                }
                return null;
            };
            MyAPIGateway.TerminalControls.AddControl<IMyShipWelder>(propertySPC);
        }

        public static void GetCollectPriority()
        {
            var propertyGPC = MyAPIGateway.TerminalControls.CreateProperty<Func<int, int>, IMyShipWelder>("BuildAndRepair.GetCollectPriority");
            propertyGPC.SupportsMultipleBlocks = false;
            propertyGPC.Getter = (block) =>
            {
                var system = NanobotTerminal.GetSystem(block);
                if (system != null)
                {
                    return system.ComponentCollectPriority.GetPriority;
                }
                return null;
            };
            MyAPIGateway.TerminalControls.AddControl<IMyShipWelder>(propertyGPC);
        }

        public static void SetCollectEnabled()
        {
            var propertySEC = MyAPIGateway.TerminalControls.CreateProperty<Action<int, bool>, IMyShipWelder>("BuildAndRepair.SetCollectEnabled");
            propertySEC.SupportsMultipleBlocks = false;
            propertySEC.Getter = (block) =>
            {
                var system = NanobotTerminal.GetSystem(block);
                if (system != null)
                {
                    return system.ComponentCollectPriority.SetEnabled;
                }
                return null;
            };
            MyAPIGateway.TerminalControls.AddControl<IMyShipWelder>(propertySEC);
        }

        public static void GetCollectEnabled()
        {
            var propertyGEC = MyAPIGateway.TerminalControls.CreateProperty<Func<int, bool>, IMyShipWelder>("BuildAndRepair.GetCollectEnabled");
            propertyGEC.SupportsMultipleBlocks = false;
            propertyGEC.Getter = (block) =>
            {
                var system = NanobotTerminal.GetSystem(block);
                if (system != null)
                {
                    return system.ComponentCollectPriority.GetEnabled;
                }
                return null;
            };
            MyAPIGateway.TerminalControls.AddControl<IMyShipWelder>(propertyGEC);
        }

        public static void MissingComponents()
        {
            var propertyMissingComponentsDict = MyAPIGateway.TerminalControls.CreateProperty<MemorySafeDictionary<VRage.Game.MyDefinitionId, int>, IMyShipWelder>("BuildAndRepair.MissingComponents");
            propertyMissingComponentsDict.SupportsMultipleBlocks = false;
            propertyMissingComponentsDict.Getter = (block) =>
            {
                var system = NanobotTerminal.GetSystem(block);
                return system != null ? system.GetMissingComponentsDict() : null;
            };
            MyAPIGateway.TerminalControls.AddControl<IMyShipWelder>(propertyMissingComponentsDict);
        }

        public static void PossibleTargets()
        {
            var propertyPossibleWeldTargetsList = MyAPIGateway.TerminalControls.CreateProperty<MemorySafeList<VRage.Game.ModAPI.Ingame.IMySlimBlock>, IMyShipWelder>("BuildAndRepair.PossibleTargets");
            propertyPossibleWeldTargetsList.SupportsMultipleBlocks = false;
            propertyPossibleWeldTargetsList.Getter = (block) =>
            {
                var system = NanobotTerminal.GetSystem(block);
                return system != null ? system.GetPossibleWeldTargetsList() : null;
            };
            MyAPIGateway.TerminalControls.AddControl<IMyShipWelder>(propertyPossibleWeldTargetsList);
        }

        public static void PossibleGrindTargets()
        {
            var propertyPossibleGrindTargetsList = MyAPIGateway.TerminalControls.CreateProperty<MemorySafeList<VRage.Game.ModAPI.Ingame.IMySlimBlock>, IMyShipWelder>("BuildAndRepair.PossibleGrindTargets");
            propertyPossibleGrindTargetsList.SupportsMultipleBlocks = false;
            propertyPossibleGrindTargetsList.Getter = (block) =>
            {
                var system = NanobotTerminal.GetSystem(block);
                return system != null ? system.GetPossibleGrindTargetsList() : null;
            };
            MyAPIGateway.TerminalControls.AddControl<IMyShipWelder>(propertyPossibleGrindTargetsList);
        }

        public static void PossibleCollectTargets()
        {
            var propertyPossibleCollectTargetsList = MyAPIGateway.TerminalControls.CreateProperty<MemorySafeList<VRage.Game.ModAPI.Ingame.IMyEntity>, IMyShipWelder>("BuildAndRepair.PossibleCollectTargets");
            propertyPossibleCollectTargetsList.SupportsMultipleBlocks = false;
            propertyPossibleCollectTargetsList.Getter = (block) =>
            {
                var system = NanobotTerminal.GetSystem(block);
                return system != null ? system.GetPossibleCollectingTargetsList() : null;
            };
            MyAPIGateway.TerminalControls.AddControl<IMyShipWelder>(propertyPossibleCollectTargetsList);
        }

        public static void CurrentPickedTarget()
        {
            var propertyCPT = MyAPIGateway.TerminalControls.CreateProperty<VRage.Game.ModAPI.Ingame.IMySlimBlock, IMyShipWelder>("BuildAndRepair.CurrentPickedTarget");
            propertyCPT.SupportsMultipleBlocks = false;
            propertyCPT.Getter = (block) =>
            {
                var system = NanobotTerminal.GetSystem(block);
                return system != null ? system.Settings.CurrentPickedWeldingBlock : null;
            };
            propertyCPT.Setter = (block, value) =>
            {
                var system = NanobotTerminal.GetSystem(block);
                if (system != null)
                {
                    system.Settings.CurrentPickedWeldingBlock = value;
                }
            };
            MyAPIGateway.TerminalControls.AddControl<IMyShipWelder>(propertyCPT);
        }

        public static void CurrentTarget()
        {
            var propertyCT = MyAPIGateway.TerminalControls.CreateProperty<VRage.Game.ModAPI.Ingame.IMySlimBlock, IMyShipWelder>("BuildAndRepair.CurrentTarget");
            propertyCT.SupportsMultipleBlocks = false;
            propertyCT.Getter = (block) =>
            {
                var system = NanobotTerminal.GetSystem(block);
                return system != null ? system.State.CurrentWeldingBlock : null;
            };
            MyAPIGateway.TerminalControls.AddControl<IMyShipWelder>(propertyCT);
        }

        public static void CurrentPickedGrindTarget()
        {
            var propertyCPGT = MyAPIGateway.TerminalControls.CreateProperty<VRage.Game.ModAPI.Ingame.IMySlimBlock, IMyShipWelder>("BuildAndRepair.CurrentPickedGrindTarget");
            propertyCPGT.SupportsMultipleBlocks = false;
            propertyCPGT.Getter = (block) =>
            {
                var system = NanobotTerminal.GetSystem(block);
                return system != null ? system.Settings.CurrentPickedGrindingBlock : null;
            };
            propertyCPGT.Setter = (block, value) =>
            {
                var system = NanobotTerminal.GetSystem(block);
                if (system != null)
                {
                    system.Settings.CurrentPickedGrindingBlock = value;
                }
            };
            MyAPIGateway.TerminalControls.AddControl<IMyShipWelder>(propertyCPGT);
        }

        public static void CurrentGrindTarget()
        {
            var propertyCGT = MyAPIGateway.TerminalControls.CreateProperty<VRage.Game.ModAPI.Ingame.IMySlimBlock, IMyShipWelder>("BuildAndRepair.CurrentGrindTarget");
            propertyCGT.SupportsMultipleBlocks = false;
            propertyCGT.Getter = (block) =>
            {
                var system = NanobotTerminal.GetSystem(block);
                return system != null ? system.State.CurrentGrindingBlock : null;
            };
            MyAPIGateway.TerminalControls.AddControl<IMyShipWelder>(propertyCGT);
        }

        public static void ProductionBlockEnsureQueued()
        {
            var propertyPEQ = MyAPIGateway.TerminalControls.CreateProperty<Func<IEnumerable<long>, VRage.Game.MyDefinitionId, int, int>, IMyShipWelder>("BuildAndRepair.ProductionBlock.EnsureQueued");
            propertyPEQ.SupportsMultipleBlocks = false;
            propertyPEQ.Getter = (block) =>
            {
                return UtilsProductionBlock.EnsureQueued;
            };
            MyAPIGateway.TerminalControls.AddControl<IMyShipWelder>(propertyPEQ);
        }

        public static void InventoryNeededComponents4Blueprint()
        {
            var propertyNC4B = MyAPIGateway.TerminalControls.CreateProperty<Func<Sandbox.ModAPI.Ingame.IMyProjector, Dictionary<VRage.Game.MyDefinitionId, MyFixedPoint>, int>, IMyShipWelder>("BuildAndRepair.Inventory.NeededComponents4Blueprint");
            propertyNC4B.SupportsMultipleBlocks = false;
            propertyNC4B.Getter = (block) =>
            {
                return UtilsInventory.NeededComponents4Blueprint;
            };
            MyAPIGateway.TerminalControls.AddControl<IMyShipWelder>(propertyNC4B);
        }
    }
}