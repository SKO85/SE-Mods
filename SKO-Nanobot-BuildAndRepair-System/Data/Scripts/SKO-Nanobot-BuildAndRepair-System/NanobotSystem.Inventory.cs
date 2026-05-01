using Sandbox.Definitions;
using Sandbox.ModAPI;
using SKONanobotBuildAndRepairSystem.Models;
using SKONanobotBuildAndRepairSystem.Profiling;
using SKONanobotBuildAndRepairSystem.Utils;
using System;
using System.Collections.Generic;
using System.Diagnostics;
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
        // Cheap signature for the current push-target list. XOR of owner EntityIds
        // mixed with the count; detects container swaps that leave the count unchanged.
        private long ComputePushTargetsSignature()
        {
            long sig = _PossiblePushTargets.Count;
            for (int i = 0; i < _PossiblePushTargets.Count; i++)
            {
                var inv = _PossiblePushTargets[i];
                if (inv == null) continue;
                var owner = inv.Owner as IMyEntity;
                if (owner != null) sig ^= owner.EntityId;
            }
            return sig;
        }

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
                var itemCount = _TempInventoryItems.Count;

                // BUG-162: chunk push work to bound the per-tick cost. The original loop
                // walked every welder item × up to 93 push destinations per item; with a
                // full inventory this produced 40+ ms main-thread stalls. Cap each call by
                // wall-clock (~5 ms) and item count (4). Cursor walks backward from a saved
                // position; on reaching 0 it wraps to itemCount-1 for the NEXT call (never
                // wraps within a single call, because backward-only iteration is index-safe
                // when items get removed by full-stack transfer).
                const int MaxPushItemsPerCall = 4;
                var budgetTicks = Stopwatch.Frequency / 200; // 5 ms in Stopwatch ticks.
                var startTs = Stopwatch.GetTimestamp();
                var processed = 0;

                if (itemCount > 0)
                {
                    // Clamp cursor; items can be removed externally between calls.
                    if (_PushItemCursor < 0 || _PushItemCursor >= itemCount) _PushItemCursor = itemCount - 1;

                    var srcItemIndex = _PushItemCursor;
                    while (srcItemIndex >= 0)
                    {
                        if (processed >= MaxPushItemsPerCall) break;
                        if (Stopwatch.GetTimestamp() - startTs >= budgetTicks) break;

                        var srcItem = _TempInventoryItems[srcItemIndex];
                        bool eligible;
                        if (srcItem.Type.TypeId == typeof(MyObjectBuilder_Ore).Name || srcItem.Type.TypeId == typeof(MyObjectBuilder_Ingot).Name)
                            eligible = (Settings.Flags & SyncBlockSettings.Settings.PushIngotOreImmediately) != 0;
                        else if (srcItem.Type.TypeId == typeof(MyObjectBuilder_Component).Name)
                            eligible = (Settings.Flags & SyncBlockSettings.Settings.PushComponentImmediately) != 0;
                        else
                            eligible = (Settings.Flags & SyncBlockSettings.Settings.PushItemsImmediately) != 0;

                        if (eligible)
                        {
                            anyAttempted = true;
                            anyPushed = welderInventory.PushComponents(_PossiblePushTargets, null, srcItemIndex, srcItem) || anyPushed;
                            _TryAutoPushInventoryLast = lastPush;
                            processed++;
                        }
                        srcItemIndex--;
                    }

                    if (srcItemIndex < 0)
                    {
                        // Walked to the bottom: wrap cursor to the top for the next call so
                        // older items at higher indices get their turn.
                        _PushItemCursor = itemCount - 1;
                    }
                    else
                    {
                        _PushItemCursor = srcItemIndex;
                    }
                }
                _TempInventoryItems.Clear();

                // BUG-016: If we attempted to push but nothing moved, mark push targets as full.
                // This prevents constant iteration over full containers every tick.
                // The flag is reset when push targets change or after a safety backoff.
                // BUG-162: chunked iteration is OK to feed this flag — destination fullness is
                // a property of the push-target list, not of which welder items we sampled in
                // this call. If our 4-item chunk attempted at least one push and nothing moved,
                // it's a strong signal the destinations are full. The flag is cleared on any
                // successful push or when ComputePushTargetsSignature changes (containers swap).
                if (anyAttempted && !anyPushed)
                {
                    _PushTargetsFull = true;
                    _PushTargetsFullSignature = ComputePushTargetsSignature();
                    _PushTargetsFullSince = MyAPIGateway.Session.ElapsedPlayTime;
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
                        // BUG-114: gate the safety overflow push on the same Push* flags as
                        // ServerTryPushInventory. When all three flags are off the player has
                        // explicitly disabled automatic pushing — so the welder is allowed to
                        // fill, State.InventoryFull will trip, and grinding will pause until
                        // the player makes room. Without this gate, disabling all three flags
                        // had no effect on this branch and items kept flowing to push targets.
                        var pushFlagsActive = (Settings.Flags & (
                            SyncBlockSettings.Settings.PushIngotOreImmediately |
                            SyncBlockSettings.Settings.PushComponentImmediately |
                            SyncBlockSettings.Settings.PushItemsImmediately)) != 0;
                        if (push && pushFlagsActive && !welderInventory.Empty() && !_PushTargetsFull)
                        {
                            if (MyAPIGateway.Session.ElapsedPlayTime.Subtract(_TryPushInventoryLast).TotalSeconds > 5 && welderInventory.MaxVolume - welderInventory.CurrentVolume < _TransportInventory.CurrentVolume * 1.5f)
                            {
                                if (!welderInventory.PushComponents(_PossiblePushTargets, null))
                                {
                                    // BUG-016: Mark push targets as full to avoid retrying every tick.
                                    _PushTargetsFull = true;
                                    _PushTargetsFullSignature = ComputePushTargetsSignature();
                                    _PushTargetsFullSince = MyAPIGateway.Session.ElapsedPlayTime;
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

        // BUG-146: per-call profiling for EmptyBlockInventories. Engine-heavy on cargo
        // containers / refineries when grinding them down: srcInventory.GetItems +
        // GetItemByID per item + TransferItemFrom per item. Sub-timers identify whether
        // the spike is in enumeration, item lookup, or the actual transfer.
        private bool EmptyBlockInventories(IMyEntity entity, IMyInventory dstInventory, out bool isEmpty)
        {
            var profilerTs = MethodProfiler.Start();
            var running = false;
            var remainingVolume = _MaxTransportVolume - (float)dstInventory.CurrentVolume;
            isEmpty = true;
            var inventoriesScanned = 0;
            var inventoriesEmpty = 0;
            var itemsExamined = 0;
            var transferAttempts = 0;
            var transfersSucceeded = 0;
            var tsGetItems = 0L;
            var tsGetItemById = 0L;
            var tsTransfer = 0L;
            long tsMark;
            var entityId = entity != null ? entity.EntityId : 0L;
            var inventoryCount = entity != null ? entity.InventoryCount : 0;

            try
            {
                for (int i1 = 0; i1 < inventoryCount; i1++)
                {
                    var srcInventory = entity.GetInventory(i1);
                    inventoriesScanned++;
                    if (srcInventory.Empty()) { inventoriesEmpty++; continue; }

                    if (remainingVolume <= 0) return true; //No more transport volume

                    _TempInventoryItems.Clear();
                    // BUG-146 fix: gate Stopwatch.GetTimestamp on profilerTs so the calls
                    // are zero-cost when profiling is off (matches BUG-122 / BUG-141 pattern
                    // used in welding). Without the gate, this loop pays ~50ns × 3-per-item
                    // even in production where profiling never runs.
                    tsMark = profilerTs != 0L ? Stopwatch.GetTimestamp() : 0L;
                    srcInventory.GetItems(_TempInventoryItems);
                    if (tsMark != 0L) tsGetItems += Stopwatch.GetTimestamp() - tsMark;

                    for (int srcItemIndex = _TempInventoryItems.Count - 1; srcItemIndex >= 0; srcItemIndex--)
                    {
                        itemsExamined++;
                        tsMark = profilerTs != 0L ? Stopwatch.GetTimestamp() : 0L;
                        var srcItem = srcInventory.GetItemByID(_TempInventoryItems[srcItemIndex].ItemId);
                        if (tsMark != 0L) tsGetItemById += Stopwatch.GetTimestamp() - tsMark;
                        if (srcItem == null) continue;

                        var definition = MyDefinitionManager.Static.GetPhysicalItemDefinition(srcItem.Content.GetId());
                        if (definition == null) continue;

                        var maxpossibleAmountFP = Math.Min((float)srcItem.Amount, (remainingVolume / definition.Volume));
                        //Real Transport Volume is always bigger than logical _MaxTransportVolume so ceiling is no problem
                        var maxpossibleAmount = (MyFixedPoint)(definition.HasIntegralAmounts ? Math.Ceiling(maxpossibleAmountFP) : maxpossibleAmountFP);
                        transferAttempts++;
                        tsMark = profilerTs != 0L ? Stopwatch.GetTimestamp() : 0L;
                        var transferred = dstInventory.TransferItemFrom(srcInventory, srcItemIndex, null, true, maxpossibleAmount, false);
                        if (tsMark != 0L) tsTransfer += Stopwatch.GetTimestamp() - tsMark;
                        if (transferred)
                        {
                            transfersSucceeded++;
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
            finally
            {
                if (profilerTs != 0L)
                {
                    var tsFreq = Stopwatch.Frequency;
                    var _getItemsMs = tsGetItems * 1000.0 / tsFreq;
                    var _getItemByIdMs = tsGetItemById * 1000.0 / tsFreq;
                    var _transferMs = tsTransfer * 1000.0 / tsFreq;
                    var _entityId = entityId;
                    var _inventoryCount = inventoryCount;
                    var _inventoriesScanned = inventoriesScanned;
                    var _inventoriesEmpty = inventoriesEmpty;
                    var _itemsExamined = itemsExamined;
                    var _transferAttempts = transferAttempts;
                    var _transfersSucceeded = transfersSucceeded;
                    var _isEmptyResult = isEmpty;
                    var _running = running;
                    MethodProfiler.StopAndLog("EmptyBlockInventories", profilerTs, () =>
                        string.Format("entityId={0};inventoryCount={1};invScanned={2};invEmpty={3};itemsExamined={4};transferAttempts={5};transferSucceeded={6};isEmpty={7};running={8};getItemsMs={9:F3};getItemByIdMs={10:F3};transferMs={11:F3}",
                            _entityId, _inventoryCount, _inventoriesScanned, _inventoriesEmpty, _itemsExamined, _transferAttempts, _transfersSucceeded, _isEmptyResult, _running,
                            _getItemsMs, _getItemByIdMs, _transferMs));
                }
            }
        }

        // BUG-133: PullComponents (per-component source walk) retired in favor of
        // PullFromSourcesOnePass in NanobotSystem.Welding.cs, which iterates sources once
        // and matches against ALL remaining missing components. The old structure ran
        // FindItem N components × M sources times on cold-cache projected blocks (18.9 ms
        // observed). The replacement is O(sources + items) per call.
        // SourceHasComponentCache and the per-source FindItem are no longer needed; the
        // cache field in InventoryHelper has been removed alongside this method.

        private bool IsTransportRunning(TimeSpan playTime)
        {
            var profilerTs = MethodProfiler.Start();
            var result = false;
            if (State.CurrentTransportStartTime > TimeSpan.Zero)
            {
                // Transport started
                if (State.CurrentTransportIsPick)
                {
                    if (!ServerEmptyTransportInventory(true))
                    {
                        result = true;
                    }
                }

                if (!result && playTime.Subtract(State.CurrentTransportStartTime) < State.CurrentTransportTime)
                {
                    // Last transport still running -> wait
                    result = true;
                }

                if (!result)
                {
                    State.CurrentTransportStartTime = TimeSpan.Zero;
                    State.LastTransportTarget = State.CurrentTransportTarget;
                    State.CurrentTransportTarget = null;
                }
            }
            else State.CurrentTransportTarget = null;

            if (profilerTs != 0L)
            {
                var _result = result;
                var _elapsedS = result ? playTime.Subtract(State.CurrentTransportStartTime).TotalSeconds : 0.0;
                var _totalS = result ? State.CurrentTransportTime.TotalSeconds : 0.0;
                var _isPick = State.CurrentTransportIsPick;
                MethodProfiler.StopAndLog("IsTransportRunning", profilerTs, () =>
                    string.Format("entityId={0};running={1};elapsedS={2:F3};totalS={3:F3};isPick={4}",
                        _Welder.EntityId, _result, _elapsedS, _totalS, _isPick));
            }
            return result;
        }
    }
}
