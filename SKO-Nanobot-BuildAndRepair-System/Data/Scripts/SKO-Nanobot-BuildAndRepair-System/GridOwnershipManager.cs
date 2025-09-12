using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using VRage.Game;
using VRage.Game.ModAPI;
using VRage.ModAPI;

namespace SKONanobotBuildAndRepairSystem
{
    public static class GridOwnershipManager
    {
        private static bool _initialized = false;
        private struct GridPlayerKey : IEquatable<GridPlayerKey>
        {
            public readonly long GridId;
            public readonly long PlayerId;
            public GridPlayerKey(long gridId, long playerId)
            {
                GridId = gridId;
                PlayerId = playerId;
            }
            public bool Equals(GridPlayerKey other)
            {
                return GridId == other.GridId && PlayerId == other.PlayerId;
            }
            public override bool Equals(object obj)
            {
                return obj is GridPlayerKey && Equals((GridPlayerKey)obj);
            }
            public override int GetHashCode()
            {
                unchecked
                {
                    // 64-bit mix down to 32-bit for dictionary buckets
                    int a = (int)(GridId ^ (GridId >> 32));
                    int b = (int)(PlayerId ^ (PlayerId >> 32));
                    return (a * 397) ^ b;
                }
            }
        }

        private class CacheEntry
        {
            public MyRelationsBetweenPlayerAndBlock Relation;
            public long ExpireTick;
        }

        // Single-level concurrent cache keyed by (gridId, playerId)
        private static readonly ConcurrentDictionary<GridPlayerKey, CacheEntry> _cache = new ConcurrentDictionary<GridPlayerKey, CacheEntry>();
        // Track last-access time per grid so we can drop idle grids entirely
        private static readonly ConcurrentDictionary<long, long> _gridLastAccess = new ConcurrentDictionary<long, long>();

        // TTL and refresh cadence
        private static readonly long TtlTicks = TimeSpan.FromSeconds(10).Ticks; // align with Utils cache TTL
        private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(5); // how often we check for expired entries
        private static TimeSpan _lastRefreshTime;
        private const int MaxRefreshPerTick = 200; // avoid long stalls
        private static readonly long IdleGridTicks = TimeSpan.FromMinutes(5).Ticks; // drop grids idle for > 5 minutes

        public static void Init()
        {
            if (_initialized) return;
            if (MyAPIGateway.Session == null) return;

            //MyAPIGateway.Entities.OnEntityRemove += Entities_OnEntityRemove;
            //MyAPIGateway.Session.Factions.FactionStateChanged += Factions_FactionStateChanged;

            _initialized = true;
        }

        public static void Unload()
        {
            if (!_initialized) return;

            //MyAPIGateway.Entities.OnEntityRemove -= Entities_OnEntityRemove;
            //MyAPIGateway.Session.Factions.FactionStateChanged -= Factions_FactionStateChanged;

            _initialized = false;
        }

        /// <summary>
        /// Called each simulation tick from the session component to schedule periodic cache refreshes.
        /// </summary>
        public static void UpdateTick()
        {
            if (!_initialized || MyAPIGateway.Session == null) return;

            var now = MyAPIGateway.Session.ElapsedPlayTime;
            if (now - _lastRefreshTime < RefreshInterval) return;

            _lastRefreshTime = now;

            AsyncTaskQueue.Enqueue(RefreshExpiredEntries);
        }

        private static void RefreshExpiredEntries()
        {
            try
            {
                var session = MyAPIGateway.Session;
                var nowTicks = session != null ? session.ElapsedPlayTime.Ticks : DateTime.UtcNow.Ticks;

                var keys = _cache.Keys.ToArray();
                int refreshed = 0;
                var invalidatedGrids = new HashSet<long>();
                for (int i = 0; i < keys.Length && refreshed < MaxRefreshPerTick; i++)
                {
                    var key = keys[i];
                    CacheEntry entry;
                    if (!_cache.TryGetValue(key, out entry))
                        continue;

                    // If this grid has been idle for too long, remove all its cache entries
                    long lastAccess;
                    if (!_gridLastAccess.TryGetValue(key.GridId, out lastAccess) || (nowTicks - lastAccess) > IdleGridTicks)
                    {
                        if (invalidatedGrids.Add(key.GridId))
                        {
                            InvalidateGrid(key.GridId);
                        }
                        continue;
                    }

                    if (entry.ExpireTick > nowTicks)
                        continue;

                    IMyEntity ent;
                    if (!MyAPIGateway.Entities.TryGetEntityById(key.GridId, out ent) || ent == null)
                    {
                        // Grid gone: remove all cached pairs for this grid
                        InvalidateGrid(key.GridId);
                        continue;
                    }

                    var grid = ent as IMyCubeGrid;
                    if (grid == null || grid.Closed)
                    {
                        InvalidateGrid(key.GridId);
                        continue;
                    }

                    var relation = GetRelationBetweenGridAndPlayerInternal(grid, key.PlayerId);
                    _cache[key] = new CacheEntry { Relation = relation, ExpireTick = nowTicks + TtlTicks };
                    refreshed++;
                }
            }
            catch
            {
                // swallow errors in background refresh
            }
        }

        private static void Factions_FactionStateChanged(MyFactionStateChange stateChange, long fromFactionId, long toFactionId, long arg4, long arg5)
        {
            try
            {
                if (fromFactionId > 0)
                {
                    var faction = MyAPIGateway.Session.Factions.TryGetFactionById(fromFactionId);
                    if (faction != null)
                    {
                        var playerIds = faction.Members.Values.Where(c => c.PlayerId > 0).Select(c => c.PlayerId);
                        RemoveForPlayers(playerIds);
                    }
                }
            }
            catch { }

            try
            {
                if (toFactionId > 0)
                {
                    var faction = MyAPIGateway.Session.Factions.TryGetFactionById(toFactionId);
                    if (faction != null)
                    {
                        var playerIds = faction.Members.Values.Where(c => c.PlayerId > 0).Select(c => c.PlayerId);
                        RemoveForPlayers(playerIds);
                    }
                }
            }
            catch { }
        }

        private static void RemoveForPlayers(IEnumerable<long> playerIds)
        {
            try
            {
                var toRemove = new List<GridPlayerKey>();
                var snapshot = _cache.Keys.ToArray();
                foreach (var key in snapshot)
                {
                    if (playerIds.Contains(key.PlayerId)) toRemove.Add(key);
                }
                foreach (var key in toRemove)
                {
                    CacheEntry removed;
                    _cache.TryRemove(key, out removed);
                }
            }
            catch { }
        }

        private static void Entities_OnEntityRemove(IMyEntity obj)
        {
            if (obj == null) return;
            if (obj is IMyCubeGrid)
            {
                try { InvalidateGrid(obj.EntityId); } catch { }

                try
                {
                    // Unregister grid events.
                    //(obj as IMyCubeGrid).OnBlockOwnershipChanged -= Grid_OnBlockOwnershipChanged;
                }
                catch { }
            }
        }

        private static void SetCache(IMyCubeGrid grid, long playerId, MyRelationsBetweenPlayerAndBlock relation)
        {
            var nowTicks = MyAPIGateway.Session != null ? MyAPIGateway.Session.ElapsedPlayTime.Ticks : DateTime.UtcNow.Ticks;
            var key = new GridPlayerKey(grid.EntityId, playerId);
            _cache[key] = new CacheEntry { Relation = relation, ExpireTick = nowTicks + TtlTicks };
            // Record access for this grid to avoid unnecessary refreshes when idle
            try { _gridLastAccess[grid.EntityId] = nowTicks; } catch { }
        }

        private static CacheEntry GetFromCache(long cubeGridEntityId, long playerid)
        {
            try
            {
                CacheEntry entry;
                if (!_cache.TryGetValue(new GridPlayerKey(cubeGridEntityId, playerid), out entry)) return null;
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
                    var nowTicksTouch = MyAPIGateway.Session != null ? MyAPIGateway.Session.ElapsedPlayTime.Ticks : DateTime.UtcNow.Ticks;
                    _gridLastAccess[cubeGrid.EntityId] = nowTicksTouch;
                }
                catch { }

                // Try get the relation from our cache first.
                var entry = GetFromCache(cubeGrid.EntityId, playerId);
                var nowTicks = MyAPIGateway.Session != null ? MyAPIGateway.Session.ElapsedPlayTime.Ticks : DateTime.UtcNow.Ticks;
                if (entry != null && entry.ExpireTick > nowTicks)
                {
                    return entry.Relation;
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

        public static void InvalidateGrid(long gridId)
        {
            try
            {
                var toRemove = new List<GridPlayerKey>();
                var keys = _cache.Keys.ToArray();
                for (int i = 0; i < keys.Length; i++)
                {
                    if (keys[i].GridId == gridId) toRemove.Add(keys[i]);
                }
                for (int i = 0; i < toRemove.Count; i++)
                {
                    CacheEntry removed;
                    _cache.TryRemove(toRemove[i], out removed);
                }
                long last;
                _gridLastAccess.TryRemove(gridId, out last);
            }
            catch { }
        }
    }
}
