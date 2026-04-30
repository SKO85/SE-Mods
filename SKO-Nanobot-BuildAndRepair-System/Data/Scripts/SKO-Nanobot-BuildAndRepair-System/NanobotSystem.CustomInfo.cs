using Sandbox.Definitions;
using Sandbox.ModAPI;
using SKONanobotBuildAndRepairSystem.Extensions;
using SKONanobotBuildAndRepairSystem.Helpers;
using SKONanobotBuildAndRepairSystem.Localization;
using SKONanobotBuildAndRepairSystem.Models;
using SKONanobotBuildAndRepairSystem.Profiling;
using SKONanobotBuildAndRepairSystem.Utils;
using System;
using System.Text;
using VRage.Game;
using VRage.Utils;
using IMyTerminalBlock = Sandbox.ModAPI.IMyTerminalBlock;

namespace SKONanobotBuildAndRepairSystem
{
    public partial class NanobotSystem
    {
        private void UpdateCustomInfo(bool changed)
        {
            _UpdateCustomInfoNeeded |= changed;
            var playTime = MyAPIGateway.Session.ElapsedPlayTime;
            if (_UpdateCustomInfoNeeded && (playTime.Subtract(_UpdateCustomInfoLast).TotalSeconds >= 2))
            {
                var profilerTs = MethodProfiler.Start();
                try
                {
                    _Welder.RefreshCustomInfo();
                    // TriggerTerminalRefresh forces the terminal panel to redraw via a
                    // workaround (Apply helpOthers action twice). On dedicated servers there's
                    // no local terminal — skip the expensive action lookup + apply calls.
                    if (!MyAPIGateway.Utilities.IsDedicated)
                    {
                        TriggerTerminalRefresh();
                    }
                }
                finally
                {
                    if (profilerTs != 0L)
                    {
                        MethodProfiler.StopAndLog("UpdateCustomInfo", profilerTs, () =>
                            string.Format("entityId={0}", _Welder.EntityId));
                    }
                }

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
                customInfo.Append($"[color=#FFFFFF00]Mod not initialized![/color]" + Environment.NewLine);
                customInfo.Append($"---" + Environment.NewLine);
                customInfo.Append($"If this message remains:" + Environment.NewLine);
                customInfo.Append($"- Try reopen this terminal in a few seconds." + Environment.NewLine + Environment.NewLine);

                customInfo.Append($"If above does not work:" + Environment.NewLine);
                customInfo.Append($"- Try cleanup the mod folder." + Environment.NewLine);
                customInfo.Append($"- Re-Subscribe to the mod." + Environment.NewLine);
                customInfo.Append($"- Check FAQ on workshop page." + Environment.NewLine);
                return;
            }

            if (CreativeModeActive)
            {
                customInfo.Append($"[color=#FFFFFF00]Creative Mode Active[/color]");
                customInfo.Append(Environment.NewLine);
            }

            customInfo.Append($"State: {GetStateString()}{Environment.NewLine}");

            // Show scan info when idle so players know the BaR is waiting, not stuck.
            if (GetWorkingState() == WorkingState.Idle)
            {
                if (MyAPIGateway.Session.IsServer)
                {
                    // Server: show countdown (we have the scan timer data)
                    var cluster = AssignedCluster;
                    var coord = cluster != null ? cluster.Coordinator : null;
                    if (coord != null)
                    {
                        var playTime = MyAPIGateway.Session.ElapsedPlayTime;
                        var idleCount = coord._consecutiveEmptyScans;
                        var interval = idleCount >= IdleScansBeforeBackoff
                            ? IdleScanInterval
                            : Mod.Settings.TargetsUpdateInterval;
                        var elapsed = playTime.Subtract(coord._LastTargetsUpdate);
                        var remaining = interval.Subtract(elapsed);
                        if (remaining.TotalSeconds < 0) remaining = TimeSpan.Zero;
                        customInfo.Append(string.Format("Next target scan: {0:F0}s{1}", remaining.TotalSeconds, Environment.NewLine));
                    }
                }
                else
                {
                    // Client: no scan timer data available, show generic message
                    customInfo.Append(string.Format("Scanning for targets...{0}", Environment.NewLine));
                }
            }

            var resourceSink = _Welder.ResourceSink as Sandbox.Game.EntityComponents.MyResourceSinkComponent;
            if (resourceSink != null)
            {
                customInfo.Append($"Power: ");
                MyValueFormatter.AppendWorkInBestUnit(PowerHelper.ComputeRequiredElectricPower(this), customInfo);
                customInfo.Append($" / ");
                MyValueFormatter.AppendWorkInBestUnit(resourceSink.MaxRequiredInputByType(ElectricityId), customInfo);
                customInfo.Append(Environment.NewLine);
            }

            // Debug diagnostics — local game only (not on DS or DS clients).
            // Version, MaxSystems/Grid, Total BaRs, Stagger, GrindBudget, ModSettings.xml
            // are shown in the debug HUD overlay instead — no duplication needed here.
            if (Mod.Settings.DebugMode && MyAPIGateway.Session.IsServer && !MyAPIGateway.Utilities.IsDedicated)
            {
                int sourceCount = 0;
                int pushTargetCount = 0;
                lock (_PossibleSources) { sourceCount = _PossibleSources.Count; }
                lock (_PossiblePushTargets) { pushTargetCount = _PossiblePushTargets.Count; }
                customInfo.Append(string.Format("Sources: {0} | Push Targets: {1}{2}", sourceCount, pushTargetCount, Environment.NewLine));

                var cluster = AssignedCluster;
                if (cluster != null)
                {
                    var isCoord = cluster.IsCoordinator(this);
                    var coordName = "";
                    var coord = cluster.Coordinator;
                    if (coord != null && coord.Welder != null)
                    {
                        coordName = Logging.BlockName(coord.Welder, Logging.BlockNameOptions.None);
                    }
                    customInfo.Append(string.Format("Cluster: {0} | Members: {1}{2}", cluster.ClusterKey.GetHashCode(), cluster.Members.Count, Environment.NewLine));
                    customInfo.Append(string.Format("Coordinator: {0}{1}{2}", coordName, isCoord ? " (self)" : "", Environment.NewLine));

                    // Scan countdown: show when the next scan will fire.
                    if (coord != null)
                    {
                        var playTime = MyAPIGateway.Session.ElapsedPlayTime;
                        var idleCount = coord._consecutiveEmptyScans;
                        var scanMode = "normal";
                        var interval = Mod.Settings.TargetsUpdateInterval;
                        if (idleCount >= IdleScansBeforeBackoff)
                        {
                            interval = IdleScanInterval;
                            scanMode = "idle";
                        }
                        if (coord._scanSkippedSaturated)
                        {
                            scanMode = "saturated (skip)";
                        }
                        var elapsed = playTime.Subtract(coord._LastTargetsUpdate);
                        var remaining = interval.Subtract(elapsed);
                        if (remaining.TotalSeconds < 0) remaining = TimeSpan.Zero;
                        customInfo.Append(string.Format("Scan: {0} | Next: {1:F0}s | Idle: {2}{3}",
                            scanMode, remaining.TotalSeconds, idleCount, Environment.NewLine));
                        customInfo.Append(string.Format("LastScan: W={0} G={1} | Forced: {2}{3}",
                            coord._lastScanWeldCandidateCount, coord._lastScanGrindCandidateCount,
                            coord._rescanForced, Environment.NewLine));
                    }
                }
                else
                {
                    customInfo.Append(string.Format("Cluster: none{0}", Environment.NewLine));
                }
            }

            customInfo.Append(Environment.NewLine);

            if (State.SafeZoneAndShieldsChecked)
            {
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
            }

            customInfo.Append(Environment.NewLine);

            if (_Welder.Enabled && _Welder.IsWorking && _Welder.IsFunctional)
            {
                if ((Settings.Flags & SyncBlockSettings.Settings.ScriptControlled) != 0)
                {
                    if (Settings.CurrentPickedWeldingBlock != null)
                    {
                        customInfo.Append(Texts.Info_CurrentWeldEntity + Environment.NewLine);
                        customInfo.Append(string.Format(" -{0}" + Environment.NewLine, Settings.CurrentPickedWeldingBlock.BlockName()));
                    }

                    if (Settings.CurrentPickedGrindingBlock != null)
                    {
                        customInfo.Append(Texts.Info_CurrentGrindEntity + Environment.NewLine);
                        customInfo.Append(string.Format(" -{0}" + Environment.NewLine, Settings.CurrentPickedGrindingBlock.BlockName()));
                    }
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
                            MyComponentDefinition componentDefinition;
                            var name = MyDefinitionManager.Static.TryGetComponentDefinition(componentId, out componentDefinition) ? componentDefinition.DisplayNameText : component.Key.SubtypeName;
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
                                if (blockData.Block == null) continue;
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
                                if (blockData.Block == null) continue;
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
                            if (entityData.Entity == null) continue;
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
                if (!_Welder.Enabled) customInfo.Append($"[color=#FFFFFF00]{Texts.Info_BlockSwitchedOff}[/color]{Environment.NewLine}");
                else if (!_Welder.IsFunctional) customInfo.Append($"[color=#FFFFFF00]{Texts.Info_BlockDamaged}[/color]{Environment.NewLine}");
                else if (!_Welder.IsWorking) customInfo.Append($"[color=#FFFFFF00]{Texts.Info_BlockUnpowered}[/color]{Environment.NewLine}");
            }
        }
    }
}
