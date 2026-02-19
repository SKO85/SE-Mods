using Sandbox.Game.Localization;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces.Terminal;
using SKONanobotBuildAndRepairSystem.Localization;
using SKONanobotBuildAndRepairSystem.Models;
using System;
using System.Text;
using VRage.Utils;

namespace SKONanobotBuildAndRepairSystem.Terminal
{
    public static class OnOffSwitches
    {
        private static IMyTerminalControlOnOffSwitch Create(
            string id,
            MyStringId title,
            MyStringId tooltip,
            MyStringId onText,
            MyStringId offText,
            Func<IMyTerminalBlock, bool> isVisible,
            Func<IMyTerminalBlock, bool> isEnabled,
            Func<IMyTerminalBlock, bool> getter,
            Action<IMyTerminalBlock, bool> setter,
            bool supportsMultipleBlocks)
        {
            var control = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlOnOffSwitch, IMyShipWelder>(id);

            control.Title = title;
            control.Tooltip = tooltip;
            control.OnText = onText;
            control.OffText = offText;

            control.Visible = isVisible;
            control.Enabled = isEnabled;
            control.Getter = getter;
            control.Setter = setter;

            control.SupportsMultipleBlocks = supportsMultipleBlocks;

            NanobotTerminal.CustomControls.Add(control);
            CreateOnOffSwitchAction(id, control);

            return control;
        }

        private static void CreateOnOffSwitchAction(string name, IMyTerminalControlOnOffSwitch onoffSwitch)
        {
            var action = MyAPIGateway.TerminalControls.CreateAction<IMyShipWelder>(string.Format("{0}_OnOff", name));
            action.Name = new StringBuilder(string.Format("{0} {1}/{2}", name, onoffSwitch.OnText, onoffSwitch.OffText));
            action.Icon = @"Textures\GUI\Icons\Actions\Toggle.dds";
            action.Enabled = onoffSwitch.Enabled;
            action.Action = (block) =>
            {
                onoffSwitch.Setter(block, !onoffSwitch.Getter(block));
            };
            action.ValidForGroups = onoffSwitch.SupportsMultipleBlocks;
            MyAPIGateway.TerminalControls.AddAction<IMyShipWelder>(action);

            action = MyAPIGateway.TerminalControls.CreateAction<IMyShipWelder>(string.Format("{0}_On", name));
            action.Name = new StringBuilder(string.Format("{0} {1}", name, onoffSwitch.OnText));
            action.Icon = @"Textures\GUI\Icons\Actions\SwitchOn.dds";
            action.Enabled = onoffSwitch.Enabled;
            action.Action = (block) =>
            {
                onoffSwitch.Setter(block, true);
            };
            action.ValidForGroups = onoffSwitch.SupportsMultipleBlocks;
            MyAPIGateway.TerminalControls.AddAction<IMyShipWelder>(action);

            action = MyAPIGateway.TerminalControls.CreateAction<IMyShipWelder>(string.Format("{0}_Off", name));
            action.Name = new StringBuilder(string.Format("{0} {1}", name, onoffSwitch.OffText));
            action.Icon = @"Textures\GUI\Icons\Actions\SwitchOff.dds";
            action.Enabled = onoffSwitch.Enabled;
            action.Action = (block) =>
            {
                onoffSwitch.Setter(block, false);
            };
            action.ValidForGroups = onoffSwitch.SupportsMultipleBlocks;
            MyAPIGateway.TerminalControls.AddAction<IMyShipWelder>(action);
        }

        public static IMyTerminalControlOnOffSwitch CreateGrindJanitorOptionDisableOnly(bool grindingAllowed, Func<IMyTerminalBlock, bool> isJanitorAllowed, Func<IMyTerminalBlock, bool> isReadonly, Func<IMyTerminalBlock, bool> isBaRSystem)
        {
            var isEnabled = Mod.Settings.Welder.UseGrindJanitorFixed || !grindingAllowed ? isReadonly : isBaRSystem;

            var control = Create(
                // Id:
                "GrindJanitorOptionDisableOnly",

                // Texts
                Texts.GrindJanitorDisableOnly,
                Texts.GrindJanitorDisableOnly_Tooltip,
                MySpaceTexts.SwitchText_On,
                MySpaceTexts.SwitchText_Off,

                // Visible:
                isJanitorAllowed,

                // Enabled:
                isEnabled,

                // Getter:
                (block) =>
                {
                    var system = NanobotTerminal.GetSystem(block);
                    return system != null ? (system.Settings.GrindJanitorOptions & AutoGrindOptions.DisableOnly) != 0 : false;
                },

                // Setter:
                (block, value) =>
                {
                    var system = NanobotTerminal.GetSystem(block);
                    if (system != null && !Mod.Settings.Welder.UseGrindJanitorFixed && isJanitorAllowed(block))
                    {
                        //Only one option (HackOnly or DisableOnly) at a time is allowed
                        if (value)
                        {
                            system.Settings.GrindJanitorOptions = (system.Settings.GrindJanitorOptions & ~AutoGrindOptions.HackOnly) | AutoGrindOptions.DisableOnly;
                            foreach (var ctrl in NanobotTerminal.CustomControls)
                            {
                                if (ctrl.Id.Contains("GrindJanitorOption")) ctrl.UpdateVisual();
                            }
                        }
                        else
                        {
                            system.Settings.GrindJanitorOptions = (system.Settings.GrindJanitorOptions & ~AutoGrindOptions.DisableOnly);
                        }
                    }
                },

                // Multiple blocks support.
                true
            );

            return control;
        }

        public static IMyTerminalControlOnOffSwitch CreateGrindJanitorOptionHackOnly(bool grindingAllowed, Func<IMyTerminalBlock, bool> isJanitorAllowed, Func<IMyTerminalBlock, bool> isReadonly, Func<IMyTerminalBlock, bool> isBaRSystem)
        {
            var isEnabled = Mod.Settings.Welder.UseGrindJanitorFixed || !grindingAllowed ? isReadonly : isBaRSystem;

            var control = Create(
                // Id:
                "GrindJanitorOptionHackOnly",

                // Texts
                Texts.GrindJanitorHackOnly,
                Texts.GrindJanitorHackOnly_Tooltip,
                MySpaceTexts.SwitchText_On,
                MySpaceTexts.SwitchText_Off,

                // Visible:
                isJanitorAllowed,

                // Enabled:
                isEnabled,

                // Getter:
                (block) =>
                {
                    var system = NanobotTerminal.GetSystem(block);
                    return system != null ? (system.Settings.GrindJanitorOptions & AutoGrindOptions.HackOnly) != 0 : false;
                },

                // Setter:
                (block, value) =>
                {
                    var system = NanobotTerminal.GetSystem(block);
                    if (system != null && !Mod.Settings.Welder.UseGrindJanitorFixed && isJanitorAllowed(block))
                    {
                        //Only one option (HackOnly or DisableOnly) at a time is allowed
                        if (value)
                        {
                            system.Settings.GrindJanitorOptions = (system.Settings.GrindJanitorOptions & ~AutoGrindOptions.DisableOnly) | AutoGrindOptions.HackOnly;
                            foreach (var ctrl in NanobotTerminal.CustomControls)
                            {
                                if (ctrl.Id.Contains("GrindJanitorOption")) ctrl.UpdateVisual();
                            }
                        }
                        else
                        {
                            system.Settings.GrindJanitorOptions = (system.Settings.GrindJanitorOptions & ~AutoGrindOptions.HackOnly);
                        }
                    }
                },

                // Multiple blocks support.
                true
            );

            return control;
        }

        /// <summary>
        /// Creates the control for Collect only if Idle option.
        /// </summary>
        /// <param name="isCollectPossible"></param>
        /// <param name="isReadonly"></param>
        /// <returns></returns>
        public static IMyTerminalControlOnOffSwitch CreateCollectIfIdle(Func<IMyTerminalBlock, bool> isCollectPossible, Func<IMyTerminalBlock, bool> isReadonly)
        {
            Func<IMyTerminalBlock, bool> isVisible = (block) => { return true; };
            var isEnabled = Mod.Settings.Welder.CollectIfIdleFixed ? isReadonly : isCollectPossible;

            var control = Create(
                "CollectIfIdle",
                Texts.CollectOnlyIfIdle,
                Texts.CollectOnlyIfIdle_Tooltip,
                MySpaceTexts.SwitchText_On,
                MySpaceTexts.SwitchText_Off,
                (_) => { return true; },
                isEnabled,

                (block) =>
                {
                    var system = NanobotTerminal.GetSystem(block);
                    return system != null ? ((system.Settings.Flags & SyncBlockSettings.Settings.ComponentCollectIfIdle) != 0) : false;
                },

                (block, value) =>
                {
                    var system = NanobotTerminal.GetSystem(block);
                    if (system != null && !Mod.Settings.Welder.CollectIfIdleFixed)
                    {
                        system.Settings.Flags = (system.Settings.Flags & ~SyncBlockSettings.Settings.ComponentCollectIfIdle) | (value ? SyncBlockSettings.Settings.ComponentCollectIfIdle : 0);
                    }
                },
                true
            );

            return control;
        }

        /// <summary>
        /// Creates the toggle for the Push Ingot Ore Immediately option.
        /// </summary>
        /// <param name="isReadonly"></param>
        /// <param name="isBaRSystem"></param>
        /// <returns></returns>
        public static IMyTerminalControlOnOffSwitch CreatePushIngotOreImmediately(Func<IMyTerminalBlock, bool> isReadonly, Func<IMyTerminalBlock, bool> isBaRSystem)
        {
            Func<IMyTerminalBlock, bool> isVisible = (block) => { return true; };
            var isEnabled = Mod.Settings.Welder.PushIngotOreImmediatelyFixed ? isReadonly : isBaRSystem;

            var control = Create(
                // Id:
                "PushIngotOreImmediately",

                // Texts
                Texts.CollectPushOre,
                Texts.CollectPushOre_Tooltip,
                MySpaceTexts.SwitchText_On,
                MySpaceTexts.SwitchText_Off,

                // Visible:
                (_) => { return true; },

                // Enabled:
                isEnabled,

                // Getter:
                (block) =>
                {
                    var system = NanobotTerminal.GetSystem(block);
                    return system != null ? ((system.Settings.Flags & SyncBlockSettings.Settings.PushIngotOreImmediately) != 0) : false;
                },

                // Setter:
                (block, value) =>
                {
                    var system = NanobotTerminal.GetSystem(block);
                    if (system != null && !Mod.Settings.Welder.PushIngotOreImmediatelyFixed)
                    {
                        system.Settings.Flags = (system.Settings.Flags & ~SyncBlockSettings.Settings.PushIngotOreImmediately) | (value ? SyncBlockSettings.Settings.PushIngotOreImmediately : 0);
                    }
                },

                // Multiple blocks support.
                true
            );

            return control;
        }

        public static IMyTerminalControlOnOffSwitch CreatePushItemsImmediately(Func<IMyTerminalBlock, bool> isReadonly, Func<IMyTerminalBlock, bool> isBaRSystem)
        {
            Func<IMyTerminalBlock, bool> isVisible = (block) => { return true; };
            var isEnabled = Mod.Settings.Welder.PushItemsImmediatelyFixed ? isReadonly : isBaRSystem;

            var control = Create(
                // Id:
                "PushItemsImmediately",

                // Texts
                Texts.CollectPushItems,
                Texts.CollectPushItems_Tooltip,
                MySpaceTexts.SwitchText_On,
                MySpaceTexts.SwitchText_Off,

                // Visible:
                isVisible,

                // Enabled:
                isEnabled,

                // Getter:
                (block) =>
                {
                    var system = NanobotTerminal.GetSystem(block);
                    return system != null ? ((system.Settings.Flags & SyncBlockSettings.Settings.PushItemsImmediately) != 0) : false;
                },

                // Setter:
                (block, value) =>
                {
                    var system = NanobotTerminal.GetSystem(block);
                    if (system != null && !Mod.Settings.Welder.PushItemsImmediatelyFixed)
                    {
                        system.Settings.Flags = (system.Settings.Flags & ~SyncBlockSettings.Settings.PushItemsImmediately) | (value ? SyncBlockSettings.Settings.PushItemsImmediately : 0);
                    }
                },

                // Multiple blocks support.
                true
            );

            return control;
        }

        public static IMyTerminalControlOnOffSwitch CreatePushComponentImmediately(Func<IMyTerminalBlock, bool> isReadonly, Func<IMyTerminalBlock, bool> isBaRSystem)
        {
            Func<IMyTerminalBlock, bool> isVisible = (block) => { return true; };
            var isEnabled = Mod.Settings.Welder.PushComponentImmediatelyFixed ? isReadonly : isBaRSystem;

            var control = Create(
                // Id:
                "PushComponentImmediately",

                // Texts
                Texts.CollectPushComp,
                Texts.CollectPushComp_Tooltip,
                MySpaceTexts.SwitchText_On,
                MySpaceTexts.SwitchText_Off,

                // Visible:
                isVisible,

                // Enabled:
                isEnabled,

                // Getter:
                (block) =>
                {
                    var system = NanobotTerminal.GetSystem(block);
                    return system != null ? ((system.Settings.Flags & SyncBlockSettings.Settings.PushComponentImmediately) != 0) : false;
                },

                // Setter:
                (block, value) =>
                {
                    var system = NanobotTerminal.GetSystem(block);
                    if (system != null && !Mod.Settings.Welder.PushComponentImmediatelyFixed)
                    {
                        system.Settings.Flags = (system.Settings.Flags & ~SyncBlockSettings.Settings.PushComponentImmediately) | (value ? SyncBlockSettings.Settings.PushComponentImmediately : 0);
                    }
                },

                // Multiple blocks support.
                true
            );

            return control;
        }

        public static IMyTerminalControlOnOffSwitch CreateShowArea(Func<IMyTerminalBlock, bool> isReadonly, Func<IMyTerminalBlock, bool> isBaRSystem)
        {
            Func<IMyTerminalBlock, bool> isVisible = (block) => { return true; };
            var isEnabled = Mod.Settings.Welder.ShowAreaFixed ? isReadonly : isBaRSystem;

            var control = Create(
                // Id:
                "ShowArea",

                // Texts
                Texts.AreaShow,
                Texts.AreaShow_Tooltip,
                MySpaceTexts.SwitchText_On,
                MySpaceTexts.SwitchText_Off,

                // Visible:
                isVisible,

                // Enabled:
                isEnabled,

                // Getter:
                (block) =>
                {
                    var system = NanobotTerminal.GetSystem(block);
                    if (system != null)
                    {
                        return system != null ? ((system.Settings.Flags & SyncBlockSettings.Settings.ShowArea) != 0) : false;
                    }

                    return false;
                },

                // Setter:
                (block, value) =>
                {
                    var system = NanobotTerminal.GetSystem(block);
                    if (system != null && !Mod.Settings.Welder.ShowAreaFixed)
                    {
                        system.Settings.Flags = (system.Settings.Flags & ~SyncBlockSettings.Settings.ShowArea) | (value ? SyncBlockSettings.Settings.ShowArea : 0);
                    }
                },

                // Multiple blocks support.
                true
            );

            return control;
        }

        public static IMyTerminalControlOnOffSwitch CreateUseIgnoreColor(bool weldingAllowed, Func<IMyTerminalBlock, bool> isWeldingAllowed, Func<IMyTerminalBlock, bool> isReadonly, Func<IMyTerminalBlock, bool> isBaRSystem)
        {
            var isEnabled = Mod.Settings.Welder.UseIgnoreColorFixed || !weldingAllowed ? isReadonly : isBaRSystem;

            var control = Create(
                // Id:
                "UseIgnoreColor",

                // Texts
                Texts.WeldUseIgnoreColor,
                Texts.WeldUseIgnoreColor_Tooltip,
                MySpaceTexts.SwitchText_On,
                MySpaceTexts.SwitchText_Off,

                // Visible:
                isWeldingAllowed,

                // Enabled:
                isEnabled,

                // Getter:
                (block) =>
                {
                    var system = NanobotTerminal.GetSystem(block);
                    return system != null ? ((system.Settings.Flags & SyncBlockSettings.Settings.UseIgnoreColor) != 0) : false;
                },

                // Setter:
                (block, value) =>
                {
                    var system = NanobotTerminal.GetSystem(block);
                    if (system != null && !Mod.Settings.Welder.UseIgnoreColorFixed && isWeldingAllowed(block))
                    {
                        system.Settings.Flags = (system.Settings.Flags & ~SyncBlockSettings.Settings.UseIgnoreColor) | (value ? SyncBlockSettings.Settings.UseIgnoreColor : 0);
                        foreach (var ctrl in NanobotTerminal.CustomControls)
                        {
                            if (ctrl.Id.Contains("IgnoreColor")) ctrl.UpdateVisual();
                        }
                    }
                },

                // Multiple blocks support.
                true
            );

            return control;
        }

        public static IMyTerminalControlOnOffSwitch CreateAllowBuild(bool weldingAllowed, Func<IMyTerminalBlock, bool> isWeldingAllowed, Func<IMyTerminalBlock, bool> isReadonly, Func<IMyTerminalBlock, bool> isBaRSystem)
        {
            var isEnabled = Mod.Settings.Welder.AllowBuildFixed || !weldingAllowed ? isReadonly : isBaRSystem;

            var control = Create(
                // Id:
                "AllowBuild",

                // Texts
                Texts.WeldBuildNew,
                Texts.WeldBuildNew_Tooltip,
                MySpaceTexts.SwitchText_On,
                MySpaceTexts.SwitchText_Off,

                // Visible:
                isWeldingAllowed,

                // Enabled:
                isEnabled,

                // Getter:
                (block) =>
                {
                    var system = NanobotTerminal.GetSystem(block);
                    return system != null ? ((system.Settings.Flags & SyncBlockSettings.Settings.AllowBuild) != 0) : false;
                },

                // Setter:
                (block, value) =>
                {
                    var system = NanobotTerminal.GetSystem(block);
                    if (system != null && !Mod.Settings.Welder.AllowBuildFixed && isWeldingAllowed(block))
                    {
                        system.Settings.Flags = (system.Settings.Flags & ~SyncBlockSettings.Settings.AllowBuild) | (value ? SyncBlockSettings.Settings.AllowBuild : 0);
                    }
                },

                // Multiple blocks support.
                true
            );

            return control;
        }

        public static IMyTerminalControlOnOffSwitch CreateWeldOptionFunctionalOnly(bool weldingAllowed, Func<IMyTerminalBlock, bool> isWeldingAllowed, Func<IMyTerminalBlock, bool> isReadonly, Func<IMyTerminalBlock, bool> isBaRSystem)
        {
            var isEnabled = !weldingAllowed ? isReadonly : isBaRSystem;

            var control = Create(
                // Id:
                "WeldOptionFunctionalOnly",

                // Texts
                Texts.WeldToFuncOnly,
                Texts.WeldToFuncOnly_Tooltip,
                MySpaceTexts.SwitchText_On,
                MySpaceTexts.SwitchText_Off,

                // Visible:
                isWeldingAllowed,

                // Enabled:
                isEnabled,

                // Getter:
                (block) =>
                {
                    var system = NanobotTerminal.GetSystem(block);
                    return system != null ? (system.Settings.WeldOptions & AutoWeldOptions.FunctionalOnly) != 0 : false;
                },

                // Setter:
                (block, value) =>
                {
                    var system = NanobotTerminal.GetSystem(block);
                    if (system != null && isWeldingAllowed(block))
                    {
                        if (value)
                        {
                            system.Settings.WeldOptions = system.Settings.WeldOptions | AutoWeldOptions.FunctionalOnly;
                            foreach (var ctrl in NanobotTerminal.CustomControls)
                            {
                                if (ctrl.Id.Contains("WeldOption")) ctrl.UpdateVisual();
                            }
                        }
                        else
                        {
                            system.Settings.WeldOptions = (system.Settings.WeldOptions & ~AutoWeldOptions.FunctionalOnly);
                        }
                    }
                },

                // Multiple blocks support.
                true
            );

            return control;
        }

        public static IMyTerminalControlOnOffSwitch CreateWeldPriority(Func<IMyTerminalBlock, bool> isWeldingAllowed, Func<IMyTerminalBlock, bool> isReadonly, Func<IMyTerminalBlock, bool> isBaRSystem)
        {
            Func<IMyTerminalBlock, bool> isEnabled = (block) =>
            {
                var system = NanobotTerminal.GetSystem(block);
                return system != null && system.BlockWeldPriority != null && system.BlockWeldPriority.Selected != null && isWeldingAllowed(block) && !Mod.Settings.Welder.PriorityFixed;
            };

            var control = Create(
                // Id:
                "WeldPriority",

                // Texts
                Texts.WeldPriority,
                Texts.WeldPriority_Tooltip,
                Texts.Priority_Enable,
                Texts.Priority_Disable,

                // Visible:
                isWeldingAllowed,

                // Enabled:
                isEnabled,

                // Getter:
                (block) =>
                {
                    var system = NanobotTerminal.GetSystem(block);
                    return system != null && system.BlockWeldPriority != null && system.BlockWeldPriority.Selected != null ? system.BlockWeldPriority.GetEnabled(system.BlockWeldPriority.Selected.Key) : false;
                },

                // Setter:
                (block, value) =>
                {
                    var system = NanobotTerminal.GetSystem(block);
                    if (system != null && system.BlockWeldPriority != null && system.BlockWeldPriority.Selected != null && isWeldingAllowed(block) && !Mod.Settings.Welder.PriorityFixed)
                    {
                        system.BlockWeldPriority.SetEnabled(system.BlockWeldPriority.Selected.Key, value);
                        system.Settings.WeldPriority = system.BlockWeldPriority.GetEntries();
                        NanobotTerminal.UpdateVisual(NanobotTerminal._WeldPriorityListBox);
                    }
                },

                // Multiple blocks support.
                true
            );

            return control;
        }

        public static IMyTerminalControlOnOffSwitch CreateUseGrindColor(bool grindingAllowed, Func<IMyTerminalBlock, bool> isGrindingAllowed, Func<IMyTerminalBlock, bool> isReadonly, Func<IMyTerminalBlock, bool> isBaRSystem)
        {
            var isEnabled = Mod.Settings.Welder.UseGrindColorFixed || !grindingAllowed ? isReadonly : isBaRSystem;

            var control = Create(
                // Id:
                "UseGrindColor",

                // Texts
                Texts.GrindUseGrindColor,
                Texts.GrindUseGrindColor_Tooltip,
                MySpaceTexts.SwitchText_On,
                MySpaceTexts.SwitchText_Off,

                // Visible:
                isGrindingAllowed,

                // Enabled:
                isEnabled,

                // Getter:
                (block) =>
                {
                    var system = NanobotTerminal.GetSystem(block);
                    return system != null ? ((system.Settings.Flags & SyncBlockSettings.Settings.UseGrindColor) != 0) : false;
                },

                // Setter:
                (block, value) =>
                {
                    var system = NanobotTerminal.GetSystem(block);
                    if (system != null && !Mod.Settings.Welder.UseGrindColorFixed && isGrindingAllowed(block))
                    {
                        system.Settings.Flags = (system.Settings.Flags & ~SyncBlockSettings.Settings.UseGrindColor) | (value ? SyncBlockSettings.Settings.UseGrindColor : 0);
                        foreach (var ctrl in NanobotTerminal.CustomControls)
                        {
                            if (ctrl.Id.Contains("GrindColor")) ctrl.UpdateVisual();
                        }
                    }
                },

                // Multiple blocks support.
                true
            );

            return control;
        }

        public static IMyTerminalControlOnOffSwitch CreateGrindJanitorEnemies(bool janitorAllowedEnemies, Func<IMyTerminalBlock, bool> isJanitorAllowedEnemies, Func<IMyTerminalBlock, bool> isReadonly, Func<IMyTerminalBlock, bool> isBaRSystem)
        {
            var isEnabled = Mod.Settings.Welder.UseGrindJanitorFixed || !janitorAllowedEnemies ? isReadonly : isBaRSystem;

            var control = Create(
                // Id:
                "GrindJanitorEnemies",

                // Texts
                Texts.GrindJanitorEnemy,
                Texts.GrindJanitorEnemy_Tooltip,
                MySpaceTexts.SwitchText_On,
                MySpaceTexts.SwitchText_Off,

                // Visible:
                isJanitorAllowedEnemies,

                // Enabled:
                isEnabled,

                // Getter:
                (block) =>
                {
                    var system = NanobotTerminal.GetSystem(block);
                    return system != null ? (system.Settings.UseGrindJanitorOn & AutoGrindRelation.Enemies) != 0 : false;
                },

                // Setter:
                (block, value) =>
                {
                    var system = NanobotTerminal.GetSystem(block);
                    if (system != null && !Mod.Settings.Welder.UseGrindJanitorFixed && isJanitorAllowedEnemies(block))
                    {
                        system.Settings.UseGrindJanitorOn = (system.Settings.UseGrindJanitorOn & ~AutoGrindRelation.Enemies) | (value ? AutoGrindRelation.Enemies : 0);
                    }
                },

                // Multiple blocks support.
                true
            );

            return control;
        }

        public static IMyTerminalControlOnOffSwitch CreateGrindJanitorNotOwned(bool janitorAllowedNoOwnership, Func<IMyTerminalBlock, bool> isJanitorAllowedNoOwnership, Func<IMyTerminalBlock, bool> isReadonly, Func<IMyTerminalBlock, bool> isBaRSystem)
        {
            var isEnabled = Mod.Settings.Welder.UseGrindJanitorFixed || !janitorAllowedNoOwnership ? isReadonly : isBaRSystem;

            var control = Create(
                // Id:
                "GrindJanitorNotOwned",

                // Texts
                Texts.GrindJanitorNotOwned,
                Texts.GrindJanitorNotOwned_Tooltip,
                MySpaceTexts.SwitchText_On,
                MySpaceTexts.SwitchText_Off,

                // Visible:
                isJanitorAllowedNoOwnership,

                // Enabled:
                isEnabled,

                // Getter:
                (block) =>
                {
                    var system = NanobotTerminal.GetSystem(block);
                    return system != null ? (system.Settings.UseGrindJanitorOn & AutoGrindRelation.NoOwnership) != 0 : false;
                },

                // Setter:
                (block, value) =>
                {
                    var system = NanobotTerminal.GetSystem(block);
                    if (system != null && !Mod.Settings.Welder.UseGrindJanitorFixed && isJanitorAllowedNoOwnership(block))
                    {
                        system.Settings.UseGrindJanitorOn = (system.Settings.UseGrindJanitorOn & ~AutoGrindRelation.NoOwnership) | (value ? AutoGrindRelation.NoOwnership : 0);
                    }
                },

                // Multiple blocks support.
                true
            );

            return control;
        }

        public static IMyTerminalControlOnOffSwitch CreateGrindJanitorNeutrals(bool janitorAllowedNeutral, Func<IMyTerminalBlock, bool> isJanitorAllowedNeutral, Func<IMyTerminalBlock, bool> isReadonly, Func<IMyTerminalBlock, bool> isBaRSystem)
        {
            var isEnabled = Mod.Settings.Welder.UseGrindJanitorFixed || !janitorAllowedNeutral ? isReadonly : isBaRSystem;

            var control = Create(
                // Id:
                "GrindJanitorNeutrals",

                // Texts
                Texts.GrindJanitorNeutrals,
                Texts.GrindJanitorNeutrals_Tooltip,
                MySpaceTexts.SwitchText_On,
                MySpaceTexts.SwitchText_Off,

                // Visible:
                isJanitorAllowedNeutral,

                // Enabled:
                isEnabled,

                // Getter:
                (block) =>
                {
                    var system = NanobotTerminal.GetSystem(block);
                    return system != null ? (system.Settings.UseGrindJanitorOn & AutoGrindRelation.Neutral) != 0 : false;
                },

                // Setter:
                (block, value) =>
                {
                    var system = NanobotTerminal.GetSystem(block);
                    if (system != null && !Mod.Settings.Welder.UseGrindJanitorFixed && isJanitorAllowedNeutral(block))
                    {
                        system.Settings.UseGrindJanitorOn = (system.Settings.UseGrindJanitorOn & ~AutoGrindRelation.Neutral) | (value ? AutoGrindRelation.Neutral : 0);
                    }
                },

                // Multiple blocks support.
                true
            );

            return control;
        }

        public static IMyTerminalControlOnOffSwitch CreateGrindPriority(Func<IMyTerminalBlock, bool> isGrindingAllowed, Func<IMyTerminalBlock, bool> isReadonly, Func<IMyTerminalBlock, bool> isBaRSystem)
        {
            var control = Create(
                // Id:
                "GrindPriority",

                // Texts
                Texts.GrindPriority,
                Texts.GrindPriority_Tooltip,
                Texts.Priority_Enable,
                Texts.Priority_Disable,

                // Visible:
                isGrindingAllowed,

                // Enabled:
                (block) =>
                {
                    var system = NanobotTerminal.GetSystem(block);
                    return system != null && system.BlockGrindPriority != null && system.BlockGrindPriority.Selected != null && isGrindingAllowed(block) && !Mod.Settings.Welder.PriorityFixed;
                },

                // Getter:
                (block) =>
                {
                    var system = NanobotTerminal.GetSystem(block);
                    return system != null && system.BlockGrindPriority != null && system.BlockGrindPriority.Selected != null ? system.BlockGrindPriority.GetEnabled(system.BlockGrindPriority.Selected.Key) : false;
                },

                // Setter:
                (block, value) =>
                {
                    var system = NanobotTerminal.GetSystem(block);
                    if (system != null && system.BlockGrindPriority != null && system.BlockGrindPriority.Selected != null && isGrindingAllowed(block) && !Mod.Settings.Welder.PriorityFixed)
                    {
                        system.BlockGrindPriority.SetEnabled(system.BlockGrindPriority.Selected.Key, value);
                        system.Settings.GrindPriority = system.BlockGrindPriority.GetEntries();
                        NanobotTerminal.UpdateVisual(NanobotTerminal._GrindPriorityListBox);
                    }
                },

                // Multiple blocks support.
                true
            );

            return control;
        }

        public static IMyTerminalControlOnOffSwitch CreateGrindIgnorePriorityOrder(bool grindingAllowed, Func<IMyTerminalBlock, bool> isGrindingAllowed, Func<IMyTerminalBlock, bool> isReadonly, Func<IMyTerminalBlock, bool> isBaRSystem)
        {
            var isEnabled = grindingAllowed ? isBaRSystem : isReadonly;

            var control = Create(
                // Id:
                "GrindIgnorePriorityOrder",

                // Texts
                Texts.GrindIgnorePriority,
                Texts.GrindIgnorePriority_Tooltip,
                MySpaceTexts.SwitchText_On,
                MySpaceTexts.SwitchText_Off,

                // Visible:
                isGrindingAllowed,

                // Enabled:
                isEnabled,

                // Getter:
                (block) =>
                {
                    var system = NanobotTerminal.GetSystem(block);
                    return system != null ? ((system.Settings.Flags & SyncBlockSettings.Settings.GrindIgnorePriorityOrder) != 0) : false;
                },

                // Setter:
                (block, value) =>
                {
                    var system = NanobotTerminal.GetSystem(block);
                    if (system != null && isGrindingAllowed(block))
                    {
                        system.Settings.Flags = (system.Settings.Flags & ~SyncBlockSettings.Settings.GrindIgnorePriorityOrder) | (value ? SyncBlockSettings.Settings.GrindIgnorePriorityOrder : 0);
                    }
                },

                // Multiple blocks support.
                true
            );

            return control;
        }

        public static IMyTerminalControlOnOffSwitch CreateGrindNearFirst(bool grindingAllowed, Func<IMyTerminalBlock, bool> isGrindingAllowed, Func<IMyTerminalBlock, bool> isReadonly, Func<IMyTerminalBlock, bool> isBaRSystem)
        {
            var isEnabled = grindingAllowed ? isBaRSystem : isReadonly;

            var control = Create(
                // Id:
                "GrindNearFirst",

                // Texts
                Texts.GrindOrderNearest,
                Texts.GrindOrderNearest_Tooltip,
                MySpaceTexts.SwitchText_On,
                MySpaceTexts.SwitchText_Off,

                // Visible:
                isGrindingAllowed,

                // Enabled:
                isEnabled,

                // Getter:
                (block) =>
                {
                    var system = NanobotTerminal.GetSystem(block);
                    return system != null ? ((system.Settings.Flags & SyncBlockSettings.Settings.GrindNearFirst) != 0) : false;
                },

                // Setter:
                (block, value) =>
                {
                    var system = NanobotTerminal.GetSystem(block);
                    if (system != null && isGrindingAllowed(block))
                    {
                        //Only one option (GrindNearFirst or GrindSmallestGridFirst) at a time is allowed
                        if (value)
                        {
                            system.Settings.Flags = (system.Settings.Flags & ~SyncBlockSettings.Settings.GrindSmallestGridFirst) | SyncBlockSettings.Settings.GrindNearFirst;
                        }
                        else
                        {
                            system.Settings.Flags = (system.Settings.Flags & ~SyncBlockSettings.Settings.GrindNearFirst);
                        }
                        foreach (var ctrl in NanobotTerminal.CustomControls)
                        {
                            if (ctrl.Id.Contains("GrindFarFirst")) ctrl.UpdateVisual();
                            if (ctrl.Id.Contains("GrindSmallestGridFirst")) ctrl.UpdateVisual();
                        }
                    }
                },

                // Multiple blocks support.
                true
            );

            return control;
        }

        public static IMyTerminalControlOnOffSwitch CreateGrindFarFirst(bool grindingAllowed, Func<IMyTerminalBlock, bool> isGrindingAllowed, Func<IMyTerminalBlock, bool> isReadonly, Func<IMyTerminalBlock, bool> isBaRSystem)
        {
            var isEnabled = grindingAllowed ? isBaRSystem : isReadonly;

            var control = Create(
                // Id:
                "GrindFarFirst",

                // Texts
                Texts.GrindOrderFarthest,
                Texts.GrindOrderFarthest_Tooltip,
                MySpaceTexts.SwitchText_On,
                MySpaceTexts.SwitchText_Off,

                // Visible:
                isGrindingAllowed,

                // Enabled:
                isEnabled,

                // Getter:
                (block) =>
                {
                    var system = NanobotTerminal.GetSystem(block);
                    return system != null ? ((system.Settings.Flags & (SyncBlockSettings.Settings.GrindNearFirst | SyncBlockSettings.Settings.GrindSmallestGridFirst)) == 0) : false;
                },

                // Setter:
                (block, value) =>
                {
                    var system = NanobotTerminal.GetSystem(block);
                    if (system != null && isGrindingAllowed(block))
                    {
                        //Only one option (GrindNearFirst or GrindSmallestGridFirst) at a time is allowed
                        if (value)
                        {
                            system.Settings.Flags = (system.Settings.Flags & ~(SyncBlockSettings.Settings.GrindSmallestGridFirst | SyncBlockSettings.Settings.GrindNearFirst));
                        }
                        foreach (var ctrl in NanobotTerminal.CustomControls)
                        {
                            if (ctrl.Id.Contains("GrindNearFirst")) ctrl.UpdateVisual();
                            if (ctrl.Id.Contains("GrindSmallestGridFirst")) ctrl.UpdateVisual();
                        }
                    }
                },

                // Multiple blocks support.
                true
            );

            return control;
        }

        public static IMyTerminalControlOnOffSwitch CreateGrindSmallestGridFirst(bool grindingAllowed, Func<IMyTerminalBlock, bool> isGrindingAllowed, Func<IMyTerminalBlock, bool> isReadonly, Func<IMyTerminalBlock, bool> isBaRSystem)
        {
            var isEnabled = grindingAllowed ? isBaRSystem : isReadonly;

            var control = Create(
                // Id:
                "GrindSmallestGridFirst",

                // Texts
                Texts.GrindOrderSmallest,
                Texts.GrindOrderSmallest_Tooltip,
                MySpaceTexts.SwitchText_On,
                MySpaceTexts.SwitchText_Off,

                // Visible:
                isGrindingAllowed,

                // Enabled:
                isEnabled,

                // Getter:
                (block) =>
                {
                    var system = NanobotTerminal.GetSystem(block);
                    return system != null ? ((system.Settings.Flags & SyncBlockSettings.Settings.GrindSmallestGridFirst) != 0) : false;
                },

                // Setter:
                (block, value) =>
                {
                    var system = NanobotTerminal.GetSystem(block);
                    if (system != null && isGrindingAllowed(block))
                    {
                        //Only one option (GrindNearFirst or GrindSmallestGridFirst) at a time is allowed
                        if (value)
                        {
                            system.Settings.Flags = (system.Settings.Flags & ~SyncBlockSettings.Settings.GrindNearFirst) | SyncBlockSettings.Settings.GrindSmallestGridFirst;
                        }
                        foreach (var ctrl in NanobotTerminal.CustomControls)
                        {
                            if (ctrl.Id.Contains("GrindNearFirst")) ctrl.UpdateVisual();
                            if (ctrl.Id.Contains("GrindFarFirst")) ctrl.UpdateVisual();
                        }
                    }
                },

                // Multiple blocks support.
                true
            );

            return control;
        }

        public static IMyTerminalControlOnOffSwitch CreateCollectPriority(Func<IMyTerminalBlock, bool> isChangeCollectPriorityPossible, Func<IMyTerminalBlock, bool> isReadonly, Func<IMyTerminalBlock, bool> isBaRSystem)
        {
            var control = Create(
                // Id:
                "CollectPriority",

                // Texts
                Texts.CollectPriority,
                Texts.CollectPriority_Tooltip,
                Texts.Priority_Enable,
                Texts.Priority_Disable,

                // Visible:
                (block) => { return true; },

                // Enabled:
                isChangeCollectPriorityPossible,

                // Getter:
                (block) =>
                {
                    var system = NanobotTerminal.GetSystem(block);
                    return system != null && system.ComponentCollectPriority != null && system.ComponentCollectPriority.Selected != null ? system.ComponentCollectPriority.GetEnabled(system.ComponentCollectPriority.Selected.Key) : false;
                },

                // Setter:
                (block, value) =>
                {
                    var system = NanobotTerminal.GetSystem(block);
                    if (system != null && system.ComponentCollectPriority != null && system.ComponentCollectPriority.Selected != null && !Mod.Settings.Welder.CollectPriorityFixed)
                    {
                        system.ComponentCollectPriority.SetEnabled(system.ComponentCollectPriority.Selected.Key, value);
                        system.Settings.ComponentCollectPriority = system.ComponentCollectPriority.GetEntries();
                        NanobotTerminal.UpdateVisual(NanobotTerminal._ComponentCollectPriorityListBox);
                    }
                },

                // Multiple blocks support.
                true
            );

            return control;
        }

        public static IMyTerminalControlOnOffSwitch ScriptControlled(Func<IMyTerminalBlock, bool> isBaRSystem)
        {
            var control = Create(
                // Id:
                "ScriptControlled",

                // Texts
                Texts.ScriptControlled,
                Texts.ScriptControlled_Tooltip,
                MySpaceTexts.SwitchText_On,
                MySpaceTexts.SwitchText_Off,

                // Visible:
                (_) => true,

                // Enabled:
                isBaRSystem,

                // Getter:
                (block) =>
                {
                    var system = NanobotTerminal.GetSystem(block);
                    return system != null ? ((system.Settings.Flags & SyncBlockSettings.Settings.ScriptControlled) != 0) : false;
                },

                // Setter:
                (block, value) =>
                {
                    var system = NanobotTerminal.GetSystem(block);
                    if (system != null)
                    {
                        system.Settings.Flags = (system.Settings.Flags & ~SyncBlockSettings.Settings.ScriptControlled) | (value ? SyncBlockSettings.Settings.ScriptControlled : 0);
                    }
                },

                // Multiple blocks support.
                true
            );

            return control;
        }
    }
}