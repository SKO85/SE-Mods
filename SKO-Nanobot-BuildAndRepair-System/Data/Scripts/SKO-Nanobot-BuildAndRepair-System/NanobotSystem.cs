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
        public const float WELDER_TRANSPORTSPEED_METER_PER_SECOND_DEFAULT = 60f;
        public const float WELDER_TRANSPORTVOLUME_DIVISOR = 10f;
        public const float WELDER_TRANSPORTVOLUME_MAX_MULTIPLIER = 16f;
        public const float WELDER_AMOUNT_PER_SECOND = 8f;
        public const float WELDER_MAX_REPAIR_BONE_MOVEMENT_SPEED = 0.2f;
        public const float GRINDER_AMOUNT_PER_SECOND = 8f;
        public const float WELDER_SOUND_VOLUME = 2f;

        private const int MaxPossibleWeldTargets = 128;
        private const int MaxPossibleGrindTargets = 128;
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
        private long _PushTargetsFullSignature;
        private TimeSpan _PushTargetsFullSince;

        /// <summary>
        /// When true, the welding loop found nothing on its last full iteration
        /// (all targets grid-limited or assigned to other systems). The loop is
        /// skipped until the target list hash changes (i.e., a new scan completes).
        /// </summary>
        private bool _weldLoopExhausted = false;
        private long _weldExhaustedAtHash;

        /// <summary>
        /// FEAT-076: Same as _weldLoopExhausted but for the grind loop.
        /// When true, all grind targets were grid-limited, assigned, or destroyed
        /// on the last iteration. The loop is skipped until the target list hash
        /// changes (new scan) or the saturated grid set changes.
        /// </summary>
        private bool _grindLoopExhausted = false;
        private long _grindExhaustedAtHash;
        private int _grindExhaustedSaturatedCount;
        private List<TargetBlockData> _TempPossibleWeldTargets = new List<TargetBlockData>();
        private List<TargetBlockData> _TempPossibleGrindTargets = new List<TargetBlockData>();
        private List<TargetEntityData> _TempPossibleFloatingTargets = new List<TargetEntityData>();
        private List<IMyInventory> _TempPossibleSources = new List<IMyInventory>();
        private List<IMyInventory> _TempPossiblePushTargets = new List<IMyInventory>();

        // Locality-aware grind sorting: after destroying a block, prefer nearby blocks
        // within the same distance band. Set on main thread, read on background scan thread.
        internal Vector3D _LastGrindWorldPosition;
        internal bool _HasLastGrindPosition;

        // Snapshot of cluster member area box centers, populated by the coordinator at
        // scan start for multi-member clusters. Used by collect/sort comparators to score
        // candidates by proximity to ANY member instead of just the coordinator, so distant
        // members on the same grid aren't starved of targets. Null on solo scans.
        private List<Vector3D> _ClusterMemberAreaCenters;

        // BUG-096: Snapshot of each cluster member's full working-area OBB. Captured in
        // parallel with _ClusterMemberAreaCenters so SortAndCapGridCandidates can drop
        // candidates that no member can actually reach before it applies farthest-first
        // sorting — without this, farthest-first on a grid extending beyond the cluster's
        // reach deliberately kept the blocks nobody could weld/grind and starved the members.
        private List<MyOrientedBoundingBoxD> _ClusterMemberAreaBoxes;

        // BUG-110: Reusable scan-thread pools to eliminate per-cluster-scan allocations.
        // Each cluster scan (~5-7s on busy servers) was creating fresh List/Dictionary/HashSet
        // instances; the cumulative allocation rate triggered gen-1 GC pauses on the main
        // thread, producing the user-visible 21→70% CPU spike rhythm. Pooled fields are owned
        // by the background scan thread (scan dispatch is serialized for a given BaR).
        private List<IMyCubeGrid> _ScanGridsBuffer;
        private List<IMyInventory> _ScanSourcesBuffer;
        private Dictionary<IMySlimBlock, double> _ScanPreSortDistances;
        // AsyncScanForSources reusable traversal state.
        private HashSet<long> _ScanSourceVisitedGridIds;
        private Queue<IMyCubeGrid> _ScanSourceGridQueue;
        // BUG-119: O(1) dedup set for AddIfConnectedToInventory. Replaces the prior
        // List.Contains linear scan that grew with possibleSources size during the scan.
        private HashSet<IMyInventory> _ScanSourceDedupSet;
        // AsyncAddBlocksOfBox reusable sort buffers (only used when GrindSmallestGridFirst flag is set).
        private List<IMyEntity> _ScanSortedGrids;
        private List<IMyEntity> _ScanNonGridEntities;

        // Reusable pools for TruncateGridAware — avoids 8 allocations per ApplyClusterResultToSelf call.
        private HashSet<long> _truncateGridIds = new HashSet<long>();
        private Dictionary<long, int> _truncateKeptPerGrid = new Dictionary<long, int>();
        private List<TargetBlockData> _truncateKept = new List<TargetBlockData>();
        private List<TargetBlockData> _truncateOverflow = new List<TargetBlockData>();

        // BUG-091: Per-grid minimum distance used by GrindSmallestGridFirst sorts so
        // same-size grids are ordered by their closest block (spatial), not by arbitrary
        // EntityId. Pooled dict cleared and refilled by each sort pre-pass.
        private Dictionary<long, double> _gridMinDistLookup = new Dictionary<long, double>();

        // BUG-099: Per-candidate min-squared-distance-to-any-cluster-member cache used by
        // SortAndCapGridCandidates. Populated once during the BUG-096 partition loop (while
        // we already have the block world position for the OBB test) so the sort comparator
        // can do a dict lookup instead of recomputing 2 * memberCount squared distances per
        // comparison. Profiling on a 58-member cluster showed the inline recompute dominated
        // sort cost (~70-125 ms per call on 11k candidates); cached lookup drops it to ~6-10 ms.
        // Pooled field: cleared and reused between calls; one background scan per BaR runs
        // at a time so no concurrent access within one instance.
        private Dictionary<IMySlimBlock, double> _sortCandidateDistances = new Dictionary<IMySlimBlock, double>();

        // BUG-100: Per-candidate priority cache used by SortAndCapGridCandidates. After
        // BUG-099 eliminated the distance recompute, profile session 20260413220505 showed
        // the sort was bounded by the comparator's per-call BlockGrindPriority.GetPriority
        // (BlockWeldPriority for the weld branch) — 2 blocks × ~132k comparisons × ~130 ns
        // per GetPriority call = ~34 ms of priority lookup cost per 9.9k-candidate sort.
        // Populated once per candidate during the partition loop (same pattern as distance
        // cache), the sort comparator reads cached priorities in ~40 ns per lookup. Expected
        // drop from ~37 ms to ~12 ms per big sort call on the 58-member cluster workload.
        private Dictionary<IMySlimBlock, int> _sortCandidatePriorities = new Dictionary<IMySlimBlock, int>();

        // Precomputed per-tick set of grid IDs definitely over MaxSystemsPerTargetGrid.
        // Rebuilt by RebuildSaturatedGrids(), used as fast-path in IsGridOverSystemLimit().
        private HashSet<long> _saturatedGridIds = new HashSet<long>();

        // BUG-115: persistent set of projected-block keys ("gridId:position") for which proj.Build()
        // has thrown a NullReferenceException (SE engine DLC-armor validation failure). The
        // per-TargetBlockData Ignore flag is wiped each scan refresh, so without this set the BaR
        // would re-lock the same broken block after every rescan and waste a tick logging the same
        // NRE. Skipping by key persists the decision across scans for this BaR's lifetime.
        private readonly HashSet<string> _BrokenProjBuildKeys = new HashSet<string>();

        // BUG-120: counter for silent proj.Build failures (block stayed projected after Build
        // returned without throwing — typical signature is online owner lacks the required DLC).
        // Promoted to _BrokenProjBuildKeys after PROJ_BUILD_MAX_SILENT_FAILS consecutive failures
        // so transient races (another BaR built it the same tick, projector briefly disabled,
        // component shortage mid-build) don't immediately get marked broken.
        private readonly Dictionary<string, int> _ProjBuildSilentFailCount = new Dictionary<string, int>();
        private const int PROJ_BUILD_MAX_SILENT_FAILS = 3;

        // BUG-120: identity (OwnerId) the broken-block caches above are scoped to. When
        // _Welder.OwnerId differs from this snapshot, both caches are cleared at the next
        // ServerTryWelding entry — the new owner may have different DLC entitlements, so
        // previously-broken blocks may now build successfully. long.MinValue ensures the
        // first comparison flips and seeds the snapshot from the live OwnerId.
        private long _BrokenCacheOwnerId = long.MinValue;

        /// <summary>
        /// Tracks grids that were scanned and had no weld/grind targets.
        /// Key: grid EntityId, Value: playTime when grid was found empty.
        /// </summary>
        private ConcurrentDictionary<long, TimeSpan> _EmptyGridCache = new ConcurrentDictionary<long, TimeSpan>();
        public int EmptyGridCacheCount { get { return _EmptyGridCache.Count; } }

        /// <summary>
        /// FEAT-073: Static predicate for filtering to fat-block-only lists.
        /// Used in the empty-grid-cache connection-discovery path to avoid iterating
        /// all blocks on large grids when only connection blocks (always fat) are needed.
        /// </summary>
        private static readonly Func<IMySlimBlock, bool> _fatBlockFilter = block => block.FatBlock != null;

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
        /// <summary>
        /// FEAT-075 fix: Tracks how many weld/grind candidates the last full scan produced.
        /// When a type was discovered (count > 0) but has since been consumed (live count 0),
        /// the scan must run to refresh it. When the type was never found (count == 0),
        /// it genuinely doesn't exist and doesn't block the saturated skip.
        /// </summary>
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
    }
}
