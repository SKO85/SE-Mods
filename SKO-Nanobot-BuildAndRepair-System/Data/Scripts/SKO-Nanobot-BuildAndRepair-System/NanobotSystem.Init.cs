using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using SKONanobotBuildAndRepairSystem.Handlers;
using SKONanobotBuildAndRepairSystem.Helpers;
using SKONanobotBuildAndRepairSystem.Utils;
using System;
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

            // Set frame update rate.
            NeedsUpdate = MyEntityUpdateEnum.EACH_FRAME | MyEntityUpdateEnum.EACH_100TH_FRAME;

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

            // Assign stagger slot so BaR updates are distributed across ticks.
            _staggerSlot = Mod.ClaimStaggerSlot();

            // Initialize controls.
            Mod.InitControls();

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

            // BUG-018: Initialize inventory state from current welder inventory.
            // On world reload, State.InventoryFull defaults to false even when the welder
            // is full from the previous session. This allows one round of collecting/grinding
            // before the proactive check in ServerTryWeldingGrindingCollecting catches it,
            // consuming floating items from the world that can't actually be stored.
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
                if (_Welder != null)
                {
                    _Welder.AppendingCustomInfo -= AppendingCustomInfo;
                    if (_onEnabledChanged != null) _Welder.EnabledChanged -= _onEnabledChanged;
                    if (_onIsWorkingChanged != null) _Welder.IsWorkingChanged -= _onIsWorkingChanged;
                }

                // Stop effects
                State.CurrentTransportTarget = null;
                State.Ready = false;
                _InitialScanCompleted = false;
                _PushTargetsFull = false;

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
                _TempPossiblePushTargets?.Clear();
                _TempMissingComponents?.Clear();
                _TempInventoryItems?.Clear();

                lock (_PossibleSources)
                {
                    _PossibleSources?.Clear();
                }
                lock (_PossiblePushTargets)
                {
                    _PossiblePushTargets?.Clear();
                }

                CachedBlocksTime.Clear();
                CachedBlocks.Clear();

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
