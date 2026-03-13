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

        public static bool AddIfConnectedToInventory(this IMyTerminalBlock terminalBlock, IMyShipWelder welder, List<IMyInventory> possibleSources, bool isSameGrid)
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

            // Same-grid blocks: skip IsConnectedTo which is unreliable from background threads.
            // CubeGrid.EntityId is also unreliable from background threads, so the caller passes
            // this flag based on which grid is being scanned (known from the call context).
            // The actual TransferItemFrom call handles real connectivity failures gracefully.
            if (isSameGrid)
            {
                var maxInv = terminalBlock.InventoryCount;
                for (var i = 0; i < maxInv; i++)
                {
                    var inventory = terminalBlock.GetInventory(i);
                    if (!possibleSources.Contains(inventory))
                    {
                        possibleSources.Add(inventory);
                    }
                }
                return maxInv > 0;
            }

            // Cross-grid blocks (via connectors/mechanical connections): use cached IsConnectedTo check.
            var key = new MyTuple<long, long>(terminalBlock.EntityId, welder.EntityId);
            var cachedConnected = false;

            if (ConnectionCache.TryGet(key, out cachedConnected))
            {
                if (cachedConnected)
                {
                    var maxInvCached = terminalBlock.InventoryCount;
                    for (var i = 0; i < maxInvCached; i++)
                    {
                        var inventory = terminalBlock.GetInventory(i);
                        if (!possibleSources.Contains(inventory))
                        {
                            possibleSources.Add(inventory);
                        }
                    }
                }
                return cachedConnected;
            }

            var welderInventory = welder.GetInventory(0);
            var maxInvCross = terminalBlock.InventoryCount;
            var isConnected = false;

            for (var i = 0; i < maxInvCross; i++)
            {
                var inventory = terminalBlock.GetInventory(i);

                if (!possibleSources.Contains(inventory) && inventory.IsConnectedTo(welderInventory))
                {
                    isConnected = true;
                    possibleSources.Add(inventory);
                }
            }

            ConnectionCache.Set(key, isConnected);
            return isConnected;
        }

        public static void Cleanup()
        {
            ConnectionCache.CleanupExpired();
        }
    }
}