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

        public const float WELDING_GRINDING_MULTIPLIER_MIN = 0.1f;
        public const float WELDING_GRINDING_MULTIPLIER_MAX = 100f;

        public const float WELDER_REQUIRED_ELECTRIC_POWER_STANDBY_DEFAULT = 50.0f / 1000; // 50 kW
        public const float WELDER_REQUIRED_ELECTRIC_POWER_WELDING_DEFAULT = 200.0f / 1000; // 200 kW
        public const float WELDER_REQUIRED_ELECTRIC_POWER_GRINDING_DEFAULT = 200.0f / 1000; // 200 kW
        public const float WELDER_REQUIRED_ELECTRIC_POWER_TRANSPORT_DEFAULT = 100.0f / 1000; // 100 kW
        public const float WELDER_TRANSPORTSPEED_METER_PER_SECOND_DEFAULT = 50f;
        public const float WELDER_TRANSPORTVOLUME_DIVISOR = 10f;
        public const float WELDER_TRANSPORTVOLUME_MAX_MULTIPLIER = 8f;
        public const float WELDER_AMOUNT_PER_SECOND = 2f;
        public const float WELDER_MAX_REPAIR_BONE_MOVEMENT_SPEED = 0.2f;
        public const float GRINDER_AMOUNT_PER_SECOND = 4f;
        public const float WELDER_SOUND_VOLUME = 2f;

        private const int MaxPossibleWeldTargets = 128;
        private const int MaxPossibleGrindTargets = 128;
        private const int MaxPossibleFloatingTargets = 16;

        private const int TransmitStateMinIntervalSeconds = 1;
        private const int TransmitStateMaxIntervalSeconds = 4;
        private const int TransmitSettingsIntervalSeconds = 1;

        public const int COLLECT_FLOATINGOBJECTS_SIMULTANEOUSLY = 50;

        public static readonly MyDefinitionId ElectricityId = new MyDefinitionId(typeof(VRage.Game.ObjectBuilders.Definitions.MyObjectBuilder_GasProperties), "Electricity");
        internal bool CreativeModeActive = false;

        private static readonly MyStringId RangeGridResourceId = MyStringId.GetOrCompute("WelderGrid");

        // PERF-10: per-instance Random seeded from the welder's EntityId. Random is not
        // thread-safe; the static instance previously shared across all BaRs assumed all
        // call sites stayed on the main thread. A per-instance Random eliminates that
        // implicit constraint and decorrelates the jitter so two BaRs with identical
        // call patterns don't draw identical sequences. EntityId is set by the engine
        // before Init runs, so the seed is stable per BaR for the session lifetime.
        private Random _RandomDelay;

        private Stopwatch _DelayWatch = new Stopwatch();
        private int _Delay = 0;

        private volatile bool _AsyncUpdateSourcesAndTargetsRunning = false;
        private volatile bool _InitialScanCompleted = false;
        private volatile bool _PushTargetsFull = false;
        private long _PushTargetsFullSignature;
        private TimeSpan _PushTargetsFullSince;

        // BUG-162: round-robin cursor for ServerTryPushInventory chunking across ticks.
        private int _PushItemCursor = 0;

        /// <summary>
        /// When true, the welding loop found nothing on its last full iteration
        /// (all targets grid-limited or assigned to other systems). The loop is
        /// skipped until the target list hash changes (i.e., a new scan completes).
        /// </summary>
        private bool _weldLoopExhausted = false;
        private long _weldExhaustedAtHash;

        /// <summary>FEAT-076: same as _weldLoopExhausted, for the grind loop.</summary>
        private bool _grindLoopExhausted = false;
        private long _grindExhaustedAtHash;
        private int _grindExhaustedSaturatedCount;
        // Background-scan-thread-only staging lists; swapped into the published State.*
        // / _PossibleSources / _PossiblePushTargets under their locks. Don't touch from
        // the main thread — read consumers go through the published collections.
        private List<TargetBlockData> _TempPossibleWeldTargets = new List<TargetBlockData>();
        private List<TargetBlockData> _TempPossibleGrindTargets = new List<TargetBlockData>();
        private List<TargetEntityData> _TempPossibleFloatingTargets = new List<TargetEntityData>();
        private List<IMyInventory> _TempPossibleSources = new List<IMyInventory>();
        private List<IMyInventory> _TempPossiblePushTargets = new List<IMyInventory>();

        // Locality-aware grind sorting: after destroying a block, prefer nearby blocks
        // within the same distance band. Set on main thread, read on background scan thread.
        internal Vector3D _LastGrindWorldPosition;
        internal bool _HasLastGrindPosition;

        // Snapshot of cluster member area centers; null on solo scans.
        private List<Vector3D> _ClusterMemberAreaCenters;

        // BUG-096: snapshot of each member's working-area OBB so SortAndCapGridCandidates
        // can drop unreachable candidates before farthest-first sorting.
        private List<MyOrientedBoundingBoxD> _ClusterMemberAreaBoxes;

        // BUG-110: reusable scan-thread pools (eliminate per-scan allocations).
        private List<IMyCubeGrid> _ScanGridsBuffer;
        private List<IMyInventory> _ScanSourcesBuffer;
        private Dictionary<IMySlimBlock, double> _ScanPreSortDistances;
        // AsyncScanForSources reusable traversal state.
        private HashSet<long> _ScanSourceVisitedGridIds;
        private Queue<IMyCubeGrid> _ScanSourceGridQueue;
        // BUG-119: O(1) dedup set for AddIfConnectedToInventory.
        private HashSet<IMyInventory> _ScanSourceDedupSet;
        // AsyncAddBlocksOfBox reusable sort buffers (only used when GrindSmallestGridFirst flag is set).
        private List<IMyEntity> _ScanSortedGrids;
        private List<IMyEntity> _ScanNonGridEntities;

        // Reusable pools for TruncateGridAware — avoids 8 allocations per ApplyClusterResultToSelf call.
        private HashSet<long> _truncateGridIds = new HashSet<long>();
        private Dictionary<long, int> _truncateKeptPerGrid = new Dictionary<long, int>();
        private List<TargetBlockData> _truncateKept = new List<TargetBlockData>();
        private List<TargetBlockData> _truncateOverflow = new List<TargetBlockData>();

        // BUG-091: per-grid min-distance for GrindSmallestGridFirst spatial tiebreak.
        private Dictionary<long, double> _gridMinDistLookup = new Dictionary<long, double>();

        // BUG-099/100: per-candidate distance and priority caches populated during the
        // partition pass and read by the sort comparator (saves recomputing per compare).
        private Dictionary<IMySlimBlock, double> _sortCandidateDistances = new Dictionary<IMySlimBlock, double>();
        private Dictionary<IMySlimBlock, int> _sortCandidatePriorities = new Dictionary<IMySlimBlock, int>();

        // Precomputed per-tick set of grid IDs definitely over MaxSystemsPerTargetGrid.
        // Rebuilt by _gridSaturation.Rebuild(), used as fast-path in IsGridOverSystemLimit().
        private readonly GridSaturationTracker _gridSaturation = new GridSaturationTracker();

        // BUG-115: persistent skip set for projected blocks that threw NRE in proj.Build.
        private readonly HashSet<string> _BrokenProjBuildKeys = new HashSet<string>();

        // BUG-120: silent proj.Build failure counter; promoted to _BrokenProjBuildKeys
        // after PROJ_BUILD_MAX_SILENT_FAILS consecutive failures.
        private readonly Dictionary<string, int> _ProjBuildSilentFailCount = new Dictionary<string, int>();
        private const int PROJ_BUILD_MAX_SILENT_FAILS = 3;

        // BUG-120: owner the broken-block caches are scoped to; cleared on owner change.
        private long _BrokenCacheOwnerId = long.MinValue;

        /// <summary>
        /// Tracks grids that were scanned and had no weld/grind targets.
        /// Key: grid EntityId, Value: playTime when grid was found empty.
        /// </summary>
        private ConcurrentDictionary<long, TimeSpan> _EmptyGridCache = new ConcurrentDictionary<long, TimeSpan>();
        public int EmptyGridCacheCount { get { return _EmptyGridCache.Count; } }

        // REF-2: pooled scratch for CleanupEmptyGridCache. Per-BaR scan dispatch is
        // serialised, so a single instance field is safe from concurrent access.
        private List<long> _emptyGridExpiredKeys;

        /// <summary>FEAT-073: filter predicate for fat-block-only lists.</summary>
        private static readonly Func<IMySlimBlock, bool> _fatBlockFilter = block => block.FatBlock != null;

        private IMyShipWelder _Welder;

        public bool IsEnabled
        {
            get { return _Welder != null && _Welder.Enabled; }
        }

        public IMyInventory _TransportInventory;
        private Effects _Effects = new Effects();

        private bool _IsInit;
        private List<IMyInventory> _PossibleSources = new List<IMyInventory>();
        private List<IMyInventory> _PossiblePushTargets = new List<IMyInventory>();
        private Dictionary<string, int> _TempMissingComponents = new Dictionary<string, int>();
        private List<MyInventoryItem> _TempInventoryItems = new List<MyInventoryItem>();
        private List<MyInventoryItem> _TempPullInventoryItems = new List<MyInventoryItem>();
        // BUG-133: source-walk inversion buffers (one pass over sources, dict lookup against needs).
        private Dictionary<string, int> _TempPullRemaining = new Dictionary<string, int>();
        private Dictionary<string, Sandbox.Definitions.MyPhysicalItemDefinition> _TempPullDefs =
            new Dictionary<string, Sandbox.Definitions.MyPhysicalItemDefinition>();
        // BUG-134: last successful source; tried first next call (temporal locality).
        private VRage.Game.ModAPI.IMyInventory _LastSuccessfulSource;
        // BUG-136: round-robin cursor for the capped source walk.
        private int _NextPullSourceIdx;

        private int _UpdateEffectsInterval;
        private bool _UpdateCustomInfoNeeded;
        internal bool _firstSettingsReceived = false;
        private float _MaxTransportVolume;
        private float _MaxWeldTransportVolume;
        private float _MaxGrindTransportVolume;


        private TimeSpan _LastSourceUpdate = -Mod.Settings.SourcesUpdateInterval;
        private TimeSpan _LastTargetsUpdate;
        public TimeSpan LastTargetsUpdate { get { return _LastTargetsUpdate; } }
        private TimeSpan _UpdateCustomInfoLast;
        private TimeSpan _UpdatePowerSinkLast;
        private TimeSpan _UpdateSettingsTransmitLast;
        private TimeSpan _UpdateStateTransmitLast;
        private int _UpdateStateTransmitInterval;

        // FEAT-038/BUG-150: progressive backoff fingerprint (long, content-aware).
        private long _lastTransmittedFingerprint;
        private int _transmitBackoffMultiplier = 1;

        private TimeSpan _PeriodicExtraChecksLast;
        private long _lastWorkCycle = -1;

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
        /// FEAT-071: Counts consecutive cluster scans that produced zero targets.
        /// After IdleScansBeforeBackoff consecutive empty scans, the coordinator
        /// extends its scan interval to reduce background work.
        /// </summary>
        internal int _consecutiveEmptyScans;
        internal const int IdleScansBeforeBackoff = 3;
        internal static readonly TimeSpan IdleScanInterval = TimeSpan.FromSeconds(20);

        /// <summary>
        /// FEAT-075: Set by the coordinator when it skips a scan because the
        /// target list is still saturated (plenty of live targets remain).
        /// Members check this flag to also skip their apply-result cycle.
        /// </summary>
        internal volatile bool _scanSkippedSaturated;
        /// <summary>
        /// FEAT-075: When a member signals the coordinator to rescan (e.g., member ran
        /// out of weld targets), this flag bypasses the saturated skip so the coordinator
        /// actually performs the scan instead of checking its own (still-full) target lists.
        /// </summary>
        internal volatile bool _rescanForced;
        /// <summary>
        /// FEAT-075: Timestamp of the last full scan that actually ran.
        /// Used to enforce a maximum skip duration (force rescan every 60s).
        /// </summary>
        internal TimeSpan _lastFullScanTime;
        internal const int SaturatedRescanThreshold = 64;
        internal static readonly TimeSpan MaxScanSkipDuration = TimeSpan.FromSeconds(60);
        /// <summary>FEAT-075: per-type scan counts to distinguish "depleted" from "never existed".</summary>
        internal int _lastScanWeldCandidateCount;
        internal int _lastScanGrindCandidateCount;

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

        /// <summary>
        /// FEAT-072: Cached numeric hash of cluster-relevant fields.
        /// Compared in the RebuildClusters fast path instead of recomputing
        /// the full cluster key string every second.
        /// </summary>
        internal int _lastClusterKeyHash;

        // Shared release helper for the weld/grind loops; second overload preserves
        // tsAssignOps profiler timing at call sites that aggregate it.
        private static void ReleaseAssignmentIfEnabled(IMySlimBlock block)
        {
            if (Mod.Settings.AssignToSystemEnabled) block.ReleaseFromSystem();
        }

        private static void ReleaseAssignmentIfEnabled(IMySlimBlock block, bool profilerEnabled, ref long tsAssignOps)
        {
            if (!Mod.Settings.AssignToSystemEnabled) return;
            var ts = profilerEnabled ? System.Diagnostics.Stopwatch.GetTimestamp() : 0L;
            block.ReleaseFromSystem();
            if (ts != 0L) tsAssignOps += System.Diagnostics.Stopwatch.GetTimestamp() - ts;
        }
    }
}
