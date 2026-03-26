using Sandbox.Definitions;
using Sandbox.ModAPI;
using SKONanobotBuildAndRepairSystem.Models;
using SKONanobotBuildAndRepairSystem.Profiling;
using SKONanobotBuildAndRepairSystem.Utils;
using System;
using System.Collections.Generic;
using VRage;
using VRage.Game;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using static SKONanobotBuildAndRepairSystem.Utils.UtilsInventory;
using MyInventoryItem = VRage.Game.ModAPI.Ingame.MyInventoryItem;

namespace SKONanobotBuildAndRepairSystem
{
    public partial class NanobotSystem
    {
        /// <summary>
        /// Push ore/ingot out of the welder
        /// </summary>
        private void ServerTryPushInventory()
        {
            var profilerTs = MethodProfiler.Start();
            try
            {
            if ((Settings.Flags & (SyncBlockSettings.Settings.PushIngotOreImmediately | SyncBlockSettings.Settings.PushComponentImmediately | SyncBlockSettings.Settings.PushItemsImmediately)) == 0)
                return;

            // FEAT-037: Compute time since last push once for adaptive interval check.
            var secondsSinceLastPush = MyAPIGateway.Session.ElapsedPlayTime.Subtract(_TryAutoPushInventoryLast).TotalSeconds;
            if (secondsSinceLastPush <= 5)
                return;

            // BUG-016: Skip push if all push targets are known to be full.
            // The flag is reset when push targets are rescanned.
            if (_PushTargetsFull)
                return;

            var welderInventory = _Welder.GetInventory(0);
            if (welderInventory != null)
            {
                if (welderInventory.Empty()) return;

                // FEAT-037: Extended push interval when welder has space.
                // During grinding, items accumulate slowly. Batch transfers by waiting
                // 10s instead of 5s when inventory is < 75% full. Cuts push frequency
                // ~50% without risking overflow (CheckAndUpdateInventoryFull catches it).
                if (secondsSinceLastPush < 10
                    && (float)welderInventory.CurrentVolume < (float)welderInventory.MaxVolume * 0.75f)
                    return;
                var lastPush = MyAPIGateway.Session.ElapsedPlayTime;
                var anyPushed = false;
                var anyAttempted = false;

                _TempInventoryItems.Clear();
                welderInventory.GetItems(_TempInventoryItems);
                for (int srcItemIndex = _TempInventoryItems.Count - 1; srcItemIndex >= 0; srcItemIndex--)
                {
                    var srcItem = _TempInventoryItems[srcItemIndex];
                    if (srcItem.Type.TypeId == typeof(MyObjectBuilder_Ore).Name || srcItem.Type.TypeId == typeof(MyObjectBuilder_Ingot).Name)
                    {
                        if ((Settings.Flags & SyncBlockSettings.Settings.PushIngotOreImmediately) != 0)
                        {
                            anyAttempted = true;
                            anyPushed = welderInventory.PushComponents(_PossiblePushTargets, null, srcItemIndex, srcItem) || anyPushed;
                            _TryAutoPushInventoryLast = lastPush;
                        }
                    }
                    else if (srcItem.Type.TypeId == typeof(MyObjectBuilder_Component).Name)
                    {
                        if ((Settings.Flags & SyncBlockSettings.Settings.PushComponentImmediately) != 0)
                        {
                            anyAttempted = true;
                            anyPushed = welderInventory.PushComponents(_PossiblePushTargets, null, srcItemIndex, srcItem) || anyPushed;
                            _TryAutoPushInventoryLast = lastPush;
                        }
                    }
                    else
                    {
                        //Any kind of items (Tools, Weapons, Ammo, Bottles, ..)
                        if ((Settings.Flags & SyncBlockSettings.Settings.PushItemsImmediately) != 0)
                        {
                            anyAttempted = true;
                            anyPushed = welderInventory.PushComponents(_PossiblePushTargets, null, srcItemIndex, srcItem) || anyPushed;
                            _TryAutoPushInventoryLast = lastPush;
                        }
                    }
                }
                _TempInventoryItems.Clear();

                // BUG-016: If we attempted to push but nothing moved, mark push targets as full.
                // This prevents constant iteration over full containers every tick.
                // The flag is reset when push targets are rescanned (~30s).
                if (anyAttempted && !anyPushed)
                {
                    _PushTargetsFull = true;
                }
                else if (anyPushed)
                {
                    _PushTargetsFull = false;
                }
            }

            }
            finally
            {
                if (profilerTs != 0L)
                {
                    MethodProfiler.StopAndLog("ServerTryPushInventory", profilerTs, () =>
                        string.Format("entityId={0};pushOre={1};pushComp={2};pushItems={3};pushTargets={4}",
                            _Welder.EntityId,
                            (Settings.Flags & SyncBlockSettings.Settings.PushIngotOreImmediately) != 0,
                            (Settings.Flags & SyncBlockSettings.Settings.PushComponentImmediately) != 0,
                            (Settings.Flags & SyncBlockSettings.Settings.PushItemsImmediately) != 0,
                            _PossiblePushTargets.Count));
                }
            }
        }

        /// <summary>
        /// Check if the transport inventory is empty after delivering/grinding/collecting, if not move items back to welder inventory
        /// </summary>
        private bool ServerEmptyTransportInventory(bool push)
        {
            var profilerTs = MethodProfiler.Start();
            var empty = _TransportInventory.Empty();
            if (!empty)
            {
                if (!CreativeModeActive)
                {
                    var welderInventory = _Welder.GetInventory(0);
                    if (welderInventory != null)
                    {
                        if (push && !welderInventory.Empty() && !_PushTargetsFull)
                        {
                            if (MyAPIGateway.Session.ElapsedPlayTime.Subtract(_TryPushInventoryLast).TotalSeconds > 5 && welderInventory.MaxVolume - welderInventory.CurrentVolume < _TransportInventory.CurrentVolume * 1.5f)
                            {
                                if (!welderInventory.PushComponents(_PossiblePushTargets, null))
                                {
                                    // BUG-016: Mark push targets as full to avoid retrying every tick.
                                    _PushTargetsFull = true;
                                    _TryPushInventoryLast = MyAPIGateway.Session.ElapsedPlayTime;
                                }
                                else
                                {
                                    _PushTargetsFull = false;
                                }
                            }
                        }

                        _TempInventoryItems.Clear();
                        _TransportInventory.GetItems(_TempInventoryItems);

                        for (int srcItemIndex = _TempInventoryItems.Count - 1; srcItemIndex >= 0; srcItemIndex--)
                        {
                            var item = _TempInventoryItems[srcItemIndex];
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

                        _TempInventoryItems.Clear();
                    }
                }
                else
                {
                    _TransportInventory.Clear();
                }

                empty = _TransportInventory.Empty();
            }

            State.InventoryFull = !empty;
            if (profilerTs != 0L)
            {
                MethodProfiler.StopAndLog("ServerEmptyTransportInventory", profilerTs, () =>
                    string.Format("entityId={0};push={1};empty={2};transportVol={3:F3};inventoryFull={4};sources={5};pushTargets={6}",
                        _Welder.EntityId, push, empty,
                        (float)_TransportInventory.CurrentVolume, State.InventoryFull,
                        _PossibleSources.Count, _PossiblePushTargets.Count));
            }
            return empty;
        }

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

                _TempInventoryItems.Clear();
                srcInventory.GetItems(_TempInventoryItems);
                for (int srcItemIndex = _TempInventoryItems.Count - 1; srcItemIndex >= 0; srcItemIndex--)
                {
                    var srcItem = srcInventory.GetItemByID(_TempInventoryItems[srcItemIndex].ItemId);
                    if (srcItem == null) continue;

                    var definition = MyDefinitionManager.Static.GetPhysicalItemDefinition(srcItem.Content.GetId());
                    if (definition == null) continue;

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
                _TempInventoryItems.Clear();
            }
            return running;
        }

        /// <summary>
        /// Pull components into welder
        /// </summary>
        private bool PullComponents(MyDefinitionId componentId, float volume, ref int neededAmount, ref float remainingVolume)
        {
            var profilerTs = MethodProfiler.Start();
            var startNeeded = neededAmount;
            int availAmount = 0;
            var welderInventory = _Welder.GetInventory(0);
            var maxpossibleAmount = Math.Min(neededAmount, (int)Math.Ceiling(remainingVolume / volume));

            if (maxpossibleAmount <= 0) return false;

            var picked = false;

            // BUG-057: Reuse pooled list across source iterations to avoid per-call allocation.
            _TempPullInventoryItems.Clear();

            lock (_PossibleSources)
            {
                foreach (var srcInventory in _PossibleSources)
                {
                    var srcOwner = srcInventory.Owner as IMyEntity;
                    if (srcOwner == null || srcOwner.MarkedForClose) continue;

                    //Pre Test is 10 timers faster then get the whole list (as copy!) and iterate for nothing
                    if (srcInventory.FindItem(componentId) != null && srcInventory.CanTransferItemTo(welderInventory, componentId))
                    {
                        _TempPullInventoryItems.Clear();
                        srcInventory.GetItems(_TempPullInventoryItems);
                        for (int srcItemIndex = _TempPullInventoryItems.Count - 1; srcItemIndex >= 0; srcItemIndex--)
                        {
                            var srcItem = _TempPullInventoryItems[srcItemIndex];
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
                    }
                    if (maxpossibleAmount <= 0) return picked;
                }
            }

            if (profilerTs != 0L)
            {
                MethodProfiler.StopAndLog("PullComponents", profilerTs, () =>
                    string.Format("entityId={0};component={1};startNeeded={2};picked={3};sources={4}",
                        _Welder.EntityId, componentId.SubtypeName, startNeeded, picked, _PossibleSources.Count));
            }
            return picked;
        }

        private bool IsTransportRunning(TimeSpan playTime)
        {
            if (State.CurrentTransportStartTime > TimeSpan.Zero)
            {
                // Transport started
                if (State.CurrentTransportIsPick)
                {
                    if (!ServerEmptyTransportInventory(true))
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
    }
}
