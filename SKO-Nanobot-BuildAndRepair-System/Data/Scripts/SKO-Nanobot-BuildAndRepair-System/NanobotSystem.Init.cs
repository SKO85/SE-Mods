using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using SKONanobotBuildAndRepairSystem.Cluster;
using SKONanobotBuildAndRepairSystem.Extensions;
using SKONanobotBuildAndRepairSystem.Handlers;
using SKONanobotBuildAndRepairSystem.Helpers;
using SKONanobotBuildAndRepairSystem.Utils;
using System;
using System.Diagnostics;
using VRage.Game;
using VRage.ModAPI;
using VRage.ObjectBuilders;
using VRageMath;

namespace SKONanobotBuildAndRepairSystem
{
    public partial class NanobotSystem
    {
        /// <summary>
        /// Initialize logical component
        /// </summary>
        /// <param name="objectBuilder"></param>
        public override void Init(MyObjectBuilder_EntityBase objectBuilder)
        {
            base.Init(objectBuilder);

            // Set frame update rate — always use EACH_10TH_FRAME.
            // Actual operation frequency is controlled by WorkSpeed setting via cycle math.
            NeedsUpdate = MyEntityUpdateEnum.EACH_FRAME | MyEntityUpdateEnum.EACH_10TH_FRAME;

            if (Entity.GameLogic is MyCompositeGameLogicComponent)
            {
                Entity.GameLogic = this;
            }

            _Welder = Entity as Sandbox.ModAPI.IMyShipWelder;
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

                if ((Mod.Settings.Welder.AllowedEffects & VisualAndSoundEffects.WeldingSoundEffect) == 0) Effects._Sounds[(int)WorkingState.Welding] = null;
                if ((Mod.Settings.Welder.AllowedEffects & VisualAndSoundEffects.GrindingSoundEffect) == 0) Effects._Sounds[(int)WorkingState.Grinding] = null;
            }

            var resourceSink = _Welder.ResourceSink as Sandbox.Game.EntityComponents.MyResourceSinkComponent;
            if (resourceSink != null)
            {
                var electricPowerTransport = Settings.MaximumRequiredElectricPowerTransport;
                if ((Mod.Settings.Welder.AllowedSearchModes & SearchModes.BoundingBox) == 0) electricPowerTransport /= 10;
                var maxPowerWorking = Math.Max(Settings.MaximumRequiredElectricPowerWelding, Settings.MaximumRequiredElectricPowerGrinding);
                resourceSink.SetMaxRequiredInputByType(ElectricityId, Math.Max(maxPowerWorking, electricPowerTransport));
                resourceSink.SetRequiredInputFuncByType(ElectricityId, ComputeRequiredElectricPower);
                resourceSink.Update();
            }

            NeedsUpdate = MyEntityUpdateEnum.EACH_FRAME | MyEntityUpdateEnum.EACH_10TH_FRAME;

            var maxMultiplier = Math.Max(Mod.Settings.Welder.WeldingMultiplier, Mod.Settings.Welder.GrindingMultiplier);
            var multiplier = (maxMultiplier > WELDER_TRANSPORTVOLUME_MAX_MULTIPLIER ? WELDER_TRANSPORTVOLUME_MAX_MULTIPLIER : maxMultiplier);
            _MaxTransportVolume = ((float)_TransportInventory.MaxVolume * multiplier) / WELDER_TRANSPORTVOLUME_DIVISOR;

            var weldMult = Math.Min(Mod.Settings.Welder.WeldingMultiplier, WELDER_TRANSPORTVOLUME_MAX_MULTIPLIER);
            _MaxWeldTransportVolume = ((float)_TransportInventory.MaxVolume * weldMult) / WELDER_TRANSPORTVOLUME_DIVISOR;
            var grindMult = Math.Min(Mod.Settings.Welder.GrindingMultiplier, WELDER_TRANSPORTVOLUME_MAX_MULTIPLIER);
            _MaxGrindTransportVolume = ((float)_TransportInventory.MaxVolume * grindMult) / WELDER_TRANSPORTVOLUME_DIVISOR;

            UpdateCustomInfo(true);

            // When a sort-relevant flag changed (near/far, smallest-grid, ignore color,
            // search mode, work mode, priority lists, etc.), the cluster key hash differs
            // from the last rebuild. Drop the in-flight lock-on AND release every
            // assignment held by this BaR — the multi-action wrapper may have claimed
            // several blocks in one cycle, and we want the next scan to pick from the
            // freshly sorted list with no carryover.
            if (_Welder != null && _Welder.CubeGrid != null)
            {
                var newClusterHash = ScanClusterCoordinator.ComputeClusterKeyHash(this);
                if (_lastClusterKeyHash != 0 && newClusterHash != _lastClusterKeyHash)
                {
                    State.CurrentWeldingBlock = null;
                    State.CurrentGrindingBlock = null;
                    if (Mod.Settings.AssignToSystemEnabled)
                    {
                        BlockSystemAssigningHandler.ReleaseAllForSystem(_Welder.EntityId);
                    }
                }
            }

            // FEAT-080: settings changed — force immediate rescan, bypassing FEAT-075 debounce.
            TriggerImmediateRescan("settingsChanged", true);
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

        private void Init()
        {
            if (_IsInit) return;
            if (_Welder.SlimBlock.IsProjected() || !_Welder.Synchronized) //Synchronized = !IsPreview
            {
                NeedsUpdate = MyEntityUpdateEnum.NONE;
                return;
            }

            // BUG-260610.6: validate everything that can early-return BEFORE the block
            // is registered or events are subscribed. Update10_100 retries Init() until
            // _IsInit is true; registering/subscribing first meant each retry stacked
            // another handler and left a half-initialized entry (no _TransportInventory)
            // visible to Mod.SettingsChanged's fan-out.
            var welderInventory = _Welder.GetInventory(0);
            if (welderInventory == null) return;

            // Register this block to the nanobot systems.
            Mod.NanobotSystems.TryAdd(Entity.EntityId, this);

            // Force HelpOthers off — the mod does not use this option.
            _Welder.HelpOthers = false;

            // PERF-10: per-instance Random seeded from EntityId (XOR-fold high+low halves).
            var seed = (int)(_Welder.EntityId ^ (_Welder.EntityId >> 32));
            _RandomDelay = new Random(seed);

            // Assign stagger slot so BaR updates are distributed across ticks.
            _staggerSlot = Mod.ClaimStaggerSlot();

            // Initialize controls.
            Mod.InitControls();

            // BUG-260610.6: create the handlers once (=, not +=) and make the event
            // subscription idempotent (unsubscribe-then-subscribe is a no-op when not
            // subscribed), so a retried Init() can never stack duplicate invocations.
            if (_onEnabledChanged == null)
            {
                _onEnabledChanged = (block) =>
                {
                    // BUG-120: power-cycle resets the broken-block caches (player's retry path).
                    _BrokenProjBuildKeys.Clear();
                    _ProjBuildSilentFailCount.Clear();
                    _BrokenCacheOwnerId = _Welder != null ? _Welder.OwnerId : long.MinValue;
                    UpdateCustomInfo(true);
                };
            }

            if (_onIsWorkingChanged == null)
            {
                _onIsWorkingChanged = (block) =>
                {
                    UpdateCustomInfo(true);
                };
            }

            _Welder.EnabledChanged -= _onEnabledChanged;
            _Welder.EnabledChanged += _onEnabledChanged;
            _Welder.IsWorkingChanged -= _onIsWorkingChanged;
            _Welder.IsWorkingChanged += _onIsWorkingChanged;

            // Set transport Inventory.
            // BUG-260827.2: explicit maxMass — current SE builds leave the short
            // constructor's mass constraint at 0, making CanItemsBeAdded/adds refuse
            // everything on this detached inventory (welding starved completely).
            _TransportInventory = new Sandbox.Game.MyInventory((float)welderInventory.MaxVolume / MyAPIGateway.Session.BlocksInventorySizeMultiplier, float.MaxValue, Vector3.MaxValue, MyInventoryFlags.CanSend);

            // BUG-018: seed InventoryFull from current welder volume on world reload.
            if ((float)welderInventory.CurrentVolume >= (float)welderInventory.MaxVolume)
            {
                State.InventoryFull = true;
            }

            // Trigger settings changed.
            SettingsChanged();

            // Set Effects Emitter Position.
            _Effects.InitEmitterPosition(_Welder);

            NetworkMessagingHandler.MsgBlockDataRequestSend(this);

            if (MyAPIGateway.Session.IsServer)
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

        public override void Close()
        {
            if (_IsInit)
            {
                // Wait for any in-flight async scan to finish (1 s ceiling, ~1 ms spin).
                var deadline = DateTime.UtcNow.AddSeconds(1);
                var pollSpacingTicks = Stopwatch.Frequency / 1000;
                var spin = new Stopwatch();
                while (DateTime.UtcNow < deadline)
                {
                    lock (_Welder)
                    {
                        if (!_AsyncUpdateSourcesAndTargetsRunning) break;
                    }
                    spin.Restart();
                    while (spin.ElapsedTicks < pollSpacingTicks) { }
                }

                // BUG-260612.27: best-effort return of in-flight transport items to the
                // welder inventory (which the engine drops as loot on destruction) —
                // Close used to silently delete the load of a BaR destroyed mid-grind.
                try { ServerEmptyTransportInventory(false); } catch { }

                // Stop effects
                State.CurrentTransportTarget = null;
                State.Ready = false;
                _InitialScanCompleted = false;
                _PushTargetsFull = false;

                // BUG-160: release grid-count contribution before tear-down (setter Dec's count).
                State.CurrentWeldingBlock = null;
                State.CurrentGrindingBlock = null;

                // BUG-260610.12: release every TTL block claim held by this BaR (the
                // multi-action wrapper can hold several) so other BaRs aren't blocked
                // for up to AssignmentTtlSeconds after this system is destroyed.
                if (Mod.Settings.AssignToSystemEnabled && _Welder != null)
                {
                    BlockSystemAssigningHandler.ReleaseAllForSystem(_Welder.EntityId);
                }

                _Effects.UpdateEffects(this);
                _Effects.Close(this);

                lock (State.PossibleWeldTargets) State.PossibleWeldTargets?.Clear();
                lock (State.PossibleGrindTargets) State.PossibleGrindTargets?.Clear();
                lock (State.PossibleFloatingTargets) State.PossibleFloatingTargets?.Clear();
                lock (State.MissingComponents) State.MissingComponents?.Clear();
                // BUG-130: per-BaR FriendlyDamage retired; Mod's shared owner-keyed map persists.

                _TempPossibleWeldTargets?.Clear();
                _TempPossibleGrindTargets?.Clear();
                _TempPossibleFloatingTargets?.Clear();
                _TempCollectTargets.Clear(); // BUG-260612.22
                _TempPossibleSources?.Clear();
                _TempPossiblePushTargets?.Clear();
                _TempMissingComponents?.Clear();
                _TempInventoryItems?.Clear();
                _TempPullInventoryItems?.Clear();

                lock (_PossibleSources)
                {
                    _PossibleSources?.Clear();
                }
                lock (_PossiblePushTargets)
                {
                    _PossiblePushTargets?.Clear();
                }

                _EmptyGridCache.Clear();

                _DelayWatch?.Stop();

                // Save settings.
                Settings.Save(Entity, Mod.ModGuid);
            }

            // BUG-260610.6: must run even when Init() never completed —
            // AppendingCustomInfo is subscribed in the public Init(), and a private
            // Init() that early-returned may already have registered the block and
            // subscribed the enabled/working handlers.
            if (_Welder != null)
            {
                _Welder.AppendingCustomInfo -= AppendingCustomInfo;
                if (_onEnabledChanged != null) _Welder.EnabledChanged -= _onEnabledChanged;
                if (_onIsWorkingChanged != null) _Welder.IsWorkingChanged -= _onIsWorkingChanged;
            }

            // Remove system from list.
            if (Entity != null)
            {
                NanobotSystem removed;
                Mod.NanobotSystems.TryRemove(Entity.EntityId, out removed);
            }

            base.Close();
        }

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
    }
}
