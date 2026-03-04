using Sandbox.Common.ObjectBuilders;
using Sandbox.Definitions;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces;
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
        // Cached reference to the "helpOthers" action — avoids a string lookup on every terminal refresh
        private ITerminalAction _helpOthersAction;

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
                        var color = Color.Black;
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

                // Capture once per update — avoids repeated property-chain traversals
                var playTime = MyAPIGateway.Session.ElapsedPlayTime;

                if (MyAPIGateway.Session.IsServer)
                {
                    CreativeModeActive = MyAPIGateway.Session.CreativeMode;

                    if (!fast)
                    {
                        CleanupFriendlyDamage();
                    }

                    ServerTryWeldingGrindingCollecting();

                    if (State.Ready && (playTime - _PeriodicExtraChecksLast).TotalSeconds >= 2)
                    {
                        _PeriodicExtraChecksLast = playTime;
                        try
                        {
                            if (SetSafeZoneAndShieldStates())
                            {
                                UpdateCustomInfo(true);
                            }
                            else
                            {
                                UpdateCustomInfo(false);
                            }
                        }
                        catch { }
                    }

                    if (!fast)
                    {
                        if (State.Ready && (playTime - _UpdatePowerSinkLast).TotalSeconds >= 2)
                        {
                            _UpdatePowerSinkLast = playTime;
                            var resourceSink = _Welder.ResourceSink as Sandbox.Game.EntityComponents.MyResourceSinkComponent;
                            if (resourceSink != null)
                            {
                                resourceSink.Update();
                            }
                        }

                        Settings.TrySave(Entity, Mod.ModGuid);

                        if (State.IsTransmitNeeded() && (playTime - _UpdateStateTransmitLast).TotalSeconds >= _UpdateStateTransmitInterval)
                        {
                            _UpdateStateTransmitLast = playTime;
                            _UpdateStateTransmitInterval = _RandomDelay.Next(TransmitStateMinIntervalSeconds, TransmitStateMaxIntervalSeconds + 1);
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
                }

                if (Settings.IsTransmitNeeded() && (playTime - _UpdateSettingsTransmitLast).TotalSeconds >= TransmitSettingsIntervalSeconds)
                {
                    _UpdateSettingsTransmitLast = playTime;
                    NetworkMessagingHandler.MsgBlockSettingsSend(0, this);
                }

                if (_UpdateCustomInfoNeeded) UpdateCustomInfo(false);

                _DelayWatch.Stop();
                if (_DelayWatch.ElapsedMilliseconds > 40)
                {
                    _Delay = _RandomDelay.Next(1, 20); //Slowdown a little bit
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
                    // Cache on first use to avoid repeated string-keyed lookups
                    if (_helpOthersAction == null)
                        _helpOthersAction = _Welder.GetActionWithName("helpOthers");
                    if (_helpOthersAction != null)
                    {
                        _helpOthersAction.Apply(_Welder);
                        _helpOthersAction.Apply(_Welder);
                    }
                }
                catch (Exception)
                {
                }
            }
        }

        public string GetStateString()
        {
            if (State.Grinding)
                return "Grinding";

            if (State.Welding)
                return "Welding";

            if (State.NeedWelding && State.Transporting)
                return "Welding (Transporting)";

            if (State.NeedGrinding && State.Transporting)
                return "Grinding (Transporting)";

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

            // If mod is not yet initialized, show this in the info panel.
            if (State.Ready && !Mod.SettingsValid)
            {
                // Split string + Environment.NewLine into two Append calls to avoid the intermediate string allocation
                customInfo.Append("[color=#FFFFFF00]Mod not initialized![/color]").Append(Environment.NewLine);
                customInfo.Append("---").Append(Environment.NewLine);
                customInfo.Append("If this message remains:").Append(Environment.NewLine);
                customInfo.Append("- Try reopen this terminal in a few seconds.").Append(Environment.NewLine).Append(Environment.NewLine);
                customInfo.Append("If above does not work:").Append(Environment.NewLine);
                customInfo.Append("- Try cleanup the mod folder.").Append(Environment.NewLine);
                customInfo.Append("- Re-Subscribe to the mod.").Append(Environment.NewLine);
                customInfo.Append("- Check FAQ on workshop page.").Append(Environment.NewLine);
                return;
            }

            if (CreativeModeActive)
            {
                customInfo.Append("[color=#FFFFFF00]Creative Mode Active[/color]");
                customInfo.Append(Environment.NewLine);
            }

            customInfo.Append("State: ").Append(GetStateString()).Append(Environment.NewLine);

            var resourceSink = _Welder.ResourceSink as Sandbox.Game.EntityComponents.MyResourceSinkComponent;
            if (resourceSink != null)
            {
                customInfo.Append("Power: ");
                MyValueFormatter.AppendWorkInBestUnit(PowerHelper.ComputeRequiredElectricPower(this), customInfo);
                customInfo.Append(" / ");
                MyValueFormatter.AppendWorkInBestUnit(resourceSink.MaxRequiredInputByType(ElectricityId), customInfo);
                customInfo.Append(Environment.NewLine);
            }

            customInfo.Append(Environment.NewLine);

            if (State.IsShielded)
            {
                customInfo.Append("[color=#FFFFFF00]Shields Active[/color]: Grinding disabled!").Append(Environment.NewLine);
            }

            if (!State.SafeZoneAllowsWelding)
            {
                customInfo.Append("[color=#FFFFFF00]SafeZone[/color]: Welding disabled!").Append(Environment.NewLine);
            }

            if (!State.SafeZoneAllowsBuildingProjections)
            {
                customInfo.Append("[color=#FFFFFF00]SafeZone[/color]: Building projections disabled!").Append(Environment.NewLine);
            }

            if (!State.SafeZoneAllowsGrinding)
            {
                customInfo.Append("[color=#FFFFFF00]SafeZone[/color]: Grinding disabled!").Append(Environment.NewLine);
            }

            customInfo.Append(Environment.NewLine);

            if (_Welder.Enabled && _Welder.IsWorking && _Welder.IsFunctional)
            {
                if ((Settings.Flags & SyncBlockSettings.Settings.ScriptControlled) != 0)
                {
                    if (Settings.CurrentPickedWeldingBlock != null)
                    {
                        customInfo.Append(Texts.Info_CurentWeldEntity).Append(Environment.NewLine);
                        customInfo.Append(" -").Append(Settings.CurrentPickedWeldingBlock.BlockName()).Append(Environment.NewLine);
                    }

                    if (Settings.CurrentPickedGrindingBlock != null)
                    {
                        customInfo.Append(Texts.Info_CurentGrindEntity).Append(Environment.NewLine);
                        customInfo.Append(" -").Append(Settings.CurrentPickedGrindingBlock.BlockName()).Append(Environment.NewLine);
                    }
                }

                if (State.InventoryFull) customInfo.Append("[color=#FFFFFF00]").Append(Texts.Info_InventoryFull).Append("[/color]").Append(Environment.NewLine).Append(Environment.NewLine);
                if (State.LimitsExceeded) customInfo.Append("[color=#FFFFFF00]").Append(Texts.Info_LimitReached).Append("[/color]").Append(Environment.NewLine).Append(Environment.NewLine);

                var cnt = 0;
                lock (State.MissingComponents)
                {
                    if (State.MissingComponents?.Count > 0)
                    {
                        customInfo.Append(Texts.Info_MissingItems).Append(Environment.NewLine);
                        foreach (var component in State.MissingComponents)
                        {
                            var componentId = new MyDefinitionId(typeof(MyObjectBuilder_Component), component.Key.SubtypeId);
                            MyComponentDefinition componentDefnition;
                            var name = MyDefinitionManager.Static.TryGetComponentDefinition(componentId, out componentDefnition) ? componentDefnition.DisplayNameText : component.Key.SubtypeName;
                            customInfo.Append(" -").Append(name).Append(": ").Append(component.Value).Append(Environment.NewLine);
                            cnt++;
                            if (cnt >= SyncBlockState.MaxSyncItems)
                            {
                                customInfo.Append(Texts.Info_More).Append(Environment.NewLine);
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
                            customInfo.Append(Texts.Info_BlocksToBuild).Append(Environment.NewLine);
                            foreach (var blockData in State.PossibleWeldTargets)
                            {
                                if (blockData.Block == null) continue;
                                customInfo.Append(" -").Append(blockData.Block.BlockName()).Append(Environment.NewLine);
                                cnt++;
                                if (cnt >= SyncBlockState.MaxSyncItems)
                                {
                                    customInfo.Append(Texts.Info_More).Append(Environment.NewLine);
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
                            customInfo.Append(Texts.Info_BlocksToGrind).Append(Environment.NewLine);
                            foreach (var blockData in State.PossibleGrindTargets)
                            {
                                if (blockData.Block == null) continue;
                                customInfo.Append(" -").Append(blockData.Block.BlockName()).Append(Environment.NewLine);
                                cnt++;
                                if (cnt >= SyncBlockState.MaxSyncItems)
                                {
                                    customInfo.Append(Texts.Info_More).Append(Environment.NewLine);
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
                        customInfo.Append(Texts.Info_ItemsToCollect).Append(Environment.NewLine);
                        foreach (var entityData in State.PossibleFloatingTargets)
                        {
                            if (entityData.Entity == null) continue;
                            customInfo.Append(" -").Append(Logging.BlockName(entityData.Entity, Logging.BlockNameOptions.None)).Append(Environment.NewLine);
                            cnt++;
                            if (cnt >= SyncBlockState.MaxSyncItems)
                            {
                                customInfo.Append(Texts.Info_More).Append(Environment.NewLine);
                                break;
                            }
                        }
                    }
                }
            }
            else
            {
                if (!_Welder.Enabled) customInfo.Append("[color=#FFFFFF00]").Append(Texts.Info_BlockSwitchedOff).Append("[/color]").Append(Environment.NewLine);
                else if (!_Welder.IsFunctional) customInfo.Append("[color=#FFFFFF00]").Append(Texts.Info_BlockDamaged).Append("[/color]").Append(Environment.NewLine);
                else if (!_Welder.IsWorking) customInfo.Append("[color=#FFFFFF00]").Append(Texts.Info_BlockUnpowered).Append("[/color]").Append(Environment.NewLine);
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
    }
}
