using Sandbox.Common.ObjectBuilders;
using SKONanobotBuildAndRepairSystem.Handlers;
using SKONanobotBuildAndRepairSystem.Models;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRage.Utils;
using VRageMath;
using IMyShipWelder = Sandbox.ModAPI.IMyShipWelder;
using MyInventoryItem = VRage.Game.ModAPI.Ingame.MyInventoryItem;

namespace SKONanobotBuildAndRepairSystem
{
    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_ShipWelder), false, "SELtdLargeNanobotBuildAndRepairSystem", "SELtdSmallNanobotBuildAndRepairSystem")]
    public partial class NanobotSystem : MyGameLogicComponent
    {
        public enum WorkingState
        {
            Invalid = 0, NotReady = 1, Idle = 2, Welding = 3, NeedWelding = 4, MissingComponents = 5, Grinding = 6, NeedGrinding = 7, InventoryFull = 8, LimitsExceeded = 9
        }

        public const int WELDER_RANGE_DEFAULT_IN_M = 100; // *2 = AreaSize
        public const int WELDER_RANGE_MAX_IN_M = 2000;
        public const int WELDER_RANGE_MIN_IN_M = 2;
        public const int WELDER_OFFSET_DEFAULT_IN_M = 0;
        public const int WELDER_OFFSET_MAX_DEFAULT_IN_M = 200;
        public const int WELDER_OFFSET_MAX_IN_M = 2000;

        public const float WELDING_GRINDING_MULTIPLIER_MIN = 0.001f;
        public const float WELDING_GRINDING_MULTIPLIER_MAX = 1000f;

        public const float WELDER_REQUIRED_ELECTRIC_POWER_STANDBY_DEFAULT = 50.0f / 1000; // 50 kW
        public const float WELDER_REQUIRED_ELECTRIC_POWER_WELDING_DEFAULT = 200.0f / 1000; // 200 kW
        public const float WELDER_REQUIRED_ELECTRIC_POWER_GRINDING_DEFAULT = 200.0f / 1000; // 200 kW
        public const float WELDER_REQUIRED_ELECTRIC_POWER_TRANSPORT_DEFAULT = 100.0f / 1000; // 100 kW
        public const float WELDER_TRANSPORTSPEED_METER_PER_SECOND_DEFAULT = 40f;
        public const float WELDER_TRANSPORTVOLUME_DIVISOR = 10f;
        public const float WELDER_TRANSPORTVOLUME_MAX_MULTIPLIER = 8f;
        public const float WELDER_AMOUNT_PER_SECOND = 2f;
        public const float WELDER_MAX_REPAIR_BONE_MOVEMENT_SPEED = 0.2f;
        public const float GRINDER_AMOUNT_PER_SECOND = 4f;
        public const float WELDER_SOUND_VOLUME = 2f;

        private const int MaxPossibleWeldTargets = 512;
        private const int MaxPossibleGrindTargets = 512;
        private const int MaxPossibleFloatingTargets = 32;

        private const int TransmitStateMinIntervalSeconds = 1;
        private const int TransmitStateMaxIntervalSeconds = 2;
        private const int TransmitSettingsIntervalSeconds = 1;

        public const int COLLECT_FLOATINGOBJECTS_SIMULTANEOUSLY = 50;

        public static readonly MyDefinitionId ElectricityId = new MyDefinitionId(typeof(VRage.Game.ObjectBuilders.Definitions.MyObjectBuilder_GasProperties), "Electricity");
        internal bool CreativeModeActive = false;

        private static readonly MyStringId RangeGridResourceId = MyStringId.GetOrCompute("WelderGrid");
        private static readonly Random _RandomDelay = new Random();

        private Stopwatch _DelayWatch = new Stopwatch();
        private int _Delay = 0;

        private bool _AsyncUpdateSourcesAndTargetsRunning = false;
        private List<TargetBlockData> _TempPossibleWeldTargets = new List<TargetBlockData>();
        private List<TargetBlockData> _TempPossibleGrindTargets = new List<TargetBlockData>();
        private List<TargetEntityData> _TempPossibleFloatingTargets = new List<TargetEntityData>();
        private List<IMyInventory> _TempPossibleSources = new List<IMyInventory>();
        private List<IMyInventory> _TempPossiblePushTargets = new List<IMyInventory>();


        private ConcurrentDictionary<long, TimeSpan> CachedBlocksTime = new ConcurrentDictionary<long, TimeSpan>();
        private ConcurrentDictionary<long, List<IMySlimBlock>> CachedBlocks = new ConcurrentDictionary<long, List<IMySlimBlock>>();

        private List<IMyEntity> _CachedEntitiesInRange;
        private TimeSpan _CachedEntitiesInRangeTime;
        private Vector3D _CachedEntitiesInRangeBBoxCenter;
        private const double EntityCacheTtlSeconds = 5.0;
        private const double EntityCachePositionTolerance = 25.0; // metres

        private IMyShipWelder _Welder;
        public IMyInventory _TransportInventory;
        private Effects _Effects = new Effects();

        private bool _IsInit;
        private List<IMyInventory> _PossibleSources = new List<IMyInventory>();
        private List<IMyInventory> _PossiblePushTargets = new List<IMyInventory>();
        private Dictionary<string, int> _TempMissingComponents = new Dictionary<string, int>();
        private List<MyInventoryItem> _TempInventoryItems = new List<MyInventoryItem>();

        private int _UpdateEffectsInterval;
        private bool _UpdateCustomInfoNeeded;
        internal bool _firstSettingsReceived = false;
        private float _MaxTransportVolume;
        private int _ContinuouslyError;

        private TimeSpan _LastFriendlyDamageCleanup;
        private TimeSpan _LastSourceUpdate = -Mod.Settings.SourcesUpdateInterval;
        private TimeSpan _LastTargetsUpdate;
        private TimeSpan _UpdateCustomInfoLast;
        private TimeSpan _UpdatePowerSinkLast;
        private TimeSpan _UpdateSettingsTransmitLast;
        private TimeSpan _UpdateStateTransmitLast;
        private int _UpdateStateTransmitInterval;

        private TimeSpan _PeriodicExtraChecksLast;

        public TimeSpan _TryAutoPushInventoryLast;
        public TimeSpan _TryPushInventoryLast;

        private Action<Sandbox.ModAPI.IMyTerminalBlock> _onEnabledChanged;
        private Action<IMyCubeBlock> _onIsWorkingChanged;

        private SyncBlockSettings _Settings;

        internal SyncBlockSettings Settings
        {
            get
            {
                return (_Settings != null) ? _Settings : _Settings = SyncBlockSettings.Load(this, Mod.ModGuid, BlockWeldPriority, BlockGrindPriority, ComponentCollectPriority);
            }
        }

        internal void ResetSettings()
        {
            _Settings = new SyncBlockSettings(this);
            BlockWeldPriority.SetAllEnabled(true);
            _Settings.WeldPriority = BlockWeldPriority.GetEntries();
            BlockGrindPriority.SetAllEnabled(true);
            _Settings.GrindPriority = BlockGrindPriority.GetEntries();
            ComponentCollectPriority.SetAllEnabled(true);
            _Settings.ComponentCollectPriority = ComponentCollectPriority.GetEntries();
            UpdateCustomInfo(true);
        }

        private BlockPriorityHandling _BlockWeldPriority = new BlockPriorityHandling();

        internal BlockPriorityHandling BlockWeldPriority
        {
            get
            {
                return _BlockWeldPriority;
            }
        }

        private BlockPriorityHandling _BlockGrindPriority = new BlockPriorityHandling();

        internal BlockPriorityHandling BlockGrindPriority
        {
            get
            {
                return _BlockGrindPriority;
            }
        }

        private ComponentPriorityHandling _ComponentCollectPriority = new ComponentPriorityHandling();

        internal ComponentPriorityHandling ComponentCollectPriority
        {
            get
            {
                return _ComponentCollectPriority;
            }
        }

        public IMyShipWelder Welder
        { get { return _Welder; } }

        private SyncBlockState _State = new SyncBlockState();

        public SyncBlockState State
        { get { return _State; } }

        /// <summary>
        /// Currently friendly damaged blocks
        /// </summary>
        public readonly Dictionary<IMySlimBlock, TimeSpan> FriendlyDamage = new Dictionary<IMySlimBlock, TimeSpan>();
    }
}
