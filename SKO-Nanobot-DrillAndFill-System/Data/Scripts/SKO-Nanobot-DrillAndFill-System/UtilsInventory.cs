using Sandbox.Definitions;
using Sandbox.ModAPI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VRage.Game;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using MyInventoryItem = VRage.Game.ModAPI.Ingame.MyInventoryItem;
using MyItemType = VRage.Game.ModAPI.Ingame.MyItemType;

namespace SpaceEquipmentLtd.Utils
{
   public static class UtilsInventory
   {
      public delegate bool ExcludeInventory(IMyInventory destInventory, IMyInventory srcInventory, ref MyInventoryItem srcItem);

      /// <summary>
      /// Check if all inventories are empty
      /// </summary>
      /// <param name="entity"></param>
      /// <returns></returns>
      public static bool InventoriesEmpty(this IMyEntity entity)
      {
         if (!entity.HasInventory) return true;
         for (int i1 = 0; i1 < entity.InventoryCount; ++i1)
         {
            var srcInventory = entity.GetInventory(i1);
            if (!srcInventory.Empty()) return false;
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
            for (int srcItemIndex = srcItems.Count-1; srcItemIndex >= 0; srcItemIndex--)
            {
               var srcItem = srcItems[srcItemIndex];
               moved = TryTransferItemTo(srcInventory, destinations, srcItemIndex, srcItem, true, exclude) || moved;
            }

            if (!moved) {
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
         int currentTry = 0;
         while (currentStep > 0)
         {
            currentTry = maxPossible + currentStep;
            if (destInventory.CanItemsBeAdded(currentTry, itemType))
            {
               maxPossible = currentTry;
            }
            else
            {
               if (currentStep <= 1) break;
            }
            if (currentStep > 1) currentStep = currentStep / 2;
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
         VRage.MyFixedPoint currentStep = (VRage.MyFixedPoint) ((float)maxNeeded / 2);
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
            if (exclude != null && exclude(destInventory, srcInventory, ref srcItem)) continue;
            if (srcInventory.CanTransferItemTo(destInventory, srcItem.Type)) 
            {
               var amount = destInventory.MaxItemsAddable(srcItem.Amount, srcItem.Type);
               if (amount > 0) {
                  moved = srcInventory.TransferItemTo(destInventory, srcItemIndex, null, true, amount, true) || moved;
                  if (srcItem.Amount <= 0) break;
               }
            }
         }
         return moved;
      }

      /// <summary>
      /// Add maxNeeded amount of items into inventory.
      /// -If not maxNeeded could be added as amany as possible is added and the added amout is returned
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
            return 0; //Amount to small
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
            return 0; //Amount to small
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

      /// <summary>
      /// Retrieve the total amount of componets to build a blueprint
      /// (blueprint loaded inside projector)
      /// </summary>
      /// <param name="projector"></param>
      /// <param name="componentList"></param>
      public static int NeededComponents4Blueprint(Sandbox.ModAPI.Ingame.IMyProjector srcProjector, Dictionary<MyDefinitionId, VRage.MyFixedPoint> componentList)
      {
         var projector = srcProjector as IMyProjector;
         if (componentList == null || projector == null || !projector.IsProjecting) return -1;

         //Add buildable blocks
         var projectedCubeGrid = projector.ProjectedGrid;
         if (projectedCubeGrid != null)
         {
            var projectedBlocks = new List<IMySlimBlock>();
            projectedCubeGrid.GetBlocks(projectedBlocks);
            foreach (IMySlimBlock block in projectedBlocks)
            {
               var blockDefinition = block.BlockDefinition as MyCubeBlockDefinition;
               foreach(var component in blockDefinition.Components)
               {
                  if (componentList.ContainsKey(component.Definition.Id)) componentList[component.Definition.Id] += component.Count;
                  else componentList[component.Definition.Id] = component.Count;
               }
            }
         }
         return componentList.Count();
      }

      public enum IntegrityLevel
      {
         Create,
         Functional,
         Complete
      }

      /// <summary>
      /// Retrieve the amount of components to build the block to the given index
      /// </summary>
      /// <param name="block"></param>
      /// <param name="componentList"></param>
      /// <param name="level">integrity level </param>
      public static void GetMissingComponents(this IMySlimBlock block, Dictionary<string, int> componentList, IntegrityLevel level)
      {
         var blockDefinition = block.BlockDefinition as MyCubeBlockDefinition;
         if (blockDefinition.Components == null || blockDefinition.Components.Length == 0) return;

         if (level == IntegrityLevel.Create) 
         {
            var component = blockDefinition.Components[0];
            componentList.Add(component.Definition.Id.SubtypeName, 1);
         } else
         {
            if (block.IsProjected())
            {
               int maxIdx = level == IntegrityLevel.Functional ? blockDefinition.CriticalGroup + 1 : blockDefinition.Components.Length;
               for (var idx = 0; idx < maxIdx; idx++)
               {
                  var component = blockDefinition.Components[idx];
                  if (componentList.ContainsKey(component.Definition.Id.SubtypeName)) componentList[component.Definition.Id.SubtypeName] += component.Count;
                  else componentList.Add(component.Definition.Id.SubtypeName, component.Count);
               }
            } else {
               block.GetMissingComponents(componentList);
               if (level == IntegrityLevel.Functional)
               {
                  for (var idx = blockDefinition.CriticalGroup + 1; idx < blockDefinition.Components.Length; idx++)
                  {
                     var component = blockDefinition.Components[idx];
                     if (componentList.ContainsKey(component.Definition.Id.SubtypeName))
                     {
                        var amount = componentList[component.Definition.Id.SubtypeName];
                        if (amount <= component.Count) componentList.Remove(component.Definition.Id.SubtypeName);
                        else componentList[component.Definition.Id.SubtypeName] -= component.Count;
                     }
                  }
               }
            }
         }
      }
   }
}
