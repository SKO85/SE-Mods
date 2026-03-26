using Sandbox.Definitions;
using Sandbox.ModAPI;
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
                    MethodProfiler.StopAndLog("UpdateCustomInfo", profilerTs, () =>
                        string.Format("entityId={0}", _Welder.EntityId));
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

            var resourceSink = _Welder.ResourceSink as Sandbox.Game.EntityComponents.MyResourceSinkComponent;
            if (resourceSink != null)
            {
                customInfo.Append($"Power: ");
                MyValueFormatter.AppendWorkInBestUnit(PowerHelper.ComputeRequiredElectricPower(this), customInfo);
                customInfo.Append($" / ");
                MyValueFormatter.AppendWorkInBestUnit(resourceSink.MaxRequiredInputByType(ElectricityId), customInfo);
                customInfo.Append(Environment.NewLine);
            }

            if (Mod.Settings.DebugMode && !MyAPIGateway.Utilities.IsDedicated)
            {
                customInfo.Append(string.Format("Version: {0}{1}", Constants.ModVersion, Environment.NewLine));
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
                }
                else
                {
                    customInfo.Append(string.Format("Cluster: none{0}", Environment.NewLine));
                }
                customInfo.Append(string.Format("MaxSystems/Grid: {0} | EmptyGrids: {1}{2}", Mod.Settings.MaxSystemsPerTargetGrid, _EmptyGridCache.Count, Environment.NewLine));
                var clusterMembers = cluster != null ? cluster.Members.Count : 1;
                var staggerCap = Mod.GetEffectiveStaggerGroupCount();
                var effectiveStagger = clusterMembers < 5 ? 1 : Math.Min(staggerCap, clusterMembers - 3);
                customInfo.Append(string.Format("Total BaRs: {0} | Stagger: {1}/{2}{3} | GrindBudget: {4}{5}{6}",
                    Mod.NanobotSystems.Count,
                    effectiveStagger, staggerCap,
                    Mod.Settings.StaggerGroupCount > 0 ? "" : " (auto)",
                    Mod.GetEffectiveMaxGrindsPerTick(),
                    Mod.Settings.MaxGrindsPerTick > 0 ? "" : " (auto)",
                    Environment.NewLine));
                customInfo.Append(string.Format("ModSettings.xml: {0}{1}",
                    Mod.CustomSettingsLoaded ? "Loaded (custom)" : "Not found (defaults)",
                    Environment.NewLine));
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
