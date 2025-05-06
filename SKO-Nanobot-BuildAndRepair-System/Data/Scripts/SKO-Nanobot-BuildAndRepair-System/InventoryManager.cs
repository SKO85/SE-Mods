using Sandbox.ModAPI;
using System.Collections.Generic;
using VRage.Game;
using VRage.Game.ModAPI;
using MyInventoryItem = VRage.Game.ModAPI.Ingame.MyInventoryItem;

namespace SKONanobotBuildAndRepairSystem
{
    public static class InventoryManager
    {
        public static void TryPushInventory(NanobotBuildAndRepairSystemBlock block)
        {
            if ((block.Settings.Flags & (SyncBlockSettings.Settings.PushIngotOreImmediately | SyncBlockSettings.Settings.PushComponentImmediately | SyncBlockSettings.Settings.PushItemsImmediately)) == 0)
                return;

            if (MyAPIGateway.Session.ElapsedPlayTime.Subtract(block._TryAutoPushInventoryLast).TotalSeconds <= 5)
                return;

            var welderInventory = block.Welder.GetInventory(0);
            if (welderInventory == null || welderInventory.Empty()) return;

            var lastPush = MyAPIGateway.Session.ElapsedPlayTime;
            var tempInventoryItems = new List<MyInventoryItem>();
            welderInventory.GetItems(tempInventoryItems);

            for (var srcItemIndex = tempInventoryItems.Count - 1; srcItemIndex >= 0; srcItemIndex--)
            {
                var srcItem = tempInventoryItems[srcItemIndex];
                var type = srcItem.Type.TypeId;

                if (type == typeof(MyObjectBuilder_Ore).Name || type == typeof(MyObjectBuilder_Ingot).Name)
                {
                    if ((block.Settings.Flags & SyncBlockSettings.Settings.PushIngotOreImmediately) != 0)
                    {
                        welderInventory.PushComponents(block._PossibleSources, (IMyInventory dest, IMyInventory src, ref MyInventoryItem i) => block._Ignore4Ingot.Contains(dest), srcItemIndex, srcItem);
                        block._TryAutoPushInventoryLast = lastPush;
                    }
                }
                else if (type == typeof(MyObjectBuilder_Component).Name)
                {
                    if ((block.Settings.Flags & SyncBlockSettings.Settings.PushComponentImmediately) != 0)
                    {
                        welderInventory.PushComponents(block._PossibleSources, (IMyInventory dest, IMyInventory src, ref MyInventoryItem i) => block._Ignore4Components.Contains(dest), srcItemIndex, srcItem);
                        block._TryAutoPushInventoryLast = lastPush;
                    }
                }
                else
                {
                    if ((block.Settings.Flags & SyncBlockSettings.Settings.PushItemsImmediately) != 0)
                    {
                        welderInventory.PushComponents(block._PossibleSources, (IMyInventory dest, IMyInventory src, ref MyInventoryItem i) => block._Ignore4Items.Contains(dest), srcItemIndex, srcItem);
                        block._TryAutoPushInventoryLast = lastPush;
                    }
                }
            }

            tempInventoryItems.Clear();
        }

        public static void EmptyInventory(NanobotBuildAndRepairSystemBlock block)
        {
            var transportInventory = block._TransportInventory;
            var welderInventory = block.Welder.GetInventory(0);

            if (welderInventory != null)
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
                        if (push && !welderInventory.Empty())
                        {
                            if (MyAPIGateway.Session.ElapsedPlayTime.Subtract(block._TryPushInventoryLast).TotalSeconds > 3 &&
                                welderInventory.MaxVolume - welderInventory.CurrentVolume < transportInventory.CurrentVolume * 1.5f)
                            {
                                if (!welderInventory.PushComponents(block._PossibleSources, null))
                                {
                                    block._TryPushInventoryLast = MyAPIGateway.Session.ElapsedPlayTime;
                                }
                            }
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