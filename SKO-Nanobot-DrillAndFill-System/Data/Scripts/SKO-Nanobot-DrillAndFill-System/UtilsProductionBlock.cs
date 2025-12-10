using Sandbox.Definitions;
using Sandbox.ModAPI;
using System;
using System.Collections.Generic;
using System.Linq;
using VRage.ModAPI;

namespace SpaceEquipmentLtd.Utils
{
   public static class UtilsProductionBlock
   {
      /// <summary>
      /// Ensure that the requested amout of material is available inside production block.
      /// Available means already in inventory or in production queue.
      /// </summary>
      /// <param name="entityId"></param>
      /// <param name="materialId"></param>
      /// <param name="amount"></param>
      /// <returns></returns>
      public static int EnsureQueued(IEnumerable<long> entityIds, VRage.Game.MyDefinitionId materialId, int amount)
      {
         if (amount <= 0) return 0;
         if (!entityIds.Any()) return -1;

         var blueprintDefinition = MyDefinitionManager.Static.TryGetBlueprintDefinitionByResultId(materialId);
         if (blueprintDefinition == null) return 0;

         var queueSizes = new List<KeyValuePair<IMyAssembler, int>>();
         var queueSizeAvg = 0;
         foreach (var entityId in entityIds)
         {
            IMyEntity entity;
            if (!MyAPIGateway.Entities.TryGetEntityById(entityId, out entity)) continue;
            var productionBlock = entity as IMyAssembler;
            if (productionBlock == null || productionBlock.Mode != Sandbox.ModAPI.Ingame.MyAssemblerMode.Assembly ) continue;

            int queueSize;
            var amountAvail = AvailableAmount(productionBlock, materialId, blueprintDefinition, out queueSize);

            if (productionBlock.CanUseBlueprint(blueprintDefinition))
            {
               queueSizes.Add(new KeyValuePair<IMyAssembler, int>(productionBlock, queueSize));
               queueSizeAvg += queueSize;
            }

            amount -= amountAvail;
            if (amount <= 0) return 0; //Allready enought available
         }

         var cnt = queueSizes.Count;
         if (cnt <= 0) return -1; //No production blocks or none could handle this material

         queueSizeAvg = queueSizeAvg / cnt;
         queueSizes.Sort((a, b) => a.Value - b.Value);

         var queued = amount;

         //First run fill to avg
         if (queueSizeAvg > 0)
         {
            foreach (var entry in queueSizes)
            {
               var space = queueSizeAvg - entry.Value;
               if (space > 0) {
                  space = Math.Min(space, amount);
                  entry.Key.AddQueueItem(blueprintDefinition, space);
                  amount -= space;
                  if (amount <= 0) return queued;
               }
            }
         }

         //Second run spread the rest
         var amountPerBlock = (int)Math.Ceiling((decimal)amount / cnt);
         foreach (var entry in queueSizes)
         {
            var space = Math.Min(amountPerBlock, amount);
            entry.Key.AddQueueItem(blueprintDefinition, space);
            amount -= space;
            if (amount <= 0) return queued;
         }

         return 0;
      }

      /// <summary>
      /// Gives the 'available' amount inventory + queued
      /// queueSize total amount of queued items indicator for workload 
      /// </summary>
      private static int AvailableAmount(IMyProductionBlock productionBlock, VRage.Game.MyDefinitionId materialId, VRage.Game.MyDefinitionBase blueprintDefinition, out int queueSize) {
         var queue = productionBlock.GetQueue();
         var inventory = productionBlock.OutputInventory;
         var tempInventoryItems = new List<VRage.Game.ModAPI.Ingame.MyInventoryItem>();
         if (inventory != null) inventory.GetItems(tempInventoryItems);
         var amount = 0;
         queueSize = 0;
         foreach (var item in tempInventoryItems)
         {
            if ((VRage.Game.MyDefinitionId)item.Type == materialId)
            {
               amount += (int)item.Amount;
            }
         }

         if (queue != null)
         {
            foreach (var item in queue)
            {
               queueSize += (int)item.Amount;
               if (item.Blueprint.Id.Equals(blueprintDefinition.Id))
               {
                  amount += (int)item.Amount;
               }
            }
         }

         return amount;
      }
   }
}
