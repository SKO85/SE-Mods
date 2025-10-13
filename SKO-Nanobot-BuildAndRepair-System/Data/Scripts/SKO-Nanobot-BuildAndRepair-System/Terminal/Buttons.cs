using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces.Terminal;
using SKONanobotBuildAndRepairSystem.Localization;
using System;
using VRage.Utils;

namespace SKONanobotBuildAndRepairSystem.Terminal
{
    public static class Buttons
    {
        private static IMyTerminalControlButton Create(
            string id,
            MyStringId title,
            Func<IMyTerminalBlock, bool> isVisible,
            Func<IMyTerminalBlock, bool> isEnabled,
            Action<IMyTerminalBlock> action,
            bool supportsMultipleBlocks)
        {
            var control = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlButton, IMyShipWelder>(id);

            control.Title = title;
            control.Visible = isVisible;
            control.Enabled = isEnabled;
            control.Action = action;
            control.SupportsMultipleBlocks = supportsMultipleBlocks;

            NanobotTerminal.CustomControls.Add(control);
            return control;
        }

        public static IMyTerminalControlButton CreateIgnoreColorPickCurrent(Func<IMyTerminalBlock, bool> colorPickerEnabled, Func<IMyTerminalBlock, bool> isWeldingAllowed)
        {
            var control = Create(
                // Id:
                "IgnoreColorPickCurrent",

                // Texts:
                Texts.Color_PickCurrentColor,

                // Visbile / Enabled:
                isWeldingAllowed,
                colorPickerEnabled,

                // Action:
                (block) =>
                {
                    var system = NanobotTerminal.GetSystem(block);
                    if (system != null && MyAPIGateway.Session.LocalHumanPlayer != null)
                    {
                        system.Settings.IgnoreColor = MyAPIGateway.Session.LocalHumanPlayer.SelectedBuildColor;
                        NanobotTerminal.UpdateVisual(NanobotTerminal._IgnoreColorHueSlider);
                        NanobotTerminal.UpdateVisual(NanobotTerminal._IgnoreColorSaturationSlider);
                        NanobotTerminal.UpdateVisual(NanobotTerminal._IgnoreColorValueSlider);
                    }
                },

                // Multiple blocks support.
                true
            );

            return control;
        }

        public static IMyTerminalControlButton CreateIgnoreColorSetAsCurrent(Func<IMyTerminalBlock, bool> colorPickerEnabled, Func<IMyTerminalBlock, bool> isWeldingAllowed)
        {
            var control = Create(
                // Id:
                "IgnoreColorSetAsCurrent",

                // Texts:
                Texts.Color_SetCurrentColor,

                // Visbile / Enabled:
                isWeldingAllowed,
                colorPickerEnabled,

                // Action:
                (block) =>
                {
                    var system = NanobotTerminal.GetSystem(block);
                    if (system != null && MyAPIGateway.Session.LocalHumanPlayer != null)
                    {
                        MyAPIGateway.Session.LocalHumanPlayer.SelectedBuildColor = system.Settings.IgnoreColor;
                    }
                },

                // Multiple blocks support.
                true
            );

            return control;
        }

        public static IMyTerminalControlButton CreateWeldPriorityUp(Func<IMyTerminalBlock, bool> isWeldingAllowed)
        {
            Func<IMyTerminalBlock, bool> isEnabled = (block) =>
            {
                var system = NanobotTerminal.GetSystem(block);
                return system != null && system.BlockWeldPriority != null && system.BlockWeldPriority.Selected != null && isWeldingAllowed(block) && !Mod.Settings.Welder.PriorityFixed;
            };

            var control = Create(
                // Id:
                "WeldPriorityUp",

                // Texts:
                Texts.Priority_Up,

                // Visbile / Enabled:
                isWeldingAllowed,
                isEnabled,

                // Action:
                (block) =>
                {
                    var system = NanobotTerminal.GetSystem(block);
                    if (system != null && !Mod.Settings.Welder.PriorityFixed)
                    {
                        system.BlockWeldPriority.MoveSelectedUp();
                        system.Settings.WeldPriority = system.BlockWeldPriority.GetEntries();
                        NanobotTerminal.UpdateVisual(NanobotTerminal._WeldPriorityListBox);
                    }
                },

                // Multiple blocks support.
                true
            );

            return control;
        }

        public static IMyTerminalControlButton CreateWeldPriorityDown(Func<IMyTerminalBlock, bool> isWeldingAllowed)
        {
            Func<IMyTerminalBlock, bool> isEnabled = (block) =>
            {
                var system = NanobotTerminal.GetSystem(block);
                return system != null && system.BlockWeldPriority != null && system.BlockWeldPriority.Selected != null && isWeldingAllowed(block) && !Mod.Settings.Welder.PriorityFixed;
            };

            var control = Create(
                // Id:
                "WeldPriorityDown",

                // Texts:
                Texts.Priority_Down,

                // Visbile / Enabled:
                isWeldingAllowed,
                isEnabled,

                // Action:
                (block) =>
                {
                    var system = NanobotTerminal.GetSystem(block);
                    if (system != null && !Mod.Settings.Welder.PriorityFixed)
                    {
                        system.BlockWeldPriority.MoveSelectedDown();
                        system.Settings.WeldPriority = system.BlockWeldPriority.GetEntries();
                        NanobotTerminal.UpdateVisual(NanobotTerminal._WeldPriorityListBox);
                    }
                },

                // Multiple blocks support.
                true
            );

            return control;
        }

        public static IMyTerminalControlButton CreateGrindColorPickCurrent(Func<IMyTerminalBlock, bool> colorPickerEnabled, Func<IMyTerminalBlock, bool> isGrindingAllowed)
        {
            var control = Create(
                // Id:
                "GrindColorPickCurrent",

                // Texts:
                Texts.Color_PickCurrentColor,

                // Visbile / Enabled:
                isGrindingAllowed,
                colorPickerEnabled,

                // Action:
                (block) =>
                {
                    var system = NanobotTerminal.GetSystem(block);
                    if (system != null && MyAPIGateway.Session.LocalHumanPlayer != null)
                    {
                        system.Settings.GrindColor = MyAPIGateway.Session.LocalHumanPlayer.SelectedBuildColor;
                        NanobotTerminal.UpdateVisual(NanobotTerminal._GrindColorHueSlider);
                        NanobotTerminal.UpdateVisual(NanobotTerminal._GrindColorSaturationSlider);
                        NanobotTerminal.UpdateVisual(NanobotTerminal._GrindColorValueSlider);
                    }
                },

                // Multiple blocks support.
                true
            );

            return control;
        }

        public static IMyTerminalControlButton CreateGrindColorSetAsCurrent(Func<IMyTerminalBlock, bool> colorPickerEnabled, Func<IMyTerminalBlock, bool> isGrindingAllowed)
        {
            var control = Create(
                // Id:
                "GrindColorSetAsCurrent",

                // Texts:
                Texts.Color_SetCurrentColor,

                // Visbile / Enabled:
                isGrindingAllowed,
                colorPickerEnabled,

                // Action:
                (block) =>
                {
                    var system = NanobotTerminal.GetSystem(block);
                    if (system != null && MyAPIGateway.Session.LocalHumanPlayer != null)
                    {
                        MyAPIGateway.Session.LocalHumanPlayer.SelectedBuildColor = system.Settings.GrindColor;
                    }
                },

                // Multiple blocks support.
                true
            );

            return control;
        }

        public static IMyTerminalControlButton CreateGrindPriorityUp(Func<IMyTerminalBlock, bool> isGrindingAllowed)
        {
            var control = Create(
                // Id:
                "GrindPriorityUp",

                // Texts:
                Texts.Priority_Up,

                // Visbile:
                isGrindingAllowed,

                // Enabled:
                (block) =>
                {
                    var system = NanobotTerminal.GetSystem(block);
                    return system != null && system.BlockGrindPriority != null && system.BlockGrindPriority.Selected != null && isGrindingAllowed(block) && !Mod.Settings.Welder.PriorityFixed;
                },

                // Action:
                (block) =>
                {
                    var system = NanobotTerminal.GetSystem(block);
                    if (system != null && !Mod.Settings.Welder.PriorityFixed)
                    {
                        system.BlockGrindPriority.MoveSelectedUp();
                        system.Settings.GrindPriority = system.BlockGrindPriority.GetEntries();
                        NanobotTerminal.UpdateVisual(NanobotTerminal._GrindPriorityListBox);
                    }
                },

                // Multiple blocks support.
                true
            );

            return control;
        }

        public static IMyTerminalControlButton CreateGrindPriorityDown(Func<IMyTerminalBlock, bool> isGrindingAllowed)
        {
            var control = Create(
                // Id:
                "GrindPriorityDown",

                // Texts:
                Texts.Priority_Down,

                // Visbile:
                isGrindingAllowed,

                // Enabled:
                (block) =>
                {
                    var system = NanobotTerminal.GetSystem(block);
                    return system != null && system.BlockGrindPriority != null && system.BlockGrindPriority.Selected != null && isGrindingAllowed(block) && !Mod.Settings.Welder.PriorityFixed;
                },

                // Action:
                (block) =>
                {
                    var system = NanobotTerminal.GetSystem(block);
                    if (system != null && !Mod.Settings.Welder.PriorityFixed)
                    {
                        system.BlockGrindPriority.MoveSelectedDown();
                        system.Settings.GrindPriority = system.BlockGrindPriority.GetEntries();
                        NanobotTerminal.UpdateVisual(NanobotTerminal._GrindPriorityListBox);
                    }
                },

                // Multiple blocks support.
                true
            );

            return control;
        }

        public static IMyTerminalControlButton CreateCollectPriorityUp(Func<IMyTerminalBlock, bool> isChangeCollectPriorityPossible)
        {
            var control = Create(
                // Id:
                "CollectPriorityUp",

                // Texts:
                Texts.Priority_Up,

                // Visbile:
                (block) => { return true; },

                // Enabled:
                isChangeCollectPriorityPossible,

                // Action:
                (block) =>
                {
                    var system = NanobotTerminal.GetSystem(block);
                    if (system != null && !Mod.Settings.Welder.CollectPriorityFixed)
                    {
                        system.ComponentCollectPriority.MoveSelectedUp();
                        system.Settings.ComponentCollectPriority = system.ComponentCollectPriority.GetEntries();
                        NanobotTerminal.UpdateVisual(NanobotTerminal._ComponentCollectPriorityListBox);
                    }
                },

                // Multiple blocks support.
                true
            );

            return control;
        }

        public static IMyTerminalControlButton CreateCollectPriorityDown(Func<IMyTerminalBlock, bool> isChangeCollectPriorityPossible)
        {
            var control = Create(
                // Id:
                "CollectPriorityDown",

                // Texts:
                Texts.Priority_Down,

                // Visbile:
                (block) => { return true; },

                // Enabled:
                isChangeCollectPriorityPossible,

                // Action:
                (block) =>
                {
                    var system = NanobotTerminal.GetSystem(block);
                    if (system != null && !Mod.Settings.Welder.CollectPriorityFixed)
                    {
                        system.ComponentCollectPriority.MoveSelectedDown();
                        system.Settings.ComponentCollectPriority = system.ComponentCollectPriority.GetEntries();
                        NanobotTerminal.UpdateVisual(NanobotTerminal._ComponentCollectPriorityListBox);
                    }
                },

                // Multiple blocks support.
                true
            );

            return control;
        }
    }
}