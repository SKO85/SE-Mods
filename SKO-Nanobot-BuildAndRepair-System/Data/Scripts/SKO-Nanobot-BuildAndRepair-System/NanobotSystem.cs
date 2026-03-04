using Sandbox.Common.ObjectBuilders;
using Sandbox.Definitions;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using SKONanobotBuildAndRepairSystem.Handlers;
using SKONanobotBuildAndRepairSystem.Helpers;
using SKONanobotBuildAndRepairSystem.Localization;
using SKONanobotBuildAndRepairSystem.Models;
using SKONanobotBuildAndRepairSystem.Utils;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using VRage;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRage.ObjectBuilders;
using VRage.Scripting.MemorySafeTypes;
using VRage.Utils;
using VRageMath;
using static SKONanobotBuildAndRepairSystem.Utils.UtilsInventory;
using IMyShipWelder = Sandbox.ModAPI.IMyShipWelder;
using IMyTerminalBlock = Sandbox.ModAPI.IMyTerminalBlock;
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

        private const int MaxPossibleWeldTargets = 256;
        private const int MaxPossibleGrindTargets = 256;
        private const int MaxPossibleFloatingTargets = 16;

        private const int TransmitStateMinIntervalSeconds = 1;
        private const int TransmitStateMaxIntervalSeconds = 2;
        private const int TransmitSettingsIntervalSeconds = 1;

        public static readonly int COLLECT_FLOATINGOBJECTS_SIMULTANEOUSLY = 50;

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
        private HashSet<IMyInventory> _TempIgnore4Ingot = new HashSet<IMyInventory>();
        private HashSet<IMyInventory> _TempIgnore4Items = new HashSet<IMyInventory>();
        private HashSet<IMyInventory> _TempIgnore4Components = new HashSet<IMyInventory>();

        private ConcurrentDictionary<long, TimeSpan> CachedBlocksTime = new ConcurrentDictionary<long, TimeSpan>();
        private ConcurrentDictionary<long, List<IMySlimBlock>> CachedBlocks = new ConcurrentDictionary<long, List<IMySlimBlock>>();

        private List<IMyEntity> _CachedEntitiesInRange;
        private TimeSpan _CachedEntitiesInRangeTime;
        private Vector3D _CachedEntitiesInRangeBBoxCenter;
        private const double EntityCacheTtlSeconds = 5.0;
        private const double EntityCachePositionTolerance = 25.0; // metres

        // Written on main thread (StartAsyncUpdateSourcesAndTargets); read on async worker thread.
        private long _ClusterKey = 0L;
        // Main-thread only — tracks when _ClusterKey was last computed via GetGroup.
        private TimeSpan _ClusterKeyLastRefreshTime;
        private static readonly TimeSpan ClusterKeyRefreshInterval = TimeSpan.FromSeconds(30.0);

        private IMyShipWelder _Welder;
        public IMyInventory _TransportInventory;
        private NanobotSystemEffects _Effects = new NanobotSystemEffects();

        private bool _IsInit;
        private List<IMyInventory> _PossibleSources = new List<IMyInventory>();
        private HashSet<IMyInventory> _Ignore4Ingot = new HashSet<IMyInventory>();
        private HashSet<IMyInventory> _Ignore4Items = new HashSet<IMyInventory>();
        private HashSet<IMyInventory> _Ignore4Components = new HashSet<IMyInventory>();
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

        private Action<IMyTerminalBlock> _onEnabledChanged;
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

        internal bool IsCoordinator
        {
            get { return Utils.ScanCoordinator.IsCoordinator(System.Threading.Interlocked.Read(ref _ClusterKey), _Welder.EntityId); }
        }

        private SyncBlockState _State = new SyncBlockState();

        public SyncBlockState State
        { get { return _State; } }

        /// <summary>
        /// Currently friendly damaged blocks
        /// </summary>
        public readonly Dictionary<IMySlimBlock, TimeSpan> FriendlyDamage = new Dictionary<IMySlimBlock, TimeSpan>();

        /// <summary>
        /// Initialize logical component
        /// </summary>
        /// <param name="objectBuilder"></param>
        public override void Init(MyObjectBuilder_EntityBase objectBuilder)
        {
            base.Init(objectBuilder);

            // Set frame update rate.
            NeedsUpdate = MyEntityUpdateEnum.EACH_FRAME | MyEntityUpdateEnum.EACH_100TH_FRAME;

            if (Entity.GameLogic is MyCompositeGameLogicComponent)
            {
                Entity.GameLogic = this;
            }

            _Welder = Entity as IMyShipWelder;
            _Welder.AppendingCustomInfo += AppendingCustomInfo;

            if (Settings == null) //Force load of settings (is much faster here than initial load in UpdateBeforeSimulation10_100)
            {
                if (Logging.Instance.ShouldLog(Logging.Level.Error))
                {
                    Logging.Instance.Write(Logging.Level.Error, "BuildAndRepairSystemBlock {0}: Initializing Load-Settings failed", Logging.BlockName(_Welder, Logging.BlockNameOptions.None));
                }
            }
        }

        public void SettingsChanged()
        {
            if (Mod.SettingsValid)
            {
                //Check limits as soon but not sooner as the 'server' settings has been received, otherwise we might use the wrong limits
                Settings.CheckLimits(this, false);

                if ((Mod.Settings.Welder.AllowedEffects & VisualAndSoundEffects.WeldingSoundEffect) == 0) NanobotSystemEffects._Sounds[(int)WorkingState.Welding] = null;
                if ((Mod.Settings.Welder.AllowedEffects & VisualAndSoundEffects.GrindingSoundEffect) == 0) NanobotSystemEffects._Sounds[(int)WorkingState.Grinding] = null;
            }

            var resourceSink = _Welder.ResourceSink as Sandbox.Game.EntityComponents.MyResourceSinkComponent;
            if (resourceSink != null)
            {
                var electricPowerTransport = Settings.MaximumRequiredElectricPowerTransport;
                if ((Mod.Settings.Welder.AllowedSearchModes & SearchModes.BoundingBox) == 0) electricPowerTransport /= 10;
                var maxPowerWorking = Math.Max(Settings.MaximumRequiredElectricPowerWelding, Settings.MaximumRequiredElectricPowerGrinding);
                resourceSink.SetMaxRequiredInputByType(ElectricityId, maxPowerWorking + electricPowerTransport + Settings.MaximumRequiredElectricPowerStandby);
                resourceSink.SetRequiredInputFuncByType(ElectricityId, ComputeRequiredElectricPower);
                resourceSink.Update();
            }

            var maxMultiplier = Math.Max(Mod.Settings.Welder.WeldingMultiplier, Mod.Settings.Welder.GrindingMultiplier);
            if (maxMultiplier > 10) NeedsUpdate = MyEntityUpdateEnum.EACH_FRAME | MyEntityUpdateEnum.EACH_10TH_FRAME;
            else NeedsUpdate = MyEntityUpdateEnum.EACH_FRAME | MyEntityUpdateEnum.EACH_100TH_FRAME;

            var multiplier = (maxMultiplier > WELDER_TRANSPORTVOLUME_MAX_MULTIPLIER ? WELDER_TRANSPORTVOLUME_MAX_MULTIPLIER : maxMultiplier);
            _MaxTransportVolume = ((float)_TransportInventory.MaxVolume * multiplier) / WELDER_TRANSPORTVOLUME_DIVISOR;

            UpdateCustomInfo(true);
        }

        /// <summary>
        /// Called on the client when server settings are received for the first time.
        /// Settings have already been applied via AssignReceived before this is called.
        /// Refreshes the terminal controls and custom info panel.
        /// </summary>
        internal void OnFirstSettingsReceived()
        {
            SettingsChanged();
            Mod.InitControls();
        }

        /// <summary>
        ///
        /// </summary>
        public override void Close()
        {
            if (_IsInit)
            {
                if (_Welder != null)
                {
                    _Welder.AppendingCustomInfo -= AppendingCustomInfo;
                    if (_onEnabledChanged != null) _Welder.EnabledChanged -= _onEnabledChanged;
                    if (_onIsWorkingChanged != null) _Welder.IsWorkingChanged -= _onIsWorkingChanged;
                }

                // ServerEmptyTranportInventory(true);

                // Stop effects
                State.CurrentTransportTarget = null;
                State.Ready = false;

                _Effects.UpdateEffects(this);
                _Effects.Close(this);

                lock (State.PossibleWeldTargets) State.PossibleWeldTargets?.Clear();
                lock (State.PossibleGrindTargets) State.PossibleGrindTargets?.Clear();
                lock (State.PossibleFloatingTargets) State.PossibleFloatingTargets?.Clear();
                lock (State.MissingComponents) State.MissingComponents?.Clear();
                lock (FriendlyDamage) FriendlyDamage?.Clear();

                _TempPossibleWeldTargets?.Clear();
                _TempPossibleGrindTargets?.Clear();
                _TempPossibleFloatingTargets?.Clear();
                _TempPossibleSources?.Clear();
                _TempIgnore4Ingot?.Clear();
                _TempIgnore4Items?.Clear();
                _TempIgnore4Components?.Clear();

                CachedBlocksTime.Clear();
                CachedBlocks.Clear();
                _CachedEntitiesInRange = null;

                _DelayWatch?.Stop();

                // Remove system from list.
                lock (Mod.NanobotSystems)
                {
                    Mod.NanobotSystems.Remove(Entity.EntityId);
                }

                // Save settings.
                Settings.Save(Entity, Mod.ModGuid);
            }
            base.Close();
        }
    }
}
