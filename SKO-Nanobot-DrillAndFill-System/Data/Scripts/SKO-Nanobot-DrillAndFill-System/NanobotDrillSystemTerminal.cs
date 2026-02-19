namespace SKONanobotDrillAndFillSystem.NanobotDrillSystem
{
    using Sandbox.Definitions;
    using Sandbox.Game.Localization;
    using Sandbox.ModAPI;
    using Sandbox.ModAPI.Interfaces.Terminal;
    using SKONanobotDrillAndFillSystem.Utils;
    using System;
    using System.Collections.Generic;
    using System.Text;
    using VRage;
    using VRage.Game;
    using VRage.Game.ModAPI;
    using VRage.ModAPI;
    using VRage.Utils;
    using VRageMath;

    [Flags]
    public enum WorkModes
    {
        /// <summary>
        /// Drills any thing in drill range
        /// </summary>
        Drill = 0x0001,

        /// <summary>
        /// Collect anything in range ignoring things inbetween
        /// </summary>
        Collect = 0x0002,

        /// <summary>
        /// Fill voxel with material
        /// </summary>
        Fill = 0x0004
    }

    [Flags]
    public enum VisualAndSoundEffects
    {
        DrillingVisualEffect = 0x00000001,
        DrillingSoundEffect = 0x00000010,
        FillingVisualEffect = 0x00000100,
        FillingSoundEffect = 0x00001000,
        TransportVisualEffect = 0x00010000,
    }

    public static class NanobotDrillSystemTerminal
    {
        public const float SATURATION_DELTA = 0.8f;
        public const float VALUE_DELTA = 0.55f;
        public const float VALUE_COLORIZE_DELTA = 0.1f;

        public static bool CustomControlsInit = false;
        private static List<IMyTerminalControl> CustomControls = new List<IMyTerminalControl>();

        private static IMyTerminalControlOnOffSwitch _DrillEnableDisableSwitch;
        private static IMyTerminalControlButton _DrillPriorityButtonUp;
        private static IMyTerminalControlButton _DrillPriorityButtonDown;
        private static IMyTerminalControlListbox _DrillPriorityListBox;

        private static IMyTerminalControlOnOffSwitch _ComponentCollectEnableDisableSwitch;
        private static IMyTerminalControlButton _ComponentCollectPriorityButtonUp;
        private static IMyTerminalControlButton _ComponentCollectPriorityButtonDown;
        private static IMyTerminalControlListbox _ComponentCollectPriorityListBox;

        /// <summary>
        /// Check an return the GameLogic object
        /// </summary>
        /// <param name="block"></param>
        /// <returns></returns>
        private static NanobotDrillSystemBlock GetSystem(IMyTerminalBlock block)
        {
            if (block != null && block.GameLogic != null) return block.GameLogic.GetAs<NanobotDrillSystemBlock>();
            return null;
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
                    // !! As we can't subtype here they will be also available in every ShipDrill but without function !!

                    if (Mod.Log.ShouldLog(Logging.Level.Event)) Mod.Log.Write(Logging.Level.Event, "InitializeControls");

                    MyAPIGateway.TerminalControls.CustomControlGetter += CustomControlGetter;

                    IMyTerminalControlLabel label;
                    IMyTerminalControlCheckbox checkbox;
                    IMyTerminalControlCombobox comboBox;
                    IMyTerminalControlSeparator separateArea;
                    IMyTerminalControlSlider slider;
                    IMyTerminalControlOnOffSwitch onoffSwitch;
                    IMyTerminalControlButton button;

                    var drillingAllowed = (NanobotDrillSystemMod.Settings.Drill.AllowedWorkModes & (WorkModes.Drill | WorkModes.Collect)) != 0;
                    var fillingAllowed = (NanobotDrillSystemMod.Settings.Drill.AllowedWorkModes & (WorkModes.Fill)) != 0;

                    Func<IMyTerminalBlock, bool> isDrillSystem = (block) =>
                    {
                        var system = GetSystem(block);
                        return system != null;
                    };

                    Func<IMyTerminalBlock, bool> isReadonly = (block) => { return false; };
                    Func<IMyTerminalBlock, bool> isDrillingAllowed = (block) => { return drillingAllowed; };
                    Func<IMyTerminalBlock, bool> isFillingAllowed = (block) => { return fillingAllowed; };

                    // --- General
                    label = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlLabel, IMyShipDrill>("ModeSettings");
                    label.Label = Texts.ModeSettings_Headline;
                    CustomControls.Add(label);
                    {
                        // --- Select work mode
                        var onlyOneAllowed = (NanobotDrillSystemMod.Settings.Drill.AllowedWorkModes & (NanobotDrillSystemMod.Settings.Drill.AllowedWorkModes - 1)) == 0;
                        comboBox = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlCombobox, IMyShipDrill>("WorkMode");
                        comboBox.Title = Texts.WorkMode;
                        comboBox.Tooltip = Texts.WorkMode_Tooltip;
                        comboBox.Enabled = onlyOneAllowed ? isReadonly : isDrillSystem;
                        comboBox.ComboBoxContent = (list) =>
                        {
                            if (NanobotDrillSystemMod.Settings.Drill.AllowedWorkModes.HasFlag(WorkModes.Collect))
                                list.Add(new MyTerminalControlComboBoxItem() { Key = (long)WorkModes.Collect, Value = Texts.WorkMode_Collect });
                            if (NanobotDrillSystemMod.Settings.Drill.AllowedWorkModes.HasFlag(WorkModes.Drill))
                                list.Add(new MyTerminalControlComboBoxItem() { Key = (long)WorkModes.Drill, Value = Texts.WorkMode_Drill });
                            if (NanobotDrillSystemMod.Settings.Drill.AllowedWorkModes.HasFlag(WorkModes.Fill))
                                list.Add(new MyTerminalControlComboBoxItem() { Key = (long)WorkModes.Fill, Value = Texts.WorkMode_Fill });
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
                                if (NanobotDrillSystemMod.Settings.Drill.AllowedWorkModes.HasFlag((WorkModes)value))
                                {
                                    system.Settings.WorkMode = (WorkModes)value;
                                }
                            }
                        };
                        comboBox.SupportsMultipleBlocks = true;
                        CustomControls.Add(comboBox);
                        CreateProperty(comboBox, onlyOneAllowed);

                        //Allow switch work mode by Buttonpanel
                        var list1 = new List<MyTerminalControlComboBoxItem>();
                        comboBox.ComboBoxContent(list1);
                        foreach (var entry in list1)
                        {
                            var mode = entry.Key;
                            var comboBox1 = comboBox;
                            var action = MyAPIGateway.TerminalControls.CreateAction<IMyShipDrill>(string.Format("{0}_On", ((WorkModes)mode).ToString()));
                            action.Name = new StringBuilder(string.Format("{0} On", entry.Value));
                            action.Icon = @"Textures\GUI\Icons\Actions\SwitchOn.dds";
                            action.Enabled = isDrillSystem;
                            action.Action = (block) =>
                            {
                                comboBox1.Setter(block, mode);
                            };
                            action.ValidForGroups = true;
                            MyAPIGateway.TerminalControls.AddAction<IMyShipDrill>(action);
                        }
                    }

                    // --- Drilling
                    label = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlLabel, IMyShipDrill>("DrillSettings");
                    label.Label = Texts.DrillSettings_Headline;
                    CustomControls.Add(label);
                    {
                        // -- Priority Drilling
                        separateArea = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlSeparator, IMyShipDrill>("SeparateDrillPrio");
                        separateArea.Visible = isDrillingAllowed;
                        CustomControls.Add(separateArea);
                        {
                            onoffSwitch = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlOnOffSwitch, IMyShipDrill>("DrillPriority");
                            _DrillEnableDisableSwitch = onoffSwitch;
                            onoffSwitch.Title = Texts.DrillPriority;
                            onoffSwitch.Tooltip = Texts.DrillPriority_Tooltip;
                            onoffSwitch.OnText = Texts.Priority_Enable;
                            onoffSwitch.OffText = Texts.Priority_Disable;
                            onoffSwitch.Visible = isDrillingAllowed;
                            onoffSwitch.Enabled = (block) =>
                            {
                                var system = GetSystem(block);
                                return system != null && system.DrillPriority != null && system.DrillPriority.Selected != null && isDrillingAllowed(block) && !NanobotDrillSystemMod.Settings.Drill.DrillPriorityFixed;
                            };
                            onoffSwitch.Getter = (block) =>
                            {
                                var system = GetSystem(block);
                                return system != null && system.DrillPriority != null && system.DrillPriority.Selected != null ?
                            system.DrillPriority.GetEnabled(system.DrillPriority.Selected.Key) : false;
                            };
                            onoffSwitch.Setter = (block, value) =>
                            {
                                var system = GetSystem(block);
                                if (system != null && system.DrillPriority != null && system.DrillPriority.Selected != null && isDrillingAllowed(block) && !NanobotDrillSystemMod.Settings.Drill.DrillPriorityFixed)
                                {
                                    system.DrillPriority.SetEnabled(system.DrillPriority.Selected.Key, value);
                                    system.Settings.DrillPriority = system.DrillPriority.GetEntries();
                                    _DrillPriorityListBox.UpdateVisual();
                                }
                            };
                            onoffSwitch.SupportsMultipleBlocks = true;
                            CustomControls.Add(onoffSwitch);

                            button = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlButton, IMyShipDrill>("DrillPriorityUp");
                            _DrillPriorityButtonUp = button;
                            button.Title = Texts.Priority_Up;
                            button.Visible = isDrillingAllowed;
                            button.Enabled = (block) =>
                            {
                                var system = GetSystem(block);
                                return system != null && system.DrillPriority != null && system.DrillPriority.Selected != null && isDrillingAllowed(block) && !NanobotDrillSystemMod.Settings.Drill.DrillPriorityFixed;
                            };
                            button.Action = (block) =>
                            {
                                var system = GetSystem(block);
                                if (system != null && !NanobotDrillSystemMod.Settings.Drill.DrillPriorityFixed)
                                {
                                    system.DrillPriority.MoveSelectedUp();
                                    system.Settings.DrillPriority = system.DrillPriority.GetEntries();
                                    _DrillPriorityListBox.UpdateVisual();
                                }
                            };
                            button.SupportsMultipleBlocks = true;
                            CustomControls.Add(button);

                            button = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlButton, IMyShipDrill>("DrillPriorityDown");
                            _DrillPriorityButtonDown = button;
                            button.Title = Texts.Priority_Down;
                            button.Visible = isDrillingAllowed;
                            button.Enabled = (block) =>
                            {
                                var system = GetSystem(block);
                                return system != null && system.DrillPriority != null && system.DrillPriority.Selected != null && isDrillingAllowed(block) && !NanobotDrillSystemMod.Settings.Drill.DrillPriorityFixed;
                            };
                            button.Action = (block) =>
                            {
                                var system = GetSystem(block);
                                if (system != null && !NanobotDrillSystemMod.Settings.Drill.DrillPriorityFixed)
                                {
                                    system.DrillPriority.MoveSelectedDown();
                                    system.Settings.DrillPriority = system.DrillPriority.GetEntries();
                                    _DrillPriorityListBox.UpdateVisual();
                                }
                            };
                            button.SupportsMultipleBlocks = true;
                            CustomControls.Add(button);

                            var listbox = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlListbox, IMyShipDrill>("DrillPriority");
                            _DrillPriorityListBox = listbox;

                            listbox.Multiselect = false;
                            listbox.VisibleRowsCount = 12;
                            listbox.Enabled = drillingAllowed ? isDrillSystem : isReadonly;
                            listbox.Visible = isDrillingAllowed;
                            listbox.ItemSelected = (block, selected) =>
                            {
                                var system = GetSystem(block);
                                if (system != null && system.DrillPriority != null)
                                {
                                    if (selected.Count > 0) system.DrillPriority.SetSelectedByKey(((PrioItem)selected[0].UserData).Key);
                                    else system.DrillPriority.ClearSelected();
                                    _DrillEnableDisableSwitch.UpdateVisual();
                                    _DrillPriorityButtonUp.UpdateVisual();
                                    _DrillPriorityButtonDown.UpdateVisual();
                                }
                            };
                            listbox.ListContent = (block, items, selected) =>
                            {
                                var system = GetSystem(block);
                                if (system != null && system.DrillPriority != null)
                                {
                                    system.DrillPriority.FillTerminalList(items, selected);
                                }
                            };
                            listbox.SupportsMultipleBlocks = true;
                            CustomControls.Add(listbox);
                        }
                    }

                    // --- Filling
                    label = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlLabel, IMyShipDrill>("FillSettings");
                    label.Label = Texts.FillSettings_Headline;
                    CustomControls.Add(label);
                    {
                        // -- Fill with
                        comboBox = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlCombobox, IMyShipDrill>("FillMaterial");
                        comboBox.Title = Texts.FillMaterial;
                        comboBox.Tooltip = Texts.FillMaterial_Tooltip;
                        comboBox.Enabled = isDrillSystem;

                        comboBox.ComboBoxContent = (list) =>
                        {
                            var materialDefinitions = MyDefinitionManager.Static.GetVoxelMaterialDefinitions();
                            foreach (var materialDefinition in materialDefinitions)
                            {
                                if (materialDefinition.Enabled && materialDefinition.Public && materialDefinition.CanBeHarvested && !string.IsNullOrEmpty(materialDefinition.MinedOre))
                                {
                                    list.Add(new MyTerminalControlComboBoxItem() { Key = (long)materialDefinition.Index, Value = MyStringId.GetOrCompute(MyTexts.Get(MyStringId.GetOrCompute(materialDefinition.Id.SubtypeName)) + " [" + MyTexts.Get(MyStringId.GetOrCompute(materialDefinition.MinedOre)) + "]") });
                                    list.Sort((a, b) => { return string.Compare(a.Value.String, b.Value.String); });
                                }
                            }
                        };
                        comboBox.Getter = (block) =>
                        {
                            var system = GetSystem(block);
                            if (system == null) return 0;
                            else return (long)system.Settings.FillMaterial;
                        };
                        comboBox.Setter = (block, value) =>
                        {
                            var system = GetSystem(block);
                            if (system != null)
                            {
                                system.Settings.FillMaterial = value;
                            }
                        };
                        comboBox.SupportsMultipleBlocks = true;
                        CustomControls.Add(comboBox);
                        CreateProperty(comboBox, false);
                    }

                    // --- Collecting
                    label = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlLabel, IMyShipDrill>("CollectingSettings");
                    label.Label = Texts.CollectSettings_Headline;
                    CustomControls.Add(label);
                    {
                        // --- Collect floating objects
                        //separateArea = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlSeparator, IMyShipDrill>("SeparateCollectPrio");
                        //CustomControls.Add(separateArea);
                        {
                            onoffSwitch = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlOnOffSwitch, IMyShipDrill>("CollectPriority");
                            _ComponentCollectEnableDisableSwitch = onoffSwitch;
                            onoffSwitch.Title = Texts.CollectPriority;
                            onoffSwitch.Tooltip = Texts.CollectPriority_Tooltip;
                            onoffSwitch.OnText = Texts.Priority_Enable;
                            onoffSwitch.OffText = Texts.Priority_Disable;
                            onoffSwitch.Enabled = (block) =>
                            {
                                var system = GetSystem(block);
                                return system != null && system.ComponentCollectPriority != null && system.ComponentCollectPriority.Selected != null && !NanobotDrillSystemMod.Settings.Drill.CollectPriorityFixed;
                            };

                            onoffSwitch.Getter = (block) =>
                            {
                                var system = GetSystem(block);
                                return system != null && system.ComponentCollectPriority != null && system.ComponentCollectPriority.Selected != null ?
                            system.ComponentCollectPriority.GetEnabled(system.ComponentCollectPriority.Selected.Key) : false;
                            };
                            onoffSwitch.Setter = (block, value) =>
                            {
                                var system = GetSystem(block);
                                if (system != null && system.ComponentCollectPriority != null && system.ComponentCollectPriority.Selected != null && !NanobotDrillSystemMod.Settings.Drill.CollectPriorityFixed)
                                {
                                    system.ComponentCollectPriority.SetEnabled(system.ComponentCollectPriority.Selected.Key, value);
                                    system.Settings.ComponentCollectPriority = system.ComponentCollectPriority.GetEntries();
                                    _ComponentCollectPriorityListBox.UpdateVisual();
                                }
                            };
                            onoffSwitch.SupportsMultipleBlocks = true;
                            CustomControls.Add(onoffSwitch);

                            button = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlButton, IMyShipDrill>("CollectPriorityUp");
                            _ComponentCollectPriorityButtonUp = button;
                            button.Title = Texts.Priority_Up;
                            button.Enabled = (block) =>
                            {
                                var system = GetSystem(block);
                                return system != null && system.ComponentCollectPriority != null && system.ComponentCollectPriority.Selected != null && !NanobotDrillSystemMod.Settings.Drill.CollectPriorityFixed;
                            };
                            button.Action = (block) =>
                            {
                                var system = GetSystem(block);
                                if (system != null && !NanobotDrillSystemMod.Settings.Drill.CollectPriorityFixed)
                                {
                                    system.ComponentCollectPriority.MoveSelectedUp();
                                    system.Settings.ComponentCollectPriority = system.ComponentCollectPriority.GetEntries();
                                    _ComponentCollectPriorityListBox.UpdateVisual();
                                }
                            };
                            button.SupportsMultipleBlocks = true;
                            CustomControls.Add(button);

                            button = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlButton, IMyShipDrill>("CollectPriorityDown");
                            _ComponentCollectPriorityButtonDown = button;
                            button.Title = Texts.Priority_Down;
                            button.Enabled = (block) =>
                            {
                                var system = GetSystem(block);
                                return system != null && system.ComponentCollectPriority != null && system.ComponentCollectPriority.Selected != null && !NanobotDrillSystemMod.Settings.Drill.CollectPriorityFixed;
                            };
                            button.Action = (block) =>
                            {
                                var system = GetSystem(block);
                                if (system != null && !NanobotDrillSystemMod.Settings.Drill.CollectPriorityFixed)
                                {
                                    system.ComponentCollectPriority.MoveSelectedDown();
                                    system.Settings.ComponentCollectPriority = system.ComponentCollectPriority.GetEntries();
                                    _ComponentCollectPriorityListBox.UpdateVisual();
                                }
                            };
                            button.SupportsMultipleBlocks = true;
                            CustomControls.Add(button);

                            var listbox = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlListbox, IMyShipDrill>("CollectPriority");
                            _ComponentCollectPriorityListBox = listbox;

                            listbox.Multiselect = false;
                            listbox.VisibleRowsCount = 5;
                            listbox.Enabled = (block) =>
                            {
                                var system = GetSystem(block);
                                return system != null;
                            };
                            listbox.ItemSelected = (block, selected) =>
                            {
                                var system = GetSystem(block);
                                if (system != null && system.ComponentCollectPriority != null)
                                {
                                    if (selected.Count > 0) system.ComponentCollectPriority.SetSelectedByKey(((PrioItem)selected[0].UserData).Key);
                                    else system.ComponentCollectPriority.ClearSelected();
                                    _ComponentCollectEnableDisableSwitch.UpdateVisual();
                                    _ComponentCollectPriorityButtonUp.UpdateVisual();
                                    _ComponentCollectPriorityButtonDown.UpdateVisual();
                                }
                            };
                            listbox.ListContent = (block, items, selected) =>
                            {
                                var system = GetSystem(block);
                                if (system != null && system.ComponentCollectPriority != null)
                                {
                                    system.ComponentCollectPriority.FillTerminalList(items, selected);
                                }
                            };
                            listbox.SupportsMultipleBlocks = true;
                            CustomControls.Add(listbox);

                            // Collect if idle
                            checkbox = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlCheckbox, IMyShipDrill>("CollectIfIdle");
                            checkbox.Title = Texts.CollectOnlyIfIdle;
                            checkbox.Tooltip = Texts.CollectOnlyIfIdle_Tooltip;
                            checkbox.OnText = MySpaceTexts.SwitchText_On;
                            checkbox.OffText = MySpaceTexts.SwitchText_Off;
                            checkbox.Enabled = NanobotDrillSystemMod.Settings.Drill.CollectIfIdleFixed ? isReadonly : isDrillSystem;
                            checkbox.Getter = (block) =>
                            {
                                var system = GetSystem(block);
                                return system != null ? ((system.Settings.Flags & SyncBlockSettings.Settings.ComponentCollectIfIdle) != 0) : false;
                            };
                            checkbox.Setter = (block, value) =>
                            {
                                var system = GetSystem(block);
                                if (system != null && !NanobotDrillSystemMod.Settings.Drill.CollectIfIdleFixed)
                                {
                                    system.Settings.Flags = (system.Settings.Flags & ~SyncBlockSettings.Settings.ComponentCollectIfIdle) | (value ? SyncBlockSettings.Settings.ComponentCollectIfIdle : 0);
                                }
                            };
                            checkbox.SupportsMultipleBlocks = true;
                            CreateCheckBoxAction("CollectIfIdle", checkbox);
                            CustomControls.Add(checkbox);
                            CreateProperty(checkbox, NanobotDrillSystemMod.Settings.Drill.CollectIfIdleFixed);
                        }
                    }

                    label = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlLabel, IMyShipDrill>("GeneralSettings");
                    label.Label = Texts.GeneralSettings_Headline;
                    CustomControls.Add(label);
                    {
                        // -- Highlight Area
                        separateArea = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlSeparator, IMyShipDrill>("SeparateArea");
                        CustomControls.Add(separateArea);
                        {
                            Func<IMyTerminalBlock, float> getLimitOffsetMin = (block) =>
                            {
                                var system = GetSystem(block);
                                return system != null && system.Settings != null ? -system.Settings.MaximumOffset : -NanobotDrillSystemBlock.DRILL_OFFSET_MAX_IN_M;
                            };
                            Func<IMyTerminalBlock, float> getLimitOffsetMax = (block) =>
                            {
                                var system = GetSystem(block);
                                return system != null && system.Settings != null ? system.Settings.MaximumOffset : NanobotDrillSystemBlock.DRILL_OFFSET_MAX_IN_M;
                            };

                            Func<IMyTerminalBlock, float> getLimitMin = (block) => NanobotDrillSystemBlock.DRILL_RANGE_MIN_IN_M;
                            Func<IMyTerminalBlock, float> getLimitMax = (block) =>
                            {
                                var system = GetSystem(block);
                                return system != null && system.Settings != null ? system.Settings.MaximumRange : NanobotDrillSystemBlock.DRILL_RANGE_MAX_IN_M;
                            };

                            checkbox = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlCheckbox, IMyShipDrill>("ShowArea");
                            checkbox.Title = Texts.AreaShow;
                            checkbox.Tooltip = Texts.AreaShow_Tooltip;
                            checkbox.Enabled = NanobotDrillSystemMod.Settings.Drill.ShowAreaFixed ? isReadonly : isDrillSystem;
                            checkbox.Getter = (block) =>
                            {
                                var system = GetSystem(block);
                                if (system != null)
                                {
                                    return system != null ? ((system.Settings.Flags & SyncBlockSettings.Settings.ShowArea) != 0) : false;
                                }

                                return false;
                            };
                            checkbox.Setter = (block, value) =>
                            {
                                var system = GetSystem(block);
                                if (system != null)
                                {
                                    system.Settings.Flags = (system.Settings.Flags & ~SyncBlockSettings.Settings.ShowArea) | (value ? SyncBlockSettings.Settings.ShowArea : 0);
                                }
                            };
                            checkbox.SupportsMultipleBlocks = true;
                            CreateCheckBoxAction("ShowArea", checkbox);
                            CustomControls.Add(checkbox);
                            CreateProperty(checkbox, NanobotDrillSystemMod.Settings.Drill.ShowAreaFixed);

                            //Slider Offset
                            slider = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlSlider, IMyShipDrill>("AreaOffsetLeftRight");
                            slider.Title = MySpaceTexts.BlockPropertyTitle_ProjectionOffsetX;
                            slider.SetLimits(getLimitOffsetMin, getLimitOffsetMax);
                            slider.Enabled = NanobotDrillSystemMod.Settings.Drill.AreaOffsetFixed ? isReadonly : isDrillSystem;
                            slider.Getter = (block) =>
                            {
                                var system = GetSystem(block);
                                return system != null ? -system.Settings.AreaOffset.X : 0;
                            };
                            slider.Setter = (block, val) =>
                            {
                                var system = GetSystem(block);
                                if (system != null)
                                {
                                    var min = getLimitOffsetMin(block);
                                    var max = getLimitOffsetMax(block);
                                    val = (float)Math.Round(-val * 2, MidpointRounding.AwayFromZero) / 2f;
                                    val = val < min ? min : val > max ? max : val;
                                    system.Settings.AreaOffset = new Vector3(val, system.Settings.AreaOffset.Y, system.Settings.AreaOffset.Z);
                                }
                            };
                            slider.Writer = (block, val) =>
                            {
                                var system = GetSystem(block);
                                if (system != null)
                                {
                                    val.Append(-system.Settings.AreaOffset.X + " m");
                                }
                            };
                            slider.SupportsMultipleBlocks = true;
                            CustomControls.Add(slider);
                            CreateSliderActions("AreaOffsetLeftRight", slider);
                            CreateProperty(slider, NanobotDrillSystemMod.Settings.Drill.AreaOffsetFixed);

                            slider = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlSlider, IMyShipDrill>("AreaOffsetUpDown");
                            slider.Title = MySpaceTexts.BlockPropertyTitle_ProjectionOffsetY;
                            slider.SetLimits(getLimitOffsetMin, getLimitOffsetMax);
                            slider.Enabled = NanobotDrillSystemMod.Settings.Drill.AreaOffsetFixed ? isReadonly : isDrillSystem;
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
                            CreateProperty(slider, NanobotDrillSystemMod.Settings.Drill.AreaOffsetFixed);

                            slider = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlSlider, IMyShipDrill>("AreaOffsetFrontBack");
                            slider.Title = MySpaceTexts.BlockPropertyTitle_ProjectionOffsetZ;
                            slider.SetLimits(getLimitOffsetMin, getLimitOffsetMax);
                            slider.Enabled = NanobotDrillSystemMod.Settings.Drill.AreaOffsetFixed ? isReadonly : isDrillSystem;
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
                            CreateProperty(slider, NanobotDrillSystemMod.Settings.Drill.AreaOffsetFixed);

                            //Slider Area
                            slider = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlSlider, IMyShipDrill>("AreaWidth");
                            slider.Title = Texts.AreaWidth;
                            slider.SetLimits(getLimitMin, getLimitMax);
                            slider.Enabled = NanobotDrillSystemMod.Settings.Drill.AreaSizeFixed ? isReadonly : isDrillSystem;
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
                            CreateProperty(slider, NanobotDrillSystemMod.Settings.Drill.AreaSizeFixed);

                            slider = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlSlider, IMyShipDrill>("AreaHeight");
                            slider.Title = Texts.AreaHeight;
                            slider.SetLimits(getLimitMin, getLimitMax);
                            slider.Enabled = NanobotDrillSystemMod.Settings.Drill.AreaSizeFixed ? isReadonly : isDrillSystem;
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
                            CreateProperty(slider, NanobotDrillSystemMod.Settings.Drill.AreaSizeFixed);

                            slider = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlSlider, IMyShipDrill>("AreaDepth");
                            slider.Title = Texts.AreaDepth;
                            slider.SetLimits(getLimitMin, getLimitMax);
                            slider.Enabled = NanobotDrillSystemMod.Settings.Drill.AreaSizeFixed ? isReadonly : isDrillSystem;
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
                            CreateProperty(slider, NanobotDrillSystemMod.Settings.Drill.AreaSizeFixed);
                        }

                        // -- Remote control
                        if (!NanobotDrillSystemMod.Settings.Drill.ScriptControllFixed)
                        {
                            separateArea = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlSeparator, IMyShipDrill>("SeparateRemoteControlled");
                            CustomControls.Add(separateArea);
                            {
                                //FollowMe
                                //Change to ComboBox as soon as ComboBoxContentWithBlock is available from Modapi
                                var listbox = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlListbox, IMyShipDrill>("RemoteControlledBy");
                                listbox.Title = Texts.RemoteCtrlBy;
                                listbox.Tooltip = Texts.RemoteCtrlBy_Tooltip;
                                listbox.Multiselect = false;
                                listbox.VisibleRowsCount = 3;
                                listbox.Enabled = drillingAllowed ? isDrillSystem : isReadonly;
                                listbox.Visible = isDrillingAllowed;
                                listbox.ItemSelected = (block, selected) =>
                                {
                                    var system = GetSystem(block);
                                    if (system != null)
                                    {
                                        if (selected.Count > 0)
                                        {
                                            system.Settings.RemoteControlledBy = (long?)selected[0].UserData;
                                        }
                                        else system.Settings.RemoteControlledBy = null;
                                    }
                                };
                                listbox.ListContent = (block, items, selected) =>
                                {
                                    var system = GetSystem(block);
                                    if (system != null)
                                    {
                                        var playersList = new List<IMyPlayer>();
                                        MyModAPIHelper.MyMultiplayer.Static.Players.GetPlayers(playersList, (player) =>
                                {
                                    var relation = player.GetRelationTo(block.OwnerId);
                                    return Utils.IsCharacterPlayerAndActive(player.Character) &&
                               (
                                  relation == MyRelationsBetweenPlayerAndBlock.FactionShare ||
                                  relation == MyRelationsBetweenPlayerAndBlock.Friends ||
                                  relation == MyRelationsBetweenPlayerAndBlock.Owner
                               );
                                }
                                );

                                        var itemNone = new MyTerminalControlListBoxItem(Texts.RemoteCtrlBy_None, MyStringId.NullOrEmpty, null);
                                        items.Add(itemNone);
                                        MyTerminalControlListBoxItem itemOwner = null;
                                        foreach (var player in playersList)
                                        {
                                            var item = new MyTerminalControlListBoxItem(MyStringId.GetOrCompute(player.DisplayName), MyStringId.NullOrEmpty, player.IdentityId);
                                            items.Add(item);
                                            if (system.Settings.RemoteControlledBy == player.IdentityId)
                                            {
                                                selected.Add(item);
                                            }
                                            if (system.Drill.OwnerId == player.IdentityId)
                                            {
                                                itemOwner = item;
                                            }
                                        }
                                        items.Sort((a, b) => { return string.Compare(a.Text.String, b.Text.String); });
                                        if (selected.Count == 0) selected.Add(itemNone);
                                        if (itemOwner != null) items.Move(items.IndexOf(itemOwner), 1);
                                    }
                                };
                                listbox.SupportsMultipleBlocks = true;
                                CustomControls.Add(listbox);

                                checkbox = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlCheckbox, IMyShipDrill>("RemoteControlShowArea");
                                checkbox.Title = Texts.RemoteCtrlShowArea;
                                checkbox.Tooltip = Texts.RemoteCtrlShowArea_Tooltip;
                                checkbox.OnText = MySpaceTexts.SwitchText_On;
                                checkbox.OffText = MySpaceTexts.SwitchText_Off;
                                checkbox.Enabled = isDrillSystem;
                                checkbox.Getter = (block) =>
                                {
                                    var system = GetSystem(block);
                                    return system != null ? ((system.Settings.Flags & SyncBlockSettings.Settings.RemoteControlShowArea) != 0) : false;
                                };
                                checkbox.Setter = (block, value) =>
                                {
                                    var system = GetSystem(block);
                                    if (system != null)
                                    {
                                        system.Settings.Flags = (system.Settings.Flags & ~SyncBlockSettings.Settings.RemoteControlShowArea) | (value ? SyncBlockSettings.Settings.RemoteControlShowArea : 0);
                                    }
                                };
                                checkbox.SupportsMultipleBlocks = true;
                                CreateCheckBoxAction("RemoteControlShowArea", checkbox);
                                CustomControls.Add(checkbox);
                                CreateProperty(checkbox, false);

                                checkbox = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlCheckbox, IMyShipDrill>("RemoteControlWorkdisabled");
                                checkbox.Title = Texts.RemoteCtrlWorking;
                                checkbox.Tooltip = Texts.RemoteCtrlWorking_Tooltip;
                                checkbox.OnText = MySpaceTexts.SwitchText_On;
                                checkbox.OffText = MySpaceTexts.SwitchText_Off;
                                checkbox.Enabled = isDrillSystem;
                                checkbox.Getter = (block) =>
                                {
                                    var system = GetSystem(block);
                                    return system != null ? ((system.Settings.Flags & SyncBlockSettings.Settings.RemoteControlWorkdisabled) != 0) : false;
                                };
                                checkbox.Setter = (block, value) =>
                                {
                                    var system = GetSystem(block);
                                    if (system != null)
                                    {
                                        system.Settings.Flags = (system.Settings.Flags & ~SyncBlockSettings.Settings.RemoteControlWorkdisabled) | (value ? SyncBlockSettings.Settings.RemoteControlWorkdisabled : 0);
                                    }
                                };
                                checkbox.SupportsMultipleBlocks = true;
                                CreateCheckBoxAction("RemoteControlWorkdisabled", checkbox);
                                CustomControls.Add(checkbox);
                                CreateProperty(checkbox, false);
                            }
                        }

                        // -- Sound enabled
                        separateArea = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlSeparator, IMyShipDrill>("SeparateOther");
                        CustomControls.Add(separateArea);

                        slider = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlSlider, IMyShipDrill>("SoundVolume");
                        slider.Title = Texts.SoundVolume;
                        slider.SetLimits(0f, 100f);
                        slider.Enabled = NanobotDrillSystemMod.Settings.Drill.SoundVolumeFixed ? isReadonly : isDrillSystem;
                        slider.Getter = (block) =>
                        {
                            var system = GetSystem(block);
                            return system != null ? 100f * system.Settings.SoundVolume / NanobotDrillSystemBlock.DRILL_SOUND_VOLUME : 0f;
                        };
                        slider.Setter = (block, val) =>
                        {
                            var system = GetSystem(block);
                            if (system != null)
                            {
                                var min = 0;
                                var max = 100;
                                val = val < min ? min : val > max ? max : val;
                                system.Settings.SoundVolume = (float)Math.Round(val * NanobotDrillSystemBlock.DRILL_SOUND_VOLUME) / 100f;
                            }
                        };
                        slider.Writer = (block, val) =>
                        {
                            var system = GetSystem(block);
                            if (system != null)
                            {
                                val.Append(Math.Round(100f * system.Settings.SoundVolume / NanobotDrillSystemBlock.DRILL_SOUND_VOLUME) + " %");
                            }
                        };
                        slider.SupportsMultipleBlocks = true;
                        CustomControls.Add(slider);
                        CreateSliderActions("SoundVolume", slider);
                        CreateProperty(slider, NanobotDrillSystemMod.Settings.Drill.SoundVolumeFixed);

                        // -- Script Control
                        if (!NanobotDrillSystemMod.Settings.Drill.ScriptControllFixed)
                        {
                            separateArea = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlSeparator, IMyShipDrill>("SeparateScriptControl");
                            CustomControls.Add(separateArea);

                            checkbox = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlCheckbox, IMyShipDrill>("ScriptControlled");
                            checkbox.Title = Texts.ScriptControlled;
                            checkbox.Tooltip = Texts.ScriptControlled_Tooltip;
                            checkbox.Enabled = isDrillSystem;
                            checkbox.Getter = (block) =>
                            {
                                var system = GetSystem(block);
                                return system != null ? ((system.Settings.Flags & SyncBlockSettings.Settings.ScriptControlled) != 0) : false;
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

                            //Scripting support for Priority and enabling Drill/Collect materials
                            var propertyDrillPriorityList = MyAPIGateway.TerminalControls.CreateProperty<List<string>, IMyShipDrill>("Drill.DrillPriorityList");
                            propertyDrillPriorityList.SupportsMultipleBlocks = false;
                            propertyDrillPriorityList.Getter = (block) =>
                            {
                                var system = block.GameLogic.GetAs<NanobotDrillSystemBlock>();
                                return system != null ? system.DrillPriority.GetList() : null;
                            };
                            MyAPIGateway.TerminalControls.AddControl<IMyShipDrill>(propertyDrillPriorityList);

                            var propertySDP = MyAPIGateway.TerminalControls.CreateProperty<Action<int, int>, IMyShipDrill>("Drill.SetDrillPriority");
                            propertySDP.SupportsMultipleBlocks = false;
                            propertySDP.Getter = (block) =>
                            {
                                var system = block.GameLogic.GetAs<NanobotDrillSystemBlock>();
                                if (system != null)
                                {
                                    return system.DrillPriority.SetPriority;
                                }
                                return null;
                            };
                            MyAPIGateway.TerminalControls.AddControl<IMyShipDrill>(propertySDP);

                            var propertyGDP = MyAPIGateway.TerminalControls.CreateProperty<Func<int, int>, IMyShipDrill>("Drill.GetDrillPriority");
                            propertyGDP.SupportsMultipleBlocks = false;
                            propertyGDP.Getter = (block) =>
                            {
                                var system = block.GameLogic.GetAs<NanobotDrillSystemBlock>();
                                if (system != null)
                                {
                                    return system.DrillPriority.GetPriority;
                                }
                                return null;
                            };
                            MyAPIGateway.TerminalControls.AddControl<IMyShipDrill>(propertyGDP);

                            var propertySDE = MyAPIGateway.TerminalControls.CreateProperty<Action<int, bool>, IMyShipDrill>("Drill.SetDrillEnabled");
                            propertySDE.SupportsMultipleBlocks = false;
                            propertySDE.Getter = (block) =>
                            {
                                var system = block.GameLogic.GetAs<NanobotDrillSystemBlock>();
                                if (system != null)
                                {
                                    return system.DrillPriority.SetEnabled;
                                }
                                return null;
                            };
                            MyAPIGateway.TerminalControls.AddControl<IMyShipDrill>(propertySDE);

                            var propertyGDE = MyAPIGateway.TerminalControls.CreateProperty<Func<int, bool>, IMyShipDrill>("Drill.GetDrillEnabled");
                            propertyGDE.SupportsMultipleBlocks = false;
                            propertyGDE.Getter = (block) =>
                            {
                                var system = block.GameLogic.GetAs<NanobotDrillSystemBlock>();
                                if (system != null)
                                {
                                    return system.DrillPriority.GetEnabled;
                                }
                                return null;
                            };
                            MyAPIGateway.TerminalControls.AddControl<IMyShipDrill>(propertyGDE);

                            //Scripting support for Priority and enabling ComponentClasses
                            var propertyComponentClassList = MyAPIGateway.TerminalControls.CreateProperty<List<string>, IMyShipDrill>("Drill.ComponentClassList");
                            propertyComponentClassList.SupportsMultipleBlocks = false;
                            propertyComponentClassList.Getter = (block) =>
                            {
                                var system = GetSystem(block);
                                return system != null ? system.ComponentCollectPriority.GetList() : null;
                            };
                            MyAPIGateway.TerminalControls.AddControl<IMyShipDrill>(propertyComponentClassList);

                            var propertySPC = MyAPIGateway.TerminalControls.CreateProperty<Action<int, int>, IMyShipDrill>("Drill.SetCollectPriority");
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
                            MyAPIGateway.TerminalControls.AddControl<IMyShipDrill>(propertySPC);

                            var propertyGPC = MyAPIGateway.TerminalControls.CreateProperty<Func<int, int>, IMyShipDrill>("Drill.GetCollectPriority");
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
                            MyAPIGateway.TerminalControls.AddControl<IMyShipDrill>(propertyGPC);

                            var propertySEC = MyAPIGateway.TerminalControls.CreateProperty<Action<int, bool>, IMyShipDrill>("Drill.SetCollectEnabled");
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
                            MyAPIGateway.TerminalControls.AddControl<IMyShipDrill>(propertySEC);

                            var propertyGEC = MyAPIGateway.TerminalControls.CreateProperty<Func<int, bool>, IMyShipDrill>("Drill.GetCollectEnabled");
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
                            MyAPIGateway.TerminalControls.AddControl<IMyShipDrill>(propertyGEC);

                            //Working Lists
                            var propertyPossibleDrillTargetsList = MyAPIGateway.TerminalControls.CreateProperty<List<List<object>>, IMyShipDrill>("Drill.PossibleDrillTargets");
                            propertyPossibleDrillTargetsList.SupportsMultipleBlocks = false;
                            propertyPossibleDrillTargetsList.Getter = (block) =>
                            {
                                var system = GetSystem(block);
                                return system != null ? system.GetPossibleDrillTargetsList() : null;
                            };
                            MyAPIGateway.TerminalControls.AddControl<IMyShipDrill>(propertyPossibleDrillTargetsList);

                            var propertyPossibleFillTargetsList = MyAPIGateway.TerminalControls.CreateProperty<List<List<object>>, IMyShipDrill>("Drill.PossibleFillTargets");
                            propertyPossibleFillTargetsList.SupportsMultipleBlocks = false;
                            propertyPossibleFillTargetsList.Getter = (block) =>
                            {
                                var system = GetSystem(block);
                                return system != null ? system.GetPossibleFillTargetsList() : null;
                            };
                            MyAPIGateway.TerminalControls.AddControl<IMyShipDrill>(propertyPossibleFillTargetsList);

                            var propertyPossibleCollectTargetsList = MyAPIGateway.TerminalControls.CreateProperty<List<VRage.Game.ModAPI.Ingame.IMyEntity>, IMyShipDrill>("Drill.PossibleCollectTargets");
                            propertyPossibleCollectTargetsList.SupportsMultipleBlocks = false;
                            propertyPossibleCollectTargetsList.Getter = (block) =>
                            {
                                var system = GetSystem(block);
                                return system != null ? system.GetPossibleCollectingTargetsList() : null;
                            };
                            MyAPIGateway.TerminalControls.AddControl<IMyShipDrill>(propertyPossibleCollectTargetsList);

                            //Control drilling
                            var propertyCPDT = MyAPIGateway.TerminalControls.CreateProperty<object, IMyShipDrill>("Drill.CurrentPickedDrillTarget");
                            propertyCPDT.SupportsMultipleBlocks = false;
                            propertyCPDT.Getter = (block) =>
                            {
                                var system = GetSystem(block);
                                return system != null ? system.Settings.CurrentPickedDrillingItem : null;
                            };
                            propertyCPDT.Setter = (block, value) =>
                            {
                                var system = GetSystem(block);
                                if (system != null)
                                {
                                    system.Settings.CurrentPickedDrillingItem = value;
                                }
                            };
                            MyAPIGateway.TerminalControls.AddControl<IMyShipDrill>(propertyCPDT);

                            var propertyCDT = MyAPIGateway.TerminalControls.CreateProperty<object, IMyShipDrill>("Drill.CurrentDrillTarget");
                            propertyCDT.SupportsMultipleBlocks = false;
                            propertyCDT.Getter = (block) =>
                            {
                                var system = GetSystem(block);
                                return system != null ? system.State.CurrentDrillingEntity : null;
                            };
                            MyAPIGateway.TerminalControls.AddControl<IMyShipDrill>(propertyCDT);

                            //Control filling
                            var propertyCPFT = MyAPIGateway.TerminalControls.CreateProperty<object, IMyShipDrill>("Drill.CurrentPickedFillTarget");
                            propertyCPFT.SupportsMultipleBlocks = false;
                            propertyCPFT.Getter = (block) =>
                            {
                                var system = GetSystem(block);
                                return system != null ? system.Settings.CurrentPickedFillingItem : null;
                            };
                            propertyCPFT.Setter = (block, value) =>
                            {
                                var system = GetSystem(block);
                                if (system != null)
                                {
                                    system.Settings.CurrentPickedFillingItem = value;
                                }
                            };
                            MyAPIGateway.TerminalControls.AddControl<IMyShipDrill>(propertyCPFT);

                            var propertyCFT = MyAPIGateway.TerminalControls.CreateProperty<object, IMyShipDrill>("Drill.CurrentFillTarget");
                            propertyCFT.SupportsMultipleBlocks = false;
                            propertyCFT.Getter = (block) =>
                            {
                                var system = GetSystem(block);
                                return system != null ? system.State.CurrentFillingEntity : null;
                            };
                            MyAPIGateway.TerminalControls.AddControl<IMyShipDrill>(propertyCFT);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Mod.Log.Write(Logging.Level.Error, "NanobotDrillSystemTerminal: InitializeControls exception: {0}", ex);
                }
            }
        }

        /// <summary>
        ///
        /// </summary>
        private static void CreateCheckBoxAction(string name, IMyTerminalControlCheckbox checkbox)
        {
            var action = MyAPIGateway.TerminalControls.CreateAction<IMyShipDrill>(string.Format("{0}_OnOff", name));
            action.Name = new StringBuilder(string.Format("{0} On/Off", name));
            action.Icon = @"Textures\GUI\Icons\Actions\Toggle.dds";
            action.Enabled = checkbox.Enabled;
            action.Action = (block) =>
            {
                checkbox.Setter(block, !checkbox.Getter(block));
            };
            action.ValidForGroups = checkbox.SupportsMultipleBlocks;
            MyAPIGateway.TerminalControls.AddAction<IMyShipDrill>(action);

            action = MyAPIGateway.TerminalControls.CreateAction<IMyShipDrill>(string.Format("{0}_On", name));
            action.Name = new StringBuilder(string.Format("{0} On", name));
            action.Icon = @"Textures\GUI\Icons\Actions\SwitchOn.dds";
            action.Enabled = checkbox.Enabled;
            action.Action = (block) =>
            {
                checkbox.Setter(block, true);
            };
            action.ValidForGroups = checkbox.SupportsMultipleBlocks;
            MyAPIGateway.TerminalControls.AddAction<IMyShipDrill>(action);

            action = MyAPIGateway.TerminalControls.CreateAction<IMyShipDrill>(string.Format("{0}_Off", name));
            action.Name = new StringBuilder(string.Format("{0} Off", name));
            action.Icon = @"Textures\GUI\Icons\Actions\SwitchOff.dds";
            action.Enabled = checkbox.Enabled;
            action.Action = (block) =>
            {
                checkbox.Setter(block, false);
            };
            action.ValidForGroups = checkbox.SupportsMultipleBlocks;
            MyAPIGateway.TerminalControls.AddAction<IMyShipDrill>(action);
        }

        /// <summary>
        ///
        /// </summary>
        private static void CreateOnOffSwitchAction(string name, IMyTerminalControlOnOffSwitch onoffSwitch)
        {
            var action = MyAPIGateway.TerminalControls.CreateAction<IMyShipDrill>(string.Format("{0}_OnOff", name));
            action.Name = new StringBuilder(string.Format("{0} {1}/{2}", name, onoffSwitch.OnText, onoffSwitch.OffText));
            action.Icon = @"Textures\GUI\Icons\Actions\Toggle.dds";
            action.Enabled = onoffSwitch.Enabled;
            action.Action = (block) =>
            {
                onoffSwitch.Setter(block, !onoffSwitch.Getter(block));
            };
            action.ValidForGroups = onoffSwitch.SupportsMultipleBlocks;
            MyAPIGateway.TerminalControls.AddAction<IMyShipDrill>(action);

            action = MyAPIGateway.TerminalControls.CreateAction<IMyShipDrill>(string.Format("{0}_On", name));
            action.Name = new StringBuilder(string.Format("{0} {1}", name, onoffSwitch.OnText));
            action.Icon = @"Textures\GUI\Icons\Actions\SwitchOn.dds";
            action.Enabled = onoffSwitch.Enabled;
            action.Action = (block) =>
            {
                onoffSwitch.Setter(block, true);
            };
            action.ValidForGroups = onoffSwitch.SupportsMultipleBlocks;
            MyAPIGateway.TerminalControls.AddAction<IMyShipDrill>(action);

            action = MyAPIGateway.TerminalControls.CreateAction<IMyShipDrill>(string.Format("{0}_Off", name));
            action.Name = new StringBuilder(string.Format("{0} {1}", name, onoffSwitch.OffText));
            action.Icon = @"Textures\GUI\Icons\Actions\SwitchOff.dds";
            action.Enabled = onoffSwitch.Enabled;
            action.Action = (block) =>
            {
                onoffSwitch.Setter(block, false);
            };
            action.ValidForGroups = onoffSwitch.SupportsMultipleBlocks;
            MyAPIGateway.TerminalControls.AddAction<IMyShipDrill>(action);
        }

        /// <summary>
        ///
        /// </summary>
        private static void CreateSliderActions(string sliderName, IMyTerminalControlSlider slider)
        {
            var action = MyAPIGateway.TerminalControls.CreateAction<IMyShipDrill>(string.Format("{0}_Increase", sliderName));
            action.Name = new StringBuilder(string.Format("{0} Increase", sliderName));
            action.Icon = @"Textures\GUI\Icons\Actions\Increase.dds";
            action.Enabled = slider.Enabled;
            action.Action = (block) =>
            {
                var val = slider.Getter(block);
                slider.Setter(block, val + 1);
            };
            action.ValidForGroups = slider.SupportsMultipleBlocks;
            MyAPIGateway.TerminalControls.AddAction<IMyShipDrill>(action);

            action = MyAPIGateway.TerminalControls.CreateAction<IMyShipDrill>(string.Format("{0}_Decrease", sliderName));
            action.Name = new StringBuilder(string.Format("{0} Decrease", sliderName));
            action.Icon = @"Textures\GUI\Icons\Actions\Decrease.dds";
            action.Enabled = slider.Enabled;
            action.Action = (block) =>
            {
                var val = slider.Getter(block);
                slider.Setter(block, val - 1);
            };
            action.ValidForGroups = slider.SupportsMultipleBlocks;
            MyAPIGateway.TerminalControls.AddAction<IMyShipDrill>(action);
        }

        /// <summary>
        ///
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="control"></param>
        private static void CreateProperty<T>(IMyTerminalValueControl<T> control, bool readOnly = false)
        {
            var property = MyAPIGateway.TerminalControls.CreateProperty<T, IMyShipDrill>("Drill." + control.Id);
            property.SupportsMultipleBlocks = false;
            property.Getter = control.Getter;
            if (!readOnly) property.Setter = control.Setter;
            MyAPIGateway.TerminalControls.AddControl<IMyShipDrill>(property);
        }

        private static void AddPossibleParents(NanobotDrillSystemBlock childSystem, List<MyTerminalControlListBoxItem> items, List<MyTerminalControlListBoxItem> selectedItems)
        {
            Vector3D posParent, posChild;
            childSystem.Drill.SlimBlock.ComputeWorldCenter(out posChild);

            var inRange = true; //To Take from Settings
            foreach (var possibleParentSystem in NanobotDrillSystemMod.DrillSystems.Values)
            {
                if (childSystem != possibleParentSystem)
                {
                    var relation = childSystem.Drill.GetUserRelationToOwner(possibleParentSystem.Drill.OwnerId);
                    if (!MyRelationsBetweenPlayerAndBlockExtensions.IsFriendly(relation)) continue; //Could not control not own or fraction blocks

                    if (inRange)
                    {
                        possibleParentSystem.Drill.SlimBlock.ComputeWorldCenter(out posParent);
                        var distance = (int)Math.Abs((posParent - posChild).Length());
                        if (distance >= NanobotDrillSystemMod.Settings.Range) continue; //Block is out of range
                    }

                    var item = new MyTerminalControlListBoxItem(MyStringId.GetOrCompute(possibleParentSystem.Drill.CustomName), MyStringId.NullOrEmpty, possibleParentSystem);
                    items.Add(item);

                    if (childSystem.Settings.ParentSystem == possibleParentSystem)
                    {
                        selectedItems.Add(item);
                    }
                }
            }
        }

        private static void ApplyParentValues(NanobotDrillSystemBlock childSystem)
        {
            //ToDo
        }

        /// <summary>
        /// Callback to add custom controls
        /// </summary>
        private static void CustomControlGetter(IMyTerminalBlock block, List<IMyTerminalControl> controls)
        {
            if (block.BlockDefinition.SubtypeName.StartsWith("SELtd") && block.BlockDefinition.SubtypeName.Contains("NanobotDrillSystem"))
            {
                foreach (var item in CustomControls)
                    controls.Add(item);
            }
        }
    }
}