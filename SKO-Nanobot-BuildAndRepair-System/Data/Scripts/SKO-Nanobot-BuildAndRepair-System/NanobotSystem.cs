using Sandbox.Common.ObjectBuilders;
using SKONanobotBuildAndRepairSystem.Cluster;
using SKONanobotBuildAndRepairSystem.Handlers;
using SKONanobotBuildAndRepairSystem.Models;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.ModAPI;
using VRage.Utils;
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

        public const float WELDING_GRINDING_MULTIPLIER_MIN = 0.1f;
        public const float WELDING_GRINDING_MULTIPLIER_MAX = 100f;

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

        private const int MaxPossibleWeldTargets = 256;
        private const int MaxPossibleGrindTargets = 256;
        private const int MaxPossibleFloatingTargets = 16;

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

        private volatile bool _AsyncUpdateSourcesAndTargetsRunning = false;
        private volatile bool _InitialScanCompleted = false;
        private volatile bool _PushTargetsFull = false;

        /// <summary>
        /// When true, the welding loop found nothing on its last full iteration
        /// (all targets grid-limited or assigned to other systems). The loop is
        /// skipped until the target list hash changes (i.e., a new scan completes).
        /// </summary>
        private bool _weldLoopExhausted = false;
        private long _weldExhaustedAtHash;
        private List<TargetBlockData> _TempPossibleWeldTargets = new List<TargetBlockData>();
        private List<TargetBlockData> _TempPossibleGrindTargets = new List<TargetBlockData>();
        private List<TargetEntityData> _TempPossibleFloatingTargets = new List<TargetEntityData>();
        private List<IMyInventory> _TempPossibleSources = new List<IMyInventory>();
        private List<IMyInventory> _TempPossiblePushTargets = new List<IMyInventory>();

        // Reusable pools for TruncateGridAware — avoids 8 allocations per ApplyClusterResultToSelf call.
        private HashSet<long> _truncateGridIds = new HashSet<long>();
        private Dictionary<long, int> _truncateKeptPerGrid = new Dictionary<long, int>();
        private List<TargetBlockData> _truncateKept = new List<TargetBlockData>();
        private List<TargetBlockData> _truncateOverflow = new List<TargetBlockData>();

        // Precomputed per-tick set of grid IDs definitely over MaxSystemsPerTargetGrid.
        // Rebuilt by RebuildSaturatedGrids(), used as fast-path in IsGridOverSystemLimit().
        private HashSet<long> _saturatedGridIds = new HashSet<long>();

        /// <summary>
        /// Tracks grids that were scanned and had no weld/grind targets.
        /// Key: grid EntityId, Value: playTime when grid was found empty.
        /// </summary>
        private ConcurrentDictionary<long, TimeSpan> _EmptyGridCache = new ConcurrentDictionary<long, TimeSpan>();
        public int EmptyGridCacheCount { get { return _EmptyGridCache.Count; } }

        private IMyShipWelder _Welder;
        public IMyInventory _TransportInventory;
        private Effects _Effects = new Effects();

        private bool _IsInit;
        private List<IMyInventory> _PossibleSources = new List<IMyInventory>();
        private List<IMyInventory> _PossiblePushTargets = new List<IMyInventory>();
        private Dictionary<string, int> _TempMissingComponents = new Dictionary<string, int>();
        private List<MyInventoryItem> _TempInventoryItems = new List<MyInventoryItem>();
        private List<MyInventoryItem> _TempPullInventoryItems = new List<MyInventoryItem>();

        private int _UpdateEffectsInterval;
        private bool _UpdateCustomInfoNeeded;
        internal bool _firstSettingsReceived = false;
        private float _MaxTransportVolume;
        private float _MaxWeldTransportVolume;
        private float _MaxGrindTransportVolume;


        private TimeSpan _LastFriendlyDamageCleanup;
        private TimeSpan _LastSourceUpdate = -Mod.Settings.SourcesUpdateInterval;
        private TimeSpan _LastTargetsUpdate;
        public TimeSpan LastTargetsUpdate { get { return _LastTargetsUpdate; } }
        private TimeSpan _UpdateCustomInfoLast;
        private TimeSpan _UpdatePowerSinkLast;
        private TimeSpan _UpdateSettingsTransmitLast;
        private TimeSpan _UpdateStateTransmitLast;
        private int _UpdateStateTransmitInterval;

        // FEAT-038: Progressive backoff for unchanged state transmits.
        private int _lastTransmittedFingerprint;
        private int _transmitBackoffMultiplier = 1;

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
            BlockWeldPriority.ResetToDefaultOrder();
            BlockWeldPriority.SetAllEnabled(true);
            _Settings.WeldPriority = BlockWeldPriority.GetEntries();
            BlockGrindPriority.ResetToDefaultOrder();
            BlockGrindPriority.SetAllEnabled(true);
            _Settings.GrindPriority = BlockGrindPriority.GetEntries();
            ComponentCollectPriority.ResetToDefaultOrder();
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
        public readonly ConcurrentDictionary<IMySlimBlock, TimeSpan> FriendlyDamage = new ConcurrentDictionary<IMySlimBlock, TimeSpan>();

        /// <summary>
        /// Cluster assignment for shared scanning. Null means solo (legacy path).
        /// Set by ScanClusterCoordinator.RebuildClusters() on main thread, read on background thread.
        /// </summary>
        internal volatile ScanCluster AssignedCluster;

        /// <summary>
        /// Counts consecutive cycles where a cluster member received no shared result.
        /// After 3 misses, falls back to independent scan.
        /// </summary>
        internal int MissedResultCycles;

        /// <summary>
        /// Stagger slot (0..StaggerGroupCount-1) assigned at init. Only BaRs whose slot
        /// matches the current cycle run ServerTryWeldingGrindingCollecting(), spreading
        /// the per-tick main-thread load across multiple ticks.
        /// </summary>
        internal int _staggerSlot = -1;

        /// <summary>
        /// Last computed cluster key. Used by RebuildClusters to detect
        /// settings changes and skip the full rebuild when nothing changed.
        /// </summary>
        internal string _lastClusterKey;
    }
}
