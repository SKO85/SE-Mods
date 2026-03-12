using Sandbox.Definitions;
using Sandbox.ModAPI;
using SKONanobotBuildAndRepairSystem.Models;
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
        /// Check if the transport inventory is empty after delivering/grinding/collecting, if not move items back to welder inventory
        /// </summary>
        private bool ServerEmptyTransportInventory(bool push)
        {
            var empty = _TransportInventory.Empty();
            if (!empty)
            {
                if (!CreativeModeActive)
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
