namespace SKONanobotDrillAndFillSystem.NanobotDrillSystem
{
    using Sandbox.Common.ObjectBuilders;
    using Sandbox.Definitions;
    using Sandbox.Game.Entities;
    using Sandbox.Game.Localization;
    using Sandbox.ModAPI;
    using Sandbox.ModAPI.Ingame;
    using SKONanobotDrillAndFillSystem.Utils;
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Linq;
    using System.Text;
    using System.Threading;
    using VRage;
    using VRage.Game;
    using VRage.Game.Components;
    using VRage.Game.ModAPI;
    using VRage.ModAPI;
    using VRage.ObjectBuilders;
    using VRage.Utils;
    using VRage.Voxels;
    using VRageMath;
    using IMyHandDrill = Sandbox.ModAPI.Weapons.IMyHandDrill;
    using IMyShipDrill = Sandbox.ModAPI.IMyShipDrill;
    using IMyTerminalBlock = Sandbox.ModAPI.IMyTerminalBlock;
    using MyInventoryItem = VRage.Game.ModAPI.Ingame.MyInventoryItem;

    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_Drill), false, "SELtdLargeNanobotDrillSystem", "SELtdSmallNanobotDrillSystem")]
    public class NanobotDrillSystemBlock : MyGameLogicComponent
    {
        private enum WorkingState
        {
            Invalid = 0, NotReady = 1, Idle = 2, Drilling = 3, NeedDrilling = 4, InventoryFull = 5, Filling = 6, NeedFilling = 7, MissingMaterial = 8, CharacterInWorkingArea = 9
        }

        private class BackgroundTaskState
        {
            public bool Running;
            public bool Working;
            public bool NeedWorking;
            public bool Flag;
            public object CurrentEntity;
            public Dictionary<byte, float> TransportMaterials;
            public TargetVoxelData TargetData;
        };

        public const int DRILL_RANGE_DEFAULT_IN_M = 75;
        public const int DRILL_RANGE_MAX_IN_M = 1000;
        public const int DRILL_RANGE_MIN_IN_M = 1;
        public const int DRILL_OFFSET_DEFAULT_IN_M = 0;
        public const int DRILL_OFFSET_MAX_IN_M = 10000;
        public const float DRILL_FILL_MULTIPLIER_MIN = 0.001f;
        public const float DRILL_FILL_MULTIPLIER_MAX = 1000f;
        public const float DRILL_VOXEL_HARVEST_MULTIPLIER = 3.9f; //Comes from ENABLE_REMOVED_VOXEL_CONTENT_HACK enabled inside MyVoxelGenerator.CutOutShapeWithProperties
        public const float DRILL_FLOATING_OBJECT_SPAWN_RADIUS = 1.0f;
        public const float DRILL_TRANSPORTSPEED_METER_PER_SECOND_DEFAULT = 20f;

        public const float DRILL_REQUIRED_ELECTRIC_POWER_STANDBY_DEFAULT = 0.02f / 1000; //20W
        public const float DRILL_REQUIRED_ELECTRIC_POWER_DRILLING_DEFAULT = 1.5f / 1000; //1.5kW
        public const float DRILL_REQUIRED_ELECTRIC_POWER_FILLING_DEFAULT = 150f / 1000; //150kW
        public const float DRILL_REQUIRED_ELECTRIC_POWER_TRANSPORT_DEFAULT = 10.0f / 1000; //10kW
        public const float DRILL_FILL_MAX_VOLUME_PER_RUN = 10f;
        public const float DRILL_SOUND_VOLUME = 2f;

        public const int VOXEL_CACHE_SIZE = 25;

        public static readonly int COLLECT_FLOATINGOBJECTS_SIMULTANEOUSLY = 50;

        public static readonly MyDefinitionId ElectricityId = new MyDefinitionId(typeof(VRage.Game.ObjectBuilders.Definitions.MyObjectBuilder_GasProperties), "Electricity");
        private static readonly MyStringId RangeGridResourceId = MyStringId.GetOrCompute("DrillGrid");
        private static readonly Random _RandomDelay = new Random();

        private static MySoundPair[] _Sounds = new[] { null, null, null, new MySoundPair("ToolShipDrillRock"), new MySoundPair("BlockModuleProductivity"), new MySoundPair("DrillSystemUnable"), new MySoundPair("MeteorFly"), new MySoundPair("BlockModuleProductivity"), new MySoundPair("DrillSystemUnable"), new MySoundPair("DrillSystemUnable") };
        private static float[] _SoundLevels = new[] { 0f, 0f, 0f, 0.8f, 0.5f, 0.4f, 1f, 0.5f, 0.4f, 0.4f };

        private const string PARTICLE_EFFECT_DRILLING1 = MyParticleEffectsNameEnum.Smoke_DrillDust;
        private const string PARTICLE_EFFECT_FILLING1 = MyParticleEffectsNameEnum.WheelDust_Rock;
        private const string PARTICLE_EFFECT_TRANSPORT1_PICK = "DrillNanobotTrace1";
        private const string PARTICLE_EFFECT_TRANSPORT1_DELIVER = "FillNanobotTrace1";

        private Stopwatch _DelayWatch = new Stopwatch();
        private int _Delay = 0;

        private bool _AsyncUpdateSourcesAndTargetsRunning = false;
        private List<TargetVoxelData> _TempPossibleDrillTargets = new List<TargetVoxelData>();
        private List<TargetVoxelData> _TempPossibleFillTargets = new List<TargetVoxelData>();
        private List<TargetEntityData> _TempPossibleFloatingTargets = new List<TargetEntityData>();
        private List<IMyInventory> _TempPossibleSources = new List<IMyInventory>();
        private HashSet<IMyInventory> _TempIgnore4Ore = new HashSet<IMyInventory>();
        private List<BoundingBoxI> _TempVoxelBoxes = new List<BoundingBoxI>();

        private IMyShipDrill _Drill;
        private IMyInventory _TransportInventory;
        private bool _IsInit;
        private List<IMyInventory> _PossibleSources = new List<IMyInventory>();
        private HashSet<IMyInventory> _Ignore4Ore = new HashSet<IMyInventory>();

        private static readonly int MaxTransportEffects = 50;
        private static int _ActiveTransportEffects = 0;
        private static readonly int MaxWorkingEffects = 80;
        private static int _ActiveWorkingEffects = 0;

        private MyEntity3DSoundEmitter _SoundEmitter;
        private MyEntity3DSoundEmitter _SoundEmitterWorking;
        private Vector3D? _SoundEmitterWorkingPosition;
        private MyParticleEffect _ParticleEffectWorking1;
        private MyParticleEffect _ParticleEffectTransport1;
        private bool _ParticleEffectTransport1Active;
        private Vector3 _EmitterPosition;

        private TimeSpan _LastSourceUpdate = -NanobotDrillSystemMod.Settings.SourcesUpdateInterval;
        private TimeSpan _LastTargetsUpdate;
        private bool LastCharacterInWorkingArea;

        private bool _CreativeModeActive;

        private int _UpdateEffectsInterval;
        private bool _UpdateCustomInfoNeeded;
        private TimeSpan _UpdateCustomInfoLast;
        private WorkingState _WorkingStateSet = WorkingState.Invalid;
        private float _SoundVolumeSet;
        private bool _TransportStateSet;
        private WorkingState _WorkingState;
        private int _ContinuouslyError;
        private bool _PowerReady;
        private bool _PowerDrilling;
        private bool _PowerFilling;
        private bool _PowerTransporting;
        private TimeSpan _UpdatePowerSinkLast;
        private TimeSpan _TryAutoPushInventoryLast;
        private TimeSpan _TryPushInventoryLast;

        private MyStorageData _VoxelDataCacheSearch;
        private MyStorageData _VoxelDataCacheFillRemove;

        private BackgroundTaskState _BackgroundDrillState = new BackgroundTaskState() { TransportMaterials = new Dictionary<byte, float>() };
        private BackgroundTaskState _BackgroundFillState = new BackgroundTaskState() { TransportMaterials = new Dictionary<byte, float>() };

        private SyncBlockSettings _Settings;

        internal SyncBlockSettings Settings
        {
            get
            {
                return (_Settings != null) ? _Settings : _Settings = SyncBlockSettings.Load(this, NanobotDrillSystemMod.ModGuid, DrillPriority, ComponentCollectPriority);
            }
        }

        private NanobotDrillSystemDrillPriorityHandling _DrillPriority = new NanobotDrillSystemDrillPriorityHandling();

        internal NanobotDrillSystemDrillPriorityHandling DrillPriority
        {
            get
            {
                return _DrillPriority;
            }
        }

        private NanobotDrillSystemComponentPriorityHandling _ComponentCollectPriority = new NanobotDrillSystemComponentPriorityHandling();

        internal NanobotDrillSystemComponentPriorityHandling ComponentCollectPriority
        {
            get
            {
                return _ComponentCollectPriority;
            }
        }

        public IMyShipDrill Drill
        { get { return _Drill; } }

        private SyncBlockState _State = new SyncBlockState();
        public SyncBlockState State
        { get { return _State; } }

        /// <summary>
        /// Initialize logical component
        /// </summary>
        /// <param name="objectBuilder"></param>
        public override void Init(MyObjectBuilder_EntityBase objectBuilder)
        {
            if (Mod.Log.ShouldLog(Logging.Level.Event)) Mod.Log.Write(Logging.Level.Event, "DrillSystemBlock {0}: Initializing", Logging.BlockName(Entity, Logging.BlockNameOptions.None));

            base.Init(objectBuilder);
            NeedsUpdate = MyEntityUpdateEnum.EACH_FRAME | MyEntityUpdateEnum.EACH_100TH_FRAME;
            if (Entity.GameLogic is MyCompositeGameLogicComponent)
            {
                if (Mod.Log.ShouldLog(Logging.Level.Event)) Mod.Log.Write(Logging.Level.Event, "DrillSystemBlock: Init Entiy.Logic remove other mods from this entity");
                Entity.GameLogic = this;
            }

            _Drill = Entity as IMyShipDrill;
            _Drill.AppendingCustomInfo += AppendingCustomInfo;

            if (MyAPIGateway.Session.IsServer)
            {
                _VoxelDataCacheSearch = new MyStorageData(MyStorageDataTypeFlags.ContentAndMaterial);
                _VoxelDataCacheSearch.Resize(new Vector3I(VOXEL_CACHE_SIZE, VOXEL_CACHE_SIZE, VOXEL_CACHE_SIZE)); //Size of search cube
                _VoxelDataCacheFillRemove = new MyStorageData(MyStorageDataTypeFlags.ContentAndMaterial);
                _VoxelDataCacheFillRemove.Resize(new Vector3I(VOXEL_CACHE_SIZE, VOXEL_CACHE_SIZE, VOXEL_CACHE_SIZE)); //Size of search cube
            }

            _WorkingState = WorkingState.NotReady;

            if (Settings == null) //Force load of settings (is much faster here than initial load in UpdateBeforeSimulation10_100)
            {
                if (Mod.Log.ShouldLog(Logging.Level.Error)) Mod.Log.Write(Logging.Level.Error, "DrillSystemBlock {0}: Initializing Load-Settings failed", Logging.BlockName(_Drill, Logging.BlockNameOptions.None));
            }
            ;

            if (Mod.Log.ShouldLog(Logging.Level.Event)) Mod.Log.Write(Logging.Level.Event, "DrillSystemBlock {0}: Initialized", Logging.BlockName(_Drill, Logging.BlockNameOptions.None));
        }

        /// <summary>
        ///
        /// </summary>
        public void SettingsChanged()
        {
            if (NanobotDrillSystemMod.SettingsValid)
            {
                //Check limits as soon but not sooner as the 'server' settings has been received, otherwise we might use the wrong limits
                Settings.CheckLimits(this, false);
                if ((NanobotDrillSystemMod.Settings.Drill.AllowedEffects & VisualAndSoundEffects.DrillingSoundEffect) == 0) _Sounds[(int)WorkingState.Drilling] = null;
                if ((NanobotDrillSystemMod.Settings.Drill.AllowedEffects & VisualAndSoundEffects.FillingSoundEffect) == 0) _Sounds[(int)WorkingState.Filling] = null;
            }

            var resourceSink = _Drill.ResourceSink as Sandbox.Game.EntityComponents.MyResourceSinkComponent;
            if (resourceSink != null)
            {
                var electricPowerTransport = Settings.MaximumRequiredElectricPowerTransport;
                var maxPowerWorking = Math.Max(Settings.MaximumRequiredElectricPowerDrilling, Settings.MaximumRequiredElectricPowerFilling);
                //Call this is the only way to set also the DetailedInfo to correct values
                _Drill.PowerConsumptionMultiplier = (float)Math.Sqrt((maxPowerWorking + electricPowerTransport + Settings.MaximumRequiredElectricPowerStandby) / 0.002f);
                resourceSink.SetMaxRequiredInputByType(ElectricityId, maxPowerWorking + electricPowerTransport + Settings.MaximumRequiredElectricPowerStandby);
                resourceSink.SetRequiredInputFuncByType(ElectricityId, ComputeRequiredElectricPower);
                resourceSink.Update();
            }

            var maxMultiplier = Math.Max(NanobotDrillSystemMod.Settings.Drill.DrillingMultiplier, NanobotDrillSystemMod.Settings.Drill.FillingMultiplier);
            if (maxMultiplier > 10) NeedsUpdate = MyEntityUpdateEnum.EACH_FRAME | MyEntityUpdateEnum.EACH_10TH_FRAME;
            else NeedsUpdate = MyEntityUpdateEnum.EACH_FRAME | MyEntityUpdateEnum.EACH_100TH_FRAME;

            if (Mod.Log.ShouldLog(Logging.Level.Event)) Mod.Log.Write(Logging.Level.Event, "DrillSystemBlock {0}: Init WorkMode={1}", Logging.BlockName(_Drill, Logging.BlockNameOptions.None), Settings.WorkMode);
        }

        /// <summary>
        ///
        /// </summary>
        private void Init()
        {
            if (_IsInit) return;
            if (_Drill.SlimBlock.IsProjected() || !_Drill.Synchronized) //Synchronized = !IsPreview
            {
                if (Mod.Log.ShouldLog(Logging.Level.Event)) Mod.Log.Write(Logging.Level.Event, "DrillSystemBlock {0}: Init Block is only projected/preview -> exit", Logging.BlockName(_Drill, Logging.BlockNameOptions.None));
                NeedsUpdate = MyEntityUpdateEnum.NONE;
                return;
            }

            lock (NanobotDrillSystemMod.DrillSystems)
            {
                if (!NanobotDrillSystemMod.DrillSystems.ContainsKey(Entity.EntityId))
                {
                    NanobotDrillSystemMod.DrillSystems.Add(Entity.EntityId, this);
                }
            }
            NanobotDrillSystemMod.InitControls();

            _Drill.EnabledChanged += (block) => { this.UpdateCustomInfo(true); };
            _Drill.IsWorkingChanged += (block) => { this.UpdateCustomInfo(true); };

            var drillInventory = _Drill.GetInventory(0);
            if (drillInventory == null) return;
            _TransportInventory = new Sandbox.Game.MyInventory((float)drillInventory.MaxVolume / MyAPIGateway.Session.BlocksInventorySizeMultiplier, Vector3.MaxValue, MyInventoryFlags.CanSend);
            //_Drill.Components.Add<Sandbox.Game.MyInventory>((Sandbox.Game.MyInventory)_TransportInventory); Won't work as the gui only could handle one inventory
            if (Mod.Log.ShouldLog(Logging.Level.Event)) Mod.Log.Write(Logging.Level.Event, "DrillSystemBlock {0}: Init Block TransportInventory added to drill MaxVolume {1}", Logging.BlockName(_Drill, Logging.BlockNameOptions.None), (float)drillInventory.MaxVolume / MyAPIGateway.Session.BlocksInventorySizeMultiplier);

            SettingsChanged();

            var dummies = new Dictionary<string, IMyModelDummy>();
            _Drill.Model.GetDummies(dummies);
            foreach (var dummy in dummies)
            {
                if (dummy.Key.ToLower().Contains("detector_emitter"))
                {
                    _EmitterPosition = dummy.Value.Matrix.Translation;
                    break;
                }
            }

            NanobotDrillSystemMod.SyncBlockDataRequestSend(this);
            UpdateCustomInfo(true);
            _TryPushInventoryLast = MyAPIGateway.Session.ElapsedPlayTime.Add(TimeSpan.FromSeconds(10));
            _TryAutoPushInventoryLast = _TryPushInventoryLast;
            _WorkingStateSet = WorkingState.Invalid;
            _SoundVolumeSet = -1;
            _IsInit = true;
            if (Mod.Log.ShouldLog(Logging.Level.Event)) Mod.Log.Write(Logging.Level.Event, "DrillSystemBlock {0}: Init -> done", Logging.BlockName(_Drill, Logging.BlockNameOptions.None));
        }

        /// <summary>
        ///
        /// </summary>
        /// <returns></returns>
        private float ComputeRequiredElectricPower()
        {
            if (_Drill == null) return 0f;
            var required = 0f;
            if (_Drill.Enabled)
            {
                required += Settings.MaximumRequiredElectricPowerStandby;
                required += _PowerDrilling || State.Drilling ? Settings.MaximumRequiredElectricPowerDrilling : 0f;
                required += _PowerFilling || State.Filling ? Settings.MaximumRequiredElectricPowerFilling : 0f;
                required += _PowerTransporting || State.Transporting ? Settings.MaximumRequiredElectricPowerTransport : 0f;
            }
            if (MyAPIGateway.Session.IsServer && Mod.Log.ShouldLog(Logging.Level.Info)) Mod.Log.Write(Logging.Level.Info, "DrillSystemBlock {0}: ComputeRequiredElectricPower Enabled={1} Required={2}", Logging.BlockName(_Drill, Logging.BlockNameOptions.None), _Drill.Enabled, required);
            return required;
        }

        /// <summary>
        ///
        /// </summary>
        /// <param name="required"></param>
        /// <returns></returns>
        private bool HasRequiredElectricPower(bool drill, bool fill, bool transport)
        {
            if (_Drill == null) return false;
            if (_CreativeModeActive) return true;

            var enought = true;
            var changeDrill = false; var changeFill = false; var changeTransport = false;
            if (drill && !_PowerDrilling) { _PowerDrilling = true; changeDrill = true; }
            if (fill && !_PowerFilling) { _PowerFilling = true; changeFill = true; }
            if (transport && !_PowerTransporting) { _PowerTransporting = true; changeTransport = true; }
            var resourceSink = _Drill.ResourceSink as Sandbox.Game.EntityComponents.MyResourceSinkComponent;
            if (resourceSink != null)
            {
                if (changeDrill || changeFill || changeTransport) resourceSink.Update();
                enought = resourceSink.IsPoweredByType(ElectricityId);
                if (changeDrill || changeFill || changeTransport)
                {
                    if (changeDrill) _PowerDrilling = false;
                    if (changeFill) _PowerFilling = false;
                    if (changeTransport) _PowerTransporting = false;
                    resourceSink.Update();
                }
            }

            if (Mod.Log.ShouldLog(Logging.Level.Info)) Mod.Log.Write(Logging.Level.Info, "DrillSystemBlock {0}: HasRequiredElectricPower {1} ({2},{3},{4}", Logging.BlockName(_Drill, Logging.BlockNameOptions.None), enought, drill, fill, transport);
            return enought;
        }

        /// <summary>
        ///
        /// </summary>
        public override void Close()
        {
            if (Mod.Log.ShouldLog(Logging.Level.Event)) Mod.Log.Write(Logging.Level.Event, "DrillSystemBlock {0}: Close", Logging.BlockName(_Drill, Logging.BlockNameOptions.None));
            if (_IsInit)
            {
                Settings.Save(Entity, NanobotDrillSystemMod.ModGuid);
                if (Mod.Log.ShouldLog(Logging.Level.Event)) Mod.Log.Write(Logging.Level.Event, "DrillSystemBlock {0}: Close Saved Settings {1}", Logging.BlockName(_Drill, Logging.BlockNameOptions.None), Settings.GetAsXML());
                lock (NanobotDrillSystemMod.DrillSystems)
                {
                    NanobotDrillSystemMod.DrillSystems.Remove(Entity.EntityId);
                }

                //Stop effects
                State.CurrentTransportTarget = null;
                State.Ready = false;
                UpdateEffects();
            }
            base.Close();
        }

        /// <summary>
        ///
        /// </summary>
        public override void UpdateBeforeSimulation()
        {
            try
            {
                base.UpdateBeforeSimulation();

                if (_Drill == null || !_IsInit) return;

                if (!MyAPIGateway.Utilities.IsDedicated)
                {
                    if ((Settings.Flags & (SyncBlockSettings.Settings.ShowArea | SyncBlockSettings.Settings.RemoteShowArea)) != 0)
                    {
                        var colorDrill = _Drill.SlimBlock.GetColorMask().HSVtoColor();
                        var color = Color.FromNonPremultiplied(colorDrill.R, colorDrill.G, colorDrill.B, 255);
                        var areaBoundingBox = Settings.AreaBoundingBox;
                        var matrix = _Drill.WorldMatrix;
                        matrix.Translation = Vector3D.Transform(GetEffectiveOffset(), matrix);
                        MySimpleObjectDraw.DrawTransparentBox(ref matrix, ref areaBoundingBox, ref color, MySimpleObjectRasterizer.Solid, 1, 0.04f, RangeGridResourceId, null, false);
                    }

                    _UpdateEffectsInterval = (_UpdateEffectsInterval++) % 2;
                    if (_UpdateEffectsInterval == 0) UpdateEffects();
                }
            }
            catch (Exception ex)
            {
                if (Mod.Log.ShouldLog(Logging.Level.Error)) Mod.Log.Write(Logging.Level.Error, "DrillSystemBlock {0}: UpdateBeforeSimulation Exception:{1}", Logging.BlockName(_Drill, Logging.BlockNameOptions.None), ex);
            }
        }

        /// <summary>
        ///
        /// </summary>
        public override void UpdateBeforeSimulation10()
        {
            base.UpdateBeforeSimulation10();
            UpdateBeforeSimulation10_100(true);
        }

        /// <summary>
        ///
        /// </summary>
        public override void UpdateBeforeSimulation100()
        {
            base.UpdateBeforeSimulation100();
            UpdateBeforeSimulation10_100(false);
        }

        /// <summary>
        ///
        /// </summary>
        public override void UpdatingStopped()
        {
            if (Mod.Log.ShouldLog(Logging.Level.Event)) Mod.Log.Write(Logging.Level.Event, "DrillSystemBlock {0}: UpdatingStopped", Logging.BlockName(_Drill, Logging.BlockNameOptions.None));
            if (_IsInit)
            {
                Settings.Save(Entity, NanobotDrillSystemMod.ModGuid);
            }
            //Stop sound effects
            StopSoundEffects();
            _WorkingStateSet = WorkingState.Invalid;
            base.UpdatingStopped();
        }

        /// <summary>
        ///
        /// </summary>
        /// <param name="fast"></param>
        private void UpdateBeforeSimulation10_100(bool fast)
        {
            try
            {
                if (_Drill == null) return;
                if (!_IsInit) Init();
                if (!_IsInit) return;

                if (_Delay > 0)
                {
                    _Delay--;
                    return;
                }

                _DelayWatch.Restart();
                if (MyAPIGateway.Session.IsServer)
                {
                    //CreativeToolsEnabled is currently not available but prepared to check it only once here MySession.Static.CreativeToolsEnabled(MySession.Static.Players.TryGetSteamId(drillOwnerPlayerId)))
                    _CreativeModeActive = MyAPIGateway.Session.CreativeMode;

                    CheckRemoteControlState();
                    ServerTryDrillingFillingCollecting();
                    if (!fast)
                    {
                        if ((State.Ready != _PowerReady || State.Drilling != _PowerDrilling || State.Transporting != _PowerTransporting) &&
                            MyAPIGateway.Session.ElapsedPlayTime.Subtract(_UpdatePowerSinkLast).TotalSeconds > 5)
                        {
                            _UpdatePowerSinkLast = MyAPIGateway.Session.ElapsedPlayTime;
                            _PowerReady = State.Ready;
                            _PowerDrilling = State.Drilling;
                            _PowerFilling = State.Filling;
                            _PowerTransporting = State.Transporting;

                            var resourceSink = _Drill.ResourceSink as Sandbox.Game.EntityComponents.MyResourceSinkComponent;
                            if (resourceSink != null)
                            {
                                resourceSink.Update();
                            }
                        }

                        Settings.TrySave(Entity, NanobotDrillSystemMod.ModGuid);
                        if (State.IsTransmitNeeded())
                        {
                            NanobotDrillSystemMod.SyncBlockStateSend(0, this);
                        }
                    }
                }
                else
                {
                    if (State.Changed)
                    {
                        UpdateCustomInfo(true);
                        State.ResetChanged();
                    }
                }
                if (Settings.IsTransmitNeeded())
                {
                    NanobotDrillSystemMod.SyncBlockSettingsSend(0, this);
                }
                if (_UpdateCustomInfoNeeded) UpdateCustomInfo(false);

                _DelayWatch.Stop();
                if (_DelayWatch.ElapsedMilliseconds > 40)
                {
                    _Delay = _RandomDelay.Next(1, 20); //Slowdown a little bit
                    if (Mod.Log.ShouldLog(Logging.Level.Event)) Mod.Log.Write(Logging.Level.Event, "DrillSystemBlock {0}: Delay {1} ({2}ms)", Logging.BlockName(_Drill, Logging.BlockNameOptions.None), _Delay, _DelayWatch.ElapsedMilliseconds);
                }
            }
            catch (Exception ex)
            {
                if (Mod.Log.ShouldLog(Logging.Level.Error)) Mod.Log.Write(Logging.Level.Error, "DrillSystemBlock {0}: UpdateBeforeSimulation10/100 Exception:{1}", Logging.BlockName(_Drill, Logging.BlockNameOptions.None), ex);
            }
        }

        /// <summary>
        /// Try to drill/collect the possible targets
        /// </summary>
        private void ServerTryDrillingFillingCollecting()
        {
            var inventoryFull = State.InventoryFull;
            var missingMaterial = State.MissingMaterial;
            var drilling = false;
            var needdrilling = false;
            var filling = false;
            var needfilling = false;
            var collecting = false;
            var needcollecting = false;
            var transporting = false;
            var ready = _Drill.Enabled && _Drill.IsWorking && _Drill.IsFunctional;
            object currentDrillingEntity = null;
            object currentFillingEntity = null;
            var playTime = MyAPIGateway.Session.ElapsedPlayTime;
            if (ready)
            {
                if (Mod.Log.ShouldLog(Logging.Level.Info)) Mod.Log.Write(Logging.Level.Info, "DrillSystemBlock {0}: ServerTryDrillingFillingCollecting Drill ready: Enabled={1}, IsWorking={2}, IsFunctional={3}, RemoteWorkdisabled={4}", Logging.BlockName(_Drill, Logging.BlockNameOptions.None), _Drill.Enabled, _Drill.IsWorking, _Drill.IsFunctional, ((Settings.Flags & SyncBlockSettings.Settings.RemoteWorkdisabled) != 0));

                ServerTryPushInventory();
                transporting = IsTransportRunnning(playTime);
                if (transporting && State.CurrentTransportIsPick) needdrilling = true;
                if ((Settings.Flags & SyncBlockSettings.Settings.ComponentCollectIfIdle) == 0 && !transporting && !State.InventoryFull) ServerTryCollectingFloatingTargets(out collecting, out needcollecting, out transporting);
                if (Mod.Log.ShouldLog(Logging.Level.Info)) Mod.Log.Write(Logging.Level.Info, "DrillSystemBlock {0}: ServerTryDrillingFillingCollecting Mode {1}", Logging.BlockName(_Drill, Logging.BlockNameOptions.None), Settings.WorkMode);
                switch (Settings.WorkMode)
                {
                    case WorkModes.Drill:
                    case WorkModes.Collect:
                        State.MissingMaterial = false; //Reset state from FillMode
                        if (!State.InventoryFull)
                        {
                            ServerTryDrilling(out drilling, out needdrilling, out transporting, out currentDrillingEntity);
                        }
                        break;

                    case WorkModes.Fill:
                        ServerTryFilling(out filling, out needfilling, out transporting, out currentFillingEntity);
                        break;
                }
                if (((Settings.Flags & SyncBlockSettings.Settings.ComponentCollectIfIdle) != 0) && !transporting && !drilling && !State.InventoryFull) ServerTryCollectingFloatingTargets(out collecting, out needcollecting, out transporting);
            }
            else
            {
                ServerTryDrillingTransportNeeded();
                ServerTryFillingTransportNeeded();
                transporting = IsTransportRunnning(playTime); //Finish running transport
                State.MissingMaterial = false;
                if (Mod.Log.ShouldLog(Logging.Level.Info)) Mod.Log.Write(Logging.Level.Info, "DrillSystemBlock {0}: ServerTryDrillingFillingCollecting Drill not ready: Enabled={1}, IsWorking={2}, IsFunctional={3}", Logging.BlockName(_Drill, Logging.BlockNameOptions.None), _Drill.Enabled || _CreativeModeActive, _Drill.IsWorking, _Drill.IsFunctional);
            }

            if ((State.Drilling && !(drilling || collecting)) || (State.Filling && !(filling || collecting)))
            {
                StartAsyncUpdateSourcesAndTargets(false); //Scan immediately once for new targets
            }

            var readyChanged = State.Ready != ready;
            State.Ready = ready;

            State.Drilling = drilling;
            State.NeedDrilling = needdrilling;
            State.Filling = filling;
            State.NeedFilling = needfilling;
            State.CurrentDrillingEntity = currentDrillingEntity;
            State.CurrentFillingEntity = currentFillingEntity;
            State.Transporting = transporting;

            var customInfoStateChanged = State.InventoryFull != inventoryFull || State.MissingMaterial != missingMaterial || State.CharacterInWorkingArea != LastCharacterInWorkingArea;
            LastCharacterInWorkingArea = State.CharacterInWorkingArea;

            var possibleDrillTargetsChanged = State.PossibleDrillTargets.LastHash != State.PossibleDrillTargets.CurrentHash;
            State.PossibleDrillTargets.LastHash = State.PossibleDrillTargets.CurrentHash;

            var possibleFillTargetsChanged = State.PossibleFillTargets.LastHash != State.PossibleFillTargets.CurrentHash;
            State.PossibleFillTargets.LastHash = State.PossibleFillTargets.CurrentHash;

            var possibleFloatingTargetsChanged = State.PossibleFloatingTargets.LastHash != State.PossibleFloatingTargets.CurrentHash;
            State.PossibleFloatingTargets.LastHash = State.PossibleFloatingTargets.CurrentHash;

            if (possibleDrillTargetsChanged || possibleFillTargetsChanged || possibleFloatingTargetsChanged) State.HasChanged();
            UpdateCustomInfo(possibleDrillTargetsChanged || possibleFillTargetsChanged || possibleFloatingTargetsChanged || readyChanged || customInfoStateChanged);
        }

        /// <summary>
        /// Push ore out of the drill
        /// </summary>
        private void ServerTryPushInventory()
        {
            var drillInventory = _Drill.GetInventory(0);
            State.InventoryFull = drillInventory == null || drillInventory.IsNearlyFull(0.01f); //1% reserve
            if (MyAPIGateway.Session.ElapsedPlayTime.Subtract(_TryAutoPushInventoryLast).TotalSeconds <= 5) return;

            if (drillInventory != null)
            {
                if (drillInventory.Empty())
                {
                    State.InventoryFull = false;
                    return;
                }
                var lastPush = MyAPIGateway.Session.ElapsedPlayTime;

                var tempInventoryItems = new List<MyInventoryItem>();
                drillInventory.GetItems(tempInventoryItems);
                for (int srcItemIndex = tempInventoryItems.Count - 1; srcItemIndex >= 0; srcItemIndex--)
                {
                    var srcItem = tempInventoryItems[srcItemIndex];
                    if (srcItem.Type.TypeId == typeof(MyObjectBuilder_Ore).Name)
                    {
                        if (Mod.Log.ShouldLog(Logging.Level.Verbose)) Mod.Log.Write(Logging.Level.Verbose, "DrillSystemBlock {0}: ServerTryPushInventory TryPush IngotOre: Item={1} Amount={2}", Logging.BlockName(_Drill, Logging.BlockNameOptions.None), srcItem.ToString(), srcItem.Amount);
                        drillInventory.PushComponents(_PossibleSources, (IMyInventory destInventory, IMyInventory srcInventory, ref MyInventoryItem srcItemIn) => { return _Ignore4Ore.Contains(destInventory); }, srcItemIndex, srcItem);
                        _TryAutoPushInventoryLast = lastPush;
                    }
                }
                tempInventoryItems.Clear();
                State.InventoryFull = drillInventory.IsNearlyFull(0.01f); //1% reserve
            }
        }

        /// <summary>
        ///
        /// </summary>
        /// <param name="collecting"></param>
        /// <param name="needcollecting"></param>
        /// <param name="transporting"></param>
        private void ServerTryCollectingFloatingTargets(out bool collecting, out bool needcollecting, out bool transporting)
        {
            collecting = false;
            needcollecting = false;
            transporting = false;
            if (!HasRequiredElectricPower(false, false, true)) return; //-> Not enought power
            lock (State.PossibleFloatingTargets)
            {
                if (Mod.Log.ShouldLog(Logging.Level.Info)) Mod.Log.Write(Logging.Level.Info, "DrillSystemBlock {0}: ServerTryCollectingFloatingTargets PossibleFloatingTargets={1}", Logging.BlockName(_Drill, Logging.BlockNameOptions.None), State.PossibleFloatingTargets.CurrentCount);
                TargetEntityData collectingFirstTarget = null;
                var collectingCount = 0;
                foreach (var targetData in State.PossibleFloatingTargets)
                {
                    if (targetData.Entity != null && !targetData.Ignore)
                    {
                        if (Mod.Log.ShouldLog(Logging.Level.Verbose)) Mod.Log.Write(Logging.Level.Verbose, "DrillSystemBlock {0}: ServerTryCollectingFloatingTargets: {1} distance={2}", Logging.BlockName(_Drill, Logging.BlockNameOptions.None), Logging.BlockName(targetData.Entity), targetData.Distance);
                        needcollecting = true;
                        var added = ServerDoCollectFloating(targetData, out transporting, ref collectingFirstTarget);
                        if (targetData.Ignore) State.PossibleFloatingTargets.ChangeHash();
                        collecting |= added;
                        if (added) collectingCount++;
                        if (transporting || collectingCount >= COLLECT_FLOATINGOBJECTS_SIMULTANEOUSLY)
                        {
                            break; //Max Inventorysize reached or max simultaneously floating object reached
                        }
                    }
                }
                if (collecting && !transporting) ServerDoCollectFloating(null, out transporting, ref collectingFirstTarget); //Starttransport if pending
            }
        }

        /// <summary>
        ///
        /// </summary>
        private void ServerTryDrilling(out bool drilling, out bool needdrilling, out bool transporting, out object currentDrillingEntity)
        {
            drilling = false;
            needdrilling = false;
            currentDrillingEntity = null;
            transporting = ServerTryDrillingTransportNeeded();

            if (!HasRequiredElectricPower(true, false, true)) return; //No power -> nothing to do
            if ((Settings.Flags & SyncBlockSettings.Settings.RemoteWorkdisabled) != 0) return; //Disabled by remote control

            lock (_BackgroundDrillState)
            {
                if (!_BackgroundDrillState.Running)
                {
                    lock (State.PossibleDrillTargets)
                    {
                        if (Mod.Log.ShouldLog(Logging.Level.Info)) Mod.Log.Write(Logging.Level.Info, "DrillSystemBlock {0}: ServerTryDrilling PossibleDrillTargets={1}", Logging.BlockName(_Drill, Logging.BlockNameOptions.None), State.PossibleDrillTargets.CurrentCount);
                        if (State.PossibleDrillTargets.Count > 0)
                        {
                            _BackgroundDrillState.Running = true;
                            MyAPIGateway.Parallel.Start(AsyncServerTryDrilling);
                        }
                        else
                        {
                            _BackgroundDrillState.Working = false;
                            _BackgroundDrillState.NeedWorking = false;
                            _BackgroundDrillState.CurrentEntity = null;
                        }
                    }
                }

                drilling = _BackgroundDrillState.Working;
                needdrilling = _BackgroundDrillState.NeedWorking;
                currentDrillingEntity = _BackgroundDrillState.CurrentEntity;
            }
        }

        private bool ServerTryDrillingTransportNeeded()
        {
            var transporting = false;

            lock (_BackgroundDrillState)
            {
                if (_BackgroundDrillState.TransportMaterials.Count > 0)
                {
                    if (Mod.Log.ShouldLog(Logging.Level.Info))
                    {
                        foreach (var entry in _BackgroundDrillState.TransportMaterials)
                        {
                            var materialDef = MyDefinitionManager.Static.GetVoxelMaterialDefinition(entry.Key);
                            Mod.Log.Write(Logging.Level.Info, "DrillSystemBlock {0}: ServerTryDrilling Removed Total {1} [{2}] amount={3} ", Logging.BlockName(_Drill, Logging.BlockNameOptions.None), materialDef != null ? materialDef.MinedOre : "NULL", entry.Key, entry.Value);
                        }
                    }

                    var start = Vector3D.Transform(_EmitterPosition, _Drill.WorldMatrix);
                    Vector3 spawnDirection = Vector3.Normalize(_BackgroundDrillState.TargetData.CurrentTargetPos - start);

                    var inventory = _Drill.GetInventory(0);
                    var maxVolume = (float)inventory.MaxVolume;

                    foreach (var entry in _BackgroundDrillState.TransportMaterials)
                    {
                        var materialDef = MyDefinitionManager.Static.GetVoxelMaterialDefinition(entry.Key);
                        if (materialDef != null) transporting = UtilsVoxels.HarvestOre(inventory, maxVolume - (float)inventory.CurrentVolume, _BackgroundDrillState.TargetData.CurrentTargetPos, spawnDirection, DRILL_FLOATING_OBJECT_SPAWN_RADIUS, materialDef, entry.Value, DRILL_VOXEL_HARVEST_MULTIPLIER) || transporting;
                    }

                    _BackgroundDrillState.TransportMaterials.Clear();
                }

                if (transporting)
                {
                    //Transport started
                    var playTime = MyAPIGateway.Session.ElapsedPlayTime;
                    AddTransport(playTime, true, ComputePosition(_BackgroundDrillState.TargetData), TimeSpan.FromSeconds(2d * _BackgroundDrillState.TargetData.Distance / Settings.TransportSpeed));
                    _BackgroundDrillState.TargetData = null;
                }

                return transporting;
            }
        }

        /// <summary>
        ///
        /// </summary>
        private void AsyncServerTryDrilling()
        {
            try
            {
                var working = false;
                var needworking = false;
                object currentEntity = null;

                lock (State.PossibleDrillTargets)
                {
                    if (Mod.Log.ShouldLog(Logging.Level.Info)) Mod.Log.Write(Logging.Level.Info, "DrillSystemBlock {0}: AsyncServerTryDrilling PossibleDrillTargets={1}", Logging.BlockName(_Drill, Logging.BlockNameOptions.None), State.PossibleDrillTargets.CurrentCount);
                    if (State.PossibleDrillTargets.Count > 0)
                    {
                        MatrixD emitterMatrix = _Drill.WorldMatrix;
                        emitterMatrix.Translation = Vector3D.Transform(GetEffectiveOffset(), emitterMatrix);

                        var drillInventory = _Drill.GetInventory(0);
                        float maxPossibleVolume = drillInventory != null ? (float)(drillInventory.MaxVolume - drillInventory.CurrentVolume) : 0f;
                        float remainVolume = Math.Min(maxPossibleVolume, NanobotDrillSystemMod.Settings.Drill.DrillingMultiplier * DRILL_FILL_MAX_VOLUME_PER_RUN);
                        var newIgnore = false;
                        foreach (var targetData in State.PossibleDrillTargets)
                        {
                            if (((Settings.Flags & SyncBlockSettings.Settings.ScriptControlled) != 0) && targetData != Settings.CurrentPickedDrillingItem) continue;
                            if (targetData.Ignore) continue;

                            if (Mod.Log.ShouldLog(Logging.Level.Verbose)) Mod.Log.Write(Logging.Level.Verbose, "DrillSystemBlock {0}: AsyncServerTryDrilling: {1} voxel exists={2}", Logging.BlockName(_Drill, Logging.BlockNameOptions.None), targetData, (targetData.Entity != null && !targetData.Entity.Closed));

                            var voxel = targetData.Entity as MyVoxelBase;
                            if (voxel != null && !voxel.Closed && !voxel.MarkedForClose)
                            {
                                needworking = true;
                                var running = AsyncServerDoDrillVoxel(targetData as TargetVoxelData, Settings.AreaBoundingBox, ref emitterMatrix, ref remainVolume);

                                newIgnore |= targetData.Ignore;
                                if (running)
                                {
                                    working = true;
                                    currentEntity = targetData;
                                    if (remainVolume <= 0) break; //No more capacity
                                }
                                continue;
                            }
                            else
                            {
                                targetData.Ignore = true;
                                newIgnore = true;
                            }
                        }
                        if (newIgnore) State.PossibleDrillTargets.ChangeHash();
                    }
                }

                lock (_BackgroundDrillState)
                {
                    _BackgroundDrillState.Working = working;
                    _BackgroundDrillState.NeedWorking = needworking;
                    _BackgroundDrillState.CurrentEntity = currentEntity;
                }
            }
            catch (Exception ex)
            {
                if (Mod.Log.ShouldLog(Logging.Level.Error)) Mod.Log.Write(Logging.Level.Error, "DrillSystemBlock {0}: AsyncServerTryDrilling: Exception {1}", Logging.BlockName(_Drill, Logging.BlockNameOptions.None), ex);
            }
            finally
            {
                _BackgroundDrillState.Running = false;
            }
        }

        /// <summary>
        ///
        /// </summary>
        private void ServerTryFilling(out bool filling, out bool needfilling, out bool transporting, out object currentFillingEntity)
        {
            filling = false;
            needfilling = false;
            currentFillingEntity = null;
            transporting = ServerTryFillingTransportNeeded();

            if (!HasRequiredElectricPower(false, true, true)) return; //No power -> nothing to do
            if ((Settings.Flags & SyncBlockSettings.Settings.RemoteWorkdisabled) != 0) return; //Disabled by remote control

            lock (_BackgroundFillState)
            {
                if (!_BackgroundFillState.Running)
                {
                    lock (State.PossibleFillTargets)
                    {
                        if (Mod.Log.ShouldLog(Logging.Level.Info)) Mod.Log.Write(Logging.Level.Info, "DrillSystemBlock {0}: ServerTryFilling PossibleFillTargets={1}", Logging.BlockName(_Drill, Logging.BlockNameOptions.None), State.PossibleFillTargets.CurrentCount);
                        if (State.PossibleFillTargets.Count > 0)
                        {
                            if (!State.CharacterInWorkingArea)
                            {
                                var materialDef = MyDefinitionManager.Static.GetVoxelMaterialDefinition((byte)Settings.FillMaterial);
                                if (materialDef != null && !string.IsNullOrEmpty(materialDef.MinedOre))
                                {
                                    MyObjectBuilder_Ore mindedOreMaterial = MyObjectBuilderSerializer.CreateNewObject<MyObjectBuilder_Ore>(materialDef.MinedOre);
                                    MyPhysicalItemDefinition definition = MyDefinitionManager.Static.GetPhysicalItemDefinition(mindedOreMaterial);

                                    float remainVolume = NanobotDrillSystemMod.Settings.Drill.DrillingMultiplier * DRILL_FILL_MAX_VOLUME_PER_RUN;
                                    var neededAmount = UtilsVoxels.VoxelVolumeToMinedOreAmount(materialDef, remainVolume, DRILL_VOXEL_HARVEST_MULTIPLIER);

                                    var remainingVolume = (float)(_TransportInventory.MaxVolume - _TransportInventory.CurrentVolume);

                                    var picked = ServerPickFromTransportInventory(mindedOreMaterial.GetId(), ref neededAmount);
                                    if (neededAmount > 0 && remainingVolume > 0) picked = ServerPickFromDrill(mindedOreMaterial.GetId(), definition.Volume, ref neededAmount, ref remainingVolume) || picked;
                                    if (neededAmount > 0 && remainingVolume > 0) picked = PullMaterial(mindedOreMaterial.GetId(), definition.Volume, ref neededAmount, ref remainingVolume) || picked;

                                    if (picked)
                                    {
                                        _BackgroundFillState.Running = true;
                                        _BackgroundFillState.Flag = false;
                                        MyAPIGateway.Parallel.Start(AsyncServerTryFilling);
                                    }
                                    else if (neededAmount > 0)
                                    {
                                        _BackgroundFillState.Working = false;
                                        _BackgroundFillState.NeedWorking = true;
                                        _BackgroundFillState.CurrentEntity = null;
                                        _BackgroundFillState.Flag = true; //Missing material
                                    }
                                }
                            }
                            else
                            {
                                _BackgroundFillState.Working = false;
                                _BackgroundFillState.NeedWorking = true;
                                _BackgroundFillState.CurrentEntity = null;
                                _BackgroundFillState.Flag = false;
                            }
                        }
                        else
                        {
                            _BackgroundFillState.Working = false;
                            _BackgroundFillState.NeedWorking = false;
                            _BackgroundFillState.CurrentEntity = null;
                            _BackgroundFillState.Flag = false;
                        }
                    }
                }

                filling = _BackgroundFillState.Working;
                needfilling = _BackgroundFillState.NeedWorking;
                currentFillingEntity = _BackgroundFillState.CurrentEntity;
            }
        }

        private bool ServerTryFillingTransportNeeded()
        {
            var transporting = false;
            lock (_BackgroundFillState)
            {
                if (!_BackgroundFillState.Running)
                {
                    if (_BackgroundFillState.TransportMaterials.Count > 0)
                    {
                        _BackgroundFillState.TransportMaterials.Clear();
                        transporting = true;
                    }
                    State.MissingMaterial = _BackgroundFillState.Flag;
                }
                if (transporting)
                {
                    //Transport started
                    var playTime = MyAPIGateway.Session.ElapsedPlayTime;
                    AddTransport(playTime, false, ComputePosition(_BackgroundFillState.TargetData), TimeSpan.FromSeconds(2d * _BackgroundFillState.TargetData.Distance / Settings.TransportSpeed));
                    _BackgroundFillState.TargetData = null;
                }
            }
            return transporting;
        }

        /// <summary>
        ///
        /// </summary>
        private void AsyncServerTryFilling()
        {
            try
            {
                var working = false;
                var needworking = false;
                object currentEntity = null;

                lock (State.PossibleFillTargets)
                {
                    if (Mod.Log.ShouldLog(Logging.Level.Info)) Mod.Log.Write(Logging.Level.Info, "DrillSystemBlock {0}: AsyncServerTryFilling PossibleFillTargets={1}", Logging.BlockName(_Drill, Logging.BlockNameOptions.None), State.PossibleFillTargets.CurrentCount);
                    if (State.PossibleFillTargets.Count > 0 && !State.CharacterInWorkingArea)
                    {
                        MatrixD emitterMatrix = _Drill.WorldMatrix;
                        emitterMatrix.Translation = Vector3D.Transform(GetEffectiveOffset(), emitterMatrix);

                        float remainVolume = NanobotDrillSystemMod.Settings.Drill.FillingMultiplier * DRILL_FILL_MAX_VOLUME_PER_RUN;
                        foreach (var targetData in State.PossibleFillTargets)
                        {
                            if (State.CharacterInWorkingArea) break;
                            if (((Settings.Flags & SyncBlockSettings.Settings.ScriptControlled) != 0) && targetData != Settings.CurrentPickedFillingItem) continue;
                            if (targetData.Ignore) continue;

                            if (Mod.Log.ShouldLog(Logging.Level.Verbose)) Mod.Log.Write(Logging.Level.Verbose, "DrillSystemBlock {0}: AsyncServerTryFilling: {1} voxel exists={2}", Logging.BlockName(_Drill, Logging.BlockNameOptions.None), targetData, (targetData.Entity != null && !targetData.Entity.Closed));

                            var voxel = targetData.Entity as MyVoxelBase;
                            if (voxel != null && !voxel.Closed && !voxel.MarkedForClose)
                            {
                                needworking = true;
                                var running = AsyncServerDoFillVoxel(targetData as TargetVoxelData, Settings.AreaBoundingBox, ref emitterMatrix, ref remainVolume);
                                if (targetData.Ignore) State.PossibleFillTargets.ChangeHash();
                                if (running)
                                {
                                    working = true;
                                    currentEntity = targetData;
                                    if (remainVolume <= 0) break; //No more capacity
                                }
                                continue;
                            }
                            else
                            {
                                targetData.Ignore = true;
                            }
                        }
                    }
                }

                if (!_TransportInventory.Empty())
                {
                    MyAPIGateway.Utilities.InvokeOnGameThread(() =>
                    {
                        ServerEmptyTranportInventory(true);
                    }, "AsyncServerTryFilling.ServerEmptyTranportInventory");
                }

                lock (_BackgroundFillState)
                {
                    _BackgroundFillState.Working = working;
                    _BackgroundFillState.NeedWorking = needworking;
                    _BackgroundFillState.CurrentEntity = currentEntity;
                }
            }
            catch (Exception ex)
            {
                if (Mod.Log.ShouldLog(Logging.Level.Error)) Mod.Log.Write(Logging.Level.Error, "DrillSystemBlock {0}: AsyncServerTryFilling: Exception {1}", Logging.BlockName(_Drill, Logging.BlockNameOptions.None), ex);
            }
            finally
            {
                _BackgroundFillState.Running = false;
            }
        }

        /// <summary>
        ///
        /// </summary>
        private bool IsTransportRunnning(TimeSpan playTime)
        {
            if (State.CurrentTransportStartTime > TimeSpan.Zero)
            {
                if (playTime.Subtract(State.CurrentTransportStartTime) < State.CurrentTransportTime)
                {
                    //Last transport still running -> wait
                    return true;
                }

                if (State.NextTransportTime != TimeSpan.Zero)
                {
                    State.LastTransportTarget = State.CurrentTransportTarget;

                    if (State.CurrentTransportIsPick != State.NextTransportIsPick)
                    {
                        SetTransportEffects(false);
                    }
                    State.CurrentTransportIsPick = State.NextTransportIsPick;
                    State.CurrentTransportTarget = State.NextTransportTarget;
                    State.CurrentTransportTime = State.NextTransportTime;
                    State.CurrentTransportStartTime = playTime;

                    State.NextTransportTime = TimeSpan.Zero;
                    State.NextTransportTarget = null;
                    return true;
                }
                else
                {
                    State.LastTransportTarget = State.CurrentTransportTarget;
                    State.CurrentTransportStartTime = TimeSpan.Zero;
                    State.CurrentTransportTarget = null;
                }
            }
            else
            {
                State.CurrentTransportTarget = null;
            }
            SetTransportEffects(false);
            return false;
        }

        /// <summary>
        ///
        /// </summary>
        private void AddTransport(TimeSpan playTime, bool isPick, Vector3D? target, TimeSpan transportTime)
        {
            if (IsTransportRunnning(playTime))
            {
                //Last transport still running -> add as next (previous next is overwritten)
                State.NextTransportIsPick = isPick;
                State.NextTransportTarget = target;
                State.NextTransportTime = transportTime;
            }
            else
            {
                State.CurrentTransportIsPick = isPick;
                State.CurrentTransportTarget = target;
                State.CurrentTransportTime = transportTime;
                State.CurrentTransportStartTime = playTime;
            }
        }

        /// <summary>
        ///
        /// </summary>
        private void UpdateCustomInfo(bool changed)
        {
            _UpdateCustomInfoNeeded |= changed;
            var playTime = MyAPIGateway.Session.ElapsedPlayTime;
            if (_UpdateCustomInfoNeeded && (playTime.Subtract(_UpdateCustomInfoLast).TotalSeconds >= 2))
            {
                _Drill.RefreshCustomInfo();
                TriggerTerminalRefresh();
                _UpdateCustomInfoLast = playTime;
                _UpdateCustomInfoNeeded = false;
            }
        }

        /// <summary>
        ///
        /// </summary>
        public void TriggerTerminalRefresh()
        {
            //Workaround as long as RaisePropertiesChanged is not public
            if (_Drill != null && MyAPIGateway.Gui.InteractedEntity != null)
            {
                var action = _Drill.GetActionWithName("UseConveyor");
                if (action != null)
                {
                    action.Apply(_Drill);
                    action.Apply(_Drill);
                }
            }
        }

        /// <summary>
        ///
        /// </summary>
        private bool ServerDoCollectFloating(TargetEntityData targetData, out bool transporting, ref TargetEntityData collectingFirstTarget)
        {
            transporting = false;
            var collecting = false;
            var canAdd = false;
            var isEmpty = true;

            var playTime = MyAPIGateway.Session.ElapsedPlayTime;
            transporting = IsTransportRunnning(playTime);
            if (transporting) return false;
            if (targetData != null)
            {
                var target = targetData.Entity as IMyEntity;
                if (target != null)
                {
                    var floating = target as MyFloatingObject;
                    var floatingFirstTarget = collectingFirstTarget != null ? collectingFirstTarget.Entity as MyFloatingObject : null;

                    canAdd = collectingFirstTarget == null || (floatingFirstTarget != null && floating != null);
                    if (canAdd)
                    {
                        if (floating != null) collecting = EmptyFloatingObject(floating, _Drill.GetInventory(0), out isEmpty);
                        else
                        {
                            collecting = EmptyBlockInventories(target, _Drill.GetInventory(0), out isEmpty);
                            if (isEmpty)
                            {
                                var character = target as IMyCharacter;
                                if (character != null && character.IsBot)
                                {
                                    //Wolf, Spider, ...
                                    target.Delete();
                                }
                            }
                        }

                        if (collecting && collectingFirstTarget == null) collectingFirstTarget = targetData;
                        targetData.Ignore = isEmpty;

                        if (Mod.Log.ShouldLog(Logging.Level.Verbose)) Mod.Log.Write(Logging.Level.Verbose, "DrillSystemBlock {0}: ServerDoCollectFloating {1} Try pick floating running={2}", Logging.BlockName(_Drill, Logging.BlockNameOptions.None), Logging.BlockName(target), collecting);
                    }
                }
                else
                {
                    targetData.Ignore = true; //No Entity
                }
            }
            if (collectingFirstTarget != null)
            {
                //Transport started
                AddTransport(playTime, true, ComputePosition(collectingFirstTarget.Entity), TimeSpan.FromSeconds(2d * collectingFirstTarget.Distance / Settings.TransportSpeed));
                transporting = true;
                collectingFirstTarget = null;
            }

            return collecting;
        }

        /// <summary>
        ///
        /// </summary>
        private bool AsyncServerDoDrillVoxel(TargetVoxelData targetData, BoundingBoxD box, ref MatrixD emitterMatrix, ref float remainVolume)
        {
            if (Mod.Log.ShouldLog(Logging.Level.Info)) Mod.Log.Write(Logging.Level.Info, "DrillSystemBlock {0}: AsyncServerDoDrillVoxel begin Voxel={1}, Position={2}, AmountPerSec={3}, Voxel(size={4},min={5},max={6})", Logging.BlockName(_Drill, Logging.BlockNameOptions.None), targetData.ToString(), targetData.WorldPos, remainVolume, targetData.VoxelCoordMax - targetData.VoxelCoordMin, targetData.VoxelCoordMin, targetData.VoxelCoordMax);

            var currentremainVolume = remainVolume;
            bool isEmpty = true;
            var running = false;

            var removedContent = UtilsVoxels.VoxelRemoveContent(targetData.Voxel, _VoxelDataCacheFillRemove, _TempVoxelBoxes, targetData.VoxelCoordMin, targetData.VoxelCoordMax, box, ref emitterMatrix, false,
            (MyVoxelBase voxelMap, ref Vector3I voxelCoordMin, ref Vector3I voxelCoordMax, ref Vector3D worldPosition, byte material, float volume, ISet<byte> ignoreMaterial, ref bool ignoreItem) =>
            {
                var enabled = false;
                var spawn = false;

                var materialDef = MyDefinitionManager.Static.GetVoxelMaterialDefinition(material);
                if (material != MyVoxelConstants.NULL_MATERIAL && materialDef != null && !string.IsNullOrEmpty(materialDef.MinedOre))
                {
                    enabled = DrillPriority.GetEnabled(materialDef.MinedOre);
                    if (Settings.WorkMode == WorkModes.Collect)
                    {
                        //Cut out only materials that should be collected
                        if (!enabled)
                        {
                            ignoreItem = true; //ignore once
                            ignoreMaterial.Add(material); //Do not call again
                        }
                        isEmpty = false;
                    }
                    else
                    {
                        //Cut out anything but collect only enabled and spawn the others
                        spawn = !enabled;
                        isEmpty = false;
                    }

                    if (!ignoreItem)
                    {
                        targetData.CurrentTargetPos = worldPosition;
                        targetData.CurrentMaterialDef = materialDef;
                        float currentvolume;
                        lock (_BackgroundDrillState)
                        {
                            _BackgroundDrillState.TargetData = targetData;
                            _BackgroundDrillState.TransportMaterials.TryGetValue(material, out currentvolume);
                            _BackgroundDrillState.TransportMaterials[material] = spawn ? -(Math.Abs(currentvolume) + volume) : (Math.Abs(currentvolume) + volume);
                        }
                        currentremainVolume -= volume;
                        running = true;
                    }
                }

                if (isEmpty && !running)
                {
                    if (Mod.Log.ShouldLog(Logging.Level.Verbose)) Mod.Log.Write(Logging.Level.Verbose, "DrillSystemBlock {0}: AsyncServerDoDrillVoxel empty voxel found! Added VoxelMat={1} Amount={2} Distance={3} VoxelCoord={4}", Logging.BlockName(_Drill, Logging.BlockNameOptions.None), targetData.MaterialDef, targetData.Amount, targetData.Distance, targetData.VoxelCoordMin);
                }

                if (Mod.Log.ShouldLog(Logging.Level.Verbose)) Mod.Log.Write(Logging.Level.Verbose, "DrillSystemBlock {0}: AsyncServerDoDrillVoxel Removed {1} amount={2} enabled={3} ignoreItem={4} remainDrillVolume={5}", Logging.BlockName(_Drill, Logging.BlockNameOptions.None), (materialDef != null ? materialDef.MinedOre : "<NULL>"), volume, enabled, ignoreItem, currentremainVolume);
                if (currentremainVolume <= 0)
                {
                    targetData.CurrentTargetPos = worldPosition; //Last drilled position
                    return false; //Max drill capacity reached
                }
                else
                {
                    return true;
                }
            });

            if (MyAPIGateway.Utilities.IsDedicated && (removedContent > 0))
            {
                NanobotDrillSystemMod.SyncBlockVoxelBoxesFillRemoveAddQueue(targetData.Voxel, targetData.VoxelCoordMin, targetData.VoxelCoordMax, _TempVoxelBoxes, MyVoxelConstants.NULL_MATERIAL, -removedContent);
            }
            _TempVoxelBoxes.Clear();

            remainVolume = currentremainVolume;
            targetData.Ignore = isEmpty;
            if (Mod.Log.ShouldLog(Logging.Level.Info)) Mod.Log.Write(Logging.Level.Info, "DrillSystemBlock {0}: AsyncServerDoDrillVoxel end Voxel={1}, Position={2}, AmountPerSec={3}, Voxel(size={4}, min={5}, max={6}), isEmpty={7}", Logging.BlockName(_Drill, Logging.BlockNameOptions.None), targetData.ToString(), targetData.WorldPos, remainVolume, targetData.VoxelCoordMax - targetData.VoxelCoordMin, targetData.VoxelCoordMin, targetData.VoxelCoordMax, isEmpty);
            return running;
        }

        /// <summary>
        ///
        /// </summary>
        private bool AsyncServerDoFillVoxel(TargetVoxelData targetData, BoundingBoxD box, ref MatrixD emitterMatrix, ref float remainVolume)
        {
            if (Mod.Log.ShouldLog(Logging.Level.Info)) Mod.Log.Write(Logging.Level.Info, "DrillSystemBlock {0}: AsyncServerDoFillVoxel begin Voxel={1}, Position={2}, AmountPerSec={3}, Voxel(size={4}, min={5}, max={6})", Logging.BlockName(_Drill, Logging.BlockNameOptions.None), targetData.ToString(), targetData.WorldPos, remainVolume, targetData.VoxelCoordMax - targetData.VoxelCoordMin, targetData.VoxelCoordMin, targetData.VoxelCoordMax);

            var currentremainVolume = remainVolume;
            bool isFull = true;
            var running = false;

            var materialDef = MyDefinitionManager.Static.GetVoxelMaterialDefinition((byte)Settings.FillMaterial);
            if (materialDef == null) return false;

            var addedContent = UtilsVoxels.VoxelAddContent(targetData.Voxel, _VoxelDataCacheFillRemove, _TempVoxelBoxes, targetData.VoxelCoordMin, targetData.VoxelCoordMax, box, ref emitterMatrix, false,
            (MyVoxelBase voxelMap, ref Vector3I voxelCoordMin, ref Vector3I voxelCoordMax, ref Vector3D worldPosition, ref byte material, ref float volume) =>
            {
                isFull = false;
                targetData.CurrentTargetPos = worldPosition;
                material = materialDef.Index;
                var missingVolume = (MyVoxelConstants.VOXEL_VOLUME_IN_METERS) - volume;
                if (missingVolume < 0) return true;

                lock (_BackgroundFillState)
                {
                    var neededAmount = UtilsVoxels.VoxelVolumeToMinedOreAmount(materialDef, missingVolume, DRILL_VOXEL_HARVEST_MULTIPLIER);
                    MyObjectBuilder_Ore mindedOreMaterial = MyObjectBuilderSerializer.CreateNewObject<MyObjectBuilder_Ore>(materialDef.MinedOre);
                    var availableAmount = _TransportInventory.RemoveMaxItems((MyFixedPoint)neededAmount, mindedOreMaterial);
                    var missingAmount = (float)((MyFixedPoint)neededAmount - availableAmount);
                    if (missingAmount > 0)
                    {
                        volume = UtilsVoxels.MinedOreAmountToVoxelVolume(materialDef, (float)availableAmount, DRILL_VOXEL_HARVEST_MULTIPLIER);
                        _BackgroundFillState.Flag = true;
                    }
                    else volume += missingVolume;
                    float currentAmount;
                    _BackgroundFillState.TransportMaterials.TryGetValue(material, out currentAmount);
                    _BackgroundFillState.TransportMaterials[material] = currentAmount + (float)availableAmount;
                    _BackgroundFillState.TargetData = targetData;
                }
                currentremainVolume -= volume;// volume;
                running = true;

                if (Mod.Log.ShouldLog(Logging.Level.Verbose)) Mod.Log.Write(Logging.Level.Verbose, "DrillSystemBlock {0}: AsyncServerDoFillVoxel Added {1} missingVolume={2} addedVolume={3} remainFillVolume={4}", Logging.BlockName(_Drill, Logging.BlockNameOptions.None), materialDef != null ? materialDef.MinedOre : "<NULL>", missingVolume, volume, currentremainVolume);
                if (currentremainVolume <= 0 || _BackgroundFillState.Flag || State.CharacterInWorkingArea)
                {
                    targetData.CurrentTargetPos = worldPosition; //Last filled position
                    currentremainVolume = 0;
                    return false; //Max fill capacity reached or no more material or character detected
                }
                else
                {
                    return true;
                }
            });

            if (MyAPIGateway.Utilities.IsDedicated && (addedContent > 0))
            {
                NanobotDrillSystemMod.SyncBlockVoxelBoxesFillRemoveAddQueue(targetData.Voxel, targetData.VoxelCoordMin, targetData.VoxelCoordMax, _TempVoxelBoxes, materialDef.Index, addedContent);
            }
            _TempVoxelBoxes.Clear();

            remainVolume = currentremainVolume;
            targetData.Ignore = isFull;
            if (Mod.Log.ShouldLog(Logging.Level.Info)) Mod.Log.Write(Logging.Level.Info, "DrillSystemBlock {0}: AsyncServerDoFillVoxel end Voxel={1}, Position={2}, AmountPerSec={3}, Voxel(size={4}, min={5}, max={6}), isFull={7}", Logging.BlockName(_Drill, Logging.BlockNameOptions.None), targetData.ToString(), targetData.WorldPos, remainVolume, targetData.VoxelCoordMax - targetData.VoxelCoordMin, targetData.VoxelCoordMin, targetData.VoxelCoordMax, isFull);
            return running;
        }

        /// <summary>
        /// Take already picked material into account
        /// </summary>
        private bool ServerPickFromTransportInventory(MyDefinitionId materialId, ref float neededAmount)
        {
            bool picked = false;
            var tempInventoryItems = new List<MyInventoryItem>();
            _TransportInventory.GetItems(tempInventoryItems);
            for (int i1 = tempInventoryItems.Count - 1; i1 >= 0; i1--)
            {
                var srcItem = tempInventoryItems[i1];
                if (srcItem != null && (MyDefinitionId)srcItem.Type == materialId && srcItem.Amount > 0)
                {
                    var pickedAmount = MyFixedPoint.Min((MyFixedPoint)neededAmount, srcItem.Amount);
                    if (pickedAmount > 0)
                    {
                        neededAmount -= (float)pickedAmount;
                        picked = true;
                    }
                    if (Mod.Log.ShouldLog(Logging.Level.Verbose)) Mod.Log.Write(Logging.Level.Verbose, "DrillSystemBlock {0}: ServerPickFromTransportInventory: {1}: missingAmount={2} pickedAmount={3}", Logging.BlockName(_Drill, Logging.BlockNameOptions.None), materialId, neededAmount, pickedAmount);
                }
                if (neededAmount <= 0) break;
            }
            tempInventoryItems.Clear();
            return picked;
        }

        /// <summary>
        /// Try to pick needed material from own inventory, if successfull material is moved into transport inventory
        /// </summary>
        private bool ServerPickFromDrill(MyDefinitionId materialId, float volume, ref float neededAmount, ref float remainingVolume)
        {
            if (Mod.Log.ShouldLog(Logging.Level.Verbose)) Mod.Log.Write(Logging.Level.Verbose, "DrillSystemBlock {0}: ServerPickFromDrill Try: {1}={2}", Logging.BlockName(_Drill, Logging.BlockNameOptions.None), materialId, neededAmount);

            var drillInventory = _Drill.GetInventory(0);
            if (drillInventory == null || drillInventory.Empty())
            {
                if (Mod.Log.ShouldLog(Logging.Level.Verbose)) Mod.Log.Write(Logging.Level.Verbose, "DrillSystemBlock {0}: ServerPickFromDrill welder empty: {1}={2}", Logging.BlockName(_Drill, Logging.BlockNameOptions.None), materialId, neededAmount);
                return false;
            }

            var picked = false;
            var tempInventoryItems = new List<MyInventoryItem>();
            drillInventory.GetItems(tempInventoryItems);
            for (int i1 = tempInventoryItems.Count - 1; i1 >= 0; i1--)
            {
                var srcItem = tempInventoryItems[i1];
                if (srcItem != null && (MyDefinitionId)srcItem.Type == materialId && srcItem.Amount > 0)
                {
                    var maxpossibleAmount = Math.Min(neededAmount, Math.Floor(remainingVolume / volume));
                    var pickedAmount = MyFixedPoint.Min((MyFixedPoint)maxpossibleAmount, srcItem.Amount);
                    if (pickedAmount > 0)
                    {
                        drillInventory.RemoveItems(srcItem.ItemId, pickedAmount);
                        var physicalObjBuilder = (MyObjectBuilder_PhysicalObject)MyObjectBuilderSerializer.CreateNewObject((MyDefinitionId)srcItem.Type);
                        _TransportInventory.AddItems(pickedAmount, physicalObjBuilder);

                        neededAmount -= (float)pickedAmount;
                        remainingVolume -= (float)pickedAmount * volume;

                        picked = true;
                    }
                    if (Mod.Log.ShouldLog(Logging.Level.Verbose)) Mod.Log.Write(Logging.Level.Verbose, "DrillSystemBlock {0}: ServerPickFromDrill: {1}: missingAmount={2} pickedAmount={3} maxpossibleAmount={4} remainingVolume={5} transportVolumeTotal={6}", Logging.BlockName(_Drill, Logging.BlockNameOptions.None), materialId, neededAmount, pickedAmount, maxpossibleAmount, remainingVolume, _TransportInventory.CurrentVolume);
                }
                if (neededAmount <= 0 || remainingVolume <= 0) break;
            }
            tempInventoryItems.Clear();
            return picked;
        }

        /// <summary>
        /// Check if the transport inventory is empty after filling/collecting, if not move items back to drill inventory
        /// </summary>
        private bool ServerEmptyTranportInventory(bool push)
        {
            var empty = _TransportInventory.Empty();
            if (!empty)
            {
                if (!_CreativeModeActive)
                {
                    var drillInventory = _Drill.GetInventory(0);
                    if (drillInventory != null)
                    {
                        if (push && !drillInventory.Empty())
                        {
                            if (Mod.Log.ShouldLog(Logging.Level.Verbose)) Mod.Log.Write(Logging.Level.Verbose, "DrillSystemBlock {0}: ServerEmptyTranportInventory: push={1}: MaxVolume={2} CurrentVolume={3} Timeout={4}", Logging.BlockName(_Drill, Logging.BlockNameOptions.None), push, drillInventory.MaxVolume, drillInventory.CurrentVolume, MyAPIGateway.Session.ElapsedPlayTime.Subtract(_TryPushInventoryLast).TotalSeconds);
                            if (MyAPIGateway.Session.ElapsedPlayTime.Subtract(_TryPushInventoryLast).TotalSeconds > 5 && drillInventory.MaxVolume - drillInventory.CurrentVolume < _TransportInventory.CurrentVolume * 1.5f)
                            {
                                if (!drillInventory.PushComponents(_PossibleSources, null))
                                {
                                    //Failed retry after timeout
                                    _TryPushInventoryLast = MyAPIGateway.Session.ElapsedPlayTime;
                                }
                            }
                        }

                        var tempInventoryItems = new List<MyInventoryItem>();
                        _TransportInventory.GetItems(tempInventoryItems);
                        for (int srcItemIndex = tempInventoryItems.Count - 1; srcItemIndex >= 0; srcItemIndex--)
                        {
                            var item = tempInventoryItems[srcItemIndex];
                            if (item == null) continue;

                            //Try to move as much as possible
                            var amount = item.Amount;
                            var moveableAmount = drillInventory.MaxItemsAddable(amount, item.Type);
                            if (moveableAmount > 0)
                            {
                                if (drillInventory.TransferItemFrom(_TransportInventory, srcItemIndex, null, true, moveableAmount, false))
                                {
                                    amount -= moveableAmount;
                                }
                            }
                            if (moveableAmount > 0 && Mod.Log.ShouldLog(Logging.Level.Info)) Mod.Log.Write(Logging.Level.Info, "DrillSystemBlock {0}: ServerEmptyTranportInventory move to drill Item {1} amount={2}", Logging.BlockName(_Drill, Logging.BlockNameOptions.None), item.Type, moveableAmount);
                            if (amount > 0 && Mod.Log.ShouldLog(Logging.Level.Info)) Mod.Log.Write(Logging.Level.Info, "DrillSystemBlock {0}: ServerEmptyTranportInventory (no more room in drill) Item {1} amount={2}", Logging.BlockName(_Drill, Logging.BlockNameOptions.None), item.Type, amount);
                        }
                        tempInventoryItems.Clear();
                    }
                }
                else
                {
                    _TransportInventory.Clear();
                }
                empty = _TransportInventory.Empty();
            }
            State.InventoryFull = !empty;
            return empty;
        }

        /// <summary>
        ///
        /// </summary>
        /// <param name="block"></param>
        /// <returns></returns>
        private bool EmptyBlockInventories(IMyEntity entity, IMyInventory dstInventory, out bool isEmpty)
        {
            var running = false;
            var remainingVolume = (float)dstInventory.MaxVolume - (float)dstInventory.CurrentVolume;

            if (Mod.Log.ShouldLog(Logging.Level.Verbose)) Mod.Log.Write(Logging.Level.Verbose, "DrillSystemBlock {0}: EmptyBlockInventories remainingVolume={1} Entity={2}, InventoryCount={3}", Logging.BlockName(_Drill, Logging.BlockNameOptions.None), remainingVolume, Logging.BlockName(entity, Logging.BlockNameOptions.None), entity.InventoryCount);

            isEmpty = true;
            var oreTypId = typeof(MyObjectBuilder_Ore);
            for (int i1 = 0; i1 < entity.InventoryCount; i1++)
            {
                var srcInventory = entity.GetInventory(i1);
                if (srcInventory.Empty()) continue;

                if (remainingVolume <= 0) return true; //No more transport volume

                var tempInventoryItems = new List<MyInventoryItem>();
                srcInventory.GetItems(tempInventoryItems);
                for (int srcItemIndex = tempInventoryItems.Count - 1; srcItemIndex >= 0; srcItemIndex--)
                {
                    var srcItem = srcInventory.GetItemByID(tempInventoryItems[srcItemIndex].ItemId);
                    if (srcItem == null) continue;
                    if (srcItem.Content.TypeId != oreTypId) continue;

                    var definition = MyDefinitionManager.Static.GetPhysicalItemDefinition(srcItem.Content.GetId());

                    var maxpossibleAmountFP = Math.Min((float)srcItem.Amount, (remainingVolume / definition.Volume));
                    //Real Transport Volume is always bigger than logical _MaxTransportVolume so ceiling is not problem
                    var maxpossibleAmount = (MyFixedPoint)(definition.HasIntegralAmounts ? Math.Ceiling(maxpossibleAmountFP) : maxpossibleAmountFP);
                    var volume = srcInventory.CurrentVolume;
                    if (dstInventory.TransferItemFrom(srcInventory, srcItemIndex, null, true, maxpossibleAmount, false))
                    {
                        remainingVolume -= (float)maxpossibleAmount * definition.Volume;
                        if (Mod.Log.ShouldLog(Logging.Level.Verbose)) Mod.Log.Write(Logging.Level.Verbose, "DrillSystemBlock {0}: EmptyBlockInventories Transfered Item {1} amount={2} remainingVolume={3}", Logging.BlockName(_Drill, Logging.BlockNameOptions.None), srcItem.Content.GetId(), maxpossibleAmount, remainingVolume);
                        running = true;
                        if (remainingVolume <= 0)
                        {
                            isEmpty = false;
                            return true; //No more transport volume
                        }
                    }
                    else
                    {
                        isEmpty = false;
                        return running; //No more space
                    }
                }
                tempInventoryItems.Clear();
            }
            return running;
        }

        /// <summary>
        ///
        /// </summary>
        private bool EmptyFloatingObject(MyFloatingObject floating, IMyInventory dstInventory, out bool isEmpty)
        {
            var running = false;
            isEmpty = floating.WasRemovedFromWorld || floating.MarkedForClose;
            if (!isEmpty)
            {
                var remainingVolume = (float)dstInventory.MaxVolume - (double)dstInventory.CurrentVolume;

                var definition = MyDefinitionManager.Static.GetPhysicalItemDefinition(floating.Item.Content.GetId());
                var startAmount = floating.Item.Amount;

                var maxremainAmount = (MyFixedPoint)(remainingVolume / definition.Volume);
                var maxpossibleAmount = maxremainAmount > floating.Item.Amount ? floating.Item.Amount : maxremainAmount; //Do not use MyFixedPoint.Min !Wrong Implementation could cause overflow!
                if (definition.HasIntegralAmounts) maxpossibleAmount = MyFixedPoint.Floor(maxpossibleAmount);
                if (Mod.Log.ShouldLog(Logging.Level.Verbose)) Mod.Log.Write(Logging.Level.Verbose, "DrillSystemBlock {0}: EmptyFloatingObject remainingVolume={1}, Item={2}, ItemAmount={3}, MaxPossibleAmount={4}, ItemVolume={5})", Logging.BlockName(_Drill, Logging.BlockNameOptions.None), remainingVolume, floating.Item.Content.GetId(), floating.Item.Amount, maxpossibleAmount, definition.Volume);
                if (maxpossibleAmount > 0)
                {
                    if (maxpossibleAmount >= floating.Item.Amount)
                    {
                        MyFloatingObjects.RemoveFloatingObject(floating);
                        isEmpty = true;
                    }
                    else
                    {
                        floating.Item.Amount = floating.Item.Amount - maxpossibleAmount;
                        floating.RefreshDisplayName();
                    }

                    dstInventory.AddItems(maxpossibleAmount, floating.Item.Content);
                    remainingVolume -= (float)maxpossibleAmount * definition.Volume;
                    if (Mod.Log.ShouldLog(Logging.Level.Verbose)) Mod.Log.Write(Logging.Level.Verbose, "DrillSystemBlock {0}: EmptyFloatingObject Removed Item {1} amount={2} remainingVolume={3} remainingItemAmount={4}", Logging.BlockName(_Drill, Logging.BlockNameOptions.None), floating.Item.Content.GetId(), maxpossibleAmount, remainingVolume, floating.Item.Amount);
                    running = true;
                }
            }
            return running;
        }

        /// <summary>
        /// Pull components into drill
        /// </summary>
        private bool PullMaterial(MyDefinitionId materialId, float volume, ref float neededAmount, ref float remainingVolume)
        {
            float availAmount = 0;
            var drillInventory = _Drill.GetInventory(0);
            var maxpossibleAmount = Math.Min(neededAmount, (float)Math.Ceiling(remainingVolume / volume));
            if (Mod.Log.ShouldLog(Logging.Level.Info)) Mod.Log.Write(Logging.Level.Info, "DrillSystemBlock {0}: PullMaterial start: {1}={2} maxpossibleAmount={3} volume={4}", Logging.BlockName(_Drill, Logging.BlockNameOptions.None), materialId, neededAmount, maxpossibleAmount, volume);
            if (maxpossibleAmount <= 0) return false;
            var picked = false;
            lock (_PossibleSources)
            {
                foreach (var srcInventory in _PossibleSources)
                {
                    //Pre Test is 10 timers faster then get the whole list (as copy!) and iterate for nothing
                    if (srcInventory.FindItem(materialId) != null && srcInventory.CanTransferItemTo(drillInventory, materialId))
                    {
                        var tempInventoryItems = new List<MyInventoryItem>();
                        srcInventory.GetItems(tempInventoryItems);
                        for (int srcItemIndex = tempInventoryItems.Count - 1; srcItemIndex >= 0; srcItemIndex--)
                        {
                            var srcItem = tempInventoryItems[srcItemIndex];
                            if (srcItem != null && (MyDefinitionId)srcItem.Type == materialId && srcItem.Amount > 0)
                            {
                                var moved = false;
                                MyFixedPoint amountMoveable = 0;
                                if (Mod.Log.ShouldLog(Logging.Level.Info)) Mod.Log.Write(Logging.Level.Info, "DrillSystemBlock {0}: PullMaterial Found: {1}={2} in {3}", Logging.BlockName(_Drill, Logging.BlockNameOptions.None), materialId, srcItem.Amount, Logging.BlockName(srcInventory));
                                var amountPossible = Math.Min(maxpossibleAmount, (float)srcItem.Amount);
                                if (amountPossible > 0)
                                {
                                    amountMoveable = drillInventory.MaxItemsAddable((MyFixedPoint)amountPossible, materialId);
                                    if (amountMoveable > 0)
                                    {
                                        if (Mod.Log.ShouldLog(Logging.Level.Info)) Mod.Log.Write(Logging.Level.Info, "DrillSystemBlock {0}: PullMaterial Try to move: {1}={2} from {3}", Logging.BlockName(_Drill, Logging.BlockNameOptions.None), materialId, amountMoveable, Logging.BlockName(srcInventory));
                                        moved = drillInventory.TransferItemFrom(srcInventory, srcItemIndex, null, true, amountMoveable);
                                        if (moved)
                                        {
                                            maxpossibleAmount -= (float)amountMoveable;
                                            availAmount += (float)amountMoveable;
                                            if (Mod.Log.ShouldLog(Logging.Level.Info)) Mod.Log.Write(Logging.Level.Info, "DrillSystemBlock {0}: PullMaterial Moved: {1}={2} from {3}", Logging.BlockName(_Drill, Logging.BlockNameOptions.None), materialId, amountMoveable, Logging.BlockName(srcInventory));
                                            picked = ServerPickFromDrill(materialId, volume, ref neededAmount, ref remainingVolume) || picked;
                                        }
                                    }
                                    else
                                    {
                                        //No (more) space in welder
                                        if (Mod.Log.ShouldLog(Logging.Level.Info)) Mod.Log.Write(Logging.Level.Info, "DrillSystemBlock {0}: PullMaterial no more space in welder: {1}", Logging.BlockName(_Drill, Logging.BlockNameOptions.None), materialId);
                                        neededAmount -= availAmount;
                                        remainingVolume -= availAmount * volume;
                                        return picked;
                                    }
                                }
                            }
                            if (maxpossibleAmount <= 0) return picked;
                        }
                        tempInventoryItems.Clear();
                    }
                    if (maxpossibleAmount <= 0) return picked;
                }
            }

            return picked;
        }

        /// <summary>
        ///
        /// </summary>
        public void UpdateSourcesAndTargetsTimer()
        {
            var playTime = MyAPIGateway.Session.ElapsedPlayTime;
            var updateTargets = playTime.Subtract(_LastTargetsUpdate) > NanobotDrillSystemMod.Settings.TargetsUpdateInterval;
            var updateSources = updateTargets && playTime.Subtract(_LastSourceUpdate) > NanobotDrillSystemMod.Settings.SourcesUpdateInterval;
            if (updateTargets)
            {
                StartAsyncUpdateSourcesAndTargets(updateSources);
            }
        }

        /// <summary>
        /// Parse all the connected blocks and find the possible targets and sources of components
        /// </summary>
        private void StartAsyncUpdateSourcesAndTargets(bool updateSource)
        {
            if (!_Drill.UseConveyorSystem)
            {
                lock (_PossibleSources)
                {
                    _PossibleSources.Clear();
                }
            }

            if (!_Drill.Enabled || !_Drill.IsFunctional || State.Ready == false)
            {
                if (Mod.Log.ShouldLog(Logging.Level.Info)) Mod.Log.Write(Logging.Level.Info, "DrillSystemBlock {0}: AsyncUpdateSourcesAndTargets Enabled={1} IsFunctional={2} ---> not ready don't search for targets", Logging.BlockName(_Drill, Logging.BlockNameOptions.None), _Drill.Enabled, _Drill.IsFunctional);
                lock (State.PossibleDrillTargets)
                {
                    State.PossibleDrillTargets.Clear();
                    State.PossibleDrillTargets.RebuildHash();
                }
                lock (State.PossibleFillTargets)
                {
                    State.PossibleFillTargets.Clear();
                    State.PossibleFillTargets.RebuildHash();
                }
                lock (State.PossibleFloatingTargets)
                {
                    State.PossibleFloatingTargets.Clear();
                    State.PossibleFloatingTargets.RebuildHash();
                }
                _AsyncUpdateSourcesAndTargetsRunning = false;
                return;
            }
            ;

            lock (_Drill)
            {
                if (_AsyncUpdateSourcesAndTargetsRunning) return;
                _AsyncUpdateSourcesAndTargetsRunning = true;
                NanobotDrillSystemMod.AddAsyncAction(() => AsyncUpdateSourcesAndTargets(updateSource));
            }
        }

        /// <summary>
        ///
        /// </summary>
        public void AsyncUpdateSourcesAndTargets(bool updateSource)
        {
            try
            {
                if (!State.Ready) return;
                var drillEnabled = DrillPriority.AnyEnabled;
                if (!drillEnabled) return;
                updateSource &= _Drill.UseConveyorSystem;
                int pos = 0;
                try
                {
                    pos = 1;

                    var grids = new List<IMyCubeGrid>();
                    _TempPossibleDrillTargets.Clear();
                    _TempPossibleFillTargets.Clear();
                    _TempPossibleFloatingTargets.Clear();
                    _TempPossibleSources.Clear();
                    _TempIgnore4Ore.Clear();

                    if (Mod.Log.ShouldLog(Logging.Level.Verbose)) Mod.Log.Write(Logging.Level.Verbose, "DrillSystemBlock {0}: AsyncUpdateSourcesAndTargets Search: Settings.WorkMode={1}", Logging.BlockName(_Drill, Logging.BlockNameOptions.None), Settings.WorkMode);

                    switch (Settings.WorkMode)
                    {
                        case WorkModes.Drill:
                        case WorkModes.Collect:
                            AsyncGetEntitiesInRange(_TempPossibleDrillTargets, null, _TempPossibleFloatingTargets);
                            break;

                        case WorkModes.Fill:
                            AsyncGetEntitiesInRange(null, _TempPossibleFillTargets, _TempPossibleFloatingTargets);
                            break;
                    }
                    if (updateSource) AsyncAddBlocksOfGrid(_Drill.CubeGrid, grids, _TempPossibleSources);

                    pos = 2;
                    if (updateSource)
                    {
                        Vector3D posDrill;
                        _Drill.SlimBlock.ComputeWorldCenter(out posDrill);
                        _TempPossibleSources.Sort((a, b) =>
                        {
                            var blockA = a.Owner as IMyCubeBlock;
                            var blockB = b.Owner as IMyCubeBlock;
                            if (blockA != null && blockB != null)
                            {
                                var drillA = blockA as IMyShipDrill;
                                var drillB = blockB as IMyShipDrill;
                                if ((drillA == null) == (drillB == null))
                                {
                                    Vector3D posA;
                                    Vector3D posB;
                                    blockA.SlimBlock.ComputeWorldCenter(out posA);
                                    blockB.SlimBlock.ComputeWorldCenter(out posB);
                                    var distanceA = (int)Math.Abs((posDrill - posA).Length());
                                    var distanceB = (int)Math.Abs((posDrill - posA).Length());
                                    return distanceA - distanceB;
                                }
                                else if (drillA == null)
                                {
                                    return -1;
                                }
                                else
                                {
                                    return 1;
                                }
                            }
                            else if (blockA != null) return -1;
                            else if (blockB != null) return 1;
                            else return 0;
                        });
                    }

                    pos = 3;
                    _TempPossibleDrillTargets.Sort((a, b) =>
                    {
                        //near to far
                        return Utils.CompareDistance(a.Distance, b.Distance);
                    });

                    pos = 4;
                    _TempPossibleFillTargets.Sort((a, b) =>
                    {
                        //Far to near
                        return Utils.CompareDistance(b.Distance, a.Distance);
                    });

                    pos = 5;
                    _TempPossibleFloatingTargets.Sort((a, b) =>
                    {
                        var itemA = a.Entity;
                        var itemB = b.Entity;
                        var itemAFloating = itemA as MyFloatingObject;
                        var itemBFloating = itemB as MyFloatingObject;
                        if (itemAFloating != null && itemBFloating != null)
                        {
                            var priorityA = ComponentCollectPriority.GetPriority(itemAFloating.Item.Content.GetObjectId());
                            var priorityB = ComponentCollectPriority.GetPriority(itemAFloating.Item.Content.GetObjectId());
                            if (priorityA == priorityB)
                            {
                                return Utils.CompareDistance(a.Distance, b.Distance);
                            }
                            else return priorityA - priorityB;
                        }
                        else if (itemAFloating == null) return -1;
                        else if (itemBFloating == null) return 1;
                        return Utils.CompareDistance(a.Distance, b.Distance);
                    });

                    pos = 6;
                    if (Mod.Log.ShouldLog(Logging.Level.Verbose))
                    {
                        lock (Mod.Log)
                        {
                            Mod.Log.Write(Logging.Level.Verbose, "DrillSystemBlock {0}: AsyncUpdateSourcesAndTargets Possible Drill Targets ---> {1}", Logging.BlockName(_Drill, Logging.BlockNameOptions.None), _TempPossibleDrillTargets.Count);
                            Mod.Log.IncreaseIndent(Logging.Level.Verbose);
                            foreach (var drillData in _TempPossibleDrillTargets)
                            {
                                Mod.Log.Write(Logging.Level.Verbose, "Entity: {0}/{1}", Logging.BlockName(drillData.Entity), drillData.ToString());
                            }
                            Mod.Log.DecreaseIndent(Logging.Level.Verbose);
                            Mod.Log.Write(Logging.Level.Verbose, "<---");

                            Mod.Log.Write(Logging.Level.Verbose, "DrillSystemBlock {0}: AsyncUpdateSourcesAndTargets Possible Fill Targets ---> {1}", Logging.BlockName(_Drill, Logging.BlockNameOptions.None), _TempPossibleFillTargets.Count);
                            Mod.Log.IncreaseIndent(Logging.Level.Verbose);
                            foreach (var fillData in _TempPossibleFillTargets)
                            {
                                Mod.Log.Write(Logging.Level.Verbose, "Entity: {0}/{1}", Logging.BlockName(fillData.Entity), fillData.ToString());
                            }
                            Mod.Log.DecreaseIndent(Logging.Level.Verbose);
                            Mod.Log.Write(Logging.Level.Verbose, "<---");

                            Mod.Log.Write(Logging.Level.Verbose, "DrillSystemBlock {0}: AsyncUpdateSourcesAndTargets Possible Floating Targets ---> {1}", Logging.BlockName(_Drill, Logging.BlockNameOptions.None), _TempPossibleFloatingTargets.Count);
                            Mod.Log.IncreaseIndent(Logging.Level.Verbose);
                            foreach (var floatingData in _TempPossibleFloatingTargets)
                            {
                                Mod.Log.Write(Logging.Level.Verbose, "Floating: {0} ({1})", Logging.BlockName(floatingData.Entity), floatingData.Distance);
                            }
                            Mod.Log.DecreaseIndent(Logging.Level.Verbose);
                            Mod.Log.Write(Logging.Level.Verbose, "<---");

                            if (updateSource)
                            {
                                Mod.Log.Write(Logging.Level.Verbose, "DrillSystemBlock {0}: AsyncUpdateSourcesAndTargets Possible Source Blocks --->", Logging.BlockName(_Drill, Logging.BlockNameOptions.None));
                                Mod.Log.IncreaseIndent(Logging.Level.Verbose);
                                foreach (var inventory in _TempPossibleSources)
                                {
                                    Mod.Log.Write(Logging.Level.Verbose, "Inventory: {0}", Logging.BlockName(inventory));
                                }
                                Mod.Log.DecreaseIndent(Logging.Level.Verbose);
                                Mod.Log.Write(Logging.Level.Verbose, "<---");
                            }
                        }
                    }

                    pos = 7;
                    lock (State.PossibleDrillTargets)
                    {
                        State.PossibleDrillTargets.Clear();
                        State.PossibleDrillTargets.AddRange(_TempPossibleDrillTargets);
                        State.PossibleDrillTargets.RebuildHash();
                    }
                    _TempPossibleDrillTargets.Clear();

                    pos = 8;
                    lock (State.PossibleFillTargets)
                    {
                        State.PossibleFillTargets.Clear();
                        State.PossibleFillTargets.AddRange(_TempPossibleFillTargets);
                        State.PossibleFillTargets.RebuildHash();
                    }
                    _TempPossibleFillTargets.Clear();

                    pos = 9;
                    lock (State.PossibleFloatingTargets)
                    {
                        State.PossibleFloatingTargets.Clear();
                        State.PossibleFloatingTargets.AddRange(_TempPossibleFloatingTargets);
                        State.PossibleFloatingTargets.RebuildHash();
                    }
                    _TempPossibleFloatingTargets.Clear();

                    pos = 10;
                    if (updateSource)
                    {
                        lock (_PossibleSources)
                        {
                            _PossibleSources.Clear();
                            _PossibleSources.AddRange(_TempPossibleSources);
                            _Ignore4Ore.Clear();
                            _Ignore4Ore.UnionWith(_TempIgnore4Ore);
                        }
                        _TempPossibleSources.Clear();
                        _TempIgnore4Ore.Clear();
                    }

                    _ContinuouslyError = 0;
                }
                catch (Exception ex)
                {
                    _ContinuouslyError++;
                    if (_ContinuouslyError > 10 || Mod.Log.ShouldLog(Logging.Level.Info) || Mod.Log.ShouldLog(Logging.Level.Verbose))
                    {
                        Mod.Log.Error("DrillSystemBlock {0}: AsyncUpdateSourcesAndTargets exception at {1}: {2}", Logging.BlockName(_Drill, Logging.BlockNameOptions.None), pos, ex);
                        _ContinuouslyError = 0;
                    }
                }
            }
            finally
            {
                _LastTargetsUpdate = MyAPIGateway.Session.ElapsedPlayTime;
                if (updateSource) _LastSourceUpdate = _LastTargetsUpdate;
                _AsyncUpdateSourcesAndTargetsRunning = false;
            }
        }

        /// <summary>
        /// Search for objects/voxels inside range
        /// </summary>
        protected void AsyncGetEntitiesInRange(List<TargetVoxelData> possibleDrillTargets, List<TargetVoxelData> possibleFillTargets, List<TargetEntityData> possibleFloatingTargets)
        {
            if (Mod.Log.ShouldLog(Logging.Level.Verbose)) Mod.Log.Write(Logging.Level.Verbose, "DrillSystemBlock {0}: AsyncGetEntitiesInRange", Logging.BlockName(_Drill, Logging.BlockNameOptions.None));

            MatrixD emitterMatrix = _Drill.WorldMatrix;
            emitterMatrix.Translation = Vector3D.Transform(GetEffectiveOffset(), emitterMatrix);
            var areaBoundingBox = Settings.AreaBoundingBox.TransformFast(emitterMatrix);
            var areaOrientedBox = MyOrientedBoundingBoxD.Create(Settings.AreaBoundingBox, emitterMatrix);
            var emitterPosition = emitterMatrix.Translation;

            List<IMyEntity> entityInRange = null;
            lock (MyAPIGateway.Entities)
            {
                //API not thread save !!!
                entityInRange = MyAPIGateway.Entities.GetTopMostEntitiesInBox(ref areaBoundingBox);
            }

            var characterInWorkingArea = false;
            if (entityInRange != null)
            {
                foreach (var entity in entityInRange)
                {
                    if (Mod.Log.ShouldLog(Logging.Level.Verbose)) Mod.Log.Write(Logging.Level.Verbose, "DrillSystemBlock {0}: AsyncGetEntitiesInRange found {1}", Logging.BlockName(_Drill, Logging.BlockNameOptions.None), Logging.BlockName(entity));
                    var floating = entity as MyFloatingObject;
                    if (floating != null)
                    {
                        if (possibleFloatingTargets != null && !floating.MarkedForClose && ComponentCollectPriority.GetEnabled(floating.Item.Content.GetObjectId()))
                        {
                            var distance = (emitterPosition - floating.WorldMatrix.Translation).Length();
                            possibleFloatingTargets.Add(new TargetEntityData(floating, distance));
                        }
                        continue;
                    }

                    var character = entity as IMyCharacter;
                    if (character != null)
                    {
                        var volume = character.WorldVolume;
                        if (areaOrientedBox.Contains(ref volume) != ContainmentType.Disjoint) characterInWorkingArea = true;
                        if (possibleFloatingTargets != null && character.IsDead && !character.InventoriesEmpty() && !((MyCharacterDefinition)character.Definition).EnableSpawnInventoryAsContainer
                            && ContainsStoreableItems(character))
                        {
                            var distance = (emitterPosition - character.WorldMatrix.Translation).Length();
                            possibleFloatingTargets.Add(new TargetEntityData(character, distance));
                        }
                        continue;
                    }

                    var inventoryBag = entity as IMyInventoryBag;
                    if (inventoryBag != null)
                    {
                        if (possibleFloatingTargets != null && !inventoryBag.InventoriesEmpty() && ContainsStoreableItems(inventoryBag))
                        {
                            var distance = (emitterPosition - inventoryBag.WorldMatrix.Translation).Length();
                            possibleFloatingTargets.Add(new TargetEntityData(inventoryBag, distance));
                        }
                        continue;
                    }

                    var voxelBase = entity as MyVoxelBase;
                    if (voxelBase != null)
                    {
                        if (!voxelBase.MarkedForClose && !voxelBase.Closed && voxelBase.GetOrePriority() != -1 && !voxelBase.IsPreview && voxelBase.InScene)
                        {
                            if (Mod.Log.ShouldLog(Logging.Level.Verbose)) Mod.Log.Write(Logging.Level.Verbose, "DrillSystemBlock {0}: AsyncGetEntitiesInRange found MyVoxelBase size {1}, id={2}, RootVoxel={3}, DebugName={4}, TopMostParent={5}, InScene={6}, isPreview={7}", Logging.BlockName(_Drill, Logging.BlockNameOptions.None), voxelBase.Storage.Size, voxelBase.EntityId, voxelBase.RootVoxel, voxelBase.DebugName, voxelBase.GetTopMostParent(), voxelBase.InScene, voxelBase.IsPreview);
                            if (possibleDrillTargets != null) AsyncAddDrillVoxelBase(ref emitterPosition, ref emitterMatrix, voxelBase, possibleDrillTargets);
                            if (possibleFillTargets != null) AsyncAddFillVoxelBase(ref emitterPosition, ref emitterMatrix, voxelBase, possibleFillTargets);
                        }
                        continue;
                    }
                }
            }
            State.CharacterInWorkingArea = characterInWorkingArea;
        }

        /// <summary>
        /// Check for possible source (destination) inventorties
        /// </summary>
        private void AsyncAddBlocksOfGrid(IMyCubeGrid cubeGrid, List<IMyCubeGrid> grids, List<IMyInventory> possibleSources)
        {
            if (!State.Ready) return; //Block not ready
            if (grids.Contains(cubeGrid)) return; //Allready parsed

            if (Mod.Log.ShouldLog(Logging.Level.Verbose)) Mod.Log.Write(Logging.Level.Verbose, "DrillSystemBlock {0}: AsyncAddBlocksOfGrid AddGrid {1}", Logging.BlockName(_Drill, Logging.BlockNameOptions.None), cubeGrid.DisplayName);
            grids.Add(cubeGrid);

            if (cubeGrid.IsProjected()) return; //We need existing inventories

            var newBlocks = new List<IMySlimBlock>();
            cubeGrid.GetBlocks(newBlocks);

            foreach (var slimBlock in newBlocks)
            {
                AsyncAddBlockIfSource(true, grids, slimBlock, possibleSources);

                var fatBlock = slimBlock.FatBlock;
                if (fatBlock == null) continue;

                var mechanicalConnectionBlock = fatBlock as Sandbox.ModAPI.IMyMechanicalConnectionBlock;
                if (mechanicalConnectionBlock != null)
                {
                    if (mechanicalConnectionBlock.TopGrid != null)
                        AsyncAddBlocksOfGrid(mechanicalConnectionBlock.TopGrid, grids, possibleSources);
                    continue;
                }

                var attachableTopBlock = fatBlock as Sandbox.ModAPI.IMyAttachableTopBlock;
                if (attachableTopBlock != null)
                {
                    if (attachableTopBlock.Base != null && attachableTopBlock.Base.CubeGrid != null)
                        AsyncAddBlocksOfGrid(attachableTopBlock.Base.CubeGrid, grids, possibleSources);
                    continue;
                }

                var connector = fatBlock as Sandbox.ModAPI.IMyShipConnector;
                if (connector != null)
                {
                    if (connector.Status == MyShipConnectorStatus.Connected && connector.OtherConnector != null)
                    {
                        AsyncAddBlocksOfGrid(connector.OtherConnector.CubeGrid, grids, possibleSources);
                    }
                    continue;
                }
            }
        }

        /// <summary>
        /// Check if the given block cloud be used as inventory
        /// </summary>
        private void AsyncAddBlockIfSource(bool isAllreadyChecked, List<IMyCubeGrid> grids, IMySlimBlock block, List<IMyInventory> possibleSources)
        {
            try
            {
                if (possibleSources != null)
                {
                    //Search for sources/destinations of ore
                    var terminalBlock = block.FatBlock as IMyTerminalBlock;
                    if (terminalBlock != null && terminalBlock.EntityId != _Drill.EntityId && terminalBlock.IsFunctional) //Own inventory is no external source (handled internally)
                    {
                        var relation = terminalBlock.GetUserRelationToOwner(_Drill.OwnerId);
                        if (MyRelationsBetweenPlayerAndBlockExtensions.IsFriendly(relation))
                        {
                            try
                            {
                                var drillInventory = _Drill.GetInventory(0);
                                var maxInv = terminalBlock.InventoryCount;
                                for (var idx = 0; idx < maxInv; idx++)
                                {
                                    var inventory = terminalBlock.GetInventory(idx);
                                    if (!possibleSources.Contains(inventory) && inventory.IsConnectedTo(drillInventory))
                                    {
                                        possibleSources.Add(inventory);
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                Mod.Log.Write(Logging.Level.Event, "DrillSystemBlock {0}: AsyncAddBlockIfTargetOrSource1 exception: {1}", Logging.BlockName(_Drill, Logging.BlockNameOptions.None), ex);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Mod.Log.Error("DrillSystemBlock {0}: AsyncAddBlockIfSource2 exception: {1}", Logging.BlockName(_Drill, Logging.BlockNameOptions.None), ex);
                throw;
            }
        }

        /// <summary>
        ///
        /// </summary>
        private void AsyncAddDrillVoxelBase(ref Vector3D emitterPosition, ref MatrixD emitterMatrix, MyVoxelBase voxelBase, List<TargetVoxelData> possibleDrillTargets)
        {
            if (!MyAPIGateway.Session.SessionSettings.EnableVoxelDestruction || voxelBase.GetOrePriority() == -1) return;
            foreach (var drillTarget in possibleDrillTargets)
            {
                if (drillTarget.Voxel.Storage == voxelBase.Storage)
                {
                    //Duplicate storage -> ignore
                    if (Mod.Log.ShouldLog(Logging.Level.Verbose)) Mod.Log.Write(Logging.Level.Verbose, "DrillSystemBlock {0}: AsyncAddDrillVoxelBase dublicate Storage ignore {1}", Logging.BlockName(_Drill, Logging.BlockNameOptions.None), voxelBase.ToString());
                    return;
                }
            }

            if (Mod.Log.ShouldLog(Logging.Level.Verbose)) Mod.Log.Write(Logging.Level.Verbose, "DrillSystemBlock {0}: AsyncAddDrillVoxelBase {1}: -->", Logging.BlockName(_Drill, Logging.BlockNameOptions.None), Logging.BlockName(voxelBase));

            var res = UtilsVoxels.VoxelIterateInShape(voxelBase, _VoxelDataCacheSearch, Settings.AreaBoundingBox, ref emitterMatrix, UtilsVoxels.Shapes.Box, false,
            (byte material, byte content, ISet<byte> ignoreMaterial) =>
            {
                switch (Settings.WorkMode)
                {
                    case WorkModes.Drill:
                        //Drill anything
                        break;

                    case WorkModes.Collect:
                        var materialDef = MyDefinitionManager.Static.GetVoxelMaterialDefinition(material);
                        if (Mod.Log.ShouldLog(Logging.Level.Special1)) Mod.Log.Write(Logging.Level.Special1, "DrillSystemBlock {0}: AsyncAddDrillVoxelBase {1} VoxelMat={2} Amount={3}", Logging.BlockName(_Drill, Logging.BlockNameOptions.None), voxelBase.ToString(), (materialDef != null ? materialDef.ToString() : material.ToString()), content);
                        if (materialDef == null || string.IsNullOrEmpty(materialDef.MinedOre) || !DrillPriority.GetEnabled(materialDef.MinedOre))
                        {
                            ignoreMaterial.Add(material);
                            if (Mod.Log.ShouldLog(Logging.Level.Special1)) Mod.Log.Write(Logging.Level.Special1, "DrillSystemBlock {0}: AsyncAddDrillVoxelBase {1} Not enabled VoxelMat={2} Amount={3}", Logging.BlockName(_Drill, Logging.BlockNameOptions.None), voxelBase.ToString(), materialDef, content);
                            return false;
                        }
                        break;
                }
                return true;
            },
            (MyVoxelBase voxelMap, uint id, ref Vector3I voxelCoordMin, ref Vector3I voxelCoordMax, ref Vector3D worldPosition, Dictionary<byte, float> materials) =>
            {
                KeyValuePair<byte, float>? showKV = null;
                switch (Settings.WorkMode)
                {
                    case WorkModes.Drill:
                        //Show largest amount
                        showKV = materials.Aggregate((l, r) => l.Value > r.Value ? l : r);
                        break;

                    case WorkModes.Collect:
                        //Show highest prio
                        showKV = materials.OrderBy((kv) =>
                        {
                            var md = MyDefinitionManager.Static.GetVoxelMaterialDefinition(kv.Key);
                            return md != null && !string.IsNullOrEmpty(md.MinedOre) ? DrillPriority.GetPriority(md.MinedOre) : -1;
                        }).FirstOrDefault();
                        break;
                }
                if (!showKV.HasValue) return;

                var materialDef = MyDefinitionManager.Static.GetVoxelMaterialDefinition(showKV.Value.Key);
                var distance = (worldPosition - _Drill.WorldMatrix.Translation).Length();
                possibleDrillTargets.Add(new TargetVoxelData(voxelBase, id, distance, voxelCoordMin, voxelCoordMax, worldPosition, materialDef, showKV.Value.Value));
                if (Mod.Log.ShouldLog(Logging.Level.Special1)) Mod.Log.Write(Logging.Level.Special1, "Added VoxelMat={0} Amount={1} Distance={2} VoxelCoord={3}/{4} ({5})", materialDef, showKV.Value.Value, distance, voxelCoordMin, voxelCoordMax, voxelCoordMax - voxelCoordMin);
            });

            if (Mod.Log.ShouldLog(Logging.Level.Verbose))
            {
                var matDict = new Dictionary<MyDefinitionBase, float>();
                possibleDrillTargets.ForEach((e) =>
                {
                    if (e.Entity == voxelBase)
                    {
                        var voxelData = e as TargetVoxelData;
                        if (voxelData.MaterialDef == null) return;
                        float amount;
                        matDict.TryGetValue(voxelData.MaterialDef, out amount);
                        matDict[voxelData.MaterialDef] = amount + voxelData.Amount;
                    }
                });
                foreach (var mat in matDict)
                {
                    Mod.Log.Write(Logging.Level.Verbose, "DrillSystemBlock {0}: AsyncAddDrillVoxelBase {1}: Material {2} Amount={3}", Logging.BlockName(_Drill, Logging.BlockNameOptions.None), Logging.BlockName(voxelBase), mat.Key, mat.Value);
                }
                Mod.Log.Write(Logging.Level.Verbose, "DrillSystemBlock {0}: AsyncAddDrillVoxelBase {1}: Result {2} <--", Logging.BlockName(_Drill, Logging.BlockNameOptions.None), Logging.BlockName(voxelBase), res);
            }
        }

        /// <summary>
        ///
        /// </summary>
        private void AsyncAddFillVoxelBase(ref Vector3D emitterPosition, ref MatrixD emitterMatrix, MyVoxelBase voxelBase, List<TargetVoxelData> possibleFillTargets)
        {
            if (!MyAPIGateway.Session.SessionSettings.EnableVoxelDestruction || voxelBase.GetOrePriority() == -1) return;
            foreach (var fillTarget in possibleFillTargets)
            {
                if (fillTarget.Voxel.Storage == voxelBase.Storage)
                {
                    //Duplicate storage -> ignore
                    if (Mod.Log.ShouldLog(Logging.Level.Verbose)) Mod.Log.Write(Logging.Level.Verbose, "DrillSystemBlock {0}: AsyncAddFillVoxelBase dublicate Storage ignore {1}", Logging.BlockName(_Drill, Logging.BlockNameOptions.None), voxelBase.ToString());
                    return;
                }
            }

            if (Mod.Log.ShouldLog(Logging.Level.Verbose))
            {
                Mod.Log.Write(Logging.Level.Verbose, "DrillSystemBlock {0}: AsyncAddFillVoxelBase {1}: -->", Logging.BlockName(_Drill, Logging.BlockNameOptions.None), voxelBase.ToString());
                Mod.Log.IncreaseIndent(Logging.Level.Verbose);
            }

            UtilsVoxels.VoxelIterateInShape(voxelBase, _VoxelDataCacheSearch, Settings.AreaBoundingBox, ref emitterMatrix, UtilsVoxels.Shapes.Box, true,
            (byte material, byte content, ISet<byte> ignoreMaterial) =>
            {
                //Include all kind of materials (including empty)
                return true;
            },
            (MyVoxelBase voxelMap, uint id, ref Vector3I voxelCoordMin, ref Vector3I voxelCoordMax, ref Vector3D worldPosition, Dictionary<byte, float> materials) =>
            {
                //Contains empty spots AND material where the new could be connected to
                if (materials.ContainsKey(MyVoxelConstants.NULL_MATERIAL) && materials.Count > 1)
                {
                    var distance = (worldPosition - _Drill.WorldMatrix.Translation).Length();
                    possibleFillTargets.Add(new TargetVoxelData(voxelBase, id, distance, voxelCoordMin, voxelCoordMax, worldPosition, null, materials[MyVoxelConstants.NULL_MATERIAL]));
                    if (Mod.Log.ShouldLog(Logging.Level.Special1)) Mod.Log.Write(Logging.Level.Special1, "Added Fillable Distance={0} VoxelCoord={1}/{2} ({3}), amount={4}", distance, voxelCoordMin, voxelCoordMax, voxelCoordMax - voxelCoordMin, materials[MyVoxelConstants.NULL_MATERIAL]);
                }
            });

            if (Mod.Log.ShouldLog(Logging.Level.Verbose))
            {
                Mod.Log.DecreaseIndent(Logging.Level.Verbose);
                Mod.Log.Write(Logging.Level.Verbose, "DrillSystemBlock {0}: AsyncAddFillVoxelBase {1}: <--", Logging.BlockName(_Drill, Logging.BlockNameOptions.None), Logging.BlockName(voxelBase));
            }
        }

        /// <summary>
        /// Check if the inventory contains at least one storeable item (ore)
        /// </summary>
        /// <param name="entity"></param>
        /// <returns></returns>
        private bool ContainsStoreableItems(IMyEntity entity)
        {
            var tempInventoryItems = new List<MyInventoryItem>();
            for (int i1 = 0; i1 < entity.InventoryCount; i1++)
            {
                var srcInventory = entity.GetInventory(i1) as IMyInventory;
                if (srcInventory.Empty()) continue;

                srcInventory.GetItems(tempInventoryItems);
                for (int i2 = 0; i2 < tempInventoryItems.Count; i2++)
                {
                    var srcItem = tempInventoryItems[i2];
                    if (srcItem == null) continue;
                    if (ComponentCollectPriority.GetEnabled(srcItem.Type))
                    {
                        return true;
                    }
                }
                tempInventoryItems.Clear();
            }
            return false;
        }

        /// <summary>
        /// Update custom info of the block
        /// </summary>
        /// <param name="block"></param>
        /// <param name="customInfo"></param>
        private void AppendingCustomInfo(IMyTerminalBlock terminalBlock, StringBuilder customInfo)
        {
            try
            {
                customInfo.Clear();
                //Already done by MyShipDrill
                //customInfo.Append(MyTexts.Get(MyStringId.GetOrCompute("BlockPropertiesText_Type")));
                //customInfo.Append(_Drill.SlimBlock.BlockDefinition.DisplayNameText);
                //customInfo.Append(Environment.NewLine);

                var resourceSink = _Drill.ResourceSink as Sandbox.Game.EntityComponents.MyResourceSinkComponent;
                if (resourceSink != null)
                {
                    //Already done by MyShipDrill
                    //customInfo.Append(MyTexts.Get(MyStringId.GetOrCompute("BlockPropertiesText_MaxRequiredInput")));
                    //MyValueFormatter.AppendWorkInBestUnit(resourceSink.MaxRequiredInputByType(ElectricityId), customInfo);
                    //customInfo.Append(Environment.NewLine);

                    customInfo.Append(MyTexts.Get(MySpaceTexts.BlockPropertiesText_RequiredInput));
                    MyValueFormatter.AppendWorkInBestUnit(resourceSink.RequiredInputByType(ElectricityId), customInfo);
                    customInfo.Append(Environment.NewLine);
                }
                customInfo.Append(Environment.NewLine);

                if ((_Drill.Enabled || _CreativeModeActive) && _Drill.IsWorking && _Drill.IsFunctional)
                {
                    if ((Settings.Flags & SyncBlockSettings.Settings.ScriptControlled) != 0)
                    {
                        customInfo.Append(Texts.Info_CurentDrillEntity + Environment.NewLine);
                        customInfo.Append(string.Format(" -{0}" + Environment.NewLine, Logging.BlockName(Settings.CurrentPickedDrillingItem)));
                        customInfo.Append(Texts.Info_CurentFillEntity + Environment.NewLine);
                        customInfo.Append(string.Format(" -{0}" + Environment.NewLine, Logging.BlockName(Settings.CurrentPickedFillingItem)));
                    }

                    var cnt = 0;
                    if (Settings.WorkMode == WorkModes.Drill || Settings.WorkMode == WorkModes.Collect)
                    {
                        if (State.InventoryFull) customInfo.Append(Texts.Info_InventoryFull + Environment.NewLine);
                        if ((Settings.Flags & SyncBlockSettings.Settings.RemoteWorkdisabled) != 0) customInfo.Append(Texts.Info_DisabledByRemote + Environment.NewLine);

                        customInfo.Append(Texts.Info_ItemsToDrill + Environment.NewLine);
                        lock (State.PossibleDrillTargets)
                        {
                            foreach (var entityData in State.PossibleDrillTargets)
                            {
                                if (entityData.Ignore) continue;
                                var targetVoxelData = entityData as TargetVoxelData;

                                customInfo.Append(string.Format(" -{0}/{1} ", targetVoxelData.Id, MyTexts.Get(MyStringId.GetOrCompute(targetVoxelData.MaterialDef.MinedOre))));
                                MyValueFormatter.AppendVolumeInBestUnit(targetVoxelData.Amount, customInfo);
                                customInfo.Append(Environment.NewLine);
                                cnt++;
                                if (cnt >= SyncBlockState.MaxSyncItems)
                                {
                                    customInfo.Append(Texts.Info_More + Environment.NewLine);
                                    break;
                                }
                            }
                        }
                        customInfo.Append(Environment.NewLine);
                    }
                    else if (Settings.WorkMode == WorkModes.Fill)
                    {
                        var materialDef = MyDefinitionManager.Static.GetVoxelMaterialDefinition((byte)Settings.FillMaterial);
                        if (State.MissingMaterial)
                        {
                            customInfo.Append(string.Format(Texts.Info_MissingMaterial + Environment.NewLine, (materialDef != null ? materialDef.MinedOre : "-")));
                        }
                        if (State.CharacterInWorkingArea)
                        {
                            customInfo.Append(Texts.Info_ObjectInWorkarea + Environment.NewLine);
                        }

                        customInfo.Append(Texts.Info_ItemsToFill + Environment.NewLine);
                        lock (State.PossibleFillTargets)
                        {
                            foreach (var entityData in State.PossibleFillTargets)
                            {
                                if (entityData.Ignore) continue;
                                var targetVoxelData = entityData as TargetVoxelData;
                                customInfo.Append(string.Format(" -{0}/{1} ", targetVoxelData.Id, MyTexts.Get(MyStringId.GetOrCompute(materialDef.MinedOre))));
                                MyValueFormatter.AppendVolumeInBestUnit(targetVoxelData.Amount, customInfo);
                                customInfo.Append(Environment.NewLine);
                                cnt++;
                                if (cnt >= SyncBlockState.MaxSyncItems)
                                {
                                    customInfo.Append(Texts.Info_More + Environment.NewLine);
                                    break;
                                }
                            }
                        }
                        customInfo.Append(Environment.NewLine);
                    }

                    cnt = 0;
                    customInfo.Append(Texts.Info_ItemsToCollect + Environment.NewLine);
                    lock (State.PossibleFloatingTargets)
                    {
                        foreach (var entityData in State.PossibleFloatingTargets)
                        {
                            customInfo.Append(string.Format(" -{0}" + Environment.NewLine, Logging.BlockName(entityData.Entity)));
                            cnt++;
                            if (cnt >= SyncBlockState.MaxSyncItems)
                            {
                                customInfo.Append(Texts.Info_More + Environment.NewLine);
                                break;
                            }
                        }
                    }
                }
                else
                {
                    if (!_Drill.Enabled) customInfo.Append(Texts.Info_BlockSwitchedOff + Environment.NewLine);
                    else if (!_Drill.IsFunctional) customInfo.Append(Texts.Info_BlockDamaged + Environment.NewLine);
                    else if (!_Drill.IsWorking) customInfo.Append(Texts.Info_BlockUnpowered + Environment.NewLine);
                }
            }
            catch
            {
                //Silent catch
            }
        }

        /// <summary>
        ///
        /// </summary>
        /// <returns></returns>
        private WorkingState GetWorkingState()
        {
            if (!State.Ready) return WorkingState.NotReady;
            else if (State.Drilling) return WorkingState.Drilling;
            else if (State.NeedDrilling)
            {
                if (State.InventoryFull) return WorkingState.InventoryFull;
                return WorkingState.NeedDrilling;
            }
            else if (State.Filling) return WorkingState.Filling;
            else if (State.NeedFilling)
            {
                if (State.CharacterInWorkingArea) return WorkingState.CharacterInWorkingArea;
                if (State.MissingMaterial) return WorkingState.MissingMaterial;
                return WorkingState.NeedFilling;
            }
            return WorkingState.Idle;
        }

        /// <summary>
        /// Set actual state and position of visual effects
        /// </summary>
        private void UpdateEffects()
        {
            var transportState = State.Transporting && State.CurrentTransportTarget != null;
            if (transportState != _TransportStateSet)
            {
                SetTransportEffects(transportState);
            }
            else
            {
                UpdateTransportEffectPosition();
            }

            //Drill/Fill state
            var workingState = GetWorkingState();
            if (workingState != _WorkingStateSet || Settings.SoundVolume != _SoundVolumeSet)
            {
                SetWorkingEffects(workingState);
                _WorkingStateSet = workingState;
                _SoundVolumeSet = Settings.SoundVolume;
            }
            else
            {
                UpdateWorkingEffectPosition(workingState);
            }
        }

        /// <summary>
        ///
        /// </summary>
        private void StopSoundEffects()
        {
            if (_SoundEmitter != null)
            {
                _SoundEmitter.StopSound(false);
            }

            if (_SoundEmitterWorking != null)
            {
                _SoundEmitterWorking.StopSound(false);
                _SoundEmitterWorking.SetPosition(null); //Reset
                _SoundEmitterWorkingPosition = null;
            }
        }

        /// <summary>
        /// Start visual effects for drill/fill
        /// </summary>
        private void SetWorkingEffects(WorkingState workingState)
        {
            if (_ParticleEffectWorking1 != null)
            {
                Interlocked.Decrement(ref _ActiveWorkingEffects);
                _ParticleEffectWorking1.Stop();
                _ParticleEffectWorking1 = null;
            }
            switch (workingState)
            {
                case WorkingState.Drilling:
                case WorkingState.Filling:
                    if ((_ActiveWorkingEffects < MaxWorkingEffects) &&
                        ((workingState == WorkingState.Drilling && ((NanobotDrillSystemMod.Settings.Drill.AllowedEffects & VisualAndSoundEffects.DrillingVisualEffect) != 0)) ||
                         (workingState == WorkingState.Filling && ((NanobotDrillSystemMod.Settings.Drill.AllowedEffects & VisualAndSoundEffects.FillingVisualEffect) != 0))
                        ))
                    {
                        Interlocked.Increment(ref _ActiveWorkingEffects);

                        MyParticlesManager.TryCreateParticleEffect(workingState == WorkingState.Drilling ? PARTICLE_EFFECT_DRILLING1 : PARTICLE_EFFECT_FILLING1, ref MatrixD.Identity, ref Vector3D.Zero, uint.MaxValue, out _ParticleEffectWorking1);
                        if (_ParticleEffectWorking1 != null) _ParticleEffectWorking1.UserRadiusMultiplier = workingState == WorkingState.Drilling ? 1f : 1f;
                    }
                    _Drill.SetEmissiveParts("Emissive", Color.Red, 1.0f);
                    _Drill.SetEmissiveParts("EmissiveReady", Color.Green, 1.0f);
                    _Drill.SetEmissiveParts("EmissiveWorking", workingState == WorkingState.Drilling ? Color.Orange : Color.DeepSkyBlue, 1.0f);
                    break;

                case WorkingState.MissingMaterial:
                case WorkingState.CharacterInWorkingArea:
                    _Drill.SetEmissiveParts("Emissive", Color.Red, 1.0f);
                    _Drill.SetEmissiveParts("EmissiveReady", Color.Red, 1.0f);
                    _Drill.SetEmissiveParts("EmissiveWorking", Color.DeepSkyBlue, 1.0f);
                    break;

                case WorkingState.InventoryFull:
                    _Drill.SetEmissiveParts("Emissive", Color.Red, 1.0f);
                    _Drill.SetEmissiveParts("EmissiveReady", Color.Red, 1.0f);
                    _Drill.SetEmissiveParts("EmissiveWorking", Color.Orange, 1.0f);
                    break;

                case WorkingState.NeedDrilling:
                    _Drill.SetEmissiveParts("Emissive", Color.Red, 1.0f);
                    _Drill.SetEmissiveParts("EmissiveReady", Color.Green, 1.0f);
                    _Drill.SetEmissiveParts("EmissiveWorking", Color.Orange, 1.0f);
                    break;

                case WorkingState.NeedFilling:
                    _Drill.SetEmissiveParts("Emissive", Color.Red, 1.0f);
                    _Drill.SetEmissiveParts("EmissiveReady", Color.Green, 1.0f);
                    _Drill.SetEmissiveParts("EmissiveWorking", Color.DeepSkyBlue, 1.0f);
                    break;

                case WorkingState.Idle:
                    _Drill.SetEmissiveParts("Emissive", Color.Red, 1.0f);
                    _Drill.SetEmissiveParts("EmissiveReady", Color.Green, 1.0f);
                    _Drill.SetEmissiveParts("EmissiveWorking", Color.Black, 1.0f);
                    break;

                case WorkingState.Invalid:
                case WorkingState.NotReady:
                    _Drill.SetEmissiveParts("Emissive", Color.White, 1.0f);
                    _Drill.SetEmissiveParts("EmissiveReady", Color.Black, 1.0f);
                    _Drill.SetEmissiveParts("EmissiveWorking", Color.Black, 1.0f);
                    break;
            }

            var sound = _Sounds[(int)workingState];
            if (sound != null)
            {
                if (_SoundEmitter == null)
                {
                    _SoundEmitter = new MyEntity3DSoundEmitter((VRage.Game.Entity.MyEntity)_Drill);
                    _SoundEmitter.CustomMaxDistance = 30f;
                    _SoundEmitter.CustomVolume = _SoundLevels[(int)workingState] * Settings.SoundVolume;
                }
                if (_SoundEmitterWorking == null)
                {
                    _SoundEmitterWorking = new MyEntity3DSoundEmitter((VRage.Game.Entity.MyEntity)_Drill);
                    _SoundEmitterWorking.CustomMaxDistance = 30f;
                    _SoundEmitterWorking.CustomVolume = _SoundLevels[(int)workingState] * Settings.SoundVolume;
                    _SoundEmitterWorkingPosition = null;
                }

                if (_SoundEmitter != null)
                {
                    _SoundEmitter.StopSound(true);
                    _SoundEmitter.CustomVolume = _SoundLevels[(int)workingState] * Settings.SoundVolume;
                    _SoundEmitter.PlaySound(sound, false, true);
                }

                if (_SoundEmitterWorking != null)
                {
                    _SoundEmitterWorking.StopSound(true);
                    _SoundEmitterWorking.CustomVolume = _SoundLevels[(int)workingState] * Settings.SoundVolume;
                    _SoundEmitterWorking.SetPosition(null); //Reset
                    _SoundEmitterWorkingPosition = null;
                    //_SoundEmitterWorking.PlaySound(sound, true); done after position is set
                }
            }
            else
            {
                if (_SoundEmitter != null)
                {
                    _SoundEmitter.StopSound(false);
                }

                if (_SoundEmitterWorking != null)
                {
                    _SoundEmitterWorking.StopSound(false);
                    _SoundEmitterWorking.SetPosition(null); //Reset
                    _SoundEmitterWorkingPosition = null;
                }
            }
            UpdateWorkingEffectPosition(workingState);
        }

        /// <summary>
        /// Set the position of the visual effects
        /// </summary>
        private void UpdateWorkingEffectPosition(WorkingState workingState)
        {
            if (_ParticleEffectWorking1 == null) return;

            Vector3D position;
            MatrixD matrix;
            if (State.CurrentDrillingEntity != null)
            {
                var drillPosition = ComputePosition(State.CurrentDrillingEntity);
                var start = Vector3D.Transform(_EmitterPosition, _Drill.WorldMatrix);
                Vector3 spawnDirection = Vector3.Normalize(drillPosition.Value - start);
                position = drillPosition.Value - spawnDirection;
                matrix = _Drill.WorldMatrix;
                matrix.Translation = position;
            }
            else if (State.CurrentFillingEntity != null)
            {
                var drillPosition = ComputePosition(State.CurrentFillingEntity);
                var start = Vector3D.Transform(_EmitterPosition, _Drill.WorldMatrix);
                Vector3 spawnDirection = Vector3.Normalize(drillPosition.Value - start);
                position = drillPosition.Value - spawnDirection;
                matrix = _Drill.WorldMatrix;
                matrix.Translation = position;
            }
            else
            {
                matrix = _Drill.WorldMatrix;
                position = matrix.Translation;
            }

            if (_ParticleEffectWorking1 != null)
            {
                _ParticleEffectWorking1.WorldMatrix = matrix;
            }
            if (_SoundEmitterWorking != null)
            {
                var sound = _Sounds[(int)workingState];
                if (sound == null)
                {
                    _SoundEmitterWorking.StopSound(false);
                    _SoundEmitterWorking.SetPosition(null); //Reset
                    _SoundEmitterWorkingPosition = null;
                }
                else if (_SoundEmitterWorkingPosition == null || Math.Abs((_SoundEmitterWorkingPosition.Value - position).Length()) > 2 || !_SoundEmitterWorking.IsPlaying || _SoundEmitterWorking.SoundPair != sound)
                {
                    _SoundEmitterWorking.StopSound(true);
                    _SoundEmitterWorking.SetPosition(position);
                    _SoundEmitterWorkingPosition = position;
                    _SoundEmitterWorking.PlaySound(sound, false, true);
                }
            }
        }

        /// <summary>
        /// Start visual effects for transport
        /// </summary>
        private void SetTransportEffects(bool active)
        {
            if ((NanobotDrillSystemMod.Settings.Drill.AllowedEffects & VisualAndSoundEffects.TransportVisualEffect) != 0)
            {
                if (active)
                {
                    if (_ParticleEffectTransport1 != null)
                    {
                        Interlocked.Decrement(ref _ActiveTransportEffects);
                        _ParticleEffectTransport1.Stop();
                        _ParticleEffectTransport1 = null;
                    }

                    if (_ActiveTransportEffects < MaxTransportEffects)
                    {
                        MyParticlesManager.TryCreateParticleEffect(State.CurrentTransportIsPick ? PARTICLE_EFFECT_TRANSPORT1_PICK : PARTICLE_EFFECT_TRANSPORT1_DELIVER, ref MatrixD.Identity, ref Vector3D.Zero, uint.MaxValue, out _ParticleEffectTransport1);
                        if (_ParticleEffectTransport1 != null)
                        {
                            Interlocked.Increment(ref _ActiveTransportEffects);
                            _ParticleEffectTransport1.UserScale = 0.1f;
                            UpdateTransportEffectPosition();
                        }
                    }
                }
                else
                {
                    if (_ParticleEffectTransport1 != null)
                    {
                        Interlocked.Decrement(ref _ActiveTransportEffects);
                        _ParticleEffectTransport1.Stop();
                        _ParticleEffectTransport1 = null;
                    }
                }
            }
            _TransportStateSet = active;
        }

        /// <summary>
        /// Set the position of the visual effects for transport
        /// </summary>
        private void UpdateTransportEffectPosition()
        {
            if (_ParticleEffectTransport1 == null) return;

            var playTime = MyAPIGateway.Session.ElapsedPlayTime;
            var elapsed = State.CurrentTransportTime.Ticks != 0 ? (double)playTime.Subtract(State.CurrentTransportStartTime).Ticks / State.CurrentTransportTime.Ticks : 0d;
            elapsed = elapsed < 1 ? elapsed : 1;
            elapsed = (elapsed > 0.5 ? 1 - elapsed : elapsed) * 2;

            MatrixD startMatrix;
            var target = State.CurrentTransportTarget;
            startMatrix = _Drill.WorldMatrix;
            startMatrix.Translation = Vector3D.Transform(_EmitterPosition, _Drill.WorldMatrix);

            var direction = target.Value - startMatrix.Translation;
            startMatrix.Translation += direction * elapsed;
            _ParticleEffectTransport1.WorldMatrix = startMatrix;
        }

        /// <summary>
        ///
        /// </summary>
        /// <param name="target"></param>
        /// <returns></returns>
        internal Vector3D? ComputePosition(object target)
        {
            if (target is IMySlimBlock)
            {
                Vector3D endPosition;
                ((IMySlimBlock)target).ComputeWorldCenter(out endPosition);
                return endPosition;
            }
            else if (target is IMyEntity) return ((IMyEntity)target).WorldMatrix.Translation;
            else if (target is TargetVoxelData) return ((TargetVoxelData)target).CurrentTargetPos;
            else if (target is Vector3D) return (Vector3D)target;
            return null;
        }

        /// <summary>
        /// Get a list of currently drill voxels (Scripting)
        /// </summary>
        /// <returns></returns>
        internal List<List<object>> GetPossibleDrillTargetsList()
        {
            var list = new List<List<object>>();
            lock (State.PossibleDrillTargets)
            {
                foreach (var entityData in State.PossibleDrillTargets)
                {
                    var targetVoxelData = entityData as TargetVoxelData;
                    list.Add(new List<object> {
                  entityData, targetVoxelData.Entity, targetVoxelData.Distance, targetVoxelData.MaterialDef, targetVoxelData.Amount
               });
                }
            }
            return list;
        }

        /// <summary>
        /// Get a list of currently filling voxels (Scripting)
        /// </summary>
        /// <returns></returns>
        internal List<List<object>> GetPossibleFillTargetsList()
        {
            var list = new List<List<object>>();
            lock (State.PossibleFillTargets)
            {
                foreach (var entityData in State.PossibleFillTargets)
                {
                    var targetVoxelData = entityData as TargetVoxelData;
                    list.Add(new List<object> {
                  entityData, targetVoxelData.Entity, targetVoxelData.Distance, targetVoxelData.MaterialDef, targetVoxelData.Amount
               });
                }
            }
            return list;
        }

        /// <summary>
        /// Get a list of currently collectable floating objects (Scripting)
        /// </summary>
        /// <returns></returns>
        internal List<VRage.Game.ModAPI.Ingame.IMyEntity> GetPossibleCollectingTargetsList()
        {
            var list = new List<VRage.Game.ModAPI.Ingame.IMyEntity>();
            lock (State.PossibleFloatingTargets)
            {
                foreach (var floatingData in State.PossibleFloatingTargets)
                {
                    if (!floatingData.Ignore) list.Add(floatingData.Entity);
                }
            }
            return list;
        }

        private Vector3 GetEffectiveOffset()
        {
            IMyCharacter character;
            Settings.RemoteControlCharacter(out character);
            if (character == null)
            {
                return _EmitterPosition + Settings.AreaOffset;
            }
            else
            {
                var position = Vector3D.Transform(character.GetPosition(), Drill.WorldMatrixInvScaled) + Settings.AreaOffset;
                return new Vector3(
                   position.X < -Settings.MaximumOffset ? -Settings.MaximumOffset : (position.X > Settings.MaximumOffset ? Settings.MaximumOffset : position.X),
                   position.Y < -Settings.MaximumOffset ? -Settings.MaximumOffset : (position.Y > Settings.MaximumOffset ? Settings.MaximumOffset : position.Y),
                   position.Z < -Settings.MaximumOffset ? -Settings.MaximumOffset : (position.Z > Settings.MaximumOffset ? Settings.MaximumOffset : position.Z)
                );
            }
        }

        private void CheckRemoteControlState()
        {
            var flags = Settings.Flags;
            IMyCharacter character;
            var remoteControled = Settings.RemoteControlCharacter(out character);
            if (!remoteControled)
            {
                //No remote control active
                Settings.Flags &= ~(SyncBlockSettings.Settings.RemoteControlled | SyncBlockSettings.Settings.RemoteShowArea | SyncBlockSettings.Settings.RemoteWorkdisabled);
            }
            else
            {
                if (character != null)
                {
                    var position = Vector3D.Transform(character.GetPosition(), Drill.WorldMatrixInvScaled) + Settings.AreaOffset;
                    var inRange = position.X >= -Settings.MaximumOffset && position.X <= Settings.MaximumOffset &&
                                position.Y >= -Settings.MaximumOffset && position.Y <= Settings.MaximumOffset &&
                                position.Z >= -Settings.MaximumOffset && position.Z <= Settings.MaximumOffset;

                    Settings.Flags |= SyncBlockSettings.Settings.RemoteControlled;
                    if (character.EquippedTool is IMyHandDrill)
                    {
                        if ((Settings.Flags & SyncBlockSettings.Settings.RemoteControlShowArea) != 0) Settings.Flags |= SyncBlockSettings.Settings.RemoteShowArea;
                        if ((Settings.Flags & SyncBlockSettings.Settings.RemoteControlWorkdisabled) != 0 && inRange) Settings.Flags &= ~SyncBlockSettings.Settings.RemoteWorkdisabled;
                    }
                    else
                    {
                        Settings.Flags &= ~SyncBlockSettings.Settings.RemoteShowArea;
                        if ((Settings.Flags & SyncBlockSettings.Settings.RemoteControlWorkdisabled) != 0) Settings.Flags |= SyncBlockSettings.Settings.RemoteWorkdisabled;
                    }
                    //Independend of setting/tool disable while not in range
                    if (!inRange) Settings.Flags |= SyncBlockSettings.Settings.RemoteWorkdisabled;
                    else if ((Settings.Flags & SyncBlockSettings.Settings.RemoteControlWorkdisabled) == 0) Settings.Flags &= ~SyncBlockSettings.Settings.RemoteWorkdisabled;
                }
                else
                {
                    //Character dead or not in game (disable drill)
                    Settings.Flags &= ~SyncBlockSettings.Settings.RemoteShowArea;
                    if ((Settings.Flags & SyncBlockSettings.Settings.RemoteControlWorkdisabled) != 0) Settings.Flags |= SyncBlockSettings.Settings.RemoteWorkdisabled;
                }
            }
            if (flags != Settings.Flags) UpdateCustomInfo(true);
        }
    }
}