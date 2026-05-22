using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using SKONanobotBuildAndRepairSystem.Caches;
using SKONanobotBuildAndRepairSystem.Profiling;
using SKONanobotBuildAndRepairSystem.Utils;
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

        public static int CacheCount { get { return Cache.Count; } }

        private static readonly ConcurrentDictionary<long, long> GridLastAccess = new ConcurrentDictionary<long, long>();

        private const int MaxRefreshItems = 100;
        private static TimeSpan LastCheckTime;
        private static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(5); // how often we check for expired entries
        private static readonly TimeSpan AllowUnusedGridsForInterval = TimeSpan.FromMinutes(5);

        // PERF-3: prefer the tick-cached play-time published once per Mod.UpdateBeforeSimulation
        // (TimeSpan reads/writes are atomic on x64). Falls back to the live session accessor
        // only on the very first ticks before the cache is populated, mirroring TtlCache.TryGetNow.
        private static long GetNowTicks()
        {
            var now = Mod.NowPlayTime;
            if (now != TimeSpan.Zero) return now.Ticks;
            var session = MyAPIGateway.Session;
            if (session != null) return session.ElapsedPlayTime.Ticks;
            return DateTime.UtcNow.Ticks;
        }

        private static void SetCache(IMyCubeGrid grid, long playerId, MyRelationsBetweenPlayerAndBlock relation)
        {
            var key = new MyTuple<long, long>(grid.EntityId, playerId);
            Cache.Set(key, relation);

            // Record access for this grid to avoid unnecessary refreshes when idle
            try { GridLastAccess[grid.EntityId] = GetNowTicks(); } catch { }
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

                // Touch grid on every query to mark it as active.
                // PERF-3: read tick-cached Mod.NowPlayTime; the original used the
                // Session+ElapsedPlayTime accessor chain on every call.
                try { GridLastAccess[cubeGrid.EntityId] = GetNowTicks(); }
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
            catch (Exception ex)
            {
                Logging.Instance.Error("GridOwnershipCacheHandler.GetRelation exception: {0}", ex.Message);
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
            catch (Exception ex)
            {
                Logging.Instance.Error("GridOwnershipCacheHandler.GetRelationInternal exception: {0}", ex.Message);
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
            catch (Exception ex)
            {
                Logging.Instance.Error("GridOwnershipCacheHandler.CheckOwners exception: {0}", ex.Message);
            }
            return null;
        }

        public static void Update()
        {
            if (MyAPIGateway.Session == null) return;
            var now = MyAPIGateway.Session.ElapsedPlayTime;

            if (now - LastCheckTime < CheckInterval) return;

            LastCheckTime = now;

            var profilerTs = MethodProfiler.Start();
            RefreshExpiredEntries();
            Cache.CleanupExpired();
            if (profilerTs != 0L)
            {
                MethodProfiler.StopAndLog("GridOwnershipCacheHandler.Update", profilerTs, () =>
                    string.Format("cacheCount={0}", Cache.Count));
            }
        }

        private static void RefreshExpiredEntries()
        {
            try
            {
                var nowTicks = GetNowTicks();

                // PERF-4: take the keys snapshot once and reuse for all InvalidateGrid
                // calls below. The pre-fix path called Cache.Entries.Keys.ToArray() inside
                // InvalidateGrid for every stale grid, allocating one array per invalidation.
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
                            InvalidateGridUsingSnapshot(key.Item1, keys);
                        }
                        continue;
                    }

                    if (entry != null)
                        continue;

                    IMyEntity ent;
                    if (!MyAPIGateway.Entities.TryGetEntityById(key.Item1, out ent) || ent == null)
                    {
                        if (invalidatedGrids.Add(key.Item1))
                        {
                            InvalidateGridUsingSnapshot(key.Item1, keys);
                        }
                        continue;
                    }

                    var grid = ent as IMyCubeGrid;
                    if (grid == null || grid.Closed)
                    {
                        if (invalidatedGrids.Add(key.Item1))
                        {
                            InvalidateGridUsingSnapshot(key.Item1, keys);
                        }
                        continue;
                    }

                    var relation = GetRelationBetweenGridAndPlayerInternal(grid, key.Item2);
                    Cache.Set(key, relation);
                    refreshed++;
                }
            }
            catch (Exception ex)
            {
                Logging.Instance.Error("GridOwnershipCacheHandler.RefreshExpiredEntries exception: {0}", ex.Message);
            }
        }

        public static void InvalidateGrid(long gridId)
        {
            try
            {
                InvalidateGridUsingSnapshot(gridId, Cache.Entries.Keys.ToArray());
            }
            catch { }
        }

        // PERF-4: shared invalidation core. Walks the caller-supplied keys snapshot in-place
        // (no per-call allocation). Called from RefreshExpiredEntries with the already-taken
        // outer snapshot, and from InvalidateGrid with a fresh snapshot for external callers.
        private static void InvalidateGridUsingSnapshot(long gridId, MyTuple<long, long>[] keysSnapshot)
        {
            try
            {
                for (int i = 0; i < keysSnapshot.Length; i++)
                {
                    if (keysSnapshot[i].Item1 == gridId) Cache.Remove(keysSnapshot[i]);
                }

                long last;
                GridLastAccess.TryRemove(gridId, out last);
            }
            catch { }
        }
    }
}