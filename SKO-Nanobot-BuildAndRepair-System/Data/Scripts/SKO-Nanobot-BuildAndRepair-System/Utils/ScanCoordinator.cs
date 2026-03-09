namespace SKONanobotBuildAndRepairSystem.Utils
{
    using Sandbox.ModAPI;
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Threading;
    using VRage.Game.ModAPI;
    using VRage.ModAPI;
    using VRageMath;
    using SKONanobotBuildAndRepairSystem;

    /// <summary>
    /// Elects one BaR per mechanical-grid cluster as the scan coordinator.
    /// The coordinator calls GetTopMostEntitiesInBox once for the union of all members'
    /// work areas; other BaRs reuse the result, avoiding N redundant engine calls per cycle.
    /// </summary>
    internal static class ScanCoordinator
    {
        private class ClusterEntry
        {
            // Interlocked access; long.MaxValue means no coordinator elected yet.
            internal long CoordinatorEntityId = long.MaxValue;

            // Written on main thread only (BeginFrame / AccumulateAndElect), under WriteLock.
            // volatile so the async reader's flag test in CoordinatorFetchEntities is always fresh.
            internal BoundingBoxD UnionBBox;
            internal volatile bool UnionBBoxValid;

            // Coordinator writes inside WriteLock; non-coordinators read volatile reference.
            internal volatile List<IMyEntity> CachedEntities;

            // Pre-sorted by grid size (BlocksCount ascending, non-grids appended at end).
            // Populated whenever CachedEntities is refreshed. Same TTL as CachedEntities.
            // Coordinator writes inside WriteLock; non-coordinators read volatile reference.
            internal volatile List<IMyEntity> CachedEntitiesSortedBySize;

            // Written inside WriteLock; worst-case stale read just triggers one extra fallback.
            internal TimeSpan CachedEntitiesTime;

            // Main-thread only.
            internal TimeSpan LastElectionTime;

            // Main-thread only. Set when any BaR in this cluster claims the push slot.
            internal TimeSpan LastPushTime;

            internal readonly object WriteLock = new object();

            // Snapshot of all grid EntityIds in this cluster's mechanical group.
            // Written on main thread inside Elect(); volatile so async readers see updates.
            // Refreshed every election cycle (~60 s). null until the first election.
            internal volatile long[] CachedMechanicalGridIds;
        }

        private static readonly ConcurrentDictionary<long, ClusterEntry> _clusters
            = new ConcurrentDictionary<long, ClusterEntry>();

        // Dedicated lock for entity queries — avoids locking on game-engine objects
        // which could deadlock with internal SE synchronization.
        private static readonly object _entityQueryLock = new object();

        private const double EntityCacheTtlSeconds = 5.0;
        private static readonly TimeSpan ElectionInterval = TimeSpan.FromSeconds(60.0);

        // ──────────────────────────────────────────────────────────────────────
        // Main-thread API
        // ──────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Reset union-bbox state for all tracked clusters.
        /// Call once per timer tick BEFORE iterating BaRs.
        /// </summary>
        internal static void BeginFrame()
        {
            foreach (var kvp in _clusters)
            {
                var e = kvp.Value;
                lock (e.WriteLock)
                {
                    e.UnionBBoxValid = false;
                    e.UnionBBox = BoundingBoxD.CreateInvalid();
                }
            }
        }

        /// <summary>
        /// Register this BaR's world AABB into its cluster's union bbox and run an election
        /// if needed. The cluster key must be pre-computed by the caller via ComputeClusterKey.
        /// </summary>
        internal static void AccumulateAndElect(
            long clusterKey,
            IMyCubeGrid welderGrid,
            BoundingBoxD worldAABB,
            List<NanobotSystem> systemsSnapshot)
        {
            var entry = _clusters.GetOrAdd(clusterKey, _ => new ClusterEntry());

            // Grow the union bounding box (under WriteLock to synchronise with the async CoordinatorFetchEntities reader).
            lock (entry.WriteLock)
            {
                entry.UnionBBox.Include(worldAABB);
                entry.UnionBBoxValid = true;
            }

            // Elect when no coordinator is registered or when the interval has expired.
            var now = MyAPIGateway.Session.ElapsedPlayTime;
            if (Interlocked.Read(ref entry.CoordinatorEntityId) == long.MaxValue
                || now - entry.LastElectionTime >= ElectionInterval)
            {
                Elect(entry, clusterKey, welderGrid, systemsSnapshot);
            }
        }

        /// <summary>
        /// Tries to claim the cluster-wide push slot. Returns true if this BaR should perform
        /// the push; false if another BaR in the same cluster already pushed within the cooldown
        /// window. When this returns true, the cluster's LastPushTime is updated.
        /// Main-thread only.
        /// </summary>
        // ------------------------------------------------------------------
        // Shared empty-grid ignore API
        // (Removed — empty-grid ignore is now per-system, tracked on each
        // NanobotSystem instance to prevent one BaR from suppressing targets
        // that only another BaR with a different work area can reach.)
        // ------------------------------------------------------------------

        internal static bool TryClaimClusterPush(long clusterKey, TimeSpan now, double cooldownSeconds = 5.0)
        {
            ClusterEntry entry;
            if (!_clusters.TryGetValue(clusterKey, out entry))
                return true; // No cluster entry yet — allow the push

            if ((now - entry.LastPushTime).TotalSeconds < cooldownSeconds)
                return false; // Another BaR in this cluster already pushed recently

            entry.LastPushTime = now;
            return true;
        }

        /// <summary>
        /// Releases the cluster push slot by resetting LastPushTime to zero.
        /// Call when PushComponents successfully moved items, so other BaRs in the cluster
        /// can push their own inventories immediately instead of waiting for the next window.
        /// Main-thread only.
        /// </summary>
        internal static void ReleaseClusterPush(long clusterKey)
        {
            ClusterEntry entry;
            if (!_clusters.TryGetValue(clusterKey, out entry))
                return;
            entry.LastPushTime = TimeSpan.Zero;
        }

        private static void Elect(
            ClusterEntry entry,
            long clusterKey,
            IMyCubeGrid welderGrid,
            List<NanobotSystem> systemsSnapshot)
        {
            // Collect all grids in the mechanical group.
            var groupGrids = new List<IMyCubeGrid>();
            MyAPIGateway.GridGroups.GetGroup(welderGrid, GridLinkTypeEnum.Mechanical, groupGrids);

            var groupGridIds = new HashSet<long>();
            foreach (var g in groupGrids)
            {
                if (g != null)
                    groupGridIds.Add(g.EntityId);
            }

            // Elect the lowest EntityId among eligible BaRs in this cluster.
            // A BaR is eligible only if it uses BoundingBox search mode and is fully operational.
            long bestId = long.MaxValue;
            foreach (var system in systemsSnapshot)
            {
                if (system == null || system.Welder == null) continue;
                if (!groupGridIds.Contains(system.Welder.CubeGrid.EntityId)) continue;
                if (!system.Welder.Enabled) continue;
                if (!system.Welder.IsFunctional) continue;
                if (!system.Welder.IsWorking) continue;
                if (system.Settings.SearchMode != SearchModes.BoundingBox) continue;

                var eid = system.Welder.EntityId;
                if (eid < bestId)
                    bestId = eid;
            }

            Interlocked.Exchange(ref entry.CoordinatorEntityId, bestId);
            entry.LastElectionTime = MyAPIGateway.Session.ElapsedPlayTime;

            // Cache the flat list of mechanical grid EntityIds for use in AsyncUpdateSourcesAndTargets.
            // Written here (main thread); read from async thread via volatile field.
            var gridIdArr = new long[groupGridIds.Count];
            var arrIdx = 0;
            foreach (var gid in groupGridIds)
                gridIdArr[arrIdx++] = gid;
            entry.CachedMechanicalGridIds = gridIdArr;

            Logging.Instance.Write(Logging.Level.Verbose,
                "ScanCoordinator: Cluster key={0} elected coordinator EntityId={1}",
                clusterKey,
                bestId == long.MaxValue ? "(none)" : bestId.ToString());
        }

        /// <summary>
        /// Returns true if the given welder is the elected coordinator for its cluster.
        /// Safe to call from the async worker thread (uses Interlocked.Read).
        /// </summary>
        internal static bool IsCoordinator(long clusterKey, long welderEntityId)
        {
            ClusterEntry entry;
            if (!_clusters.TryGetValue(clusterKey, out entry))
                return false;
            return Interlocked.Read(ref entry.CoordinatorEntityId) == welderEntityId;
        }

        // ──────────────────────────────────────────────────────────────────────
        // Async-thread API
        // ──────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Called by the elected coordinator to fetch (or return cached) entities for the
        /// whole cluster union bbox. Returns null if the cluster bbox is not yet valid
        /// (first frame); caller should fall back to its own per-BaR fetch.
        /// </summary>
        internal static List<IMyEntity> CoordinatorFetchEntities(long clusterKey)
        {
            ClusterEntry entry;
            if (!_clusters.TryGetValue(clusterKey, out entry))
                return null;

            // First-frame guard: main thread hasn't set up the union bbox yet.
            if (!entry.UnionBBoxValid)
                return null;

            var now = MyAPIGateway.Session.ElapsedPlayTime;

            // Fast path: already cached within TTL.
            var cached = entry.CachedEntities;   // volatile read
            if (cached != null
                && (now - entry.CachedEntitiesTime).TotalSeconds <= EntityCacheTtlSeconds)
            {
                return cached;
            }

            // Slow path: acquire WriteLock and double-check.
            lock (entry.WriteLock)
            {
                cached = entry.CachedEntities;
                if (cached != null
                    && (now - entry.CachedEntitiesTime).TotalSeconds <= EntityCacheTtlSeconds)
                {
                    return cached;
                }

                var bbox = entry.UnionBBox;
                List<IMyEntity> fetched;
                lock (_entityQueryLock)
                {
                    fetched = MyAPIGateway.Entities.GetTopMostEntitiesInBox(ref bbox);
                }

                entry.CachedEntities = fetched;
                entry.CachedEntitiesTime = MyAPIGateway.Session.ElapsedPlayTime;

                // Build the size-sorted copy for GrindSmallestGridFirst consumers.
                if (fetched != null)
                {
                    var sortedGrids = new List<IMyEntity>();
                    var nonGrids = new List<IMyEntity>();
                    foreach (var ent in fetched)
                    {
                        if (ent is Sandbox.Game.Entities.MyCubeGrid)
                            sortedGrids.Add(ent);
                        else
                            nonGrids.Add(ent);
                    }
                    sortedGrids.Sort((a, b) =>
                        ((Sandbox.Game.Entities.MyCubeGrid)a).BlocksCount -
                        ((Sandbox.Game.Entities.MyCubeGrid)b).BlocksCount);
                    sortedGrids.AddRange(nonGrids);
                    entry.CachedEntitiesSortedBySize = sortedGrids;
                }
                else
                {
                    entry.CachedEntitiesSortedBySize = null;
                }

                Logging.Instance.Write(Logging.Level.Verbose,
                    "ScanCoordinator: Cluster key={0} coordinator fetched {1} entities.",
                    clusterKey, fetched != null ? fetched.Count : 0);

                return fetched;
            }
        }

        /// <summary>
        /// Called by non-coordinator BaRs to read the coordinator's cached entity list.
        /// Returns null on cache miss or TTL expiry; caller falls back to a per-BaR fetch.
        /// No lock taken — reads the volatile reference atomically on x64.
        /// </summary>
        internal static List<IMyEntity> TryGetCachedEntities(long clusterKey)
        {
            ClusterEntry entry;
            if (!_clusters.TryGetValue(clusterKey, out entry))
                return null;

            var cached = entry.CachedEntities;   // volatile read
            if (cached == null)
                return null;

            var now = MyAPIGateway.Session.ElapsedPlayTime;
            if ((now - entry.CachedEntitiesTime).TotalSeconds > EntityCacheTtlSeconds)
                return null;

            Logging.Instance.Write(Logging.Level.Verbose,
                "ScanCoordinator: Cluster key={0} non-coordinator cache hit ({1} entities).",
                clusterKey, cached.Count);

            return cached;
        }

        /// <summary>
        /// Returns the coordinator's cached entity list pre-sorted by grid size (BlocksCount ascending,
        /// non-grid entities appended at the end). Used by BaRs with GrindSmallestGridFirst active
        /// to avoid re-sorting the shared entity list on every scan cycle.
        /// Returns null on cache miss or TTL expiry; caller falls back to a local sort.
        /// No lock taken — reads the volatile reference atomically on x64.
        /// </summary>
        internal static List<IMyEntity> TryGetSizeSortedEntities(long clusterKey)
        {
            ClusterEntry entry;
            if (!_clusters.TryGetValue(clusterKey, out entry))
                return null;

            var cached = entry.CachedEntitiesSortedBySize;   // volatile read
            if (cached == null)
                return null;

            var now = MyAPIGateway.Session.ElapsedPlayTime;
            if ((now - entry.CachedEntitiesTime).TotalSeconds > EntityCacheTtlSeconds)
                return null;

            return cached;
        }

        /// <summary>
        /// Returns the cached flat array of grid EntityIds in the mechanical group for
        /// <paramref name="clusterKey"/>. Returns null until the first election runs.
        /// Refreshed every ~60 s on the main thread; safe to call from async threads
        /// (volatile read, no lock required).
        /// </summary>
        internal static long[] TryGetMechanicalGridIds(long clusterKey)
        {
            ClusterEntry entry;
            if (!_clusters.TryGetValue(clusterKey, out entry))
                return null;

            return entry.CachedMechanicalGridIds;   // volatile read
        }

        // ──────────────────────────────────────────────────────────────────────
        // Lifecycle
        // ──────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Evict cluster entries that have had no activity for more than 10 minutes.
        /// Safe to call from a background thread (no access to NanobotSystems).
        /// </summary>
        internal static void CleanupExpired()
        {
            var now = MyAPIGateway.Session.ElapsedPlayTime;
            var staleThreshold = TimeSpan.FromMinutes(10.0);

            foreach (var key in _clusters.Keys)
            {
                ClusterEntry entry;
                if (!_clusters.TryGetValue(key, out entry))
                    continue;

                // If no fetch has ever happened, CachedEntitiesTime is default (zero).
                // Treat zero as not stale — it will be populated on the first coordinator run.
                if (entry.CachedEntitiesTime == TimeSpan.Zero)
                    continue;

                if (now - entry.CachedEntitiesTime > staleThreshold)
                {
                    ClusterEntry removed;
                    _clusters.TryRemove(key, out removed);
                    continue;
                }
            }
        }

        /// <summary>
        /// Wipe the entire coordinator state. Call from Mod.UnloadData().
        /// </summary>
        internal static void Clear()
        {
            _clusters.Clear();
        }

        // ──────────────────────────────────────────────────────────────────────
        // Helpers
        // ──────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Returns the smallest EntityId in the mechanical group containing <paramref name="grid"/>.
        /// This is stable as long as the grid group doesn't change and serves as the cluster key.
        /// </summary>
        internal static long ComputeClusterKey(IMyCubeGrid grid)
        {
            var groupGrids = new List<IMyCubeGrid>();
            MyAPIGateway.GridGroups.GetGroup(grid, GridLinkTypeEnum.Mechanical, groupGrids);

            long minId = grid.EntityId;
            foreach (var g in groupGrids)
            {
                if (g != null && g.EntityId < minId)
                    minId = g.EntityId;
            }
            return minId;
        }
    }
}
