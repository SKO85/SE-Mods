using Sandbox.Definitions;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using SKONanobotBuildAndRepairSystem.Models;
using SKONanobotBuildAndRepairSystem.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using VRage;
using VRage.Game;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRage.ObjectBuilders;
using static SKONanobotBuildAndRepairSystem.Utils.UtilsInventory;

namespace SKONanobotBuildAndRepairSystem
{
    partial class NanobotSystem
    {
        /// <summary>
        /// Try to find an the missing components and moves them into welder inventory
        /// </summary>
        private bool ServerFindMissingComponents(TargetBlockData targetData)
        {
            try
            {
                var playTime = MyAPIGateway.Session.ElapsedPlayTime;

                if (IsTransportRunning(playTime))
                    return true;

                var remainingVolume = _MaxTransportVolume;
                _TempMissingComponents.Clear();
                var picked = false;
                var cubeGrid = targetData.Block.CubeGrid as MyCubeGrid;

                if ((targetData.Attributes & TargetBlockData.AttributeFlags.Projected) != 0)
                {
                    targetData.Block.GetMissingComponents(_TempMissingComponents, UtilsInventory.IntegrityLevel.Create);
                    if (_TempMissingComponents.Count > 0)
                    {
                        picked = ServerFindMissingComponents(targetData, ref remainingVolume);

                        if (picked && Settings.WeldOptions != AutoWeldOptions.WeldSkeleton)
                        {
                            if (((Settings.Flags & SyncBlockSettings.Settings.UseIgnoreColor) == 0) || !IsColorNearlyEquals(Settings.IgnoreColorPacked, targetData.Block.GetColorMask()))
                            {
                                //Block could be created and should be welded -> so retrieve the remaining material also
                                var keyValue = _TempMissingComponents.ElementAt(0);
                                _TempMissingComponents.Clear();

                                targetData.Block.GetMissingComponents(_TempMissingComponents, Settings.WeldOptions == AutoWeldOptions.WeldFull ? UtilsInventory.IntegrityLevel.Complete : UtilsInventory.IntegrityLevel.Functional);

                                if (_TempMissingComponents.ContainsKey(keyValue.Key))
                                {
                                    if (_TempMissingComponents[keyValue.Key] <= keyValue.Value)
                                    {
                                        _TempMissingComponents.Remove(keyValue.Key);
                                    }
                                    else
                                    {
                                        _TempMissingComponents[keyValue.Key] -= keyValue.Value;
                                    }
                                }
                            }
                        }
                        else if (picked)
                        {
                            // WeldSkeleton: only the 1 create-item was needed; clear so the
                            // generic pick at line 74 does not fetch it a second time.
                            _TempMissingComponents.Clear();
                        }
                    }
                }
                else
                {
                    targetData.Block.GetMissingComponents(_TempMissingComponents, Settings.WeldOptions == AutoWeldOptions.WeldFull ? UtilsInventory.IntegrityLevel.Complete : UtilsInventory.IntegrityLevel.Functional);
                }

                if (_TempMissingComponents.Count > 0)
                {
                    ServerFindMissingComponents(targetData, ref remainingVolume);
                }

                if (remainingVolume < _MaxTransportVolume || (CreativeModeActive && _TempMissingComponents.Count > 0))
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

        /// <param name="targetData"></param>
        /// <returns></returns>
        private bool ServerFindMissingComponents(TargetBlockData targetData, ref float remainingVolume)
        {
            var picked = false;
            foreach (var keyValue in _TempMissingComponents)
            {
                var componentId = new MyDefinitionId(typeof(MyObjectBuilder_Component), keyValue.Key);
                var definition = MyDefinitionManager.Static.GetPhysicalItemDefinition(componentId);
                if (definition == null) continue;
                int neededAmount = keyValue.Value;

                picked = ServerPickFromWelder(componentId, definition.Volume, ref neededAmount, ref remainingVolume) || picked;

                if (neededAmount > 0 && remainingVolume > 0)
                {
                    picked = PullComponents(componentId, definition.Volume, ref neededAmount, ref remainingVolume) || picked;
                }

                if (neededAmount > 0 && remainingVolume > 0)
                {
                    AddToMissingComponents(componentId, neededAmount);
                }

                if (remainingVolume <= 0)
                {
                    break;
                }
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

            _TempInventoryItems.Clear();
            welderInventory.GetItems(_TempInventoryItems);
            for (int i1 = _TempInventoryItems.Count - 1; i1 >= 0; i1--)
            {
                var srcItem = _TempInventoryItems[i1];
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
            _TempInventoryItems.Clear();
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
                if (!CreativeModeActive)
                {
                    var welderInventory = _Welder.GetInventory(0);
                    if (welderInventory != null)
                    {
                        if (push && !welderInventory.Empty())
                        {
                            if (MyAPIGateway.Session.ElapsedPlayTime.Subtract(_TryPushInventoryLast).TotalSeconds > 5 && welderInventory.MaxVolume - welderInventory.CurrentVolume < _TransportInventory.CurrentVolume * 1.5f)
                            {
                                // Prefer cargo containers; fall back to all possible sources
                                // (e.g. ore → refineries) when cargo is full so the welder
                                // can drain and break the inventory-full deadlock.
                                List<IMyInventory> pushTargetsSnapshot;
                                lock (_PossibleSources)
                                {
                                    bool cargoHasSpace = false;
                                    foreach (var inv in _PossiblePushTargets)
                                        if (inv.MaxVolume > inv.CurrentVolume) { cargoHasSpace = true; break; }
                                    pushTargetsSnapshot = cargoHasSpace
                                        ? new List<IMyInventory>(_PossiblePushTargets)
                                        : new List<IMyInventory>(_PossibleSources);
                                }
                                if (!welderInventory.PushComponents(pushTargetsSnapshot, null))
                                {
                                    // Push attempted but nothing moved — back off
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

                _TempInventoryItems.Clear();
                srcInventory.GetItems(_TempInventoryItems);
                for (int srcItemIndex = _TempInventoryItems.Count - 1; srcItemIndex >= 0; srcItemIndex--)
                {
                    var srcItem = srcInventory.GetItemByID(_TempInventoryItems[srcItemIndex].ItemId);
                    if (srcItem == null) continue;

                    var definition = MyDefinitionManager.Static.GetPhysicalItemDefinition(srcItem.Content.GetId());
                    if (definition == null || definition.Volume <= 0) continue;

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

        private bool EmptyFloatingObject(MyFloatingObject floating, IMyInventory dstInventory, out bool isEmpty)
        {
            var running = false;
            isEmpty = floating.WasRemovedFromWorld || floating.MarkedForClose;
            if (!isEmpty)
            {
                var remainingVolume = _MaxTransportVolume - (double)dstInventory.CurrentVolume;

                var definition = MyDefinitionManager.Static.GetPhysicalItemDefinition(floating.Item.Content.GetId());
                if (definition == null || definition.Volume <= 0)
                {
                    isEmpty = floating.WasRemovedFromWorld || floating.MarkedForClose;
                    return running;
                }
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
                        // Reuse pre-allocated list to avoid a new heap allocation per source inventory
                        _TempPullComponentItems.Clear();
                        srcInventory.GetItems(_TempPullComponentItems);
                        var tempInventoryItems = _TempPullComponentItems;
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

            return picked;
        }
    }
}
