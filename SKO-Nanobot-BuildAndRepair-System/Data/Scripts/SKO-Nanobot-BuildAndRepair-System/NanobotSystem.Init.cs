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
    partial class NanobotSystem
    {
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

            // Stagger the first target scan across the full TargetsUpdateInterval window so that
            // systems initialised at the same time don't all scan simultaneously (thundering herd).
            var intervalSeconds = Mod.Settings.TargetsUpdateInterval.TotalSeconds;
            var offsetSeconds = (Math.Abs(Entity.EntityId) % 100) / 100.0 * intervalSeconds;
            _LastTargetsUpdate = MyAPIGateway.Session.ElapsedPlayTime
                - Mod.Settings.TargetsUpdateInterval
                + TimeSpan.FromSeconds(offsetSeconds);

            _IsInit = true;
        }

        private float ComputeRequiredElectricPower()
        {
            return PowerHelper.ComputeRequiredElectricPower(this);
        }
    }
}
