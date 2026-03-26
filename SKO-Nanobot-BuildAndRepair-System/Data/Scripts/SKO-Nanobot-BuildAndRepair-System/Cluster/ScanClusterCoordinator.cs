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
                    var anyChanged = false;
                    foreach (var system in Mod.NanobotSystems.Values)
                    {
                        var isReady = system.Welder != null && system.Welder.IsFunctional && system.Welder.Enabled && system.State.Ready;
                        if (!isReady)
                        {
                            if (system.AssignedCluster != null) { anyChanged = true; break; }
                            continue;
                        }
                        var key = ComputeClusterKey(system);
                        if (key != system._lastClusterKey) { anyChanged = true; break; }
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
                        continue;
                    }

                    var key = ComputeClusterKey(system);
                    system._lastClusterKey = key;

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
                }

                // Clean up empty clusters
                for (int i = 0; i < keysToRemove.Count; i++)
                {
                    _clusters.Remove(keysToRemove[i]);
                }
            }
            finally
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
