using Sandbox.Common.ObjectBuilders;
using Sandbox.Definitions;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Ingame;
using SKONanobotBuildAndRepairSystem.Handlers;
using SKONanobotBuildAndRepairSystem.Helpers;
using SKONanobotBuildAndRepairSystem.Localization;
using SKONanobotBuildAndRepairSystem.Models;
using SKONanobotBuildAndRepairSystem.Utils;
using System;
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
using static SKONanobotBuildAndRepairSystem.Handlers.SafeZoneHandler;
using static SKONanobotBuildAndRepairSystem.Utils.UtilsInventory;
using IMyShipWelder = Sandbox.ModAPI.IMyShipWelder;
using IMyTerminalBlock = Sandbox.ModAPI.IMyTerminalBlock;
using MyInventoryItem = VRage.Game.ModAPI.Ingame.MyInventoryItem;

namespace SKONanobotBuildAndRepairSystem
{
    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_ShipWelder), false, "SELtdLargeNanobotBuildAndRepairSystem", "SELtdSmallNanobotBuildAndRepairSystem")]
    public class NanobotSystem : MyGameLogicComponent
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
        private const int MaxPossibleFloatingTargets = 256;

        public static readonly int COLLECT_FLOATINGOBJECTS_SIMULTANEOUSLY = 50;

        public static readonly MyDefinitionId ElectricityId = new MyDefinitionId(typeof(VRage.Game.ObjectBuilders.Definitions.MyObjectBuilder_GasProperties), "Electricity");

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

        private IMyShipWelder _Welder;
        private IMyInventory _TransportInventory;
        private NanobotSystemEffects _Effects = new NanobotSystemEffects();

        private bool _IsInit;
        private List<IMyInventory> _PossibleSources = new List<IMyInventory>();
        private HashSet<IMyInventory> _Ignore4Ingot = new HashSet<IMyInventory>();
        private HashSet<IMyInventory> _Ignore4Items = new HashSet<IMyInventory>();
        private HashSet<IMyInventory> _Ignore4Components = new HashSet<IMyInventory>();
        private Dictionary<string, int> _TempMissingComponents = new Dictionary<string, int>();

        private int _UpdateEffectsInterval;
        private bool _UpdateCustomInfoNeeded;
        private float _MaxTransportVolume;
        private int _ContinuouslyError;

        private TimeSpan _LastFriendlyDamageCleanup;
        private TimeSpan _LastSourceUpdate = -Mod.Settings.SourcesUpdateInterval;
        private TimeSpan _LastTargetsUpdate;
        private TimeSpan _UpdateCustomInfoLast;
        private TimeSpan _UpdatePowerSinkLast;

        private TimeSpan _CustomInfoUpdateLast;
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
        }

        /// <summary>
        ///
        /// </summary>
        private void Init()
        {
            if (_IsInit) return;
            if (_Welder.SlimBlock.IsProjected() || !_Welder.Synchronized) //Synchronized = !IsPreview
            {
                NeedsUpdate = MyEntityUpdateEnum.NONE;
                return;
            }

            // Register this block to the nanobot systems.
            lock (Mod.NanobotSystems)
            {
                if (!Mod.NanobotSystems.ContainsKey(Entity.EntityId))
                {
                    Mod.NanobotSystems.Add(Entity.EntityId, this);
                }
            }

            // Initialize controls.
            Mod.InitControls();

            // TODO register/unregister events.
            _onEnabledChanged += (block) =>
            {
                UpdateCustomInfo(true);
            };

            _onIsWorkingChanged += (block) =>
            {
                UpdateCustomInfo(true);
            };

            _Welder.EnabledChanged += _onEnabledChanged;
            _Welder.IsWorkingChanged += _onIsWorkingChanged;

            // Set transport Inventory.
            var welderInventory = _Welder.GetInventory(0);
            if (welderInventory == null) return;
            _TransportInventory = new Sandbox.Game.MyInventory((float)welderInventory.MaxVolume / MyAPIGateway.Session.BlocksInventorySizeMultiplier, Vector3.MaxValue, MyInventoryFlags.CanSend);

            // Trigger settings changed.
            SettingsChanged();

            // Set Effects Emitter Position.
            // TODO: move this to effects class. InitEmitterPosition(...)
            var dummies = new Dictionary<string, IMyModelDummy>();
            _Welder.Model.GetDummies(dummies);
            foreach (var dummy in dummies)
            {
                if (dummy.Key.ToLower().Contains("detector_emitter"))
                {
                    _Effects.EmitterPosition = dummy.Value.Matrix.Translation;
                    break;
                }
            }
            
            NetworkMessagingHandler.MsgBlockDataRequestSend(this);

            if(MyAPIGateway.Session.IsServer)
            {
                SetSafeZoneAndShieldStates();
                NetworkMessagingHandler.MsgBlockStateSend(0, this);
            }            

            UpdateCustomInfo(true);

            _TryPushInventoryLast = MyAPIGateway.Session.ElapsedPlayTime.Add(TimeSpan.FromSeconds(10));
            _TryAutoPushInventoryLast = _TryPushInventoryLast;
            _Effects.WorkingStateSet = WorkingState.Invalid;
            _Effects.SoundVolumeSet = -1;
            _IsInit = true;
        }

        private float ComputeRequiredElectricPower()
        {
            return PowerHelper.ComputeRequiredElectricPower(this);
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

                ServerEmptyTranportInventory(true);

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

                    //Debug draw target boxes
                    //lock (_PossibleWeldTargets)
                    //{
                    //   var colorWelder = _Welder.SlimBlock.GetColorMask().HSVtoColor();
                    //   var color = Color.FromNonPremultiplied(colorWelder.R, colorWelder.G, colorWelder.B, 255);

                    //   foreach (var targetData in _PossibleWeldTargets)
                    //   {
                    //      BoundingBoxD box;
                    //      Vector3 halfExtents;
                    //      targetData.Block.ComputeScaledHalfExtents(out halfExtents);
                    //      halfExtents *= 1.2f;
                    //      var matrix = targetData.Block.CubeGrid.WorldMatrix;
                    //      matrix.Translation = targetData.Block.CubeGrid.GridIntegerToWorld(targetData.Block.Position);

                    //      box = new BoundingBoxD(-(halfExtents), (halfExtents));
                    //      MySimpleObjectDraw.DrawTransparentBox(ref matrix, ref box, ref color, MySimpleObjectRasterizer.Solid, 1, 0.04f, "HoneyComb", null, false);
                    //   }
                    //}

                    _UpdateEffectsInterval = (++_UpdateEffectsInterval) % 2;
                    if (_UpdateEffectsInterval == 0) _Effects.UpdateEffects(this);
                }
            }
            catch (Exception ex)
            {
                if (Logging.Instance.ShouldLog(Logging.Level.Error))
                {
                    Logging.Instance.Write(Logging.Level.Error, "BuildAndRepairSystemBlock {0}: UpdateBeforeSimulation Exception:{1}", Logging.BlockName(_Welder, Logging.BlockNameOptions.None), ex);
                }
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
            if (_IsInit)
            {
                Settings.Save(Entity, Mod.ModGuid);
            }

            // Stop sound effects
            _Effects.StopSoundEffects();
            _Effects.WorkingStateSet = WorkingState.Invalid;

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
                    if (!fast)
                    {
                        CleanupFriendlyDamage();                       
                    }

                    ServerTryWeldingGrindingCollecting();

                    if (!fast)
                    {
                        if (State.Ready && MyAPIGateway.Session.ElapsedPlayTime.Subtract(_UpdatePowerSinkLast).TotalSeconds >= 3)
                        {
                            _UpdatePowerSinkLast = MyAPIGateway.Session.ElapsedPlayTime;
                            var resourceSink = _Welder.ResourceSink as Sandbox.Game.EntityComponents.MyResourceSinkComponent;
                            if (resourceSink != null)
                            {
                                resourceSink.Update();
                            }
                        }

                        Settings.TrySave(Entity, Mod.ModGuid);

                        if (State.IsTransmitNeeded())
                        {
                            NetworkMessagingHandler.MsgBlockStateSend(0, this);
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
                    else
                    {
                        if (State.Ready && MyAPIGateway.Session.ElapsedPlayTime.Subtract(_CustomInfoUpdateLast).TotalSeconds >= 5)
                        {
                            _CustomInfoUpdateLast = MyAPIGateway.Session.ElapsedPlayTime;
                            UpdateCustomInfo(true);
                        }
                    }
                }                

                if (Settings.IsTransmitNeeded())
                {
                    NetworkMessagingHandler.MsgBlockSettingsSend(0, this);
                }

                if (_UpdateCustomInfoNeeded) UpdateCustomInfo(false);

                _DelayWatch.Stop();
                if (_DelayWatch.ElapsedMilliseconds > 40)
                {
                    _Delay = _RandomDelay.Next(10, 40); //Slowdown a little bit
                }
            }
            catch (Exception ex)
            {
                if (Logging.Instance.ShouldLog(Logging.Level.Error))
                {
                    Logging.Instance.Write(Logging.Level.Error, "BuildAndRepairSystemBlock {0}: UpdateBeforeSimulation10/100 Exception:{1}", Logging.BlockName(_Welder, Logging.BlockNameOptions.None), ex);
                }
            }
        }

        private bool SetSafeZoneAndShieldStates()
        {
            var safezoneAllowsWelding = SafeZoneHandler.IsActionAllowedForSystem(this, SafeZoneAction.Welding);
            var safeZoneAllowsBuildingProjections = SafeZoneHandler.IsActionAllowedForSystem(this, SafeZoneAction.BuildingProjections);
            var safeZoneAllowsGrinding = SafeZoneHandler.IsActionAllowedForSystem(this, SafeZoneAction.Grinding);
            var welderIsShielded = IsWelderShielded();
            var changed = false;

            if (State.SafeZoneAllowsWelding != safezoneAllowsWelding)
            {
                State.SafeZoneAllowsWelding = safezoneAllowsWelding;
                changed = true;
            }

            if (State.SafeZoneAllowsBuildingProjections != safeZoneAllowsBuildingProjections)
            {
                State.SafeZoneAllowsBuildingProjections = safeZoneAllowsBuildingProjections;
                changed = true;
            }

            if (State.SafeZoneAllowsGrinding != safeZoneAllowsGrinding)
            {
                State.SafeZoneAllowsGrinding = safeZoneAllowsGrinding;
                changed = true;
            }

            if (State.IsShielded != welderIsShielded)
            {
                State.IsShielded = welderIsShielded;
                changed = true;
            }

            return changed;
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
            var safeZoneStateChanged = false;

            var ready = _Welder.Enabled && _Welder.IsWorking && _Welder.IsFunctional;

            IMySlimBlock currentWeldingBlock = null;
            IMySlimBlock currentGrindingBlock = null;

            var playTime = MyAPIGateway.Session.ElapsedPlayTime;
            var isFullInventoryAndPicking = State.InventoryFull && State.CurrentTransportIsPick;

            if (ready)
            {
                if (MyAPIGateway.Session.ElapsedPlayTime.Subtract(_PeriodicExtraChecksLast).TotalSeconds >= 3)
                {
                    _PeriodicExtraChecksLast = MyAPIGateway.Session.ElapsedPlayTime;
                    try {

                        if (SetSafeZoneAndShieldStates())
                        {
                            safeZoneStateChanged = true;
                        } 
                    } catch { };                    
                }

                ServerTryPushInventory();

                if (isFullInventoryAndPicking)
                {
                    State.LastTransportTarget = State.CurrentTransportTarget;
                    State.CurrentTransportTarget = null;
                    transporting = false;
                }
                else
                {
                    transporting = IsTransportRunnning(playTime);
                }

                if (transporting && State.CurrentTransportIsPick) needgrinding = true;
                if ((Settings.Flags & SyncBlockSettings.Settings.ComponentCollectIfIdle) == 0 && !transporting) ServerTryCollectingFloatingTargets(out collecting, out needcollecting, out transporting);

                if (!transporting)
                {
                    State.MissingComponents.Clear();
                    State.LimitsExceeded = false;
                    switch (Settings.WorkMode)
                    {
                        case WorkModes.WeldBeforeGrind:
                            ServerTryWelding(out welding, out needwelding, out transporting, out currentWeldingBlock);
                            if (State.PossibleWeldTargets.CurrentCount == 0 || (((Settings.Flags & SyncBlockSettings.Settings.ScriptControlled) != 0) && Settings.CurrentPickedGrindingBlock != null))
                            {
                                ServerTryGrinding(out grinding, out needgrinding, out transporting, out currentGrindingBlock);
                            }
                            break;

                        case WorkModes.GrindBeforeWeld:
                            ServerTryGrinding(out grinding, out needgrinding, out transporting, out currentGrindingBlock);
                            if (State.PossibleGrindTargets.CurrentCount == 0 || (((Settings.Flags & SyncBlockSettings.Settings.ScriptControlled) != 0) && Settings.CurrentPickedWeldingBlock != null))
                            {
                                ServerTryWelding(out welding, out needwelding, out transporting, out currentWeldingBlock);
                            }
                            break;

                        case WorkModes.GrindIfWeldGetStuck:
                            ServerTryWelding(out welding, out needwelding, out transporting, out currentWeldingBlock);
                            if (!(welding || transporting) || (((Settings.Flags & SyncBlockSettings.Settings.ScriptControlled) != 0) && Settings.CurrentPickedGrindingBlock != null))
                            {
                                ServerTryGrinding(out grinding, out needgrinding, out transporting, out currentGrindingBlock);
                            }
                            break;

                        case WorkModes.WeldOnly:
                            ServerTryWelding(out welding, out needwelding, out transporting, out currentWeldingBlock);
                            break;

                        case WorkModes.GrindOnly:
                            ServerTryGrinding(out grinding, out needgrinding, out transporting, out currentGrindingBlock);
                            break;
                    }
                    State.MissingComponents.RebuildHash();
                }

                if (((Settings.Flags & SyncBlockSettings.Settings.ComponentCollectIfIdle) != 0) && !transporting && !welding && !grinding)
                    ServerTryCollectingFloatingTargets(out collecting, out needcollecting, out transporting);
            }
            else
            {
                if (isFullInventoryAndPicking)
                {
                    State.LastTransportTarget = State.CurrentTransportTarget;
                    State.CurrentTransportTarget = null;
                    transporting = false;
                }
                else
                {
                    transporting = IsTransportRunnning(playTime); //Finish running transport
                }

                State.MissingComponents.Clear();
                State.MissingComponents.RebuildHash();
            }

            if (!(welding || grinding || collecting || transporting) && _TransportInventory.CurrentVolume > 0)
            {
                // Idle but not empty -> empty inventory
                if (!isFullInventoryAndPicking && State.LastTransportTarget.HasValue)
                {
                    State.CurrentTransportIsPick = true;
                    State.CurrentTransportTarget = State.LastTransportTarget;
                    State.CurrentTransportStartTime = playTime;

                    transporting = true;
                }

                if (ready)
                    ServerEmptyTranportInventory(true);
            }

            if (((State.Welding && !welding) || (State.Grinding && !(grinding || collecting))))
            {
                if (!isFullInventoryAndPicking && ready)
                {
                    StartAsyncUpdateSourcesAndTargets(false); //Scan immediately once for new targets
                }
            }

            var readyChanged = State.Ready != ready;
            State.Ready = ready;
            State.Welding = welding;
            State.NeedWelding = needwelding;
            State.CurrentWeldingBlock = currentWeldingBlock;

            State.Grinding = grinding;
            State.NeedGrinding = needgrinding;
            State.CurrentGrindingBlock = currentGrindingBlock;

            var transportChanged = State.Transporting != transporting;
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

            if (missingComponentsChanged || possibleWeldTargetsChanged || possibleGrindTargetsChanged || possibleFloatingTargetsChanged || transportChanged || safeZoneStateChanged) State.HasChanged();

            if (MyAPIGateway.Session.IsServer)
            {
                if (State.IsTransmitNeeded() && MyAPIGateway.Multiplayer.MultiplayerActive)
                {
                    NetworkMessagingHandler.MsgBlockStateSend(0, this);
                }
            }

            UpdateCustomInfo(
                missingComponentsChanged ||
                possibleWeldTargetsChanged ||
                possibleGrindTargetsChanged ||
                possibleFloatingTargetsChanged ||
                readyChanged ||
                inventoryFullChanged ||
                limitsExceededChanged ||
                transportChanged ||
                safeZoneStateChanged);
        }

        /// <summary>
        /// Push ore/ingot out of the welder
        /// </summary>
        private void ServerTryPushInventory()
        {
            if ((Settings.Flags & (SyncBlockSettings.Settings.PushIngotOreImmediately | SyncBlockSettings.Settings.PushComponentImmediately | SyncBlockSettings.Settings.PushItemsImmediately)) == 0)
                return;

            if (MyAPIGateway.Session.ElapsedPlayTime.Subtract(_TryAutoPushInventoryLast).TotalSeconds <= 5)
                return;

            var welderInventory = _Welder.GetInventory(0);
            if (welderInventory != null)
            {
                if (welderInventory.Empty()) return;
                var lastPush = MyAPIGateway.Session.ElapsedPlayTime;

                var tempInventoryItems = new List<MyInventoryItem>();
                welderInventory.GetItems(tempInventoryItems);
                for (int srcItemIndex = tempInventoryItems.Count - 1; srcItemIndex >= 0; srcItemIndex--)
                {
                    var srcItem = tempInventoryItems[srcItemIndex];
                    if (srcItem.Type.TypeId == typeof(MyObjectBuilder_Ore).Name || srcItem.Type.TypeId == typeof(MyObjectBuilder_Ingot).Name)
                    {
                        if ((Settings.Flags & SyncBlockSettings.Settings.PushIngotOreImmediately) != 0)
                        {
                            welderInventory.PushComponents(_PossibleSources, (IMyInventory destInventory, IMyInventory srcInventory, ref MyInventoryItem srcItemIn) => { return _Ignore4Ingot.Contains(destInventory); }, srcItemIndex, srcItem);
                            _TryAutoPushInventoryLast = lastPush;
                        }
                    }
                    else if (srcItem.Type.TypeId == typeof(MyObjectBuilder_Component).Name)
                    {
                        if ((Settings.Flags & SyncBlockSettings.Settings.PushComponentImmediately) != 0)
                        {
                            welderInventory.PushComponents(_PossibleSources, (IMyInventory destInventory, IMyInventory srcInventory, ref MyInventoryItem srcItemIn) => { return _Ignore4Components.Contains(destInventory); }, srcItemIndex, srcItem);
                            _TryAutoPushInventoryLast = lastPush;
                        }
                    }
                    else
                    {
                        //Any kind of items (Tools, Weapons, Ammo, Bottles, ..)
                        if ((Settings.Flags & SyncBlockSettings.Settings.PushItemsImmediately) != 0)
                        {
                            welderInventory.PushComponents(_PossibleSources, (IMyInventory destInventory, IMyInventory srcInventory, ref MyInventoryItem srcItemIn) => { return _Ignore4Items.Contains(destInventory); }, srcItemIndex, srcItem);
                            _TryAutoPushInventoryLast = lastPush;
                        }
                    }
                }
                tempInventoryItems.Clear();
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
            if (!PowerHelper.HasRequiredElectricPower(this)) return; //-> Not enought power
            lock (State.PossibleFloatingTargets)
            {
                TargetEntityData collectingFirstTarget = null;
                var collectingCount = 0;
                foreach (var targetData in State.PossibleFloatingTargets)
                {
                    if (targetData.Entity != null && !targetData.Ignore)
                    {
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
        private void ServerTryGrinding(out bool grinding, out bool needgrinding, out bool transporting, out IMySlimBlock currentGrindingBlock)
        {
            grinding = false;
            needgrinding = false;
            transporting = false;
            currentGrindingBlock = null;

            if (State.InventoryFull)
                return;

            if (!PowerHelper.HasRequiredElectricPower(this)) return; //No power -> nothing to do

            lock (State.PossibleGrindTargets)
            {
                foreach (var targetData in State.PossibleGrindTargets)
                {
                    var cubeGrid = targetData.Block.CubeGrid as MyCubeGrid;
                    if (!cubeGrid.IsPowered && !cubeGrid.IsStatic) cubeGrid.Physics.ClearSpeed();
                }

                foreach (var targetData in State.PossibleGrindTargets)
                {
                    if (((Settings.Flags & SyncBlockSettings.Settings.ScriptControlled) != 0) && targetData.Block != Settings.CurrentPickedGrindingBlock) continue;

                    if (!targetData.Block.IsDestroyed)
                    {
                        needgrinding = true;
                        grinding = ServerDoGrind(targetData, out transporting);
                        if (grinding)
                        {
                            currentGrindingBlock = targetData.Block;
                            break; //Only grind one block at once
                        }
                    }
                }
            }

            // Faction reputation when grinding for not owned grids.
            if (currentGrindingBlock != null)
            {
                if (currentGrindingBlock.OwnerId != Welder.OwnerId && currentGrindingBlock.CubeGrid.EntityId != Welder.CubeGrid.EntityId)
                {
                    var ownerId = UtilsPlayer.GetOwner(currentGrindingBlock.CubeGrid as MyCubeGrid);
                    if (ownerId > 0 && ownerId != Welder.OwnerId)
                    {
                        UtilsFaction.DamageReputationWithPlayerFaction(Welder.OwnerId, ownerId);
                    }
                }
            }
        }

        /// <summary>
        ///
        /// </summary>
        private void ServerTryWelding(out bool welding, out bool needwelding, out bool transporting, out IMySlimBlock currentWeldingBlock)
        {
            welding = false;
            needwelding = false;
            transporting = false;
            currentWeldingBlock = null;

            var hasRequiredPower = PowerHelper.HasRequiredElectricPower(this);
            if (!hasRequiredPower) return; //No power -> nothing to do

            lock (State.PossibleWeldTargets)
            {
                foreach (var targetData in State.PossibleWeldTargets)
                {
                    if (((Settings.Flags & SyncBlockSettings.Settings.ScriptControlled) != 0) && targetData.Block != Settings.CurrentPickedWeldingBlock) continue;
                    if (((Settings.Flags & SyncBlockSettings.Settings.ScriptControlled) != 0) || (!targetData.Ignore && Weldable(targetData)))
                    {
                        needwelding = true;

                        if (!transporting) //Transport needs to be weld afterwards
                        {
                            transporting = ServerFindMissingComponents(targetData);
                        }

                        welding = ServerDoWeld(targetData);

                        ServerEmptyTranportInventory(false);

                        if (targetData.Ignore)
                            State.PossibleWeldTargets.ChangeHash();

                        if (welding)
                        {
                            currentWeldingBlock = targetData.Block;
                            break; //Only weld one block at once (do not split over all blocks as the base shipwelder does)
                        }
                    }
                }
            }
        }

        /// <summary>
        ///
        /// </summary>
        /// <param name="targetData"></param>
        /// <returns></returns>
        private bool Weldable(TargetBlockData targetData)
        {
            var target = targetData.Block;
            if ((targetData.Attributes & TargetBlockData.AttributeFlags.Projected) != 0)
            {
                if (target.CanBuild(true))
                    return true;

                // Is the block already created (maybe by user or an other BaR block) ->
                // After creation we can't welding this projected block, we have to find the 'physical' block instead.
                var cubeGridProjected = target.CubeGrid as MyCubeGrid;
                if (cubeGridProjected != null && cubeGridProjected.Projector != null)
                {
                    var cubeGrid = cubeGridProjected.Projector.CubeGrid;
                    var blockPos = cubeGrid.WorldToGridInteger(cubeGridProjected.GridIntegerToWorld(target.Position));
                    target = cubeGrid.GetCubeBlock(blockPos);

                    if (target != null)
                    {
                        targetData.Block = target;
                        targetData.Attributes &= ~TargetBlockData.AttributeFlags.Projected;
                        return Weldable(targetData);
                    }
                }
                targetData.Ignore = true;
                return false;
            }

            var needRepair = target.NeedRepair((Settings.WeldOptions & AutoWeldOptions.FunctionalOnly) != 0);
            var integrityReached = IsWeldIntegrityReached(target);
            var weld = (!integrityReached || needRepair) && !IsFriendlyDamage(target);

            targetData.Ignore = !weld;
            return weld;
        }

        internal bool IsWeldIntegrityReached(IMySlimBlock target)
        {
            try
            {
                var isFunctionalOnly = (Settings.WeldOptions & AutoWeldOptions.FunctionalOnly) != 0;
                if (!isFunctionalOnly)
                {
                    return target.IsFullIntegrity;
                }

                var requiredIntegrity = target.GetRequiredIntegrity(isFunctionalOnly);

                return target.Integrity >= requiredIntegrity;
            }
            catch
            {
                // If something goes wrong, lets say its all built to avoid issues!
                return true;
            }
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
                // Transport started
                if (State.CurrentTransportIsPick)
                {
                    if (!ServerEmptyTranportInventory(true))
                    {
                        return true;
                    }
                }

                if (playTime.Subtract(State.CurrentTransportStartTime) < State.CurrentTransportTime)
                {
                    // Last transport still running -> wait
                    return true;
                }

                State.CurrentTransportStartTime = TimeSpan.Zero;
                State.LastTransportTarget = State.CurrentTransportTarget;
                State.CurrentTransportTarget = null;
            }
            else State.CurrentTransportTarget = null;
            return false;
        }

        private void UpdateCustomInfo(bool changed)
        {
            _UpdateCustomInfoNeeded |= changed;
            var playTime = MyAPIGateway.Session.ElapsedPlayTime;
            if (_UpdateCustomInfoNeeded && (playTime.Subtract(_UpdateCustomInfoLast).TotalSeconds >= 1))
            {
                _Welder.RefreshCustomInfo();

                TriggerTerminalRefresh();

                _UpdateCustomInfoLast = playTime;
                _UpdateCustomInfoNeeded = false;
            }
        }

        public void TriggerTerminalRefresh()
        {
            //Workaround as long as RaisePropertiesChanged is not public
            if (_Welder != null)
            {
                try
                {
                    var action = _Welder.GetActionWithName("helpOthers");
                    if (action != null)
                    {
                        action.Apply(_Welder);
                        action.Apply(_Welder);
                    }
                }
                catch (Exception)
                {
                }
            }
        }

        private bool ServerDoWeld(TargetBlockData targetData)
        {
            var welderInventory = _Welder.GetInventory(0);
            var welding = false;
            var created = false;
            var target = targetData.Block;
            var hasIgnoreColor = ((Settings.Flags & SyncBlockSettings.Settings.UseIgnoreColor) != 0) && IsColorNearlyEquals(Settings.IgnoreColorPacked, target.GetColorMask());

            if ((targetData.Attributes & TargetBlockData.AttributeFlags.Projected) != 0)
            {
                //New Block (Projected)
                var cubeGridProjected = target.CubeGrid as MyCubeGrid;
                var blockDefinition = target.BlockDefinition as MyCubeBlockDefinition;
                var item = _TransportInventory.FindItem(blockDefinition.Components[0].Definition.Id);
                if (item != null && item.Amount >= 1 && cubeGridProjected != null && cubeGridProjected.Projector != null)
                {
                    if (_Welder.IsWithinWorldLimits(cubeGridProjected.Projector, blockDefinition.BlockPairName, blockDefinition.PCU))
                    {
                        if (!cubeGridProjected.Projector.Closed && !cubeGridProjected.Projector.CubeGrid.Closed && (target.FatBlock == null || !target.FatBlock.Closed))
                        {
                            ((Sandbox.ModAPI.IMyProjector)cubeGridProjected.Projector).Build(target, _Welder.OwnerId, _Welder.EntityId, true, _Welder.SlimBlock.BuiltBy);
                        }

                        _TransportInventory.RemoveItems(item.ItemId, 1);

                        //After creation we can't welding this projected block, we have to find the 'physical' block instead.
                        var cubeGrid = cubeGridProjected.Projector.CubeGrid;
                        Vector3I blockPos = cubeGrid.WorldToGridInteger(cubeGridProjected.GridIntegerToWorld(target.Position));
                        target = cubeGrid.GetCubeBlock(blockPos);
                        if (target != null) targetData.Block = target;
                        targetData.Attributes &= ~TargetBlockData.AttributeFlags.Projected;
                        created = true;
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
                    welding = target.CanContinueBuild(_TransportInventory);

                    if (welding)
                    {
                        target.IncreaseMountLevel(MyAPIGateway.Session.WelderSpeedMultiplier * Mod.Settings.Welder.WeldingMultiplier * WELDER_AMOUNT_PER_SECOND, _Welder.OwnerId, welderInventory, MyAPIGateway.Session.WelderSpeedMultiplier * Mod.Settings.Welder.WeldingMultiplier * WELDER_MAX_REPAIR_BONE_MOVEMENT_SPEED, _Welder.HelpOthers);
                    }
                    if (target.IsFullIntegrity || (((Settings.WeldOptions & AutoWeldOptions.FunctionalOnly) != 0) && target.Integrity >= target.MaxIntegrity * ((MyCubeBlockDefinition)target.BlockDefinition).CriticalIntegrityRatio))
                    {
                        targetData.Ignore = true;
                    }
                }
                else
                {
                    //Deformation
                    welding = true;
                    target.IncreaseMountLevel(MyAPIGateway.Session.WelderSpeedMultiplier * Mod.Settings.Welder.WeldingMultiplier * WELDER_AMOUNT_PER_SECOND, _Welder.OwnerId, welderInventory, MyAPIGateway.Session.WelderSpeedMultiplier * Mod.Settings.Welder.WeldingMultiplier * WELDER_MAX_REPAIR_BONE_MOVEMENT_SPEED, _Welder.HelpOthers);
                }
            }
            return welding || created;
        }

        private bool ServerDoGrind(TargetBlockData targetData, out bool transporting)
        {
            var target = targetData.Block;
            var playTime = MyAPIGateway.Session.ElapsedPlayTime;
            transporting = IsTransportRunnning(playTime);
            if (transporting) return false;

            // var welderInventory = _Welder.GetInventory(0);
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

            var disassembleRatio = target.FatBlock != null ? target.FatBlock.DisassembleRatio : ((MyCubeBlockDefinition)target.BlockDefinition).DisassembleRatio;
            var integrityPointsPerSec = ((MyCubeBlockDefinition)target.BlockDefinition).IntegrityPointsPerSec;

            float damage = MyAPIGateway.Session.GrinderSpeedMultiplier * Mod.Settings.Welder.GrindingMultiplier * GRINDER_AMOUNT_PER_SECOND;
            var grinderAmount = damage * integrityPointsPerSec / disassembleRatio;
            integrityRatio = (target.Integrity - grinderAmount) / target.MaxIntegrity;

            if ((targetData.Attributes & TargetBlockData.AttributeFlags.Autogrind) != 0)
            {
                if ((Settings.GrindJanitorOptions & AutoGrindOptions.DisableOnly) != 0 && integrityRatio < criticalIntegrityRatio)
                {
                    //Grind only down to critical ratio not further
                    grinderAmount = target.Integrity - (0.9f * criticalIntegrityRatio * target.MaxIntegrity);
                    damage = grinderAmount * disassembleRatio / integrityPointsPerSec;
                    integrityRatio = criticalIntegrityRatio;
                }
                else if ((Settings.GrindJanitorOptions & AutoGrindOptions.HackOnly) != 0 && integrityRatio < ownershipIntegrityRatio)
                {
                    //Grind only down to ownership ratio not further
                    grinderAmount = target.Integrity - (0.9f * ownershipIntegrityRatio * target.MaxIntegrity);
                    damage = grinderAmount * disassembleRatio / integrityPointsPerSec;
                    integrityRatio = ownershipIntegrityRatio;
                }
            }

            var emptying = false;
            bool isEmpty = false;

            if (integrityRatio <= 0.2)
            {
                //Try to emtpy inventory (if any)
                if (target.FatBlock != null && target.FatBlock.HasInventory)
                {
                    emptying = EmptyBlockInventories(target.FatBlock, _TransportInventory, out isEmpty);
                }
            }

            if (!emptying || isEmpty)
            {
                MyDamageInformation damageInfo = new MyDamageInformation(false, damage, MyDamageType.Grind, _Welder.EntityId);

                if (target.UseDamageSystem)
                {
                    //Not available in modding
                    //MyAPIGateway.Session.DamageSystem.RaiseBeforeDamageApplied(target, ref damageInfo);

                    foreach (var entry in Mod.NanobotSystems)
                    {
                        var relation = entry.Value.Welder.GetUserRelationToOwner(_Welder.OwnerId);
                        if (MyRelationsBetweenPlayerAndBlockExtensions.IsFriendly(relation))
                        {
                            //A 'friendly' damage from grinder -> do not repair (for a while)
                            //I don't check block relation here, because if it is enemy we won't repair it in any case and it just times out
                            entry.Value.FriendlyDamage[target] = MyAPIGateway.Session.ElapsedPlayTime + Mod.Settings.FriendlyDamageTimeout;
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

                ServerEmptyTranportInventory(true);
                transporting = true;
            }

            return true;
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

            if (State.InventoryFull)
                return false;

            var playTime = MyAPIGateway.Session.ElapsedPlayTime;
            transporting = IsTransportRunnning(playTime);

            if (transporting) return false;
            if (targetData != null)
            {
                var target = targetData.Entity;
                var floating = target as MyFloatingObject;
                var floatingFirstTarget = collectingFirstTarget != null ? collectingFirstTarget.Entity as MyFloatingObject : null;

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
                                // TODO: collect stuff from them, like meat.
                                //Wolf, Spider, ...
                                target.Delete();
                            }
                        }
                    }

                    if (collecting && collectingFirstTarget == null) collectingFirstTarget = targetData;

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

                ServerEmptyTranportInventory(true);
                transporting = true;
                collectingFirstTarget = null;
            }

            return collecting;
        }

        /// <summary>
        /// Try to find an the missing components and moves them into welder inventory
        /// </summary>
        private bool ServerFindMissingComponents(TargetBlockData targetData)
        {
            try
            {
                var playTime = MyAPIGateway.Session.ElapsedPlayTime;

                if (IsTransportRunnning(playTime))
                    return true;

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
                            if (((Settings.Flags & SyncBlockSettings.Settings.UseIgnoreColor) == 0) || !IsColorNearlyEquals(Settings.IgnoreColorPacked, targetData.Block.GetColorMask()))
                            {
                                //Block could be created and should be welded -> so retrieve the remaining material also
                                var keyValue = _TempMissingComponents.ElementAt(0);
                                _TempMissingComponents.Clear();
                                targetData.Block.GetMissingComponents(_TempMissingComponents, ((Settings.WeldOptions & AutoWeldOptions.FunctionalOnly) == 0) ? UtilsInventory.IntegrityLevel.Complete : UtilsInventory.IntegrityLevel.Functional);
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
                    targetData.Block.GetMissingComponents(_TempMissingComponents, ((Settings.WeldOptions & AutoWeldOptions.FunctionalOnly) == 0) ? UtilsInventory.IntegrityLevel.Complete : UtilsInventory.IntegrityLevel.Functional);
                }

                if (_TempMissingComponents.Count > 0)
                {
                    ServerFindMissingComponents(targetData, ref remainingVolume);
                }

                if (remainingVolume < _MaxTransportVolume)
                {
                    //Transport startet
                    State.CurrentTransportIsPick = false;
                    State.CurrentTransportTarget = ComputePosition(targetData.Block);
                    State.CurrentTransportStartTime = playTime;
                    State.CurrentTransportTime = TimeSpan.FromSeconds(2d * targetData.Distance / Settings.TransportSpeed);

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
                int neededAmount = 0;
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
            var picked = false;

            var welderInventory = _Welder.GetInventory(0);
            if (welderInventory == null || welderInventory.Empty())
            {
                return picked;
            }

            var tempInventoryItems = new List<MyInventoryItem>();
            welderInventory.GetItems(tempInventoryItems);
            for (int i1 = tempInventoryItems.Count - 1; i1 >= 0; i1--)
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
                }
                if (neededAmount <= 0 || remainingVolume <= 0) break;
            }
            tempInventoryItems.Clear();
            return picked;
        }

        /// <summary>
        /// Check if the transport inventory is empty after delivering/grinding/collecting, if not move items back to welder inventory
        /// </summary>
        private bool ServerEmptyTranportInventory(bool push)
        {
            var empty = _TransportInventory.Empty();
            if (!empty)
            {
                var welderInventory = _Welder.GetInventory(0);
                if (welderInventory != null)
                {
                    if (push && !welderInventory.Empty())
                    {
                        if (MyAPIGateway.Session.ElapsedPlayTime.Subtract(_TryPushInventoryLast).TotalSeconds > 5 && welderInventory.MaxVolume - welderInventory.CurrentVolume < _TransportInventory.CurrentVolume * 1.5f)
                        {
                            if (!welderInventory.PushComponents(_PossibleSources, null))
                            {
                                // Failed retry after timeout
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

                        // Try to move as much as possible
                        var amount = item.Amount;
                        var moveableAmount = welderInventory.MaxItemsAddable(amount, item.Type);
                        if (moveableAmount > 0)
                        {
                            if (welderInventory.TransferItemFrom(_TransportInventory, srcItemIndex, null, true, moveableAmount, false))
                            {
                                amount -= moveableAmount;
                            }
                        }
                    }

                    tempInventoryItems.Clear();
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
            var remainingVolume = _MaxTransportVolume - (float)dstInventory.CurrentVolume;
            isEmpty = true;

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

                    var definition = MyDefinitionManager.Static.GetPhysicalItemDefinition(srcItem.Content.GetId());

                    var maxpossibleAmountFP = Math.Min((float)srcItem.Amount, (remainingVolume / definition.Volume));
                    //Real Transport Volume is always bigger than logical _MaxTransportVolume so ceiling is no problem
                    var maxpossibleAmount = (MyFixedPoint)(definition.HasIntegralAmounts ? Math.Ceiling(maxpossibleAmountFP) : maxpossibleAmountFP);
                    if (dstInventory.TransferItemFrom(srcInventory, srcItemIndex, null, true, maxpossibleAmount, false))
                    {
                        remainingVolume -= (float)maxpossibleAmount * definition.Volume;
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
            int availAmount = 0;
            var welderInventory = _Welder.GetInventory(0);
            var maxpossibleAmount = Math.Min(neededAmount, (int)Math.Ceiling(remainingVolume / volume));

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
                        for (int srcItemIndex = tempInventoryItems.Count - 1; srcItemIndex >= 0; srcItemIndex--)
                        {
                            var srcItem = tempInventoryItems[srcItemIndex];
                            if (srcItem != null && (MyDefinitionId)srcItem.Type == componentId && srcItem.Amount > 0)
                            {
                                var moved = false;
                                var amountMoveable = 0;
                                var amountPossible = Math.Min(maxpossibleAmount, (int)srcItem.Amount);

                                if (amountPossible > 0)
                                {
                                    amountMoveable = (int)welderInventory.MaxItemsAddable(amountPossible, componentId);
                                    if (amountMoveable > 0)
                                    {
                                        moved = welderInventory.TransferItemFrom(srcInventory, srcItemIndex, null, true, amountMoveable);
                                        if (moved)
                                        {
                                            maxpossibleAmount -= amountMoveable;
                                            availAmount += amountMoveable;
                                            picked = ServerPickFromWelder(componentId, volume, ref neededAmount, ref remainingVolume) || picked;
                                        }
                                    }
                                    else
                                    {
                                        //No (more) space in welder
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
            var updateTargets = playTime.Subtract(_LastTargetsUpdate) >= Mod.Settings.TargetsUpdateInterval;
            var updateSources = updateTargets && playTime.Subtract(_LastSourceUpdate) >= Mod.Settings.SourcesUpdateInterval;
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
            if (!_Welder.UseConveyorSystem)
            {
                lock (_PossibleSources)
                {
                    _PossibleSources.Clear();
                }
            }

            if (!_Welder.Enabled || !_Welder.IsFunctional || State.Ready == false)
            {
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
            ;

            lock (_Welder)
            {
                if (_AsyncUpdateSourcesAndTargetsRunning) return;

                _AsyncUpdateSourcesAndTargetsRunning = true;
                Mod.AddAsyncAction(() => AsyncUpdateSourcesAndTargets(updateSource));
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

                var weldingEnabled = BlockWeldPriority.AnyEnabled && Settings.WorkMode != WorkModes.GrindOnly;
                var grindingEnabled = BlockGrindPriority.AnyEnabled && Settings.WorkMode != WorkModes.WeldOnly;

                updateSource &= _Welder.UseConveyorSystem;
                int pos = 0;

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

                    AsyncAddBlocksOfGrid(ref areaOrientedBox, ((Settings.Flags & SyncBlockSettings.Settings.UseIgnoreColor) != 0), ref ignoreColor, ((Settings.Flags & SyncBlockSettings.Settings.UseGrindColor) != 0), ref grindColor, Settings.UseGrindJanitorOn, Settings.GrindJanitorOptions, _Welder.CubeGrid, grids, updateSource ? _TempPossibleSources : null, weldingEnabled ? _TempPossibleWeldTargets : null, grindingEnabled ? _TempPossibleGrindTargets : null);

                    switch (Settings.SearchMode)
                    {
                        case SearchModes.Grids:
                            break;

                        case SearchModes.BoundingBox:
                            AsyncAddBlocksOfBox(ref areaOrientedBox, ((Settings.Flags & SyncBlockSettings.Settings.UseIgnoreColor) != 0), ref ignoreColor, ((Settings.Flags & SyncBlockSettings.Settings.UseGrindColor) != 0), ref grindColor, Settings.UseGrindJanitorOn, Settings.GrindJanitorOptions, grids, weldingEnabled ? _TempPossibleWeldTargets : null, grindingEnabled ? _TempPossibleGrindTargets : null, _ComponentCollectPriority.AnyEnabled ? _TempPossibleFloatingTargets : null);
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
                                if ((welderA == null) == (welderB == null))
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
                                var bar = block.GameLogic.GetAs<NanobotSystem>();

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
                            return Utils.Utils.CompareDistance(a.Distance, b.Distance);
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
                                    if (((Settings.Flags & SyncBlockSettings.Settings.GrindSmallestGridFirst) != 0))
                                    {
                                        var res = ((MyCubeGrid)a.Block.CubeGrid).BlocksCount - ((MyCubeGrid)b.Block.CubeGrid).BlocksCount;
                                        return res != 0 ? res : Utils.Utils.CompareDistance(a.Distance, b.Distance);
                                    }
                                    if (((Settings.Flags & SyncBlockSettings.Settings.GrindNearFirst) != 0)) return Utils.Utils.CompareDistance(a.Distance, b.Distance);
                                    return Utils.Utils.CompareDistance(b.Distance, a.Distance);
                                }
                                else return priorityA - priorityB;
                            }

                            if (((Settings.Flags & SyncBlockSettings.Settings.GrindSmallestGridFirst) != 0))
                            {
                                var res = ((MyCubeGrid)a.Block.CubeGrid).BlocksCount - ((MyCubeGrid)b.Block.CubeGrid).BlocksCount;
                                return res != 0 ? res : Utils.Utils.CompareDistance(a.Distance, b.Distance);
                            }
                            if (((Settings.Flags & SyncBlockSettings.Settings.GrindNearFirst) != 0)) return Utils.Utils.CompareDistance(a.Distance, b.Distance);
                            return Utils.Utils.CompareDistance(b.Distance, a.Distance);
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
                                return Utils.Utils.CompareDistance(a.Distance, b.Distance);
                            }
                            else return priorityA - priorityB;
                        }
                        else if (itemAFloating == null) return -1;
                        else if (itemBFloating == null) return 1;
                        return Utils.Utils.CompareDistance(a.Distance, b.Distance);
                    });

                    pos = 5;
                    // Removed logging.

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
                        Logging.Instance.Error("BuildAndRepairSystemBlock {0}: AsyncUpdateSourcesAndTargets exception at {1}: {2}", Logging.BlockName(_Welder, Logging.BlockNameOptions.None), pos, ex);
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
            var emitterMatrix = _Welder.WorldMatrix;
            emitterMatrix.Translation = Vector3D.Transform(Settings.CorrectedAreaOffset, emitterMatrix);
            var areaBoundingBox = Settings.CorrectedAreaBoundingBox.TransformFast(emitterMatrix);

            // TODO: Improve performance with chaching this.
            List<IMyEntity> entityInRange = null;
            lock (MyAPIGateway.Entities)
            {
                //API not thread save !!!
                entityInRange = MyAPIGateway.Entities.GetElementsInBox(ref areaBoundingBox);
                //The list contains grid, Fatblocks and Damaged blocks in range. But as I would like to use the searchfunction also for grinding,
                //I only could use the grids and have to traverse through the grids to get all slimblocks.
            }

            if (entityInRange != null)
            {
                foreach (var entity in entityInRange)
                {
                    if (ShouldStopScan(possibleWeldTargets, possibleGrindTargets, possibleFloatingTargets))
                    {
                        break;
                    }

                    var grid = entity as IMyCubeGrid;
                    if (grid != null)
                    {
                        AsyncAddBlocksOfGrid(ref areaBox, useIgnoreColor, ref ignoreColor, useGrindColor, ref grindColor, autoGrindRelation, autoGrindOptions, grid, grids, null, possibleWeldTargets, possibleGrindTargets);
                        continue;
                    }

                    if (possibleFloatingTargets != null)
                    {
                        var hasReachedMaxFloatingObjects = possibleFloatingTargets.Count >= MaxPossibleFloatingTargets;
                        if (hasReachedMaxFloatingObjects)
                        {
                            continue;
                        }

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

        /// <summary>
        ///
        /// </summary>
        private void AsyncAddBlocksOfGrid(ref MyOrientedBoundingBoxD areaBox, bool useIgnoreColor, ref uint ignoreColor, bool useGrindColor, ref uint grindColor, AutoGrindRelation autoGrindRelation, AutoGrindOptions autoGrindOptions, IMyCubeGrid cubeGrid, List<IMyCubeGrid> grids, List<IMyInventory> possibleSources, List<TargetBlockData> possibleWeldTargets, List<TargetBlockData> possibleGrindTargets)
        {
            if (!State.Ready) return; //Block not ready
            if (grids.Contains(cubeGrid)) return; //Allready parsed

            grids.Add(cubeGrid);

            // TODO: Improve performance by using a cached list to avoid repeated GetBlcoks.
            var newBlocks = new List<IMySlimBlock>();
            cubeGrid.GetBlocks(newBlocks);

            foreach (var slimBlock in newBlocks)
            {
                if (ShouldStopScan(possibleWeldTargets, possibleGrindTargets, null))
                {
                    break;
                }

                AsyncAddBlockIfTargetOrSource(ref areaBox, useIgnoreColor, ref ignoreColor, useGrindColor, ref grindColor, autoGrindRelation, autoGrindOptions, slimBlock, possibleSources, possibleWeldTargets, possibleGrindTargets);

                var fatBlock = slimBlock.FatBlock;
                if (fatBlock == null) continue;

                var mechanicalConnectionBlock = fatBlock as Sandbox.ModAPI.IMyMechanicalConnectionBlock;
                if (mechanicalConnectionBlock != null)
                {
                    if (mechanicalConnectionBlock.TopGrid != null && !ShouldStopScan(possibleWeldTargets, possibleGrindTargets, null))
                        AsyncAddBlocksOfGrid(ref areaBox, useIgnoreColor, ref ignoreColor, useGrindColor, ref grindColor, autoGrindRelation, autoGrindOptions, mechanicalConnectionBlock.TopGrid, grids, possibleSources, possibleWeldTargets, possibleGrindTargets);
                    continue;
                }

                var attachableTopBlock = fatBlock as Sandbox.ModAPI.IMyAttachableTopBlock;
                if (attachableTopBlock != null)
                {
                    if (attachableTopBlock.Base != null && attachableTopBlock.Base.CubeGrid != null && !ShouldStopScan(possibleWeldTargets, possibleGrindTargets, null))
                        AsyncAddBlocksOfGrid(ref areaBox, useIgnoreColor, ref ignoreColor, useGrindColor, ref grindColor, autoGrindRelation, autoGrindOptions, attachableTopBlock.Base.CubeGrid, grids, possibleSources, possibleWeldTargets, possibleGrindTargets);
                    continue;
                }

                var connector = fatBlock as Sandbox.ModAPI.IMyShipConnector;
                if (connector != null)
                {
                    if (connector.Status == MyShipConnectorStatus.Connected && connector.OtherConnector != null && !ShouldStopScan(possibleWeldTargets, possibleGrindTargets, null))
                    {
                        AsyncAddBlocksOfGrid(ref areaBox, useIgnoreColor, ref ignoreColor, useGrindColor, ref grindColor, autoGrindRelation, autoGrindOptions, connector.OtherConnector.CubeGrid, grids, possibleSources, possibleWeldTargets, possibleGrindTargets);
                    }
                    continue;
                }

                if (possibleWeldTargets != null && ((Settings.Flags & SyncBlockSettings.Settings.AllowBuild) != 0)) //If projected blocks should be build
                {
                    var projector = fatBlock as Sandbox.ModAPI.IMyProjector;
                    if (projector != null)
                    {
                        if (projector.IsProjecting && projector.BuildableBlocksCount > 0 && IsRelationAllowed4Welding(slimBlock))
                        {
                            if (!State.SafeZoneAllowsBuildingProjections)
                            {
                                continue;
                            }

                            //Add buildable blocks
                            var projectedCubeGrid = projector.ProjectedGrid;
                            if (projectedCubeGrid != null && !grids.Contains(projectedCubeGrid))
                            {
                                grids.Add(projectedCubeGrid);
                                var projectedBlocks = new List<IMySlimBlock>();

                                // TODO: Use cached list.
                                projectedCubeGrid.GetBlocks(projectedBlocks);

                                foreach (IMySlimBlock block in projectedBlocks)
                                {
                                    if (ShouldStopScan(possibleWeldTargets, possibleGrindTargets, null))
                                    {
                                        break;
                                    }

                                    double distance;
                                    if (BlockWeldPriority.GetEnabled(block) && block.IsInRange(ref areaBox, out distance) && block.CanBuild(false))
                                    {
                                        if (possibleWeldTargets.Count < MaxPossibleWeldTargets)
                                        {
                                            possibleWeldTargets.Add(new TargetBlockData(block, distance, TargetBlockData.AttributeFlags.Projected));
                                        }
                                    }
                                }
                            }
                        }
                        continue;
                    }
                }
            }
        }

        private bool ShouldStopScan(List<TargetBlockData> possibleWeldTargets, List<TargetBlockData> possibleGrindTargets, List<TargetEntityData> possibleFloatingTargets)
        {
            var weldFull = possibleWeldTargets == null || possibleWeldTargets.Count >= MaxPossibleWeldTargets;
            var grindFull = possibleGrindTargets == null || possibleGrindTargets.Count >= MaxPossibleGrindTargets;
            var floatingFull = possibleFloatingTargets == null || possibleFloatingTargets.Count >= MaxPossibleFloatingTargets;
            return weldFull && grindFull && floatingFull;
        }

        /// <summary>
        ///
        /// </summary>
        private void AsyncAddBlockIfTargetOrSource(ref MyOrientedBoundingBoxD areaBox, bool useIgnoreColor, ref uint ignoreColor, bool useGrindColor, ref uint grindColor, AutoGrindRelation autoGrindRelation, AutoGrindOptions autoGrindOptions, IMySlimBlock block, List<IMyInventory> possibleSources, List<TargetBlockData> possibleWeldTargets, List<TargetBlockData> possibleGrindTargets)
        {
            try
            {
                if (ShouldStopScan(possibleWeldTargets, possibleGrindTargets, null))
                {
                    return;
                }

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
                                terminalBlock.AddIfConnectedToInventory(_Welder, possibleSources);
                            }
                            catch (Exception ex)
                            {
                                Logging.Instance.Write(Logging.Level.Event, "BuildAndRepairSystemBlock {0}: AsyncAddBlockIfTargetOrSource1 exception: {1}", Logging.BlockName(_Welder, Logging.BlockNameOptions.None), ex);
                            }
                        }
                    }
                }

                var added = false;

                if (possibleGrindTargets != null && (useGrindColor || autoGrindRelation != 0))
                {
                    if (State.SafeZoneAllowsGrinding)
                    {
                        if (possibleGrindTargets.Count < MaxPossibleGrindTargets)
                        {
                            added = AsyncAddBlockIfGrindTarget(ref areaBox, useGrindColor, ref grindColor, autoGrindRelation, autoGrindOptions, block, possibleGrindTargets);
                        }
                    }
                }

                if (possibleWeldTargets != null && !added) //Do not weld if in grind list (could happen if auto grind neutrals is enabled and "HelpOthers" is active)
                {
                    if (possibleWeldTargets.Count < MaxPossibleWeldTargets)
                    {
                        AsyncAddBlockIfWeldTarget(ref areaBox, useIgnoreColor, ref ignoreColor, useGrindColor, ref grindColor, block, possibleWeldTargets);
                    }
                }
            }
            catch (Exception ex)
            {
                Logging.Instance.Error("BuildAndRepairSystemBlock {0}: AsyncAddBlockIfTargetOrSource2 exception: {1}", Logging.BlockName(_Welder, Logging.BlockNameOptions.None), ex);
                throw;
            }
        }

        /// <summary>
        /// Check if the given slim block is a weld target (in range, owned, damaged, new, ..)
        /// </summary>
        private bool AsyncAddBlockIfWeldTarget(ref MyOrientedBoundingBoxD areaBox, bool useIgnoreColor, ref uint ignoreColor, bool useGrindColor, ref uint grindColor, IMySlimBlock block, List<TargetBlockData> possibleWeldTargets)
        {
            if (possibleWeldTargets != null && possibleWeldTargets.Count >= MaxPossibleWeldTargets)
            {
                return false;
            }

            double distance;
            var colorMask = block.GetColorMask();
            Sandbox.ModAPI.IMyProjector projector;
            if (block.IsProjected(out projector))
            {
                if (!State.SafeZoneAllowsBuildingProjections)
                    return false;

                if (((Settings.Flags & SyncBlockSettings.Settings.AllowBuild) != 0) &&
                   (!useGrindColor || !IsColorNearlyEquals(grindColor, colorMask)) &&
                   BlockWeldPriority.GetEnabled(block) &&
                   block.IsInRange(ref areaBox, out distance) &&
                   IsRelationAllowed4Welding(projector.SlimBlock) &&
                   block.CanBuild(false))
                {
                    if (possibleWeldTargets.Count < MaxPossibleWeldTargets)
                    {
                        possibleWeldTargets.Add(new TargetBlockData(block, distance, TargetBlockData.AttributeFlags.Projected));
                        return true;
                    }
                }
            }
            else
            {
                if (!State.SafeZoneAllowsWelding)
                    return false;

                if ((!useIgnoreColor || !IsColorNearlyEquals(ignoreColor, colorMask)) && (!useGrindColor || !IsColorNearlyEquals(grindColor, colorMask)) &&
                   BlockWeldPriority.GetEnabled(block) &&
                   block.IsInRange(ref areaBox, out distance) &&
                   IsRelationAllowed4Welding(block) &&
                   block.NeedRepair((Settings.WeldOptions & AutoWeldOptions.FunctionalOnly) != 0))
                {
                    if (possibleWeldTargets.Count < MaxPossibleWeldTargets)
                    {
                        possibleWeldTargets.Add(new TargetBlockData(block, distance, 0));
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// Check if the given slim block is a grind target (in range, color )
        /// </summary>
        private bool AsyncAddBlockIfGrindTarget(ref MyOrientedBoundingBoxD areaBox, bool useGrindColor, ref uint grindColor, AutoGrindRelation autoGrindRelation, AutoGrindOptions autoGrindOptions, IMySlimBlock block, List<TargetBlockData> possibleGrindTargets)
        {
            if (possibleGrindTargets != null && possibleGrindTargets.Count >= MaxPossibleGrindTargets)
            {
                return false;
            }

            //block.CubeGrid.BlocksDestructionEnabled is not available for modding, so at least check if general destruction is enabled
            if ((MyAPIGateway.Session.SessionSettings.Scenario || MyAPIGateway.Session.SessionSettings.ScenarioEditMode) && !MyAPIGateway.Session.SessionSettings.DestructibleBlocks) return false;

            if (block.IsProjected())
                return false;

            var autoGrind = autoGrindRelation != 0 && BlockGrindPriority.GetEnabled(block);
            if (autoGrind)
            {
                // Do not allow grinding if our shields are up.
                if (block.CubeGrid.EntityId != Welder.CubeGrid.EntityId && State.IsShielded)
                {
                    return false;
                }

                var relation = block.GetUserRelationToOwner(_Welder.OwnerId);
                autoGrind =
                   (relation == MyRelationsBetweenPlayerAndBlock.NoOwnership && ((autoGrindRelation & AutoGrindRelation.NoOwnership) != 0)) ||
                   (relation == MyRelationsBetweenPlayerAndBlock.Enemies && ((autoGrindRelation & AutoGrindRelation.Enemies) != 0)) ||
                   (relation == MyRelationsBetweenPlayerAndBlock.Neutral && ((autoGrindRelation & AutoGrindRelation.Neutral) != 0));
            }

            if (autoGrind && ((autoGrindOptions & (AutoGrindOptions.DisableOnly | AutoGrindOptions.HackOnly)) != 0))
            {
                var criticalIntegrityRatio = ((MyCubeBlockDefinition)block.BlockDefinition).CriticalIntegrityRatio;
                var ownershipIntegrityRatio = ((MyCubeBlockDefinition)block.BlockDefinition).OwnershipIntegrityRatio > 0 ? ((MyCubeBlockDefinition)block.BlockDefinition).OwnershipIntegrityRatio : criticalIntegrityRatio;
                var integrityRation = block.Integrity / block.MaxIntegrity;
                if (autoGrind && ((autoGrindOptions & AutoGrindOptions.DisableOnly) != 0))
                {
                    autoGrind = block.FatBlock != null && integrityRation > criticalIntegrityRatio;
                }
                if (autoGrind && ((autoGrindOptions & AutoGrindOptions.HackOnly) != 0))
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
                    if (SafeZoneHandler.IsProtectedFromGrinding(block, Welder))
                    {
                        return false;
                    }

                    // Is protected by shields.
                    if (IsShieldProtected(block))
                    {
                        return false;
                    }

                    if (possibleGrindTargets.Count < MaxPossibleGrindTargets)
                    {
                        possibleGrindTargets.Add(new TargetBlockData(block, distance, autoGrind ? TargetBlockData.AttributeFlags.Autogrind : 0));
                        return true;
                    }
                }
            }
            return false;
        }

        public bool IsShieldProtected(IMySlimBlock slimBlock)
        {
            try
            {
                if (Mod.Settings.ShieldCheckEnabled && slimBlock != null && Mod.Shield != null && Mod.Shield.IsReady)
                {
                    var isProtected = Mod.Shield.ProtectedByShield(slimBlock.CubeGrid);

                    if (slimBlock.CubeGrid.EntityId == Welder.CubeGrid.EntityId)
                        return false;

                    if (!isProtected)
                        return false;

                    return Mod.Shield.IsBlockProtected(slimBlock);
                }
            }
            catch { }

            return false;
        }

        public bool IsWelderShielded()
        {
            try
            {
                if (Welder != null && Mod.Settings.ShieldCheckEnabled && Mod.Shield != null && Mod.Shield.IsReady)
                {
                    if (Mod.Shield.ProtectedByShield(Welder.CubeGrid))
                    {
                        return Mod.Shield.IsBlockProtected(Welder.SlimBlock);
                    }
                }
            }
            catch
            {
            }

            return false;
        }

        // TODO: heavy calls, imrove this one.
        private bool IsRelationAllowed4Welding(IMySlimBlock block)
        {
            var relation = _Welder.OwnerId == 0 ? MyRelationsBetweenPlayerAndBlock.NoOwnership : block.GetUserRelationToOwner(_Welder.OwnerId);
            if (relation == MyRelationsBetweenPlayerAndBlock.Enemies) return false;
            if (!_Welder.HelpOthers && (relation == MyRelationsBetweenPlayerAndBlock.Neutral || relation == MyRelationsBetweenPlayerAndBlock.NoOwnership)) return false;
            return true;
        }

        private static bool IsColorNearlyEquals(uint colorA, Vector3 colorB)
        {
            return colorA == colorB.PackHSVToUint();
        }

        public string GetStateString()
        {
            if (State.Grinding || State.NeedGrinding)
                return "Grinding";

            if (State.Welding || State.NeedWelding)
                return "Welding";

            if (State.Transporting)
                return "Transporting";

            return "Idle";
        }

        /// <summary>
        /// Update custom info of the block
        /// </summary>
        /// <param name="block"></param>
        /// <param name="customInfo"></param>
        private void AppendingCustomInfo(IMyTerminalBlock terminalBlock, StringBuilder customInfo)
        {
            customInfo.Clear();

            customInfo.Append($"SZ: {SafeZoneHandler.Zones.Count}{Environment.NewLine}");
            customInfo.Append($"State: {GetStateString()}{Environment.NewLine}");

            var resourceSink = _Welder.ResourceSink as Sandbox.Game.EntityComponents.MyResourceSinkComponent;
            if (resourceSink != null)
            {
                customInfo.Append($"Power: ");
                MyValueFormatter.AppendWorkInBestUnit(resourceSink.RequiredInputByType(ElectricityId), customInfo);
                customInfo.Append($" / ");
                MyValueFormatter.AppendWorkInBestUnit(resourceSink.MaxRequiredInputByType(ElectricityId), customInfo);
                customInfo.Append(Environment.NewLine);
            }

            customInfo.Append(Environment.NewLine);

            if (State.IsShielded)
            {
                customInfo.Append($"[color=#FFFFFF00]Shields Active[/color]: Grinding disabled!");
                customInfo.Append(Environment.NewLine);
            }

            if (!State.SafeZoneAllowsWelding)
            {
                customInfo.Append($"[color=#FFFFFF00]SafeZone[/color]: Welding disabled!");
                customInfo.Append(Environment.NewLine);
            }

            if (!State.SafeZoneAllowsBuildingProjections)
            {
                customInfo.Append($"[color=#FFFFFF00]SafeZone[/color]: Building projections disabled!");
                customInfo.Append(Environment.NewLine);
            }

            if (!State.SafeZoneAllowsGrinding)
            {
                customInfo.Append($"[color=#FFFFFF00]SafeZone[/color]: Grinding disabled!");
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

                if (State.InventoryFull) customInfo.Append($"[color=#FFFFFF00]{Texts.Info_InventoryFull}[/color]{Environment.NewLine + Environment.NewLine}");
                if (State.LimitsExceeded) customInfo.Append($"[color=#FFFFFF00]{Texts.Info_LimitReached}[/color]{Environment.NewLine + Environment.NewLine}");

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
                if (State.Welding || State.NeedWelding)
                {
                    lock (State.PossibleWeldTargets)
                    {
                        if (State.PossibleWeldTargets?.Count > 0)
                        {
                            customInfo.Append(Texts.Info_BlocksToBuild + Environment.NewLine);
                            foreach (var blockData in State.PossibleWeldTargets)
                            {
                                customInfo.Append(string.Format(" -{0}" + Environment.NewLine, blockData.Block.BlockName()));
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
                }

                cnt = 0;
                if (State.Grinding || State.NeedGrinding)
                {
                    lock (State.PossibleGrindTargets)
                    {
                        if (State.PossibleGrindTargets?.Count > 0)
                        {
                            customInfo.Append(Texts.Info_BlocksToGrind + Environment.NewLine);
                            foreach (var blockData in State.PossibleGrindTargets)
                            {
                                customInfo.Append(string.Format(" -{0}" + Environment.NewLine, blockData.Block.BlockName()));
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
                }

                cnt = 0;
                lock (State.PossibleFloatingTargets)
                {
                    if (State.PossibleFloatingTargets?.Count > 0)
                    {
                        customInfo.Append(Texts.Info_ItemsToCollect + Environment.NewLine);
                        foreach (var entityData in State.PossibleFloatingTargets)
                        {
                            customInfo.Append(string.Format(" -{0}" + Environment.NewLine, Logging.BlockName(entityData.Entity, Logging.BlockNameOptions.None)));
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
            if (playTime.Subtract(_LastFriendlyDamageCleanup) > Mod.Settings.FriendlyDamageCleanup)
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
            // Not Ready.
            if (!State.Ready)
                return WorkingState.NotReady;

            // Welding.
            else if (State.Welding)
                return WorkingState.Welding;

            // Need welding.
            else if (State.NeedWelding)
            {
                if (State.MissingComponents.Count > 0)
                    return WorkingState.MissingComponents;

                if (State.LimitsExceeded)
                    return WorkingState.LimitsExceeded;

                return WorkingState.NeedWelding;
            }

            // Grinding.
            else if (State.Grinding)
                return WorkingState.Grinding;

            // Need grinding.
            else if (State.NeedGrinding)
            {
                if (State.InventoryFull)
                    return WorkingState.InventoryFull;

                return WorkingState.NeedGrinding;
            }

            // Idle.
            return WorkingState.Idle;
        }

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
        internal MemorySafeDictionary<MyDefinitionId, int> GetMissingComponentsDict()
        {
            var dict = new MemorySafeDictionary<MyDefinitionId, int>();
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