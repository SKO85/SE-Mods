namespace SKONanobotBuildAndRepairSystem
{
    using System;
    using System.Collections.Generic;
    using System.Text;
    using Sandbox.Game.Localization;
    using Sandbox.ModAPI;
    using Sandbox.ModAPI.Interfaces.Terminal;
    using VRage;
    using VRage.ModAPI;
    using VRage.Utils;
    using VRageMath;

    [Flags]
    public enum SearchModes
    {
        /// <summary>
        /// Search Target blocks only inside connected blocks
        /// </summary>
        Grids = 0x0001,

        /// <summary>
        /// Search Target blocks in bounding boy independend of connection
        /// </summary>
        BoundingBox = 0x0002
    }

    [Flags]
    public enum WeldTo
    {
        WeldToFull = 0x0001,
        WeldToFunctionalOnly = 0x0002,
        WeldTo10Percent = 0x0003
    }

    [Flags]
    public enum WorkModes
    {
        /// <summary>
        /// Grind only if nothing to weld
        /// </summary>
        WeldBeforeGrind = 0x0001,

        /// <summary>
        /// Weld onyl if nothing to grind
        /// </summary>
        GrindBeforeWeld = 0x0002,

        /// <summary>
        /// Grind only if nothing to weld or
        /// build waiting for missing items
        /// </summary>
        GrindIfWeldGetStuck = 0x0004,

        /// <summary>
        /// Only welding is allowed
        /// </summary>
        WeldOnly = 0x0008,

        /// <summary>
        /// Only grinding is allowed
        /// </summary>
        GrindOnly = 0x0010
    }

    [Flags]
    public enum AutoGrindRelation
    {
        NoOwnership = 0x0001,
        Owner = 0x0002,
        FactionShare = 0x0004,
        Neutral = 0x0008,
        Enemies = 0x0010
    }

    [Flags]
    public enum AutoGrindOptions
    {
        DisableOnly = 0x0001,
        HackOnly = 0x0002
    }

    [Flags]
    public enum VisualAndSoundEffects
    {
        WeldingVisualEffect = 0x00000001,
        WeldingSoundEffect = 0x00000010,
        GrindingVisualEffect = 0x00000100,
        GrindingSoundEffect = 0x00001000,
        TransportVisualEffect = 0x00010000,
    }

    public static class NanobotBuildAndRepairSystemTerminal
    {
        public const float SATURATION_DELTA = 0.8f;
        public const float VALUE_DELTA = 0.55f;
        public const float VALUE_COLORIZE_DELTA = 0.1f;

        public static bool CustomControlsInit = false;
        private static readonly List<IMyTerminalControl> CustomControls = new List<IMyTerminalControl>();

        private static IMyTerminalControl _HelpOthers;
        private static IMyTerminalControlSeparator _SeparateWeldOptions;

        private static IMyTerminalControlSlider _IgnoreColorHueSlider;
        private static IMyTerminalControlSlider _IgnoreColorSaturationSlider;
        private static IMyTerminalControlSlider _IgnoreColorValueSlider;

        private static IMyTerminalControlSlider _GrindColorHueSlider;
        private static IMyTerminalControlSlider _GrindColorSaturationSlider;
        private static IMyTerminalControlSlider _GrindColorValueSlider;

        private static IMyTerminalControlOnOffSwitch _WeldEnableDisableSwitch;
        private static IMyTerminalControlButton _WeldPriorityButtonUp;
        private static IMyTerminalControlButton _WeldPriorityButtonDown;
        private static IMyTerminalControlListbox _WeldPriorityListBox;
        private static IMyTerminalControlOnOffSwitch _GrindEnableDisableSwitch;
        private static IMyTerminalControlButton _GrindPriorityButtonUp;
        private static IMyTerminalControlButton _GrindPriorityButtonDown;
        private static IMyTerminalControlListbox _GrindPriorityListBox;

        private static IMyTerminalControlOnOffSwitch _ComponentCollectEnableDisableSwitch;
        private static IMyTerminalControlButton _ComponentCollectPriorityButtonUp;
        private static IMyTerminalControlButton _ComponentCollectPriorityButtonDown;
        private static IMyTerminalControlListbox _ComponentCollectPriorityListBox;
        private static IMyTerminalControlCheckbox _ComponentCollectIfIdleSwitch;

        /// <summary>
        /// Check an return the GameLogic object
        /// </summary>
        /// <param name="block"></param>
        /// <returns></returns>
        private static NanobotBuildAndRepairSystemBlock GetSystem(IMyTerminalBlock block)
        {
            return block?.GameLogic?.GetAs<NanobotBuildAndRepairSystemBlock>();
        }

        /// <summary>
        /// Initialize custom control definition
        /// </summary>
        public static void InitializeControls()
        {
            lock (CustomControls)
            {
                if (CustomControlsInit) return;
                CustomControlsInit = true;
                try
                {
                    // As CustomControlGetter is only called if the Terminal is opened,
                    // I add also some properties immediately and permanent to support scripting.
                    // !! As we can't subtype here they will be also available in every Shipwelder but without function !!

                    Logging.Instance?.Write(Logging.Level.Event, "InitializeControls");

                    MyAPIGateway.TerminalControls.CustomControlGetter += CustomControlGetter;

                    IMyTerminalControlLabel label;
                    IMyTerminalControlCheckbox checkbox;
                    IMyTerminalControlCombobox comboBox;
                    IMyTerminalControlSeparator separateArea;
                    IMyTerminalControlSlider slider;
                    IMyTerminalControlOnOffSwitch onoffSwitch;
                    IMyTerminalControlButton button;

                    var weldingAllowed = (NanobotBuildAndRepairSystemMod.Settings.Welder.AllowedWorkModes & (WorkModes.WeldBeforeGrind | WorkModes.GrindBeforeWeld | WorkModes.GrindIfWeldGetStuck | WorkModes.WeldOnly)) != 0;
                    var grindingAllowed = (NanobotBuildAndRepairSystemMod.Settings.Welder.AllowedWorkModes & (WorkModes.WeldBeforeGrind | WorkModes.GrindBeforeWeld | WorkModes.GrindIfWeldGetStuck | WorkModes.GrindOnly)) != 0;
                    var janitorAllowed = grindingAllowed && NanobotBuildAndRepairSystemMod.Settings.Welder.AllowedGrindJanitorRelations != 0;
                    var janitorAllowedNoOwnership = janitorAllowed && (NanobotBuildAndRepairSystemMod.Settings.Welder.AllowedGrindJanitorRelations & AutoGrindRelation.NoOwnership) != 0;
                    var janitorAllowedOwner = janitorAllowed && (NanobotBuildAndRepairSystemMod.Settings.Welder.AllowedGrindJanitorRelations & AutoGrindRelation.Owner) != 0;
                    var janitorAllowedFactionShare = janitorAllowed && (NanobotBuildAndRepairSystemMod.Settings.Welder.AllowedGrindJanitorRelations & AutoGrindRelation.FactionShare) != 0;
                    var janitorAllowedNeutral = janitorAllowed && (NanobotBuildAndRepairSystemMod.Settings.Welder.AllowedGrindJanitorRelations & AutoGrindRelation.Neutral) != 0;
                    var janitorAllowedEnemies = janitorAllowed && (NanobotBuildAndRepairSystemMod.Settings.Welder.AllowedGrindJanitorRelations & AutoGrindRelation.Enemies) != 0;

                    Func<IMyTerminalBlock, bool> isBaRSystem = (block) =>
                    {
                        var system = GetSystem(block);
                        return system != null;
                    };

                    Func<IMyTerminalBlock, bool> isReadonly = (block) => { return false; };
                    Func<IMyTerminalBlock, bool> isWeldingAllowed = (block) => { return weldingAllowed; };
                    Func<IMyTerminalBlock, bool> isGrindingAllowed = (block) => { return grindingAllowed; };
                    Func<IMyTerminalBlock, bool> isJanitorAllowed = (block) => { return janitorAllowed; };
                    Func<IMyTerminalBlock, bool> isJanitorAllowedNoOwnership = (block) => { return janitorAllowedNoOwnership; };
                    Func<IMyTerminalBlock, bool> isJanitorAllowedOwner = (block) => { return janitorAllowedOwner; };
                    Func<IMyTerminalBlock, bool> isJanitorAllowedFactionShare = (block) => { return janitorAllowedFactionShare; };
                    Func<IMyTerminalBlock, bool> isJanitorAllowedNeutral = (block) => { return janitorAllowedNeutral; };
                    Func<IMyTerminalBlock, bool> isJanitorAllowedEnemies = (block) => { return janitorAllowedEnemies; };
                    Func<IMyTerminalBlock, bool> isCollectPossible = (block) =>
                    {
                        var system = GetSystem(block);
                        return system != null && system.Settings.SearchMode == SearchModes.BoundingBox;
                    };
                    Func<IMyTerminalBlock, bool> isChangeCollectPriorityPossible = (block) =>
                    {
                        var system = GetSystem(block);
                        return system?.ComponentCollectPriority?.Selected != null && system.Settings.SearchMode == SearchModes.BoundingBox && !NanobotBuildAndRepairSystemMod.Settings.Welder.CollectPriorityFixed;
                    };

                    List<IMyTerminalControl> controls;
                    MyAPIGateway.TerminalControls.GetControls<IMyShipWelder>(out controls);
                    _HelpOthers = controls.Find((ctrl) =>
                    {
                        var cb = ctrl as IMyTerminalControlCheckbox;
                        return cb != null && ctrl.Id == "helpOthers";
                    });

                    // --- General
                    label = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlLabel, IMyShipWelder>("ModeSettings");
                    label.Label = Texts.ModeSettings_Headline;
                    CustomControls.Add(label);
                    {
                        // --- Select search mode
                        var onlyOneAllowed = (NanobotBuildAndRepairSystemMod.Settings.Welder.AllowedSearchModes & (NanobotBuildAndRepairSystemMod.Settings.Welder.AllowedSearchModes - 1)) == 0;
                        comboBox = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlCombobox, IMyShipWelder>("Mode");
                        comboBox.Title = Texts.SearchMode;
                        comboBox.Tooltip = Texts.SearchMode_Tooltip;
                        comboBox.Enabled = onlyOneAllowed ? isReadonly : isBaRSystem;

                        comboBox.ComboBoxContent = (list) =>
                        {
                            if (NanobotBuildAndRepairSystemMod.Settings.Welder.AllowedSearchModes.HasFlag(SearchModes.Grids))
                                list.Add(new MyTerminalControlComboBoxItem() { Key = (long)SearchModes.Grids, Value = Texts.SearchMode_Walk });
                            if (NanobotBuildAndRepairSystemMod.Settings.Welder.AllowedSearchModes.HasFlag(SearchModes.BoundingBox))
                                list.Add(new MyTerminalControlComboBoxItem() { Key = (long)SearchModes.BoundingBox, Value = Texts.SearchMode_Fly });
                        };
                        comboBox.Getter = (block) =>
                        {
                            var system = GetSystem(block);
                            if (system == null) return 0;
                            else return (long)system.Settings.SearchMode;
                        };
                        comboBox.Setter = (block, value) =>
                        {
                            var system = GetSystem(block);
                            if (system != null)
                            {
                                if (NanobotBuildAndRepairSystemMod.Settings.Welder.AllowedSearchModes.HasFlag((SearchModes)value))
                                {
                                    system.Settings.SearchMode = (SearchModes)value;
                                    UpdateVisual(_ComponentCollectPriorityListBox);
                                    UpdateVisual(_ComponentCollectIfIdleSwitch);
                                }
                            }
                        };
                        comboBox.SupportsMultipleBlocks = true;
                        CustomControls.Add(comboBox);
                        CreateProperty(comboBox, onlyOneAllowed);

                        //Allow switch mode by Buttonpanel
                        var list1 = new List<MyTerminalControlComboBoxItem>();
                        comboBox.ComboBoxContent(list1);
                        foreach (var entry in list1)
                        {
                            var mode = entry.Key;
                            var comboBox1 = comboBox;
                            var action = MyAPIGateway.TerminalControls.CreateAction<IMyShipWelder>(
                                $"{((SearchModes)mode).ToString()}_On");
                            action.Name = new StringBuilder($"{entry.Value} On");
                            action.Icon = @"Textures\GUI\Icons\Actions\SwitchOn.dds";
                            action.Enabled = isBaRSystem;
                            action.Action = (block) =>
                            {
                                comboBox1.Setter(block, mode);
                            };
                            action.ValidForGroups = true;
                            MyAPIGateway.TerminalControls.AddAction<IMyShipWelder>(action);
                        }

                        // --- Select work mode
                        onlyOneAllowed = (NanobotBuildAndRepairSystemMod.Settings.Welder.AllowedWorkModes & (NanobotBuildAndRepairSystemMod.Settings.Welder.AllowedWorkModes - 1)) == 0;
                        comboBox = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlCombobox, IMyShipWelder>("WorkMode");
                        comboBox.Title = Texts.WorkMode;
                        comboBox.Tooltip = Texts.WorkMode_Tooltip;
                        comboBox.Enabled = onlyOneAllowed ? isReadonly : isBaRSystem;
                        comboBox.ComboBoxContent = (list) =>
                        {
                            if (NanobotBuildAndRepairSystemMod.Settings.Welder.AllowedWorkModes.HasFlag(WorkModes.WeldBeforeGrind))
                                list.Add(new MyTerminalControlComboBoxItem() { Key = (long)WorkModes.WeldBeforeGrind, Value = Texts.WorkMode_WeldB4Grind });
                            if (NanobotBuildAndRepairSystemMod.Settings.Welder.AllowedWorkModes.HasFlag(WorkModes.GrindBeforeWeld))
                                list.Add(new MyTerminalControlComboBoxItem() { Key = (long)WorkModes.GrindBeforeWeld, Value = Texts.WorkMode_GrindB4Weld });
                            if (NanobotBuildAndRepairSystemMod.Settings.Welder.AllowedWorkModes.HasFlag(WorkModes.GrindIfWeldGetStuck))
                                list.Add(new MyTerminalControlComboBoxItem() { Key = (long)WorkModes.GrindIfWeldGetStuck, Value = Texts.WorkMode_GrindIfWeldStuck });
                            if (NanobotBuildAndRepairSystemMod.Settings.Welder.AllowedWorkModes.HasFlag(WorkModes.WeldOnly))
                                list.Add(new MyTerminalControlComboBoxItem() { Key = (long)WorkModes.WeldOnly, Value = Texts.WorkMode_WeldOnly });
                            if (NanobotBuildAndRepairSystemMod.Settings.Welder.AllowedWorkModes.HasFlag(WorkModes.GrindOnly))
                                list.Add(new MyTerminalControlComboBoxItem() { Key = (long)WorkModes.GrindOnly, Value = Texts.WorkMode_GrindOnly });
                        };
                        comboBox.Getter = (block) =>
                        {
                            var system = GetSystem(block);
                            if (system == null) return 0;
                            else return (long)system.Settings.WorkMode;
                        };
                        comboBox.Setter = (block, value) =>
                        {
                            var system = GetSystem(block);
                            if (system != null)
                            {
                                if (NanobotBuildAndRepairSystemMod.Settings.Welder.AllowedWorkModes.HasFlag((WorkModes)value))
                                {
                                    system.Settings.WorkMode = (WorkModes)value;
                                }
                            }
                        };
                        comboBox.SupportsMultipleBlocks = true;
                        CustomControls.Add(comboBox);
                        CreateProperty(comboBox, onlyOneAllowed);

                        //Allow switch work mode by Buttonpanel
                        list1 = new List<MyTerminalControlComboBoxItem>();
                        comboBox.ComboBoxContent(list1);
                        foreach (var entry in list1)
                        {
                            var mode = entry.Key;
                            var comboBox1 = comboBox;
                            var action = MyAPIGateway.TerminalControls.CreateAction<IMyShipWelder>(
                                $"{((WorkModes)mode).ToString()}_On");
                            action.Name = new StringBuilder($"{entry.Value} On");
                            action.Icon = @"Textures\GUI\Icons\Actions\SwitchOn.dds";
                            action.Enabled = isBaRSystem;
                            action.Action = (block) =>
                            {
                                comboBox1.Setter(block, mode);
                            };
                            action.ValidForGroups = true;
                            MyAPIGateway.TerminalControls.AddAction<IMyShipWelder>(action);
                        }
                    }

                    // --- Welding
                    label = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlLabel, IMyShipWelder>("WeldingSettings");
                    label.Label = Texts.WeldSettings_Headline;
                    CustomControls.Add(label);
                    {
                        // --- Set Color that marks blocks as 'ignore'
                        {
                            checkbox = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlCheckbox, IMyShipWelder>("UseIgnoreColor");
                            checkbox.Title = Texts.WeldUseIgnoreColor;
                            checkbox.Tooltip = Texts.WeldUseIgnoreColor_Tooltip;
                            checkbox.OnText = MySpaceTexts.SwitchText_On;
                            checkbox.OffText = MySpaceTexts.SwitchText_Off;
                            checkbox.Enabled = NanobotBuildAndRepairSystemMod.Settings.Welder.UseIgnoreColorFixed || !weldingAllowed ? isReadonly : isBaRSystem;
                            checkbox.Visible = isWeldingAllowed;
                            checkbox.Getter = (block) =>
                            {
                                var system = GetSystem(block);
                                return system != null && (system.Settings.Flags & SyncBlockSettings.Settings.UseIgnoreColor) != 0;
                            };
                            checkbox.Setter = (block, value) =>
                            {
                                var system = GetSystem(block);
                                if (system != null && !NanobotBuildAndRepairSystemMod.Settings.Welder.UseIgnoreColorFixed && isWeldingAllowed(block))
                                {
                                    system.Settings.Flags = (system.Settings.Flags & ~SyncBlockSettings.Settings.UseIgnoreColor) | (value ? SyncBlockSettings.Settings.UseIgnoreColor : 0);
                                    foreach (var ctrl in CustomControls)
                                    {
                                        if (ctrl.Id.Contains("IgnoreColor")) ctrl.UpdateVisual();
                                    }
                                }
                            };
                            checkbox.SupportsMultipleBlocks = true;
                            CreateCheckBoxAction("UseIgnoreColor", checkbox);
                            CustomControls.Add(checkbox);
                            CreateProperty(checkbox, NanobotBuildAndRepairSystemMod.Settings.Welder.UseIgnoreColorFixed);

                            Func<IMyTerminalBlock, bool> colorPickerEnabled = (block) =>
                            {
                                var system = GetSystem(block);
                                return system != null && (system.Settings.Flags & SyncBlockSettings.Settings.UseIgnoreColor) != 0 && !NanobotBuildAndRepairSystemMod.Settings.Welder.UseIgnoreColorFixed && isWeldingAllowed(block);
                            };

                            button = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlButton, IMyShipWelder>("IgnoreColorPickCurrent");
                            button.Title = Texts.Color_PickCurrentColor;
                            button.Enabled = colorPickerEnabled;
                            button.Visible = isWeldingAllowed;
                            button.Action = (block) =>
                            {
                                var system = GetSystem(block);
                                if (system != null && MyAPIGateway.Session.LocalHumanPlayer != null)
                                {
                                    system.Settings.IgnoreColor = MyAPIGateway.Session.LocalHumanPlayer.SelectedBuildColor;
                                    UpdateVisual(_IgnoreColorHueSlider);
                                    UpdateVisual(_IgnoreColorSaturationSlider);
                                    UpdateVisual(_IgnoreColorValueSlider);
                                }
                            };
                            button.SupportsMultipleBlocks = true;
                            CustomControls.Add(button);

                            button = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlButton, IMyShipWelder>("IgnoreColorSetAsCurrent");
                            button.Title = Texts.Color_SetCurrentColor;
                            button.Enabled = colorPickerEnabled;
                            button.Visible = isWeldingAllowed;
                            button.Action = (block) =>
                            {
                                var system = GetSystem(block);
                                if (system != null && MyAPIGateway.Session.LocalHumanPlayer != null)
                                {
                                    MyAPIGateway.Session.LocalHumanPlayer.SelectedBuildColor = system.Settings.IgnoreColor;
                                }
                            };
                            button.SupportsMultipleBlocks = true;
                            CustomControls.Add(button);

                            slider = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlSlider, IMyShipWelder>("IgnoreColorHue");
                            _IgnoreColorHueSlider = slider;
                            slider.Title = MySpaceTexts.EditFaction_HueSliderText;
                            slider.SetLimits(0, 360);
                            slider.Enabled = colorPickerEnabled;
                            slider.Visible = isWeldingAllowed;
                            slider.Getter = (block) =>
                            {
                                var system = GetSystem(block);
                                return system != null ? system.Settings.IgnoreColor.X * 360f : 0;
                            };
                            slider.Setter = (block, x) =>
                            {
                                var system = GetSystem(block);
                                if (system != null)
                                {
                                    var hsv = system.Settings.IgnoreColor;
                                    x = x < 0 ? 0 : x > 360 ? 360 : x;
                                    hsv.X = (float)Math.Round(x, 1, MidpointRounding.AwayFromZero) / 360;
                                    system.Settings.IgnoreColor = hsv;
                                }
                            };
                            slider.Writer = (block, val) =>
                            {
                                var system = GetSystem(block);
                                if (system != null)
                                {
                                    var hsv = system.Settings.IgnoreColor;
                                    val.Append(Math.Round(hsv.X * 360f, 1, MidpointRounding.AwayFromZero));
                                }
                            };
                            slider.SupportsMultipleBlocks = true;
                            CustomControls.Add(slider);
                            CreateSliderActions("IgnoreColorHue", slider);

                            slider = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlSlider, IMyShipWelder>("IgnoreColorSaturation");
                            _IgnoreColorSaturationSlider = slider;
                            slider.Title = MySpaceTexts.EditFaction_SaturationSliderText;
                            slider.SetLimits(0, 100);
                            slider.Enabled = colorPickerEnabled;
                            slider.Visible = isWeldingAllowed;
                            slider.Getter = (block) =>
                            {
                                var system = GetSystem(block);
                                return system != null ? (system.Settings.IgnoreColor.Y + SATURATION_DELTA) * 100f : 0;
                            };
                            slider.Setter = (block, val) =>
                            {
                                var system = GetSystem(block);
                                if (system != null)
                                {
                                    var hsv = system.Settings.IgnoreColor;
                                    val = val < 0 ? 0 : val > 100 ? 100 : val;
                                    hsv.Y = (float)Math.Round(val, 1, MidpointRounding.AwayFromZero) / 100f - SATURATION_DELTA;
                                    system.Settings.IgnoreColor = hsv;
                                }
                            };
                            slider.Writer = (block, val) =>
                            {
                                var system = GetSystem(block);
                                if (system != null)
                                {
                                    var hsv = system.Settings.IgnoreColor;
                                    val.Append(Math.Round((hsv.Y + SATURATION_DELTA) * 100f, 1, MidpointRounding.AwayFromZero));
                                }
                            };
                            slider.SupportsMultipleBlocks = true;
                            CustomControls.Add(slider);
                            CreateSliderActions("IgnoreColorSaturation", slider);

                            slider = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlSlider, IMyShipWelder>("IgnoreColorValue");
                            _IgnoreColorValueSlider = slider;
                            slider.Title = MySpaceTexts.EditFaction_ValueSliderText; ;
                            slider.SetLimits(0, 100);
                            slider.Enabled = colorPickerEnabled;
                            slider.Visible = isWeldingAllowed;
                            slider.Getter = (block) =>
                            {
                                var system = GetSystem(block);
                                return system != null ? (system.Settings.IgnoreColor.Z + VALUE_DELTA - VALUE_COLORIZE_DELTA) * 100f : 0;
                            };
                            slider.Setter = (block, z) =>
                            {
                                var system = GetSystem(block);
                                if (system != null)
                                {
                                    var hsv = system.Settings.IgnoreColor;
                                    z = z < 0 ? 0 : z > 100 ? 100 : z;
                                    hsv.Z = (float)Math.Round(z, 1, MidpointRounding.AwayFromZero) / 100f - VALUE_DELTA + VALUE_COLORIZE_DELTA;
                                    system.Settings.IgnoreColor = hsv;
                                }
                            };
                            slider.Writer = (block, val) =>
                            {
                                var system = GetSystem(block);
                                if (system != null)
                                {
                                    var hsv = system.Settings.IgnoreColor;
                                    val.Append(Math.Round((hsv.Z + VALUE_DELTA - VALUE_COLORIZE_DELTA) * 100f, 1, MidpointRounding.AwayFromZero));
                                }
                            };
                            CustomControls.Add(slider);
                            CreateSliderActions("IgnoreColorValue", slider);

                            var propertyIC = MyAPIGateway.TerminalControls.CreateProperty<Vector3, IMyShipWelder>("BuildAndRepair.IgnoreColor");
                            propertyIC.SupportsMultipleBlocks = false;
                            propertyIC.Getter = (block) =>
                            {
                                var system = GetSystem(block);
                                return system != null ? ConvertFromHSVColor(system.Settings.IgnoreColor) : Vector3.Zero;
                            };
                            propertyIC.Setter = (block, value) =>
                            {
                                var system = GetSystem(block);
                                if (system != null && !NanobotBuildAndRepairSystemMod.Settings.Welder.UseIgnoreColorFixed)
                                {
                                    system.Settings.IgnoreColor = CheckConvertToHSVColor(value);
                                }
                            };
                            MyAPIGateway.TerminalControls.AddControl<IMyShipWelder>(propertyIC);
                        }

                        //Weld Options
                        _SeparateWeldOptions = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlSeparator, IMyShipWelder>("SeparateWeldOptions");
                        _SeparateWeldOptions.Visible = isWeldingAllowed;
                        CustomControls.Add(_SeparateWeldOptions);
                        {
                            // ---helpOthers
                            //Moved here

                            // --- AllowBuild CheckBox
                            checkbox = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlCheckbox, IMyShipWelder>("AllowBuild");
                            checkbox.Title = Texts.WeldBuildNew;
                            checkbox.Tooltip = Texts.WeldBuildNew_Tooltip;
                            checkbox.OnText = MySpaceTexts.SwitchText_On;
                            checkbox.OffText = MySpaceTexts.SwitchText_Off;
                            checkbox.Enabled = NanobotBuildAndRepairSystemMod.Settings.Welder.AllowBuildFixed || !weldingAllowed ? isReadonly : isBaRSystem;
                            checkbox.Visible = isWeldingAllowed;
                            checkbox.Getter = (block) =>
                            {
                                var system = GetSystem(block);
                                return system != null && (system.Settings.Flags & SyncBlockSettings.Settings.AllowBuild) != 0;
                            };
                            checkbox.Setter = (block, value) =>
                            {
                                var system = GetSystem(block);
                                if (system != null && !NanobotBuildAndRepairSystemMod.Settings.Welder.AllowBuildFixed && isWeldingAllowed(block))
                                {
                                    system.Settings.Flags = (system.Settings.Flags & ~SyncBlockSettings.Settings.AllowBuild) | (value ? SyncBlockSettings.Settings.AllowBuild : 0);
                                }
                            };
                            checkbox.SupportsMultipleBlocks = true;
                            CreateCheckBoxAction("AllowBuild", checkbox);
                            CustomControls.Add(checkbox);
                            CreateProperty(checkbox, NanobotBuildAndRepairSystemMod.Settings.Welder.AllowBuildFixed || !weldingAllowed);

                            //--Weld to functional only
                            comboBox = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlCombobox, IMyShipWelder>("WeldToCombobox");
                            comboBox.Title = MyStringId.GetOrCompute("Weld to");
                            comboBox.Tooltip = Texts.WorkMode_Tooltip;
                            comboBox.Enabled = (_) => true;
                            comboBox.ComboBoxContent = (list) =>
                            {
                                list.Add(new MyTerminalControlComboBoxItem() { Key = (long)WeldTo.WeldToFull, Value = MyStringId.GetOrCompute("Weld to full") });
                                list.Add(new MyTerminalControlComboBoxItem() { Key = (long)WeldTo.WeldToFunctionalOnly, Value = MyStringId.GetOrCompute("Weld to Functional Only") });
                                list.Add(new MyTerminalControlComboBoxItem() { Key = (long)WeldTo.WeldTo10Percent, Value = MyStringId.GetOrCompute("Weld to ~20%") });
                            };

                            comboBox.Getter = (block) =>
                            {
                                var system = GetSystem(block);
                                if (system == null)
                                    return 0;
                                else return (long)system.Settings.WeldTo;                                
                            };

                            comboBox.Setter = (block, value) =>
                            {
                                var system = GetSystem(block);
                                if (system != null)
                                {
                                    system.Settings.WeldTo = (WeldTo)value;                                    
                                }
                            };

                            comboBox.SupportsMultipleBlocks = true;
                            CustomControls.Add(comboBox);
                            CreateProperty(comboBox, false);




                            //checkbox = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlCheckbox, IMyShipWelder>("WeldOptionFunctionalOnly");
                            //checkbox.Title = Texts.WeldToFuncOnly;
                            //checkbox.Tooltip = Texts.WeldToFuncOnly_Tooltip;
                            //checkbox.OnText = MySpaceTexts.SwitchText_On;
                            //checkbox.OffText = MySpaceTexts.SwitchText_Off;
                            //checkbox.Enabled = !weldingAllowed ? isReadonly : isBaRSystem;
                            //checkbox.Visible = isWeldingAllowed;
                            //checkbox.Getter = (block) =>
                            //{
                            //    var system = GetSystem(block);
                            //    return system != null && (system.Settings.WeldOptions & AutoWeldOptions.FunctionalOnly) != 0;
                            //};
                            //checkbox.Setter = (block, value) =>
                            //{
                            //    var system = GetSystem(block);
                            //    if (system != null && isWeldingAllowed(block))
                            //    {
                            //        if (value)
                            //        {
                            //            system.Settings.WeldOptions = system.Settings.WeldOptions | AutoWeldOptions.FunctionalOnly;
                            //            foreach (var ctrl in CustomControls)
                            //            {
                            //                if (ctrl.Id.Contains("WeldOption")) ctrl.UpdateVisual();
                            //            }
                            //        }
                            //        else
                            //        {
                            //            system.Settings.WeldOptions = system.Settings.WeldOptions & ~AutoWeldOptions.FunctionalOnly;
                            //        }
                            //    }
                            //};
                            //checkbox.SupportsMultipleBlocks = true;
                            //CreateCheckBoxAction("WeldOptionFunctionalOnly", checkbox);
                            //CustomControls.Add(checkbox);
                            //CreateProperty(checkbox, !weldingAllowed);
                        }

                        // -- Priority Welding
                        separateArea = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlSeparator, IMyShipWelder>("SeparateWeldPrio");
                        separateArea.Visible = isWeldingAllowed;
                        CustomControls.Add(separateArea);
                        {
                            onoffSwitch = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlOnOffSwitch, IMyShipWelder>("WeldPriority");
                            _WeldEnableDisableSwitch = onoffSwitch;
                            onoffSwitch.Title = Texts.WeldPriority;
                            onoffSwitch.Tooltip = Texts.WeldPriority_Tooltip;
                            onoffSwitch.OnText = Texts.Priority_Enable;
                            onoffSwitch.OffText = Texts.Priority_Disable;
                            onoffSwitch.Visible = isWeldingAllowed;
                            onoffSwitch.Enabled = (block) =>
                            {
                                var system = GetSystem(block);
                                return system?.BlockWeldPriority?.Selected != null && isWeldingAllowed(block) && !NanobotBuildAndRepairSystemMod.Settings.Welder.PriorityFixed;
                            };

                            onoffSwitch.Getter = (block) =>
                            {
                                var system = GetSystem(block);
                                return system?.BlockWeldPriority?.Selected != null && system.BlockWeldPriority.GetEnabled(system.BlockWeldPriority.Selected.Key);
                            };
                            onoffSwitch.Setter = (block, value) =>
                            {
                                var system = GetSystem(block);
                                if (system?.BlockWeldPriority?.Selected != null && isWeldingAllowed(block) && !NanobotBuildAndRepairSystemMod.Settings.Welder.PriorityFixed)
                                {
                                    system.BlockWeldPriority.SetEnabled(system.BlockWeldPriority.Selected.Key, value);
                                    system.Settings.WeldPriority = system.BlockWeldPriority.GetEntries();
                                    UpdateVisual(_WeldPriorityListBox);
                                }
                            };
                            onoffSwitch.SupportsMultipleBlocks = true;
                            CustomControls.Add(onoffSwitch);

                            button = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlButton, IMyShipWelder>("WeldPriorityUp");
                            _WeldPriorityButtonUp = button;
                            button.Title = Texts.Priority_Up;
                            button.Visible = isWeldingAllowed;
                            button.Enabled = (block) =>
                            {
                                var system = GetSystem(block);
                                return system?.BlockWeldPriority?.Selected != null && isWeldingAllowed(block) && !NanobotBuildAndRepairSystemMod.Settings.Welder.PriorityFixed;
                            };
                            button.Action = (block) =>
                            {
                                var system = GetSystem(block);
                                if (system != null && !NanobotBuildAndRepairSystemMod.Settings.Welder.PriorityFixed)
                                {
                                    system.BlockWeldPriority.MoveSelectedUp();
                                    system.Settings.WeldPriority = system.BlockWeldPriority.GetEntries();
                                    UpdateVisual(_WeldPriorityListBox);
                                }
                            };
                            button.SupportsMultipleBlocks = true;
                            CustomControls.Add(button);

                            button = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlButton, IMyShipWelder>("WeldPriorityDown");
                            _WeldPriorityButtonDown = button;
                            button.Title = Texts.Priority_Down;
                            button.Visible = isWeldingAllowed;
                            button.Enabled = (block) =>
                            {
                                var system = GetSystem(block);
                                return system?.BlockWeldPriority?.Selected != null && isWeldingAllowed(block) && !NanobotBuildAndRepairSystemMod.Settings.Welder.PriorityFixed;
                            };
                            button.Action = (block) =>
                            {
                                var system = GetSystem(block);
                                if (system != null && !NanobotBuildAndRepairSystemMod.Settings.Welder.PriorityFixed)
                                {
                                    system.BlockWeldPriority.MoveSelectedDown();
                                    system.Settings.WeldPriority = system.BlockWeldPriority.GetEntries();
                                    UpdateVisual(_WeldPriorityListBox);
                                }
                            };
                            button.SupportsMultipleBlocks = true;
                            CustomControls.Add(button);

                            var listbox = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlListbox, IMyShipWelder>("WeldPriority");
                            _WeldPriorityListBox = listbox;

                            listbox.Multiselect = false;
                            listbox.VisibleRowsCount = 15;
                            listbox.Enabled = weldingAllowed ? isBaRSystem : isReadonly;
                            listbox.Visible = isWeldingAllowed;
                            listbox.ItemSelected = (block, selected) =>
                            {
                                var system = GetSystem(block);
                                if (system?.BlockWeldPriority != null)
                                {
                                    if (selected.Count > 0) system.BlockWeldPriority.SetSelectedByKey(((PrioItem)selected[0].UserData).Key);
                                    else system.BlockWeldPriority.ClearSelected();
                                    UpdateVisual(_WeldEnableDisableSwitch);
                                    UpdateVisual(_WeldPriorityButtonUp);
                                    UpdateVisual(_WeldPriorityButtonDown);
                                }
                            };
                            listbox.ListContent = (block, items, selected) =>
                            {
                                var system = GetSystem(block);
                                system?.BlockWeldPriority?.FillTerminalList(items, selected);
                            };
                            listbox.SupportsMultipleBlocks = true;
                            CustomControls.Add(listbox);
                        }
                    }

                    // --- Grinding
                    label = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlLabel, IMyShipWelder>("GrindingSettings");
                    label.Label = Texts.GrindSettings_Headline;
                    CustomControls.Add(label);
                    {
                        // --- Set Color that marks blocks as 'grind'
                        //separateArea = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlSeparator, IMyShipWelder>("SeparateGrindColor");
                        //separateArea.Visible = isGrindingAllowed;
                        //CustomControls.Add(separateArea);
                        {
                            checkbox = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlCheckbox, IMyShipWelder>("UseGrindColor");
                            checkbox.Title = Texts.GrindUseGrindColor;
                            checkbox.Tooltip = Texts.GrindUseGrindColor_Tooltip;
                            checkbox.OnText = MySpaceTexts.SwitchText_On;
                            checkbox.OffText = MySpaceTexts.SwitchText_Off;
                            checkbox.Enabled = NanobotBuildAndRepairSystemMod.Settings.Welder.UseGrindColorFixed || !grindingAllowed ? isReadonly : isBaRSystem;
                            checkbox.Visible = isGrindingAllowed;
                            checkbox.Getter = (block) =>
                            {
                                var system = GetSystem(block);
                                return system != null && (system.Settings.Flags & SyncBlockSettings.Settings.UseGrindColor) != 0;
                            };
                            checkbox.Setter = (block, value) =>
                            {
                                var system = GetSystem(block);
                                if (system != null && !NanobotBuildAndRepairSystemMod.Settings.Welder.UseGrindColorFixed && isGrindingAllowed(block))
                                {
                                    system.Settings.Flags = (system.Settings.Flags & ~SyncBlockSettings.Settings.UseGrindColor) | (value ? SyncBlockSettings.Settings.UseGrindColor : 0);
                                    foreach (var ctrl in CustomControls)
                                    {
                                        if (ctrl.Id.Contains("GrindColor")) ctrl.UpdateVisual();
                                    }
                                }
                            };
                            checkbox.SupportsMultipleBlocks = true;
                            CreateCheckBoxAction("UseGrindColor", checkbox);
                            CustomControls.Add(checkbox);
                            CreateProperty(checkbox, NanobotBuildAndRepairSystemMod.Settings.Welder.UseGrindColorFixed || !grindingAllowed);

                            Func<IMyTerminalBlock, bool> colorPickerEnabled = (block) =>
                            {
                                var system = GetSystem(block);
                                return system != null && (system.Settings.Flags & SyncBlockSettings.Settings.UseGrindColor) != 0 && !NanobotBuildAndRepairSystemMod.Settings.Welder.UseGrindColorFixed && isGrindingAllowed(block);
                            };

                            button = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlButton, IMyShipWelder>("GrindColorPickCurrent");
                            button.Title = Texts.Color_PickCurrentColor;
                            button.Enabled = colorPickerEnabled;
                            button.Visible = isGrindingAllowed;
                            button.Action = (block) =>
                            {
                                var system = GetSystem(block);
                                if (system != null && MyAPIGateway.Session.LocalHumanPlayer != null)
                                {
                                    system.Settings.GrindColor = MyAPIGateway.Session.LocalHumanPlayer.SelectedBuildColor;
                                    UpdateVisual(_GrindColorHueSlider);
                                    UpdateVisual(_GrindColorSaturationSlider);
                                    UpdateVisual(_GrindColorValueSlider);
                                }
                            };
                            button.SupportsMultipleBlocks = true;
                            CustomControls.Add(button);

                            button = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlButton, IMyShipWelder>("GrindColorSetAsCurrent");
                            button.Title = Texts.Color_SetCurrentColor;
                            button.Enabled = colorPickerEnabled;
                            button.Visible = isGrindingAllowed;
                            button.Action = (block) =>
                            {
                                var system = GetSystem(block);
                                if (system != null && MyAPIGateway.Session.LocalHumanPlayer != null)
                                {
                                    MyAPIGateway.Session.LocalHumanPlayer.SelectedBuildColor = system.Settings.GrindColor;
                                }
                            };
                            button.SupportsMultipleBlocks = true;
                            CustomControls.Add(button);

                            slider = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlSlider, IMyShipWelder>("GrindColorHue");
                            _GrindColorHueSlider = slider;
                            slider.Title = MySpaceTexts.EditFaction_HueSliderText;
                            slider.SetLimits(0, 360);
                            slider.Enabled = colorPickerEnabled;
                            slider.Visible = isGrindingAllowed;
                            slider.Getter = (block) =>
                            {
                                var system = GetSystem(block);
                                return system != null ? system.Settings.GrindColor.X * 360f : 0;
                            };
                            slider.Setter = (block, val) =>
                            {
                                var system = GetSystem(block);
                                if (system != null)
                                {
                                    var hsv = system.Settings.GrindColor;
                                    val = val < 0 ? 0 : val > 360 ? 360 : val;
                                    hsv.X = (float)Math.Round(val, 1, MidpointRounding.AwayFromZero) / 360;
                                    system.Settings.GrindColor = hsv;
                                }
                            };
                            slider.Writer = (block, val) =>
                            {
                                var system = GetSystem(block);
                                if (system != null)
                                {
                                    var hsv = system.Settings.GrindColor;
                                    val.Append(Math.Round(hsv.X * 360f, 1, MidpointRounding.AwayFromZero));
                                }
                            };
                            slider.SupportsMultipleBlocks = true;
                            CustomControls.Add(slider);
                            CreateSliderActions("GrindColorHue", slider);

                            slider = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlSlider, IMyShipWelder>("GrindColorSaturation");
                            _GrindColorSaturationSlider = slider;
                            slider.Title = MySpaceTexts.EditFaction_SaturationSliderText;
                            slider.SetLimits(0, 100);
                            slider.Enabled = colorPickerEnabled;
                            slider.Visible = isGrindingAllowed;
                            slider.Getter = (block) =>
                            {
                                var system = GetSystem(block);
                                return system != null ? (system.Settings.GrindColor.Y + SATURATION_DELTA) * 100f : 0;
                            };
                            slider.Setter = (block, val) =>
                            {
                                var system = GetSystem(block);
                                if (system != null)
                                {
                                    var hsv = system.Settings.GrindColor;
                                    val = val < 0 ? 0 : val > 100 ? 100 : val;
                                    hsv.Y = (float)Math.Round(val, 1, MidpointRounding.AwayFromZero) / 100f - SATURATION_DELTA;
                                    system.Settings.GrindColor = hsv;
                                }
                            };
                            slider.Writer = (block, val) =>
                            {
                                var system = GetSystem(block);
                                if (system != null)
                                {
                                    var hsv = system.Settings.GrindColor;
                                    val.Append(Math.Round((hsv.Y + SATURATION_DELTA) * 100f, 1, MidpointRounding.AwayFromZero));
                                }
                            };
                            slider.SupportsMultipleBlocks = true;
                            CustomControls.Add(slider);
                            CreateSliderActions("GrindColorSaturation", slider);

                            slider = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlSlider, IMyShipWelder>("GrindColorValue");
                            _GrindColorValueSlider = slider;
                            slider.Title = MySpaceTexts.EditFaction_ValueSliderText;
                            slider.SetLimits(0, 100);
                            slider.Enabled = colorPickerEnabled;
                            slider.Visible = isGrindingAllowed;
                            slider.Getter = (block) =>
                            {
                                var system = GetSystem(block);
                                return system != null ? (system.Settings.GrindColor.Z + VALUE_DELTA - VALUE_COLORIZE_DELTA) * 100f : 0;
                            };
                            slider.Setter = (block, z) =>
                            {
                                var system = GetSystem(block);
                                if (system != null)
                                {
                                    var hsv = system.Settings.GrindColor;
                                    z = z < 0 ? 0 : z > 100 ? 100 : z;
                                    hsv.Z = (float)Math.Round(z, 1, MidpointRounding.AwayFromZero) / 100f - VALUE_DELTA + VALUE_COLORIZE_DELTA;
                                    system.Settings.GrindColor = hsv;
                                }
                            };
                            slider.Writer = (block, val) =>
                            {
                                var system = GetSystem(block);
                                if (system != null)
                                {
                                    var hsv = system.Settings.GrindColor;
                                    val.Append(Math.Round((hsv.Z + VALUE_DELTA - VALUE_COLORIZE_DELTA) * 100f, 1, MidpointRounding.AwayFromZero));
                                }
                            };
                            slider.SupportsMultipleBlocks = true;
                            CustomControls.Add(slider);
                            CreateSliderActions("GrindColorValue", slider);

                            var propertyGC = MyAPIGateway.TerminalControls.CreateProperty<Vector3, IMyShipWelder>("BuildAndRepair.GrindColor");
                            propertyGC.SupportsMultipleBlocks = false;
                            propertyGC.Getter = (block) =>
                            {
                                var system = GetSystem(block);
                                return system != null ? ConvertFromHSVColor(system.Settings.GrindColor) : Vector3.Zero;
                            };
                            propertyGC.Setter = (block, value) =>
                            {
                                var system = GetSystem(block);
                                if (system != null && !NanobotBuildAndRepairSystemMod.Settings.Welder.UseGrindColorFixed)
                                {
                                    system.Settings.GrindColor = CheckConvertToHSVColor(value);
                                }
                            };
                            MyAPIGateway.TerminalControls.AddControl<IMyShipWelder>(propertyGC);
                        }

                        // --- Enable Janitor grinding
                        separateArea = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlSeparator, IMyShipWelder>("SeparateGrindJanitor");
                        separateArea.Visible = isJanitorAllowed;
                        CustomControls.Add(separateArea);
                        {
                            //--Grind enemy
                            onoffSwitch = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlOnOffSwitch, IMyShipWelder>("GrindJanitorEnemies");
                            onoffSwitch.Title = Texts.GrindJanitorEnemy;
                            onoffSwitch.Tooltip = Texts.GrindJanitorEnemy_Tooltip;
                            onoffSwitch.OnText = MySpaceTexts.SwitchText_On;
                            onoffSwitch.OffText = MySpaceTexts.SwitchText_Off;
                            onoffSwitch.Enabled = NanobotBuildAndRepairSystemMod.Settings.Welder.UseGrindJanitorFixed || !janitorAllowedEnemies ? isReadonly : isBaRSystem;
                            onoffSwitch.Visible = isJanitorAllowedEnemies;
                            onoffSwitch.Getter = (block) =>
                            {
                                var system = GetSystem(block);
                                return system != null && (system.Settings.UseGrindJanitorOn & AutoGrindRelation.Enemies) != 0;
                            };
                            onoffSwitch.Setter = (block, value) =>
                            {
                                var system = GetSystem(block);
                                if (system != null && !NanobotBuildAndRepairSystemMod.Settings.Welder.UseGrindJanitorFixed && isJanitorAllowedEnemies(block))
                                {
                                    system.Settings.UseGrindJanitorOn = (system.Settings.UseGrindJanitorOn & ~AutoGrindRelation.Enemies) | (value ? AutoGrindRelation.Enemies : 0);
                                }
                            };
                            onoffSwitch.SupportsMultipleBlocks = true;
                            CreateOnOffSwitchAction("GrindJanitorEnemies", onoffSwitch);
                            CustomControls.Add(onoffSwitch);
                            CreateProperty(onoffSwitch, NanobotBuildAndRepairSystemMod.Settings.Welder.UseGrindJanitorFixed || !janitorAllowedEnemies);

                            //--Grind not owned
                            onoffSwitch = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlOnOffSwitch, IMyShipWelder>("GrindJanitorNotOwned");
                            onoffSwitch.Title = Texts.GrindJanitorNotOwned;
                            onoffSwitch.Tooltip = Texts.GrindJanitorNotOwned_Tooltip;
                            onoffSwitch.OnText = MySpaceTexts.SwitchText_On;
                            onoffSwitch.OffText = MySpaceTexts.SwitchText_Off;
                            onoffSwitch.Enabled = NanobotBuildAndRepairSystemMod.Settings.Welder.UseGrindJanitorFixed || !janitorAllowedNoOwnership ? isReadonly : isBaRSystem;
                            onoffSwitch.Visible = isJanitorAllowedNoOwnership;
                            onoffSwitch.Getter = (block) =>
                            {
                                var system = GetSystem(block);
                                return system != null && (system.Settings.UseGrindJanitorOn & AutoGrindRelation.NoOwnership) != 0;
                            };
                            onoffSwitch.Setter = (block, value) =>
                            {
                                var system = GetSystem(block);
                                if (system != null && !NanobotBuildAndRepairSystemMod.Settings.Welder.UseGrindJanitorFixed && isJanitorAllowedNoOwnership(block))
                                {
                                    system.Settings.UseGrindJanitorOn = (system.Settings.UseGrindJanitorOn & ~AutoGrindRelation.NoOwnership) | (value ? AutoGrindRelation.NoOwnership : 0);
                                }
                            };
                            onoffSwitch.SupportsMultipleBlocks = true;
                            CreateOnOffSwitchAction("GrindJanitorNotOwned", onoffSwitch);
                            CustomControls.Add(onoffSwitch);
                            CreateProperty(onoffSwitch, NanobotBuildAndRepairSystemMod.Settings.Welder.UseGrindJanitorFixed || !janitorAllowedNoOwnership);

                            //--Grind Neutrals
                            onoffSwitch = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlOnOffSwitch, IMyShipWelder>("GrindJanitorNeutrals");
                            onoffSwitch.Title = Texts.GrindJanitorNeutrals;
                            onoffSwitch.Tooltip = Texts.GrindJanitorNeutrals_Tooltip;
                            onoffSwitch.OnText = MySpaceTexts.SwitchText_On;
                            onoffSwitch.OffText = MySpaceTexts.SwitchText_Off;
                            onoffSwitch.Enabled = NanobotBuildAndRepairSystemMod.Settings.Welder.UseGrindJanitorFixed || !janitorAllowedNeutral ? isReadonly : isBaRSystem;
                            onoffSwitch.Visible = isJanitorAllowedNeutral;
                            onoffSwitch.Getter = (block) =>
                            {
                                var system = GetSystem(block);
                                return system != null && (system.Settings.UseGrindJanitorOn & AutoGrindRelation.Neutral) != 0;
                            };
                            onoffSwitch.Setter = (block, value) =>
                            {
                                var system = GetSystem(block);
                                if (system != null && !NanobotBuildAndRepairSystemMod.Settings.Welder.UseGrindJanitorFixed && isJanitorAllowedNeutral(block))
                                {
                                    system.Settings.UseGrindJanitorOn = (system.Settings.UseGrindJanitorOn & ~AutoGrindRelation.Neutral) | (value ? AutoGrindRelation.Neutral : 0);
                                }
                            };
                            onoffSwitch.SupportsMultipleBlocks = true;
                            CreateOnOffSwitchAction("GrindJanitorNeutrals", onoffSwitch);
                            CustomControls.Add(onoffSwitch);
                            CreateProperty(onoffSwitch, NanobotBuildAndRepairSystemMod.Settings.Welder.UseGrindJanitorFixed || !janitorAllowedNeutral);
                        }

                        //Grind Options
                        separateArea = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlSeparator, IMyShipWelder>("SeparateGrindOptions");
                        separateArea.Visible = isJanitorAllowed;
                        CustomControls.Add(separateArea);
                        {
                            //--Grind Disable only
                            checkbox = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlCheckbox, IMyShipWelder>("GrindJanitorOptionDisableOnly");
                            checkbox.Title = Texts.GrindJanitorDisableOnly;
                            checkbox.Tooltip = Texts.GrindJanitorDisableOnly_Tooltip;
                            checkbox.OnText = MySpaceTexts.SwitchText_On;
                            checkbox.OffText = MySpaceTexts.SwitchText_Off;
                            checkbox.Enabled = NanobotBuildAndRepairSystemMod.Settings.Welder.UseGrindJanitorFixed || !grindingAllowed ? isReadonly : isBaRSystem;
                            checkbox.Visible = isJanitorAllowed;
                            checkbox.Getter = (block) =>
                            {
                                var system = GetSystem(block);
                                return system != null && (system.Settings.GrindJanitorOptions & AutoGrindOptions.DisableOnly) != 0;
                            };
                            checkbox.Setter = (block, value) =>
                            {
                                var system = GetSystem(block);
                                if (system != null && !NanobotBuildAndRepairSystemMod.Settings.Welder.UseGrindJanitorFixed && isJanitorAllowed(block))
                                {
                                    //Only one option (HackOnly or DisableOnly) at a time is allowed
                                    if (value)
                                    {
                                        system.Settings.GrindJanitorOptions = (system.Settings.GrindJanitorOptions & ~AutoGrindOptions.HackOnly) | AutoGrindOptions.DisableOnly;
                                        foreach (var ctrl in CustomControls)
                                        {
                                            if (ctrl.Id.Contains("GrindJanitorOption")) ctrl.UpdateVisual();
                                        }
                                    }
                                    else
                                    {
                                        system.Settings.GrindJanitorOptions = system.Settings.GrindJanitorOptions & ~AutoGrindOptions.DisableOnly;
                                    }
                                }
                            };
                            checkbox.SupportsMultipleBlocks = true;
                            CreateCheckBoxAction("GrindJanitorOptionDisableOnly", checkbox);
                            CustomControls.Add(checkbox);
                            CreateProperty(checkbox, NanobotBuildAndRepairSystemMod.Settings.Welder.UseGrindJanitorFixed || !grindingAllowed);

                            //--Grind Hack only
                            checkbox = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlCheckbox, IMyShipWelder>("GrindJanitorOptionHackOnly");
                            checkbox.Title = Texts.GrindJanitorHackOnly;
                            checkbox.Tooltip = Texts.GrindJanitorHackOnly_Tooltip;
                            checkbox.OnText = MySpaceTexts.SwitchText_On;
                            checkbox.OffText = MySpaceTexts.SwitchText_Off;
                            checkbox.Enabled = NanobotBuildAndRepairSystemMod.Settings.Welder.UseGrindJanitorFixed || !grindingAllowed ? isReadonly : isBaRSystem;
                            checkbox.Visible = isJanitorAllowed;
                            checkbox.Getter = (block) =>
                            {
                                var system = GetSystem(block);
                                return system != null && (system.Settings.GrindJanitorOptions & AutoGrindOptions.HackOnly) != 0;
                            };
                            checkbox.Setter = (block, value) =>
                            {
                                var system = GetSystem(block);
                                if (system != null && !NanobotBuildAndRepairSystemMod.Settings.Welder.UseGrindJanitorFixed && isJanitorAllowed(block))
                                {
                                    //Only one option (HackOnly or DisableOnly) at a time is allowed
                                    if (value)
                                    {
                                        system.Settings.GrindJanitorOptions = (system.Settings.GrindJanitorOptions & ~AutoGrindOptions.DisableOnly) | AutoGrindOptions.HackOnly;
                                        foreach (var ctrl in CustomControls)
                                        {
                                            if (ctrl.Id.Contains("GrindJanitorOption")) ctrl.UpdateVisual();
                                        }
                                    }
                                    else
                                    {
                                        system.Settings.GrindJanitorOptions = system.Settings.GrindJanitorOptions & ~AutoGrindOptions.HackOnly;
                                    }
                                }
                            };
                            checkbox.SupportsMultipleBlocks = true;
                            CreateCheckBoxAction("GrindJanitorOptionHackOnly", checkbox);
                            CustomControls.Add(checkbox);
                            CreateProperty(checkbox, NanobotBuildAndRepairSystemMod.Settings.Welder.UseGrindJanitorFixed || !grindingAllowed);
                        }

                        //Grind Priority
                        separateArea = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlSeparator, IMyShipWelder>("SeparateGrindPrio");
                        separateArea.Visible = isGrindingAllowed;
                        CustomControls.Add(separateArea);
                        {
                            onoffSwitch = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlOnOffSwitch, IMyShipWelder>("GrindPriority");
                            _GrindEnableDisableSwitch = onoffSwitch;
                            onoffSwitch.Title = Texts.GrindPriority;
                            onoffSwitch.Tooltip = Texts.GrindPriority_Tooltip;
                            onoffSwitch.OnText = Texts.Priority_Enable;
                            onoffSwitch.OffText = Texts.Priority_Disable;
                            onoffSwitch.Visible = isGrindingAllowed;
                            onoffSwitch.Enabled = (block) =>
                            {
                                var system = GetSystem(block);
                                return system?.BlockGrindPriority?.Selected != null && isGrindingAllowed(block) && !NanobotBuildAndRepairSystemMod.Settings.Welder.PriorityFixed;
                            };

                            onoffSwitch.Getter = (block) =>
                            {
                                var system = GetSystem(block);
                                return system?.BlockGrindPriority?.Selected != null && system.BlockGrindPriority.GetEnabled(system.BlockGrindPriority.Selected.Key);
                            };
                            onoffSwitch.Setter = (block, value) =>
                            {
                                var system = GetSystem(block);
                                if (system?.BlockGrindPriority?.Selected != null && isGrindingAllowed(block) && !NanobotBuildAndRepairSystemMod.Settings.Welder.PriorityFixed)
                                {
                                    system.BlockGrindPriority.SetEnabled(system.BlockGrindPriority.Selected.Key, value);
                                    system.Settings.GrindPriority = system.BlockGrindPriority.GetEntries();
                                    UpdateVisual(_GrindPriorityListBox);
                                }
                            };
                            onoffSwitch.SupportsMultipleBlocks = true;
                            CustomControls.Add(onoffSwitch);

                            button = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlButton, IMyShipWelder>("GrindPriorityUp");
                            _GrindPriorityButtonUp = button;
                            button.Title = Texts.Priority_Up;
                            button.Visible = isGrindingAllowed;
                            button.Enabled = (block) =>
                            {
                                var system = GetSystem(block);
                                return system?.BlockGrindPriority?.Selected != null && isGrindingAllowed(block) && !NanobotBuildAndRepairSystemMod.Settings.Welder.PriorityFixed;
                            };
                            button.Action = (block) =>
                            {
                                var system = GetSystem(block);
                                if (system != null && !NanobotBuildAndRepairSystemMod.Settings.Welder.PriorityFixed)
                                {
                                    system.BlockGrindPriority.MoveSelectedUp();
                                    system.Settings.GrindPriority = system.BlockGrindPriority.GetEntries();
                                    UpdateVisual(_GrindPriorityListBox);
                                }
                            };
                            button.SupportsMultipleBlocks = true;
                            CustomControls.Add(button);

                            button = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlButton, IMyShipWelder>("GrindPriorityDown");
                            _GrindPriorityButtonDown = button;
                            button.Title = Texts.Priority_Down;
                            button.Visible = isGrindingAllowed;
                            button.Enabled = (block) =>
                            {
                                var system = GetSystem(block);
                                return system?.BlockGrindPriority?.Selected != null && isGrindingAllowed(block) && !NanobotBuildAndRepairSystemMod.Settings.Welder.PriorityFixed;
                            };
                            button.Action = (block) =>
                            {
                                var system = GetSystem(block);
                                if (system != null && !NanobotBuildAndRepairSystemMod.Settings.Welder.PriorityFixed)
                                {
                                    system.BlockGrindPriority.MoveSelectedDown();
                                    system.Settings.GrindPriority = system.BlockGrindPriority.GetEntries();
                                    UpdateVisual(_GrindPriorityListBox);
                                }
                            };
                            button.SupportsMultipleBlocks = true;
                            CustomControls.Add(button);

                            var listbox = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlListbox, IMyShipWelder>("GrindPriority");
                            _GrindPriorityListBox = listbox;

                            listbox.Multiselect = false;
                            listbox.VisibleRowsCount = 15;
                            listbox.Enabled = grindingAllowed ? isBaRSystem : isReadonly;
                            listbox.Visible = isGrindingAllowed;
                            listbox.ItemSelected = (block, selected) =>
                            {
                                var system = GetSystem(block);
                                if (system?.BlockGrindPriority != null)
                                {
                                    if (selected.Count > 0) system.BlockGrindPriority.SetSelectedByKey(((PrioItem)selected[0].UserData).Key);
                                    else system.BlockGrindPriority.ClearSelected();
                                    UpdateVisual(_GrindEnableDisableSwitch);
                                    UpdateVisual(_GrindPriorityButtonUp);
                                    UpdateVisual(_GrindPriorityButtonDown);
                                }
                            };
                            listbox.ListContent = (block, items, selected) =>
                            {
                                var system = GetSystem(block);
                                system?.BlockGrindPriority?.FillTerminalList(items, selected);
                            };
                            listbox.SupportsMultipleBlocks = true;
                            CustomControls.Add(listbox);

                            //--Grind order near/far/smallest grid
                            checkbox = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlCheckbox, IMyShipWelder>("GrindNearFirst");
                            checkbox.Title = Texts.GrindOrderNearest;
                            checkbox.Tooltip = Texts.GrindOrderNearest_Tooltip;
                            checkbox.OnText = MySpaceTexts.SwitchText_On;
                            checkbox.OffText = MySpaceTexts.SwitchText_Off;
                            checkbox.Enabled = grindingAllowed ? isBaRSystem : isReadonly;
                            checkbox.Visible = isGrindingAllowed;
                            checkbox.Getter = (block) =>
                            {
                                var system = GetSystem(block);
                                return system != null && (system.Settings.Flags & SyncBlockSettings.Settings.GrindNearFirst) != 0;
                            };
                            checkbox.Setter = (block, value) =>
                            {
                                var system = GetSystem(block);
                                if (system != null && isGrindingAllowed(block))
                                {
                                    //Only one option (GrindNearFirst or GrindSmallestGridFirst) at a time is allowed
                                    if (value)
                                    {
                                        system.Settings.Flags = (system.Settings.Flags & ~SyncBlockSettings.Settings.GrindSmallestGridFirst) | SyncBlockSettings.Settings.GrindNearFirst;
                                    }
                                    else
                                    {
                                        system.Settings.Flags = system.Settings.Flags & ~SyncBlockSettings.Settings.GrindNearFirst;
                                    }
                                    foreach (var ctrl in CustomControls)
                                    {
                                        if (ctrl.Id.Contains("GrindFarFirst")) ctrl.UpdateVisual();
                                        if (ctrl.Id.Contains("GrindSmallestGridFirst")) ctrl.UpdateVisual();
                                    }
                                }
                            };
                            checkbox.SupportsMultipleBlocks = true;
                            CreateCheckBoxAction("GrindNearFirst", checkbox);
                            CustomControls.Add(checkbox);
                            CreateProperty(checkbox);

                            checkbox = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlCheckbox, IMyShipWelder>("GrindFarFirst");
                            checkbox.Title = Texts.GrindOrderFurthest;
                            checkbox.Tooltip = Texts.GrindOrderFurthest_Tooltip;
                            checkbox.OnText = MySpaceTexts.SwitchText_On;
                            checkbox.OffText = MySpaceTexts.SwitchText_Off;
                            checkbox.Enabled = grindingAllowed ? isBaRSystem : isReadonly;
                            checkbox.Visible = isGrindingAllowed;
                            checkbox.Getter = (block) =>
                            {
                                var system = GetSystem(block);
                                return system != null && (system.Settings.Flags & (SyncBlockSettings.Settings.GrindNearFirst | SyncBlockSettings.Settings.GrindSmallestGridFirst)) == 0;
                            };
                            checkbox.Setter = (block, value) =>
                            {
                                var system = GetSystem(block);
                                if (system != null && isGrindingAllowed(block))
                                {
                                    //Only one option (GrindNearFirst or GrindSmallestGridFirst) at a time is allowed
                                    if (value)
                                    {
                                        system.Settings.Flags = system.Settings.Flags & ~(SyncBlockSettings.Settings.GrindSmallestGridFirst | SyncBlockSettings.Settings.GrindNearFirst);
                                    }
                                    foreach (var ctrl in CustomControls)
                                    {
                                        if (ctrl.Id.Contains("GrindNearFirst")) ctrl.UpdateVisual();
                                        if (ctrl.Id.Contains("GrindSmallestGridFirst")) ctrl.UpdateVisual();
                                    }
                                }
                            };
                            checkbox.SupportsMultipleBlocks = true;
                            CreateCheckBoxAction("GrindFarFirst", checkbox);
                            CustomControls.Add(checkbox);
                            CreateProperty(checkbox);

                            checkbox = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlCheckbox, IMyShipWelder>("GrindSmallestGridFirst");
                            checkbox.Title = Texts.GrindOrderSmallest;
                            checkbox.Tooltip = Texts.GrindOrderSmallest_Tooltip;
                            checkbox.OnText = MySpaceTexts.SwitchText_On;
                            checkbox.OffText = MySpaceTexts.SwitchText_Off;
                            checkbox.Enabled = grindingAllowed ? isBaRSystem : isReadonly;
                            checkbox.Visible = isGrindingAllowed;
                            checkbox.Getter = (block) =>
                            {
                                var system = GetSystem(block);
                                return system != null && (system.Settings.Flags & SyncBlockSettings.Settings.GrindSmallestGridFirst) != 0;
                            };
                            checkbox.Setter = (block, value) =>
                            {
                                var system = GetSystem(block);
                                if (system != null && isGrindingAllowed(block))
                                {
                                    //Only one option (GrindNearFirst or GrindSmallestGridFirst) at a time is allowed
                                    if (value)
                                    {
                                        system.Settings.Flags = (system.Settings.Flags & ~SyncBlockSettings.Settings.GrindNearFirst) | SyncBlockSettings.Settings.GrindSmallestGridFirst;
                                    }
                                    foreach (var ctrl in CustomControls)
                                    {
                                        if (ctrl.Id.Contains("GrindNearFirst")) ctrl.UpdateVisual();
                                        if (ctrl.Id.Contains("GrindFarFirst")) ctrl.UpdateVisual();
                                    }
                                }
                            };
                            checkbox.SupportsMultipleBlocks = true;
                            CreateCheckBoxAction("GrindSmallestGridFirst", checkbox);
                            CustomControls.Add(checkbox);
                            CreateProperty(checkbox);
                        }
                    }

                    // --- Collecting
                    label = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlLabel, IMyShipWelder>("CollectingSettings");
                    label.Label = Texts.CollectSettings_Headline;
                    CustomControls.Add(label);
                    {
                        // --- Collect floating objects
                        //separateArea = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlSeparator, IMyShipWelder>("SeparateCollectPrio");
                        //CustomControls.Add(separateArea);
                        {
                            onoffSwitch = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlOnOffSwitch, IMyShipWelder>("CollectPriority");
                            _ComponentCollectEnableDisableSwitch = onoffSwitch;
                            onoffSwitch.Title = Texts.CollectPriority;
                            onoffSwitch.Tooltip = Texts.CollectPriority_Tooltip;
                            onoffSwitch.OnText = Texts.Priority_Enable;
                            onoffSwitch.OffText = Texts.Priority_Disable;
                            onoffSwitch.Enabled = isChangeCollectPriorityPossible;
                            onoffSwitch.Getter = (block) =>
                            {
                                var system = GetSystem(block);
                                return system?.ComponentCollectPriority?.Selected != null && system.ComponentCollectPriority.GetEnabled(system.ComponentCollectPriority.Selected.Key);
                            };
                            onoffSwitch.Setter = (block, value) =>
                            {
                                var system = GetSystem(block);
                                if (system?.ComponentCollectPriority?.Selected != null && !NanobotBuildAndRepairSystemMod.Settings.Welder.CollectPriorityFixed)
                                {
                                    system.ComponentCollectPriority.SetEnabled(system.ComponentCollectPriority.Selected.Key, value);
                                    system.Settings.ComponentCollectPriority = system.ComponentCollectPriority.GetEntries();
                                    UpdateVisual(_ComponentCollectPriorityListBox);
                                }
                            };
                            onoffSwitch.SupportsMultipleBlocks = true;
                            CustomControls.Add(onoffSwitch);

                            button = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlButton, IMyShipWelder>("CollectPriorityUp");
                            _ComponentCollectPriorityButtonUp = button;
                            button.Title = Texts.Priority_Up;
                            button.Enabled = isChangeCollectPriorityPossible;
                            button.Action = (block) =>
                            {
                                var system = GetSystem(block);
                                if (system != null && !NanobotBuildAndRepairSystemMod.Settings.Welder.CollectPriorityFixed)
                                {
                                    system.ComponentCollectPriority.MoveSelectedUp();
                                    system.Settings.ComponentCollectPriority = system.ComponentCollectPriority.GetEntries();
                                    UpdateVisual(_ComponentCollectPriorityListBox);
                                }
                            };
                            button.SupportsMultipleBlocks = true;
                            CustomControls.Add(button);

                            button = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlButton, IMyShipWelder>("CollectPriorityDown");
                            _ComponentCollectPriorityButtonDown = button;
                            button.Title = Texts.Priority_Down;
                            button.Enabled = isChangeCollectPriorityPossible;
                            button.Action = (block) =>
                            {
                                var system = GetSystem(block);
                                if (system != null && !NanobotBuildAndRepairSystemMod.Settings.Welder.CollectPriorityFixed)
                                {
                                    system.ComponentCollectPriority.MoveSelectedDown();
                                    system.Settings.ComponentCollectPriority = system.ComponentCollectPriority.GetEntries();
                                    UpdateVisual(_ComponentCollectPriorityListBox);
                                }
                            };
                            button.SupportsMultipleBlocks = true;
                            CustomControls.Add(button);

                            var listbox = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlListbox, IMyShipWelder>("CollectPriority");
                            _ComponentCollectPriorityListBox = listbox;

                            listbox.Multiselect = false;
                            listbox.VisibleRowsCount = 5;
                            listbox.Enabled = isCollectPossible;
                            listbox.ItemSelected = (block, selected) =>
                            {
                                var system = GetSystem(block);
                                if (system?.ComponentCollectPriority != null)
                                {
                                    if (selected.Count > 0) system.ComponentCollectPriority.SetSelectedByKey(((PrioItem)selected[0].UserData).Key);
                                    else system.ComponentCollectPriority.ClearSelected();
                                    UpdateVisual(_ComponentCollectEnableDisableSwitch);
                                    UpdateVisual(_ComponentCollectPriorityButtonUp);
                                    UpdateVisual(_ComponentCollectPriorityButtonDown);
                                }
                            };
                            listbox.ListContent = (block, items, selected) =>
                            {
                                var system = GetSystem(block);
                                system?.ComponentCollectPriority?.FillTerminalList(items, selected);
                            };
                            listbox.SupportsMultipleBlocks = true;
                            CustomControls.Add(listbox);

                            // Collect if idle
                            checkbox = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlCheckbox, IMyShipWelder>("CollectIfIdle");
                            _ComponentCollectIfIdleSwitch = checkbox;
                            checkbox.Title = Texts.CollectOnlyIfIdle;
                            checkbox.Tooltip = Texts.CollectOnlyIfIdle_Tooltip;
                            checkbox.OnText = MySpaceTexts.SwitchText_On;
                            checkbox.OffText = MySpaceTexts.SwitchText_Off;
                            checkbox.Enabled = NanobotBuildAndRepairSystemMod.Settings.Welder.CollectIfIdleFixed ? isReadonly : isCollectPossible;
                            checkbox.Getter = (block) =>
                            {
                                var system = GetSystem(block);
                                return system != null && (system.Settings.Flags & SyncBlockSettings.Settings.ComponentCollectIfIdle) != 0;
                            };
                            checkbox.Setter = (block, value) =>
                            {
                                var system = GetSystem(block);
                                if (system != null && !NanobotBuildAndRepairSystemMod.Settings.Welder.CollectIfIdleFixed)
                                {
                                    system.Settings.Flags = (system.Settings.Flags & ~SyncBlockSettings.Settings.ComponentCollectIfIdle) | (value ? SyncBlockSettings.Settings.ComponentCollectIfIdle : 0);
                                }
                            };
                            checkbox.SupportsMultipleBlocks = true;
                            CreateCheckBoxAction("CollectIfIdle", checkbox);
                            CustomControls.Add(checkbox);
                            CreateProperty(checkbox, NanobotBuildAndRepairSystemMod.Settings.Welder.CollectIfIdleFixed);

                            //Push Ingot/ore immediately
                            checkbox = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlCheckbox, IMyShipWelder>("PushIngotOreImmediately");
                            checkbox.Title = Texts.CollectPushOre;
                            checkbox.Tooltip = Texts.CollectPushOre_Tooltip;
                            checkbox.OnText = MySpaceTexts.SwitchText_On;
                            checkbox.OffText = MySpaceTexts.SwitchText_Off;
                            checkbox.Enabled = NanobotBuildAndRepairSystemMod.Settings.Welder.PushIngotOreImmediatelyFixed ? isReadonly : isBaRSystem;
                            checkbox.Getter = (block) =>
                            {
                                var system = GetSystem(block);
                                return system != null && (system.Settings.Flags & SyncBlockSettings.Settings.PushIngotOreImmediately) != 0;
                            };
                            checkbox.Setter = (block, value) =>
                            {
                                var system = GetSystem(block);
                                if (system != null && !NanobotBuildAndRepairSystemMod.Settings.Welder.PushIngotOreImmediatelyFixed)
                                {
                                    system.Settings.Flags = (system.Settings.Flags & ~SyncBlockSettings.Settings.PushIngotOreImmediately) | (value ? SyncBlockSettings.Settings.PushIngotOreImmediately : 0);
                                }
                            };
                            checkbox.SupportsMultipleBlocks = true;
                            CreateCheckBoxAction("PushIngotOreImmediately", checkbox);
                            CustomControls.Add(checkbox);
                            CreateProperty(checkbox, NanobotBuildAndRepairSystemMod.Settings.Welder.PushIngotOreImmediatelyFixed);

                            //Push Items immediately
                            checkbox = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlCheckbox, IMyShipWelder>("PushItemsImmediately");
                            checkbox.Title = Texts.CollectPushItems;
                            checkbox.Tooltip = Texts.CollectPushItems_Tooltip;
                            checkbox.OnText = MySpaceTexts.SwitchText_On;
                            checkbox.OffText = MySpaceTexts.SwitchText_Off;
                            checkbox.Enabled = NanobotBuildAndRepairSystemMod.Settings.Welder.PushItemsImmediatelyFixed ? isReadonly : isBaRSystem;
                            checkbox.Getter = (block) =>
                            {
                                var system = GetSystem(block);
                                return system != null && (system.Settings.Flags & SyncBlockSettings.Settings.PushItemsImmediately) != 0;
                            };
                            checkbox.Setter = (block, value) =>
                            {
                                var system = GetSystem(block);
                                if (system != null && !NanobotBuildAndRepairSystemMod.Settings.Welder.PushItemsImmediatelyFixed)
                                {
                                    system.Settings.Flags = (system.Settings.Flags & ~SyncBlockSettings.Settings.PushItemsImmediately) | (value ? SyncBlockSettings.Settings.PushItemsImmediately : 0);
                                }
                            };
                            checkbox.SupportsMultipleBlocks = true;
                            CreateCheckBoxAction("PushItemsImmediately", checkbox);
                            CustomControls.Add(checkbox);
                            CreateProperty(checkbox, NanobotBuildAndRepairSystemMod.Settings.Welder.PushItemsImmediatelyFixed);

                            //Push Component immediately
                            checkbox = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlCheckbox, IMyShipWelder>("PushComponentImmediately");
                            checkbox.Title = Texts.CollectPushComp;
                            checkbox.Tooltip = Texts.CollectPushComp_Tooltip;
                            checkbox.OnText = MySpaceTexts.SwitchText_On;
                            checkbox.OffText = MySpaceTexts.SwitchText_Off;
                            checkbox.Enabled = NanobotBuildAndRepairSystemMod.Settings.Welder.PushComponentImmediatelyFixed ? isReadonly : isBaRSystem;
                            checkbox.Getter = (block) =>
                            {
                                var system = GetSystem(block);
                                return system != null && (system.Settings.Flags & SyncBlockSettings.Settings.PushComponentImmediately) != 0;
                            };
                            checkbox.Setter = (block, value) =>
                            {
                                var system = GetSystem(block);
                                if (system != null && !NanobotBuildAndRepairSystemMod.Settings.Welder.PushComponentImmediatelyFixed)
                                {
                                    system.Settings.Flags = (system.Settings.Flags & ~SyncBlockSettings.Settings.PushComponentImmediately) | (value ? SyncBlockSettings.Settings.PushComponentImmediately : 0);
                                }
                            };
                            checkbox.SupportsMultipleBlocks = true;
                            CreateCheckBoxAction("PushComponentImmediately", checkbox);
                            CustomControls.Add(checkbox);
                            CreateProperty(checkbox, NanobotBuildAndRepairSystemMod.Settings.Welder.PushComponentImmediatelyFixed);
                        }
                    }

                    // -- Highlight Area
                    separateArea = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlSeparator, IMyShipWelder>("SeparateArea");
                    CustomControls.Add(separateArea);
                    {
                        Func<IMyTerminalBlock, float> getLimitOffsetMin = (block) =>
                        {
                            var system = GetSystem(block);
                            return -system?.Settings?.MaximumOffset ?? -Constants.WELDER_OFFSET_MAX_IN_M;
                        };
                        Func<IMyTerminalBlock, float> getLimitOffsetMax = (block) =>
                        {
                            var system = GetSystem(block);
                            return system?.Settings?.MaximumOffset ?? Constants.WELDER_OFFSET_MAX_IN_M;
                        };

                        Func<IMyTerminalBlock, float> getLimitMin = (block) => Constants.WELDER_RANGE_MIN_IN_M;
                        Func<IMyTerminalBlock, float> getLimitMax = (block) =>
                        {
                            var system = GetSystem(block);
                            return system?.Settings?.MaximumRange ?? Constants.WELDER_RANGE_MAX_IN_M;
                        };

                        checkbox = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlCheckbox, IMyShipWelder>("ShowArea");
                        checkbox.Title = Texts.AreaShow;
                        checkbox.Tooltip = Texts.AreaShow_Tooltip;
                        checkbox.OnText = MySpaceTexts.SwitchText_On;
                        checkbox.OffText = MySpaceTexts.SwitchText_Off;
                        checkbox.Enabled = NanobotBuildAndRepairSystemMod.Settings.Welder.ShowAreaFixed ? isReadonly : isBaRSystem;
                        checkbox.Getter = (block) =>
                        {
                            var system = GetSystem(block);
                            if (system != null)
                            {
                                return (system.Settings.Flags & SyncBlockSettings.Settings.ShowArea) != 0;
                            }

                            return false;
                        };
                        checkbox.Setter = (block, value) =>
                        {
                            var system = GetSystem(block);
                            if (system != null && !NanobotBuildAndRepairSystemMod.Settings.Welder.ShowAreaFixed)
                            {
                                system.Settings.Flags = (system.Settings.Flags & ~SyncBlockSettings.Settings.ShowArea) | (value ? SyncBlockSettings.Settings.ShowArea : 0);
                            }
                        };
                        checkbox.SupportsMultipleBlocks = true;
                        CreateCheckBoxAction("ShowArea", checkbox);
                        CustomControls.Add(checkbox);
                        CreateProperty(checkbox, NanobotBuildAndRepairSystemMod.Settings.Welder.ShowAreaFixed);

                        //Slider Offset
                        slider = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlSlider, IMyShipWelder>("AreaOffsetLeftRight");
                        slider.Title = MySpaceTexts.BlockPropertyTitle_ProjectionOffsetX;
                        slider.SetLimits(getLimitOffsetMin, getLimitOffsetMax);
                        slider.Enabled = NanobotBuildAndRepairSystemMod.Settings.Welder.AreaOffsetFixed ? isReadonly : isBaRSystem;
                        slider.Getter = (block) =>
                        {
                            var system = GetSystem(block);
                            return system != null ? system.Settings.AreaOffset.X : 0;
                        };
                        slider.Setter = (block, val) =>
                        {
                            var system = GetSystem(block);
                            if (system != null)
                            {
                                var min = getLimitOffsetMin(block);
                                var max = getLimitOffsetMax(block);
                                val = (float)Math.Round(val * 2, MidpointRounding.AwayFromZero) / 2f;
                                val = val < min ? min : val > max ? max : val;
                                system.Settings.AreaOffset = new Vector3(val, system.Settings.AreaOffset.Y, system.Settings.AreaOffset.Z);
                            }
                        };
                        slider.Writer = (block, val) =>
                        {
                            var system = GetSystem(block);
                            if (system != null)
                            {
                                val.Append(system.Settings.AreaOffset.X + " m");
                            }
                        };
                        slider.SupportsMultipleBlocks = true;
                        CustomControls.Add(slider);
                        CreateSliderActions("AreaOffsetLeftRight", slider);
                        CreateProperty(slider, NanobotBuildAndRepairSystemMod.Settings.Welder.AreaOffsetFixed);

                        slider = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlSlider, IMyShipWelder>("AreaOffsetUpDown");
                        slider.Title = MySpaceTexts.BlockPropertyTitle_ProjectionOffsetY;
                        slider.SetLimits(getLimitOffsetMin, getLimitOffsetMax);
                        slider.Enabled = NanobotBuildAndRepairSystemMod.Settings.Welder.AreaOffsetFixed ? isReadonly : isBaRSystem;
                        slider.Getter = (block) =>
                        {
                            var system = GetSystem(block);
                            return system != null ? system.Settings.AreaOffset.Y : 0;
                        };
                        slider.Setter = (block, val) =>
                        {
                            var system = GetSystem(block);
                            if (system != null)
                            {
                                var min = getLimitOffsetMin(block);
                                var max = getLimitOffsetMax(block);
                                val = (float)Math.Round(val * 2, MidpointRounding.AwayFromZero) / 2f;
                                val = val < min ? min : val > max ? max : val;
                                system.Settings.AreaOffset = new Vector3(system.Settings.AreaOffset.X, val, system.Settings.AreaOffset.Z);
                            }
                        };
                        slider.Writer = (block, val) =>
                        {
                            var system = GetSystem(block);
                            if (system != null)
                            {
                                val.Append(system.Settings.AreaOffset.Y + " m");
                            }
                        };
                        slider.SupportsMultipleBlocks = true;
                        CustomControls.Add(slider);
                        CreateSliderActions("AreaOffsetUpDown", slider);
                        CreateProperty(slider, NanobotBuildAndRepairSystemMod.Settings.Welder.AreaOffsetFixed);

                        slider = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlSlider, IMyShipWelder>("AreaOffsetFrontBack");
                        slider.Title = MySpaceTexts.BlockPropertyTitle_ProjectionOffsetZ;
                        slider.SetLimits(getLimitOffsetMin, getLimitOffsetMax);
                        slider.Enabled = NanobotBuildAndRepairSystemMod.Settings.Welder.AreaOffsetFixed ? isReadonly : isBaRSystem;
                        slider.Getter = (block) =>
                        {
                            var system = GetSystem(block);
                            return system != null ? system.Settings.AreaOffset.Z : 0;
                        };
                        slider.Setter = (block, val) =>
                        {
                            var system = GetSystem(block);
                            if (system != null)
                            {
                                var min = getLimitOffsetMin(block);
                                var max = getLimitOffsetMax(block);
                                val = (float)Math.Round(val * 2, MidpointRounding.AwayFromZero) / 2f;
                                val = val < min ? min : val > max ? max : val;
                                system.Settings.AreaOffset = new Vector3(system.Settings.AreaOffset.X, system.Settings.AreaOffset.Y, val);
                            }
                        };
                        slider.Writer = (block, val) =>
                        {
                            var system = GetSystem(block);
                            if (system != null)
                            {
                                val.Append(system.Settings.AreaOffset.Z + " m");
                            }
                        };
                        slider.SupportsMultipleBlocks = true;
                        CustomControls.Add(slider);
                        CreateSliderActions("AreaOffsetFrontBack", slider);
                        CreateProperty(slider, NanobotBuildAndRepairSystemMod.Settings.Welder.AreaOffsetFixed);

                        //Slider Area
                        slider = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlSlider, IMyShipWelder>("AreaWidth");
                        slider.Title = Texts.AreaWidth;
                        slider.SetLimits(getLimitMin, getLimitMax);
                        slider.Enabled = NanobotBuildAndRepairSystemMod.Settings.Welder.AreaSizeFixed ? isReadonly : isBaRSystem;
                        slider.Getter = (block) =>
                        {
                            var system = GetSystem(block);
                            return system != null ? system.Settings.AreaSize.X : 0;
                        };
                        slider.Setter = (block, val) =>
                        {
                            var system = GetSystem(block);
                            if (system != null)
                            {
                                var min = getLimitMin(block);
                                var max = getLimitMax(block);
                                val = val < min ? min : val > max ? max : val;
                                system.Settings.AreaSize = new Vector3((int)Math.Round(val), system.Settings.AreaSize.Y, system.Settings.AreaSize.Z);
                            }
                        };
                        slider.Writer = (block, val) =>
                        {
                            var system = GetSystem(block);
                            if (system != null)
                            {
                                val.Append(system.Settings.AreaSize.X + " m");
                            }
                        };
                        slider.SupportsMultipleBlocks = true;
                        CustomControls.Add(slider);
                        CreateSliderActions("AreaWidth", slider);
                        CreateProperty(slider, NanobotBuildAndRepairSystemMod.Settings.Welder.AreaSizeFixed);

                        slider = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlSlider, IMyShipWelder>("AreaHeight");
                        slider.Title = Texts.AreaHeight;
                        slider.SetLimits(getLimitMin, getLimitMax);
                        slider.Enabled = NanobotBuildAndRepairSystemMod.Settings.Welder.AreaSizeFixed ? isReadonly : isBaRSystem;
                        slider.Getter = (block) =>
                        {
                            var system = GetSystem(block);
                            return system != null ? system.Settings.AreaSize.Y : 0;
                        };
                        slider.Setter = (block, val) =>
                        {
                            var system = GetSystem(block);
                            if (system != null)
                            {
                                var min = getLimitMin(block);
                                var max = getLimitMax(block);
                                val = val < min ? min : val > max ? max : val;
                                system.Settings.AreaSize = new Vector3(system.Settings.AreaSize.X, (int)Math.Round(val), system.Settings.AreaSize.Z);
                            }
                        };
                        slider.Writer = (block, val) =>
                        {
                            var system = GetSystem(block);
                            if (system != null)
                            {
                                val.Append(system.Settings.AreaSize.Y + " m");
                            }
                        };
                        slider.SupportsMultipleBlocks = true;
                        CustomControls.Add(slider);
                        CreateSliderActions("AreaHeight", slider);
                        CreateProperty(slider, NanobotBuildAndRepairSystemMod.Settings.Welder.AreaSizeFixed);

                        slider = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlSlider, IMyShipWelder>("AreaDepth");
                        slider.Title = Texts.AreaDepth;
                        slider.SetLimits(getLimitMin, getLimitMax);
                        slider.Enabled = NanobotBuildAndRepairSystemMod.Settings.Welder.AreaSizeFixed ? isReadonly : isBaRSystem;
                        slider.Getter = (block) =>
                        {
                            var system = GetSystem(block);
                            return system != null ? system.Settings.AreaSize.Z : 0;
                        };
                        slider.Setter = (block, val) =>
                        {
                            var system = GetSystem(block);
                            if (system != null)
                            {
                                var min = getLimitMin(block);
                                var max = getLimitMax(block);
                                val = val < min ? min : val > max ? max : val;
                                system.Settings.AreaSize = new Vector3(system.Settings.AreaSize.X, system.Settings.AreaSize.Y, (int)Math.Round(val));
                            }
                        };
                        slider.Writer = (block, val) =>
                        {
                            var system = GetSystem(block);
                            if (system != null)
                            {
                                val.Append(system.Settings.AreaSize.Z + " m");
                            }
                        };
                        slider.SupportsMultipleBlocks = true;
                        CustomControls.Add(slider);
                        CreateSliderActions("AreaDepth", slider);
                        CreateProperty(slider, NanobotBuildAndRepairSystemMod.Settings.Welder.AreaSizeFixed);

                        // -- Sound enabled
                        separateArea = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlSeparator, IMyShipWelder>("SeparateOther");
                        CustomControls.Add(separateArea);

                        slider = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlSlider, IMyShipWelder>("SoundVolume");
                        slider.Title = Texts.SoundVolume;
                        slider.SetLimits(0f, 100f);
                        slider.Enabled = NanobotBuildAndRepairSystemMod.Settings.Welder.SoundVolumeFixed ? isReadonly : isBaRSystem;
                        slider.Getter = (block) =>
                        {
                            var system = GetSystem(block);
                            return system != null ? 100f * system.Settings.SoundVolume / Constants.WELDER_SOUND_VOLUME : 0f;
                        };
                        slider.Setter = (block, val) =>
                        {
                            var system = GetSystem(block);
                            if (system != null)
                            {
                                var min = 0;
                                var max = 100;
                                val = val < min ? min : val > max ? max : val;
                                system.Settings.SoundVolume = (float)Math.Round(val * Constants.WELDER_SOUND_VOLUME) / 100f;
                            }
                        };
                        slider.Writer = (block, val) =>
                        {
                            var system = GetSystem(block);
                            if (system != null)
                            {
                                val.Append(Math.Round(100f * system.Settings.SoundVolume / Constants.WELDER_SOUND_VOLUME) + " %");
                            }
                        };
                        slider.SupportsMultipleBlocks = true;
                        CustomControls.Add(slider);
                        CreateSliderActions("SoundVolume", slider);
                        CreateProperty(slider, NanobotBuildAndRepairSystemMod.Settings.Welder.SoundVolumeFixed);
                    }

                    // -- Script Control
                    if (!NanobotBuildAndRepairSystemMod.Settings.Welder.ScriptControllFixed)
                    {
                        separateArea = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlSeparator, IMyShipWelder>("SeparateScriptControl");
                        CustomControls.Add(separateArea);

                        checkbox = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlCheckbox, IMyShipWelder>("ScriptControlled");
                        checkbox.Title = Texts.ScriptControlled;
                        checkbox.Tooltip = Texts.ScriptControlled_Tooltip;
                        checkbox.OnText = MySpaceTexts.SwitchText_On;
                        checkbox.OffText = MySpaceTexts.SwitchText_Off;
                        checkbox.Enabled = isBaRSystem;
                        checkbox.Getter = (block) =>
                        {
                            var system = GetSystem(block);
                            return system != null && (system.Settings.Flags & SyncBlockSettings.Settings.ScriptControlled) != 0;
                        };
                        checkbox.Setter = (block, value) =>
                        {
                            var system = GetSystem(block);
                            if (system != null)
                            {
                                system.Settings.Flags = (system.Settings.Flags & ~SyncBlockSettings.Settings.ScriptControlled) | (value ? SyncBlockSettings.Settings.ScriptControlled : 0);
                            }
                        };
                        checkbox.SupportsMultipleBlocks = true;
                        CreateCheckBoxAction("ScriptControlled", checkbox);
                        CustomControls.Add(checkbox);
                        CreateProperty(checkbox);

                        //Scripting support for Priority and enabling Weld BlockClasses
                        var propertyWeldPriorityList = MyAPIGateway.TerminalControls.CreateProperty<List<string>, IMyShipWelder>("BuildAndRepair.WeldPriorityList");
                        propertyWeldPriorityList.SupportsMultipleBlocks = false;
                        propertyWeldPriorityList.Getter = (block) =>
                        {
                            var system = GetSystem(block);
                            return system?.BlockWeldPriority.GetList();
                        };
                        MyAPIGateway.TerminalControls.AddControl<IMyShipWelder>(propertyWeldPriorityList);

                        var propertySWP = MyAPIGateway.TerminalControls.CreateProperty<Action<int, int>, IMyShipWelder>("BuildAndRepair.SetWeldPriority");
                        propertySWP.SupportsMultipleBlocks = false;
                        propertySWP.Getter = (block) =>
                        {
                            var system = GetSystem(block);
                            if (system != null)
                            {
                                return system.BlockWeldPriority.SetPriority;
                            }
                            return null;
                        };
                        MyAPIGateway.TerminalControls.AddControl<IMyShipWelder>(propertySWP);

                        var propertyGWP = MyAPIGateway.TerminalControls.CreateProperty<Func<int, int>, IMyShipWelder>("BuildAndRepair.GetWeldPriority");
                        propertyGWP.SupportsMultipleBlocks = false;
                        propertyGWP.Getter = (block) =>
                        {
                            var system = GetSystem(block);
                            if (system != null)
                            {
                                return system.BlockWeldPriority.GetPriority;
                            }
                            return null;
                        };
                        MyAPIGateway.TerminalControls.AddControl<IMyShipWelder>(propertyGWP);

                        var propertySWE = MyAPIGateway.TerminalControls.CreateProperty<Action<int, bool>, IMyShipWelder>("BuildAndRepair.SetWeldEnabled");
                        propertySWE.SupportsMultipleBlocks = false;
                        propertySWE.Getter = (block) =>
                        {
                            var system = GetSystem(block);
                            if (system != null)
                            {
                                return system.BlockWeldPriority.SetEnabled;
                            }
                            return null;
                        };
                        MyAPIGateway.TerminalControls.AddControl<IMyShipWelder>(propertySWE);

                        var propertyGWE = MyAPIGateway.TerminalControls.CreateProperty<Func<int, bool>, IMyShipWelder>("BuildAndRepair.GetWeldEnabled");
                        propertyGWE.SupportsMultipleBlocks = false;
                        propertyGWE.Getter = (block) =>
                        {
                            var system = GetSystem(block);
                            if (system != null)
                            {
                                return system.BlockWeldPriority.GetEnabled;
                            }
                            return null;
                        };
                        MyAPIGateway.TerminalControls.AddControl<IMyShipWelder>(propertyGWE);

                        //Scripting support for Priority and enabling GrindWeld BlockClasses
                        var propertyGrindPriorityList = MyAPIGateway.TerminalControls.CreateProperty<List<string>, IMyShipWelder>("BuildAndRepair.GrindPriorityList");
                        propertyGrindPriorityList.SupportsMultipleBlocks = false;
                        propertyGrindPriorityList.Getter = (block) =>
                        {
                            var system = GetSystem(block);
                            return system?.BlockGrindPriority.GetList();
                        };
                        MyAPIGateway.TerminalControls.AddControl<IMyShipWelder>(propertyGrindPriorityList);

                        var propertySGP = MyAPIGateway.TerminalControls.CreateProperty<Action<int, int>, IMyShipWelder>("BuildAndRepair.SetGrindPriority");
                        propertySGP.SupportsMultipleBlocks = false;
                        propertySGP.Getter = (block) =>
                        {
                            var system = GetSystem(block);
                            if (system != null)
                            {
                                return system.BlockGrindPriority.SetPriority;
                            }
                            return null;
                        };
                        MyAPIGateway.TerminalControls.AddControl<IMyShipWelder>(propertySGP);

                        var propertyGGP = MyAPIGateway.TerminalControls.CreateProperty<Func<int, int>, IMyShipWelder>("BuildAndRepair.GetGrindPriority");
                        propertyGGP.SupportsMultipleBlocks = false;
                        propertyGGP.Getter = (block) =>
                        {
                            var system = GetSystem(block);
                            if (system != null)
                            {
                                return system.BlockGrindPriority.GetPriority;
                            }
                            return null;
                        };
                        MyAPIGateway.TerminalControls.AddControl<IMyShipWelder>(propertyGGP);

                        var propertySGE = MyAPIGateway.TerminalControls.CreateProperty<Action<int, bool>, IMyShipWelder>("BuildAndRepair.SetGrindEnabled");
                        propertySGE.SupportsMultipleBlocks = false;
                        propertySGE.Getter = (block) =>
                        {
                            var system = GetSystem(block);
                            if (system != null)
                            {
                                return system.BlockGrindPriority.SetEnabled;
                            }
                            return null;
                        };
                        MyAPIGateway.TerminalControls.AddControl<IMyShipWelder>(propertySGE);

                        var propertyGGE = MyAPIGateway.TerminalControls.CreateProperty<Func<int, bool>, IMyShipWelder>("BuildAndRepair.GetGrindEnabled");
                        propertyGGE.SupportsMultipleBlocks = false;
                        propertyGGE.Getter = (block) =>
                        {
                            var system = GetSystem(block);
                            if (system != null)
                            {
                                return system.BlockGrindPriority.GetEnabled;
                            }
                            return null;
                        };
                        MyAPIGateway.TerminalControls.AddControl<IMyShipWelder>(propertyGGE);

                        //Scripting support for Priority and enabling ComponentClasses
                        var propertyComponentClassList = MyAPIGateway.TerminalControls.CreateProperty<List<string>, IMyShipWelder>("BuildAndRepair.ComponentClassList");
                        propertyComponentClassList.SupportsMultipleBlocks = false;
                        propertyComponentClassList.Getter = (block) =>
                        {
                            var system = GetSystem(block);
                            return system?.ComponentCollectPriority.GetList();
                        };
                        MyAPIGateway.TerminalControls.AddControl<IMyShipWelder>(propertyComponentClassList);

                        var propertySPC = MyAPIGateway.TerminalControls.CreateProperty<Action<int, int>, IMyShipWelder>("BuildAndRepair.SetCollectPriority");
                        propertySPC.SupportsMultipleBlocks = false;
                        propertySPC.Getter = (block) =>
                        {
                            var system = GetSystem(block);
                            if (system != null)
                            {
                                return system.ComponentCollectPriority.SetPriority;
                            }
                            return null;
                        };
                        MyAPIGateway.TerminalControls.AddControl<IMyShipWelder>(propertySPC);

                        var propertyGPC = MyAPIGateway.TerminalControls.CreateProperty<Func<int, int>, IMyShipWelder>("BuildAndRepair.GetCollectPriority");
                        propertyGPC.SupportsMultipleBlocks = false;
                        propertyGPC.Getter = (block) =>
                        {
                            var system = GetSystem(block);
                            if (system != null)
                            {
                                return system.ComponentCollectPriority.GetPriority;
                            }
                            return null;
                        };
                        MyAPIGateway.TerminalControls.AddControl<IMyShipWelder>(propertyGPC);

                        var propertySEC = MyAPIGateway.TerminalControls.CreateProperty<Action<int, bool>, IMyShipWelder>("BuildAndRepair.SetCollectEnabled");
                        propertySEC.SupportsMultipleBlocks = false;
                        propertySEC.Getter = (block) =>
                        {
                            var system = GetSystem(block);
                            if (system != null)
                            {
                                return system.ComponentCollectPriority.SetEnabled;
                            }
                            return null;
                        };
                        MyAPIGateway.TerminalControls.AddControl<IMyShipWelder>(propertySEC);

                        var propertyGEC = MyAPIGateway.TerminalControls.CreateProperty<Func<int, bool>, IMyShipWelder>("BuildAndRepair.GetCollectEnabled");
                        propertyGEC.SupportsMultipleBlocks = false;
                        propertyGEC.Getter = (block) =>
                        {
                            var system = GetSystem(block);
                            if (system != null)
                            {
                                return system.ComponentCollectPriority.GetEnabled;
                            }
                            return null;
                        };
                        MyAPIGateway.TerminalControls.AddControl<IMyShipWelder>(propertyGEC);

                        //Working Lists
                        var propertyMissingComponentsDict = MyAPIGateway.TerminalControls.CreateProperty<Dictionary<VRage.Game.MyDefinitionId, int>, IMyShipWelder>("BuildAndRepair.MissingComponents");
                        propertyMissingComponentsDict.SupportsMultipleBlocks = false;
                        propertyMissingComponentsDict.Getter = (block) =>
                        {
                            var system = GetSystem(block);
                            return system?.GetMissingComponentsDict();
                        };
                        MyAPIGateway.TerminalControls.AddControl<IMyShipWelder>(propertyMissingComponentsDict);

                        var propertyPossibleWeldTargetsList = MyAPIGateway.TerminalControls.CreateProperty<List<VRage.Game.ModAPI.Ingame.IMySlimBlock>, IMyShipWelder>("BuildAndRepair.PossibleTargets");
                        propertyPossibleWeldTargetsList.SupportsMultipleBlocks = false;
                        propertyPossibleWeldTargetsList.Getter = (block) =>
                        {
                            var system = GetSystem(block);
                            return system?.GetPossibleWeldTargetsList();
                        };
                        MyAPIGateway.TerminalControls.AddControl<IMyShipWelder>(propertyPossibleWeldTargetsList);

                        var propertyPossibleGrindTargetsList = MyAPIGateway.TerminalControls.CreateProperty<List<VRage.Game.ModAPI.Ingame.IMySlimBlock>, IMyShipWelder>("BuildAndRepair.PossibleGrindTargets");
                        propertyPossibleGrindTargetsList.SupportsMultipleBlocks = false;
                        propertyPossibleGrindTargetsList.Getter = (block) =>
                        {
                            var system = GetSystem(block);
                            return system?.GetPossibleGrindTargetsList();
                        };
                        MyAPIGateway.TerminalControls.AddControl<IMyShipWelder>(propertyPossibleGrindTargetsList);

                        var propertyPossibleCollectTargetsList = MyAPIGateway.TerminalControls.CreateProperty<List<VRage.Game.ModAPI.Ingame.IMyEntity>, IMyShipWelder>("BuildAndRepair.PossibleCollectTargets");
                        propertyPossibleCollectTargetsList.SupportsMultipleBlocks = false;
                        propertyPossibleCollectTargetsList.Getter = (block) =>
                        {
                            var system = GetSystem(block);
                            return system?.GetPossibleCollectingTargetsList();
                        };
                        MyAPIGateway.TerminalControls.AddControl<IMyShipWelder>(propertyPossibleCollectTargetsList);

                        //Control welding
                        var propertyCPT = MyAPIGateway.TerminalControls.CreateProperty<VRage.Game.ModAPI.Ingame.IMySlimBlock, IMyShipWelder>("BuildAndRepair.CurrentPickedTarget");
                        propertyCPT.SupportsMultipleBlocks = false;
                        propertyCPT.Getter = (block) =>
                        {
                            var system = GetSystem(block);
                            return system?.Settings.CurrentPickedWeldingBlock;
                        };
                        propertyCPT.Setter = (block, value) =>
                        {
                            var system = GetSystem(block);
                            if (system != null)
                            {
                                system.Settings.CurrentPickedWeldingBlock = value;
                            }
                        };
                        MyAPIGateway.TerminalControls.AddControl<IMyShipWelder>(propertyCPT);

                        var propertyCT = MyAPIGateway.TerminalControls.CreateProperty<VRage.Game.ModAPI.Ingame.IMySlimBlock, IMyShipWelder>("BuildAndRepair.CurrentTarget");
                        propertyCT.SupportsMultipleBlocks = false;
                        propertyCT.Getter = (block) =>
                        {
                            var system = GetSystem(block);
                            return system?.State.CurrentWeldingBlock;
                        };
                        MyAPIGateway.TerminalControls.AddControl<IMyShipWelder>(propertyCT);

                        //Control grinding
                        var propertyCPGT = MyAPIGateway.TerminalControls.CreateProperty<VRage.Game.ModAPI.Ingame.IMySlimBlock, IMyShipWelder>("BuildAndRepair.CurrentPickedGrindTarget");
                        propertyCPGT.SupportsMultipleBlocks = false;
                        propertyCPGT.Getter = (block) =>
                        {
                            var system = GetSystem(block);
                            return system?.Settings.CurrentPickedGrindingBlock;
                        };
                        propertyCPGT.Setter = (block, value) =>
                        {
                            var system = GetSystem(block);
                            if (system != null)
                            {
                                system.Settings.CurrentPickedGrindingBlock = value;
                            }
                        };
                        MyAPIGateway.TerminalControls.AddControl<IMyShipWelder>(propertyCPGT);

                        var propertyCGT = MyAPIGateway.TerminalControls.CreateProperty<VRage.Game.ModAPI.Ingame.IMySlimBlock, IMyShipWelder>("BuildAndRepair.CurrentGrindTarget");
                        propertyCGT.SupportsMultipleBlocks = false;
                        propertyCGT.Getter = (block) =>
                        {
                            var system = GetSystem(block);
                            return system?.State.CurrentGrindingBlock;
                        };
                        MyAPIGateway.TerminalControls.AddControl<IMyShipWelder>(propertyCGT);

                        //Publish functions to scripting
                        var propertyPEQ = MyAPIGateway.TerminalControls.CreateProperty<Func<IEnumerable<long>, VRage.Game.MyDefinitionId, int, int>, IMyShipWelder>("BuildAndRepair.ProductionBlock.EnsureQueued");
                        propertyPEQ.SupportsMultipleBlocks = false;
                        propertyPEQ.Getter = (block) => UtilsProductionBlock.EnsureQueued;
                        MyAPIGateway.TerminalControls.AddControl<IMyShipWelder>(propertyPEQ);

                        var propertyNC4B = MyAPIGateway.TerminalControls.CreateProperty<Func<Sandbox.ModAPI.Ingame.IMyProjector, Dictionary<VRage.Game.MyDefinitionId, VRage.MyFixedPoint>, int>, IMyShipWelder>("BuildAndRepair.Inventory.NeededComponents4Blueprint");
                        propertyNC4B.SupportsMultipleBlocks = false;
                        propertyNC4B.Getter = (block) => UtilsInventory.NeededComponents4Blueprint;
                        MyAPIGateway.TerminalControls.AddControl<IMyShipWelder>(propertyNC4B);
                    }
                }
                catch (Exception ex)
                {
                    Logging.Instance?.Write(Logging.Level.Error, "NanobotBuildAndRepairSystemTerminal: InitializeControls exception: {0}", ex);
                }
            }
        }

        private static void CreateCheckBoxAction(string name, IMyTerminalControlCheckbox checkbox)
        {
            var action = MyAPIGateway.TerminalControls.CreateAction<IMyShipWelder>($"{name}_OnOff");
            action.Name = new StringBuilder($"{name} On/Off");
            action.Icon = @"Textures\GUI\Icons\Actions\Toggle.dds";
            action.Enabled = checkbox.Enabled;
            action.Action = (block) =>
            {
                checkbox.Setter(block, !checkbox.Getter(block));
            };
            action.Writer = (block, result) =>
            {
                result.Append(checkbox.Getter(block) ? MyTexts.Get(checkbox.OnText) : MyTexts.Get(checkbox.OffText));
            };
            action.ValidForGroups = checkbox.SupportsMultipleBlocks;
            MyAPIGateway.TerminalControls.AddAction<IMyShipWelder>(action);

            action = MyAPIGateway.TerminalControls.CreateAction<IMyShipWelder>($"{name}_On");
            action.Name = new StringBuilder($"{name} On");
            action.Icon = @"Textures\GUI\Icons\Actions\SwitchOn.dds";
            action.Enabled = checkbox.Enabled;
            action.Action = (block) =>
            {
                checkbox.Setter(block, true);
            };
            action.Writer = (block, result) =>
            {
                result.Append(checkbox.Getter(block) ? MyTexts.Get(checkbox.OnText) : MyTexts.Get(checkbox.OffText));
            };
            action.ValidForGroups = checkbox.SupportsMultipleBlocks;
            MyAPIGateway.TerminalControls.AddAction<IMyShipWelder>(action);

            action = MyAPIGateway.TerminalControls.CreateAction<IMyShipWelder>($"{name}_Off");
            action.Name = new StringBuilder($"{name} Off");
            action.Icon = @"Textures\GUI\Icons\Actions\SwitchOff.dds";
            action.Enabled = checkbox.Enabled;
            action.Action = (block) =>
            {
                checkbox.Setter(block, false);
            };
            action.Writer = (block, result) =>
            {
                result.Append(checkbox.Getter(block) ? MyTexts.Get(checkbox.OnText) : MyTexts.Get(checkbox.OffText));
            };
            action.ValidForGroups = checkbox.SupportsMultipleBlocks;
            MyAPIGateway.TerminalControls.AddAction<IMyShipWelder>(action);
        }

        private static void CreateOnOffSwitchAction(string name, IMyTerminalControlOnOffSwitch onoffSwitch)
        {
            var action = MyAPIGateway.TerminalControls.CreateAction<IMyShipWelder>($"{name}_OnOff");
            action.Name = new StringBuilder($"{name} {onoffSwitch.OnText}/{onoffSwitch.OffText}");
            action.Icon = @"Textures\GUI\Icons\Actions\Toggle.dds";
            action.Enabled = onoffSwitch.Enabled;
            action.Action = (block) =>
            {
                onoffSwitch.Setter(block, !onoffSwitch.Getter(block));
            };
            action.ValidForGroups = onoffSwitch.SupportsMultipleBlocks;
            MyAPIGateway.TerminalControls.AddAction<IMyShipWelder>(action);

            action = MyAPIGateway.TerminalControls.CreateAction<IMyShipWelder>($"{name}_On");
            action.Name = new StringBuilder($"{name} {onoffSwitch.OnText}");
            action.Icon = @"Textures\GUI\Icons\Actions\SwitchOn.dds";
            action.Enabled = onoffSwitch.Enabled;
            action.Action = (block) =>
            {
                onoffSwitch.Setter(block, true);
            };
            action.ValidForGroups = onoffSwitch.SupportsMultipleBlocks;
            MyAPIGateway.TerminalControls.AddAction<IMyShipWelder>(action);

            action = MyAPIGateway.TerminalControls.CreateAction<IMyShipWelder>($"{name}_Off");
            action.Name = new StringBuilder($"{name} {onoffSwitch.OffText}");
            action.Icon = @"Textures\GUI\Icons\Actions\SwitchOff.dds";
            action.Enabled = onoffSwitch.Enabled;
            action.Action = (block) =>
            {
                onoffSwitch.Setter(block, false);
            };
            action.ValidForGroups = onoffSwitch.SupportsMultipleBlocks;
            MyAPIGateway.TerminalControls.AddAction<IMyShipWelder>(action);
        }

        private static void CreateProperty<T>(IMyTerminalValueControl<T> control, bool readOnly = false)
        {
            var property = MyAPIGateway.TerminalControls.CreateProperty<T, IMyShipWelder>("BuildAndRepair." + control.Id);
            property.SupportsMultipleBlocks = false;
            property.Getter = control.Getter;
            if (!readOnly) property.Setter = control.Setter;
            MyAPIGateway.TerminalControls.AddControl<IMyShipWelder>(property);
        }

        private static void CreateSliderActions(string sliderName, IMyTerminalControlSlider slider)
        {
            var action = MyAPIGateway.TerminalControls.CreateAction<IMyShipWelder>($"{sliderName}_Increase");
            action.Name = new StringBuilder($"{sliderName} Increase");
            action.Icon = @"Textures\GUI\Icons\Actions\Increase.dds";
            action.Enabled = slider.Enabled;
            action.Action = (block) =>
            {
                var val = slider.Getter(block);
                slider.Setter(block, val + 1);
            };
            action.ValidForGroups = slider.SupportsMultipleBlocks;
            MyAPIGateway.TerminalControls.AddAction<IMyShipWelder>(action);

            action = MyAPIGateway.TerminalControls.CreateAction<IMyShipWelder>($"{sliderName}_Decrease");
            action.Name = new StringBuilder($"{sliderName} Decrease");
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

        private static Vector3 CheckConvertToHSVColor(Vector3 value)
        {
            if (value.X < 0f) value.X = 0f;
            if (value.X > 360f) value.X = 360f;
            if (value.Y < 0f) value.Y = 0f;
            if (value.Y > 100f) value.Y = 100f;
            if (value.Z < 0f) value.Z = 0f;
            if (value.Z > 100f) value.Z = 100f;

            return new Vector3(value.X / 360f,
                              value.Y / 100f - NanobotBuildAndRepairSystemTerminal.SATURATION_DELTA,
                              value.Z / 100f - NanobotBuildAndRepairSystemTerminal.VALUE_DELTA + NanobotBuildAndRepairSystemTerminal.VALUE_COLORIZE_DELTA);
        }

        private static Vector3 ConvertFromHSVColor(Vector3 value)
        {
            return new Vector3(value.X * 360f,
                              (value.Y + SATURATION_DELTA) * 100f,
                              (value.Z + VALUE_DELTA - VALUE_COLORIZE_DELTA) * 100f);
        }

        private static void UpdateVisual(IMyTerminalControl control)
        {
            control?.UpdateVisual();
        }

        /// <summary>
        /// Callback to add custom controls
        /// </summary>
        private static void CustomControlGetter(IMyTerminalBlock block, List<IMyTerminalControl> controls)
        {
            if (block.BlockDefinition.SubtypeName.StartsWith("SELtd") && block.BlockDefinition.SubtypeName.Contains("NanobotBuildAndRepairSystem"))
            {
                foreach (var item in CustomControls)
                {
                    controls.Add(item);
                    if (item == _SeparateWeldOptions)
                    {
                        var fromIdx = controls.IndexOf(_HelpOthers);
                        var toIdx = controls.IndexOf(_SeparateWeldOptions);
                        if (fromIdx >= 0 && toIdx >= 0) controls.Move(fromIdx, toIdx);
                    }
                }
            }
        }

        public static void Dispose()
        {
            MyAPIGateway.TerminalControls.CustomControlGetter -= CustomControlGetter;
        }
    }
}
