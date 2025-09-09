using VRage.Game;

namespace SKONanobotBuildAndRepairSystem
{
    public static class Constants
    {
        // Collection limits
        public const int COLLECT_FLOATINGOBJECTS_SIMULTANEOUSLY = 50;

        // Color adjustments for HSV sliders (from Terminal UI)
        public const float SATURATION_DELTA = 0.8f;
        public const float VALUE_DELTA = 0.55f;
        public const float VALUE_COLORIZE_DELTA = 0.1f;

        // Sync and messaging IDs (can be consolidated further if used externally)
        public const ushort MSGID_MOD_DATAREQUEST = 40000;
        public const ushort MSGID_MOD_SETTINGS = 40001;
        public const ushort MSGID_MOD_COMMAND = 40010;
        public const ushort MSGID_BLOCK_DATAREQUEST = 40100;
        public const ushort MSGID_BLOCK_SETTINGS_FROM_SERVER = 40102;
        public const ushort MSGID_BLOCK_SETTINGS_FROM_CLIENT = 40103;
        public const ushort MSGID_BLOCK_STATE_FROM_SERVER = 40104;

        public const int WELDER_RANGE_DEFAULT_IN_M = 100; //*2 = AreaSize
        public const int WELDER_RANGE_MAX_IN_M = 2000;
        public const int WELDER_RANGE_MIN_IN_M = 2;
        public const int WELDER_OFFSET_DEFAULT_IN_M = 0;
        public const int WELDER_OFFSET_MAX_DEFAULT_IN_M = 200;
        public const int WELDER_OFFSET_MAX_IN_M = 2000;

        public const float WELDING_GRINDING_MULTIPLIER_MIN = 0.001f;
        public const float WELDING_GRINDING_MULTIPLIER_MAX = 1000f;

        public const float WELDER_REQUIRED_ELECTRIC_POWER_STANDBY_DEFAULT = 50.0f / 1000;        // 50kW       //20W, 0.02f
        public const float WELDER_REQUIRED_ELECTRIC_POWER_WELDING_DEFAULT = 200.0f / 1000;      // 200kW     //2kW, 2.0f
        public const float WELDER_REQUIRED_ELECTRIC_POWER_GRINDING_DEFAULT = 200.0f / 1000;     // 200kW     //1.5kW, 1.5f
        public const float WELDER_REQUIRED_ELECTRIC_POWER_TRANSPORT_DEFAULT = 100.0f / 1000;    // 100kW     //10kW, 10.0f
        public const float WELDER_TRANSPORTSPEED_METER_PER_SECOND_DEFAULT = 40f;                             // 20f
        public const float WELDER_TRANSPORTVOLUME_DIVISOR = 10f;
        public const float WELDER_TRANSPORTVOLUME_MAX_MULTIPLIER = 4f;
        public const float WELDER_AMOUNT_PER_SECOND = 2f;                                                    // 2f
        public const float WELDER_MAX_REPAIR_BONE_MOVEMENT_SPEED = 0.2f;
        public const float GRINDER_AMOUNT_PER_SECOND = 4f;                                                   // 4f
        public const float WELDER_SOUND_VOLUME = 2f;

        public const string PARTICLE_EFFECT_WELDING1 = MyParticleEffectsNameEnum.WelderContactPoint;
        public const string PARTICLE_EFFECT_GRINDING1 = MyParticleEffectsNameEnum.ShipGrinder;
        public const string PARTICLE_EFFECT_TRANSPORT1_PICK = "GrindNanobotTrace1";
        public const string PARTICLE_EFFECT_TRANSPORT1_DELIVER = "WeldNanobotTrace1";

        public const int MaxBackgroundTasks_Default = 8;
        public const int MaxBackgroundTasks_Max = 15;
        public const int MaxBackgroundTasks_Min = 1;
        public const float MaxSkeletonCreateIntegrityRatio = 0.2f;

        public const bool DisableLocalization = false;
        public const ulong sId = 76561198001777579;

        public const bool AutoPowerOffOnIdleForcedDefault = false;
        public const int AutoPowerOffOnIdleMinutesDefault = 15;
        public const bool AllowEnemyGrindingInMotionDefault = true;

        public const float UpdateIntervalSecondsDefault = 0.5f;
        public static string Version = "1.7.0";
    }
}