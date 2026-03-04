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

            if (_Welder.Closed || _Welder.MarkedForClose)
                return;

            var ready = _Welder.Enabled && _Welder.IsWorking && _Welder.IsFunctional;

            IMySlimBlock currentWeldingBlock = null;
            IMySlimBlock currentGrindingBlock = null;

            var playTime = MyAPIGateway.Session.ElapsedPlayTime;
            var isFullInventoryAndPicking = State.InventoryFull && State.CurrentTransportIsPick;

            if (ready)
            {
                ServerTryPushInventory();

                if (isFullInventoryAndPicking)
                {
                    State.LastTransportTarget = State.CurrentTransportTarget;
                    State.CurrentTransportTarget = null;
                    State.CurrentTransportStartTime = TimeSpan.Zero;
                    transporting = false;
                }
                else
                {
                    transporting = IsTransportRunnning(playTime);
                }

                if (transporting && State.CurrentTransportIsPick) needgrinding = true;

                if ((Settings.Flags & SyncBlockSettings.Settings.ComponentCollectIfIdle) == 0 && !transporting)
                    ServerTryCollectingFloatingTargets(out collecting, out needcollecting, out transporting);

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
                    State.CurrentTransportStartTime = TimeSpan.Zero;
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
                    StartAsyncUpdateSourcesAndTargets(false, new List<NanobotSystem>(Mod.NanobotSystems.Values)); //Scan immediately once for new targets
                }
            }

            // When transporting components for welding (delivery, not a pick), preserve the
            // previous NeedWelding state so the "Blocks to Build" panel remains visible and
            // MissingComponents doesn't flicker while the transport timer is ticking.
            if (transporting && !State.CurrentTransportIsPick && !needwelding && State.NeedWelding)
            {
                needwelding = State.NeedWelding;
                currentWeldingBlock = State.CurrentWeldingBlock;
            }

            var readyChanged = State.Ready != ready;
            var weldingChanged = State.Welding != welding;
            var needWeldingChanged = State.NeedWelding != needwelding;
            var grindingChanged = State.Grinding != grinding;
            var needGrindingChanged = State.NeedGrinding != needgrinding;
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

            if (missingComponentsChanged || possibleWeldTargetsChanged || possibleGrindTargetsChanged || possibleFloatingTargetsChanged || transportChanged) State.HasChanged();

            if (MyAPIGateway.Session.IsServer)
            {
                if (State.IsTransmitNeeded() && MyAPIGateway.Multiplayer.MultiplayerActive && MyAPIGateway.Session.ElapsedPlayTime.Subtract(_UpdateStateTransmitLast).TotalSeconds >= _UpdateStateTransmitInterval)
                {
                    _UpdateStateTransmitLast = MyAPIGateway.Session.ElapsedPlayTime;
                    _UpdateStateTransmitInterval = _RandomDelay.Next(TransmitStateMinIntervalSeconds, TransmitStateMaxIntervalSeconds + 1);
                    NetworkMessagingHandler.MsgBlockStateSend(0, this);
                }
            }

            UpdateCustomInfo(
                missingComponentsChanged ||
                possibleWeldTargetsChanged ||
                possibleGrindTargetsChanged ||
                possibleFloatingTargetsChanged ||
                readyChanged ||
                weldingChanged ||
                needWeldingChanged ||
                grindingChanged ||
                needGrindingChanged ||
                inventoryFullChanged ||
                limitsExceededChanged ||
                transportChanged);
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

                _TempInventoryItems.Clear();
                welderInventory.GetItems(_TempInventoryItems);
                for (int srcItemIndex = _TempInventoryItems.Count - 1; srcItemIndex >= 0; srcItemIndex--)
                {
                    var srcItem = _TempInventoryItems[srcItemIndex];
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
                _TempInventoryItems.Clear();
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

            // Build once per call — O(N) — so the per-block check is O(1) instead of O(N).
            var activeGridSystems = Mod.Settings.DisableLimitSystemsPerTargetGrid
                ? null : BuildActiveGridMap();

            lock (State.PossibleGrindTargets)
            {
                //foreach (var targetData in State.PossibleGrindTargets)
                //{
                //    var cubeGrid = targetData.Block.CubeGrid as MyCubeGrid;
                //    if (!cubeGrid.IsPowered && !cubeGrid.IsStatic) cubeGrid.Physics.ClearSpeed();
                //}

                foreach (var targetData in State.PossibleGrindTargets)
                {
                    if (targetData.Block != null && targetData.Block.FatBlock != null && targetData.Block.FatBlock.Closed)
                    {
                        continue;
                    }

                    if (activeGridSystems != null)
                    {
                        int activeCount;
                        activeGridSystems.TryGetValue(targetData.Block.CubeGrid.EntityId, out activeCount);
                        if (activeCount >= Mod.Settings.MaxSystemsPerTargetGrid)
                            continue;
                    }

                    if (Mod.Settings.AssignToSystemEnabled && _Welder.IsWorking && _Welder.Enabled && Settings.CurrentPickedGrindingBlock == null && !targetData.Block.AssignToSystem(_Welder.EntityId))
                    {
                        continue;
                    }

                    if (((Settings.Flags & SyncBlockSettings.Settings.ScriptControlled) != 0) && targetData.Block != Settings.CurrentPickedGrindingBlock)
                    {
                        continue;
                    }

                    if (!targetData.Block.IsDestroyed)
                    {
                        needgrinding = true;

                        grinding = ServerDoGrind(targetData, out transporting);

                        if (grinding)
                        {
                            currentGrindingBlock = targetData.Block;
                            break; //Only grind one block at once
                        }

                        if (Mod.Settings.AssignToSystemEnabled && (targetData.Ignore || targetData.Block.IsFullyDismounted))
                        {
                            // Release the block from this system.
                            targetData.Block.ReleaseFromSystem();
                        }
                    }
                }
            }

            // Faction reputation when grinding for not owned grids.
            if (Mod.Settings.DecreaseFactionReputationOnGrinding && currentGrindingBlock != null)
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
        /// Builds a map of gridEntityId → count of OTHER NanobotSystem instances currently
        /// welding or grinding a block on that grid. Called once per grind/weld pass so the
        /// per-block limit check is a O(1) dictionary lookup instead of an O(N) scan.
        /// </summary>
        private Dictionary<long, int> BuildActiveGridMap()
        {
            var map = new Dictionary<long, int>();
            foreach (var system in Mod.NanobotSystems.Values)
            {
                if (system == this) continue;
                long? weldGridId  = system.State.CurrentWeldingBlock?.CubeGrid?.EntityId;
                long? grindGridId = system.State.CurrentGrindingBlock?.CubeGrid?.EntityId;
                if (weldGridId.HasValue)
                {
                    int c;
                    map[weldGridId.Value] = map.TryGetValue(weldGridId.Value, out c) ? c + 1 : 1;
                }
                if (grindGridId.HasValue && grindGridId != weldGridId)
                {
                    int c;
                    map[grindGridId.Value] = map.TryGetValue(grindGridId.Value, out c) ? c + 1 : 1;
                }
            }
            return map;
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

            // Build once per call — O(N) — so the per-block check is O(1) instead of O(N).
            var activeGridSystems = Mod.Settings.DisableLimitSystemsPerTargetGrid
                ? null : BuildActiveGridMap();

            lock (State.PossibleWeldTargets)
            {
                // Set to true once the locked-on block completes this tick so the loop
                // can find the next target immediately, without actually welding it
                // (only one block is welded per tick). The next target is returned as
                // currentWeldingBlock so it starts welding on the very next update cycle.
                var lookingForNext = false;
                foreach (var targetData in State.PossibleWeldTargets)
                {
                    if (!lookingForNext && State.CurrentWeldingBlock != null && State.CurrentWeldingBlock != targetData.Block)
                    {
                        continue;
                    }

                    if (((Settings.Flags & SyncBlockSettings.Settings.ScriptControlled) != 0) && targetData.Block != Settings.CurrentPickedWeldingBlock) continue;
                    if (((Settings.Flags & SyncBlockSettings.Settings.ScriptControlled) != 0) || (!targetData.Ignore && Weldable(targetData)))
                    {
                        if (targetData.Block != null && targetData.Block.FatBlock != null && targetData.Block.FatBlock.Closed)
                        {
                            continue;
                        }

                        if (activeGridSystems != null)
                        {
                            int activeCount;
                            activeGridSystems.TryGetValue(targetData.Block.CubeGrid.EntityId, out activeCount);
                            if (activeCount >= Mod.Settings.MaxSystemsPerTargetGrid)
                                continue;
                        }

                        if (Mod.Settings.AssignToSystemEnabled && _Welder.IsWorking && _Welder.Enabled && !_Welder.HelpOthers && Settings.CurrentPickedWeldingBlock == null && !targetData.Block.AssignToSystem(_Welder.EntityId))
                        {
                            continue;
                        }

                        needwelding = true;

                        if (lookingForNext)
                        {
                            // The previous block just completed this tick. Lock on to this
                            // next eligible block so it starts welding next cycle immediately.
                            currentWeldingBlock = targetData.Block;
                            break;
                        }

                        if (!transporting) //Transport needs to be weld afterwards
                        {
                            transporting = ServerFindMissingComponents(targetData);
                        }

                        welding = ServerDoWeld(targetData);

                        ServerEmptyTranportInventory(false);

                        if (targetData.Ignore)
                        {
                            if (Mod.Settings.AssignToSystemEnabled) targetData.Block.ReleaseFromSystem();
                            State.PossibleWeldTargets.ChangeHash();
                            // Block completed this tick. Clear lock-on and search for the
                            // next target in the same iteration. welding stays true so
                            // state and effects remain correct for this tick.
                            State.CurrentWeldingBlock = null;
                            lookingForNext = true;
                            // Do NOT break — fall through to find the next target.
                        }
                        else if (welding || transporting)
                        {
                            currentWeldingBlock = targetData.Block;
                            break; //Only weld one block at once (do not split over all blocks as the base shipwelder does)
                        }
                    }
                    else
                    {
                        if (targetData.Ignore)
                        {
                            if (Mod.Settings.AssignToSystemEnabled) targetData.Block.ReleaseFromSystem();
                            State.PossibleWeldTargets.ChangeHash();
                        }
                        // Current tracked block is no longer weldable; clear the lock so the
                        // loop can find the next eligible block in this same tick.
                        if (State.CurrentWeldingBlock == targetData.Block)
                        {
                            State.CurrentWeldingBlock = null;
                        }
                        // TODO: Cooldown as the block is not weldable...
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
                // Keep this at false, otherwise it will not work with Multigrid Projections.
                if (target.CanBuild(false))
                {
                    targetData.Ignore = false;
                    return true;
                }

                // Is the block already created (maybe by user or an other BaR block) ->
                // After creation we can't welding this projected block, we have to find the 'physical' block instead.
                //var cubeGridProjected = target.CubeGrid as MyCubeGrid;
                //if (cubeGridProjected != null && cubeGridProjected.Projector != null)
                //{
                //    var cubeGrid = cubeGridProjected.Projector.CubeGrid;
                //    var blockPos = cubeGrid.WorldToGridInteger(cubeGridProjected.GridIntegerToWorld(target.Position));
                //    target = cubeGrid.GetCubeBlock(blockPos);

                //    if (target != null)
                //    {
                //        targetData.Block = target;
                //        targetData.Attributes &= ~TargetBlockData.AttributeFlags.Projected;
                //        return Weldable(targetData);
                //    }
                //}

                targetData.Ignore = true;
                return false;
            }

            var isFunctionalOnly = (Settings.WeldOptions & AutoWeldOptions.FunctionalOnly) != 0;
            var weld = (!IsWeldIntegrityReached(target) || target.NeedRepair(isFunctionalOnly)) && !IsFriendlyDamage(target);

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

                if ((CreativeModeActive || (item != null && item.Amount >= 1)) && cubeGridProjected != null && cubeGridProjected.Projector != null)
                {
                    if (_Welder.IsWithinWorldLimits(cubeGridProjected.Projector, blockDefinition.BlockPairName, blockDefinition.PCU))
                    {
                        //var blockBuildIntegrity = target.Integrity;

                        if (!cubeGridProjected.Projector.Closed && !cubeGridProjected.Projector.CubeGrid.Closed && (target.FatBlock == null || !target.FatBlock.Closed))
                        {
                            var proj = cubeGridProjected.Projector as Sandbox.ModAPI.IMyProjector;
                            proj.Build(target, _Welder.OwnerId, _Welder.EntityId, true, _Welder.SlimBlock.BuiltBy);
                        }

                        // TODO Check this again.
                        // _TransportInventory.RemoveItems(item.ItemId, 1);

                        //After creation we can't welding this projected block, we have to find the 'physical' block instead.
                        var cubeGrid = cubeGridProjected.Projector.CubeGrid;
                        var blockPos = cubeGrid.WorldToGridInteger(cubeGridProjected.GridIntegerToWorld(target.Position));
                        target = cubeGrid.GetCubeBlock(blockPos);

                        if (target != null)
                        {
                            targetData.Block = target;
                            targetData.Attributes &= ~TargetBlockData.AttributeFlags.Projected;
                            created = true;
                        }
                        else
                        {
                            targetData.Ignore = true;
                        }

                        //var newIntegrity = target?.BuildIntegrity;
                        //if (newIntegrity > blockBuildIntegrity)
                        //{
                        //    _TransportInventory.RemoveItems(item.ItemId, 1);
                        //}
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
                    welding = target.CanContinueBuild(_TransportInventory) || CreativeModeActive;

                    if (welding)
                    {
                        target.IncreaseMountLevel(MyAPIGateway.Session.WelderSpeedMultiplier * Mod.Settings.Welder.WeldingMultiplier * WELDER_AMOUNT_PER_SECOND, _Welder.OwnerId, welderInventory, MyAPIGateway.Session.WelderSpeedMultiplier * Mod.Settings.Welder.WeldingMultiplier * WELDER_MAX_REPAIR_BONE_MOVEMENT_SPEED, _Welder.HelpOthers);
                    }

                    if (IsWeldIntegrityReached(target))
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

            if (transporting)
                return false;

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
                            if (character != null && character.IsBot && Mod.Settings.DeleteBotsWhenDead)
                            {
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
    }
}
