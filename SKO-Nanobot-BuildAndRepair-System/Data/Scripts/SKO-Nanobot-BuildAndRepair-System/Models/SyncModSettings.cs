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
        private const int CurrentSettingsVersion = 6;

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

        [ProtoMember(3), XmlElement]
        public long SourcesAndTargetsUpdateIntervalTicks
        {
            get { return TargetsUpdateInterval.Ticks; }
            set
            {
                TargetsUpdateInterval = new TimeSpan(value);
                SourcesUpdateInterval = new TimeSpan(value * 6);
            }
        }

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

        public SyncModSettings()
        {
            DisableLocalization = false;
            LogLevel = Logging.Level.Error; //Default
            MaxBackgroundTasks = Mod.MaxBackgroundTasks_Default;
            TargetsUpdateInterval = TimeSpan.FromSeconds(10);
            SourcesUpdateInterval = TimeSpan.FromSeconds(60);
            FriendlyDamageTimeout = TimeSpan.FromSeconds(60);
            FriendlyDamageCleanup = TimeSpan.FromSeconds(10);
            Range = NanobotSystem.WELDER_RANGE_DEFAULT_IN_M;
            MaximumOffset = NanobotSystem.WELDER_OFFSET_MAX_DEFAULT_IN_M;
            MaximumRequiredElectricPowerStandby = NanobotSystem.WELDER_REQUIRED_ELECTRIC_POWER_STANDBY_DEFAULT;
            MaximumRequiredElectricPowerTransport = NanobotSystem.WELDER_REQUIRED_ELECTRIC_POWER_TRANSPORT_DEFAULT;
            Welder = new SyncModSettingsWelder();
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

                    Logging.Instance.Write(Logging.Level.Info, "NanobotBuildAndRepairSystemSettings: Settings {0}", settings);
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