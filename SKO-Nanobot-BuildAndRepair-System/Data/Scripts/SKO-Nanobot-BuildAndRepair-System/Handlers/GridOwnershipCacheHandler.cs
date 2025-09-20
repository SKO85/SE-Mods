using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using SKONanobotBuildAndRepairSystem.Models;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using VRage;
using VRage.Game;
using VRage.Game.ModAPI;
using VRage.ModAPI;

namespace SKONanobotBuildAndRepairSystem.Handlers
{
    public static class GridOwnershipCacheHandler
    {
        private static readonly TtlCache<MyTuple<long, long>, MyRelationsBetweenPlayerAndBlock?> Cache = new TtlCache<MyTuple<long, long>, MyRelationsBetweenPlayerAndBlock?>(
           defaultTtl: TimeSpan.FromSeconds(30),
           comparer: new MyTupleComparer<long, long>(),
           concurrencyLevel: 4,
           capacity: 1024);

        private static readonly ConcurrentDictionary<long, long> GridLastAccess = new ConcurrentDictionary<long, long>();

        private const int MaxRefreshItems = 100;
        private static TimeSpan LastCheckTime;
        private static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(5); // how often we check for expired entries
        private static readonly TimeSpan AllowUnusedGridsForInterval = TimeSpan.FromMinutes(5);

        private static void SetCache(IMyCubeGrid grid, long playerId, MyRelationsBetweenPlayerAndBlock relation)
        {
            var nowTicks = MyAPIGateway.Session != null ? MyAPIGateway.Session.ElapsedPlayTime.Ticks : DateTime.UtcNow.Ticks;
            var key = new MyTuple<long, long>(grid.EntityId, playerId);
            Cache.Set(key, relation);

            // Record access for this grid to avoid unnecessary refreshes when idle
            try { GridLastAccess[grid.EntityId] = nowTicks; } catch { }
        }

        private static MyRelationsBetweenPlayerAndBlock? GetFromCache(long cubeGridEntityId, long playerid)
        {
            try
            {
                MyRelationsBetweenPlayerAndBlock? entry;
                if (!Cache.TryGet(new MyTuple<long, long>(cubeGridEntityId, playerid), out entry))
                {
                    return null;
                }
                return entry;
            }
            catch { }
            return null;
        }

        public static MyRelationsBetweenPlayerAndBlock GetRelationBetweenGridAndPlayer(this IMyCubeGrid cubeGrid, long playerId)
        {
            try
            {
                if (cubeGrid == null)
                {
                    return MyRelationsBetweenPlayerAndBlock.NoOwnership;
                }

                // Touch grid on every query to mark it as active
                try
                {
                    var nowTicksTouch = MyAPIGateway.Session.ElapsedPlayTime.Ticks;
                    GridLastAccess[cubeGrid.EntityId] = nowTicksTouch;
                }
                catch { }

                // Try get the relation from our cache first.
                var entry = GetFromCache(cubeGrid.EntityId, playerId);
                if (entry != null)
                {
                    return entry.Value;
                }

                // If no entry or expired, recompute and store with new TTL
                var relation = GetRelationBetweenGridAndPlayerInternal(cubeGrid, playerId);
                SetCache(cubeGrid, playerId, relation);
                return relation;
            }
            catch
            {
            }

            return MyRelationsBetweenPlayerAndBlock.NoOwnership;
        }

        private static MyRelationsBetweenPlayerAndBlock GetRelationBetweenGridAndPlayerInternal(IMyCubeGrid cubeGrid, long playerId)
        {
            try
            {
                // 1) Check owners for the given grid
                var rel = CheckOwners(cubeGrid, playerId);
                if (rel.HasValue) return rel.Value;

                // 2) Check the entire mechanical group iteratively (no recursion) to avoid cycles
                var groups = new List<IMyCubeGrid>();
                MyAPIGateway.GridGroups.GetGroup(cubeGrid, GridLinkTypeEnum.Mechanical, groups);
                if (groups != null)
                {
                    for (int i = 0; i < groups.Count; i++)
                    {
                        var subGrid = groups[i];
                        if (subGrid == null || subGrid.EntityId == cubeGrid.EntityId) continue;

                        rel = CheckOwners(subGrid, playerId);
                        if (rel.HasValue) return rel.Value;
                    }
                }
            }
            catch
            {
            }

            // Everything else:
            return MyRelationsBetweenPlayerAndBlock.NoOwnership;
        }

        private static MyRelationsBetweenPlayerAndBlock? CheckOwners(IMyCubeGrid grid, long playerId)
        {
            try
            {
                var owners = grid.BigOwners;
                if (owners == null || owners.Count == 0) return null;

                // Exact owner
                for (int i = 0; i < owners.Count; i++)
                {
                    var ownerId = owners[i];
                    if (ownerId == playerId) return MyRelationsBetweenPlayerAndBlock.Owner;
                }

                // Other relations; skip invalid ownerId=0
                for (int i = 0; i < owners.Count; i++)
                {
                    var ownerId = owners[i];
                    if (ownerId <= 0) continue;
                    var relation = MyIDModule.GetRelationPlayerBlock(ownerId, playerId, MyOwnershipShareModeEnum.Faction);
                    if (relation == MyRelationsBetweenPlayerAndBlock.Owner ||
                        relation == MyRelationsBetweenPlayerAndBlock.FactionShare ||
                        relation == MyRelationsBetweenPlayerAndBlock.Enemies ||
                        relation == MyRelationsBetweenPlayerAndBlock.Neutral)
                    {
                        return relation;
                    }
                }
            }
            catch { }
            return null;
        }

        public static void Update()
        {
            if (MyAPIGateway.Session == null) return;
            var now = MyAPIGateway.Session.ElapsedPlayTime;

            if (now - LastCheckTime < CheckInterval) return;

            LastCheckTime = now;

            RefreshExpiredEntries();
            Cache.CleanupExpired();
        }

        private static void RefreshExpiredEntries()
        {
            try
            {
                var session = MyAPIGateway.Session;
                var nowTicks = session != null ? session.ElapsedPlayTime.Ticks : DateTime.UtcNow.Ticks;

                var keys = Cache.Entries.Keys.ToArray();
                int refreshed = 0;
                var invalidatedGrids = new HashSet<long>();

                for (int i = 0; i < keys.Length && refreshed < MaxRefreshItems; i++)
                {
                    var key = keys[i];
                    MyRelationsBetweenPlayerAndBlock? entry;
                    if (!Cache.TryGet(key, out entry))
                        continue;

                    // If this grid has been idle for too long, remove all its cache entries
                    long lastAccess;
                    if (!GridLastAccess.TryGetValue(key.Item1, out lastAccess) || (nowTicks - lastAccess) > AllowUnusedGridsForInterval.Ticks)
                    {
                        if (invalidatedGrids.Add(key.Item1))
                        {
                            InvalidateGrid(key.Item1);
                        }
                        continue;
                    }

                    if (entry != null)
                        continue;

                    IMyEntity ent;
                    if (!MyAPIGateway.Entities.TryGetEntityById(key.Item1, out ent) || ent == null)
                    {
                        // Grid gone: remove all cached pairs for this grid
                        InvalidateGrid(key.Item1);
                        continue;
                    }

                    var grid = ent as IMyCubeGrid;
                    if (grid == null || grid.Closed)
                    {
                        InvalidateGrid(key.Item1);
                        continue;
                    }

                    var relation = GetRelationBetweenGridAndPlayerInternal(grid, key.Item2);
                    Cache.Set(key, relation);
                    refreshed++;
                }
            }
            catch
            {
                // swallow errors in background refresh
            }
        }

        public static void InvalidateGrid(long gridId)
        {
            try
            {
                var toRemove = new List<MyTuple<long, long>>();
                var keys = Cache.Entries.Keys.ToArray();
                for (int i = 0; i < keys.Length; i++)
                {
                    if (keys[i].Item1 == gridId) toRemove.Add(keys[i]);
                }

                for (int i = 0; i < toRemove.Count; i++)
                {
                    Cache.Remove(toRemove[i]);
                }

                long last;
                GridLastAccess.TryRemove(gridId, out last);
            }
            catch { }
        }
    }
}