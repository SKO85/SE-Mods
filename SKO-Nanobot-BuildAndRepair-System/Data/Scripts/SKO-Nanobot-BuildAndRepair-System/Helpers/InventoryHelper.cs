using Sandbox.ModAPI;
using SKONanobotBuildAndRepairSystem.Caches;
using SKONanobotBuildAndRepairSystem.Profiling;
using System;
using System.Collections.Generic;
using VRage;
using VRage.Game;
using VRage.Game.ModAPI;
using VRage.ModAPI;

namespace SKONanobotBuildAndRepairSystem.Helpers
{
    public static class InventoryHelper
    {
        // BUG-119: 60s TTL — stale entries are harmless (transfer fails later, refresh picks up new).
        private static readonly TtlCache<MyTuple<long, long>, bool> ConnectionCache = new TtlCache<MyTuple<long, long>, bool>(
            defaultTtl: TimeSpan.FromSeconds(60),
            comparer: new MyTupleComparer<long, long>(),
            concurrencyLevel: 4,
            capacity: 2048);

        // BUG-142's TransferCache (cached CanTransferItemTo wrapper) removed in
        // BUG-260610.36: it had no callers, and its key dropped the component TypeId
        // (Ore/Ingot collisions) — deleted rather than fixed so nobody adopts a
        // latently broken cache.
        // BUG-133: SourceHasComponentCache retired (PullFromSourcesOnePass made FindItem unused).

        public static bool AddIfConnectedToInventory(this IMyTerminalBlock terminalBlock, IMyShipWelder welder, List<IMyInventory> possibleSources, HashSet<IMyInventory> possibleSourcesSet)
        {
            var profilerTs = MethodProfiler.Start();
            if (terminalBlock == null || welder == null || possibleSources == null || possibleSourcesSet == null) return false;
            if (terminalBlock.EntityId == welder.EntityId) return false;

            // Only the following types for containers/inventories as valid external sources or push targets to reduce scanning of all types.
            var isCargo = terminalBlock is IMyCargoContainer;
            var isAssembler = terminalBlock is IMyAssembler;
            var isWelder = terminalBlock is IMyShipWelder;
            var isGrinder = terminalBlock is IMyShipGrinder;
            var isSorter = terminalBlock is IMyConveyorSorter;
            var isConnector = terminalBlock is IMyShipConnector;
            var isCryo = terminalBlock is IMyCryoChamber;
            var isRefinery = terminalBlock is IMyRefinery;

            // Just return false if the terminal block is none of the above types.
            if (!(isCargo || isAssembler || isWelder || isGrinder || isSorter || isConnector || isCryo || isRefinery)) return false;

            // All blocks (same-grid and cross-grid): use cached IsConnectedTo check.
            var key = new MyTuple<long, long>(terminalBlock.EntityId, welder.EntityId);
            var cachedConnected = false;

            if (ConnectionCache.TryGet(key, out cachedConnected))
            {
                if (cachedConnected)
                {
                    var maxInvCached = terminalBlock.InventoryCount;
                    for (var i = 0; i < maxInvCached; i++)
                    {
                        // BUG-119: HashSet.Add for O(1) dedup (was O(n) Contains scan).
                        var inventory = terminalBlock.GetInventory(i);
                        if (possibleSourcesSet.Add(inventory))
                        {
                            possibleSources.Add(inventory);
                        }
                    }
                }
                if (profilerTs != 0L)
                {
                    var _cachedConnected = cachedConnected;
                    MethodProfiler.StopAndLog("AddIfConnectedToInventory", profilerTs, () =>
                        string.Format("blockId={0};welderId={1};cached=true;connected={2}", terminalBlock.EntityId, welder.EntityId, _cachedConnected));
                }
                return cachedConnected;
            }

            // Cache miss: probe each inventory until one is connected. In vanilla SE all
            // inventories on a block share the conveyor endpoint, but modded blocks can
            // expose the port on a non-zero slot — checking only slot 0 would memoize a
            // false negative for the whole block.
            var welderInventory = welder.GetInventory(0);
            var maxInvCross = terminalBlock.InventoryCount;
            var isConnected = false;
            for (var i = 0; i < maxInvCross; i++)
            {
                var inventory = terminalBlock.GetInventory(i);
                if (inventory != null && inventory.IsConnectedTo(welderInventory))
                {
                    isConnected = true;
                    break;
                }
            }

            if (isConnected)
            {
                for (var i = 0; i < maxInvCross; i++)
                {
                    var inventory = terminalBlock.GetInventory(i);
                    if (possibleSourcesSet.Add(inventory))
                    {
                        possibleSources.Add(inventory);
                    }
                }
            }

            ConnectionCache.Set(key, isConnected);
            if (profilerTs != 0L)
            {
                var _isConnected = isConnected;
                MethodProfiler.StopAndLog("AddIfConnectedToInventory", profilerTs, () =>
                    string.Format("blockId={0};welderId={1};cached=false;connected={2}", terminalBlock.EntityId, welder.EntityId, _isConnected));
            }
            return isConnected;
        }

        public static void Cleanup()
        {
            ConnectionCache.CleanupExpired();
        }
    }
}