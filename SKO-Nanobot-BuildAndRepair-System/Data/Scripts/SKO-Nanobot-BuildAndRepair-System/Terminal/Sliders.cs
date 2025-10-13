using Sandbox.Game.Localization;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces.Terminal;
using SKONanobotBuildAndRepairSystem.Localization;
using System;
using System.Text;
using VRage.Utils;
using VRageMath;

namespace SKONanobotBuildAndRepairSystem.Terminal
{
    public static class Sliders
    {
        private static IMyTerminalControlSlider Create(
            string id,
            MyStringId title,
            Func<IMyTerminalBlock, float> min,
            Func<IMyTerminalBlock, float> max,

            Func<IMyTerminalBlock, bool> isVisible,
            Func<IMyTerminalBlock, bool> isEnabled,
            Func<IMyTerminalBlock, float> getter,
            Action<IMyTerminalBlock, float> setter,
            Action<IMyTerminalBlock, StringBuilder> writer,

            bool supportsMultipleBlocks
            )
        {
            var control = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlSlider, IMyShipWelder>(id);

            control.Title = title;
            control.SetLimits(min, max);
            control.Visible = isVisible;
            control.Enabled = isEnabled;
            control.Getter = getter;
            control.Setter = setter;
            control.Writer = writer;
            control.SupportsMultipleBlocks = supportsMultipleBlocks;

            NanobotTerminal.CustomControls.Add(control);
            CreateSliderActions(id, control);

            return control;
        }

        private static void CreateSliderActions(string sliderName, IMyTerminalControlSlider slider)
        {
            var action = MyAPIGateway.TerminalControls.CreateAction<IMyShipWelder>(string.Format("{0}_Increase", sliderName));
            action.Name = new StringBuilder(string.Format("{0} Increase", sliderName));
            action.Icon = @"Textures\GUI\Icons\Actions\Increase.dds";
            action.Enabled = slider.Enabled;
            action.Action = (block) =>
            {
                var val = slider.Getter(block);
                slider.Setter(block, val + 1);
            };
            action.ValidForGroups = slider.SupportsMultipleBlocks;
            MyAPIGateway.TerminalControls.AddAction<IMyShipWelder>(action);

            action = MyAPIGateway.TerminalControls.CreateAction<IMyShipWelder>(string.Format("{0}_Decrease", sliderName));
            action.Name = new StringBuilder(string.Format("{0} Decrease", sliderName));
            action.Icon = @"Textures\GUI\Icons\Actions\Decrease.dds";
            action.Enabled = slider.Enabled;
            action.Action = (block) =>
            {
                var val = slider.Getter(block);
                slider.Setter(block, val - 1);
            };
            action.ValidForGroups = slider.SupportsMultipleBlocks;
            MyAPIGateway.TerminalControls.AddAction<IMyShipWelder>(action);
        }

        public static IMyTerminalControlSlider IgnoreColorHue(Func<IMyTerminalBlock, bool> colorPickerEnabled, Func<IMyTerminalBlock, bool> isWeldingAllowed)
        {
            var contrtol = Create(
                // Id:
                "IgnoreColorHue",

                // Texts:
                MySpaceTexts.EditFaction_HueSliderText,

                // Min/Max:
                (_) => 0, (_) => 360,

                // Visible / Enable:
                isWeldingAllowed,
                colorPickerEnabled,

                // Getter:
                (block) =>
                {
                    var system = NanobotTerminal.GetSystem(block);
                    return system != null ? system.Settings.IgnoreColor.X * 360f : 0;
                },

                // Setter:
                (block, val) =>
                {
                    var system = NanobotTerminal.GetSystem(block);
                    if (system != null)
                    {
                        var hsv = system.Settings.IgnoreColor;
                        val = val < 0 ? 0 : val > 360 ? 360 : val;
                        hsv.X = (float)Math.Round(val, 1, MidpointRounding.AwayFromZero) / 360;
                        system.Settings.IgnoreColor = hsv;
                    }
                },

                // Writer:
                (block, val) =>
                {
                    var system = NanobotTerminal.GetSystem(block);
                    if (system != null)
                    {
                        var hsv = system.Settings.IgnoreColor;
                        val.Append(Math.Round(hsv.X * 360f, 1, MidpointRounding.AwayFromZero));
                    }
                },

                // Multiple blocks support.
                true
            );

            return contrtol;
        }

        public static IMyTerminalControlSlider IgnoreColorSaturation(Func<IMyTerminalBlock, bool> colorPickerEnabled, Func<IMyTerminalBlock, bool> isWeldingAllowed)
        {
            var contrtol = Create(
                // Id:
                "IgnoreColorSaturation",

                // Texts:
                MySpaceTexts.EditFaction_SaturationSliderText,

                // Min/Max:
                (_) => 0, (_) => 100,

                // Visible / Enable:
                isWeldingAllowed,
                colorPickerEnabled,

                // Getter:
                (block) =>
                {
                    var system = NanobotTerminal.GetSystem(block);
                    return system != null ? (system.Settings.IgnoreColor.Y + NanobotTerminal.SATURATION_DELTA) * 100f : 0;
                },

                // Setter:
                (block, val) =>
                {
                    var system = NanobotTerminal.GetSystem(block);
                    if (system != null)
                    {
                        var hsv = system.Settings.IgnoreColor;
                        val = val < 0 ? 0 : val > 100 ? 100 : val;
                        hsv.Y = ((float)Math.Round(val, 1, MidpointRounding.AwayFromZero) / 100f) - NanobotTerminal.SATURATION_DELTA;
                        system.Settings.IgnoreColor = hsv;
                    }
                },

                // Writer:
                (block, val) =>
                {
                    var system = NanobotTerminal.GetSystem(block);
                    if (system != null)
                    {
                        var hsv = system.Settings.IgnoreColor;
                        val.Append(Math.Round((hsv.Y + NanobotTerminal.SATURATION_DELTA) * 100f, 1, MidpointRounding.AwayFromZero));
                    }
                },

                // Multiple blocks support.
                true
            );

            return contrtol;
        }

        public static IMyTerminalControlSlider IgnoreColorValue(Func<IMyTerminalBlock, bool> colorPickerEnabled, Func<IMyTerminalBlock, bool> isWeldingAllowed)
        {
            var contrtol = Create(
                // Id:
                "IgnoreColorValue",

                // Texts:
                MySpaceTexts.EditFaction_ValueSliderText,

                // Min/Max:
                (_) => 0, (_) => 100,

                // Visible / Enable:
                isWeldingAllowed,
                colorPickerEnabled,

                // Getter:
                (block) =>
                {
                    var system = NanobotTerminal.GetSystem(block);
                    return system != null ? (system.Settings.IgnoreColor.Z + NanobotTerminal.VALUE_DELTA - NanobotTerminal.VALUE_COLORIZE_DELTA) * 100f : 0;
                },

                // Setter:
                (block, z) =>
                {
                    var system = NanobotTerminal.GetSystem(block);
                    if (system != null)
                    {
                        var hsv = system.Settings.IgnoreColor;
                        z = z < 0 ? 0 : z > 100 ? 100 : z;
                        hsv.Z = ((float)Math.Round(z, 1, MidpointRounding.AwayFromZero) / 100f) - NanobotTerminal.VALUE_DELTA + NanobotTerminal.VALUE_COLORIZE_DELTA;
                        system.Settings.IgnoreColor = hsv;
                    }
                },

                // Writer:
                (block, val) =>
                {
                    var system = NanobotTerminal.GetSystem(block);
                    if (system != null)
                    {
                        var hsv = system.Settings.IgnoreColor;
                        val.Append(Math.Round((hsv.Z + NanobotTerminal.VALUE_DELTA - NanobotTerminal.VALUE_COLORIZE_DELTA) * 100f, 1, MidpointRounding.AwayFromZero));
                    }
                },

                // Multiple blocks support.
                true
            );

            return contrtol;
        }

        public static IMyTerminalControlSlider GrindColorHue(Func<IMyTerminalBlock, bool> colorPickerEnabled, Func<IMyTerminalBlock, bool> isGrindingAllowed)
        {
            var contrtol = Create(
                // Id:
                "GrindColorHue",

                // Texts:
                MySpaceTexts.EditFaction_HueSliderText,

                // Min/Max:
                (_) => 0, (_) => 360,

                // Visible / Enable:
                isGrindingAllowed,
                colorPickerEnabled,

                // Getter:
                (block) =>
                {
                    var system = NanobotTerminal.GetSystem(block);
                    return system != null ? system.Settings.GrindColor.X * 360f : 0;
                },

                // Setter:
                (block, val) =>
                {
                    var system = NanobotTerminal.GetSystem(block);
                    if (system != null)
                    {
                        var hsv = system.Settings.GrindColor;
                        val = val < 0 ? 0 : val > 360 ? 360 : val;
                        hsv.X = (float)Math.Round(val, 1, MidpointRounding.AwayFromZero) / 360;
                        system.Settings.GrindColor = hsv;
                    }
                },

                // Writer:
                (block, val) =>
                {
                    var system = NanobotTerminal.GetSystem(block);
                    if (system != null)
                    {
                        var hsv = system.Settings.GrindColor;
                        val.Append(Math.Round(hsv.X * 360f, 1, MidpointRounding.AwayFromZero));
                    }
                },

                // Multiple blocks support.
                true
            );

            return contrtol;
        }

        public static IMyTerminalControlSlider GrindColorSaturation(Func<IMyTerminalBlock, bool> colorPickerEnabled, Func<IMyTerminalBlock, bool> isGrindingAllowed)
        {
            var contrtol = Create(
                // Id:
                "GrindColorSaturation",

                // Texts:
                MySpaceTexts.EditFaction_SaturationSliderText,

                // Min/Max:
                (_) => 0, (_) => 100,

                // Visible / Enable:
                isGrindingAllowed,
                colorPickerEnabled,

                // Getter:
                (block) =>
                {
                    var system = NanobotTerminal.GetSystem(block);
                    return system != null ? (system.Settings.GrindColor.Y + NanobotTerminal.SATURATION_DELTA) * 100f : 0;
                },

                // Setter:
                (block, val) =>
                {
                    var system = NanobotTerminal.GetSystem(block);
                    if (system != null)
                    {
                        var hsv = system.Settings.GrindColor;
                        val = val < 0 ? 0 : val > 100 ? 100 : val;
                        hsv.Y = ((float)Math.Round(val, 1, MidpointRounding.AwayFromZero) / 100f) - NanobotTerminal.SATURATION_DELTA;
                        system.Settings.GrindColor = hsv;
                    }
                },

                // Writer:
                (block, val) =>
                {
                    var system = NanobotTerminal.GetSystem(block);
                    if (system != null)
                    {
                        var hsv = system.Settings.GrindColor;
                        val.Append(Math.Round((hsv.Y + NanobotTerminal.SATURATION_DELTA) * 100f, 1, MidpointRounding.AwayFromZero));
                    }
                },

                // Multiple blocks support.
                true
            );

            return contrtol;
        }

        public static IMyTerminalControlSlider GrindColorValue(Func<IMyTerminalBlock, bool> colorPickerEnabled, Func<IMyTerminalBlock, bool> isGrindingAllowed)
        {
            var contrtol = Create(
                // Id:
                "GrindColorValue",

                // Texts:
                MySpaceTexts.EditFaction_ValueSliderText,

                // Min/Max:
                (_) => 0, (_) => 100,

                // Visible / Enable:
                isGrindingAllowed,
                colorPickerEnabled,

                // Getter:
                (block) =>
                {
                    var system = NanobotTerminal.GetSystem(block);
                    return system != null ? (system.Settings.GrindColor.Z + NanobotTerminal.VALUE_DELTA - NanobotTerminal.VALUE_COLORIZE_DELTA) * 100f : 0;
                },

                // Setter:
                (block, z) =>
                {
                    var system = NanobotTerminal.GetSystem(block);
                    if (system != null)
                    {
                        var hsv = system.Settings.GrindColor;
                        z = z < 0 ? 0 : z > 100 ? 100 : z;
                        hsv.Z = ((float)Math.Round(z, 1, MidpointRounding.AwayFromZero) / 100f) - NanobotTerminal.VALUE_DELTA + NanobotTerminal.VALUE_COLORIZE_DELTA;
                        system.Settings.GrindColor = hsv;
                    }
                },

                // Writer:
                (block, val) =>
                {
                    var system = NanobotTerminal.GetSystem(block);
                    if (system != null)
                    {
                        var hsv = system.Settings.GrindColor;
                        val.Append(Math.Round((hsv.Z + NanobotTerminal.VALUE_DELTA - NanobotTerminal.VALUE_COLORIZE_DELTA) * 100f, 1, MidpointRounding.AwayFromZero));
                    }
                },

                // Multiple blocks support.
                true
            );

            return contrtol;
        }

        public static IMyTerminalControlSlider AreaOffsetLeftRight(Func<IMyTerminalBlock, float> getLimitOffsetMin, Func<IMyTerminalBlock, float> getLimitOffsetMax, Func<IMyTerminalBlock, bool> isReadonly, Func<IMyTerminalBlock, bool> isBaRSystem)
        {
            var contrtol = Create(
                // Id:
                "AreaOffsetLeftRight",

                // Texts:
                MySpaceTexts.BlockPropertyTitle_ProjectionOffsetX,

                // Min/Max:
                getLimitOffsetMin, getLimitOffsetMax,

                // Visible:
                (_) => { return true; },

                // Enabled:
                Mod.Settings.Welder.AreaOffsetFixed ? isReadonly : isBaRSystem,

                // Getter:
                (block) =>
                {
                    var system = NanobotTerminal.GetSystem(block);
                    return system != null ? system.Settings.AreaOffset.X : 0;
                },

                // Setter:
                (block, val) =>
                {
                    var system = NanobotTerminal.GetSystem(block);
                    if (system != null)
                    {
                        var min = getLimitOffsetMin(block);
                        var max = getLimitOffsetMax(block);
                        val = (float)Math.Round(val * 2, MidpointRounding.AwayFromZero) / 2f;
                        val = val < min ? min : val > max ? max : val;
                        system.Settings.AreaOffset = new Vector3(val, system.Settings.AreaOffset.Y, system.Settings.AreaOffset.Z);
                    }
                },

                // Writer:
                (block, val) =>
                {
                    var system = NanobotTerminal.GetSystem(block);
                    if (system != null)
                    {
                        val.Append(system.Settings.AreaOffset.X + " m");
                    }
                },

                // Multiple blocks support.
                true
            );

            return contrtol;
        }

        public static IMyTerminalControlSlider AreaOffsetUpDown(Func<IMyTerminalBlock, float> getLimitOffsetMin, Func<IMyTerminalBlock, float> getLimitOffsetMax, Func<IMyTerminalBlock, bool> isReadonly, Func<IMyTerminalBlock, bool> isBaRSystem)
        {
            var contrtol = Create(
                // Id:
                "AreaOffsetUpDown",

                // Texts:
                MySpaceTexts.BlockPropertyTitle_ProjectionOffsetY,

                // Min/Max:
                getLimitOffsetMin, getLimitOffsetMax,

                // Visible:
                (_) => { return true; },

                // Enabled:
                Mod.Settings.Welder.AreaOffsetFixed ? isReadonly : isBaRSystem,

                // Getter:
                (block) =>
                {
                    var system = NanobotTerminal.GetSystem(block);
                    return system != null ? system.Settings.AreaOffset.Y : 0;
                },

                // Setter:
                (block, val) =>
                {
                    var system = NanobotTerminal.GetSystem(block);
                    if (system != null)
                    {
                        var min = getLimitOffsetMin(block);
                        var max = getLimitOffsetMax(block);
                        val = (float)Math.Round(val * 2, MidpointRounding.AwayFromZero) / 2f;
                        val = val < min ? min : val > max ? max : val;
                        system.Settings.AreaOffset = new Vector3(system.Settings.AreaOffset.X, val, system.Settings.AreaOffset.Z);
                    }
                },

                // Writer:
                (block, val) =>
                {
                    var system = NanobotTerminal.GetSystem(block);
                    if (system != null)
                    {
                        val.Append(system.Settings.AreaOffset.Y + " m");
                    }
                },

                // Multiple blocks support.
                true
            );

            return contrtol;
        }

        public static IMyTerminalControlSlider AreaOffsetFrontBack(Func<IMyTerminalBlock, float> getLimitOffsetMin, Func<IMyTerminalBlock, float> getLimitOffsetMax, Func<IMyTerminalBlock, bool> isReadonly, Func<IMyTerminalBlock, bool> isBaRSystem)
        {
            var contrtol = Create(
                // Id:
                "AreaOffsetFrontBack",

                // Texts:
                MySpaceTexts.BlockPropertyTitle_ProjectionOffsetZ,

                // Min/Max:
                getLimitOffsetMin, getLimitOffsetMax,

                // Visible:
                (_) => { return true; },

                // Enabled:
                Mod.Settings.Welder.AreaOffsetFixed ? isReadonly : isBaRSystem,

                // Getter:
                (block) =>
                {
                    var system = NanobotTerminal.GetSystem(block);
                    return system != null ? system.Settings.AreaOffset.Z : 0;
                },

                // Setter:
                (block, val) =>
                {
                    var system = NanobotTerminal.GetSystem(block);
                    if (system != null)
                    {
                        var min = getLimitOffsetMin(block);
                        var max = getLimitOffsetMax(block);
                        val = (float)Math.Round(val * 2, MidpointRounding.AwayFromZero) / 2f;
                        val = val < min ? min : val > max ? max : val;
                        system.Settings.AreaOffset = new Vector3(system.Settings.AreaOffset.X, system.Settings.AreaOffset.Y, val);
                    }
                },

                // Writer:
                (block, val) =>
                {
                    var system = NanobotTerminal.GetSystem(block);
                    if (system != null)
                    {
                        val.Append(system.Settings.AreaOffset.Z + " m");
                    }
                },

                // Multiple blocks support.
                true
            );

            return contrtol;
        }

        public static IMyTerminalControlSlider AreaWidth(Func<IMyTerminalBlock, float> getLimitMin, Func<IMyTerminalBlock, float> getLimitMax, Func<IMyTerminalBlock, bool> isReadonly, Func<IMyTerminalBlock, bool> isBaRSystem)
        {
            var contrtol = Create(
                // Id:
                "AreaWidth",

                // Texts:
                Texts.AreaWidth,

                // Min/Max:
                getLimitMin, getLimitMax,

                // Visible:
                (_) => { return true; },

                // Enabled:
                Mod.Settings.Welder.AreaSizeFixed ? isReadonly : isBaRSystem,

                // Getter:
                (block) =>
                {
                    var system = NanobotTerminal.GetSystem(block);
                    return system != null ? system.Settings.AreaSize.X : 0;
                },

                // Setter:
                (block, val) =>
                {
                    var system = NanobotTerminal.GetSystem(block);
                    if (system != null)
                    {
                        var min = getLimitMin(block);
                        var max = getLimitMax(block);
                        val = val < min ? min : val > max ? max : val;
                        system.Settings.AreaSize = new Vector3((int)Math.Round(val), system.Settings.AreaSize.Y, system.Settings.AreaSize.Z);
                    }
                },

                // Writer:
                (block, val) =>
                {
                    var system = NanobotTerminal.GetSystem(block);
                    if (system != null)
                    {
                        val.Append(system.Settings.AreaSize.X + " m");
                    }
                },

                // Multiple blocks support.
                true
            );

            return contrtol;
        }

        public static IMyTerminalControlSlider AreaHeight(Func<IMyTerminalBlock, float> getLimitMin, Func<IMyTerminalBlock, float> getLimitMax, Func<IMyTerminalBlock, bool> isReadonly, Func<IMyTerminalBlock, bool> isBaRSystem)
        {
            var contrtol = Create(
                // Id:
                "AreaHeight",

                // Texts:
                Texts.AreaHeight,

                // Min/Max:
                getLimitMin, getLimitMax,

                // Visible:
                (_) => { return true; },

                // Enabled:
                Mod.Settings.Welder.AreaSizeFixed ? isReadonly : isBaRSystem,

                // Getter:
                (block) =>
                {
                    var system = NanobotTerminal.GetSystem(block);
                    return system != null ? system.Settings.AreaSize.Y : 0;
                },

                // Setter:
                (block, val) =>
                {
                    var system = NanobotTerminal.GetSystem(block);
                    if (system != null)
                    {
                        var min = getLimitMin(block);
                        var max = getLimitMax(block);
                        val = val < min ? min : val > max ? max : val;
                        system.Settings.AreaSize = new Vector3(system.Settings.AreaSize.X, (int)Math.Round(val), system.Settings.AreaSize.Z);
                    }
                },

                // Writer:
                (block, val) =>
                {
                    var system = NanobotTerminal.GetSystem(block);
                    if (system != null)
                    {
                        val.Append(system.Settings.AreaSize.Y + " m");
                    }
                },

                // Multiple blocks support.
                true
            );

            return contrtol;
        }

        public static IMyTerminalControlSlider AreaDepth(Func<IMyTerminalBlock, float> getLimitMin, Func<IMyTerminalBlock, float> getLimitMax, Func<IMyTerminalBlock, bool> isReadonly, Func<IMyTerminalBlock, bool> isBaRSystem)
        {
            var contrtol = Create(
                // Id:
                "AreaDepth",

                // Texts:
                Texts.AreaDepth,

                // Min/Max:
                getLimitMin, getLimitMax,

                // Visible:
                (_) => { return true; },

                // Enabled:
                Mod.Settings.Welder.AreaSizeFixed ? isReadonly : isBaRSystem,

                // Getter:
                (block) =>
                {
                    var system = NanobotTerminal.GetSystem(block);
                    return system != null ? system.Settings.AreaSize.Z : 0;
                },

                // Setter:
                (block, val) =>
                {
                    var system = NanobotTerminal.GetSystem(block);
                    if (system != null)
                    {
                        var min = getLimitMin(block);
                        var max = getLimitMax(block);
                        val = val < min ? min : val > max ? max : val;
                        system.Settings.AreaSize = new Vector3(system.Settings.AreaSize.X, system.Settings.AreaSize.Y, (int)Math.Round(val));
                    }
                },

                // Writer:
                (block, val) =>
                {
                    var system = NanobotTerminal.GetSystem(block);
                    if (system != null)
                    {
                        val.Append(system.Settings.AreaSize.Z + " m");
                    }
                },

                // Multiple blocks support.
                true
            );

            return contrtol;
        }

        public static IMyTerminalControlSlider SoundVolume(Func<IMyTerminalBlock, bool> isReadonly, Func<IMyTerminalBlock, bool> isBaRSystem)
        {
            var contrtol = Create(
                // Id:
                "SoundVolume",

                // Texts:
                Texts.SoundVolume,

                // Min/Max:
                (_) => 0f, (_) => 100f,

                // Visible:
                (_) => { return true; },

                // Enabled:
                Mod.Settings.Welder.SoundVolumeFixed ? isReadonly : isBaRSystem,

                // Getter:
                (block) =>
                {
                    var system = NanobotTerminal.GetSystem(block);
                    return system != null ? 100f * system.Settings.SoundVolume / NanobotSystem.WELDER_SOUND_VOLUME : 0f;
                },

                // Setter:
                (block, val) =>
                {
                    var system = NanobotTerminal.GetSystem(block);
                    if (system != null)
                    {
                        var min = 0;
                        var max = 100;
                        val = val < min ? min : val > max ? max : val;
                        system.Settings.SoundVolume = (float)Math.Round(val * NanobotSystem.WELDER_SOUND_VOLUME) / 100f;
                    }
                },

                // Writer:
                (block, val) =>
                {
                    var system = NanobotTerminal.GetSystem(block);
                    if (system != null)
                    {
                        val.Append(Math.Round(100f * system.Settings.SoundVolume / NanobotSystem.WELDER_SOUND_VOLUME) + " %");
                    }
                },

                // Multiple blocks support.
                true
            );

            return contrtol;
        }
    }
}