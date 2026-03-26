using ProtoBuf;
using Sandbox.ModAPI;
using SKONanobotBuildAndRepairSystem.Utils;
using System;
using System.Xml.Serialization;

namespace SKONanobotBuildAndRepairSystem.Models
{
    /// <summary>
    /// The settings for Mod
    /// </summary>
    [ProtoContract(SkipConstructor = true, UseProtoMembersOnly = true)]
    public class SyncModSettings
    {
        private const int CurrentSettingsVersion = 7;

        [ProtoMember(2000), XmlElement]
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



        [ProtoMember(4), XmlElement]
        public long FriendlyDamageTimeoutTicks
        {
            get { return FriendlyDamageTimeout.Ticks; }
            set { FriendlyDamageTimeout = new TimeSpan(value); }
        }

        [ProtoMember(5), XmlElement]
        public long FriendlyDamageCleanupTicks
        {
            get { return FriendlyDamageCleanup.Ticks; }
            set { FriendlyDamageCleanup = new TimeSpan(value); }
        }

        [ProtoMember(8), XmlElement]
        public float MaximumRequiredElectricPowerTransport { get; set; }

        [ProtoMember(9), XmlElement]
        public float MaximumRequiredElectricPowerStandby { get; set; }

        [ProtoMember(10), XmlElement]
        public SyncModSettingsWelder Welder { get; set; }

        [ProtoMember(20), XmlElement]
        public int MaxBackgroundTasks { get; set; }

        [ProtoMember(21), XmlElement]
        public int MaximumOffset { get; set; }

        [ProtoMember(22), XmlElement]
        public bool SafeZoneCheckEnabled { get; set; }

        [ProtoMember(23), XmlElement]
        public bool ShieldCheckEnabled { get; set; }

        [ProtoMember(24), XmlElement]
        public bool DecreaseFactionReputationOnGrinding { get; set; }

        [ProtoMember(25), XmlElement]
        public bool DeleteBotsWhenDead { get; set; }

        [ProtoMember(26), XmlElement]
        public bool DisableTickingSound { get; set; }

        [ProtoMember(27), XmlElement]
        public bool DisableParticleEffects { get; set; }

        [ProtoMember(28), XmlElement]
        public bool DisableLimitSystemsPerTargetGrid { get; set; }

        [ProtoMember(29), XmlElement]
        public int MaxSystemsPerTargetGrid { get; set; }

        [ProtoMember(30), XmlElement]
        public bool AssignToSystemEnabled { get; set; }

        /// <summary>
        /// Enables debug information in the terminal custom info panel
        /// (sources, push targets, cluster details).
        /// </summary>
        [ProtoMember(35), XmlElement]
        public bool DebugMode { get; set; }

        /// <summary>
        /// After scanning a grid and finding no weld or grind targets, skip it for this
        /// many seconds before rescanning. Sub-grid connections are always traversed.
        /// 0 = disabled. Range: 0-300.
        /// </summary>
        [ProtoMember(36), XmlElement]
        public int EmptyGridRescanDelaySeconds { get; set; }

        /// <summary>
        /// Maximum stagger groups for distributing BaR updates across ticks.
        /// 0 = auto (scales with total BaR count). Range: 0-10.
        /// Higher values spread work across more ticks (less CPU per tick, slower response).
        /// </summary>
        [ProtoMember(37), XmlElement]
        public int StaggerGroupCount { get; set; }

        /// <summary>
        /// Global cap on ServerDoGrind calls per tick across all BaRs.
        /// 0 = auto (scales with total BaR count). Range: 0-100.
        /// Higher values allow faster grinding but cost more CPU per tick.
        /// </summary>
        [ProtoMember(38), XmlElement]
        public int MaxGrindsPerTick { get; set; }

        /// <summary>
        /// How long (seconds) a block assignment reservation is held before expiring.
        /// Prevents two BaRs from targeting the same block. Lower = faster slot recycling
        /// when BaRs disconnect. Higher = more protection against assignment steals.
        /// Range: 2-30. Default: 8.
        /// </summary>
        [ProtoMember(39), XmlElement]
        public int AssignmentTtlSeconds { get; set; }

        public SyncModSettings()
        {
            DisableLocalization = false;
            LogLevel = Logging.Level.Error; //Default
            MaxBackgroundTasks = Mod.MaxBackgroundTasks_Default;
            TargetsUpdateInterval = TimeSpan.FromSeconds(10);
            SourcesUpdateInterval = TimeSpan.FromSeconds(30);
            FriendlyDamageTimeout = TimeSpan.FromSeconds(60);
            FriendlyDamageCleanup = TimeSpan.FromSeconds(10);
            Range = NanobotSystem.WELDER_RANGE_DEFAULT_IN_M;
            MaximumOffset = NanobotSystem.WELDER_OFFSET_MAX_DEFAULT_IN_M;
            MaximumRequiredElectricPowerStandby = NanobotSystem.WELDER_REQUIRED_ELECTRIC_POWER_STANDBY_DEFAULT;
            MaximumRequiredElectricPowerTransport = NanobotSystem.WELDER_REQUIRED_ELECTRIC_POWER_TRANSPORT_DEFAULT;
            Welder = new SyncModSettingsWelder();
            SafeZoneCheckEnabled = true;
            ShieldCheckEnabled = true;
            DecreaseFactionReputationOnGrinding = true;
            DeleteBotsWhenDead = true;
            MaxSystemsPerTargetGrid = 0;
            AssignToSystemEnabled = true;
            DebugMode = false;
            EmptyGridRescanDelaySeconds = 20;
            StaggerGroupCount = 0;
            MaxGrindsPerTick = 0;
            AssignmentTtlSeconds = 8;
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
                    }
                }
                else if (MyAPIGateway.Utilities.FileExistsInLocalStorage("ModSettings.xml", typeof(SyncModSettings)))
                {
                    using (var reader = MyAPIGateway.Utilities.ReadFileInLocalStorage("ModSettings.xml", typeof(SyncModSettings)))
                    {
                        settings = MyAPIGateway.Utilities.SerializeFromXML<SyncModSettings>(reader.ReadToEnd());
                    }
                }

                Mod.CustomSettingsLoaded = settings != null;

                if (settings != null)
                {
                    var adjusted = AdjustSettings(settings);
                    if (settings.MaxBackgroundTasks > Mod.MaxBackgroundTasks_Max)
                    {
                        settings.MaxBackgroundTasks = Mod.MaxBackgroundTasks_Max;
                        adjusted = true;
                    }
                    else if (settings.MaxBackgroundTasks < Mod.MaxBackgroundTasks_Min)
                    {
                        settings.MaxBackgroundTasks = Mod.MaxBackgroundTasks_Min;
                        adjusted = true;
                    }

                    if (settings.Range > NanobotSystem.WELDER_RANGE_MAX_IN_M)
                    {
                        settings.Range = NanobotSystem.WELDER_RANGE_MAX_IN_M;
                        adjusted = true;
                    }
                    else if (settings.Range < NanobotSystem.WELDER_RANGE_MIN_IN_M)
                    {
                        settings.Range = NanobotSystem.WELDER_RANGE_MIN_IN_M;
                        adjusted = true;
                    }

                    if (settings.MaximumOffset > NanobotSystem.WELDER_OFFSET_MAX_IN_M)
                    {
                        settings.MaximumOffset = NanobotSystem.WELDER_OFFSET_MAX_IN_M;
                        adjusted = true;
                    }
                    else if (settings.MaximumOffset < 0)
                    {
                        settings.MaximumOffset = 0;
                        adjusted = true;
                    }

                    if (settings.Welder.WeldingMultiplier < NanobotSystem.WELDING_GRINDING_MULTIPLIER_MIN)
                    {
                        settings.Welder.WeldingMultiplier = NanobotSystem.WELDING_GRINDING_MULTIPLIER_MIN;
                        adjusted = true;
                    }
                    else if (settings.Welder.WeldingMultiplier >= NanobotSystem.WELDING_GRINDING_MULTIPLIER_MAX)
                    {
                        settings.Welder.WeldingMultiplier = NanobotSystem.WELDING_GRINDING_MULTIPLIER_MAX;
                        adjusted = true;
                    }

                    if (settings.Welder.GrindingMultiplier < NanobotSystem.WELDING_GRINDING_MULTIPLIER_MIN)
                    {
                        settings.Welder.GrindingMultiplier = NanobotSystem.WELDING_GRINDING_MULTIPLIER_MIN;
                        adjusted = true;
                    }
                    else if (settings.Welder.GrindingMultiplier >= NanobotSystem.WELDING_GRINDING_MULTIPLIER_MAX)
                    {
                        settings.Welder.GrindingMultiplier = NanobotSystem.WELDING_GRINDING_MULTIPLIER_MAX;
                        adjusted = true;
                    }

                    if (settings.EmptyGridRescanDelaySeconds < 0)
                    {
                        settings.EmptyGridRescanDelaySeconds = 0;
                        adjusted = true;
                    }
                    else if (settings.EmptyGridRescanDelaySeconds > 300)
                    {
                        settings.EmptyGridRescanDelaySeconds = 300;
                        adjusted = true;
                    }

                    if (settings.StaggerGroupCount < 0)
                    {
                        settings.StaggerGroupCount = 0;
                        adjusted = true;
                    }
                    else if (settings.StaggerGroupCount > 10)
                    {
                        settings.StaggerGroupCount = 10;
                        adjusted = true;
                    }

                    if (settings.MaxGrindsPerTick < 0)
                    {
                        settings.MaxGrindsPerTick = 0;
                        adjusted = true;
                    }
                    else if (settings.MaxGrindsPerTick > 100)
                    {
                        settings.MaxGrindsPerTick = 100;
                        adjusted = true;
                    }

                    if (settings.AssignmentTtlSeconds < 2)
                    {
                        settings.AssignmentTtlSeconds = 2;
                        adjusted = true;
                    }
                    else if (settings.AssignmentTtlSeconds > 30)
                    {
                        settings.AssignmentTtlSeconds = 30;
                        adjusted = true;
                    }

                    Logging.Instance.Write(Logging.Level.Info, "NanobotBuildAndRepairSystemSettings: Settings {0}", settings);
                }
                else
                {
                    settings = new SyncModSettings() { Version = CurrentSettingsVersion };
                }

                // Apply dynamic default for MaxSystemsPerTargetGrid based on game type.
                // 0 = no explicit value set → use 30 for local, 15 for multiplayer.
                if (settings.MaxSystemsPerTargetGrid <= 0)
                {
                    settings.MaxSystemsPerTargetGrid = MyAPIGateway.Multiplayer.MultiplayerActive ? 10 : 20;
                }
            }
            catch (Exception ex)
            {
                Logging.Instance.Write(Logging.Level.Error, "NanobotBuildAndRepairSystemSettings: Exception while loading: {0}", ex);
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

            Logging.Instance.Write("NanobotBuildAndRepairSystemSettings: Settings have old version: {0} update to {1}", settings.Version, CurrentSettingsVersion);

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
}