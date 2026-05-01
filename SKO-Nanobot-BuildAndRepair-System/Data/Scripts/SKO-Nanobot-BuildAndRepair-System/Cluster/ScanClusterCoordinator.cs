using SKONanobotBuildAndRepairSystem.Models;
using SKONanobotBuildAndRepairSystem.Profiling;
using System.Collections.Generic;

namespace SKONanobotBuildAndRepairSystem.Cluster
{
    /// <summary>
    /// Static orchestrator that groups BaR systems with equivalent scan configurations
    /// into clusters. One BaR per cluster performs the expensive scan; all members
    /// apply their own range/distance filtering to the shared results.
    /// Called from Mod.RebuildSourcesAndTargetsTimer() on the main thread.
    /// </summary>
    static class ScanClusterCoordinator
    {
        private static Dictionary<string, ScanCluster> _clusters = new Dictionary<string, ScanCluster>();
        private static int _lastSystemCount;

        public static int ClusterCount { get { return _clusters.Count; } }

        /// <summary>
        /// Mask of SyncBlockSettings.Settings flags that affect scan results.
        /// </summary>
        private const SyncBlockSettings.Settings ClusterRelevantFlags =
            SyncBlockSettings.Settings.UseIgnoreColor |
            SyncBlockSettings.Settings.UseGrindColor |
            SyncBlockSettings.Settings.AllowBuild |
            SyncBlockSettings.Settings.GrindSmallestGridFirst |
            SyncBlockSettings.Settings.GrindNearFirst |
            SyncBlockSettings.Settings.GrindIgnorePriorityOrder;

        /// <summary>
        /// Rebuilds clusters from all active NanobotSystems. O(N) on main thread.
        /// All systems (including solo BaRs) get assigned to a cluster.
        /// </summary>
        public static void RebuildClusters()
        {
            var profilerTs = MethodProfiler.Start();
            int clusterCount = 0;
            int clusteredSystems = 0;
            var skipped = false;
            try
            {
                // Quick check: if BaR count unchanged, compare keys to detect settings changes.
                // Skip the full rebuild if nothing changed — saves O(N) dictionary/list operations.
                var systemCount = Mod.NanobotSystems.Count;
                if (systemCount == _lastSystemCount && _clusters.Count > 0)
                {
                    // FEAT-072: Use cheap numeric hash comparison instead of recomputing
                    // the full cluster key string for every BaR every second.
                    // ComputeClusterKeyHash uses only integer/bool fields — no string allocation.
                    var anyChanged = false;
                    foreach (var system in Mod.NanobotSystems.Values)
                    {
                        var isReady = system.Welder != null && system.Welder.IsFunctional && system.Welder.Enabled && system.State.Ready;
                        if (!isReady)
                        {
                            if (system.AssignedCluster != null) { anyChanged = true; break; }
                            continue;
                        }
                        var hash = ComputeClusterKeyHash(system);
                        if (hash != system._lastClusterKeyHash) { anyChanged = true; break; }
                    }
                    if (!anyChanged)
                    {
                        skipped = true;
                        return;
                    }
                }
                _lastSystemCount = systemCount;

                // Clear previous clusters
                foreach (var cluster in _clusters.Values)
                {
                    cluster.Members.Clear();
                }

                // Group systems by cluster key
                foreach (var system in Mod.NanobotSystems.Values)
                {
                    // Skip systems that aren't ready for scanning
                    if (system.Welder == null || !system.Welder.IsFunctional || !system.Welder.Enabled || !system.State.Ready)
                    {
                        system.AssignedCluster = null;
                        system._lastClusterKey = null;
                        system._lastClusterKeyHash = 0;
                        continue;
                    }

                    var key = ComputeClusterKey(system);
                    system._lastClusterKey = key;
                    system._lastClusterKeyHash = ComputeClusterKeyHash(system);

                    ScanCluster cluster;
                    if (!_clusters.TryGetValue(key, out cluster))
                    {
                        cluster = new ScanCluster(key);
                        _clusters[key] = cluster;
                    }
                    cluster.Members.Add(system);
                }

                // Assign clusters (including single-member clusters)
                var keysToRemove = new List<string>();
                foreach (var kvp in _clusters)
                {
                    var cluster = kvp.Value;
                    if (cluster.Members.Count == 0)
                    {
                        keysToRemove.Add(kvp.Key);
                        continue;
                    }

                    cluster.ElectCoordinator();
                    for (int i = 0; i < cluster.Members.Count; i++)
                    {
                        cluster.Members[i].AssignedCluster = cluster;
                    }
                    clusterCount++;
                    clusteredSystems += cluster.Members.Count;

                    // BUG-260501.1: propagate any pending _rescanForced from non-coordinator
                    // members to the (possibly new) coordinator. A ClusterRelevantFlags toggle
                    // (e.g. GrindNearFirst) reshuffles the toggling BaR into a different cluster
                    // whose coordinator never received TriggerImmediateRescan — without this
                    // hop, the saturated-skip gate suppresses the scan for up to 60s and the
                    // cluster keeps serving the pre-toggle sort order. Clear the member flag
                    // on consumption so we don't re-trigger on subsequent rebuilds.
                    var coord = cluster.Coordinator;
                    if (coord != null)
                    {
                        var hasPendingTrigger = false;
                        for (int i = 0; i < cluster.Members.Count; i++)
                        {
                            var m = cluster.Members[i];
                            if (m == coord) continue;
                            if (m._rescanForced)
                            {
                                hasPendingTrigger = true;
                                m._rescanForced = false;
                            }
                        }
                        if (hasPendingTrigger)
                        {
                            coord.InheritForcedRescan();
                        }
                    }
                }

                // Clean up empty clusters
                for (int i = 0; i < keysToRemove.Count; i++)
                {
                    _clusters.Remove(keysToRemove[i]);
                }
            }
            finally
            {
                if (profilerTs != 0L)
                {
                    var _clusterCount = clusterCount;
                    var _clusteredSystems = clusteredSystems;
                    var _totalSystems = Mod.NanobotSystems.Count;
                    var _skipped = skipped;
                    MethodProfiler.StopAndLog("ScanClusterCoordinator.RebuildClusters", profilerTs, () =>
                        string.Format("clusters={0};clusteredSystems={1};totalSystems={2};skipped={3}",
                            _clusterCount, _clusteredSystems, _totalSystems, _skipped));
                }
            }
        }

        /// <summary>
        /// FEAT-072: Computes a numeric hash of all cluster-relevant fields.
        /// Used in the fast path to detect settings changes without string allocation.
        /// The full string key (ComputeClusterKey) is only computed when a rebuild is needed.
        /// </summary>
        public static int ComputeClusterKeyHash(NanobotSystem system)
        {
            var s = system.Settings;
            var flags = (int)(s.Flags & ClusterRelevantFlags);

            // FNV-1a style hash combining — fast, no allocation, good distribution.
            unchecked
            {
                int hash = (int)2166136261;
                hash = (hash ^ (int)(system.Welder.CubeGrid.EntityId >> 32)) * 16777619;
                hash = (hash ^ (int)(system.Welder.CubeGrid.EntityId)) * 16777619;
                hash = (hash ^ (int)s.WorkMode) * 16777619;
                hash = (hash ^ (int)s.SearchMode) * 16777619;
                hash = (hash ^ flags) * 16777619;

                // Priority strings — hash their .NET hash codes (stable within a session)
                hash = (hash ^ (s.WeldPriority != null ? s.WeldPriority.GetHashCode() : 0)) * 16777619;
                hash = (hash ^ (s.GrindPriority != null ? s.GrindPriority.GetHashCode() : 0)) * 16777619;
                hash = (hash ^ (s.ComponentCollectPriority != null ? s.ComponentCollectPriority.GetHashCode() : 0)) * 16777619;

                // Color settings (only relevant if flag is set)
                if ((flags & (int)SyncBlockSettings.Settings.UseIgnoreColor) != 0)
                    hash = (hash ^ (int)s.IgnoreColorPacked) * 16777619;
                if ((flags & (int)SyncBlockSettings.Settings.UseGrindColor) != 0)
                    hash = (hash ^ (int)s.GrindColorPacked) * 16777619;

                // Grind janitor settings
                hash = (hash ^ (int)s.UseGrindJanitorOn) * 16777619;
                hash = (hash ^ (int)s.GrindJanitorOptions) * 16777619;

                // Weld options
                hash = (hash ^ (int)s.WeldOptions) * 16777619;

                // Ownership settings
                hash = (hash ^ (int)(system.Welder.OwnerId >> 32)) * 16777619;
                hash = (hash ^ (int)(system.Welder.OwnerId)) * 16777619;
                hash = (hash ^ (system.Welder.UseConveyorSystem ? 1 : 0)) * 16777619;

                // Safe zone state
                hash = (hash ^ (system.State.SafeZoneAllowsWelding ? 1 : 0)) * 16777619;
                hash = (hash ^ (system.State.SafeZoneAllowsGrinding ? 1 : 0)) * 16777619;

                return hash;
            }
        }

        /// <summary>
        /// Builds a cluster key string from all fields that affect scan results.
        /// Two BaRs with the same key produce identical candidates (modulo range/distance).
        /// </summary>
        public static string ComputeClusterKey(NanobotSystem system)
        {
            var s = system.Settings;
            var flags = s.Flags & ClusterRelevantFlags;

            // Start with grid, work mode, search mode, flags
            var key = string.Format("{0}|{1}|{2}|{3}",
                system.Welder.CubeGrid.EntityId,
                (int)s.WorkMode,
                (int)s.SearchMode,
                (int)flags);

            // Priority strings (already serialized with order + enabled state)
            key += "|" + (s.WeldPriority ?? "");
            key += "|" + (s.GrindPriority ?? "");
            key += "|" + (s.ComponentCollectPriority ?? "");

            // Color settings (only relevant if flag is set)
            if ((flags & SyncBlockSettings.Settings.UseIgnoreColor) != 0)
            {
                key += "|IC" + s.IgnoreColorPacked;
            }
            if ((flags & SyncBlockSettings.Settings.UseGrindColor) != 0)
            {
                key += "|GC" + s.GrindColorPacked;
            }

            // Grind janitor settings
            key += "|" + (int)s.UseGrindJanitorOn;
            key += "|" + (int)s.GrindJanitorOptions;

            // Weld options (FunctionalOnly affects NeedRepair check)
            key += "|" + (int)s.WeldOptions;

            // Ownership settings
            key += "|" + system.Welder.OwnerId;
            key += "|" + (system.Welder.UseConveyorSystem ? "1" : "0");

            // Safe zone state — BaRs inside vs outside safe zones need separate clusters
            // so the coordinator's scan gates match all members' capabilities.
            // BUG-053: Timing fix ensures safe zone state is refreshed for all BaRs
            // immediately before RebuildClusters() (see Mod.RebuildSourcesAndTargetsTimer).
            key += "|SZ" + (system.State.SafeZoneAllowsWelding ? "1" : "0")
                 + (system.State.SafeZoneAllowsGrinding ? "1" : "0");

            return key;
        }

        /// <summary>
        /// Session unload cleanup.
        /// </summary>
        public static void Clear()
        {
            _clusters.Clear();
        }
    }
}
