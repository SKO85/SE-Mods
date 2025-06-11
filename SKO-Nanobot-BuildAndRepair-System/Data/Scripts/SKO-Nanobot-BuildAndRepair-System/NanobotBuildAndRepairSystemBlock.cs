namespace SKONanobotBuildAndRepairSystem
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Linq;
    using System.Text;
    using DefenseShields;
    using Sandbox.Common.ObjectBuilders;
    using Sandbox.Definitions;
    using Sandbox.Game.Entities;
    using Sandbox.Game.Lights;
    using Sandbox.ModAPI;
    using Sandbox.ModAPI.Ingame;
    using VRage;
    using VRage.Game;
    using VRage.Game.Components;
    using VRage.Game.ModAPI;
    using VRage.ModAPI;
    using VRage.ObjectBuilders;
    using VRage.Scripting.MemorySafeTypes;
    using VRage.Utils;
    using VRageMath;

    using IMyShipWelder = Sandbox.ModAPI.IMyShipWelder;
    using IMyTerminalBlock = Sandbox.ModAPI.IMyTerminalBlock;  
    using MyInventoryItem = VRage.Game.ModAPI.Ingame.MyInventoryItem;

    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_ShipWelder), false, "SELtdLargeNanobotBuildAndRepairSystem", "SELtdSmallNanobotBuildAndRepairSystem")]
    public class NanobotBuildAndRepairSystemBlock : MyGameLogicComponent
    {
        #region Fields and Properties

        public static ShieldApi Shield;

        public enum WorkingState
        {
            Invalid = 0, NotReady = 1, Idle = 2, Welding = 3, NeedWelding = 4, MissingComponents = 5, Grinding = 6, NeedGrinding = 7, InventoryFull = 8, LimitsExceeded = 9
        }

        public static readonly MyDefinitionId ElectricityId = new MyDefinitionId(typeof(VRage.Game.ObjectBuilders.Definitions.MyObjectBuilder_GasProperties), "Electricity");
        private static readonly MyStringId RangeGridResourceId = MyStringId.GetOrCompute("WelderGrid");
        private static readonly Random _RandomDelay = new Random();
        private TimeSpan LastTaskTime = new TimeSpan();

        private Action<IMyTerminalBlock> _onEnabledChanged;
        private Action<IMyCubeBlock> _onIsWorkingChanged;

        private readonly Stopwatch _DelayWatch = new Stopwatch();
        private int _Delay = 0;

        private bool _AsyncUpdateSourcesAndTargetsRunning = false;
        private readonly List<TargetBlockData> _TempPossibleWeldTargets = new List<TargetBlockData>();
        private readonly List<TargetBlockData> _TempPossibleGrindTargets = new List<TargetBlockData>();
        private readonly List<TargetEntityData> _TempPossibleFloatingTargets = new List<TargetEntityData>();
        private readonly List<IMyInventory> _TempPossibleSources = new List<IMyInventory>();
        private readonly HashSet<IMyInventory> _TempIgnore4Ingot = new HashSet<IMyInventory>();
        private readonly HashSet<IMyInventory> _TempIgnore4Items = new HashSet<IMyInventory>();
        private readonly HashSet<IMyInventory> _TempIgnore4Components = new HashSet<IMyInventory>();
        internal IMyShipWelder _Welder;
        internal IMyInventory _TransportInventory;
        private bool _IsInit;
        internal readonly List<IMyInventory> _PossibleSources = new List<IMyInventory>();
        internal readonly HashSet<IMyInventory> _Ignore4Ingot = new HashSet<IMyInventory>();
        internal readonly HashSet<IMyInventory> _Ignore4Items = new HashSet<IMyInventory>();
        internal readonly HashSet<IMyInventory> _Ignore4Components = new HashSet<IMyInventory>();
        private readonly Dictionary<string, int> _TempMissingComponents = new Dictionary<string, int>();
        private TimeSpan _LastFriendlyDamageCleanup;
        internal MyEntity3DSoundEmitter _SoundEmitter;
        internal MyEntity3DSoundEmitter _SoundEmitterWorking;
        internal Vector3D? _SoundEmitterWorkingPosition;
        internal MyParticleEffect _ParticleEffectWorking1;
        internal MyParticleEffect _ParticleEffectTransport1;
        internal bool _ParticleEffectTransport1Active;
        internal MyLight _LightEffect;
        internal MyFlareDefinition _LightEffectFlareWelding;
        internal MyFlareDefinition _LightEffectFlareGrinding;
        internal Vector3 _EmitterPosition;

        private TimeSpan _LastSourceUpdate = -NanobotBuildAndRepairSystemMod.Settings.SourcesUpdateInterval;
        private TimeSpan _LastTargetsUpdate;
        private TimeSpan _LastUpdate;

        internal bool _CreativeModeActive;
        private int _UpdateEffectsInterval;
        private bool _UpdateCustomInfoNeeded;
        private TimeSpan _UpdateCustomInfoLast;
        internal WorkingState _WorkingStateSet = WorkingState.Invalid;
        internal float _SoundVolumeSet;
        internal bool _TransportStateSet;
        private float _MaxTransportVolume;
        private int _ContinuouslyError;
        internal bool _PowerReady;
        internal bool _PowerWelding;
        internal bool _PowerGrinding;
        internal bool _PowerTransporting;
        private TimeSpan _UpdatePowerSinkLast;
        internal TimeSpan _TryAutoPushInventoryLast;
        internal TimeSpan _TryPushInventoryLast;

        private SyncBlockSettings _Settings;
        public SyncBlockSettings Settings
        {
            get
            {
                return _Settings ?? (_Settings = SyncBlockSettings.Load(this, NanobotBuildAndRepairSystemMod.ModGuid, BlockWeldPriority, BlockGrindPriority, ComponentCollectPriority));
            }
        }

        private readonly NanobotBuildAndRepairSystemBlockPriorityHandling _BlockWeldPriority = new NanobotBuildAndRepairSystemBlockPriorityHandling();

        internal NanobotBuildAndRepairSystemBlockPriorityHandling BlockWeldPriority
        {
            get
            {
                return _BlockWeldPriority;
            }
        }

        private readonly NanobotBuildAndRepairSystemBlockPriorityHandling _BlockGrindPriority = new NanobotBuildAndRepairSystemBlockPriorityHandling();

        internal NanobotBuildAndRepairSystemBlockPriorityHandling BlockGrindPriority
        {
            get
            {
                return _BlockGrindPriority;
            }
        }

        private readonly NanobotBuildAndRepairSystemComponentPriorityHandling _ComponentCollectPriority = new NanobotBuildAndRepairSystemComponentPriorityHandling();
        internal NanobotBuildAndRepairSystemComponentPriorityHandling ComponentCollectPriority
        {
            get
            {
                return _ComponentCollectPriority;
            }
        }

        public IMyShipWelder Welder { get { return _Welder; } }

        private readonly SyncBlockState _State = new SyncBlockState();
        public SyncBlockState State { get { return _State; } }

        private Dictionary<IMySlimBlock, TimeSpan> _FriendlyDamage;
        public Dictionary<IMySlimBlock, TimeSpan> FriendlyDamage
        {
            get
            {
                return _FriendlyDamage ?? (_FriendlyDamage = new Dictionary<IMySlimBlock, TimeSpan>());
            }
        }

        #endregion

        /// <summary>
        /// Initialize logical component
        /// </summary>
        /// <param name="objectBuilder"></param>
        public override void Init(MyObjectBuilder_EntityBase objectBuilder)
        {
            Logging.Instance?.Write(Logging.Level.Event, "BuildAndRepairSystemBlock {0}: Initializing", Logging.BlockName(Entity, Logging.BlockNameOptions.None));

            base.Init(objectBuilder);
            NeedsUpdate = MyEntityUpdateEnum.EACH_FRAME | MyEntityUpdateEnum.EACH_100TH_FRAME;

            if (Entity.GameLogic is MyCompositeGameLogicComponent)
            {
                Logging.Instance?.Write(Logging.Level.Event, "BuildAndRepairSystemBlock: Init Entiy.Logic remove other mods from this entity");
                Entity.GameLogic = this;
            }

            _Welder = Entity as IMyShipWelder;
            _Welder.AppendingCustomInfo += AppendingCustomInfo;

            if (Settings == null) //Force load of settings (is much faster here than initial load in UpdateBeforeSimulation10_100)
            {
                Logging.Instance?.Write(Logging.Level.Error, "BuildAndRepairSystemBlock {0}: Initializing Load-Settings failed", Logging.BlockName(_Welder, Logging.BlockNameOptions.None));
            };

            if (Shield == null)
            {
                Shield = new ShieldApi();
                Shield.Load();
            }

            Logging.Instance?.Write(Logging.Level.Event, "BuildAndRepairSystemBlock {0}: Initialized", Logging.BlockName(_Welder, Logging.BlockNameOptions.None));
        }

        /// <summary>
        ///
        /// </summary>
        public void SettingsChanged()
        {
            if (NanobotBuildAndRepairSystemMod.SettingsValid)
            {
                //Check limits as soon but not sooner as the 'server' settings has been received, otherwise we might use the wrong limits
                Settings.CheckLimits(this, false);
                if ((NanobotBuildAndRepairSystemMod.Settings.Welder.AllowedEffects & VisualAndSoundEffects.WeldingSoundEffect) == 0) EffectManager._Sounds[(int)WorkingState.Welding] = null;
                if ((NanobotBuildAndRepairSystemMod.Settings.Welder.AllowedEffects & VisualAndSoundEffects.GrindingSoundEffect) == 0) EffectManager._Sounds[(int)WorkingState.Grinding] = null;
            }

            var resourceSink = _Welder.Components.Get<Sandbox.Game.EntityComponents.MyResourceSinkComponent>();
            if (resourceSink != null)
            {
                var electricPowerTransport = Settings.MaximumRequiredElectricPowerTransport;
                if ((NanobotBuildAndRepairSystemMod.Settings.Welder.AllowedSearchModes & SearchModes.BoundingBox) == 0) electricPowerTransport /= 10;
                var maxPowerWorking = Math.Max(Settings.MaximumRequiredElectricPowerWelding, Settings.MaximumRequiredElectricPowerGrinding);
                resourceSink.SetMaxRequiredInputByType(ElectricityId, maxPowerWorking + electricPowerTransport + Settings.MaximumRequiredElectricPowerStandby);
                resourceSink.SetRequiredInputFuncByType(ElectricityId, ComputeRequiredElectricPower);
                resourceSink.Update();
            }

            var maxMultiplier = Math.Max(NanobotBuildAndRepairSystemMod.Settings.Welder.WeldingMultiplier, NanobotBuildAndRepairSystemMod.Settings.Welder.GrindingMultiplier);
            if (maxMultiplier > 10) NeedsUpdate = MyEntityUpdateEnum.EACH_FRAME | MyEntityUpdateEnum.EACH_10TH_FRAME;
            else NeedsUpdate = MyEntityUpdateEnum.EACH_FRAME | MyEntityUpdateEnum.EACH_100TH_FRAME;

            var multiplier = maxMultiplier > Constants.WELDER_TRANSPORTVOLUME_MAX_MULTIPLIER ? Constants.WELDER_TRANSPORTVOLUME_MAX_MULTIPLIER : maxMultiplier;
            _MaxTransportVolume = (float)_TransportInventory.MaxVolume * multiplier / Constants.WELDER_TRANSPORTVOLUME_DIVISOR;

            Logging.Instance?.Write(Logging.Level.Event, "BuildAndRepairSystemBlock {0}: Init Inventory Volume {1}/{2} MaxTransportVolume={3} Mode={4}", Logging.BlockName(_Welder, Logging.BlockNameOptions.None), (float)_Welder.GetInventory(0).MaxVolume, _TransportInventory.MaxVolume, _MaxTransportVolume, Settings.SearchMode);
        }

        /// <summary>
        ///
        /// </summary>
        private void Init()
        {
            if (_IsInit) return;
            if (_Welder.SlimBlock.IsProjected() || !_Welder.Synchronized) //Synchronized = !IsPreview
            {
                Logging.Instance?.Write(Logging.Level.Event, "BuildAndRepairSystemBlock {0}: Init Block is only projected/preview -> exit", Logging.BlockName(_Welder, Logging.BlockNameOptions.None));
                NeedsUpdate = MyEntityUpdateEnum.NONE;
                return;
            }

            lock (NanobotBuildAndRepairSystemMod.BuildAndRepairSystems)
            {
                if (!NanobotBuildAndRepairSystemMod.BuildAndRepairSystems.ContainsKey(Entity.EntityId))
                {
                    NanobotBuildAndRepairSystemMod.BuildAndRepairSystems.Add(Entity.EntityId, this);
                }
            }

            TerminalControlManager.InitControls();

            _onEnabledChanged = (block) =>
            {
                OnEnabledOrWorkingChanged();
                UpdateCustomInfo(true);
            };

            _onIsWorkingChanged = (block) =>
            {
                OnEnabledOrWorkingChanged();
                UpdateCustomInfo(true);
            };

            _Welder.EnabledChanged += _onEnabledChanged;
            _Welder.IsWorkingChanged += _onIsWorkingChanged;

            var welderInventory = _Welder.GetInventory(0);
            if (welderInventory == null) return;
            _TransportInventory = new Sandbox.Game.MyInventory((float)welderInventory.MaxVolume / MyAPIGateway.Session.BlocksInventorySizeMultiplier, Vector3.MaxValue, MyInventoryFlags.CanSend);
            //_Welder.Components.Add<Sandbox.Game.MyInventory>((Sandbox.Game.MyInventory)_TransportInventory); Won't work as the gui only could handle one inventory
            Logging.Instance?.Write(Logging.Level.Event, "BuildAndRepairSystemBlock {0}: Init Block TransportInventory Added to welder MaxVolume {1}", Logging.BlockName(_Welder, Logging.BlockNameOptions.None), (float)welderInventory.MaxVolume / MyAPIGateway.Session.BlocksInventorySizeMultiplier);
            SettingsChanged();

            var dummies = new Dictionary<string, IMyModelDummy>();
            _Welder.Model.GetDummies(dummies);
            foreach (var dummy in dummies)
            {
                if (dummy.Key.ToLower().Contains("detector_emitter"))
                {
                    _EmitterPosition = dummy.Value.Matrix.Translation;
                    break;
                }
            }


            MessageSyncHelper.SyncBlockDataRequestSend(this);

            UpdateCustomInfo(true);
            _TryPushInventoryLast = MyAPIGateway.Session.ElapsedPlayTime.Add(TimeSpan.FromSeconds(10));
            _TryAutoPushInventoryLast = _TryPushInventoryLast;
            _WorkingStateSet = WorkingState.Invalid;
            _SoundVolumeSet = -1;
            _LastUpdate = MyAPIGateway.Session.ElapsedPlayTime;

            _IsInit = true;
            Logging.Instance?.Write(Logging.Level.Event, "BuildAndRepairSystemBlock {0}: Init -> done", Logging.BlockName(_Welder, Logging.BlockNameOptions.None));
        }

        private float ComputeRequiredElectricPower()
        {
            return PowerManager.ComputeRequiredElectricPower(this);
        }

        /// <summary>
        ///
        /// </summary>
        public override void Close()
        {
            Logging.Instance.Write(Logging.Level.Event, "BuildAndRepairSystemBlock {0}: Close", Logging.BlockName(_Welder, Logging.BlockNameOptions.None));
            if (_IsInit)
            {
                InventoryManager.EmptyTransportInventory(this, true);
                Settings.Save(Entity, NanobotBuildAndRepairSystemMod.ModGuid);
                Logging.Instance.Write(Logging.Level.Event, "BuildAndRepairSystemBlock {0}: Close Saved Settings {1}", Logging.BlockName(_Welder, Logging.BlockNameOptions.None), Settings.GetAsXML());

                lock (NanobotBuildAndRepairSystemMod.BuildAndRepairSystems)
                {
                    NanobotBuildAndRepairSystemMod.BuildAndRepairSystems.Remove(Entity.EntityId);
                }

                //Stop effects
                State.CurrentTransportTarget = null;
                State.Ready = false;

                EffectManager.Unregister(this);

                GrindManager.ReleaseAll(Entity.EntityId);
                WeldManager.ReleaseAll(Entity.EntityId);

                if (_onEnabledChanged != null) _Welder.EnabledChanged -= _onEnabledChanged;
                if (_onIsWorkingChanged != null) _Welder.IsWorkingChanged -= _onIsWorkingChanged;

                lock (State.PossibleWeldTargets) State.PossibleWeldTargets?.Clear();
                lock (State.PossibleGrindTargets) State.PossibleGrindTargets?.Clear();
                lock (State.PossibleFloatingTargets) State.PossibleFloatingTargets?.Clear();

                State.MissingComponents?.Clear();
                FriendlyDamage?.Clear();

                _TempPossibleWeldTargets?.Clear();
                _TempPossibleGrindTargets?.Clear();
                _TempPossibleFloatingTargets?.Clear();
                _TempPossibleSources?.Clear();
                _TempIgnore4Ingot?.Clear();
                _TempIgnore4Items?.Clear();
                _TempIgnore4Components?.Clear();

                _TransportInventory?.Clear();

                _DelayWatch?.Stop();

                Settings.Save(Entity, NanobotBuildAndRepairSystemMod.ModGuid);
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

                if (_Welder == null || !_IsInit) return;

                if (!MyAPIGateway.Utilities.IsDedicated)
                {
                    if ((Settings.Flags & SyncBlockSettings.Settings.ShowArea) != 0)
                    {
                        var colorWelder = _Welder.SlimBlock.GetColorMask().HSVtoColor();
                        var color = Color.FromNonPremultiplied(colorWelder.R, colorWelder.G, colorWelder.B, 255);
                        var areaBoundingBox = Settings.CorrectedAreaBoundingBox;
                        var emitterMatrix = _Welder.WorldMatrix;
                        emitterMatrix.Translation = Vector3D.Transform(Settings.CorrectedAreaOffset, emitterMatrix);
                        MySimpleObjectDraw.DrawTransparentBox(ref emitterMatrix, ref areaBoundingBox, ref color, MySimpleObjectRasterizer.Solid, 1, 0.04f, RangeGridResourceId, null, false);
                    }

                    _UpdateEffectsInterval = ++_UpdateEffectsInterval % 2;
                    if (_UpdateEffectsInterval == 0) EffectManager.UpdateEffects(this);
                }
            }
            catch (Exception ex)
            {
                Logging.Instance?.Write(Logging.Level.Error, "BuildAndRepairSystemBlock {0}: UpdateBeforeSimulation Exception:{1}", Logging.BlockName(_Welder, Logging.BlockNameOptions.None), ex);
            }
        }

        public override void UpdateBeforeSimulation10()
        {
            base.UpdateBeforeSimulation10();
            UpdateBeforeSimulation10_100(true);
        }

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
            Logging.Instance?.Write(Logging.Level.Event, "BuildAndRepairSystemBlock {0}: UpdatingStopped", Logging.BlockName(_Welder, Logging.BlockNameOptions.None));
            if (_IsInit)
            {
                Settings.Save(Entity, NanobotBuildAndRepairSystemMod.ModGuid);
            }
            //Stop sound effects
            EffectManager.StopSoundEffects(this);
            _WorkingStateSet = WorkingState.Invalid;
            base.UpdatingStopped();
        }

        private void UpdateBeforeSimulation10_100(bool fast)
        {
            try
            {
                if (_Welder == null) return;
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
                    _CreativeModeActive = MyAPIGateway.Session.CreativeMode;

                    if (!fast)
                    {
                        CleanupFriendlyDamage();
                    }
                    
                    if(MyAPIGateway.Session.ElapsedPlayTime.Subtract(_LastUpdate).TotalSeconds >= Constants.UpdateIntervalSecondsDefault)
                    {
                        ServerTryWeldingGrindingCollecting();
                        _LastUpdate = MyAPIGateway.Session.ElapsedPlayTime;
                    }

                    if (!fast)
                    {
                        if ((State.Ready != _PowerReady || State.Welding != _PowerWelding || State.Grinding != _PowerGrinding || State.Transporting != _PowerTransporting) &&
                            MyAPIGateway.Session.ElapsedPlayTime.Subtract(_UpdatePowerSinkLast).TotalSeconds >= 6)
                        {
                            _UpdatePowerSinkLast = MyAPIGateway.Session.ElapsedPlayTime;
                            _PowerReady = State.Ready;
                            _PowerWelding = State.Welding;
                            _PowerGrinding = State.Grinding;
                            _PowerTransporting = State.Transporting;

                            var resourceSink = _Welder.Components.Get<Sandbox.Game.EntityComponents.MyResourceSinkComponent>();
                            resourceSink?.Update();
                        }

                        Settings.TrySave(Entity, NanobotBuildAndRepairSystemMod.ModGuid);
                        if (State.IsTransmitNeeded())
                        {
                            MessageSyncHelper.SyncBlockStateSend(0, this);
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
                    MessageSyncHelper.SyncBlockSettingsSend(0, this);
                }

                if (_UpdateCustomInfoNeeded) UpdateCustomInfo(true);

                _DelayWatch.Stop();
                if (_DelayWatch.ElapsedMilliseconds > 40)
                {
                    _Delay = _RandomDelay.Next(1, 20); //Slowdown a little bit
                    Logging.Instance?.Write(Logging.Level.Event, "BuildAndRepairSystemBlock {0}: Delay {1} ({2}ms)", Logging.BlockName(_Welder, Logging.BlockNameOptions.None), _Delay, _DelayWatch.ElapsedMilliseconds);
                }
            }
            catch (Exception ex)
            {
                Logging.Instance?.Write(Logging.Level.Error, "BuildAndRepairSystemBlock {0}: UpdateBeforeSimulation10/100 Exception:{1} {2}", Logging.BlockName(_Welder, Logging.BlockNameOptions.None), ex, ex.StackTrace);
                _LastUpdate = MyAPIGateway.Session.ElapsedPlayTime;
            }
        }

        private bool IsIdle()
        {
            var ready = _Welder.Enabled && _Welder.IsWorking && _Welder.IsFunctional;
            if (ready)
            {
                if (State.Welding || State.NeedWelding || State.Transporting || State.Grinding || State.NeedGrinding)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Try to weld/grind/collect the possible targets
        /// </summary>
        private void ServerTryWeldingGrindingCollecting()
        {
            var inventoryFull = State.InventoryFull;
            var limitsExceeded = State.LimitsExceeded;
            var welding = false;
            var needwelding = false;
            var grinding = false;
            var needgrinding = false;
            var collecting = false;
            var needcollecting = false;
            var transporting = false;
            var ready = _Welder.Enabled && _Welder.IsWorking && _Welder.IsFunctional;
            IMySlimBlock currentWeldingBlock = null;
            IMySlimBlock currentGrindingBlock = null;
            var playTime = MyAPIGateway.Session.ElapsedPlayTime;
            var idleCounterUpdated = false;

            if (ready)
            {
                // Check if idle long time.
                if (!IsIdle())
                {
                    LastTaskTime = playTime;
                }
                else
                {
                    if (_Welder.Enabled && Settings.UseAutoPowerOffWhenIdle == 1)
                    {
                        if(playTime.Subtract(LastTaskTime).TotalMinutes >= NanobotBuildAndRepairSystemMod.Settings.AutoPowerOffOnIdleMinutes)
                        {
                            _Welder.Enabled = false;
                            idleCounterUpdated = true;
                            LastTaskTime = playTime;
                        }
                        else
                        {
                            idleCounterUpdated = true;
                        }
                    }
                }

                Logging.Instance?.Write(Logging.Level.Info, "BuildAndRepairSystemBlock {0}: ServerTryWeldingGrindingCollecting Welder ready: Enabled={1}, IsWorking={2}, IsFunctional={3}", Logging.BlockName(_Welder, Logging.BlockNameOptions.None), _Welder.Enabled, _Welder.IsWorking, _Welder.IsFunctional);

                InventoryManager.TryPushInventory(this);
                transporting = IsTransportRunnning(playTime);

                if (transporting && State.CurrentTransportIsPick) needgrinding = true;
                if ((Settings.Flags & SyncBlockSettings.Settings.ComponentCollectIfIdle) == 0 && !transporting) CollectManager.TryCollectingFloatingTargets(this, out collecting, out needcollecting, out transporting);
                if (!transporting)
                {
                    Logging.Instance?.Write(Logging.Level.Info, "BuildAndRepairSystemBlock {0}: ServerTryWeldingGrindingCollecting TryWeldGrind: Mode {1}", Logging.BlockName(_Welder, Logging.BlockNameOptions.None), Settings.WorkMode);
                    State.MissingComponents.Clear();
                    State.LimitsExceeded = false;

                    switch (Settings.WorkMode)
                    {
                        case WorkModes.WeldBeforeGrind:
                            WeldManager.TryWelding(this, out welding, out needwelding, out transporting, out currentWeldingBlock);
                            if (State.PossibleWeldTargets.CurrentCount == 0 || ((Settings.Flags & SyncBlockSettings.Settings.ScriptControlled) != 0 && Settings.CurrentPickedGrindingBlock != null))
                            {
                                GrindManager.TryGrinding(this, out grinding, out needgrinding, out transporting, out currentGrindingBlock);
                            }
                            break;

                        case WorkModes.GrindBeforeWeld:
                            GrindManager.TryGrinding(this, out grinding, out needgrinding, out transporting, out currentGrindingBlock);
                            if (State.PossibleGrindTargets.CurrentCount == 0 || ((Settings.Flags & SyncBlockSettings.Settings.ScriptControlled) != 0 && Settings.CurrentPickedWeldingBlock != null))
                            {
                                WeldManager.TryWelding(this, out welding, out needwelding, out transporting, out currentWeldingBlock);
                            }
                            break;

                        case WorkModes.GrindIfWeldGetStuck:
                            WeldManager.TryWelding(this, out welding, out needwelding, out transporting, out currentWeldingBlock);
                            if (!(welding || transporting) || ((Settings.Flags & SyncBlockSettings.Settings.ScriptControlled) != 0 && Settings.CurrentPickedGrindingBlock != null))
                            {
                                GrindManager.TryGrinding(this, out grinding, out needgrinding, out transporting, out currentGrindingBlock);
                            }
                            break;

                        case WorkModes.WeldOnly:
                            WeldManager.TryWelding(this, out welding, out needwelding, out transporting, out currentWeldingBlock);
                            break;

                        case WorkModes.GrindOnly:
                            GrindManager.TryGrinding(this, out grinding, out needgrinding, out transporting, out currentGrindingBlock);
                            break;
                    }
                    State.MissingComponents.RebuildHash();
                }
                if ((Settings.Flags & SyncBlockSettings.Settings.ComponentCollectIfIdle) != 0 && !transporting && !welding && !grinding) CollectManager.TryCollectingFloatingTargets(this, out collecting, out needcollecting, out transporting);
            }
            else
            {
                transporting = IsTransportRunnning(playTime); //Finish running transport
                State.MissingComponents.Clear();
                State.MissingComponents.RebuildHash();
                Logging.Instance?.Write(Logging.Level.Info, "BuildAndRepairSystemBlock {0}: TryWelding Welder not ready: Enabled={1}, IsWorking={2}, IsFunctional={3}", Logging.BlockName(_Welder, Logging.BlockNameOptions.None), _Welder.Enabled || _CreativeModeActive, _Welder.IsWorking, _Welder.IsFunctional);
            }

            if (!(welding || grinding || collecting || transporting) && _TransportInventory.CurrentVolume > 0)
            {
                //Idle but not empty -> empty inventory
                if (State.LastTransportTarget.HasValue)
                {
                    State.CurrentTransportIsPick = true;
                    State.CurrentTransportTarget = State.LastTransportTarget;
                    State.CurrentTransportStartTime = playTime;
                    //State.TransportTime same (way back)
                    Logging.Instance?.Write(Logging.Level.Info, "BuildAndRepairSystemBlock {0}: Idle but not empty started transporttime={1} CurrentVolume={2}/{3}",
                       Logging.BlockName(_Welder, Logging.BlockNameOptions.None), State.CurrentTransportTime, _TransportInventory.CurrentVolume, _MaxTransportVolume);
                    transporting = true;
                }
                InventoryManager.EmptyTransportInventory(this, true);
            }

            if ((State.Welding && !welding) || (State.Grinding && !(grinding || collecting)))
            {
                StartAsyncUpdateSourcesAndTargets(false); //Scan immediately once for new targets
            }

            var readyChanged = State.Ready != ready;
            State.Ready = ready;
            State.Welding = welding;
            State.NeedWelding = needwelding;
            State.CurrentWeldingBlock = currentWeldingBlock;

            State.Grinding = grinding;
            State.NeedGrinding = needgrinding;
            State.CurrentGrindingBlock = currentGrindingBlock;

            State.Transporting = transporting;

            var inventoryFullChanged = State.InventoryFull != inventoryFull;
            var limitsExceededChanged = State.LimitsExceeded != limitsExceeded;

            var missingComponentsChanged = State.MissingComponents.LastHash != State.MissingComponents.CurrentHash;
            State.MissingComponents.LastHash = State.MissingComponents.CurrentHash;

            var possibleWeldTargetsChanged = State.PossibleWeldTargets.LastHash != State.PossibleWeldTargets.CurrentHash;
            State.PossibleWeldTargets.LastHash = State.PossibleWeldTargets.CurrentHash;

            var possibleGrindTargetsChanged = State.PossibleGrindTargets.LastHash != State.PossibleGrindTargets.CurrentHash;
            State.PossibleGrindTargets.LastHash = State.PossibleGrindTargets.CurrentHash;

            var possibleFloatingTargetsChanged = State.PossibleFloatingTargets.LastHash != State.PossibleFloatingTargets.CurrentHash;
            State.PossibleFloatingTargets.LastHash = State.PossibleFloatingTargets.CurrentHash;

            if (idleCounterUpdated || missingComponentsChanged || possibleWeldTargetsChanged || possibleGrindTargetsChanged || possibleFloatingTargetsChanged) State.HasChanged();

            if (missingComponentsChanged && Logging.Instance.ShouldLog(Logging.Level.Verbose))
            {
                lock (Logging.Instance)
                {
                    Logging.Instance?.Write(Logging.Level.Verbose, "BuildAndRepairSystemBlock {0}: TryWelding: MissingComponents --->", Logging.BlockName(_Welder, Logging.BlockNameOptions.None));
                    Logging.Instance?.IncreaseIndent(Logging.Level.Verbose);
                    foreach (var missing in State.MissingComponents)
                    {
                        Logging.Instance?.Write(Logging.Level.Verbose, "{0}:{1}", missing.Key.SubtypeName, missing.Value);
                    }
                    Logging.Instance?.DecreaseIndent(Logging.Level.Verbose);
                    Logging.Instance?.Write(Logging.Level.Verbose, "<--- MissingComponents");
                }
            }

            UpdateCustomInfo(idleCounterUpdated || missingComponentsChanged || possibleWeldTargetsChanged || possibleGrindTargetsChanged || possibleFloatingTargetsChanged || readyChanged || inventoryFullChanged || limitsExceededChanged);
        }

        public bool IsWelderShielded()
        {
            try
            {
                if (Welder != null && Shield != null && Shield.IsReady)
                {
                    if (Shield.ProtectedByShield(Welder.CubeGrid))
                    {
                        return Shield.IsBlockProtected(Welder.SlimBlock);
                    }
                }
            }
            catch
            {
            }

            return false;
        }

        /// <summary>
        ///
        /// </summary>
        /// <param name="playTime"></param>
        /// <returns></returns>
        private bool IsTransportRunnning(TimeSpan playTime)
        {
            if (State.CurrentTransportStartTime > TimeSpan.Zero)
            {
                //Transport started
                if (State.CurrentTransportIsPick)
                {
                    if (!InventoryManager.EmptyTransportInventory(this, true))
                    {
                        Logging.Instance?.Write(Logging.Level.Info, "BuildAndRepairSystemBlock {0}: IsTransportRunnning transport still running transport inventory not emtpy",
                           Logging.BlockName(_Welder, Logging.BlockNameOptions.None));
                        return true;
                    }
                }

                if (playTime.Subtract(State.CurrentTransportStartTime) < State.CurrentTransportTime)
                {
                    //Last transport still running -> wait
                    Logging.Instance?.Write(Logging.Level.Info, "BuildAndRepairSystemBlock {0}: IsTransportRunnning: transport still running remaining transporttime={1}",
                       Logging.BlockName(_Welder, Logging.BlockNameOptions.None), State.CurrentTransportTime.Subtract(MyAPIGateway.Session.ElapsedPlayTime.Subtract(State.CurrentTransportStartTime)));
                    return true;
                }
                State.CurrentTransportStartTime = TimeSpan.Zero;
                State.LastTransportTarget = State.CurrentTransportTarget;
                State.CurrentTransportTarget = null;
            }
            else State.CurrentTransportTarget = null;
            return false;
        }

        /// <summary>
        ///
        /// </summary>
        private void UpdateCustomInfo(bool changed)
        {
            _UpdateCustomInfoNeeded |= changed;
            var playTime = MyAPIGateway.Session.ElapsedPlayTime;

            if (_UpdateCustomInfoNeeded && playTime.Subtract(_UpdateCustomInfoLast).TotalSeconds >= 2)
            {
                _Welder.RefreshCustomInfo();
                TriggerTerminalRefresh();
                _UpdateCustomInfoLast = playTime;
                _UpdateCustomInfoNeeded = false;
            }
        }

        private void OnEnabledOrWorkingChanged()
        {
            try
            {
                GrindManager.ReleaseAll(Entity.EntityId);
                WeldManager.ReleaseAll(Entity.EntityId);
                InventoryManager.EmptyInventory(this);
                EffectManager.UpdateEffects(this);
            }
            catch { }

            LastTaskTime = MyAPIGateway.Session.ElapsedPlayTime;
        }

        /// <summary>
        ///
        /// </summary>
        public void TriggerTerminalRefresh()
        {
            //Workaround as long as RaisePropertiesChanged is not public
            if (_Welder != null)
            {
                var action = _Welder.GetActionWithName("helpOthers");
                if (action != null)
                {
                    action.Apply(_Welder);
                    action.Apply(_Welder);
                }
            }
        }

        /// <summary>
        ///
        /// </summary>
        public bool ServerDoWeld(TargetBlockData targetData)
        {
            var welderInventory = _Welder.GetInventory(0);
            var welding = false;
            var created = false;
            var target = targetData.Block;
            var hasIgnoreColor = (Settings.Flags & SyncBlockSettings.Settings.UseIgnoreColor) != 0 && IsColorNearlyEquals(Settings.IgnoreColorPacked, target.GetColorMask());

            if ((targetData.Attributes & TargetBlockData.AttributeFlags.Projected) != 0)
            {
                //New Block (Projected)
                var cubeGridProjected = target.CubeGrid as MyCubeGrid;
                var blockDefinition = target.BlockDefinition as MyCubeBlockDefinition;
                var item = _TransportInventory.FindItem(blockDefinition.Components[0].Definition.Id);

                if ((_CreativeModeActive || (item != null && item.Amount >= 1)) && cubeGridProjected?.Projector != null)
                {
                    if (_Welder.IsWithinWorldLimits(cubeGridProjected.Projector, blockDefinition.BlockPairName, blockDefinition.PCU))
                    {
                        var blockBuildIntegrity = target.BuildIntegrity;

                        if (!cubeGridProjected.Projector.Closed && !cubeGridProjected.Projector.CubeGrid.Closed && (target.FatBlock == null || !target.FatBlock.Closed || !target.FatBlock.IsVisible()))
                        {
                            ((Sandbox.ModAPI.IMyProjector)cubeGridProjected.Projector).Build(target, _Welder.OwnerId, _Welder.EntityId, true, _Welder.SlimBlock.BuiltBy);
                        }

                        //After creation we can't welding this projected block, we have to find the 'physical' block instead.
                        var cubeGrid = cubeGridProjected.Projector.CubeGrid;
                        var blockPos = cubeGrid.WorldToGridInteger(cubeGridProjected.GridIntegerToWorld(target.Position));
                        target = cubeGrid.GetCubeBlock(blockPos);

                        if (target != null) targetData.Block = target;
                        targetData.Attributes &= ~TargetBlockData.AttributeFlags.Projected;
                        created = true;

                        if (!_CreativeModeActive)
                        {
                            var newIntegrity = target?.BuildIntegrity;
                            if (newIntegrity > blockBuildIntegrity)
                            {
                                _TransportInventory.RemoveItems(item.ItemId, 1);
                            }
                        }

                        Logging.Instance?.Write(Logging.Level.Info, "BuildAndRepairSystemBlock {0}: ServerDoWeld (new): {1}", Logging.BlockName(_Welder, Logging.BlockNameOptions.None), Logging.BlockName(target));
                    }
                    else
                    {
                        State.LimitsExceeded = true;
                        targetData.Ignore = true;
                    }
                }
            }

            if (!hasIgnoreColor && target != null && (targetData.Attributes & TargetBlockData.AttributeFlags.Projected) == 0)
            {
                //No ignore color and allready created
                if (!target.IsFullIntegrity || created)
                {
                    //Move collected/needed items to stockpile.
                    target.MoveItemsToConstructionStockpile(_TransportInventory);

                    //Incomplete
                    welding = target.CanContinueBuild(_TransportInventory) || _CreativeModeActive;

                    if (welding)
                    {
                        Logging.Instance?.Write(Logging.Level.Info, "BuildAndRepairSystemBlock {0}: ServerDoWeld (incomplete): {1}", Logging.BlockName(_Welder, Logging.BlockNameOptions.None), Logging.BlockName(target));

                        target.IncreaseMountLevel(MyAPIGateway.Session.WelderSpeedMultiplier * NanobotBuildAndRepairSystemMod.Settings.Welder.WeldingMultiplier * Constants.WELDER_AMOUNT_PER_SECOND, _Welder.OwnerId, welderInventory, MyAPIGateway.Session.WelderSpeedMultiplier * NanobotBuildAndRepairSystemMod.Settings.Welder.WeldingMultiplier * Constants.WELDER_MAX_REPAIR_BONE_MOVEMENT_SPEED, _Welder.HelpOthers);
                    }

                    if (IsWeldIntegrityReached(target))
                    {
                        targetData.Ignore = true;
                    }
                }
                else
                {
                    //Deformation
                    Logging.Instance?.Write(Logging.Level.Info, "BuildAndRepairSystemBlock {0}: ServerDoWeld (deformed): {1}", Logging.BlockName(_Welder, Logging.BlockNameOptions.None), Logging.BlockName(target));
                    welding = true;
                    target.IncreaseMountLevel(MyAPIGateway.Session.WelderSpeedMultiplier * NanobotBuildAndRepairSystemMod.Settings.Welder.WeldingMultiplier * Constants.WELDER_AMOUNT_PER_SECOND, _Welder.OwnerId, welderInventory, MyAPIGateway.Session.WelderSpeedMultiplier * NanobotBuildAndRepairSystemMod.Settings.Welder.WeldingMultiplier * Constants.WELDER_MAX_REPAIR_BONE_MOVEMENT_SPEED, _Welder.HelpOthers);
                }
            }

            return welding || created;
        }

        internal bool IsWeldIntegrityReached(IMySlimBlock target)
        {
            try
            {
                var def = ((MyCubeBlockDefinition)target.BlockDefinition);

                if (Settings.WeldTo == WeldTo.WeldToFull)
                {
                    return target.IsFullIntegrity;
                }
                else if (Settings.WeldTo == WeldTo.WeldToFunctionalOnly)
                {
                    var addIntegrity = target.MaxIntegrity * 0.03f;
                    return target.Integrity >= (target.MaxIntegrity + addIntegrity) * def.CriticalIntegrityRatio;
                }
                else if (Settings.WeldTo == WeldTo.Skeleton)
                {
                    return target.Integrity >= target.MaxIntegrity * Constants.MaxCreateIntegrityRatio;
                }
            }
            catch
            {
                // If something goes wrong, lets say its all built to avoid issues!
                return true;
            }

            return false;
        }

        internal UtilsInventory.IntegrityLevel GetIntegrityLevel()
        {
            if (Settings.WeldTo == WeldTo.WeldToFunctionalOnly)
            {
                return UtilsInventory.IntegrityLevel.Functional;
            }
            else if (Settings.WeldTo == WeldTo.Skeleton)
            {
                return UtilsInventory.IntegrityLevel.Skeleton;
            }

            return UtilsInventory.IntegrityLevel.Complete;
        }

        /// <summary>
        ///
        /// </summary>
        public bool ServerDoGrind(TargetBlockData targetData, out bool transporting)
        {
            var target = targetData.Block;
            var playTime = MyAPIGateway.Session.ElapsedPlayTime;
            transporting = IsTransportRunnning(playTime);
            if (transporting) return false;

            var welderInventory = _Welder.GetInventory(0);
            var targetGrid = target.CubeGrid;

            if (targetGrid.Physics == null || !targetGrid.Physics.Enabled) return false;

            var criticalIntegrityRatio = ((MyCubeBlockDefinition)target.BlockDefinition).CriticalIntegrityRatio;
            var ownershipIntegrityRatio = ((MyCubeBlockDefinition)target.BlockDefinition).OwnershipIntegrityRatio > 0 ? ((MyCubeBlockDefinition)target.BlockDefinition).OwnershipIntegrityRatio : criticalIntegrityRatio;
            var integrityRatio = target.Integrity / target.MaxIntegrity;

            if ((targetData.Attributes & TargetBlockData.AttributeFlags.Autogrind) != 0)
            {
                if ((Settings.GrindJanitorOptions & AutoGrindOptions.DisableOnly) != 0 && target.FatBlock != null && integrityRatio < criticalIntegrityRatio)
                {
                    //Block allready out of order -> stop grinding and switch to next
                    return false;
                }
                if ((Settings.GrindJanitorOptions & AutoGrindOptions.HackOnly) != 0 && target.FatBlock != null && integrityRatio < ownershipIntegrityRatio)
                {
                    //Block allready hacked -> stop grinding and switch to next
                    return false;
                }
            }

            var disassembleRatio = target.FatBlock?.DisassembleRatio ?? ((MyCubeBlockDefinition)target.BlockDefinition).DisassembleRatio;
            var integrityPointsPerSec = ((MyCubeBlockDefinition)target.BlockDefinition).IntegrityPointsPerSec;

            var damage = MyAPIGateway.Session.GrinderSpeedMultiplier * NanobotBuildAndRepairSystemMod.Settings.Welder.GrindingMultiplier * Constants.GRINDER_AMOUNT_PER_SECOND;
            var grinderAmount = damage * integrityPointsPerSec / disassembleRatio;
            integrityRatio = (target.Integrity - grinderAmount) / target.MaxIntegrity;

            if ((targetData.Attributes & TargetBlockData.AttributeFlags.Autogrind) != 0)
            {
                if ((Settings.GrindJanitorOptions & AutoGrindOptions.DisableOnly) != 0 && integrityRatio < criticalIntegrityRatio)
                {
                    //Grind only down to critical ratio not further
                    grinderAmount = target.Integrity - 0.9f * criticalIntegrityRatio * target.MaxIntegrity;
                    damage = grinderAmount * disassembleRatio / integrityPointsPerSec;
                    integrityRatio = criticalIntegrityRatio;
                }
                else if ((Settings.GrindJanitorOptions & AutoGrindOptions.HackOnly) != 0 && integrityRatio < ownershipIntegrityRatio)
                {
                    //Grind only down to ownership ratio not further
                    grinderAmount = target.Integrity - 0.9f * ownershipIntegrityRatio * target.MaxIntegrity;
                    damage = grinderAmount * disassembleRatio / integrityPointsPerSec;
                    integrityRatio = ownershipIntegrityRatio;
                }
            }

            var emptying = false;
            var isEmpty = false;
            Logging.Instance?.Write(Logging.Level.Verbose, "BuildAndRepairSystemBlock {0}: ServerDoGrind {1} integrityRatio={2}", Logging.BlockName(_Welder, Logging.BlockNameOptions.None), Logging.BlockName(target), integrityRatio);
            if (integrityRatio <= 0.2)
            {
                //Try to emtpy inventory (if any)
                if (target.FatBlock != null && target.FatBlock.HasInventory)
                {
                    emptying = EmptyBlockInventories(target.FatBlock, _TransportInventory, out isEmpty);
                    Logging.Instance?.Write(Logging.Level.Verbose, "BuildAndRepairSystemBlock {0}: ServerDoGrind {1} Try empty Inventory running={2}", Logging.BlockName(_Welder, Logging.BlockNameOptions.None), Logging.BlockName(target), emptying);
                }
            }

            if (!emptying || isEmpty)
            {
                var damageInfo = new MyDamageInformation(false, damage, MyDamageType.Grind, _Welder.EntityId);

                if (target.UseDamageSystem)
                {
                    //Not available in modding
                    //MyAPIGateway.Session.DamageSystem.RaiseBeforeDamageApplied(target, ref damageInfo);

                    foreach (var entry in NanobotBuildAndRepairSystemMod.BuildAndRepairSystems)
                    {
                        var relation = entry.Value.Welder.GetUserRelationToOwner(_Welder.OwnerId);
                        if (MyRelationsBetweenPlayerAndBlockExtensions.IsFriendly(relation))
                        {
                            //A 'friendly' damage from grinder -> do not repair (for a while)
                            //I don't check block relation here, because if it is enemy we won't repair it in any case and it just times out
                            entry.Value.FriendlyDamage[target] = MyAPIGateway.Session.ElapsedPlayTime + NanobotBuildAndRepairSystemMod.Settings.FriendlyDamageTimeout;
                            Logging.Instance?.Write(Logging.Level.Info, "BuildAndRepairSystemBlock: Damaged Add FriendlyDamage {0} Timeout {1}", Logging.BlockName(target), entry.Value.FriendlyDamage[target]);
                        }
                    }
                }

                target.DecreaseMountLevel(damageInfo.Amount, _TransportInventory);
                target.MoveItemsFromConstructionStockpile(_TransportInventory);

                if (target.UseDamageSystem)
                {
                    //Not available in modding
                    //MyAPIGateway.Session.DamageSystem.RaiseAfterDamageApplied(target, ref damageInfo);
                }
                if (target.IsFullyDismounted)
                {
                    Logging.Instance?.Write(Logging.Level.Info, "BuildAndRepairSystemBlock {0}: ServerDoGrind {1} FullyDismounted", Logging.BlockName(_Welder, Logging.BlockNameOptions.None), Logging.BlockName(target));
                    if (target.UseDamageSystem)
                    {
                        //Not available in modding
                        //MyAPIGateway.Session.DamageSystem.RaiseDestroyed(target, damageInfo);
                    }

                    target.SpawnConstructionStockpile();
                    target.CubeGrid.RazeBlock(target.Position);
                }
            }

            if ((float)_TransportInventory.CurrentVolume >= _MaxTransportVolume || target.IsFullyDismounted)
            {
                //Transport started
                State.CurrentTransportIsPick = true;
                State.CurrentTransportTarget = ComputePosition(target);
                State.CurrentTransportStartTime = playTime;
                State.CurrentTransportTime = TimeSpan.FromSeconds(2d * targetData.Distance / Settings.TransportSpeed);
                Logging.Instance?.Write(Logging.Level.Info, "BuildAndRepairSystemBlock {0}: ServerDoGrind: Target {1} transport started transporttime={2} CurrentVolume={3}/{4}",
                   Logging.BlockName(_Welder, Logging.BlockNameOptions.None), Logging.BlockName(targetData.Block), State.CurrentTransportTime, _TransportInventory.CurrentVolume, _MaxTransportVolume);
                InventoryManager.EmptyTransportInventory(this, true);
                transporting = true;
            }

            return true;
        }

        /// <summary>
        ///
        /// </summary>
        public bool ServerDoCollectFloating(TargetEntityData targetData, out bool transporting, ref TargetEntityData collectingFirstTarget)
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
                var target = targetData.Entity;
                var floating = target as MyFloatingObject;
                var floatingFirstTarget = collectingFirstTarget?.Entity as MyFloatingObject;

                canAdd = collectingFirstTarget == null || (floatingFirstTarget != null && floating != null);
                if (canAdd)
                {
                    if (floating != null) collecting = EmptyFloatingObject(floating, _TransportInventory, out isEmpty);
                    else
                    {
                        collecting = EmptyBlockInventories(target, _TransportInventory, out isEmpty);
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

                    Logging.Instance?.Write(Logging.Level.Verbose, "BuildAndRepairSystemBlock {0}: ServerDoCollectFloating {1} Try pick floating running={2}", Logging.BlockName(_Welder, Logging.BlockNameOptions.None), Logging.BlockName(target), collecting);

                    targetData.Ignore = isEmpty;
                }
            }
            if (collectingFirstTarget != null && ((float)_TransportInventory.CurrentVolume >= _MaxTransportVolume || (!canAdd && _TransportInventory.CurrentVolume > 0)))
            {
                //Transport started
                State.CurrentTransportIsPick = true;
                State.CurrentTransportTarget = ComputePosition(collectingFirstTarget.Entity);
                State.CurrentTransportStartTime = playTime;
                State.CurrentTransportTime = TimeSpan.FromSeconds(2d * collectingFirstTarget.Distance / Settings.TransportSpeed);
                Logging.Instance?.Write(Logging.Level.Info, "BuildAndRepairSystemBlock {0}: ServerDoCollectFloating: Target {1} transport started transporttime={2} CurrentVolume={3}/{4}",
                   Logging.BlockName(_Welder, Logging.BlockNameOptions.None), Logging.BlockName(collectingFirstTarget.Entity), State.CurrentTransportTime, _TransportInventory.CurrentVolume, _MaxTransportVolume);
                InventoryManager.EmptyTransportInventory(this, true);
                transporting = true;
                collectingFirstTarget = null;
            }

            return collecting;
        }

        /// <summary>
        /// Try to find an the missing components and moves them into welder inventory
        /// </summary>
        public bool ServerFindMissingComponents(TargetBlockData targetData)
        {
            try
            {
                var playTime = MyAPIGateway.Session.ElapsedPlayTime;
                if (IsTransportRunnning(playTime)) return true;

                var remainingVolume = _MaxTransportVolume;
                _TempMissingComponents.Clear();
                var picked = false; ;
                var cubeGrid = targetData.Block.CubeGrid as MyCubeGrid;
                if ((targetData.Attributes & TargetBlockData.AttributeFlags.Projected) != 0)
                {
                    targetData.Block.GetMissingComponents(_TempMissingComponents, UtilsInventory.IntegrityLevel.Create);
                    if (_TempMissingComponents.Count > 0)
                    {
                        picked = ServerFindMissingComponents(targetData, ref remainingVolume);
                        if (picked)
                        {
                            if ((Settings.Flags & SyncBlockSettings.Settings.UseIgnoreColor) == 0 || !IsColorNearlyEquals(Settings.IgnoreColorPacked, targetData.Block.GetColorMask()))
                            {
                                //Block could be created and should be welded -> so retrieve the remaining material also
                                var keyValue = _TempMissingComponents.ElementAt(0);
                                _TempMissingComponents.Clear();

                                targetData.Block.GetMissingComponents(_TempMissingComponents, GetIntegrityLevel());

                                if (_TempMissingComponents.ContainsKey(keyValue.Key))
                                {
                                    if (_TempMissingComponents[keyValue.Key] <= keyValue.Value) _TempMissingComponents.Remove(keyValue.Key);
                                    else _TempMissingComponents[keyValue.Key] -= keyValue.Value;
                                }
                            }
                        }
                    }
                }
                else
                {
                    targetData.Block.GetMissingComponents(_TempMissingComponents, GetIntegrityLevel());
                }

                if (_TempMissingComponents.Count > 0)
                {
                    ServerFindMissingComponents(targetData, ref remainingVolume);
                }

                if (remainingVolume < _MaxTransportVolume || (_CreativeModeActive && _TempMissingComponents.Count > 0))
                {
                    //Transport startet
                    State.CurrentTransportIsPick = false;
                    State.CurrentTransportTarget = ComputePosition(targetData.Block);
                    State.CurrentTransportStartTime = playTime;
                    State.CurrentTransportTime = TimeSpan.FromSeconds(2d * targetData.Distance / Settings.TransportSpeed);
                    Logging.Instance?.Write(Logging.Level.Info, "BuildAndRepairSystemBlock {0}: FindMissingComponents: Target {1} transport started volume={2} (max {3}) transporttime={4}",
                       Logging.BlockName(_Welder, Logging.BlockNameOptions.None), Logging.BlockName(targetData.Block), _MaxTransportVolume - remainingVolume, _MaxTransportVolume, State.CurrentTransportTime);
                    return true;
                }
                return false;
            }
            finally
            {
                _TempMissingComponents.Clear();
            }
        }

        /// <summary>
        ///
        /// </summary>
        /// <param name="targetData"></param>
        /// <returns></returns>
        private bool ServerFindMissingComponents(TargetBlockData targetData, ref float remainingVolume)
        {
            var picked = false;
            foreach (var keyValue in _TempMissingComponents)
            {
                Logging.Instance?.Write(Logging.Level.Info, "BuildAndRepairSystemBlock {0}: FindMissingComponents: Target {1} missing {2}={3} remainingVolume={4}", Logging.BlockName(_Welder, Logging.BlockNameOptions.None), Logging.BlockName(targetData.Block), keyValue.Key, keyValue.Value, remainingVolume);
                var neededAmount = 0;

                var componentId = new MyDefinitionId(typeof(MyObjectBuilder_Component), keyValue.Key);
                int allreadyMissingAmount;
                if (!State.MissingComponents.TryGetValue(componentId, out allreadyMissingAmount))
                {
                    var definition = MyDefinitionManager.Static.GetPhysicalItemDefinition(componentId);
                    neededAmount = keyValue.Value;
                    picked = ServerPickFromWelder(componentId, definition.Volume, ref neededAmount, ref remainingVolume) || picked;
                    if (neededAmount > 0 && remainingVolume > 0) picked = PullComponents(componentId, definition.Volume, ref neededAmount, ref remainingVolume) || picked;
                }
                else
                {
                    neededAmount = keyValue.Value;
                }

                if (neededAmount > 0 && remainingVolume > 0) AddToMissingComponents(componentId, neededAmount);
                if (remainingVolume <= 0) break;
            }
            return picked;
        }

        /// <summary>
        /// Try to pick needed material from own inventory, if successfull material is moved into transport inventory
        /// </summary>
        private bool ServerPickFromWelder(MyDefinitionId componentId, float volume, ref int neededAmount, ref float remainingVolume)
        {
            Logging.Instance?.Write(Logging.Level.Verbose, "BuildAndRepairSystemBlock {0}: ServerPickFromWelder Try: {1}={2}", Logging.BlockName(_Welder, Logging.BlockNameOptions.None), componentId, neededAmount);

            var picked = false;

            var welderInventory = _Welder.GetInventory(0);
            if (welderInventory == null || welderInventory.Empty())
            {
                Logging.Instance?.Write(Logging.Level.Verbose, "BuildAndRepairSystemBlock {0}: ServerPickFromWelder welder empty: {1}={2}", Logging.BlockName(_Welder, Logging.BlockNameOptions.None), componentId, neededAmount);
                return picked;
            }

            var tempInventoryItems = new List<MyInventoryItem>();
            welderInventory.GetItems(tempInventoryItems);
            for (var i1 = tempInventoryItems.Count - 1; i1 >= 0; i1--)
            {
                var srcItem = tempInventoryItems[i1];
                if (srcItem != null && (MyDefinitionId)srcItem.Type == componentId && srcItem.Amount > 0)
                {
                    var maxpossibleAmount = Math.Min(neededAmount, (int)Math.Floor(remainingVolume / volume));
                    var pickedAmount = MyFixedPoint.Min(maxpossibleAmount, srcItem.Amount);
                    if (pickedAmount > 0)
                    {
                        welderInventory.RemoveItems(srcItem.ItemId, pickedAmount);
                        var physicalObjBuilder = (MyObjectBuilder_PhysicalObject)MyObjectBuilderSerializer.CreateNewObject((MyDefinitionId)srcItem.Type);
                        _TransportInventory.AddItems(pickedAmount, physicalObjBuilder);

                        neededAmount -= (int)pickedAmount;
                        remainingVolume -= (float)pickedAmount * volume;

                        picked = true;
                    }
                    Logging.Instance?.Write(Logging.Level.Verbose, "BuildAndRepairSystemBlock {0}: ServerPickFromWelder: {1}: missingAmount={2} pickedAmount={3} maxpossibleAmount={4} remainingVolume={5} transportVolumeTotal={6}", Logging.BlockName(_Welder, Logging.BlockNameOptions.None), componentId, neededAmount, pickedAmount, maxpossibleAmount, remainingVolume, _TransportInventory.CurrentVolume);
                }
                if (neededAmount <= 0 || remainingVolume <= 0) break;
            }
            tempInventoryItems.Clear();
            return picked;
        }

        /// <summary>
        ///
        /// </summary>
        /// <param name="block"></param>
        /// <returns></returns>
        private bool EmptyBlockInventories(IMyEntity entity, IMyInventory dstInventory, out bool isEmpty)
        {
            var running = false;
            var remainingVolume = _MaxTransportVolume - (float)dstInventory.CurrentVolume;

            Logging.Instance?.Write(Logging.Level.Verbose, "BuildAndRepairSystemBlock {0}: EmptyBlockInventories remainingVolume={1} Entity={2}, InventoryCount={3}", Logging.BlockName(_Welder, Logging.BlockNameOptions.None), remainingVolume, Logging.BlockName(entity, Logging.BlockNameOptions.None), entity.InventoryCount);

            isEmpty = true;
            for (var i1 = 0; i1 < entity.InventoryCount; i1++)
            {
                var srcInventory = entity.GetInventory(i1);
                if (srcInventory.Empty()) continue;

                if (remainingVolume <= 0) return true; //No more transport volume

                var tempInventoryItems = new List<MyInventoryItem>();
                srcInventory.GetItems(tempInventoryItems);
                for (var srcItemIndex = tempInventoryItems.Count - 1; srcItemIndex >= 0; srcItemIndex--)
                {
                    var srcItem = srcInventory.GetItemByID(tempInventoryItems[srcItemIndex].ItemId);
                    if (srcItem == null) continue;

                    var definition = MyDefinitionManager.Static.GetPhysicalItemDefinition(srcItem.Content.GetId());

                    var maxpossibleAmountFP = Math.Min((float)srcItem.Amount, remainingVolume / definition.Volume);
                    //Real Transport Volume is always bigger than logical _MaxTransportVolume so ceiling is no problem
                    var maxpossibleAmount = (MyFixedPoint)(definition.HasIntegralAmounts ? Math.Ceiling(maxpossibleAmountFP) : maxpossibleAmountFP);
                    if (dstInventory.TransferItemFrom(srcInventory, srcItemIndex, null, true, maxpossibleAmount, false))
                    {
                        remainingVolume -= (float)maxpossibleAmount * definition.Volume;
                        Logging.Instance?.Write(Logging.Level.Verbose, "BuildAndRepairSystemBlock {0}: EmptyBlockInventories Transfered Item {1} amount={2} remainingVolume={3}", Logging.BlockName(_Welder, Logging.BlockNameOptions.None), srcItem.Content.GetId(), maxpossibleAmount, remainingVolume);
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
                var remainingVolume = _MaxTransportVolume - (double)dstInventory.CurrentVolume;

                var definition = MyDefinitionManager.Static.GetPhysicalItemDefinition(floating.Item.Content.GetId());
                var startAmount = floating.Item.Amount;

                var maxremainAmount = (MyFixedPoint)(remainingVolume / definition.Volume);
                var maxpossibleAmount = maxremainAmount > floating.Item.Amount ? floating.Item.Amount : maxremainAmount; //Do not use MyFixedPoint.Min !Wrong Implementation could cause overflow!
                if (definition.HasIntegralAmounts) maxpossibleAmount = MyFixedPoint.Floor(maxpossibleAmount);
                Logging.Instance?.Write(Logging.Level.Verbose, "BuildAndRepairSystemBlock {0}: EmptyFloatingObject remainingVolume={1}, Item={2}, ItemAmount={3}, MaxPossibleAmount={4}, ItemVolume={5})", Logging.BlockName(_Welder, Logging.BlockNameOptions.None), remainingVolume, floating.Item.Content.GetId(), floating.Item.Amount, maxpossibleAmount, definition.Volume);
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
                    Logging.Instance?.Write(Logging.Level.Verbose, "BuildAndRepairSystemBlock {0}: EmptyFloatingObject Removed Item {1} amount={2} remainingVolume={3} remainingItemAmount={4}", Logging.BlockName(_Welder, Logging.BlockNameOptions.None), floating.Item.Content.GetId(), maxpossibleAmount, remainingVolume, floating.Item.Amount);
                    running = true;
                }
            }
            return running;
        }

        /// <summary>
        ///
        /// </summary>
        /// <param name="componentId"></param>
        /// <param name="neededAmount"></param>
        private void AddToMissingComponents(MyDefinitionId componentId, int neededAmount)
        {
            int missingAmount;
            if (State.MissingComponents.TryGetValue(componentId, out missingAmount))
            {
                State.MissingComponents[componentId] = missingAmount + neededAmount;
            }
            else
            {
                State.MissingComponents.Add(componentId, neededAmount);
            }
        }

        /// <summary>
        /// Pull components into welder
        /// </summary>
        private bool PullComponents(MyDefinitionId componentId, float volume, ref int neededAmount, ref float remainingVolume)
        {
            var availAmount = 0;
            var welderInventory = _Welder.GetInventory(0);
            var maxpossibleAmount = Math.Min(neededAmount, (int)Math.Ceiling(remainingVolume / volume));
            Logging.Instance?.Write(Logging.Level.Info, "BuildAndRepairSystemBlock {0}: PullComponents start: {1}={2} maxpossibleAmount={3} volume={4}", Logging.BlockName(_Welder, Logging.BlockNameOptions.None), componentId, neededAmount, maxpossibleAmount, volume);
            if (maxpossibleAmount <= 0) return false;
            var picked = false;
            lock (_PossibleSources)
            {
                foreach (var srcInventory in _PossibleSources)
                {
                    //Pre Test is 10 timers faster then get the whole list (as copy!) and iterate for nothing
                    if (srcInventory.FindItem(componentId) != null && srcInventory.CanTransferItemTo(welderInventory, componentId))
                    {
                        var tempInventoryItems = new List<MyInventoryItem>();
                        srcInventory.GetItems(tempInventoryItems);
                        for (var srcItemIndex = tempInventoryItems.Count - 1; srcItemIndex >= 0; srcItemIndex--)
                        {
                            var srcItem = tempInventoryItems[srcItemIndex];
                            if (srcItem != null && (MyDefinitionId)srcItem.Type == componentId && srcItem.Amount > 0)
                            {
                                var moved = false;
                                var amountMoveable = 0;
                                Logging.Instance?.Write(Logging.Level.Info, "BuildAndRepairSystemBlock {0}: PullComponents Found: {1}={2} in {3}", Logging.BlockName(_Welder, Logging.BlockNameOptions.None), componentId, srcItem.Amount, Logging.BlockName(srcInventory));
                                var amountPossible = Math.Min(maxpossibleAmount, (int)srcItem.Amount);
                                if (amountPossible > 0)
                                {
                                    amountMoveable = (int)welderInventory.MaxItemsAddable(amountPossible, componentId);
                                    if (amountMoveable > 0)
                                    {
                                        Logging.Instance?.Write(Logging.Level.Info, "BuildAndRepairSystemBlock {0}: PullComponents Try to move: {1}={2} from {3}", Logging.BlockName(_Welder, Logging.BlockNameOptions.None), componentId, amountMoveable, Logging.BlockName(srcInventory));
                                        moved = welderInventory.TransferItemFrom(srcInventory, srcItemIndex, null, true, amountMoveable);
                                        if (moved)
                                        {
                                            maxpossibleAmount -= amountMoveable;
                                            availAmount += amountMoveable;
                                            Logging.Instance?.Write(Logging.Level.Info, "BuildAndRepairSystemBlock {0}: PullComponents Moved: {1}={2} from {3}", Logging.BlockName(_Welder, Logging.BlockNameOptions.None), componentId, amountMoveable, Logging.BlockName(srcInventory));
                                            picked = ServerPickFromWelder(componentId, volume, ref neededAmount, ref remainingVolume) || picked;
                                        }
                                    }
                                    else
                                    {
                                        //No (more) space in welder
                                        Logging.Instance?.Write(Logging.Level.Info, "BuildAndRepairSystemBlock {0}: PullComponents no more space in welder: {1}", Logging.BlockName(_Welder, Logging.BlockNameOptions.None), componentId);
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
            var updateTargets = playTime.Subtract(_LastTargetsUpdate) >= NanobotBuildAndRepairSystemMod.Settings.TargetsUpdateInterval;
            var updateSources = updateTargets && playTime.Subtract(_LastSourceUpdate) >= NanobotBuildAndRepairSystemMod.Settings.SourcesUpdateInterval;
            if (updateTargets)
            {
                StartAsyncUpdateSourcesAndTargets(updateSources);
            }
        }

        /// <summary>
        /// Parse all the connected blocks and find the possible targets and sources of components
        /// </summary>
        public void StartAsyncUpdateSourcesAndTargets(bool updateSource)
        {
            if (!_Welder.UseConveyorSystem)
            {
                lock (_PossibleSources)
                {
                    _PossibleSources.Clear();
                }
            }

            if (!_Welder.Enabled || !_Welder.IsFunctional || State.Ready == false)
            {
                Logging.Instance?.Write(Logging.Level.Info, "BuildAndRepairSystemBlock {0}: AsyncUpdateSourcesAndTargets Enabled={1} IsFunctional={2} ---> not ready don't search for targets",
                        Logging.BlockName(_Welder, Logging.BlockNameOptions.None),
                        _Welder.Enabled,
                        _Welder.IsFunctional);

                lock (State.PossibleWeldTargets)
                {
                    State.PossibleWeldTargets.Clear();
                    State.PossibleWeldTargets.RebuildHash();
                }

                lock (State.PossibleGrindTargets)
                {
                    State.PossibleGrindTargets.Clear();
                    State.PossibleGrindTargets.RebuildHash();
                }

                lock (State.PossibleFloatingTargets)
                {
                    State.PossibleFloatingTargets.Clear();
                    State.PossibleFloatingTargets.RebuildHash();
                }

                _AsyncUpdateSourcesAndTargetsRunning = false;
                return;
            }

            lock (_Welder)
            {
                if (_AsyncUpdateSourcesAndTargetsRunning) return;

                _AsyncUpdateSourcesAndTargetsRunning = true;
                AsyncTaskQueue.Enqueue(() => AsyncUpdateSourcesAndTargets(updateSource));
            }
        }

        public void AsyncUpdateSourcesAndTargets(bool updateSource)
        {
            try
            {
                if (!State.Ready) return;
                var weldingEnabled = BlockWeldPriority.AnyEnabled && Settings.WorkMode != WorkModes.GrindOnly;
                var grindingEnabled = BlockGrindPriority.AnyEnabled && Settings.WorkMode != WorkModes.WeldOnly;

                updateSource &= _Welder.UseConveyorSystem;
                var pos = 0;
                try
                {
                    pos = 1;

                    var grids = new List<IMyCubeGrid>();
                    _TempPossibleWeldTargets.Clear();
                    _TempPossibleGrindTargets.Clear();
                    _TempPossibleFloatingTargets.Clear();
                    _TempPossibleSources.Clear();
                    _TempIgnore4Ingot.Clear();
                    _TempIgnore4Components.Clear();
                    _TempIgnore4Items.Clear();

                    var ignoreColor = Settings.IgnoreColorPacked;
                    var grindColor = Settings.GrindColorPacked;
                    var emitterMatrix = _Welder.WorldMatrix;
                    emitterMatrix.Translation = Vector3D.Transform(Settings.CorrectedAreaOffset, emitterMatrix);
                    var areaOrientedBox = new MyOrientedBoundingBoxD(Settings.CorrectedAreaBoundingBox, emitterMatrix);

                    Logging.Instance?.Write(Logging.Level.Verbose, "BuildAndRepairSystemBlock {0}: AsyncUpdateSourcesAndTargets Search: IgnoreColor={1}, GrindColor={2}, UseGrindJanitorOn={3}, Settings.WorkMode={4}, GrindJanitorOptions={5}",
                       Logging.BlockName(_Welder, Logging.BlockNameOptions.None), ignoreColor, grindColor, Settings.UseGrindJanitorOn, Settings.WorkMode, Settings.GrindJanitorOptions);

                    AsyncAddBlocksOfGrid(ref areaOrientedBox, (Settings.Flags & SyncBlockSettings.Settings.UseIgnoreColor) != 0, ref ignoreColor, (Settings.Flags & SyncBlockSettings.Settings.UseGrindColor) != 0, ref grindColor, Settings.UseGrindJanitorOn, Settings.GrindJanitorOptions, _Welder.CubeGrid, grids, updateSource ? _TempPossibleSources : null, weldingEnabled ? _TempPossibleWeldTargets : null, grindingEnabled ? _TempPossibleGrindTargets : null);
                    switch (Settings.SearchMode)
                    {
                        case SearchModes.Grids:
                            break;

                        case SearchModes.BoundingBox:
                            AsyncAddBlocksOfBox(ref areaOrientedBox, (Settings.Flags & SyncBlockSettings.Settings.UseIgnoreColor) != 0, ref ignoreColor, (Settings.Flags & SyncBlockSettings.Settings.UseGrindColor) != 0, ref grindColor, Settings.UseGrindJanitorOn, Settings.GrindJanitorOptions, grids, weldingEnabled ? _TempPossibleWeldTargets : null, grindingEnabled ? _TempPossibleGrindTargets : null, _ComponentCollectPriority.AnyEnabled ? _TempPossibleFloatingTargets : null);
                            break;
                    }

                    pos = 2;
                    if (updateSource)
                    {
                        Vector3D posWelder;
                        _Welder.SlimBlock.ComputeWorldCenter(out posWelder);
                        _TempPossibleSources.Sort((a, b) =>
                        {
                            var blockA = a.Owner as IMyCubeBlock;
                            var blockB = b.Owner as IMyCubeBlock;
                            if (blockA != null && blockB != null)
                            {
                                var welderA = blockA as IMyShipWelder;
                                var welderB = blockB as IMyShipWelder;
                                if (welderA == null == (welderB == null))
                                {
                                    Vector3D posA;
                                    Vector3D posB;
                                    blockA.SlimBlock.ComputeWorldCenter(out posA);
                                    blockB.SlimBlock.ComputeWorldCenter(out posB);
                                    var distanceA = (int)Math.Abs((posWelder - posA).Length());
                                    var distanceB = (int)Math.Abs((posWelder - posA).Length());
                                    return distanceA - distanceB;
                                }
                                else if (welderA == null)
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
                        foreach (var inventory in _TempPossibleSources)
                        {
                            var block = inventory.Owner as IMyShipWelder;
                            if (block != null && block.BlockDefinition.SubtypeName.Contains("NanobotBuildAndRepairSystem") && block.GameLogic != null)
                            {
                                var bar = block.GameLogic.GetAs<NanobotBuildAndRepairSystemBlock>();
                                //Don't use Bar's as destination that would push immediately
                                if (bar != null)
                                {
                                    if ((bar.Settings.Flags & SyncBlockSettings.Settings.PushIngotOreImmediately) != 0)
                                    {
                                        _TempIgnore4Ingot.Add(inventory);
                                    }
                                    if ((bar.Settings.Flags & SyncBlockSettings.Settings.PushComponentImmediately) != 0)
                                    {
                                        _TempIgnore4Components.Add(inventory);
                                    }
                                    if ((bar.Settings.Flags & SyncBlockSettings.Settings.PushItemsImmediately) != 0)
                                    {
                                        _TempIgnore4Items.Add(inventory);
                                    }
                                }
                            }
                        }
                    }

                    pos = 3;
                    _TempPossibleWeldTargets.Sort((a, b) =>
                    {
                        var priorityA = BlockWeldPriority.GetPriority(a.Block);
                        var priorityB = BlockWeldPriority.GetPriority(b.Block);
                        if (priorityA == priorityB)
                        {
                            return Utils.CompareDistance(a.Distance, b.Distance);
                        }
                        else return priorityA - priorityB;
                    });

                    pos = 4;
                    _TempPossibleGrindTargets.Sort((a, b) =>
                    {
                        if ((a.Attributes & TargetBlockData.AttributeFlags.Autogrind) == (b.Attributes & TargetBlockData.AttributeFlags.Autogrind))
                        {
                            if ((a.Attributes & TargetBlockData.AttributeFlags.Autogrind) != 0)
                            {
                                var priorityA = BlockGrindPriority.GetPriority(a.Block);
                                var priorityB = BlockGrindPriority.GetPriority(b.Block);
                                if (priorityA == priorityB)
                                {
                                    if ((Settings.Flags & SyncBlockSettings.Settings.GrindSmallestGridFirst) != 0)
                                    {
                                        var res = ((MyCubeGrid)a.Block.CubeGrid).BlocksCount - ((MyCubeGrid)b.Block.CubeGrid).BlocksCount;
                                        return res != 0 ? res : Utils.CompareDistance(a.Distance, b.Distance);
                                    }
                                    if ((Settings.Flags & SyncBlockSettings.Settings.GrindNearFirst) != 0) return Utils.CompareDistance(a.Distance, b.Distance);
                                    return Utils.CompareDistance(b.Distance, a.Distance);
                                }
                                else return priorityA - priorityB;
                            }

                            if ((Settings.Flags & SyncBlockSettings.Settings.GrindSmallestGridFirst) != 0)
                            {
                                var res = ((MyCubeGrid)a.Block.CubeGrid).BlocksCount - ((MyCubeGrid)b.Block.CubeGrid).BlocksCount;
                                return res != 0 ? res : Utils.CompareDistance(a.Distance, b.Distance);
                            }
                            if ((Settings.Flags & SyncBlockSettings.Settings.GrindNearFirst) != 0) return Utils.CompareDistance(a.Distance, b.Distance);
                            return Utils.CompareDistance(b.Distance, a.Distance);
                        }
                        else if ((a.Attributes & TargetBlockData.AttributeFlags.Autogrind) != 0) return -1;
                        else if ((b.Attributes & TargetBlockData.AttributeFlags.Autogrind) != 0) return 1;
                        return 0;
                    });

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

                    pos = 5;
                    if (Logging.Instance.ShouldLog(Logging.Level.Verbose))
                    {
                        lock (Logging.Instance)
                        {
                            Logging.Instance?.Write(Logging.Level.Verbose, "BuildAndRepairSystemBlock {0}: AsyncUpdateSourcesAndTargets Possible Build Target Blocks ---> {1}", Logging.BlockName(_Welder, Logging.BlockNameOptions.None), _TempPossibleWeldTargets.Count);
                            Logging.Instance.IncreaseIndent(Logging.Level.Verbose);
                            foreach (var blockData in _TempPossibleWeldTargets)
                            {
                                Logging.Instance?.Write(Logging.Level.Verbose, "Block: {0} ({1})", Logging.BlockName(blockData.Block), blockData.Distance);
                            }
                            Logging.Instance.DecreaseIndent(Logging.Level.Verbose);
                            Logging.Instance?.Write(Logging.Level.Verbose, "<---");

                            Logging.Instance?.Write(Logging.Level.Verbose, "BuildAndRepairSystemBlock {0}: AsyncUpdateSourcesAndTargets Possible Grind Target Blocks ---> {1}", Logging.BlockName(_Welder, Logging.BlockNameOptions.None), _TempPossibleGrindTargets.Count);
                            Logging.Instance.IncreaseIndent(Logging.Level.Verbose);
                            foreach (var blockData in _TempPossibleGrindTargets)
                            {
                                Logging.Instance?.Write(Logging.Level.Verbose, "Block: {0} ({1})", Logging.BlockName(blockData.Block), blockData.Distance);
                            }
                            Logging.Instance.DecreaseIndent(Logging.Level.Verbose);
                            Logging.Instance?.Write(Logging.Level.Verbose, "<---");

                            Logging.Instance?.Write(Logging.Level.Verbose, "BuildAndRepairSystemBlock {0}: AsyncUpdateSourcesAndTargets Possible Floating Targets ---> {1}", Logging.BlockName(_Welder, Logging.BlockNameOptions.None), _TempPossibleFloatingTargets.Count);
                            Logging.Instance.IncreaseIndent(Logging.Level.Verbose);
                            foreach (var floatingData in _TempPossibleFloatingTargets)
                            {
                                Logging.Instance?.Write(Logging.Level.Verbose, "Floating: {0} ({1})", Logging.BlockName(floatingData.Entity), floatingData.Distance);
                            }
                            Logging.Instance.DecreaseIndent(Logging.Level.Verbose);
                            Logging.Instance?.Write(Logging.Level.Verbose, "<---");

                            if (updateSource)
                            {
                                Logging.Instance?.Write(Logging.Level.Verbose, "BuildAndRepairSystemBlock {0}: AsyncUpdateSourcesAndTargets Possible Source Blocks ---> {1}", Logging.BlockName(_Welder, Logging.BlockNameOptions.None), _TempPossibleSources.Count);
                                Logging.Instance.IncreaseIndent(Logging.Level.Verbose);
                                foreach (var inventory in _TempPossibleSources)
                                {
                                    Logging.Instance?.Write(Logging.Level.Verbose, "Inventory: {0} {1}{2}{3}", Logging.BlockName(inventory), _TempIgnore4Ingot.Contains(inventory) ? string.Empty : "(Not 4 Ingot)", _TempIgnore4Components.Contains(inventory) ? string.Empty : "(Not 4 Components)", _TempIgnore4Items.Contains(inventory) ? string.Empty : "(Not 4 Items)");
                                }
                                Logging.Instance.DecreaseIndent(Logging.Level.Verbose);
                                Logging.Instance?.Write(Logging.Level.Verbose, "<---");
                            }
                        }
                    }

                    pos = 6;
                    lock (State.PossibleWeldTargets)
                    {
                        State.PossibleWeldTargets.Clear();
                        State.PossibleWeldTargets.AddRange(_TempPossibleWeldTargets);
                        State.PossibleWeldTargets.RebuildHash();
                    }
                    _TempPossibleWeldTargets.Clear();
                    pos = 7;
                    lock (State.PossibleGrindTargets)
                    {
                        State.PossibleGrindTargets.Clear();
                        State.PossibleGrindTargets.AddRange(_TempPossibleGrindTargets);
                        State.PossibleGrindTargets.RebuildHash();
                    }
                    _TempPossibleGrindTargets.Clear();
                    pos = 8;
                    lock (State.PossibleFloatingTargets)
                    {
                        State.PossibleFloatingTargets.Clear();
                        State.PossibleFloatingTargets.AddRange(_TempPossibleFloatingTargets);
                        State.PossibleFloatingTargets.RebuildHash();
                    }
                    _TempPossibleFloatingTargets.Clear();

                    pos = 9;
                    if (updateSource)
                    {
                        lock (_PossibleSources)
                        {
                            _PossibleSources.Clear();
                            _PossibleSources.AddRange(_TempPossibleSources);
                            _Ignore4Ingot.Clear();
                            _Ignore4Ingot.UnionWith(_TempIgnore4Ingot);
                            _Ignore4Components.Clear();
                            _Ignore4Components.UnionWith(_TempIgnore4Components);
                            _Ignore4Items.Clear();
                            _Ignore4Items.UnionWith(_TempIgnore4Items);
                        }
                        _TempPossibleSources.Clear();
                        _TempIgnore4Ingot.Clear();
                        _TempIgnore4Components.Clear();
                        _TempIgnore4Items.Clear();
                    }

                    _ContinuouslyError = 0;
                }
                catch (Exception ex)
                {
                    _ContinuouslyError++;
                    if (_ContinuouslyError > 10 || Logging.Instance.ShouldLog(Logging.Level.Info) || Logging.Instance.ShouldLog(Logging.Level.Verbose))
                    {
                        Logging.Instance?.Error("BuildAndRepairSystemBlock {0}: AsyncUpdateSourcesAndTargets exception at {1}: {2}", Logging.BlockName(_Welder, Logging.BlockNameOptions.None), pos, ex);
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
        /// Search for grids inside bounding box and add their damaged block also
        /// </summary>
        private void AsyncAddBlocksOfBox(ref MyOrientedBoundingBoxD areaBox, bool useIgnoreColor, ref uint ignoreColor, bool useGrindColor, ref uint grindColor, AutoGrindRelation autoGrindRelation, AutoGrindOptions autoGrindOptions, List<IMyCubeGrid> grids, List<TargetBlockData> possibleWeldTargets, List<TargetBlockData> possibleGrindTargets, List<TargetEntityData> possibleFloatingTargets)
        {
            Logging.Instance?.Write(Logging.Level.Verbose, "BuildAndRepairSystemBlock {0}: AsyncAddBlockOfBox", Logging.BlockName(_Welder, Logging.BlockNameOptions.None));

            var emitterMatrix = _Welder.WorldMatrix;
            emitterMatrix.Translation = Vector3D.Transform(Settings.CorrectedAreaOffset, emitterMatrix);
            var areaBoundingBox = Settings.CorrectedAreaBoundingBox.TransformFast(emitterMatrix);
            List<IMyEntity> entityInRange = null;

            // TODO: Cache this?
            lock (MyAPIGateway.Entities)
            {
                entityInRange = MyAPIGateway.Entities.GetElementsInBox(ref areaBoundingBox);
            }

            if (entityInRange != null)
            {
                foreach (var entity in entityInRange)
                {
                    var grid = entity as IMyCubeGrid;
                    if (grid != null)
                    {
                        AsyncAddBlocksOfGrid(ref areaBox, useIgnoreColor, ref ignoreColor, useGrindColor, ref grindColor, autoGrindRelation, autoGrindOptions, grid, grids, null, possibleWeldTargets, possibleGrindTargets);
                        continue;
                    }

                    if (possibleFloatingTargets != null)
                    {
                        var floating = entity as MyFloatingObject;

                        if (floating != null)
                        {
                            if (!floating.MarkedForClose && ComponentCollectPriority.GetEnabled(floating.Item.Content.GetObjectId()))
                            {
                                var distance = (areaBox.Center - floating.WorldMatrix.Translation).Length();
                                possibleFloatingTargets.Add(new TargetEntityData(floating, distance));
                            }
                            continue;
                        }

                        var character = entity as IMyCharacter;
                        if (character != null)
                        {
                            if (character.IsDead && !character.InventoriesEmpty() && !((MyCharacterDefinition)character.Definition).EnableSpawnInventoryAsContainer)
                            {
                                var distance = (areaBox.Center - character.WorldMatrix.Translation).Length();
                                possibleFloatingTargets.Add(new TargetEntityData(character, distance));
                            }
                            continue;
                        }

                        var inventoryBag = entity as IMyInventoryBag;
                        if (inventoryBag != null)
                        {
                            if (!inventoryBag.InventoriesEmpty())
                            {
                                var distance = (areaBox.Center - inventoryBag.WorldMatrix.Translation).Length();
                                possibleFloatingTargets.Add(new TargetEntityData(inventoryBag, distance));
                            }
                            continue;
                        }
                    }
                }
            }
        }
       
        private bool IsMovingNotOwnedTarget(IMyCubeGrid targetGrid)
        {
            if (targetGrid != null && targetGrid.Closed && Welder.CubeGrid?.EntityId != targetGrid.EntityId)
            {
                var isMoving = targetGrid.Physics?.IsMoving == true && targetGrid.Physics?.Speed >= 1;

                if (isMoving)
                {
                    return true;
                }                
            }

            return false;
        }

        private void AsyncAddBlocksOfGrid(ref MyOrientedBoundingBoxD areaBox, bool useIgnoreColor, ref uint ignoreColor, bool useGrindColor, ref uint grindColor, AutoGrindRelation autoGrindRelation, AutoGrindOptions autoGrindOptions, IMyCubeGrid cubeGrid, List<IMyCubeGrid> grids, List<IMyInventory> possibleSources, List<TargetBlockData> possibleWeldTargets, List<TargetBlockData> possibleGrindTargets)
        {
            if (!State.Ready) return; //Block not ready

            if (grids.Contains(cubeGrid)) return; //Allready parsed

            Logging.Instance?.Write(Logging.Level.Verbose, "BuildAndRepairSystemBlock {0}: AsyncAddBlocksOfGrid AddGrid {1}", Logging.BlockName(_Welder, Logging.BlockNameOptions.None), cubeGrid.DisplayName);
            grids.Add(cubeGrid);

            var newBlocks = new List<IMySlimBlock>();

            // TODO: Cache this?
            cubeGrid.GetBlocks(newBlocks);

            foreach (var slimBlock in newBlocks)
            {
                AsyncAddBlockIfTargetOrSource(ref areaBox, useIgnoreColor, ref ignoreColor, useGrindColor, ref grindColor, autoGrindRelation, autoGrindOptions, slimBlock, possibleSources, possibleWeldTargets, possibleGrindTargets);

                var fatBlock = slimBlock.FatBlock;
                if (fatBlock == null) continue;

                var mechanicalConnectionBlock = fatBlock as Sandbox.ModAPI.IMyMechanicalConnectionBlock;
                if (mechanicalConnectionBlock != null)
                {
                    if (mechanicalConnectionBlock.TopGrid != null)
                        AsyncAddBlocksOfGrid(ref areaBox, useIgnoreColor, ref ignoreColor, useGrindColor, ref grindColor, autoGrindRelation, autoGrindOptions, mechanicalConnectionBlock.TopGrid, grids, possibleSources, possibleWeldTargets, possibleGrindTargets);
                    continue;
                }

                var attachableTopBlock = fatBlock as Sandbox.ModAPI.IMyAttachableTopBlock;
                if (attachableTopBlock != null)
                {
                    if (attachableTopBlock.Base?.CubeGrid != null)
                        AsyncAddBlocksOfGrid(ref areaBox, useIgnoreColor, ref ignoreColor, useGrindColor, ref grindColor, autoGrindRelation, autoGrindOptions, attachableTopBlock.Base.CubeGrid, grids, possibleSources, possibleWeldTargets, possibleGrindTargets);
                    continue;
                }

                var connector = fatBlock as Sandbox.ModAPI.IMyShipConnector;
                if (connector != null)
                {
                    if (connector.Status == MyShipConnectorStatus.Connected && connector.OtherConnector != null)
                    {
                        AsyncAddBlocksOfGrid(ref areaBox, useIgnoreColor, ref ignoreColor, useGrindColor, ref grindColor, autoGrindRelation, autoGrindOptions, connector.OtherConnector.CubeGrid, grids, possibleSources, possibleWeldTargets, possibleGrindTargets);
                    }
                    continue;
                }

                if (possibleWeldTargets != null && (Settings.Flags & SyncBlockSettings.Settings.AllowBuild) != 0) //If projected blocks should be build
                {
                    var projector = fatBlock as Sandbox.ModAPI.IMyProjector;
                    if (projector != null)
                    {
                        Logging.Instance?.Write(Logging.Level.Verbose, "BuildAndRepairSystemBlock {0}: Projector={1} IsProjecting={2} BuildableBlockCount={3} IsRelationAllowed={4} Relation={5}/{6}/{7}", Logging.BlockName(_Welder, Logging.BlockNameOptions.None), Logging.BlockName(projector), projector.IsProjecting, projector.BuildableBlocksCount, IsRelationAllowed4Welding(slimBlock), slimBlock.GetUserRelationToOwner(_Welder.OwnerId), projector.GetUserRelationToOwner(_Welder.OwnerId), slimBlock.CubeGrid.GetUserRelationToOwner(_Welder.OwnerId));
                        if (projector.IsProjecting && projector.BuildableBlocksCount > 0 && IsRelationAllowed4Welding(slimBlock))
                        {
                            //Add buildable blocks
                            var projectedCubeGrid = projector.ProjectedGrid;
                            if (projectedCubeGrid != null && !grids.Contains(projectedCubeGrid))
                            {
                                grids.Add(projectedCubeGrid);
                                var projectedBlocks = new List<IMySlimBlock>();
                                projectedCubeGrid.GetBlocks(projectedBlocks);

                                foreach (var block in projectedBlocks)
                                {
                                    double distance;
                                    Logging.Instance?.Write(Logging.Level.Verbose, "BuildAndRepairSystemBlock {0}: Projector={1} Block={2} BlockKindEnabled={3}, InRange={4}, CanBuild={5}/{6} BlockClass={7}", Logging.BlockName(_Welder, Logging.BlockNameOptions.None), Logging.BlockName(projector), Logging.BlockName(block), BlockWeldPriority.GetEnabled(block), block.IsInRange(ref areaBox, out distance), block.CanBuild(false), block.Dithering, BlockWeldPriority.GetItemAlias(block, true));
                                    if (BlockWeldPriority.GetEnabled(block) && block.IsInRange(ref areaBox, out distance) && block.CanBuild(false))
                                    {
                                        Logging.Instance?.Write(Logging.Level.Verbose, "BuildAndRepairSystemBlock {0}: Add projected Block {1}:{2}", Logging.BlockName(_Welder, Logging.BlockNameOptions.None), Logging.BlockName(projector), Logging.BlockName(block));
                                        possibleWeldTargets.Add(new TargetBlockData(block, distance, TargetBlockData.AttributeFlags.Projected));
                                    }
                                }
                            }
                        }
                        continue;
                    }
                }
            }
        }

        /// <summary>
        ///
        /// </summary>
        private void AsyncAddBlockIfTargetOrSource(ref MyOrientedBoundingBoxD areaBox, bool useIgnoreColor, ref uint ignoreColor, bool useGrindColor, ref uint grindColor, AutoGrindRelation autoGrindRelation, AutoGrindOptions autoGrindOptions, IMySlimBlock block, List<IMyInventory> possibleSources, List<TargetBlockData> possibleWeldTargets, List<TargetBlockData> possibleGrindTargets)
        {
            try
            {
                if (possibleSources != null)
                {
                    //Search for sources of components (Container, Assembler, Welder, Grinder, ?)
                    var terminalBlock = block.FatBlock as IMyTerminalBlock;
                    if (terminalBlock != null && terminalBlock.EntityId != _Welder.EntityId && terminalBlock.IsFunctional) //Own inventory is no external source (handled internally)
                    {
                        var relation = terminalBlock.GetUserRelationToOwner(_Welder.OwnerId);
                        if (MyRelationsBetweenPlayerAndBlockExtensions.IsFriendly(relation))
                        {
                            try
                            {
                                var welderInventory = _Welder.GetInventory(0);
                                var maxInv = terminalBlock.InventoryCount;
                                for (var idx = 0; idx < maxInv; idx++)
                                {
                                    var inventory = terminalBlock.GetInventory(idx);
                                    if (!possibleSources.Contains(inventory) && inventory.IsConnectedTo(welderInventory))
                                    {
                                        possibleSources.Add(inventory);
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                Logging.Instance?.Write(Logging.Level.Event, "BuildAndRepairSystemBlock {0}: AsyncAddBlockIfTargetOrSource1 exception: {1}", Logging.BlockName(_Welder, Logging.BlockNameOptions.None), ex);
                            }
                        }
                    }
                }

                var added = false;
                if (possibleGrindTargets != null && (useGrindColor || autoGrindRelation != 0))
                {
                    added = AsyncAddBlockIfGrindTarget(ref areaBox, useGrindColor, ref grindColor, autoGrindRelation, autoGrindOptions, block, possibleGrindTargets);                    
                }

                if (possibleWeldTargets != null && !added) //Do not weld if in grind list (could happen if auto grind neutrals is enabled and "HelpOthers" is active)
                {
                    AsyncAddBlockIfWeldTarget(ref areaBox, useIgnoreColor, ref ignoreColor, useGrindColor, ref grindColor, block, possibleWeldTargets);                    
                }
            }
            catch (Exception ex)
            {
                Logging.Instance?.Error("BuildAndRepairSystemBlock {0}: AsyncAddBlockIfTargetOrSource2 exception: {1}", Logging.BlockName(_Welder, Logging.BlockNameOptions.None), ex);
                throw;
            }
        }

        /// <summary>
        /// Check if the given slim block is a weld target (in range, owned, damaged, new, ..)
        /// </summary>
        private bool AsyncAddBlockIfWeldTarget(ref MyOrientedBoundingBoxD areaBox, bool useIgnoreColor, ref uint ignoreColor, bool useGrindColor, ref uint grindColor, IMySlimBlock block, List<TargetBlockData> possibleWeldTargets)
        {
            Logging.Instance?.Write(Logging.Level.Verbose, "BuildAndRepairSystemBlock {0}: Weld Check Block {1} IsProjected={2} IsDestroyed={3}, IsFullyDismounted={4}, HasFatBlock={5}, FatBlockClosed={6}, MaxDeformation={7}, (HasDeformation={8}), IsFullIntegrity={9}, Integrity={10}, NeedRepair={11}, Relation={12}, useIgnorColor={13}, HasIgnoreColor={14} ({15},{16})", //, ActionAllowed={17}",
                Logging.BlockName(_Welder, Logging.BlockNameOptions.None),
                Logging.BlockName(block),
                block.IsProjected(),
                block.IsDestroyed, block.IsFullyDismounted, block.FatBlock != null, block.FatBlock != null ? block.FatBlock.Closed.ToString() : "-",
                block.MaxDeformation, block.HasDeformation, block.IsFullIntegrity, block.Integrity, block.NeedRepair(GetIntegrityLevel()), block.GetUserRelationToOwner(_Welder.OwnerId),
                useIgnoreColor, IsColorNearlyEquals(ignoreColor, block.GetColorMask()), ignoreColor, block.GetColorMask().PackHSVToUint()
            );

            double distance;
            var colorMask = block.GetColorMask();
            Sandbox.ModAPI.IMyProjector projector;
            if (block.IsProjected(out projector))
            {
                if ((Settings.Flags & SyncBlockSettings.Settings.AllowBuild) != 0 &&
                   (!useGrindColor || !IsColorNearlyEquals(grindColor, colorMask)) &&
                   BlockWeldPriority.GetEnabled(block) &&
                   block.IsInRange(ref areaBox, out distance) &&
                   IsRelationAllowed4Welding(projector.SlimBlock) &&
                   block.CanBuild(false) && 
                   !SafeZoneProtection.IsProtectedFromWelding(block, Welder, true))
                {
                    Logging.Instance?.Write(Logging.Level.Info, "BuildAndRepairSystemBlock {0}: Add projected Block {1}, HasFatBlock={2}, Class={3}", Logging.BlockName(_Welder, Logging.BlockNameOptions.None), Logging.BlockName(block), block.FatBlock != null, BlockWeldPriority.GetItemAlias(block, true));
                    possibleWeldTargets.Add(new TargetBlockData(block, distance, TargetBlockData.AttributeFlags.Projected));
                    return true;
                }
            }
            else
            {
                if ((!useIgnoreColor || !IsColorNearlyEquals(ignoreColor, colorMask)) && (!useGrindColor || !IsColorNearlyEquals(grindColor, colorMask)) &&
                   BlockWeldPriority.GetEnabled(block) &&
                   block.IsInRange(ref areaBox, out distance) &&
                   IsRelationAllowed4Welding(block) &&
                   block.NeedRepair(GetIntegrityLevel()) &&
                   !SafeZoneProtection.IsProtectedFromWelding(block, Welder))
                {
                    Logging.Instance?.Write(Logging.Level.Info, "BuildAndRepairSystemBlock {0}: Add damaged Block {1} MaxDeformation={2}, (HasDeformation={3}), IsFullIntegrity={4}, HasFatBlock={5}", Logging.BlockName(_Welder, Logging.BlockNameOptions.None), Logging.BlockName(block), block.MaxDeformation, block.HasDeformation, block.IsFullIntegrity, block.FatBlock != null);
                    possibleWeldTargets.Add(new TargetBlockData(block, distance, 0));
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Check if the given slim block is a grind target (in range, color )
        /// </summary>
        private bool AsyncAddBlockIfGrindTarget(ref MyOrientedBoundingBoxD areaBox, bool useGrindColor, ref uint grindColor, AutoGrindRelation autoGrindRelation, AutoGrindOptions autoGrindOptions, IMySlimBlock block, List<TargetBlockData> possibleGrindTargets)
        {
            //block.CubeGrid.BlocksDestructionEnabled is not available for modding, so at least check if general destruction is enabled
            if ((MyAPIGateway.Session.SessionSettings.Scenario || MyAPIGateway.Session.SessionSettings.ScenarioEditMode) && !MyAPIGateway.Session.SessionSettings.DestructibleBlocks) return false;

            // Is being projected?
            if (block.IsProjected())
                return false;

            if(!NanobotBuildAndRepairSystemMod.Settings.AllowEnemyGrindingInMotion && block.CubeGrid != null && !block.CubeGrid.Closed)
            {
                // Check if grinding is allowed in motion or not. Handles grinding abuse of enemy grids. Can be configured in modsettings.xml
                if (IsMovingNotOwnedTarget(block?.CubeGrid))
                {
                    return false;
                }

                // If we are in motion, grinding is not allowed.
                if (Welder.CubeGrid.EntityId != block.CubeGrid.EntityId && Welder.CubeGrid.Physics?.IsMoving == true && Welder.CubeGrid.Physics?.Speed >= 1)
                {
                    return false;
                }
            }

            if (Logging.Instance.ShouldLog(Logging.Level.Verbose))
            {
                Logging.Instance?.Write(Logging.Level.Verbose, "BuildAndRepairSystemBlock {0}: Grind Check Block {1} Projected={2} AutoGrindRelation={3} Relation={4} UseGrindColor={5} HasGrindColor={6} ({7},{8})/", // ActionAllowed={10}",
                Logging.BlockName(_Welder, Logging.BlockNameOptions.None), Logging.BlockName(block), block.IsProjected(), autoGrindRelation, block.GetUserRelationToOwner(_Welder.OwnerId), useGrindColor,
                   IsColorNearlyEquals(grindColor, block.GetColorMask()), grindColor, block.GetColorMask().PackHSVToUint());
            }

            var autoGrind = autoGrindRelation != 0 && BlockGrindPriority.GetEnabled(block);

            if (autoGrind)
            {
                // Do not allow grinding if our shields are up.
                if (block.CubeGrid.EntityId != Welder.CubeGrid.EntityId && IsWelderShielded())
                    return false;

                var relation = block.GetUserRelationToOwner(_Welder.OwnerId);
                autoGrind =
                   (relation == MyRelationsBetweenPlayerAndBlock.NoOwnership && (autoGrindRelation & AutoGrindRelation.NoOwnership) != 0) ||
                   (relation == MyRelationsBetweenPlayerAndBlock.Enemies && (autoGrindRelation & AutoGrindRelation.Enemies) != 0) ||
                   (relation == MyRelationsBetweenPlayerAndBlock.Neutral && (autoGrindRelation & AutoGrindRelation.Neutral) != 0);
            }

            if (autoGrind && (autoGrindOptions & (AutoGrindOptions.DisableOnly | AutoGrindOptions.HackOnly)) != 0)
            {
                var criticalIntegrityRatio = ((MyCubeBlockDefinition)block.BlockDefinition).CriticalIntegrityRatio;
                var ownershipIntegrityRatio = ((MyCubeBlockDefinition)block.BlockDefinition).OwnershipIntegrityRatio > 0 ? ((MyCubeBlockDefinition)block.BlockDefinition).OwnershipIntegrityRatio : criticalIntegrityRatio;
                var integrityRation = block.Integrity / block.MaxIntegrity;
                if (autoGrind && (autoGrindOptions & AutoGrindOptions.DisableOnly) != 0)
                {
                    autoGrind = block.FatBlock != null && integrityRation > criticalIntegrityRatio;
                }
                if (autoGrind && (autoGrindOptions & AutoGrindOptions.HackOnly) != 0)
                {
                    autoGrind = block.FatBlock != null && integrityRation > ownershipIntegrityRatio;
                }
            }

            if (autoGrind || (useGrindColor && IsColorNearlyEquals(grindColor, block.GetColorMask())))
            {
                double distance;
                if (block.IsInRange(ref areaBox, out distance))
                {
                    // Is protected by SafeZone?
                    if (SafeZoneProtection.IsProtectedFromGrinding(block, Welder))
                    {
                        return false;
                    }

                    // Is protected by shields.
                    if (GrindManager.IsShieldProtected(this, block))
                    {
                        return false;
                    }

                    Logging.Instance?.Write(Logging.Level.Info, "BuildAndRepairSystemBlock {0}: Add grind Block {1}", Logging.BlockName(_Welder, Logging.BlockNameOptions.None), Logging.BlockName(block));
                    possibleGrindTargets.Add(new TargetBlockData(block, distance, autoGrind ? TargetBlockData.AttributeFlags.Autogrind : 0));
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        ///
        /// </summary>
        /// <param name="block"></param>
        /// <returns></returns>
        private bool IsRelationAllowed4Welding(IMySlimBlock block)
        {
            var relation = _Welder.OwnerId == 0 ? MyRelationsBetweenPlayerAndBlock.NoOwnership : block.GetUserRelationToOwner(_Welder.OwnerId);
            if (relation == MyRelationsBetweenPlayerAndBlock.Enemies) return false;
            if (!_Welder.HelpOthers && (relation == MyRelationsBetweenPlayerAndBlock.Neutral || relation == MyRelationsBetweenPlayerAndBlock.NoOwnership)) return false;
            return true;
        }

        /// <summary>
        ///
        /// </summary>
        /// <param name="block"></param>
        /// <returns></returns>
        private static bool IsColorNearlyEquals(uint colorA, Vector3 colorB)
        {
            return colorA == colorB.PackHSVToUint();
        }

        /// <summary>
        /// Update custom info of the block
        /// </summary>
        /// <param name="block"></param>
        /// <param name="customInfo"></param>
        private void AppendingCustomInfo(IMyTerminalBlock terminalBlock, StringBuilder customInfo)
        {
            customInfo.Clear();

            customInfo.Append("State: ");

            if (State.Welding)
            {
                customInfo.Append("Welding");
            }
            else if (State.Grinding)
            {
                customInfo.Append("Grinding");
            }
            else if (State.InventoryFull)
            {
                customInfo.Append("Inventory Full");
            }
            else if (State.Transporting)
            {
                customInfo.Append("Transporting");
            }
            else
            {
                customInfo.Append("Idle");
            }

            customInfo.Append(Environment.NewLine);

            // Print time until power off.
            if(_Welder.Enabled && Settings.UseAutoPowerOffWhenIdle == 1 && IsIdle())
            {
                var elapsedIdleTime = MyAPIGateway.Session.ElapsedPlayTime.Subtract(LastTaskTime);
                var timeTillPowerOff = TimeSpan.FromMinutes(NanobotBuildAndRepairSystemMod.Settings.AutoPowerOffOnIdleMinutes).Subtract(elapsedIdleTime);

                customInfo.Append($"Time till Auto Power Off: {timeTillPowerOff.ToString(@"mm\:ss")}");
                customInfo.Append(Environment.NewLine);
            }

            // Print Power details.
            var resourceSink = _Welder.Components.Get<Sandbox.Game.EntityComponents.MyResourceSinkComponent>();
            if (resourceSink != null)
            {
                customInfo.Append(MyTexts.Get(MyStringId.GetOrCompute("BlockPropertiesText_MaxRequiredInput")));
                MyValueFormatter.AppendWorkInBestUnit(resourceSink.MaxRequiredInputByType(ElectricityId), customInfo);
                customInfo.Append(Environment.NewLine);

                customInfo.Append(MyTexts.Get(MyStringId.GetOrCompute("BlockPropertiesText_RequiredInput")));

                var requiredPower = PowerManager.ComputeRequiredElectricPower(this);
                // var availablePower = PowerManager.GetAvailablePower(this);

                MyValueFormatter.AppendWorkInBestUnit(PowerManager.ComputeRequiredElectricPower(this), customInfo);
                //MyValueFormatter.AppendWorkInBestUnit(resourceSink.RequiredInputByType(ElectricityId), customInfo);
                customInfo.Append(Environment.NewLine);
            }
            customInfo.Append(Environment.NewLine);

            if (_Welder.Enabled && _Welder.IsWorking && _Welder.IsFunctional)
            {
                if ((Settings.Flags & SyncBlockSettings.Settings.ScriptControlled) != 0)
                {
                    customInfo.Append(Texts.Info_CurentWeldEntity + Environment.NewLine);
                    customInfo.Append(string.Format(" -{0}" + Environment.NewLine, Settings.CurrentPickedWeldingBlock.BlockName()));
                    customInfo.Append(Texts.Info_CurentGrindEntity + Environment.NewLine);
                    customInfo.Append(string.Format(" -{0}" + Environment.NewLine, Settings.CurrentPickedGrindingBlock.BlockName()));
                }

                if (State.InventoryFull) customInfo.Append(Texts.Info_InventoryFull + Environment.NewLine);
                if (State.LimitsExceeded) customInfo.Append(Texts.Info_LimitReached + Environment.NewLine);

                var cnt = 0;

                lock (State.MissingComponents)
                {
                    if (State.MissingComponents?.Count > 0)
                    {
                        customInfo.Append(Texts.Info_MissingItems + Environment.NewLine);

                        foreach (var component in State.MissingComponents)
                        {
                            var componentId = new MyDefinitionId(typeof(MyObjectBuilder_Component), component.Key.SubtypeId);
                            MyComponentDefinition componentDefnition;
                            var name = MyDefinitionManager.Static.TryGetComponentDefinition(componentId, out componentDefnition) ? componentDefnition.DisplayNameText : component.Key.SubtypeName;
                            customInfo.Append(string.Format(" -{0}: {1}" + Environment.NewLine, name, component.Value));
                            cnt++;
                            if (cnt >= SyncBlockState.MaxSyncItems)
                            {
                                customInfo.Append(Texts.Info_More + Environment.NewLine);
                                break;
                            }
                        }

                        customInfo.Append(Environment.NewLine);
                    }
                }

                cnt = 0;

                lock (State.PossibleWeldTargets)
                {
                    if (State.PossibleWeldTargets?.Count > 0)
                    {
                        customInfo.Append(Texts.Info_BlocksToBuild + Environment.NewLine);

                        foreach (var blockData in State.PossibleWeldTargets)
                        {
                            if(blockData.Block != null)
                            {
                                customInfo.Append(string.Format(" -{0}" + Environment.NewLine, blockData.Block.BlockName()));
                            }

                            cnt++;
                            if (cnt >= SyncBlockState.MaxSyncItems)
                            {
                                customInfo.Append(Texts.Info_More + Environment.NewLine);
                                break;
                            }
                        }

                        customInfo.Append(Environment.NewLine);
                    }
                }

                cnt = 0;

                lock (State.PossibleGrindTargets)
                {
                    if (State.PossibleGrindTargets?.Count > 0)
                    {
                        customInfo.Append(Texts.Info_BlocksToGrind + Environment.NewLine);

                        foreach (var blockData in State.PossibleGrindTargets)
                        {
                            if (blockData.Block != null)
                            {
                                customInfo.Append(string.Format(" -{0}" + Environment.NewLine, blockData.Block.BlockName()));
                            }

                            cnt++;
                            if (cnt >= SyncBlockState.MaxSyncItems)
                            {
                                customInfo.Append(Texts.Info_More + Environment.NewLine);
                                break;
                            }
                        }
                        customInfo.Append(Environment.NewLine);
                    }
                }

                cnt = 0;

                lock (State.PossibleFloatingTargets)
                {
                    if (State.PossibleFloatingTargets?.Count > 0)
                    {
                        customInfo.Append(Texts.Info_ItemsToCollect + Environment.NewLine);

                        foreach (var entityData in State.PossibleFloatingTargets)
                        {
                            customInfo.Append(string.Format(" -{0}" + Environment.NewLine, Logging.BlockName(entityData.Entity, Logging.BlockNameOptions.None, false)));
                            cnt++;
                            if (cnt >= SyncBlockState.MaxSyncItems)
                            {
                                customInfo.Append(Texts.Info_More + Environment.NewLine);
                                break;
                            }
                        }
                    }
                }
            }
            else
            {
                if (!_Welder.Enabled) customInfo.Append(Texts.Info_BlockSwitchedOff + Environment.NewLine);
                else if (!_Welder.IsFunctional) customInfo.Append(Texts.Info_BlockDamaged + Environment.NewLine);
                else if (!_Welder.IsWorking) customInfo.Append(Texts.Info_BlockUnpowered + Environment.NewLine);
            }
        }

        /// <summary>
        /// Check if block currently has been damaged by friendly(grinder)
        /// </summary>
        public bool IsFriendlyDamage(IMySlimBlock slimBlock)
        {
            return FriendlyDamage.ContainsKey(slimBlock);
        }

        /// <summary>
        /// Clear timedout friendly damaged blocks
        /// </summary>
        private void CleanupFriendlyDamage()
        {
            var playTime = MyAPIGateway.Session.ElapsedPlayTime;
            if (playTime.Subtract(_LastFriendlyDamageCleanup) > NanobotBuildAndRepairSystemMod.Settings.FriendlyDamageCleanup)
            {
                //Cleanup
                var timedout = new List<IMySlimBlock>();
                foreach (var entry in FriendlyDamage)
                {
                    if (entry.Value < playTime) timedout.Add(entry.Key);
                }
                for (var idx = timedout.Count - 1; idx >= 0; idx--)
                {
                    FriendlyDamage.Remove(timedout[idx]);
                }
                _LastFriendlyDamageCleanup = playTime;
            }
        }

        public WorkingState GetWorkingState()
        {
            if (!State.Ready) return WorkingState.NotReady;
            else if (State.Welding) return WorkingState.Welding;
            else if (State.NeedWelding)
            {
                if (State.MissingComponents.Count > 0) return WorkingState.MissingComponents;
                if (State.LimitsExceeded) return WorkingState.LimitsExceeded;
                return WorkingState.NeedWelding;
            }
            else if (State.Grinding) return WorkingState.Grinding;
            else if (State.NeedGrinding)
            {
                if (State.InventoryFull) return WorkingState.InventoryFull;
                return WorkingState.NeedGrinding;
            }
            return WorkingState.Idle;
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
            else if (target is Vector3D) return (Vector3D)target;
            return null;
        }

        /// <summary>
        /// Get a list of currently missing components (Scripting)
        /// </summary>
        /// <returns></returns>
        internal MemorySafeDictionary<VRage.Game.MyDefinitionId, int> GetMissingComponentsDict()
        {
            var dict = new MemorySafeDictionary<VRage.Game.MyDefinitionId, int>();
            lock (State.MissingComponents)
            {
                foreach (var item in State.MissingComponents)
                {
                    dict.Add(item.Key, item.Value);
                }
            }
            return dict;
        }

        /// <summary>
        /// Get a list of currently build/repairable blocks (Scripting)
        /// </summary>
        /// <returns></returns>
        internal MemorySafeList<VRage.Game.ModAPI.Ingame.IMySlimBlock> GetPossibleWeldTargetsList()
        {
            var list = new MemorySafeList<VRage.Game.ModAPI.Ingame.IMySlimBlock>();
            lock (State.PossibleWeldTargets)
            {
                foreach (var blockData in State.PossibleWeldTargets)
                {
                    if (!blockData.Ignore) list.Add(blockData.Block);
                }
            }
            return list;
        }

        /// <summary>
        /// Get a list of currently grind blocks (Scripting)
        /// </summary>
        /// <returns></returns>
        internal MemorySafeList<VRage.Game.ModAPI.Ingame.IMySlimBlock> GetPossibleGrindTargetsList()
        {
            var list = new MemorySafeList<VRage.Game.ModAPI.Ingame.IMySlimBlock>();
            lock (State.PossibleGrindTargets)
            {
                foreach (var blockData in State.PossibleGrindTargets)
                {
                    if (!blockData.Ignore) list.Add(blockData.Block);
                }
            }
            return list;
        }

        /// <summary>
        /// Get a list of currently collectable floating objects (Scripting)
        /// </summary>
        /// <returns></returns>
        internal MemorySafeList<VRage.Game.ModAPI.Ingame.IMyEntity> GetPossibleCollectingTargetsList()
        {
            var list = new MemorySafeList<VRage.Game.ModAPI.Ingame.IMyEntity>();
            lock (State.PossibleFloatingTargets)
            {
                foreach (var floatingData in State.PossibleFloatingTargets)
                {
                    if (!floatingData.Ignore) list.Add(floatingData.Entity);
                }
            }
            return list;
        }
    }
}
