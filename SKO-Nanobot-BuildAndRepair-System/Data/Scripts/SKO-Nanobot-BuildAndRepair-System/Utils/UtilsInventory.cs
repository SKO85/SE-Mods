using Sandbox.Definitions;
using Sandbox.Game;
using Sandbox.ModAPI;
using SKONanobotBuildAndRepairSystem.Extensions;
using System;
using System.Collections.Generic;
using System.Linq;
using VRage.Game;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using MyInventoryItem = VRage.Game.ModAPI.Ingame.MyInventoryItem;
using MyItemType = VRage.Game.ModAPI.Ingame.MyItemType;

namespace SKONanobotBuildAndRepairSystem.Utils
{
    public static class UtilsInventory
    {
        public delegate bool ExcludeInventory(IMyInventory destInventory, IMyInventory srcInventory, ref MyInventoryItem srcItem);

        /// <summary>
        /// Inventory fill ratio as a value in [0,1] — the higher of volume- and mass-based ratios.
        /// </summary>
        public static float IsFilledToPercent(this IMyInventory inventory)
        {
            return Math.Max((float)inventory.CurrentVolume / (float)inventory.MaxVolume, (float)inventory.CurrentMass / (float)((MyInventory)inventory).MaxMass);
        }

        /// <summary>
        /// Check if all inventories are empty
        /// </summary>
        /// <param name="entity"></param>
        /// <returns></returns>
        public static bool InventoriesEmpty(this IMyEntity entity)
        {
            if (!entity.HasInventory)
                return true;

            for (int i = 0; i < entity.InventoryCount; ++i)
            {
                var srcInventory = entity.GetInventory(i);

                if (!srcInventory.Empty())
                    return false;
            }

            return true;
        }

        /// <summary>
        /// Push all components into destinations
        /// </summary>
        public static bool PushComponents(this IMyInventory srcInventory, List<IMyInventory> destinations, ExcludeInventory exclude)
        {
            var moved = false;
            lock (destinations)
            {
                var srcItems = new List<MyInventoryItem>();
                srcInventory.GetItems(srcItems);
                for (int srcItemIndex = srcItems.Count - 1; srcItemIndex >= 0; srcItemIndex--)
                {
                    var srcItem = srcItems[srcItemIndex];
                    moved = TryTransferItemTo(srcInventory, destinations, srcItemIndex, srcItem, true, exclude) || moved;
                }

                if (!moved)
                {
                    srcItems.Clear();
                    srcInventory.GetItems(srcItems);
                    for (int srcItemIndex = srcItems.Count - 1; srcItemIndex >= 0; srcItemIndex--)
                    {
                        var srcItem = srcItems[srcItemIndex];
                        moved = TryTransferItemTo(srcInventory, destinations, srcItemIndex, srcItem, false, exclude) || moved;
                    }
                }
            }
            return moved;
        }

        /// <summary>
        /// Push given items into destinations
        /// </summary>
        public static bool PushComponents(this IMyInventory srcInventory, List<IMyInventory> destinations, ExcludeInventory exclude, int srcItemIndex, MyInventoryItem srcItem)
        {
            var moved = false;
            lock (destinations)
            {
                moved = TryTransferItemTo(srcInventory, destinations, srcItemIndex, srcItem, true, exclude);
                if (!moved)
                {
                    moved = TryTransferItemTo(srcInventory, destinations, srcItemIndex, srcItem, false, exclude);
                }
            }
            return moved;
        }

        /// <summary>
        /// Checks if inventory is nearly full
        /// </summary>
        public static bool IsNearlyFull(this IMyInventory destInventory, float percent)
        {
            float minRemainVolume = (float)destInventory.MaxVolume * percent;
            return (float)(destInventory.MaxVolume - destInventory.CurrentVolume) <= minRemainVolume;
        }

        /// <summary>
        /// As long as ComputeAmountThatFits is not available for modding we have to try
        /// </summary>
        public static VRage.MyFixedPoint MaxItemsAddable(this IMyInventory destInventory, VRage.MyFixedPoint maxNeeded, MyItemType itemType)
        {
            if (destInventory.CanItemsBeAdded(maxNeeded, itemType))
            {
                return maxNeeded;
            }

            int maxPossible = 0;
            int currentStep = Math.Max((int)maxNeeded / 2, 1);
            while (currentStep > 0)
            {
                int currentTry = maxPossible + currentStep;
                if (destInventory.CanItemsBeAdded(currentTry, itemType))
                {
                    maxPossible = currentTry;
                }
                currentStep = currentStep / 2;
            }
            return maxPossible;
        }

        /// <summary>
        /// As long as ComputeAmountThatFits is not available for modding we have to try
        /// </summary>
        public static VRage.MyFixedPoint MaxFractionItemsAddable(this IMyInventory destInventory, VRage.MyFixedPoint maxNeeded, MyItemType itemType)
        {
            if (destInventory.CanItemsBeAdded(maxNeeded, itemType))
            {
                return maxNeeded;
            }

            VRage.MyFixedPoint maxPossible = 0;
            VRage.MyFixedPoint currentStep = (VRage.MyFixedPoint)((float)maxNeeded / 2);
            VRage.MyFixedPoint currentTry = 0;
            while (currentStep > VRage.MyFixedPoint.SmallestPossibleValue)
            {
                currentTry = maxPossible + currentStep;
                if (destInventory.CanItemsBeAdded(currentTry, itemType))
                {
                    maxPossible = currentTry;
                }
                currentStep = (VRage.MyFixedPoint)((float)currentStep / 2);
            }
            return maxPossible;
        }

        /// <summary>
        /// Moves as many as possible from srcInventory to destinations
        /// </summary>
        private static bool TryTransferItemTo(IMyInventory srcInventory, List<IMyInventory> destinations, int srcItemIndex, MyInventoryItem srcItem, bool all, ExcludeInventory exclude)
        {
            var moved = false;
            if (all)
            {
                foreach (var destInventory in destinations)
                {
                    var destOwner = destInventory.Owner as IMyEntity;
                    if (destOwner == null || destOwner.MarkedForClose) continue;
                    if (exclude != null && exclude(destInventory, srcInventory, ref srcItem)) continue;
                    if (destInventory.CanItemsBeAdded(srcItem.Amount, srcItem.Type) && srcInventory.CanTransferItemTo(destInventory, srcItem.Type))
                    {
                        moved = srcInventory.TransferItemTo(destInventory, srcItemIndex, null, true, srcItem.Amount, false);
                        if (moved) break;
                    }
                }
                return moved;
            }

            foreach (var destInventory in destinations)
            {
                var destOwner = destInventory.Owner as IMyEntity;
                if (destOwner == null || destOwner.MarkedForClose) continue;
                if (exclude != null && exclude(destInventory, srcInventory, ref srcItem)) continue;
                if (srcInventory.CanTransferItemTo(destInventory, srcItem.Type))
                {
                    var amount = destInventory.MaxItemsAddable(srcItem.Amount, srcItem.Type);
                    if (amount > 0)
                    {
                        moved = srcInventory.TransferItemTo(destInventory, srcItemIndex, null, true, amount, true) || moved;
                    }
                }
            }
            return moved;
        }

        /// <summary>
        /// Add maxNeeded amount of items into inventory.
        /// -If maxNeeded cannot be added, as many as possible are added and the added amount is returned
        /// -If maxNeeded is less than MyFixedPoint can handle 0 is returned
        /// </summary>
        public static float AddMaxItems(this IMyInventory destInventory, float maxNeeded, MyObjectBuilder_PhysicalObject objectBuilder)
        {
            var maxNeededFP = (VRage.MyFixedPoint)maxNeeded;
            return (float)destInventory.AddMaxItems(maxNeededFP, objectBuilder);
        }

        public static VRage.MyFixedPoint AddMaxItems(this IMyInventory destInventory, VRage.MyFixedPoint maxNeededFP, MyObjectBuilder_PhysicalObject objectBuilder)
        {
            var contentId = objectBuilder.GetObjectId();
            if (maxNeededFP <= 0)
            {
                return 0; //Amount too small
            }

            var maxPossible = destInventory.MaxFractionItemsAddable(maxNeededFP, contentId);
            if (maxPossible > 0)
            {
                destInventory.AddItems(maxPossible, objectBuilder);
                return maxPossible;
            }
            else
            {
                return 0;
            }
        }

        public static VRage.MyFixedPoint AddMaxItemsWithCheck(this IMyInventory destInventory, VRage.MyFixedPoint maxNeededFP, MyObjectBuilder_PhysicalObject objectBuilder)
        {
            var contentId = objectBuilder.GetObjectId();
            if (maxNeededFP <= 0)
            {
                return 0; //Amount too small
            }

            var maxPossible = destInventory.MaxFractionItemsAddable(maxNeededFP, contentId);
            if (maxPossible > 0)
            {
                var amountBefore = destInventory.GetItemAmount(objectBuilder);
                destInventory.AddItems(maxPossible, objectBuilder);
                var amountAfter = destInventory.GetItemAmount(objectBuilder);
                return amountAfter - amountBefore;
            }
            else
            {
                return 0;
            }
        }

        public static VRage.MyFixedPoint RemoveMaxItems(this IMyInventory srcInventory, VRage.MyFixedPoint maxRemoveFP, MyObjectBuilder_PhysicalObject objectBuilder)
        {
            if (maxRemoveFP <= 0) return 0;
            var contentId = objectBuilder.GetObjectId();
            VRage.MyFixedPoint removedAmount = 0;
            if (!srcInventory.ContainItems(maxRemoveFP, objectBuilder))
            {
                maxRemoveFP = srcInventory.GetItemAmount(contentId);
            }
            if (maxRemoveFP > 0)
            {
                srcInventory.RemoveItemsOfType(maxRemoveFP, contentId, MyItemFlags.None, false);
                removedAmount = maxRemoveFP;
            }
            return removedAmount;
        }

    }
}