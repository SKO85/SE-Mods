using Sandbox.ModAPI;
using SKONanobotBuildAndRepairSystem.Caches;
using SKONanobotBuildAndRepairSystem.Profiling;
using System;
using System.Collections.Generic;
using VRage;
using VRage.Game.ModAPI;

namespace SKONanobotBuildAndRepairSystem.Helpers
{
    public static class InventoryHelper
    {
        // BUG-119: TTL extended from 15s to 60s. Cluster/source scans fire every ~6s; with 15s
        // TTL most entries expired mid-session and the cache miss path runs IsConnectedTo
        // (engine conveyor walk, expensive). Stale entries are harmless — a disconnected
        // inventory just fails its actual transfer later, and a newly connected one is picked
        // up on the next refresh (welds run for minutes anyway).
        private static readonly TtlCache<MyTuple<long, long>, bool> ConnectionCache = new TtlCache<MyTuple<long, long>, bool>(
            defaultTtl: TimeSpan.FromSeconds(60),
            comparer: new MyTupleComparer<long, long>(),
            concurrencyLevel: 4,
            capacity: 2048);

        // BUG-129: source-has-component cache. Skips sources known not to contain a given
        // component during PullComponents' source walk. Profile session 20260429214111 showed
        // pullPickMs=6.5 ms on LargeRefinery projected — dominated by 94 sources × FindItem
        // engine calls per missing component. Cache key: (sourceOwnerEntityId, componentSubtypeName).
        // Stale "true" entries → existing FindItem still runs, wasted cycle but correct outcome.
        // Stale "false" entries → BaR may miss a freshly-stocked source for up to 30 s; benign,
        // self-corrects on TTL expiry. Mirrors BUG-119 ConnectionCache pattern.
        public static readonly TtlCache<MyTuple<long, string>, bool> SourceHasComponentCache = new TtlCache<MyTuple<long, string>, bool>(
            defaultTtl: TimeSpan.FromSeconds(30),
            comparer: new MyTupleComparer<long, string>(),
            concurrencyLevel: 4,
            capacity: 4096);

        public static bool AddIfConnectedToInventory(this IMyTerminalBlock terminalBlock, IMyShipWelder welder, List<IMyInventory> possibleSources, HashSet<IMyInventory> possibleSourcesSet)
        {
            var profilerTs = MethodProfiler.Start();
            if (terminalBlock == null || welder == null || possibleSources == null) return false;
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
                        var inventory = terminalBlock.GetInventory(i);
                        // BUG-119: HashSet.Add returns false if already present (O(1)); replaces
                        // the prior O(n) possibleSources.Contains scan that grew with source count.
                        if (possibleSourcesSet == null)
                        {
                            if (!possibleSources.Contains(inventory)) possibleSources.Add(inventory);
                        }
                        else if (possibleSourcesSet.Add(inventory))
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

            var welderInventory = welder.GetInventory(0);
            var maxInvCross = terminalBlock.InventoryCount;
            var isConnected = false;

            for (var i = 0; i < maxInvCross; i++)
            {
                var inventory = terminalBlock.GetInventory(i);

                // BUG-119: dedup via HashSet first to avoid the O(n) Contains scan, then
                // pay the engine IsConnectedTo cost only for inventories we'd actually keep.
                bool alreadyPresent;
                if (possibleSourcesSet == null)
                {
                    alreadyPresent = possibleSources.Contains(inventory);
                }
                else
                {
                    alreadyPresent = !possibleSourcesSet.Add(inventory);
                }

                if (!alreadyPresent && inventory.IsConnectedTo(welderInventory))
                {
                    isConnected = true;
                    possibleSources.Add(inventory);
                }
                else if (!alreadyPresent && possibleSourcesSet != null)
                {
                    // Speculatively added to the set above; roll back since IsConnectedTo failed.
                    possibleSourcesSet.Remove(inventory);
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
            // BUG-129: sweep stale source-has-component entries.
            SourceHasComponentCache.CleanupExpired();
        }
    }
}