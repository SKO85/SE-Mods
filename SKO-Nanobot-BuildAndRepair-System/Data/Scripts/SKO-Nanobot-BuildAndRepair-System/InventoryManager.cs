using Sandbox.ModAPI;
using System;
using System.Collections.Generic;
using VRage.Game;
using VRage.Game.ModAPI;
using MyInventoryItem = VRage.Game.ModAPI.Ingame.MyInventoryItem;

namespace SKONanobotBuildAndRepairSystem
{
    public static class InventoryManager
    {
    private static readonly Random _Rng = new Random();
        public static void TryPushInventory(NanobotBuildAndRepairSystemBlock block)
        {
            if ((block.Settings.Flags & (SyncBlockSettings.Settings.PushIngotOreImmediately | SyncBlockSettings.Settings.PushComponentImmediately | SyncBlockSettings.Settings.PushItemsImmediately)) == 0)
                return;

            var now = MyAPIGateway.Session.ElapsedPlayTime;
            // Quick probe: is there any destination with free space?
            var anyDestHasFree = false;
            lock (block._PossibleSources)
            {
                anyDestHasFree = DestinationCapacityCache.AnyDestinationHasFree(block._PossibleSources, now);
            }
            // If all destinations are full, increase backoff and skip this cycle to avoid hot-looping
            if (!anyDestHasFree)
            {
                var next = block._InventoryPushBackoffSeconds == 0 ? _Rng.Next(5, 11) : Math.Min(60, Math.Max(5, block._InventoryPushBackoffSeconds * 2));
                block._InventoryPushBackoffSeconds = next;
                block._NextInventoryPushAllowed = now.Add(TimeSpan.FromMilliseconds(next * 1000 + block._PushJitterMs));
                return;
            }
            // Respect the backoff window; do nothing until the next allowed time
            if (now < block._NextInventoryPushAllowed)
            {
                return;
            }
            // Basic throttle: much slower cadence to 5–10s
            var fastPush = block.State.InventoryFull || (block._TransportInventory != null && block._TransportInventory.CurrentVolume > 0);
            var minInterval = fastPush ? 5 : 10;
            if (now.Subtract(block._TryAutoPushInventoryLast).TotalSeconds <= minInterval)
            {
                return;
            }

            var welderInventory = block.Welder.GetInventory(0);
            if (welderInventory == null || welderInventory.Empty()) return;

            var lastPush = now;
            var tempInventoryItems = new List<MyInventoryItem>();
            welderInventory.GetItems(tempInventoryItems);

            var movedSomething = false;

            // Limit how many items we try to move per tick to avoid long CPU stalls
            var movedCount = 0;
            const int maxMovesPerCall = 8;
            for (var srcItemIndex = tempInventoryItems.Count - 1; srcItemIndex >= 0; srcItemIndex--)
            {
                if (movedCount >= maxMovesPerCall) { break; }
                var srcItem = tempInventoryItems[srcItemIndex];
                var type = srcItem.Type.TypeId;

                if (type == typeof(MyObjectBuilder_Ore).Name || type == typeof(MyObjectBuilder_Ingot).Name)
                {
                    if ((block.Settings.Flags & SyncBlockSettings.Settings.PushIngotOreImmediately) != 0)
                    {
                        var moved = welderInventory.PushComponents(block._PossibleSources, (IMyInventory dest, IMyInventory src, ref MyInventoryItem i) => block._Ignore4Ingot.Contains(dest), srcItemIndex, srcItem);
                        movedSomething |= moved;
                        if (moved) { block._TryAutoPushInventoryLast = lastPush; movedCount++; }
                    }
                }
                else if (type == typeof(MyObjectBuilder_Component).Name)
                {
                    if ((block.Settings.Flags & SyncBlockSettings.Settings.PushComponentImmediately) != 0)
                    {
                        var moved = welderInventory.PushComponents(block._PossibleSources, (IMyInventory dest, IMyInventory src, ref MyInventoryItem i) => block._Ignore4Components.Contains(dest), srcItemIndex, srcItem);
                        movedSomething |= moved;
                        if (moved) { block._TryAutoPushInventoryLast = lastPush; movedCount++; }
                    }
                }
                else
                {
                    if ((block.Settings.Flags & SyncBlockSettings.Settings.PushItemsImmediately) != 0)
                    {
                        var moved = welderInventory.PushComponents(block._PossibleSources, (IMyInventory dest, IMyInventory src, ref MyInventoryItem i) => block._Ignore4Items.Contains(dest), srcItemIndex, srcItem);
                        movedSomething |= moved;
                        if (moved) { block._TryAutoPushInventoryLast = lastPush; movedCount++; }
                    }
                }
            }

            tempInventoryItems.Clear();

            // Adjust backoff depending on success
            if (movedSomething)
            {
                // Reset backoff to a small interval
                block._InventoryPushBackoffSeconds = 0;
                block._NextInventoryPushAllowed = now.Add(TimeSpan.FromMilliseconds((5000) + block._PushJitterMs));
            }
            else
            {
                // Exponential backoff up to a cap to avoid hot-looping when all destinations are full
                var baseSec = fastPush ? 5 : 10;
                var next = Math.Max(baseSec, block._InventoryPushBackoffSeconds == 0 ? _Rng.Next(5, 11) : block._InventoryPushBackoffSeconds * 2);
                if (next > 60) next = 60; // cap at 60s
                block._InventoryPushBackoffSeconds = next;
                block._NextInventoryPushAllowed = now.Add(TimeSpan.FromMilliseconds(next * 1000 + block._PushJitterMs));
            }
        }

        public static void EmptyInventory(NanobotBuildAndRepairSystemBlock block)
        {
            var transportInventory = block._TransportInventory;
            var welderInventory = block.Welder.GetInventory(0);

            if (transportInventory != null && welderInventory != null)
            {
                if (!welderInventory.Empty())
                {
                    welderInventory.PushComponents(block._PossibleSources, null);
                }

                var tempInventoryItems = new List<MyInventoryItem>();
                transportInventory.GetItems(tempInventoryItems);

                for (var i = tempInventoryItems.Count - 1; i >= 0; i--)
                {
                    var item = tempInventoryItems[i];
                    if (item == null) continue;

                    var amount = item.Amount;
                    var moveableAmount = welderInventory.MaxItemsAddable(amount, item.Type);

                    if (moveableAmount > 0)
                    {
                        welderInventory.TransferItemFrom(transportInventory, i, null, true, moveableAmount, false);
                    }
                }
                tempInventoryItems.Clear();
            }
        }

        public static bool EmptyTransportInventory(NanobotBuildAndRepairSystemBlock block, bool push)
        {
            var transportInventory = block._TransportInventory;
            var welderInventory = block.Welder.GetInventory(0);

            var empty = transportInventory.Empty();
            if (!empty)
            {
                if (!block._CreativeModeActive)
                {
                    if (welderInventory != null)
                    {
                        var now = MyAPIGateway.Session.ElapsedPlayTime;
                        // Probe destinations only once here; if none free, increase backoff and skip to avoid tight loops
                        var anyDestHasFree = false;
                        lock (block._PossibleSources)
                        {
                            anyDestHasFree = DestinationCapacityCache.AnyDestinationHasFree(block._PossibleSources, now);
                        }
                        if (!anyDestHasFree)
                        {
                            var next = block._InventoryPushBackoffSeconds == 0 ? _Rng.Next(5, 11) : Math.Min(60, Math.Max(5, block._InventoryPushBackoffSeconds * 2));
                            block._InventoryPushBackoffSeconds = next;
                            block._NextInventoryPushAllowed = now.Add(TimeSpan.FromMilliseconds(next * 1000 + block._PushJitterMs));
                            block.State.InventoryFull = true;
                            return false;
                        }
                        if (push && !welderInventory.Empty())
                        {
                            // Throttle transport-side push attempts using adaptive backoff; use faster cadence when full
                            var minInterval = block.State.InventoryFull ? 5 : 10;
                            if (now >= block._NextInventoryPushAllowed &&
                                now.Subtract(block._TryPushInventoryLast).TotalSeconds > minInterval)
                            {
                                var moved = welderInventory.PushComponents(block._PossibleSources, null);
                                block._TryPushInventoryLast = now;

                                if (moved)
                                {
                                    // Reset backoff to a small interval
                                    block._InventoryPushBackoffSeconds = 0;
                                    block._NextInventoryPushAllowed = now.Add(TimeSpan.FromMilliseconds(5000 + block._PushJitterMs));
                                }
                                else
                                {
                                    // Increase backoff when nothing moved
                                    var next = Math.Max(5, block._InventoryPushBackoffSeconds == 0 ? _Rng.Next(5, 11) : block._InventoryPushBackoffSeconds * 2);
                                    if (next > 60) next = 60;
                                    block._InventoryPushBackoffSeconds = next;
                                    block._NextInventoryPushAllowed = now.Add(TimeSpan.FromMilliseconds(next * 1000 + block._PushJitterMs));
                                }
                            }
                        }

                        // If welder has no free volume, we still try a couple of transfers; otherwise return
                        var freeVolume = welderInventory.MaxVolume - welderInventory.CurrentVolume;
                        if (freeVolume <= 0)
                        {
                            block.State.InventoryFull = !empty;
                            return empty;
                        }

                        var tempInventoryItems = new List<MyInventoryItem>();
                        transportInventory.GetItems(tempInventoryItems);

                        var transferCount = 0;
                        const int maxTransfers = 8; // lower per-call transfers to reduce frame spikes
                        for (var i = tempInventoryItems.Count - 1; i >= 0; i--)
                        {
                            if (transferCount >= maxTransfers) { break; }
                            var item = tempInventoryItems[i];
                            if (item == null) continue;

                            var amount = item.Amount;
                            var moveableAmount = welderInventory.MaxItemsAddable(amount, item.Type);

                            if (moveableAmount > 0)
                            {
                                welderInventory.TransferItemFrom(transportInventory, i, null, true, moveableAmount, false);
                                // Recompute free volume; if none left, break early
                                freeVolume = welderInventory.MaxVolume - welderInventory.CurrentVolume;
                                if (freeVolume <= 0)
                                {
                                    break;
                                }
                                transferCount++;
                            }
                        }
                        tempInventoryItems.Clear();
                    }
                }
                else
                {
                    transportInventory.Clear();
                }

                empty = transportInventory.Empty();
            }

            block.State.InventoryFull = !empty;
            return empty;
        }
    }
}