namespace SKONanobotBuildAndRepairSystem
{
    using System;
    using System.Collections.Generic;
    using System.Xml.Serialization;
    using ProtoBuf;
    using Sandbox.Game.EntityComponents;
    using Sandbox.ModAPI;
    using VRage.Game;
    using VRage.Game.ModAPI;
    using VRage.ModAPI;
    using VRageMath;

    /// <summary>
    /// The settings for Mod
    /// </summary>
    [ProtoContract(SkipConstructor = true, UseProtoMembersOnly = true)]
    public class SyncModSettings
    {
        private const int CurrentSettingsVersion = 6;

        [ProtoMember(2000), XmlElement, XmlIgnore]
        public int Version { get; set; }

        [XmlElement]
        public bool DisableLocalization { get; set; }

        [ProtoMember(1), XmlElement]
        public Logging.Level LogLevel { get; set; }

        [XmlIgnore]
        public TimeSpan SourcesUpdateInterval { get; set; }

        [XmlIgnore]
        public TimeSpan TargetsUpdateInterval { get; set; }

        [XmlIgnore]
        public TimeSpan FriendlyDamageTimeout { get; set; }

        [XmlIgnore]
        public TimeSpan FriendlyDamageCleanup { get; set; }

        [ProtoMember(2), XmlElement]
        public int Range { get; set; }

        [ProtoMember(3), XmlElement, XmlIgnore]
        public long SourcesAndTargetsUpdateIntervalTicks
        {
            get { return TargetsUpdateInterval.Ticks; }
            set
            {
                TargetsUpdateInterval = new TimeSpan(value);
                SourcesUpdateInterval = new TimeSpan(value * 6);
            }
        }

        [ProtoMember(4), XmlElement, XmlIgnore]
        public long FriendlyDamageTimeoutTicks
        {
            get { return FriendlyDamageTimeout.Ticks; }
            set { FriendlyDamageTimeout = new TimeSpan(value); }
        }

        [ProtoMember(5), XmlElement, XmlIgnore]
        public long FriendlyDamageCleanupTicks
        {
            get { return FriendlyDamageCleanup.Ticks; }
            set { FriendlyDamageCleanup = new TimeSpan(value); }
        }

        [ProtoMember(8), XmlElement, XmlIgnore]
        public float MaximumRequiredElectricPowerTransport { get; set; }

        [ProtoMember(9), XmlElement, XmlIgnore]
        public float MaximumRequiredElectricPowerStandby { get; set; }

        [ProtoMember(10), XmlElement]
        public SyncModSettingsWelder Welder { get; set; }

        [ProtoMember(20), XmlElement, XmlIgnore]
        public int MaxBackgroundTasks { get; set; }

        [ProtoMember(21), XmlElement, XmlIgnore]
        public int MaximumOffset { get; set; }

        [ProtoMember(22), XmlElement]
        public bool AutoPowerOffOnIdleForced { get; set; }

        [ProtoMember(23), XmlElement]
        public int AutoPowerOffOnIdleMinutes { get; set; }

        [ProtoMember(24), XmlElement]
        public bool AllowEnemyGrindingInMotion { get; set; }

        public SyncModSettings()
        {
            Version = CurrentSettingsVersion;
            DisableLocalization = false;
            LogLevel = Logging.Level.Error; //Default
            MaxBackgroundTasks = Constants.MaxBackgroundTasks_Default;
            TargetsUpdateInterval = TimeSpan.FromSeconds(10);
            SourcesUpdateInterval = TimeSpan.FromSeconds(60);
            FriendlyDamageTimeout = TimeSpan.FromSeconds(60);
            FriendlyDamageCleanup = TimeSpan.FromSeconds(10);
            Range = Constants.WELDER_RANGE_DEFAULT_IN_M;
            MaximumOffset = Constants.WELDER_OFFSET_MAX_DEFAULT_IN_M;
            MaximumRequiredElectricPowerStandby = Constants.WELDER_REQUIRED_ELECTRIC_POWER_STANDBY_DEFAULT;
            MaximumRequiredElectricPowerTransport = Constants.WELDER_REQUIRED_ELECTRIC_POWER_TRANSPORT_DEFAULT;
            Welder = new SyncModSettingsWelder();
            AutoPowerOffOnIdleForced = Constants.AutoPowerOffOnIdleForcedDefault;
            AutoPowerOffOnIdleMinutes = Constants.AutoPowerOffOnIdleMinutesDefault;
            AllowEnemyGrindingInMotion = Constants.AllowEnemyGrindingInMotionDefault;            
        }

        public static SyncModSettings Load()
        {
            SyncModSettings settings = null;
            try
            {
                if (MyAPIGateway.Utilities.FileExistsInWorldStorage("ModSettings.xml", typeof(SyncModSettings)))
                {
                    using (var reader = MyAPIGateway.Utilities.ReadFileInWorldStorage("ModSettings.xml", typeof(SyncModSettings)))
                    {
                        settings = MyAPIGateway.Utilities.SerializeFromXML<SyncModSettings>(reader.ReadToEnd());
                        Logging.Instance?.Write("NanobotBuildAndRepairSystemSettings: Loaded from world file.");
                    }
                }
                else if (MyAPIGateway.Utilities.FileExistsInLocalStorage("ModSettings.xml", typeof(SyncModSettings)))
                {
                    using (var reader = MyAPIGateway.Utilities.ReadFileInLocalStorage("ModSettings.xml", typeof(SyncModSettings)))
                    {
                        settings = MyAPIGateway.Utilities.SerializeFromXML<SyncModSettings>(reader.ReadToEnd());
                        Logging.Instance?.Write("NanobotBuildAndRepairSystemSettings: Loaded from local storage.");
                    }
                }

                if (settings != null)
                {
                    var adjusted = AdjustSettings(settings);
                    if (settings.MaxBackgroundTasks > Constants.MaxBackgroundTasks_Max)
                    {
                        settings.MaxBackgroundTasks = Constants.MaxBackgroundTasks_Max;
                        adjusted = true;
                    }
                    else if (settings.MaxBackgroundTasks < Constants.MaxBackgroundTasks_Min)
                    {
                        settings.MaxBackgroundTasks = Constants.MaxBackgroundTasks_Min;
                        adjusted = true;
                    }

                    if (settings.Range > Constants.WELDER_RANGE_MAX_IN_M)
                    {
                        settings.Range = Constants.WELDER_RANGE_MAX_IN_M;
                        adjusted = true;
                    }
                    else if (settings.Range < Constants.WELDER_RANGE_MIN_IN_M)
                    {
                        settings.Range = Constants.WELDER_RANGE_MIN_IN_M;
                        adjusted = true;
                    }

                    if (settings.MaximumOffset > Constants.WELDER_OFFSET_MAX_IN_M)
                    {
                        settings.MaximumOffset = Constants.WELDER_OFFSET_MAX_IN_M;
                        adjusted = true;
                    }
                    else if (settings.MaximumOffset < 0)
                    {
                        settings.MaximumOffset = 0;
                        adjusted = true;
                    }

                    if (settings.Welder.WeldingMultiplier < Constants.WELDING_GRINDING_MULTIPLIER_MIN)
                    {
                        settings.Welder.WeldingMultiplier = Constants.WELDING_GRINDING_MULTIPLIER_MIN;
                        adjusted = true;
                    }
                    else if (settings.Welder.WeldingMultiplier >= Constants.WELDING_GRINDING_MULTIPLIER_MAX)
                    {
                        settings.Welder.WeldingMultiplier = Constants.WELDING_GRINDING_MULTIPLIER_MAX;
                        adjusted = true;
                    }

                    if (settings.Welder.GrindingMultiplier < Constants.WELDING_GRINDING_MULTIPLIER_MIN)
                    {
                        settings.Welder.GrindingMultiplier = Constants.WELDING_GRINDING_MULTIPLIER_MIN;
                        adjusted = true;
                    }
                    else if (settings.Welder.GrindingMultiplier >= Constants.WELDING_GRINDING_MULTIPLIER_MAX)
                    {
                        settings.Welder.GrindingMultiplier = Constants.WELDING_GRINDING_MULTIPLIER_MAX;
                        adjusted = true;
                    }

                    // Set back to default if someone used strange values for auto-power-off. Ignore their value.
                    if(settings.AutoPowerOffOnIdleMinutes < 1 || settings.AutoPowerOffOnIdleMinutes >= TimeSpan.MaxValue.TotalMinutes)
                    {
                        settings.AutoPowerOffOnIdleMinutes = Constants.AutoPowerOffOnIdleMinutesDefault;
                    }

                    Logging.Instance?.Write(Logging.Level.Info, "NanobotBuildAndRepairSystemSettings: Settings {0}", settings);
                    //if (adjusted) Save(settings, world); don't save file
                }
                else
                {
                    settings = new SyncModSettings() { Version = CurrentSettingsVersion };
                    //Save(settings, world); don't save file with default values
                }
            }
            catch (Exception ex)
            {
                Logging.Instance?.Write(Logging.Level.Error, "NanobotBuildAndRepairSystemSettings: Exception while loading: {0}", ex);
            }

            return settings;
        }

        public static void Save(SyncModSettings settings, bool world)
        {
            if (world)
            {
                using (var writer = MyAPIGateway.Utilities.WriteFileInWorldStorage("ModSettings.xml", typeof(SyncModSettings)))
                {
                    writer.Write(MyAPIGateway.Utilities.SerializeToXML(settings));
                }
            }
            else
            {
                using (var writer = MyAPIGateway.Utilities.WriteFileInLocalStorage("ModSettings.xml", typeof(SyncModSettings)))
                {
                    writer.Write(MyAPIGateway.Utilities.SerializeToXML(settings));
                }
            }
        }

        public static bool AdjustSettings(SyncModSettings settings)
        {
            if (settings.Version >= CurrentSettingsVersion) return false;

            Logging.Instance?.Write("NanobotBuildAndRepairSystemSettings: Settings have old version: {0} update to {1}", settings.Version, CurrentSettingsVersion);

            if (settings.Version <= 0) settings.LogLevel = Logging.Level.Error;
            if (settings.Version <= 4 && settings.Welder.AllowedSearchModes == 0) settings.Welder.AllowedSearchModes = SearchModes.Grids | SearchModes.BoundingBox;
            if (settings.Version <= 4 && settings.Welder.AllowedWorkModes == 0) settings.Welder.AllowedWorkModes = WorkModes.WeldBeforeGrind | WorkModes.GrindBeforeWeld | WorkModes.GrindIfWeldGetStuck | WorkModes.WeldOnly | WorkModes.GrindOnly;
            if (settings.Version <= 4 && settings.Welder.WeldingMultiplier == 0) settings.Welder.WeldingMultiplier = 1;
            if (settings.Version <= 4 && settings.Welder.GrindingMultiplier == 0) settings.Welder.GrindingMultiplier = 1;
            if (settings.Version <= 5 && settings.Welder.AllowedGrindJanitorRelations == 0) settings.Welder.AllowedGrindJanitorRelations = AutoGrindRelation.NoOwnership | AutoGrindRelation.Enemies | AutoGrindRelation.Neutral;

            settings.Version = CurrentSettingsVersion;
            return true;
        }
    }

    [ProtoContract(SkipConstructor = true, UseProtoMembersOnly = true)]
    public class SyncModSettingsWelder
    {
        [ProtoMember(1), XmlElement, XmlIgnore]
        public float MaximumRequiredElectricPowerWelding { get; set; }

        [ProtoMember(2), XmlElement, XmlIgnore]
        public float MaximumRequiredElectricPowerGrinding { get; set; }

        [ProtoMember(10), XmlElement]
        public float WeldingMultiplier { get; set; }

        [ProtoMember(11), XmlElement]
        public float GrindingMultiplier { get; set; }

        [ProtoMember(90), XmlElement, XmlIgnore]
        public SearchModes AllowedSearchModes { get; set; }

        [ProtoMember(91), XmlElement, XmlIgnore]
        public SearchModes SearchModeDefault { get; set; }

        [ProtoMember(101), XmlElement, XmlIgnore]
        public bool AllowBuildFixed { get; set; }

        [ProtoMember(102), XmlElement, XmlIgnore]
        public bool AllowBuildDefault { get; set; }

        [ProtoMember(105), XmlElement, XmlIgnore]
        public WorkModes AllowedWorkModes { get; set; }

        [ProtoMember(106), XmlElement, XmlIgnore]
        public WorkModes WorkModeDefault { get; set; }

        [ProtoMember(110), XmlElement, XmlIgnore]
        public bool UseIgnoreColorFixed { get; set; }

        [ProtoMember(111), XmlElement, XmlIgnore]
        public bool UseIgnoreColorDefault { get; set; }

        [ProtoMember(112), XmlArray, XmlIgnore]
        public float[] IgnoreColorDefault { get; set; }

        [ProtoMember(115), XmlElement, XmlIgnore]
        public bool UseGrindColorFixed { get; set; }

        [ProtoMember(116), XmlElement, XmlIgnore]
        public bool UseGrindColorDefault { get; set; }

        [ProtoMember(117), XmlArray, XmlIgnore]
        public float[] GrindColorDefault { get; set; }

        [ProtoMember(118), XmlElement, XmlIgnore]
        public bool UseGrindJanitorFixed { get; set; }

        [ProtoMember(119), XmlElement, XmlIgnore]
        public AutoGrindRelation UseGrindJanitorDefault { get; set; }

        [ProtoMember(120), XmlElement, XmlIgnore]
        public AutoGrindOptions GrindJanitorOptionsDefault { get; set; }

        [ProtoMember(121), XmlElement, XmlIgnore]
        public AutoGrindRelation AllowedGrindJanitorRelations { get; set; }

        [ProtoMember(125), XmlElement, XmlIgnore]
        public bool ShowAreaFixed { get; set; }

        [ProtoMember(130), XmlElement, XmlIgnore]
        public bool AreaSizeFixed { get; set; }

        [ProtoMember(131), XmlElement, XmlIgnore]
        public bool AreaOffsetFixed { get; set; }

        [ProtoMember(140), XmlElement, XmlIgnore]
        public bool PriorityFixed { get; set; }

        [ProtoMember(144), XmlElement, XmlIgnore]
        public bool CollectPriorityFixed { get; set; }

        [ProtoMember(145), XmlElement, XmlIgnore]
        public bool PushIngotOreImmediatelyFixed { get; set; }

        [ProtoMember(146), XmlElement, XmlIgnore]
        public bool PushIngotOreImmediatelyDefault { get; set; }

        [ProtoMember(147), XmlElement, XmlIgnore]
        public bool PushComponentImmediatelyFixed { get; set; }

        [ProtoMember(148), XmlElement, XmlIgnore]
        public bool PushComponentImmediatelyDefault { get; set; }

        [ProtoMember(149), XmlElement, XmlIgnore]
        public bool PushItemsImmediatelyFixed { get; set; }

        [ProtoMember(150), XmlElement, XmlIgnore]
        public bool PushItemsImmediatelyDefault { get; set; }

        [ProtoMember(156), XmlElement, XmlIgnore]
        public bool CollectIfIdleFixed { get; set; }

        [ProtoMember(157), XmlElement, XmlIgnore]
        public bool CollectIfIdleDefault { get; set; }

        [ProtoMember(160), XmlElement, XmlIgnore]
        public bool SoundVolumeFixed { get; set; }

        [ProtoMember(161), XmlElement, XmlIgnore]
        public float SoundVolumeDefault { get; set; }

        [ProtoMember(170), XmlElement, XmlIgnore]
        public bool ScriptControllFixed { get; set; }

        [ProtoMember(200), XmlElement, XmlIgnore]
        public VisualAndSoundEffects AllowedEffects { get; set; }

        public SyncModSettingsWelder()
        {
            MaximumRequiredElectricPowerWelding = Constants.WELDER_REQUIRED_ELECTRIC_POWER_WELDING_DEFAULT;
            MaximumRequiredElectricPowerGrinding = Constants.WELDER_REQUIRED_ELECTRIC_POWER_GRINDING_DEFAULT;

            WeldingMultiplier = 1f;
            GrindingMultiplier = 1f;

            AllowedSearchModes = SearchModes.Grids | SearchModes.BoundingBox;
            SearchModeDefault = SearchModes.Grids;

            AllowBuildFixed = false;
            AllowBuildDefault = true;

            AllowedWorkModes = WorkModes.GrindBeforeWeld | WorkModes.GrindIfWeldGetStuck | WorkModes.WeldBeforeGrind | WorkModes.WeldOnly | WorkModes.GrindOnly;
            WorkModeDefault = WorkModes.WeldBeforeGrind;

            UseIgnoreColorFixed = false;
            UseIgnoreColorDefault = true;
            IgnoreColorDefault = new float[] { 321f, 100f, 51f };

            UseGrindColorFixed = false;
            UseGrindColorDefault = true;
            GrindColorDefault = new float[] { 321f, 100f, 50f };

            UseGrindJanitorFixed = false;
            UseGrindJanitorDefault = AutoGrindRelation.Enemies | AutoGrindRelation.NoOwnership;
            GrindJanitorOptionsDefault = 0;
            AllowedGrindJanitorRelations = AutoGrindRelation.NoOwnership | AutoGrindRelation.Enemies | AutoGrindRelation.Neutral;

            ShowAreaFixed = false;
            AreaSizeFixed = false;
            AreaOffsetFixed = false;
            PriorityFixed = false;
            CollectPriorityFixed = false;

            PushIngotOreImmediatelyFixed = false;
            PushIngotOreImmediatelyDefault = true;
            PushItemsImmediatelyFixed = false;
            PushItemsImmediatelyDefault = true;
            PushComponentImmediatelyFixed = false;
            PushComponentImmediatelyDefault = false;

            CollectIfIdleDefault = false;

            SoundVolumeFixed = false;
            SoundVolumeDefault = Constants.WELDER_SOUND_VOLUME / 2;

            ScriptControllFixed = false;
            AllowedEffects = VisualAndSoundEffects.WeldingVisualEffect | VisualAndSoundEffects.WeldingSoundEffect
                     | VisualAndSoundEffects.GrindingVisualEffect | VisualAndSoundEffects.GrindingSoundEffect
                     | VisualAndSoundEffects.TransportVisualEffect;
        }
    }

    /// <summary>
    /// The settings for Block
    /// </summary>
    [ProtoContract(SkipConstructor = true, UseProtoMembersOnly = true)]
    public class SyncBlockSettings
    {
        [Flags]
        public enum Settings
        {
            AllowBuild = 0x00000001,
            ShowArea = 0x00000002,
            ScriptControlled = 0x00000004,
            UseIgnoreColor = 0x00000010,
            UseGrindColor = 0x00000020,
            GrindNearFirst = 0x00000100,
            GrindSmallestGridFirst = 0x00000200,
            ComponentCollectIfIdle = 0x00010000,
            PushIngotOreImmediately = 0x00020000,
            PushComponentImmediately = 0x00040000,
            PushItemsImmediately = 0x00080000
        }

        private BoundingBoxD _CorrectedAreaBoundingBox;
        private Vector3 _CorrectedAreaOffset;
        private Settings _Flags;
        private Vector3 _IgnoreColor;
        private uint _IgnoreColorPacked;
        private Vector3 _GrindColor;
        private uint _GrindColorPacked;
        private AutoGrindRelation _UseGrindJanitorOn;
        private AutoGrindOptions _GrindJanitorOptions;
        private Vector3 _AreaOffset;
        private Vector3 _AreaSize;
        private string _WeldPriority;
        private string _GrindPriority;
        private string _ComponentCollectPriority;
        private float _SoundVolume;
        private SearchModes _SearchMode;
        private WorkModes _WorkMode;
        private WeldTo _WeldTo;
        private uint _UseAutoPowerOffWhenIdle;
        private VRage.Game.ModAPI.Ingame.IMySlimBlock _CurrentPickedWeldingBlock;
        private VRage.Game.ModAPI.Ingame.IMySlimBlock _CurrentPickedGrindingBlock;
        private TimeSpan _LastStored;
        private TimeSpan _LastTransmitted;

        [XmlIgnore]
        public uint Changed { get; private set; }

        [ProtoMember(5), XmlElement]
        public Settings Flags
        {
            get
            {
                return _Flags;
            }
            set
            {
                if (_Flags != value)
                {
                    _Flags = value;
                    Changed = 3u;
                }
            }
        }

        [ProtoMember(20), XmlElement]
        public SearchModes SearchMode
        {
            get
            {
                return _SearchMode;
            }
            set
            {
                if (_SearchMode != value)
                {
                    _SearchMode = value;
                    Changed = 3u;
                }
            }
        }

        [ProtoMember(25), XmlElement]
        public WorkModes WorkMode
        {
            get
            {
                return _WorkMode;
            }
            set
            {
                if (_WorkMode != value)
                {
                    _WorkMode = value;
                    Changed = 3u;
                }
            }
        }        

        [ProtoMember(31), XmlElement]
        public Vector3 IgnoreColor
        {
            get
            {
                return _IgnoreColor;
            }
            set
            {
                if (_IgnoreColor != value)
                {
                    _IgnoreColor = value;
                    _IgnoreColorPacked = value.PackHSVToUint();
                    Changed = 3u;
                }
            }
        }

        public uint IgnoreColorPacked
        {
            get
            {
                return _IgnoreColorPacked;
            }
        }

        [ProtoMember(36), XmlElement]
        public Vector3 GrindColor
        {
            get
            {
                return _GrindColor;
            }
            set
            {
                if (_GrindColor != value)
                {
                    _GrindColor = value;
                    _GrindColorPacked = value.PackHSVToUint();
                    Changed = 3u;
                }
            }
        }

        public uint GrindColorPacked
        {
            get
            {
                return _GrindColorPacked;
            }
        }

        [ProtoMember(39), XmlElement]
        public AutoGrindRelation UseGrindJanitorOn
        {
            get
            {
                return _UseGrindJanitorOn;
            }
            set
            {
                if (_UseGrindJanitorOn != value)
                {
                    _UseGrindJanitorOn = value;
                    Changed = 3u;
                }
            }
        }

        [ProtoMember(40), XmlElement]
        public AutoGrindOptions GrindJanitorOptions
        {
            get
            {
                return _GrindJanitorOptions;
            }
            set
            {
                if (_GrindJanitorOptions != value)
                {
                    _GrindJanitorOptions = value;
                    Changed = 3u;
                }
            }
        }      

        //+X = Right   -Y = Left
        //+Y = Up      -Y = Down
        //+Z = Forward -Z = Backward
        [ProtoMember(50), XmlElement]
        public Vector3 AreaOffset
        {
            get
            {
                return _AreaOffset;
            }
            set
            {
                if (_AreaOffset != value)
                {
                    _AreaOffset = value;
                    Changed = 3u;
                    RecalcAreaBoundigBox();
                }
            }
        }

        [ProtoMember(51), XmlElement]
        public Vector3 AreaSize
        {
            get
            {
                return _AreaSize;
            }
            set
            {
                if (_AreaSize != value)
                {
                    _AreaSize = value;
                    Changed = 3u;
                    RecalcAreaBoundigBox();
                }
            }
        }

        private int? _AreaWidthLeft;

        [XmlElement]
        public int? AreaWidthLeft
        {
            get
            {
                return null;
            }
            set
            {
                _AreaWidthLeft = value;
                if (value != null) RecalcOffsetAndSize();
            }
        }

        private int? _AreaWidthRight;

        [XmlElement]
        public int? AreaWidthRight
        {
            get
            {
                return null;
            }
            set
            {
                _AreaWidthRight = value;
                if (value != null) RecalcOffsetAndSize();
            }
        }

        private int? _AreaWidthTop;

        [XmlElement]
        public int? AreaHeightTop
        {
            get
            {
                return null;
            }
            set
            {
                _AreaWidthTop = value;
                if (value != null) RecalcOffsetAndSize();
            }
        }

        private int? _AreaWidthBottom;

        [XmlElement]
        public int? AreaHeightBottom
        {
            get
            {
                return null;
            }
            set
            {
                _AreaWidthBottom = value;
                if (value != null) RecalcOffsetAndSize();
            }
        }

        private int? _AreaWidthFront;

        [XmlElement]
        public int? AreaDepthFront
        {
            get
            {
                return null;
            }
            set
            {
                _AreaWidthFront = value;
                if (value != null) RecalcOffsetAndSize();
            }
        }

        private int? _AreaWidthRear;

        [XmlElement]
        public int? AreaDepthRear
        {
            get
            {
                return null;
            }
            set
            {
                _AreaWidthRear = value;
                if (value != null) RecalcOffsetAndSize();
            }
        }

        [ProtoMember(61), XmlElement]
        public string WeldPriority
        {
            get
            {
                return _WeldPriority;
            }
            set
            {
                if (_WeldPriority != value)
                {
                    _WeldPriority = value;
                    Changed = 3u;
                }
            }
        }

        [ProtoMember(62), XmlElement]
        public string GrindPriority
        {
            get
            {
                return _GrindPriority;
            }
            set
            {
                if (_GrindPriority != value)
                {
                    _GrindPriority = value;
                    Changed = 3u;
                }
            }
        }

        [ProtoMember(65), XmlElement]
        public string ComponentCollectPriority
        {
            get
            {
                return _ComponentCollectPriority;
            }
            set
            {
                if (_ComponentCollectPriority != value)
                {
                    _ComponentCollectPriority = value;
                    Changed = 3u;
                }
            }
        }

        [ProtoMember(80), XmlElement]
        public float SoundVolume
        {
            get
            {
                return _SoundVolume;
            }
            set
            {
                if (_SoundVolume != value)
                {
                    _SoundVolume = value;
                    Changed = 3u;
                }
            }
        }

        [XmlIgnore]
        public VRage.Game.ModAPI.Ingame.IMySlimBlock CurrentPickedWeldingBlock
        {
            get
            {
                return _CurrentPickedWeldingBlock;
            }
            set
            {
                if (_CurrentPickedWeldingBlock != value)
                {
                    _CurrentPickedWeldingBlock = value;
                    Changed = 3u;
                }
            }
        }

        [ProtoMember(100), XmlElement]
        public SyncEntityId CurrentPickedWeldingBlockSync
        {
            get
            {
                return SyncEntityId.GetSyncId(_CurrentPickedWeldingBlock);
            }
            set
            {
                CurrentPickedWeldingBlock = SyncEntityId.GetItemAsSlimBlock(value);
            }
        }

        [XmlIgnore]
        public VRage.Game.ModAPI.Ingame.IMySlimBlock CurrentPickedGrindingBlock
        {
            get
            {
                return _CurrentPickedGrindingBlock;
            }
            set
            {
                if (_CurrentPickedGrindingBlock != value)
                {
                    _CurrentPickedGrindingBlock = value;
                    Changed = 3u;
                }
            }
        }

        [ProtoMember(105), XmlElement]
        public SyncEntityId CurrentPickedGrindingBlockSync
        {
            get
            {
                return SyncEntityId.GetSyncId(_CurrentPickedGrindingBlock);
            }
            set
            {
                CurrentPickedGrindingBlock = SyncEntityId.GetItemAsSlimBlock(value);
            }
        }

       

        [XmlIgnore]
        public int MaximumRange { get; private set; }

        [XmlIgnore]
        public int MaximumOffset { get; private set; }

        [XmlIgnore]
        public float TransportSpeed { get; private set; }

        [XmlIgnore]
        public float MaximumRequiredElectricPowerStandby { get; private set; }

        [XmlIgnore]
        public float MaximumRequiredElectricPowerWelding { get; private set; }

        [XmlIgnore]
        public float MaximumRequiredElectricPowerGrinding { get; private set; }

        [XmlIgnore]
        public float MaximumRequiredElectricPowerTransport { get; private set; }

        [ProtoMember(201), XmlElement]
        public WeldTo WeldTo
        {
            get
            {
                return _WeldTo;
            }
            set
            {
                if (_WeldTo != value)
                {
                    _WeldTo = value;
                    Changed = 3u;
                }
            }
        }

        [ProtoMember(202), XmlElement]
        public uint UseAutoPowerOffWhenIdle
        {
            get
            {
                return _UseAutoPowerOffWhenIdle;
            }
            set
            {
                if (_UseAutoPowerOffWhenIdle != value)
                {
                    _UseAutoPowerOffWhenIdle = value;
                    Changed = 3u;
                }
            }
        }



        //+X = Forward -X = Backward
        //+Y = Left    -Y = Right
        //+Z = Up      -Z = Down
        internal Vector3 CorrectedAreaOffset
        {
            get
            {
                return _CorrectedAreaOffset;
            }
        }

        internal BoundingBoxD CorrectedAreaBoundingBox
        {
            get
            {
                return _CorrectedAreaBoundingBox;
            }
        }

        public SyncBlockSettings() : this(null)
        {
        }

        public SyncBlockSettings(NanobotBuildAndRepairSystemBlock system)
        {
            _WeldPriority = string.Empty;
            _GrindPriority = string.Empty;
            _ComponentCollectPriority = string.Empty;
            CheckLimits(system, true);

            Changed = 0;
            _LastStored = MyAPIGateway.Session.ElapsedPlayTime.Add(TimeSpan.FromSeconds(60));
            _LastTransmitted = MyAPIGateway.Session.ElapsedPlayTime;

            _WeldTo = WeldTo.WeldToFull;
            _UseAutoPowerOffWhenIdle = 1;

            RecalcAreaBoundigBox();
        }

        public void TrySave(IMyEntity entity, Guid guid)
        {
            if ((Changed & 2u) == 0) return;
            if (MyAPIGateway.Session.ElapsedPlayTime.Subtract(_LastStored) < TimeSpan.FromSeconds(20)) return;
            Save(entity, guid);
        }

        public void Save(IMyEntity entity, Guid guid)
        {
            if (entity.Storage == null)
            {
                entity.Storage = new MyModStorageComponent();
            }

            var storage = entity.Storage;
            storage[guid] = GetAsXML();
            Changed = Changed & ~2u;
            _LastStored = MyAPIGateway.Session.ElapsedPlayTime;
        }

        public string GetAsXML()
        {
            return MyAPIGateway.Utilities.SerializeToXML(this);
        }

        public void ResetChanged()
        {
            Changed = Changed & ~2u;
        }

        public static SyncBlockSettings Load(NanobotBuildAndRepairSystemBlock system, Guid guid, NanobotBuildAndRepairSystemBlockPriorityHandling blockWeldPriority, NanobotBuildAndRepairSystemBlockPriorityHandling blockGrindPriority, NanobotBuildAndRepairSystemComponentPriorityHandling componentCollectPriority)
        {
            var storage = system.Entity.Storage;
            string data;
            SyncBlockSettings settings = null;

            if (storage != null && storage.TryGetValue(guid, out data))
            {
                try
                {
                    //Fix changed names
                    data = data.Replace("GrindColorNearFirst", "GrindNearFirst");

                    settings = MyAPIGateway.Utilities.SerializeFromXML<SyncBlockSettings>(data);

                    if (settings != null)
                    {
                        settings.RecalcAreaBoundigBox();
                        //Retrieve current settings or default if WeldPriority/GrindPriority/ComponentCollectPriority was empty
                        blockWeldPriority.SetEntries(settings.WeldPriority);
                        settings.WeldPriority = blockWeldPriority.GetEntries();

                        blockGrindPriority.SetEntries(settings.GrindPriority);
                        settings.GrindPriority = blockGrindPriority.GetEntries();

                        componentCollectPriority.SetEntries(settings.ComponentCollectPriority);
                        settings.ComponentCollectPriority = componentCollectPriority.GetEntries();

                        settings.Changed = 0;
                        settings._LastStored = MyAPIGateway.Session.ElapsedPlayTime.Add(TimeSpan.FromSeconds(60));
                        settings._LastTransmitted = MyAPIGateway.Session.ElapsedPlayTime;
                        return settings;
                    }
                }
                catch (Exception ex)
                {
                    Logging.Instance?.Write("SyncBlockSettings: Exception: " + ex);
                }
            }

            settings = new SyncBlockSettings(system);
            blockWeldPriority.SetEntries(settings.WeldPriority);
            blockGrindPriority.SetEntries(settings.GrindPriority);
            componentCollectPriority.SetEntries(settings.ComponentCollectPriority);
            settings.Changed = 0;

            return settings;
        }

        public void AssignReceived(SyncBlockSettings newSettings, NanobotBuildAndRepairSystemBlockPriorityHandling weldPriority, NanobotBuildAndRepairSystemBlockPriorityHandling grindPriority, NanobotBuildAndRepairSystemComponentPriorityHandling componentCollectPriority)
        {
            _Flags = newSettings._Flags;
            _IgnoreColor = newSettings.IgnoreColor;
            _GrindColor = newSettings.GrindColor;
            _UseGrindJanitorOn = newSettings.UseGrindJanitorOn;
            _GrindJanitorOptions = newSettings.GrindJanitorOptions;

            _AreaOffset = newSettings.AreaOffset;
            _AreaSize = newSettings.AreaSize;

            _WeldPriority = newSettings.WeldPriority;
            _GrindPriority = newSettings.GrindPriority;
            _ComponentCollectPriority = newSettings.ComponentCollectPriority;

            _SoundVolume = newSettings.SoundVolume;
            _SearchMode = newSettings.SearchMode;
            _WorkMode = newSettings.WorkMode;
            
            _WeldTo = newSettings.WeldTo;
            _UseAutoPowerOffWhenIdle = newSettings.UseAutoPowerOffWhenIdle;

            RecalcAreaBoundigBox();
            _IgnoreColorPacked = _IgnoreColor.PackHSVToUint();
            _GrindColorPacked = _GrindColor.PackHSVToUint();
            weldPriority.SetEntries(WeldPriority);
            grindPriority.SetEntries(GrindPriority);
            componentCollectPriority.SetEntries(ComponentCollectPriority);

            Changed = 2u;
        }

        public SyncBlockSettings GetTransmit()
        {
            _LastTransmitted = MyAPIGateway.Session.ElapsedPlayTime;
            Changed = Changed & ~1u;
            return this;
        }

        public bool IsTransmitNeeded()
        {
            return (Changed & 1u) != 0 && MyAPIGateway.Session.ElapsedPlayTime.Subtract(_LastTransmitted) >= TimeSpan.FromSeconds(2);
        }

        private void RecalcAreaBoundigBox()
        {
            var border = 0.25d;
            _CorrectedAreaBoundingBox = new BoundingBoxD(new Vector3D(-AreaSize.Z / 2 + border, -AreaSize.X / 2 + border, -AreaSize.Y / 2 + border), new Vector3D(AreaSize.Z / 2 - border, AreaSize.X / 2 - border, AreaSize.Y / 2 - border));
            _CorrectedAreaOffset = new Vector3(AreaOffset.Z, -AreaOffset.X, AreaOffset.Y);
        }

        private void RecalcOffsetAndSize()
        {
            if (_AreaWidthLeft != null && _AreaWidthRight != null)
            {
                AreaSize = new Vector3(_AreaWidthRight.Value + _AreaWidthLeft.Value, AreaSize.Y, AreaSize.Z);
                AreaOffset = new Vector3(AreaSize.X / 2 - _AreaWidthRight.Value, AreaOffset.Y, AreaOffset.Z);
            }
            if (_AreaWidthTop != null && _AreaWidthBottom != null)
            {
                AreaSize = new Vector3(AreaSize.X, _AreaWidthTop.Value + _AreaWidthBottom.Value, AreaSize.Z);
                AreaOffset = new Vector3(AreaOffset.X, AreaSize.Y / 2 - _AreaWidthBottom.Value, AreaOffset.Z);
            }
            if (_AreaWidthFront != null && _AreaWidthRear != null)
            {
                AreaSize = new Vector3(AreaSize.X, AreaSize.Y, _AreaWidthFront.Value + _AreaWidthRear.Value);
                AreaOffset = new Vector3(AreaOffset.X, AreaOffset.Y, AreaSize.Z / 2 - _AreaWidthRear.Value);
            }
        }

        public void CheckLimits(NanobotBuildAndRepairSystemBlock system, bool init)
        {
            var scale = system?.Welder != null ? system.Welder.BlockDefinition.SubtypeName.Contains("Large") ? 1f : 3f : 1f;

            if (NanobotBuildAndRepairSystemMod.Settings.Welder.AreaOffsetFixed || init)
            {
                MaximumOffset = 0;
                AreaOffset = new Vector3(0, 0, 0);
            }
            else
            {
                MaximumOffset = (int)Math.Ceiling(NanobotBuildAndRepairSystemMod.Settings.MaximumOffset / scale);
                if (AreaOffset.X > MaximumOffset || init) AreaOffset = new Vector3(init ? 0 : (float)MaximumOffset, AreaOffset.Y, AreaOffset.Z);
                else if (AreaOffset.X < -MaximumOffset || init) AreaOffset = new Vector3(init ? 0 : (float)-MaximumOffset, AreaOffset.Y, AreaOffset.Z);

                if (AreaOffset.Y > MaximumOffset || init) AreaOffset = new Vector3(AreaOffset.X, init ? 0 : (float)MaximumOffset, AreaOffset.Z);
                else if (AreaOffset.Y < -MaximumOffset || init) AreaOffset = new Vector3(AreaOffset.X, init ? 0 : (float)-MaximumOffset, AreaOffset.Z);

                if (AreaOffset.Z > MaximumOffset || init) AreaOffset = new Vector3(AreaOffset.X, AreaOffset.Y, init ? 0 : (float)MaximumOffset);
                else if (AreaOffset.Z < -MaximumOffset || init) AreaOffset = new Vector3(AreaOffset.X, AreaOffset.Y, init ? 0 : (float)-MaximumOffset);
            }

            MaximumRange = (int)Math.Ceiling(NanobotBuildAndRepairSystemMod.Settings.Range * 2 / scale);
            if (NanobotBuildAndRepairSystemMod.Settings.Welder.AreaSizeFixed || init)
            {
                AreaSize = new Vector3(MaximumRange, MaximumRange, MaximumRange);
            }
            else
            {
                if (AreaSize.X > MaximumRange || init) AreaSize = new Vector3(MaximumRange, AreaSize.Y, AreaSize.Z);
                if (AreaSize.Y > MaximumRange || init) AreaSize = new Vector3(AreaSize.X, MaximumRange, AreaSize.Z);
                if (AreaSize.Z > MaximumRange || init) AreaSize = new Vector3(AreaSize.X, AreaSize.Y, MaximumRange);
            }

            MaximumRequiredElectricPowerStandby = NanobotBuildAndRepairSystemMod.Settings.MaximumRequiredElectricPowerStandby / scale;
            MaximumRequiredElectricPowerTransport = NanobotBuildAndRepairSystemMod.Settings.MaximumRequiredElectricPowerTransport / scale;
            MaximumRequiredElectricPowerWelding = NanobotBuildAndRepairSystemMod.Settings.Welder.MaximumRequiredElectricPowerWelding / scale;
            MaximumRequiredElectricPowerGrinding = NanobotBuildAndRepairSystemMod.Settings.Welder.MaximumRequiredElectricPowerGrinding / scale;

            var maxMultiplier = Math.Max(NanobotBuildAndRepairSystemMod.Settings.Welder.WeldingMultiplier, NanobotBuildAndRepairSystemMod.Settings.Welder.GrindingMultiplier);
            TransportSpeed = maxMultiplier * Constants.WELDER_TRANSPORTSPEED_METER_PER_SECOND_DEFAULT * Math.Min(NanobotBuildAndRepairSystemMod.Settings.Range / Constants.WELDER_RANGE_DEFAULT_IN_M, 4.0f);

            if (NanobotBuildAndRepairSystemMod.Settings.Welder.AllowBuildFixed || init)
            {
                Flags = (Flags & ~Settings.AllowBuild) | (NanobotBuildAndRepairSystemMod.Settings.Welder.AllowBuildDefault ? Settings.AllowBuild : 0);
            }

            if (NanobotBuildAndRepairSystemMod.Settings.Welder.UseIgnoreColorFixed || init)
            {
                Flags = (Flags & ~Settings.UseIgnoreColor) | (NanobotBuildAndRepairSystemMod.Settings.Welder.UseIgnoreColorDefault ? Settings.UseIgnoreColor : 0);
                if (NanobotBuildAndRepairSystemMod.Settings.Welder.IgnoreColorDefault != null && NanobotBuildAndRepairSystemMod.Settings.Welder.IgnoreColorDefault.Length >= 3)
                {
                    IgnoreColor = new Vector3D(NanobotBuildAndRepairSystemMod.Settings.Welder.IgnoreColorDefault[0] / 360f,
                                              (float)Math.Round(NanobotBuildAndRepairSystemMod.Settings.Welder.IgnoreColorDefault[1], 1, MidpointRounding.AwayFromZero) / 100f - NanobotBuildAndRepairSystemTerminal.SATURATION_DELTA,
                                              (float)Math.Round(NanobotBuildAndRepairSystemMod.Settings.Welder.IgnoreColorDefault[2], 1, MidpointRounding.AwayFromZero) / 100f - NanobotBuildAndRepairSystemTerminal.VALUE_DELTA + NanobotBuildAndRepairSystemTerminal.VALUE_COLORIZE_DELTA);
                }
            }

            if (NanobotBuildAndRepairSystemMod.Settings.Welder.UseGrindColorFixed || init)
            {
                Flags = (Flags & ~Settings.UseGrindColor) | (NanobotBuildAndRepairSystemMod.Settings.Welder.UseGrindColorDefault ? Settings.UseGrindColor : 0);
                if (NanobotBuildAndRepairSystemMod.Settings.Welder.GrindColorDefault != null && NanobotBuildAndRepairSystemMod.Settings.Welder.GrindColorDefault.Length >= 3)
                {
                    GrindColor = new Vector3D(NanobotBuildAndRepairSystemMod.Settings.Welder.GrindColorDefault[0] / 360f,
                                              (float)Math.Round(NanobotBuildAndRepairSystemMod.Settings.Welder.GrindColorDefault[1], 1, MidpointRounding.AwayFromZero) / 100f - NanobotBuildAndRepairSystemTerminal.SATURATION_DELTA,
                                              (float)Math.Round(NanobotBuildAndRepairSystemMod.Settings.Welder.GrindColorDefault[2], 1, MidpointRounding.AwayFromZero) / 100f - NanobotBuildAndRepairSystemTerminal.VALUE_DELTA + NanobotBuildAndRepairSystemTerminal.VALUE_COLORIZE_DELTA);
                }
            }

            if (NanobotBuildAndRepairSystemMod.Settings.Welder.UseGrindJanitorFixed || init)
            {
                UseGrindJanitorOn = NanobotBuildAndRepairSystemMod.Settings.Welder.UseGrindJanitorDefault;
                GrindJanitorOptions = NanobotBuildAndRepairSystemMod.Settings.Welder.GrindJanitorOptionsDefault;
            }

            UseGrindJanitorOn &= NanobotBuildAndRepairSystemMod.Settings.Welder.AllowedGrindJanitorRelations;

            if (NanobotBuildAndRepairSystemMod.Settings.Welder.ShowAreaFixed || init) Flags = Flags & ~Settings.ShowArea;
            if (NanobotBuildAndRepairSystemMod.Settings.Welder.PushIngotOreImmediatelyFixed || init) Flags = (Flags & ~Settings.PushIngotOreImmediately) | (NanobotBuildAndRepairSystemMod.Settings.Welder.PushIngotOreImmediatelyDefault ? Settings.PushIngotOreImmediately : 0);
            if (NanobotBuildAndRepairSystemMod.Settings.Welder.PushComponentImmediatelyFixed || init) Flags = (Flags & ~Settings.PushComponentImmediately) | (NanobotBuildAndRepairSystemMod.Settings.Welder.PushComponentImmediatelyDefault ? Settings.PushComponentImmediately : 0);
            if (NanobotBuildAndRepairSystemMod.Settings.Welder.PushItemsImmediatelyFixed || init) Flags = (Flags & ~Settings.PushItemsImmediately) | (NanobotBuildAndRepairSystemMod.Settings.Welder.PushItemsImmediatelyDefault ? Settings.PushItemsImmediately : 0);
            if (NanobotBuildAndRepairSystemMod.Settings.Welder.CollectIfIdleFixed || init) Flags = (Flags & ~Settings.ComponentCollectIfIdle) | (NanobotBuildAndRepairSystemMod.Settings.Welder.CollectIfIdleDefault ? Settings.ComponentCollectIfIdle : 0);
            if (NanobotBuildAndRepairSystemMod.Settings.Welder.SoundVolumeFixed || init) SoundVolume = NanobotBuildAndRepairSystemMod.Settings.Welder.SoundVolumeDefault;
            if (NanobotBuildAndRepairSystemMod.Settings.Welder.ScriptControllFixed || init) Flags = Flags & ~Settings.ScriptControlled;
            if ((NanobotBuildAndRepairSystemMod.Settings.Welder.AllowedSearchModes & SearchMode) == 0 || init)
            {
                if ((NanobotBuildAndRepairSystemMod.Settings.Welder.AllowedSearchModes & NanobotBuildAndRepairSystemMod.Settings.Welder.SearchModeDefault) != 0)
                {
                    SearchMode = NanobotBuildAndRepairSystemMod.Settings.Welder.SearchModeDefault;
                }
                else
                {
                    if ((NanobotBuildAndRepairSystemMod.Settings.Welder.AllowedSearchModes & SearchModes.Grids) != 0) SearchMode = SearchModes.Grids;
                    else if ((NanobotBuildAndRepairSystemMod.Settings.Welder.AllowedSearchModes & SearchModes.BoundingBox) != 0) SearchMode = SearchModes.BoundingBox;
                }
            }

            if ((NanobotBuildAndRepairSystemMod.Settings.Welder.AllowedWorkModes & WorkMode) == 0 || init)
            {
                if ((NanobotBuildAndRepairSystemMod.Settings.Welder.AllowedWorkModes & NanobotBuildAndRepairSystemMod.Settings.Welder.WorkModeDefault) != 0)
                {
                    WorkMode = NanobotBuildAndRepairSystemMod.Settings.Welder.WorkModeDefault;
                }
                else
                {
                    if ((NanobotBuildAndRepairSystemMod.Settings.Welder.AllowedWorkModes & WorkModes.WeldBeforeGrind) != 0) WorkMode = WorkModes.WeldBeforeGrind;
                    else if ((NanobotBuildAndRepairSystemMod.Settings.Welder.AllowedWorkModes & WorkModes.GrindBeforeWeld) != 0) WorkMode = WorkModes.GrindBeforeWeld;
                    else if ((NanobotBuildAndRepairSystemMod.Settings.Welder.AllowedWorkModes & WorkModes.GrindIfWeldGetStuck) != 0) WorkMode = WorkModes.GrindIfWeldGetStuck;
                }
            }
        }
    }

    /// <summary>
    /// Current State of block
    /// </summary>
    [ProtoContract(SkipConstructor = true, UseProtoMembersOnly = true)]
    public class SyncBlockState
    {
        public const int MaxSyncItems = 20;
        private bool _Ready;
        private bool _Welding;
        private bool _NeedWelding;
        private bool _Grinding;
        private bool _NeedGrinding;
        private bool _Transporting;
        private bool _InventoryFull;
        private bool _LimitsExceeded;
        private List<SyncComponents> _MissingComponentsSync;
        private List<SyncTargetEntityData> _PossibleWeldTargetsSync;
        private List<SyncTargetEntityData> _PossibleGrindTargetsSync;
        private List<SyncTargetEntityData> _PossibleFloatingTargetsSync;
        private IMySlimBlock _CurrentWeldingBlock;
        private IMySlimBlock _CurrentGrindingBlock;

        private Vector3D? _CurrentTransportTarget;
        private Vector3D? _LastTransportTarget;
        private bool _CurrentTransportIsPick;
        private TimeSpan _CurrentTransportTime = TimeSpan.Zero;
        private TimeSpan _CurrentTransportStartTime = TimeSpan.Zero;

        public bool Changed { get; private set; }

        public override string ToString()
        {
            return
                $"Ready={Ready}, Welding={Welding}/{NeedWelding}, Grinding={Grinding}/{NeedGrinding}, MissingComponentsCount={MissingComponentsSync?.Count ?? -1}, PossibleWeldTargetsCount={PossibleWeldTargetsSync?.Count ?? -1}, PossibleGrindTargetsCount={PossibleGrindTargetsSync?.Count ?? -1}, PossibleFloatingTargetsCount={PossibleFloatingTargetsSync?.Count ?? -1}, CurrentWeldingBlock={Logging.BlockName(CurrentWeldingBlock, Logging.BlockNameOptions.None)}, CurrentGrindingBlock={Logging.BlockName(CurrentGrindingBlock, Logging.BlockNameOptions.None)}, CurrentTransportTarget={CurrentTransportTarget}";
        }

        [ProtoMember(1)]
        public bool Ready
        {
            get { return _Ready; }
            set
            {
                if (value != _Ready)
                {
                    _Ready = value;
                    Changed = true;
                }
            }
        }

        [ProtoMember(2)]
        public bool Welding
        {
            get { return _Welding; }
            set
            {
                if (value != _Welding)
                {
                    _Welding = value;
                    Changed = true;
                }
            }
        }

        [ProtoMember(3)]
        public bool NeedWelding
        {
            get { return _NeedWelding; }
            set
            {
                if (value != _NeedWelding)
                {
                    _NeedWelding = value;
                    Changed = true;
                }
            }
        }

        [ProtoMember(4)]
        public bool Grinding
        {
            get { return _Grinding; }
            set
            {
                if (value != _Grinding)
                {
                    _Grinding = value;
                    Changed = true;
                }
            }
        }

        [ProtoMember(5)]
        public bool NeedGrinding
        {
            get { return _NeedGrinding; }
            set
            {
                if (value != _NeedGrinding)
                {
                    _NeedGrinding = value;
                    Changed = true;
                }
            }
        }

        [ProtoMember(6)]
        public bool Transporting
        {
            get { return _Transporting; }
            set
            {
                if (value != _Transporting)
                {
                    _Transporting = value;
                    Changed = true;
                }
            }
        }

        [ProtoMember(7)]
        public TimeSpan LastTransmitted { get; set; }

        public IMySlimBlock CurrentWeldingBlock
        {
            get { return _CurrentWeldingBlock; }
            set
            {
                if (value != _CurrentWeldingBlock)
                {
                    _CurrentWeldingBlock = value;
                    Changed = true;
                }
            }
        }

        [ProtoMember(10)]
        public SyncEntityId CurrentWeldingBlockSync
        {
            get
            {
                return SyncEntityId.GetSyncId(_CurrentWeldingBlock);
            }
            set
            {
                CurrentWeldingBlock = SyncEntityId.GetItemAsSlimBlock(value);
            }
        }

        public IMySlimBlock CurrentGrindingBlock
        {
            get { return _CurrentGrindingBlock; }
            set
            {
                if (value != _CurrentGrindingBlock)
                {
                    _CurrentGrindingBlock = value;
                    Changed = true;
                }
            }
        }

        [ProtoMember(15)]
        public SyncEntityId CurrentGrindingBlockSync
        {
            get
            {
                return SyncEntityId.GetSyncId(_CurrentGrindingBlock);
            }
            set
            {
                CurrentGrindingBlock = SyncEntityId.GetItemAsSlimBlock(value);
            }
        }

        [ProtoMember(16)]
        public Vector3D? CurrentTransportTarget
        {
            get { return _CurrentTransportTarget; }
            set
            {
                if (value != _CurrentTransportTarget)
                {
                    _CurrentTransportTarget = value;
                    Changed = true;
                }
            }
        }

        [ProtoMember(17)]
        public Vector3D? LastTransportTarget
        {
            get { return _LastTransportTarget; }
            set
            {
                if (value != _LastTransportTarget)
                {
                    _LastTransportTarget = value;
                    Changed = true;
                }
            }
        }

        [ProtoMember(18)]
        public bool CurrentTransportIsPick
        {
            get { return _CurrentTransportIsPick; }
            set
            {
                if (value != _CurrentTransportIsPick)
                {
                    _CurrentTransportIsPick = value;
                    Changed = true;
                }
            }
        }

        [ProtoMember(19)]
        public TimeSpan CurrentTransportTime
        {
            get { return _CurrentTransportTime; }
            set
            {
                if (value != _CurrentTransportTime)
                {
                    _CurrentTransportTime = value;
                    Changed = true;
                }
            }
        }

        [ProtoMember(20)]
        public TimeSpan CurrentTransportStartTime
        {
            get { return _CurrentTransportStartTime; }
            set
            {
                if (value != _CurrentTransportStartTime)
                {
                    _CurrentTransportStartTime = value;
                    Changed = true;
                }
            }
        }

        public DefinitionIdHashDictionary MissingComponents { get; private set; }

        [ProtoMember(21)]
        public List<SyncComponents> MissingComponentsSync
        {
            get
            {
                if (_MissingComponentsSync == null)
                {
                    if (MissingComponents != null) _MissingComponentsSync = MissingComponents.GetSyncList();
                    else _MissingComponentsSync = new List<SyncComponents>();
                }
                return _MissingComponentsSync;
            }
        }

        [ProtoMember(22)]
        public bool InventoryFull
        {
            get { return _InventoryFull; }
            set
            {
                if (value != _InventoryFull)
                {
                    _InventoryFull = value;
                    Changed = true;
                }
            }
        }

        [ProtoMember(23)]
        public bool LimitsExceeded
        {
            get { return _LimitsExceeded; }
            set
            {
                if (value != _LimitsExceeded)
                {
                    _LimitsExceeded = value;
                    Changed = true;
                }
            }
        }

        public TargetBlockDataHashList PossibleWeldTargets { get; private set; }

        [ProtoMember(30)]
        public List<SyncTargetEntityData> PossibleWeldTargetsSync
        {
            get
            {
                if (_PossibleWeldTargetsSync == null)
                {
                    if (PossibleWeldTargets != null) _PossibleWeldTargetsSync = PossibleWeldTargets.GetSyncList();
                    else _PossibleWeldTargetsSync = new List<SyncTargetEntityData>();
                }
                return _PossibleWeldTargetsSync;
            }
        }

        public TargetBlockDataHashList PossibleGrindTargets { get; private set; }

        [ProtoMember(35)]
        public List<SyncTargetEntityData> PossibleGrindTargetsSync
        {
            get
            {
                if (_PossibleGrindTargetsSync == null)
                {
                    if (PossibleGrindTargets != null) _PossibleGrindTargetsSync = PossibleGrindTargets.GetSyncList();
                    else _PossibleGrindTargetsSync = new List<SyncTargetEntityData>();
                }
                return _PossibleGrindTargetsSync;
            }
        }

        public TargetEntityDataHashList PossibleFloatingTargets { get; private set; }

        [ProtoMember(36)]
        public List<SyncTargetEntityData> PossibleFloatingTargetsSync
        {
            get
            {
                if (_PossibleFloatingTargetsSync == null)
                {
                    if (PossibleFloatingTargets != null) _PossibleFloatingTargetsSync = PossibleFloatingTargets.GetSyncList();
                    else _PossibleFloatingTargetsSync = new List<SyncTargetEntityData>();
                }
                return _PossibleFloatingTargetsSync;
            }
        }

        public SyncBlockState()
        {
            MissingComponents = new DefinitionIdHashDictionary();
            PossibleWeldTargets = new TargetBlockDataHashList();
            PossibleGrindTargets = new TargetBlockDataHashList();
            PossibleFloatingTargets = new TargetEntityDataHashList();
        }

        internal void HasChanged()
        {
            Changed = true;
        }

        internal bool IsTransmitNeeded()
        {
            return Changed && MyAPIGateway.Session.ElapsedPlayTime.Subtract(LastTransmitted).TotalSeconds >= 2;
        }

        internal SyncBlockState GetTransmit()
        {
            _MissingComponentsSync = null;
            _PossibleWeldTargetsSync = null;
            _PossibleGrindTargetsSync = null;
            _PossibleFloatingTargetsSync = null;
            LastTransmitted = MyAPIGateway.Session.ElapsedPlayTime;
            Changed = false;
            return this;
        }

        internal void AssignReceived(SyncBlockState newState)
        {
            _Ready = newState.Ready;
            _Welding = newState.Welding;
            _NeedWelding = newState.NeedWelding;
            _Grinding = newState.Grinding;
            _NeedGrinding = newState.NeedGrinding;
            _InventoryFull = newState.InventoryFull;
            _LimitsExceeded = newState.LimitsExceeded;
            _CurrentTransportStartTime = MyAPIGateway.Session.ElapsedPlayTime - (newState.LastTransmitted - newState.CurrentTransportStartTime);
            _CurrentTransportTime = newState.CurrentTransportTime;

            _CurrentWeldingBlock = SyncEntityId.GetItemAsSlimBlock(newState.CurrentWeldingBlockSync);
            _CurrentGrindingBlock = SyncEntityId.GetItemAsSlimBlock(newState.CurrentGrindingBlockSync);
            _CurrentTransportTarget = newState.CurrentTransportTarget;
            _CurrentTransportIsPick = newState.CurrentTransportIsPick;

            MissingComponents.Clear();
            var missingComponentsSync = newState.MissingComponentsSync;
            if (missingComponentsSync != null) foreach (var item in missingComponentsSync) MissingComponents.Add(item.Component, item.Amount);

            PossibleWeldTargets.Clear();
            var possibleWeldTargetsSync = newState.PossibleWeldTargetsSync;
            if (possibleWeldTargetsSync != null)
            {
                foreach (var item in possibleWeldTargetsSync)
                {
                    if(item.Entity.EntityId == 0)
                    {
                        IMyEntity gridEntity;
                        if (MyAPIGateway.Entities.TryGetEntityById(item.Entity.GridId, out gridEntity))
                        {
                            var grid = gridEntity as IMyCubeGrid;
                            var block = grid?.GetCubeBlock(item.Entity.Position.Value);
                            if (block != null)
                            {                                
                                PossibleWeldTargets.Add(new TargetBlockData(SyncEntityId.GetItemAsSlimBlock(SyncEntityId.GetSyncId(block)), item.Distance, 0));
                            }
                        }
                    }
                    else
                    {
                        var slimBlock = SyncEntityId.GetItemAsSlimBlock(item.Entity);
                        PossibleWeldTargets.Add(new TargetBlockData(slimBlock, item.Distance, 0));
                    }                    
                }
            }

            PossibleGrindTargets.Clear();
            var possibleGrindTargetsSync = newState.PossibleGrindTargetsSync;
            if (possibleGrindTargetsSync != null) foreach (var item in possibleGrindTargetsSync)
                {
                    var slimBlock = SyncEntityId.GetItemAsSlimBlock(item.Entity);
                    PossibleGrindTargets.Add(new TargetBlockData(slimBlock, item.Distance, 0));
                }

            PossibleFloatingTargets.Clear();
            var possibleFloatingTargetsSync = newState.PossibleFloatingTargetsSync;
            if (possibleFloatingTargetsSync != null) foreach (var item in possibleFloatingTargetsSync) PossibleFloatingTargets.Add(new TargetEntityData(SyncEntityId.GetItemAs<Sandbox.Game.Entities.MyFloatingObject>(item.Entity), item.Distance));

            Changed = true;
        }

        internal void ResetChanged()
        {
            Changed = false;
        }
    }

    [ProtoContract(SkipConstructor = true, UseProtoMembersOnly = true)]
    public class MsgModCommand
    {
        [ProtoMember(1)]
        public ulong SteamId { get; set; }

        [ProtoMember(2)]
        public string Command { get; set; }
    }

    [ProtoContract(SkipConstructor = true, UseProtoMembersOnly = true)]
    public class MsgModDataRequest
    {
        [ProtoMember(1)]
        public ulong SteamId { get; set; }
    }

    [ProtoContract(SkipConstructor = true, UseProtoMembersOnly = true)]
    public class MsgModSettings
    {
        [ProtoMember(2)]
        public SyncModSettings Settings { get; set; }
    }

    [ProtoContract(SkipConstructor = true, UseProtoMembersOnly = true)]
    public class MsgBlockDataRequest
    {
        [ProtoMember(1)]
        public ulong SteamId { get; set; }

        [ProtoMember(2)]
        public long EntityId { get; set; }
    }

    [ProtoContract(SkipConstructor = true, UseProtoMembersOnly = true)]
    public class MsgBlockSettings
    {
        [ProtoMember(1)]
        public long EntityId { get; set; }

        [ProtoMember(2)]
        public SyncBlockSettings Settings { get; set; }
    }

    [ProtoContract(SkipConstructor = true, UseProtoMembersOnly = true)]
    public class MsgBlockState
    {
        [ProtoMember(1)]
        public long EntityId { get; set; }

        [ProtoMember(2)]
        public SyncBlockState State { get; set; }
    }

    /// <summary>
    /// Hash list for TargetBlockData
    /// </summary>
    public class TargetBlockDataHashList : HashList<TargetBlockData, SyncTargetEntityData>
    {
        public override List<SyncTargetEntityData> GetSyncList()
        {
            var result = new List<SyncTargetEntityData>();
            var idx = 0;
            foreach (var item in this)
            {
                result.Add(new SyncTargetEntityData() { Entity = SyncEntityId.GetSyncId(item.Block), Distance = item.Distance });
                idx++;
                if (idx > SyncBlockState.MaxSyncItems) break;
            }
            return result;
        }

        public override void RebuildHash()
        {
            uint hash = 0;
            var idx = 0;
            lock (this)
            {
                foreach (var entry in this)
                {
                    hash ^= UtilsSynchronization.RotateLeft((uint)entry.Block.GetHashCode(), idx + 1);
                    idx++;
                    if (idx >= SyncBlockState.MaxSyncItems) break;
                }
                CurrentCount = this.Count;
                CurrentHash = hash;
            }
        }
    }

    /// <summary>
    /// Hash list for TargetEntityData
    /// </summary>
    public class TargetEntityDataHashList : HashList<TargetEntityData, SyncTargetEntityData>
    {
        public override List<SyncTargetEntityData> GetSyncList()
        {
            var result = new List<SyncTargetEntityData>();
            var idx = 0;
            foreach (var item in this)
            {
                result.Add(new SyncTargetEntityData() { Entity = SyncEntityId.GetSyncId(item.Entity), Distance = item.Distance });
                idx++;
                if (idx > SyncBlockState.MaxSyncItems) break;
            }
            return result;
        }

        public override void RebuildHash()
        {
            uint hash = 0;
            var idx = 0;
            lock (this)
            {
                foreach (var entry in this)
                {
                    hash ^= UtilsSynchronization.RotateLeft((uint)entry.Entity.GetHashCode(), idx + 1);
                    idx++;
                    if (idx >= SyncBlockState.MaxSyncItems) break;
                }
                CurrentCount = this.Count;
                CurrentHash = hash;
            }
        }
    }

    public class DefinitionIdHashDictionary : HashDictionary<MyDefinitionId, int, SyncComponents>
    {
        public override List<SyncComponents> GetSyncList()
        {
            var result = new List<SyncComponents>();
            var idx = 0;
            foreach (var item in this)
            {
                result.Add(new SyncComponents() { Component = item.Key, Amount = item.Value });
                idx++;
                if (idx > SyncBlockState.MaxSyncItems) break;
            }
            return result;
        }

        public override void RebuildHash()
        {
            uint hash = 0;
            var idx = 0;
            lock (this)
            {
                foreach (var entry in this)
                {
                    hash ^= UtilsSynchronization.RotateLeft((uint)entry.GetHashCode(), idx + 1);
                    idx++;
                    if (idx >= SyncBlockState.MaxSyncItems) break;
                }
                CurrentCount = Count;
                CurrentHash = hash;
            }
        }
    }

    public class TargetEntityData
    {
        public IMyEntity Entity { get; internal set; }
        public double Distance { get; internal set; }
        public bool Ignore { get; set; }

        public TargetEntityData(IMyEntity entity, double distance)
        {
            Entity = entity;
            Distance = distance;
            Ignore = false;
        }
    }

    public class TargetBlockData : TargetEntityData
    {
        [Flags]
        public enum AttributeFlags
        {
            Projected = 0x0001,
            Autogrind = 0x0100
        }

        public IMySlimBlock Block { get; internal set; }
        public AttributeFlags Attributes { get; internal set; }

        public TargetBlockData(IMySlimBlock block, double distance, AttributeFlags attributes) : base(block?.FatBlock, distance)
        {
            Block = block;
            Attributes = attributes;
        }
    }
}
