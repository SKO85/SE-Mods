using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces.Terminal;
using SKONanobotBuildAndRepairSystem.Localization;
using System;
using System.Collections.Generic;
using System.Text;
using VRage.ModAPI;
using VRage.Utils;

namespace SKONanobotBuildAndRepairSystem.Terminal
{
    public static class ComboBoxes
    {
        private static IMyTerminalControlCombobox Create(
            string id,
            MyStringId title,
            MyStringId tooltip,
            Func<IMyTerminalBlock, bool> isEnabled,
            Action<List<MyTerminalControlComboBoxItem>> comboBoxContent,
            Func<IMyTerminalBlock, long> getter,
            Action<IMyTerminalBlock, long> setter,
            bool supportsMultipleBlocks)
        {
            var control = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlCombobox, IMyShipWelder>(id);

            control.Title = title;
            control.Tooltip = tooltip;
            control.Enabled = isEnabled;
            control.ComboBoxContent = comboBoxContent;
            control.Getter = getter;
            control.Setter = setter;
            control.SupportsMultipleBlocks = supportsMultipleBlocks;

            NanobotTerminal.CustomControls.Add(control);
            return control;
        }

        public static IMyTerminalControlCombobox CreateSearchMode(bool onlyOneAllowed, Func<IMyTerminalBlock, bool> isReadonly, Func<IMyTerminalBlock, bool> isBaRSystem)
        {
            var isEnabled = onlyOneAllowed ? isReadonly : isBaRSystem;

            var control = Create(
                // Id:
                "Mode",

                // Texts
                Texts.SearchMode,
                Texts.SearchMode_Tooltip,

                // Enabled:
                isEnabled,

                // ComboboxContent:
                (list) =>
                {
                    if (Mod.Settings.Welder.AllowedSearchModes.HasFlag(SearchModes.Grids))
                        list.Add(new MyTerminalControlComboBoxItem() { Key = (long)SearchModes.Grids, Value = Texts.SearchMode_Walk });

                    if (Mod.Settings.Welder.AllowedSearchModes.HasFlag(SearchModes.BoundingBox))
                        list.Add(new MyTerminalControlComboBoxItem() { Key = (long)SearchModes.BoundingBox, Value = Texts.SearchMode_Fly });
                },

                // Getter:
                (block) =>
                {
                    var system = NanobotTerminal.GetSystem(block);

                    if (system == null)
                        return 0;
                    else return
                        (long)system.Settings.SearchMode;
                },

                // Setter:
                (block, value) =>
                {
                    var system = NanobotTerminal.GetSystem(block);
                    if (system != null)
                    {
                        if (Mod.Settings.Welder.AllowedSearchModes.HasFlag((SearchModes)value))
                        {
                            system.Settings.SearchMode = (SearchModes)value;
                            NanobotTerminal.UpdateVisual(NanobotTerminal._ComponentCollectPriorityListBox);
                            NanobotTerminal.UpdateVisual(NanobotTerminal._ComponentCollectIfIdleSwitch);
                        }
                    }
                },

                // Multiple blocks support.
                true
            );

            // Allow switch mode by Buttonpanel
            var list1 = new List<MyTerminalControlComboBoxItem>();
            control.ComboBoxContent(list1);
            foreach (var entry in list1)
            {
                var mode = entry.Key;
                var comboBox1 = control;
                var action = MyAPIGateway.TerminalControls.CreateAction<IMyShipWelder>(string.Format("{0}_On", ((SearchModes)mode).ToString()));
                action.Name = new StringBuilder(string.Format("{0} On", entry.Value));
                action.Icon = @"Textures\GUI\Icons\Actions\SwitchOn.dds";
                action.Enabled = isBaRSystem;
                action.Action = (block) =>
                {
                    comboBox1.Setter(block, mode);
                };
                action.ValidForGroups = true;
                MyAPIGateway.TerminalControls.AddAction<IMyShipWelder>(action);
            }

            return control;
        }

        public static IMyTerminalControlCombobox CreateWorkMode(bool onlyOneAllowed, Func<IMyTerminalBlock, bool> isReadonly, Func<IMyTerminalBlock, bool> isBaRSystem)
        {
            var isEnabled = onlyOneAllowed ? isReadonly : isBaRSystem;

            var control = Create(
                // Id:
                "WorkMode",

                // Texts
                Texts.WorkMode,
                Texts.WorkMode_Tooltip,

                // Enabled:
                isEnabled,

                // ComboboxContent:
                (list) =>
                {
                    if (Mod.Settings.Welder.AllowedWorkModes.HasFlag(WorkModes.WeldBeforeGrind))
                        list.Add(new MyTerminalControlComboBoxItem() { Key = (long)WorkModes.WeldBeforeGrind, Value = Texts.WorkMode_WeldB4Grind });
                    if (Mod.Settings.Welder.AllowedWorkModes.HasFlag(WorkModes.GrindBeforeWeld))
                        list.Add(new MyTerminalControlComboBoxItem() { Key = (long)WorkModes.GrindBeforeWeld, Value = Texts.WorkMode_GrindB4Weld });
                    if (Mod.Settings.Welder.AllowedWorkModes.HasFlag(WorkModes.GrindIfWeldGetStuck))
                        list.Add(new MyTerminalControlComboBoxItem() { Key = (long)WorkModes.GrindIfWeldGetStuck, Value = Texts.WorkMode_GrindIfWeldStuck });
                    if (Mod.Settings.Welder.AllowedWorkModes.HasFlag(WorkModes.WeldOnly))
                        list.Add(new MyTerminalControlComboBoxItem() { Key = (long)WorkModes.WeldOnly, Value = Texts.WorkMode_WeldOnly });
                    if (Mod.Settings.Welder.AllowedWorkModes.HasFlag(WorkModes.GrindOnly))
                        list.Add(new MyTerminalControlComboBoxItem() { Key = (long)WorkModes.GrindOnly, Value = Texts.WorkMode_GrindOnly });
                },

                // Getter:
                (block) =>
                {
                    var system = NanobotTerminal.GetSystem(block);
                    if (system == null) return 0;
                    else return (long)system.Settings.WorkMode;
                },

                // Setter:
                (block, value) =>
                {
                    var system = NanobotTerminal.GetSystem(block);
                    if (system != null)
                    {
                        if (Mod.Settings.Welder.AllowedWorkModes.HasFlag((WorkModes)value))
                        {
                            system.Settings.WorkMode = (WorkModes)value;
                        }
                    }
                },

                // Multiple blocks support.
                true
            );

            // Allow switch work mode by Buttonpanel
            var list1 = new List<MyTerminalControlComboBoxItem>();
            control.ComboBoxContent(list1);
            foreach (var entry in list1)
            {
                var mode = entry.Key;
                var comboBox1 = control;
                var action = MyAPIGateway.TerminalControls.CreateAction<IMyShipWelder>(string.Format("{0}_On", ((WorkModes)mode).ToString()));
                action.Name = new StringBuilder(string.Format("{0} On", entry.Value));
                action.Icon = @"Textures\GUI\Icons\Actions\SwitchOn.dds";
                action.Enabled = isBaRSystem;
                action.Action = (block) =>
                {
                    comboBox1.Setter(block, mode);
                };
                action.ValidForGroups = true;
                MyAPIGateway.TerminalControls.AddAction<IMyShipWelder>(action);
            }

            return control;
        }
    }
}