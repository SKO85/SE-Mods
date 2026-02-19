using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces.Terminal;
using SKONanobotBuildAndRepairSystem.Localization;
using SKONanobotBuildAndRepairSystem.Models;
using SKONanobotBuildAndRepairSystem.Terminal;
using SKONanobotBuildAndRepairSystem.Utils;
using System;
using System.Collections.Generic;
using VRageMath;

namespace SKONanobotBuildAndRepairSystem
{
    [Flags]
    public enum SearchModes
    {
        /// <summary>
        /// Search Target blocks only inside connected blocks
        /// </summary>
        Grids = 0x0001,

        /// <summary>
        /// Search Target blocks in bounding box independend of connection
        /// </summary>
        BoundingBox = 0x0002
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
    public enum AutoWeldOptions
    {
        FunctionalOnly = 0x0001
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

    public static class NanobotTerminal
    {
        public const float SATURATION_DELTA = 0.8f;
        public const float VALUE_DELTA = 0.55f;
        public const float VALUE_COLORIZE_DELTA = 0.1f;

        public static bool CustomControlsInit = false;
        internal static List<IMyTerminalControl> CustomControls = new List<IMyTerminalControl>();

        internal static IMyTerminalControl _HelpOthers;
        internal static IMyTerminalControlSeparator _SeparateWeldOptions;

        internal static IMyTerminalControlSlider _IgnoreColorHueSlider;
        internal static IMyTerminalControlSlider _IgnoreColorSaturationSlider;
        internal static IMyTerminalControlSlider _IgnoreColorValueSlider;

        internal static IMyTerminalControlSlider _GrindColorHueSlider;
        internal static IMyTerminalControlSlider _GrindColorSaturationSlider;
        internal static IMyTerminalControlSlider _GrindColorValueSlider;

        internal static IMyTerminalControlOnOffSwitch _WeldEnableDisableSwitch;
        internal static IMyTerminalControlButton _WeldPriorityButtonUp;
        internal static IMyTerminalControlButton _WeldPriorityButtonDown;
        internal static IMyTerminalControlListbox _WeldPriorityListBox;
        internal static IMyTerminalControlOnOffSwitch _GrindEnableDisableSwitch;
        internal static IMyTerminalControlButton _GrindPriorityButtonUp;
        internal static IMyTerminalControlButton _GrindPriorityButtonDown;
        internal static IMyTerminalControlListbox _GrindPriorityListBox;

        internal static IMyTerminalControlOnOffSwitch _ComponentCollectEnableDisableSwitch;
        internal static IMyTerminalControlButton _ComponentCollectPriorityButtonUp;
        internal static IMyTerminalControlButton _ComponentCollectPriorityButtonDown;
        internal static IMyTerminalControlListbox _ComponentCollectPriorityListBox;
        internal static IMyTerminalControlOnOffSwitch _ComponentCollectIfIdleSwitch;

        /// <summary>
        /// Check an return the GameLogic object
        /// </summary>
        /// <param name="block"></param>
        /// <returns></returns>
        public static NanobotSystem GetSystem(IMyTerminalBlock block)
        {
            if (block != null && block.GameLogic != null) return block.GameLogic.GetAs<NanobotSystem>();
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
                    // !! As we can't subtype here they will be also available in every Shipwelder but without function !!

                    MyAPIGateway.TerminalControls.CustomControlGetter += CustomControlGetter;

                    IMyTerminalControlLabel label;
                    IMyTerminalControlCheckbox checkbox;
                    IMyTerminalControlCombobox comboBox;
                    IMyTerminalControlSeparator separateArea;
                    IMyTerminalControlSlider slider;
                    IMyTerminalControlOnOffSwitch onoffSwitch;
                    IMyTerminalControlButton button;

                    var weldingAllowed = (Mod.Settings.Welder.AllowedWorkModes & (WorkModes.WeldBeforeGrind | WorkModes.GrindBeforeWeld | WorkModes.GrindIfWeldGetStuck | WorkModes.WeldOnly)) != 0;
                    var grindingAllowed = (Mod.Settings.Welder.AllowedWorkModes & (WorkModes.WeldBeforeGrind | WorkModes.GrindBeforeWeld | WorkModes.GrindIfWeldGetStuck | WorkModes.GrindOnly)) != 0;
                    var janitorAllowed = grindingAllowed && (Mod.Settings.Welder.AllowedGrindJanitorRelations != 0);
                    var janitorAllowedNoOwnership = janitorAllowed && ((Mod.Settings.Welder.AllowedGrindJanitorRelations & AutoGrindRelation.NoOwnership) != 0);
                    var janitorAllowedOwner = janitorAllowed && ((Mod.Settings.Welder.AllowedGrindJanitorRelations & AutoGrindRelation.Owner) != 0);
                    var janitorAllowedFactionShare = janitorAllowed && ((Mod.Settings.Welder.AllowedGrindJanitorRelations & AutoGrindRelation.FactionShare) != 0);
                    var janitorAllowedNeutral = janitorAllowed && ((Mod.Settings.Welder.AllowedGrindJanitorRelations & AutoGrindRelation.Neutral) != 0);
                    var janitorAllowedEnemies = janitorAllowed && ((Mod.Settings.Welder.AllowedGrindJanitorRelations & AutoGrindRelation.Enemies) != 0);

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
                        return system != null && system.ComponentCollectPriority != null && system.ComponentCollectPriority.Selected != null && system.Settings.SearchMode == SearchModes.BoundingBox && !Mod.Settings.Welder.CollectPriorityFixed;
                    };

                    List<IMyTerminalControl> controls;
                    MyAPIGateway.TerminalControls.GetControls<IMyShipWelder>(out controls);

                    _HelpOthers = controls.Find((ctrl) =>
                    {
                        var cb = ctrl as IMyTerminalControlCheckbox;
                        return (cb != null && ctrl.Id == "helpOthers");
                    });

                    // --- General ---
                    label = Labels.Create("ModeSettings", Texts.ModeSettings_Headline);
                    {
                        // --- Select search mode ---
                        var onlyOneAllowed = (Mod.Settings.Welder.AllowedSearchModes & (Mod.Settings.Welder.AllowedSearchModes - 1)) == 0;
                        comboBox = ComboBoxes.CreateSearchMode(onlyOneAllowed, isReadonly, isBaRSystem);
                        CreateProperty(comboBox, onlyOneAllowed);

                        // --- Select work mode ---
                        onlyOneAllowed = (Mod.Settings.Welder.AllowedWorkModes & (Mod.Settings.Welder.AllowedWorkModes - 1)) == 0;
                        comboBox = ComboBoxes.CreateWorkMode(onlyOneAllowed, isReadonly, isBaRSystem);
                        CreateProperty(comboBox, onlyOneAllowed);
                    }

                    // --- Welding ---
                    label = Labels.Create("WeldingSettings", Texts.WeldSettings_Headline);
                    {
                        // --- Set Color that marks blocks as 'ignore' ---
                        {
                            onoffSwitch = OnOffSwitches.CreateUseIgnoreColor(weldingAllowed, isWeldingAllowed, isReadonly, isBaRSystem);
                            CreateProperty(onoffSwitch, Mod.Settings.Welder.UseIgnoreColorFixed);

                            Func<IMyTerminalBlock, bool> colorPickerEnabled = (block) =>
                            {
                                var system = GetSystem(block);
                                return system != null && (system.Settings.Flags & SyncBlockSettings.Settings.UseIgnoreColor) != 0 && !Mod.Settings.Welder.UseIgnoreColorFixed && isWeldingAllowed(block);
                            };

                            // IgnoreColorPickCurrent:
                            button = Buttons.CreateIgnoreColorPickCurrent(colorPickerEnabled, isWeldingAllowed);

                            // IgnoreColorSetAsCurrent:
                            button = Buttons.CreateIgnoreColorSetAsCurrent(colorPickerEnabled, isWeldingAllowed);

                            // IgnoreColorHue:
                            slider = Sliders.IgnoreColorHue(colorPickerEnabled, isWeldingAllowed);
                            _IgnoreColorHueSlider = slider;

                            // IgnoreColorSaturation:
                            slider = Sliders.IgnoreColorSaturation(colorPickerEnabled, isWeldingAllowed);
                            _IgnoreColorSaturationSlider = slider;

                            // IgnoreColorValue:
                            slider = Sliders.IgnoreColorValue(colorPickerEnabled, isWeldingAllowed);
                            _IgnoreColorValueSlider = slider;

                            // BuildAndRepair.IgnoreColor:
                            Properties.IgnoreColor();
                        }

                        // Weld Options
                        _SeparateWeldOptions = Separators.Create("SeparateWeldOptions", isWeldingAllowed);
                        {
                            // ---helpOthers
                            // Moved here

                            // --- AllowBuild CheckBox ---
                            onoffSwitch = OnOffSwitches.CreateAllowBuild(weldingAllowed, isWeldingAllowed, isReadonly, isBaRSystem);
                            CreateProperty(onoffSwitch, Mod.Settings.Welder.AllowBuildFixed || !weldingAllowed);

                            // --Weld to functional only ---
                            onoffSwitch = OnOffSwitches.CreateWeldOptionFunctionalOnly(weldingAllowed, isWeldingAllowed, isReadonly, isBaRSystem);
                            CreateProperty(onoffSwitch, !weldingAllowed);
                        }

                        // --- Priority Welding ---
                        separateArea = Separators.Create("SeparateWeldPrio", isWeldingAllowed);
                        {
                            // --- WeldPriority ---
                            onoffSwitch = OnOffSwitches.CreateWeldPriority(isWeldingAllowed, isReadonly, isBaRSystem);
                            _WeldEnableDisableSwitch = onoffSwitch;

                            // --- Weld Priority Button Up ---
                            button = Buttons.CreateWeldPriorityUp(isWeldingAllowed);
                            _WeldPriorityButtonUp = button;

                            // --- Weld Priority Button Down ---
                            button = Buttons.CreateWeldPriorityDown(isWeldingAllowed);
                            _WeldPriorityButtonDown = button;

                            // --- List Weld Priority ---
                            var listbox = ListBoxes.CreateWeldPriority(weldingAllowed, isReadonly, isBaRSystem, isWeldingAllowed);
                            _WeldPriorityListBox = listbox;
                        }
                    }

                    // --- Grinding ---
                    label = Labels.Create("GrindingSettings", Texts.GrindSettings_Headline);
                    {
                        // --- Set Color that marks blocks as 'grind' ---
                        {
                            // --- UseGrindColor ---
                            onoffSwitch = OnOffSwitches.CreateUseGrindColor(grindingAllowed, isGrindingAllowed, isReadonly, isBaRSystem);
                            CreateProperty(onoffSwitch, Mod.Settings.Welder.UseGrindColorFixed || !grindingAllowed);

                            Func<IMyTerminalBlock, bool> colorPickerEnabled = (block) =>
                            {
                                var system = GetSystem(block);
                                return system != null && (system.Settings.Flags & SyncBlockSettings.Settings.UseGrindColor) != 0 && !Mod.Settings.Welder.UseGrindColorFixed && isGrindingAllowed(block);
                            };

                            // --- GrindColorPickCurrent ---
                            button = Buttons.CreateGrindColorPickCurrent(colorPickerEnabled, isGrindingAllowed);

                            // --- GrindColorSetAsCurrent ---
                            button = Buttons.CreateGrindColorSetAsCurrent(colorPickerEnabled, isGrindingAllowed);

                            // --- GrindColorHue ---
                            slider = Sliders.GrindColorHue(colorPickerEnabled, isGrindingAllowed);
                            _GrindColorHueSlider = slider;

                            // --- GrindColorSaturation ---
                            slider = Sliders.GrindColorSaturation(colorPickerEnabled, isGrindingAllowed);
                            _GrindColorSaturationSlider = slider;

                            // --- GrindColorValue ---
                            slider = Sliders.GrindColorValue(colorPickerEnabled, isGrindingAllowed);
                            _GrindColorValueSlider = slider;

                            // --- BuildAndRepair.GrindColor ---
                            Properties.GrindColor();
                        }

                        // --- Enable Janitor grinding ---
                        separateArea = Separators.Create("SeparateGrindJanitor", isJanitorAllowed);
                        {
                            // --- Grind enemy ---
                            onoffSwitch = OnOffSwitches.CreateGrindJanitorEnemies(janitorAllowedEnemies, isJanitorAllowedEnemies, isReadonly, isBaRSystem);
                            CreateProperty(onoffSwitch, Mod.Settings.Welder.UseGrindJanitorFixed || !janitorAllowedEnemies);

                            // --- Grind not owned ---
                            onoffSwitch = OnOffSwitches.CreateGrindJanitorNotOwned(janitorAllowedNoOwnership, isJanitorAllowedNoOwnership, isReadonly, isBaRSystem);
                            CreateProperty(onoffSwitch, Mod.Settings.Welder.UseGrindJanitorFixed || !janitorAllowedNoOwnership);

                            // --- Grind Neutrals ---
                            onoffSwitch = OnOffSwitches.CreateGrindJanitorNeutrals(janitorAllowedNeutral, isJanitorAllowedNeutral, isReadonly, isBaRSystem);
                            CreateProperty(onoffSwitch, Mod.Settings.Welder.UseGrindJanitorFixed || !janitorAllowedNeutral);
                        }

                        // --- Grind Options ---
                        separateArea = Separators.Create("SeparateGrindOptions", isJanitorAllowed);
                        {
                            // --- Grind Disable only ---
                            onoffSwitch = OnOffSwitches.CreateGrindJanitorOptionDisableOnly(grindingAllowed, isJanitorAllowed, isReadonly, isBaRSystem);
                            CreateProperty(onoffSwitch, Mod.Settings.Welder.UseGrindJanitorFixed || !grindingAllowed);

                            // --- Grind Hack only ---
                            onoffSwitch = OnOffSwitches.CreateGrindJanitorOptionHackOnly(grindingAllowed, isJanitorAllowed, isReadonly, isBaRSystem);
                            CreateProperty(onoffSwitch, Mod.Settings.Welder.UseGrindJanitorFixed || !grindingAllowed);
                        }

                        // --- Grind Priority ---
                        separateArea = Separators.Create("SeparateGrindPrio", isGrindingAllowed);
                        {
                            // --- GrindPriority ---
                            onoffSwitch = OnOffSwitches.CreateGrindPriority(isGrindingAllowed, isReadonly, isBaRSystem);
                            _GrindEnableDisableSwitch = onoffSwitch;

                            // --- GrindPriorityUp ---
                            button = Buttons.CreateGrindPriorityUp(isGrindingAllowed);
                            _GrindPriorityButtonUp = button;

                            // --- GrindPriorityDown ---
                            button = Buttons.CreateGrindPriorityDown(isGrindingAllowed);
                            _GrindPriorityButtonDown = button;

                            // --- GrindPriority ---
                            var listbox = ListBoxes.CreateGrindPriority(grindingAllowed, isGrindingAllowed, isReadonly, isBaRSystem);
                            _GrindPriorityListBox = listbox;

                            // --- GrindIgnorePriorityOrder ---
                            onoffSwitch = OnOffSwitches.CreateGrindIgnorePriorityOrder(grindingAllowed, isGrindingAllowed, isReadonly, isBaRSystem);
                            CreateProperty(onoffSwitch);

                            // --- GrindNearFirst ---
                            onoffSwitch = OnOffSwitches.CreateGrindNearFirst(grindingAllowed, isGrindingAllowed, isReadonly, isBaRSystem);
                            CreateProperty(onoffSwitch);

                            // --- GrindFarFirst ---
                            onoffSwitch = OnOffSwitches.CreateGrindFarFirst(grindingAllowed, isGrindingAllowed, isReadonly, isBaRSystem);
                            CreateProperty(onoffSwitch);

                            // --- GrindSmallestGridFirst ---
                            onoffSwitch = OnOffSwitches.CreateGrindSmallestGridFirst(grindingAllowed, isGrindingAllowed, isReadonly, isBaRSystem);
                            CreateProperty(onoffSwitch);
                        }
                    }

                    // --- Collecting ---
                    label = Labels.Create("CollectingSettings", Texts.CollectSettings_Headline);
                    {
                        // --- Collect floating objects ---
                        {
                            // --- CollectPriority ---
                            onoffSwitch = OnOffSwitches.CreateCollectPriority(isChangeCollectPriorityPossible, isReadonly, isBaRSystem);
                            _ComponentCollectEnableDisableSwitch = onoffSwitch;

                            // --- CollectPriorityUp ---
                            button = Buttons.CreateCollectPriorityUp(isChangeCollectPriorityPossible);
                            _ComponentCollectPriorityButtonUp = button;

                            // --- CollectPriorityUp ---
                            button = Buttons.CreateCollectPriorityDown(isChangeCollectPriorityPossible);
                            _ComponentCollectPriorityButtonDown = button;

                            // --- CollectPriority ---
                            var listbox = ListBoxes.CreateCollectPriority(isCollectPossible, isReadonly, isBaRSystem);
                            _ComponentCollectPriorityListBox = listbox;

                            // --- Collect if idle ---
                            onoffSwitch = OnOffSwitches.CreateCollectIfIdle(isCollectPossible, isReadonly);
                            _ComponentCollectIfIdleSwitch = onoffSwitch;
                            CreateProperty(onoffSwitch, Mod.Settings.Welder.CollectIfIdleFixed);

                            // --- Push Ingot/ore immediately ---
                            onoffSwitch = OnOffSwitches.CreatePushIngotOreImmediately(isReadonly, isBaRSystem);
                            CreateProperty(onoffSwitch, Mod.Settings.Welder.PushIngotOreImmediatelyFixed);

                            //--- Push Items immediately ---
                            onoffSwitch = OnOffSwitches.CreatePushItemsImmediately(isReadonly, isBaRSystem);
                            CreateProperty(onoffSwitch, Mod.Settings.Welder.PushItemsImmediatelyFixed);

                            // --- Push Component immediately ---
                            onoffSwitch = OnOffSwitches.CreatePushComponentImmediately(isReadonly, isBaRSystem);
                            CreateProperty(onoffSwitch, Mod.Settings.Welder.PushComponentImmediatelyFixed);
                        }
                    }

                    // --- Highlight Area ---
                    separateArea = Separators.Create("SeparateArea", (b) => { return true; });
                    {
                        Func<IMyTerminalBlock, float> getLimitOffsetMin = (block) =>
                        {
                            var system = GetSystem(block);
                            return system != null && system.Settings != null ? -system.Settings.MaximumOffset : -NanobotSystem.WELDER_OFFSET_MAX_IN_M;
                        };

                        Func<IMyTerminalBlock, float> getLimitOffsetMax = (block) =>
                        {
                            var system = GetSystem(block);
                            return system != null && system.Settings != null ? system.Settings.MaximumOffset : NanobotSystem.WELDER_OFFSET_MAX_IN_M;
                        };

                        Func<IMyTerminalBlock, float> getLimitMin = (block) => NanobotSystem.WELDER_RANGE_MIN_IN_M;

                        Func<IMyTerminalBlock, float> getLimitMax = (block) =>
                        {
                            var system = GetSystem(block);
                            return system != null && system.Settings != null ? system.Settings.MaximumRange : NanobotSystem.WELDER_RANGE_MAX_IN_M;
                        };

                        // --- Show Area ---
                        onoffSwitch = OnOffSwitches.CreateShowArea(isReadonly, isBaRSystem);
                        CreateProperty(onoffSwitch, Mod.Settings.Welder.ShowAreaFixed);

                        // --- Slider Offset ---
                        slider = Sliders.AreaOffsetLeftRight(getLimitOffsetMin, getLimitOffsetMax, isReadonly, isBaRSystem);
                        CreateProperty(slider, Mod.Settings.Welder.AreaOffsetFixed);

                        // --- AreaOffsetUpDown ---
                        slider = Sliders.AreaOffsetUpDown(getLimitOffsetMin, getLimitOffsetMax, isReadonly, isBaRSystem);
                        CreateProperty(slider, Mod.Settings.Welder.AreaOffsetFixed);

                        // --- AreaOffsetFrontBack ---
                        slider = Sliders.AreaOffsetFrontBack(getLimitOffsetMin, getLimitOffsetMax, isReadonly, isBaRSystem);
                        CreateProperty(slider, Mod.Settings.Welder.AreaOffsetFixed);

                        // --- Slider Area ---
                        slider = Sliders.AreaWidth(getLimitMin, getLimitMax, isReadonly, isBaRSystem);
                        CreateProperty(slider, Mod.Settings.Welder.AreaSizeFixed);

                        // --- AreaHeight ---
                        slider = Sliders.AreaHeight(getLimitMin, getLimitMax, isReadonly, isBaRSystem);
                        CreateProperty(slider, Mod.Settings.Welder.AreaSizeFixed);

                        // --- AreaDepth ---
                        slider = Sliders.AreaDepth(getLimitMin, getLimitMax, isReadonly, isBaRSystem);
                        CreateProperty(slider, Mod.Settings.Welder.AreaSizeFixed);

                        // --- Sound enabled ---
                        separateArea = Separators.Create("SeparateOther", (_) => true);

                        // --- SoundVolume ---
                        slider = Sliders.SoundVolume(isReadonly, isBaRSystem);
                        CreateProperty(slider, Mod.Settings.Welder.SoundVolumeFixed);
                    }

                    // -- Script Control
                    if (!Mod.Settings.Welder.ScriptControllFixed)
                    {
                        separateArea = Separators.Create("SeparateScriptControl", (_) => true);

                        // --- ScriptControlled ---
                        onoffSwitch = OnOffSwitches.ScriptControlled(isBaRSystem);
                        CreateProperty(onoffSwitch);

                        // =========== Properties ============

                        // --- Scripting support for Priority and enabling Weld BlockClasses
                        Properties.WeldPriorityList();
                        Properties.SetWeldPriority();
                        Properties.GetWeldPriority();
                        Properties.SetWeldEnabled();
                        Properties.GetWeldEnabled();

                        // --- Scripting support for Priority and enabling GrindWeld BlockClasses
                        Properties.GrindPriorityList();
                        Properties.SetGrindPriority();
                        Properties.GetGrindPriority();
                        Properties.SetGrindEnabled();
                        Properties.GetGrindEnabled();

                        // --- Scripting support for Priority and enabling ComponentClasses
                        Properties.ComponentClassList();
                        Properties.SetCollectPriority();
                        Properties.GetCollectPriority();
                        Properties.SetCollectEnabled();
                        Properties.GetCollectEnabled();

                        // --- Working Lists
                        Properties.MissingComponents();
                        Properties.PossibleTargets();
                        Properties.PossibleGrindTargets();
                        Properties.PossibleCollectTargets();

                        // --- Control welding
                        Properties.CurrentPickedTarget();
                        Properties.CurrentTarget();

                        // --- Control grinding
                        Properties.CurrentPickedGrindTarget();
                        Properties.CurrentGrindTarget();

                        // --- Publish functions to scripting
                        Properties.ProductionBlockEnsureQueued();
                        Properties.InventoryNeededComponents4Blueprint();
                    }
                }
                catch (Exception ex)
                {
                    Logging.Instance.Write(Logging.Level.Error, "NanobotBuildAndRepairSystemTerminal: InitializeControls exception: {0}", ex);
                }
            }
        }

        private static void CreateProperty<T>(IMyTerminalValueControl<T> control, bool readOnly = false)
        {
            var property = MyAPIGateway.TerminalControls.CreateProperty<T, IMyShipWelder>("BuildAndRepair." + control.Id);
            property.SupportsMultipleBlocks = false;
            property.Getter = control.Getter;
            if (!readOnly) property.Setter = control.Setter;
            MyAPIGateway.TerminalControls.AddControl<IMyShipWelder>(property);
        }

        internal static Vector3 CheckConvertToHSVColor(Vector3 value)
        {
            if (value.X < 0f) value.X = 0f;
            if (value.X > 360f) value.X = 360f;
            if (value.Y < 0f) value.Y = 0f;
            if (value.Y > 100f) value.Y = 100f;
            if (value.Z < 0f) value.Z = 0f;
            if (value.Z > 100f) value.Z = 100f;

            return new Vector3(value.X / 360f,
                              (value.Y / 100f) - NanobotTerminal.SATURATION_DELTA,
                              (value.Z / 100f) - NanobotTerminal.VALUE_DELTA + NanobotTerminal.VALUE_COLORIZE_DELTA);
        }

        internal static Vector3 ConvertFromHSVColor(Vector3 value)
        {
            return new Vector3(value.X * 360f,
                              (value.Y + SATURATION_DELTA) * 100f,
                              (value.Z + VALUE_DELTA - VALUE_COLORIZE_DELTA) * 100f);
        }

        internal static void UpdateVisual(IMyTerminalControl control)
        {
            if (control != null) control.UpdateVisual();
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
    }
}