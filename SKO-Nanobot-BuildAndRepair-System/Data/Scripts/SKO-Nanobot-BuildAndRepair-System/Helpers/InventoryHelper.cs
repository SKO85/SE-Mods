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

        public static bool AddIfConnectedToInventory(this IMyTerminalBlock terminalBlock, IMyShipWelder welder, List<IMyInventory> possibleSources, HashSet<IMyInventory> seenInventories = null)
        {
            if (terminalBlock == null || welder == null || possibleSources == null) return false;
            if (terminalBlock.EntityId == welder.EntityId) return false;

            // Only the following types for containers/inventories as valid external sources to reduce scanning of all types.
            var isCargo = terminalBlock is IMyCargoContainer;
            var isAssembler = terminalBlock is IMyAssembler;
            var isWelder = terminalBlock is IMyShipWelder;
            var isGrinder = terminalBlock is IMyShipGrinder;
            var isSorter = terminalBlock is IMyConveyorSorter;
            var isConnector = terminalBlock is IMyShipConnector;

            // Just return false if the terminal block is none of the above types.
            if (!(isCargo || isAssembler || isWelder || isGrinder || isSorter || isConnector)) return false;

            var key = new MyTuple<long, long>(terminalBlock.EntityId, welder.EntityId);
            var isConnected = false;

            if (ConnectionCache.TryGet(key, out isConnected))
            {
                return isConnected;
            }

            var welderInventory = welder.GetInventory(0);
            var maxInv = terminalBlock.InventoryCount;

            for (var i = 0; i < maxInv; i++)
            {
                var inventory = terminalBlock.GetInventory(i);

                // Use the HashSet companion for O(1) duplicate check when available;
                // fall back to the list's O(N) Contains when called without a seenInventories set.
                var alreadySeen = seenInventories != null
                    ? seenInventories.Contains(inventory)
                    : possibleSources.Contains(inventory);

                if (!alreadySeen && inventory.IsConnectedTo(welderInventory))
                {
                    isConnected = true;
                    possibleSources.Add(inventory);
                    if (seenInventories != null) seenInventories.Add(inventory);
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