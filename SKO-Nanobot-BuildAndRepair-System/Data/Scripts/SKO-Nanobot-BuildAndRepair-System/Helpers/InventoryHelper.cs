using Sandbox.ModAPI;
using SKONanobotBuildAndRepairSystem.Models;
using System;
using System.Collections.Generic;
using VRage;
using VRage.Game.ModAPI;

namespace SKONanobotBuildAndRepairSystem.Helpers
{
    public static class InventoryHelper
    {
        private static readonly TtlCache<MyTuple<long, long>, bool> ConnectionCache = new TtlCache<MyTuple<long, long>, bool>(
            defaultTtl: TimeSpan.FromSeconds(15),
            comparer: new MyTupleComparer<long, long>(),
            concurrencyLevel: 4,
            capacity: 1024);

        public static bool AddIfConnectedToInventory(this IMyTerminalBlock terminalBlock, IMyShipWelder welder, List<IMyInventory> possibleSources)
        {
            if (terminalBlock == null || welder == null || possibleSources == null) return false;
            if (terminalBlock.EntityId == welder.EntityId) return false;

            // Only consider cargo containers, assemblers and welders as valid external sources to reduce scanning cost
            var isCargo = terminalBlock is IMyCargoContainer;
            var isAssembler = terminalBlock is IMyAssembler;
            var isWelder = terminalBlock is IMyShipWelder;

            if (!(isCargo || isAssembler || isWelder)) return false;

            var key = new MyTuple<long, long>(terminalBlock.EntityId, welder.EntityId);
            var isConnected = false;

            if (ConnectionCache.TryGet(key, out isConnected))
            {
                return isConnected;
            }
            ;

            var welderInventory = welder.GetInventory(0);
            var maxInv = terminalBlock.InventoryCount;

            for (var i = 0; i < maxInv; i++)
            {
                var inventory = terminalBlock.GetInventory(i);

                if (!possibleSources.Contains(inventory) && inventory.IsConnectedTo(welderInventory))
                {
                    isConnected = true;
                    possibleSources.Add(inventory);
                    ConnectionCache.Set(key, isConnected);
                }
            }

            return isConnected;
        }

        public static void Cleanup()
        {
            ConnectionCache.CleanupExpired();
        }
    }
}