using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces.Terminal;
using SKONanobotBuildAndRepairSystem.Handlers;
using System;
using System.Collections.Generic;
using VRage.ModAPI;

namespace SKONanobotBuildAndRepairSystem.Terminal
{
    public static class ListBoxes
    {
        private static IMyTerminalControlListbox Create(
           string id,

           bool isMultiSelect,
           int visibleRowsCount,

           Func<IMyTerminalBlock, bool> isVisible,
           Func<IMyTerminalBlock, bool> isEnabled,

           Action<IMyTerminalBlock, List<MyTerminalControlListBoxItem>> itemsSelected,
           Action<IMyTerminalBlock, List<MyTerminalControlListBoxItem>, List<MyTerminalControlListBoxItem>> listContent,

           bool supportsMultipleBlocks)
        {
            var control = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlListbox, IMyShipWelder>(id);

            control.Multiselect = isMultiSelect;
            control.VisibleRowsCount = visibleRowsCount;
            control.Visible = isVisible;
            control.Enabled = isEnabled;

            control.ItemSelected = itemsSelected;
            control.ListContent = listContent;

            control.SupportsMultipleBlocks = supportsMultipleBlocks;

            NanobotTerminal.CustomControls.Add(control);
            return control;
        }

        public static IMyTerminalControlListbox CreateWeldPriority(bool weldingAllowed, Func<IMyTerminalBlock, bool> isReadonly, Func<IMyTerminalBlock, bool> isBaRSystem, Func<IMyTerminalBlock, bool> isWeldingAllowed)
        {
            var isEnabled = weldingAllowed ? isBaRSystem : isReadonly;
            var control = Create("WeldPriority", false, 15, isWeldingAllowed, isEnabled,

                // ItemsSelected:
                (block, selected) =>
                {
                    var system = NanobotTerminal.GetSystem(block);
                    if (system != null && system.BlockWeldPriority != null)
                    {
                        if (selected.Count > 0)
                            system.BlockWeldPriority.SetSelectedByKey(((PrioItem)selected[0].UserData).Key);
                        else
                            system.BlockWeldPriority.ClearSelected();

                        NanobotTerminal.UpdateVisual(NanobotTerminal._WeldEnableDisableSwitch);
                        NanobotTerminal.UpdateVisual(NanobotTerminal._WeldPriorityButtonUp);
                        NanobotTerminal.UpdateVisual(NanobotTerminal._WeldPriorityButtonDown);
                    }
                },

                // List Content:
                (block, items, selected) =>
                {
                    var system = NanobotTerminal.GetSystem(block);
                    if (system != null && system.BlockWeldPriority != null)
                    {
                        system.BlockWeldPriority.FillTerminalList(items, selected);
                    }
                },

                // Multiple Blocks
                true
            );

            return control;
        }

        public static IMyTerminalControlListbox CreateGrindPriority(bool grindingAllowed, Func<IMyTerminalBlock, bool> isGrindingAllowed, Func<IMyTerminalBlock, bool> isReadonly, Func<IMyTerminalBlock, bool> isBaRSystem)
        {
            var isEnabled = grindingAllowed ? isBaRSystem : isReadonly;
            var control = Create("GrindPriority", false, 15, isGrindingAllowed, isEnabled,

                // ItemsSelected:
                (block, selected) =>
                {
                    var system = NanobotTerminal.GetSystem(block);
                    if (system != null && system.BlockGrindPriority != null)
                    {
                        if (selected.Count > 0) system.BlockGrindPriority.SetSelectedByKey(((PrioItem)selected[0].UserData).Key);
                        else system.BlockGrindPriority.ClearSelected();
                        NanobotTerminal.UpdateVisual(NanobotTerminal._GrindEnableDisableSwitch);
                        NanobotTerminal.UpdateVisual(NanobotTerminal._GrindPriorityButtonUp);
                        NanobotTerminal.UpdateVisual(NanobotTerminal._GrindPriorityButtonDown);
                    }
                },

                // List Content:
                (block, items, selected) =>
                {
                    var system = NanobotTerminal.GetSystem(block);
                    if (system != null && system.BlockGrindPriority != null)
                    {
                        system.BlockGrindPriority.FillTerminalList(items, selected);
                    }
                },

                // Multiple Blocks
                true
            );

            return control;
        }

        public static IMyTerminalControlListbox CreateCollectPriority(Func<IMyTerminalBlock, bool> isCollectPossible, Func<IMyTerminalBlock, bool> isReadonly, Func<IMyTerminalBlock, bool> isBaRSystem)
        {
            var control = Create("CollectPriority", false, 5, (block) => { return true; }, isCollectPossible,

                // ItemsSelected:
                (block, selected) =>
                {
                    var system = NanobotTerminal.GetSystem(block);
                    if (system != null && system.ComponentCollectPriority != null)
                    {
                        if (selected.Count > 0) system.ComponentCollectPriority.SetSelectedByKey(((PrioItem)selected[0].UserData).Key);
                        else system.ComponentCollectPriority.ClearSelected();
                        NanobotTerminal.UpdateVisual(NanobotTerminal._ComponentCollectEnableDisableSwitch);
                        NanobotTerminal.UpdateVisual(NanobotTerminal._ComponentCollectPriorityButtonUp);
                        NanobotTerminal.UpdateVisual(NanobotTerminal._ComponentCollectPriorityButtonDown);
                    }
                },

                // List Content:
                (block, items, selected) =>
                {
                    var system = NanobotTerminal.GetSystem(block);
                    if (system != null && system.ComponentCollectPriority != null)
                    {
                        system.ComponentCollectPriority.FillTerminalList(items, selected);
                    }
                },

                // Multiple Blocks
                true
            );

            return control;
        }
    }
}