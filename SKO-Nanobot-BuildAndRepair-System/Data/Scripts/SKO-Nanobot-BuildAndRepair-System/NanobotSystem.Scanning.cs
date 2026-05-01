using Sandbox.Definitions;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using SKONanobotBuildAndRepairSystem.Cluster;
using SKONanobotBuildAndRepairSystem.Extensions;
using SKONanobotBuildAndRepairSystem.Handlers;
using SKONanobotBuildAndRepairSystem.Helpers;
using SKONanobotBuildAndRepairSystem.Caches;
using SKONanobotBuildAndRepairSystem.Models;
using SKONanobotBuildAndRepairSystem.Profiling;
using SKONanobotBuildAndRepairSystem.Utils;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using VRage.Game;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRageMath;
using static SKONanobotBuildAndRepairSystem.Utils.UtilsInventory;
using IMyShipWelder = Sandbox.ModAPI.IMyShipWelder;
using IMyTerminalBlock = Sandbox.ModAPI.IMyTerminalBlock;

namespace SKONanobotBuildAndRepairSystem
{
    public partial class NanobotSystem
    {
        /// <summary>
        /// Force the next scan to fire immediately. No-op on clients.
        /// <paramref name="bypassDebounce"/> drops the FEAT-075 forceDebounce — use it for
        /// user-driven triggers (settings change) where the debounce window feels broken.
        /// </summary>
        internal void TriggerImmediateRescan(string reason, bool bypassDebounce = false)
        {
            if (!MyAPIGateway.Session.IsServer) return;

            var immediateScanTs = MethodProfiler.Start();
            _LastTargetsUpdate = TimeSpan.Zero;

            // BUG-139: set _rescanForced on THIS BaR too — settings changes can reshuffle
            // it into a different cluster before the new coordinator sees the trigger.
            _rescanForced = true;
            if (bypassDebounce)
            {
                _lastFullScanTime = TimeSpan.Zero;
            }

            // Always poke the coordinator's flag, including the self case.
            var cluster = AssignedCluster;
            var coordinator = cluster != null ? cluster.Coordinator : null;
            if (coordinator != null)
            {
                coordinator._rescanForced = true;
                if (coordinator != this)
                {
                    coordinator._LastTargetsUpdate = TimeSpan.Zero;
                }
                if (bypassDebounce)
                {
                    coordinator._lastFullScanTime = TimeSpan.Zero;
                }
            }
            UpdateSourcesAndTargetsTimer();

            if (immediateScanTs != 0L)
            {
                var _reason = reason;
                var _bypass = bypassDebounce;
                MethodProfiler.StopAndLog("ImmediateRescanTrigger", immediateScanTs, () =>
                    string.Format("entityId={0};reason={1};bypassDebounce={2}", _Welder.EntityId, _reason, _bypass));
            }
        }

        /// <summary>
        /// BUG-260501.1: surfaces a pending _rescanForced flag onto the new coordinator
        /// after a cluster reshuffle so the saturated-skip gate doesn't suppress it.
        /// </summary>
        internal void InheritForcedRescan()
        {
            _rescanForced = true;
            _lastFullScanTime = TimeSpan.Zero;
            _LastTargetsUpdate = TimeSpan.Zero;
        }

        public void UpdateSourcesAndTargetsTimer()
        {
            // Block is off — skip scanning. Reset initial-scan flag so a scan
            // triggers immediately when the block is re-enabled.
            if (!_Welder.Enabled || !_Welder.IsFunctional)
            {
                _InitialScanCompleted = false;
                return;
            }

            var playTime = MyAPIGateway.Session.ElapsedPlayTime;

            // Capture cluster reference (atomic read) early — needed for idle check.
            var cluster = AssignedCluster;
            if (cluster == null)
            {
                // Not yet assigned to a cluster (first tick or system excluded from clustering) — skip this cycle
                return;
            }

            // Projector cold-start detection: when idle and AllowBuild is enabled,
            // check if any projector on our grid has buildable blocks. If so, reset
            // the idle backoff so the next scan discovers them within 1 tick instead
            // of waiting 20s+. Only the coordinator checks (once per 1s timer tick).
            // BuildableBlocksCount is a cheap property read — no scan overhead.
            var coordinator = cluster.Coordinator;
            if (coordinator == this
                && _consecutiveEmptyScans >= 1
                && Settings.WorkMode != WorkModes.GrindOnly
                && (Settings.Flags & SyncBlockSettings.Settings.AllowBuild) != 0)
            {
                if (HasBuildableProjectorOnGrid())
                {
                    _consecutiveEmptyScans = 0;
                    _rescanForced = true;
                }
            }

            // FEAT-071: longer scan interval after consecutive empty scans; members
            // inherit the coordinator's idle state.
            var idleCount = coordinator != null ? coordinator._consecutiveEmptyScans : 0;
            var effectiveTargetInterval = idleCount >= IdleScansBeforeBackoff
                ? IdleScanInterval
                : Mod.Settings.TargetsUpdateInterval;
            var updateTargets = playTime.Subtract(_LastTargetsUpdate) >= effectiveTargetInterval;
            var updateSources = updateTargets && playTime.Subtract(_LastSourceUpdate) >= Mod.Settings.SourcesUpdateInterval;
            if (updateTargets)
            {
                if (cluster.IsCoordinator(this))
                {
                    // FEAT-075: skip full scan when the target list is still saturated.
                    // Safety: force rescan every MaxScanSkipDuration regardless.
                    var timeSinceFullScan = playTime.Subtract(_lastFullScanTime);
                    // Half-interval debounce (min 5s) coalesces multiple member signals.
                    var forceDebounce = TimeSpan.FromSeconds(Math.Max(5, Mod.Settings.TargetsUpdateInterval.TotalSeconds / 2));
                    var forceRescan = _rescanForced && timeSinceFullScan >= forceDebounce;
                    // Both loops exhausted = stale list (projected blocks built but still
                    // looking alive); bypass saturated skip.
                    var coordExhausted = _weldLoopExhausted && _grindLoopExhausted;
                    if (_InitialScanCompleted
                        && !forceRescan
                        && !coordExhausted
                        && timeSinceFullScan < MaxScanSkipDuration
                        && IsTargetListSaturated())
                    {
                        _LastTargetsUpdate = playTime;
                        _scanSkippedSaturated = true;
                        return;
                    }
                    _scanSkippedSaturated = false;
                    _rescanForced = false;
                    StartAsyncClusterScan(cluster, updateSources);
                }
                else
                {
                    // FEAT-075: members skip when the coordinator skipped (saturated).
                    if (coordinator != null && coordinator._scanSkippedSaturated)
                    {
                        _LastTargetsUpdate = playTime;
                        return;
                    }
                    StartAsyncApplyClusterResults(cluster, updateSources);
                }
            }
        }

        /// <summary>
        /// FEAT-075: per-work-type live-target check; rescan if any active type fell below threshold.
        /// </summary>
        private bool IsTargetListSaturated()
        {
            // Check grind targets
            var grindActive = Settings.WorkMode != WorkModes.WeldOnly;
            if (grindActive && _lastScanGrindCandidateCount > 0)
            {
                int liveGrind = 0;
                lock (State.PossibleGrindTargets)
                {
                    foreach (var t in State.PossibleGrindTargets)
                    {
                        if (t.Block != null && t.Block.CubeGrid != null
                            && (t.Block.FatBlock == null || !t.Block.FatBlock.Closed))
                        {
                            liveGrind++;
                            if (liveGrind > SaturatedRescanThreshold) break;
                        }
                    }
                }
                if (liveGrind <= SaturatedRescanThreshold) return false;
            }

            // Check weld targets
            var weldActive = Settings.WorkMode != WorkModes.GrindOnly;
            if (weldActive && _lastScanWeldCandidateCount > 0)
            {
                int liveWeld = 0;
                lock (State.PossibleWeldTargets)
                {
                    foreach (var t in State.PossibleWeldTargets)
                    {
                        if (t.Block != null && t.Block.CubeGrid != null)
                        {
                            liveWeld++;
                            if (liveWeld > SaturatedRescanThreshold) break;
                        }
                    }
                }
                if (liveWeld <= SaturatedRescanThreshold) return false;
            }

            // At least one active type must be saturated for the skip to apply.
            // If both types had 0 candidates on last scan, the idle backoff (FEAT-071) handles it.
            return _lastScanGrindCandidateCount > 0 || _lastScanWeldCandidateCount > 0;
        }

        /// <summary>
        /// Scans for inventory sources on the BaR's own grid and all connected grids.
        /// Uses raw (unsorted) block lists — no expensive sort needed for source scanning.
        /// Traverses mechanical connections and connectors via BFS.
        /// </summary>
        private void AsyncScanForSources(List<IMyInventory> possibleSources)
        {
            var profilerTs = MethodProfiler.Start();
            // BUG-110: reusable scan-thread traversal state.
            if (_ScanSourceVisitedGridIds == null) _ScanSourceVisitedGridIds = new HashSet<long>();
            else _ScanSourceVisitedGridIds.Clear();
            if (_ScanSourceGridQueue == null) _ScanSourceGridQueue = new Queue<IMyCubeGrid>();
            else _ScanSourceGridQueue.Clear();
            // BUG-119: dedup set for AddIfConnectedToInventory.
            if (_ScanSourceDedupSet == null) _ScanSourceDedupSet = new HashSet<IMyInventory>();
            else _ScanSourceDedupSet.Clear();
            var visited = _ScanSourceVisitedGridIds;
            var toVisit = _ScanSourceGridQueue;
            var dedupSet = _ScanSourceDedupSet;
            // Seed the dedup set with anything the caller already populated (defensive — current
            // callers pass an empty buffer, but keeps the contract correct if that changes).
            for (var i = 0; i < possibleSources.Count; i++) dedupSet.Add(possibleSources[i]);
            toVisit.Enqueue(_Welder.CubeGrid);

            // BUG-141: source-scan diagnostics.
            var blocksExamined = 0;
            var terminalBlocksChecked = 0;
            var addIfConnCalls = 0;
            var tsAddIfConnTotal = 0L;

            try
            {
                while (toVisit.Count > 0)
                {
                    var grid = toVisit.Dequeue();
                    if (grid == null || !visited.Add(grid.EntityId)) continue;

                    // Use shared raw block cache — no per-BaR sort needed for sources.
                    var blocks = SharedGridBlockCache.GetBlocks(grid);

                    foreach (var slimBlock in blocks)
                    {
                        blocksExamined++;
                        var fatBlock = slimBlock.FatBlock;
                        if (fatBlock == null) continue;

                        // Follow mechanical connections and connectors to connected grids.
                        var mechanical = fatBlock as Sandbox.ModAPI.IMyMechanicalConnectionBlock;
                        if (mechanical != null)
                        {
                            if (mechanical.TopGrid != null)
                                toVisit.Enqueue(mechanical.TopGrid);
                            continue;
                        }

                        var attachable = fatBlock as Sandbox.ModAPI.IMyAttachableTopBlock;
                        if (attachable != null)
                        {
                            if (attachable.Base != null && attachable.Base.CubeGrid != null)
                                toVisit.Enqueue(attachable.Base.CubeGrid);
                            continue;
                        }

                        var connector = fatBlock as Sandbox.ModAPI.IMyShipConnector;
                        if (connector != null)
                        {
                            if (connector.Status == Sandbox.ModAPI.Ingame.MyShipConnectorStatus.Connected && connector.OtherConnector != null)
                                toVisit.Enqueue(connector.OtherConnector.CubeGrid);
                        }

                        // Check for source-eligible blocks.
                        var terminalBlock = fatBlock as IMyTerminalBlock;
                        if (terminalBlock != null && terminalBlock.EntityId != _Welder.EntityId && terminalBlock.IsFunctional)
                        {
                            terminalBlocksChecked++;
                            var relation = terminalBlock.GetUserRelationToOwner(_Welder.OwnerId);
                            if (MyRelationsBetweenPlayerAndBlockExtensions.IsFriendly(relation))
                            {
                                try
                                {
                                    addIfConnCalls++;
                                    var tsMark = profilerTs != 0L ? Stopwatch.GetTimestamp() : 0L;
                                    terminalBlock.AddIfConnectedToInventory(_Welder, possibleSources, dedupSet);
                                    if (tsMark != 0L) tsAddIfConnTotal += Stopwatch.GetTimestamp() - tsMark;
                                }
                                catch (Exception ex)
                                {
                                    Logging.Instance.Write(Logging.Level.Event, "BuildAndRepairSystemBlock {0}: AsyncScanForSources exception: {1}", Logging.BlockName(_Welder, Logging.BlockNameOptions.None), ex);
                                }
                            }
                        }
                    }
                }
            }
            finally
            {
                if (profilerTs != 0L)
                {
                    var _visitedCount = visited.Count;
                    var _sourceCount = possibleSources.Count;
                    var _blocksExamined = blocksExamined;
                    var _terminalBlocksChecked = terminalBlocksChecked;
                    var _addIfConnCalls = addIfConnCalls;
                    var _addIfConnTotalMs = tsAddIfConnTotal * 1000.0 / Stopwatch.Frequency;
                    MethodProfiler.StopAndLog("AsyncScanForSources", profilerTs, () =>
                        string.Format("entityId={0};gridsVisited={1};sourcesFound={2};blocksExamined={3};terminalBlocksChecked={4};addIfConnCalls={5};addIfConnTotalMs={6:F3}",
                            _Welder.EntityId, _visitedCount, _sourceCount, _blocksExamined, _terminalBlocksChecked, _addIfConnCalls, _addIfConnTotalMs));
                }
            }
        }



        /// <summary>
        /// Removes expired entries from the empty grid cache.
        /// Uses two-pass (collect keys, then remove) to avoid modifying the dictionary during enumeration.
        /// </summary>
        private void CleanupEmptyGridCache()
        {
            var emptyDelay = Mod.Settings.EmptyGridRescanDelaySeconds;
            if (emptyDelay <= 0 || _EmptyGridCache.Count == 0) return;

            var playTime = MyAPIGateway.Session.ElapsedPlayTime;
            // REF-2: reuse the pooled buffer. Lazy-init keeps the field at null on BaRs
            // whose empty-grid cache never accumulates expirable entries.
            if (_emptyGridExpiredKeys == null) _emptyGridExpiredKeys = new List<long>();
            else _emptyGridExpiredKeys.Clear();

            foreach (var kvp in _EmptyGridCache)
            {
                if (playTime.Subtract(kvp.Value).TotalSeconds >= emptyDelay)
                {
                    _emptyGridExpiredKeys.Add(kvp.Key);
                }
            }

            if (_emptyGridExpiredKeys.Count > 0)
            {
                TimeSpan dummy;
                for (int i = 0; i < _emptyGridExpiredKeys.Count; i++)
                {
                    _EmptyGridCache.TryRemove(_emptyGridExpiredKeys[i], out dummy);
                }
                _emptyGridExpiredKeys.Clear();
            }
        }


        /// <summary>
        /// Starts the async cluster scan for the coordinator BaR.
        /// Guards against re-entry with _AsyncUpdateSourcesAndTargetsRunning flag.
        /// </summary>
        private void StartAsyncClusterScan(ScanCluster cluster, bool updateSource)
        {
            if (!_Welder.UseConveyorSystem)
            {
                lock (_PossibleSources) { _PossibleSources.Clear(); }
                lock (_PossiblePushTargets) { _PossiblePushTargets.Clear(); }
            }

            if (!_Welder.Enabled || !_Welder.IsFunctional || State.Ready == false)
            {
                // BUG-095: defer cleanup while a prior scan is still running.
                lock (_Welder)
                {
                    if (_AsyncUpdateSourcesAndTargetsRunning) return;
                }
                lock (State.PossibleWeldTargets) { State.PossibleWeldTargets.Clear(); State.PossibleWeldTargets.RebuildHash(); }
                lock (State.PossibleGrindTargets) { State.PossibleGrindTargets.Clear(); State.PossibleGrindTargets.RebuildHash(); }
                lock (State.PossibleFloatingTargets) { State.PossibleFloatingTargets.Clear(); State.PossibleFloatingTargets.RebuildHash(); }
                _InitialScanCompleted = false;
                _LastTargetsUpdate = MyAPIGateway.Session.ElapsedPlayTime;
                _LastSourceUpdate = _LastTargetsUpdate;
                return;
            }

            lock (_Welder)
            {
                if (_AsyncUpdateSourcesAndTargetsRunning) return;
                _AsyncUpdateSourcesAndTargetsRunning = true;
                Mod.AddAsyncAction(() => AsyncClusterScan(cluster, updateSource));
            }
        }

        /// <summary>
        /// Coordinator scan: performs the expensive scan once for the entire cluster.
        /// Produces a ScanClusterResult with candidates that passed all position-independent checks
        /// (but NOT IsInRange for multi-member clusters). After publishing shared results, applies own range/distance filtering.
        /// </summary>
        private void AsyncClusterScan(ScanCluster cluster, bool updateSource)
        {
            var profilerTs = MethodProfiler.Start();
            try
            {
                if (!State.Ready) return;

                var weldingEnabled = BlockWeldPriority.AnyEnabled && Settings.WorkMode != WorkModes.GrindOnly;

                var useGrindColor = (Settings.Flags & SyncBlockSettings.Settings.UseGrindColor) != 0;
                var autoGrindRelation = Settings.UseGrindJanitorOn;
                var grindingEnabled = BlockGrindPriority.AnyEnabled
                    && Settings.WorkMode != WorkModes.WeldOnly
                    && (useGrindColor || autoGrindRelation != 0);

                updateSource &= _Welder.UseConveyorSystem;

                // Collection budget: 4x-16x cap so SortAndCapGridCandidates can pick
                // the best 256 per grid; multiplier scales with cluster member count.
                var memberCount = cluster.Members.Count;
                var capMultiplier = Math.Max(4, Math.Min(memberCount * 4, 16));
                var maxWeld = MaxPossibleWeldTargets * capMultiplier;
                var maxGrind = MaxPossibleGrindTargets * capMultiplier;
                var maxFloat = MaxPossibleFloatingTargets * capMultiplier;

                var result = new ScanClusterResult();
                var clusterWeldTargets = weldingEnabled ? result.WeldCandidates : null;
                var clusterGrindTargets = grindingEnabled ? result.GrindCandidates : null;
                var clusterFloatingTargets = _ComponentCollectPriority.AnyEnabled ? result.FloatingCandidates : null;

                try
                {
                    // BUG-110: reusable scan-thread buffer.
                    if (_ScanGridsBuffer == null) _ScanGridsBuffer = new List<IMyCubeGrid>();
                    else _ScanGridsBuffer.Clear();
                    var grids = _ScanGridsBuffer;

                    var ignoreColor = Settings.IgnoreColorPacked;
                    var grindColor = Settings.GrindColorPacked;
                    var emitterMatrix = _Welder.WorldMatrix;
                    emitterMatrix.Translation = Vector3D.Transform(Settings.CorrectedAreaOffset, emitterMatrix);
                    var areaOrientedBox = new MyOrientedBoundingBoxD(Settings.CorrectedAreaBoundingBox, emitterMatrix);

                    // Solo coordinators scan with range checks (same as legacy behavior).
                    // Multi-member coordinators skip range checks — members apply their own filtering.
                    var skipRangeCheck = cluster.Members.Count > 1;

                    // Snapshot each member's working-area so sort can score by min-distance
                    // to ANY member, preventing starvation of far-from-coordinator BaRs.
                    if (memberCount > 1)
                    {
                        if (_ClusterMemberAreaCenters == null)
                            _ClusterMemberAreaCenters = new List<Vector3D>(memberCount);
                        else
                            _ClusterMemberAreaCenters.Clear();

                        if (_ClusterMemberAreaBoxes == null)
                            _ClusterMemberAreaBoxes = new List<MyOrientedBoundingBoxD>(memberCount);
                        else
                            _ClusterMemberAreaBoxes.Clear();

                        for (int i = 0; i < cluster.Members.Count; i++)
                        {
                            var member = cluster.Members[i];
                            if (member == null || member.Welder == null) continue;
                            var memberMatrix = member.Welder.WorldMatrix;
                            memberMatrix.Translation = Vector3D.Transform(member.Settings.CorrectedAreaOffset, memberMatrix);
                            var memberBox = new MyOrientedBoundingBoxD(member.Settings.CorrectedAreaBoundingBox, memberMatrix);
                            _ClusterMemberAreaCenters.Add(memberBox.Center);
                            _ClusterMemberAreaBoxes.Add(memberBox);
                        }
                    }

                    // Scan own grid
                    AsyncAddBlocksOfGrid(ref areaOrientedBox, ((Settings.Flags & SyncBlockSettings.Settings.UseIgnoreColor) != 0), ref ignoreColor, ((Settings.Flags & SyncBlockSettings.Settings.UseGrindColor) != 0), ref grindColor, Settings.UseGrindJanitorOn, Settings.GrindJanitorOptions, _Welder.CubeGrid, grids, clusterWeldTargets, clusterGrindTargets, maxWeld, maxGrind, skipRangeCheck);

                    // BoundingBox scan if applicable
                    if (Settings.SearchMode == SearchModes.BoundingBox)
                    {
                        AsyncAddBlocksOfBox(ref areaOrientedBox, ((Settings.Flags & SyncBlockSettings.Settings.UseIgnoreColor) != 0), ref ignoreColor, ((Settings.Flags & SyncBlockSettings.Settings.UseGrindColor) != 0), ref grindColor, Settings.UseGrindJanitorOn, Settings.GrindJanitorOptions, grids, clusterWeldTargets, clusterGrindTargets, clusterFloatingTargets, maxWeld, maxGrind, maxFloat, skipRangeCheck);
                    }

                    // Source scanning (once for the cluster)
                    if (updateSource)
                    {
                        // BUG-110: reusable scan-thread buffer.
                        if (_ScanSourcesBuffer == null) _ScanSourcesBuffer = new List<IMyInventory>();
                        else _ScanSourcesBuffer.Clear();
                        var tempSources = _ScanSourcesBuffer;
                        AsyncScanForSources(tempSources);

                        Vector3D posWelder;
                        _Welder.SlimBlock.ComputeWorldCenter(out posWelder);
                        try
                        {
                            tempSources.Sort((a, b) =>
                            {
                                var blockA = a.Owner as IMyCubeBlock;
                                var blockB = b.Owner as IMyCubeBlock;
                                if (blockA != null && blockB != null)
                                {
                                    var welderA = blockA as IMyShipWelder;
                                    var welderB = blockB as IMyShipWelder;
                                    if ((welderA == null) == (welderB == null))
                                    {
                                        Vector3D posA;
                                        Vector3D posB;
                                        blockA.SlimBlock.ComputeWorldCenter(out posA);
                                        blockB.SlimBlock.ComputeWorldCenter(out posB);
                                        var distanceA = (int)Math.Abs((posWelder - posA).Length());
                                        var distanceB = (int)Math.Abs((posWelder - posB).Length());
                                        return distanceA - distanceB;
                                    }
                                    else if (welderA == null) return -1;
                                    else return 1;
                                }
                                else if (blockA != null) return -1;
                                else if (blockB != null) return 1;
                                else return 0;
                            });
                        }
                        catch (Exception ex)
                        {
                            Logging.Instance.Error("Error on .Sort for cluster sources. Exception: {0}", ex);
                        }

                        foreach (var inventory in tempSources)
                        {
                            if (inventory.Owner is IMyCargoContainer || inventory.Owner is IMyRefinery)
                            {
                                result.PushTargets.Add(inventory);
                            }
                        }

                        tempSources.RemoveAll(inv => inv.Owner is IMyRefinery);
                        result.Sources.AddRange(tempSources);
                        result.SourcesUpdated = true;
                    }

                    result.Timestamp = MyAPIGateway.Session.ElapsedPlayTime;

                    // Pre-sort candidates for multi-member clusters.
                    // Members reuse this order and skip their local sort.
                    if (memberCount > 1)
                    {
                        PreSortClusterCandidates(areaOrientedBox.Center, clusterGrindTargets, clusterWeldTargets);
                        result.PreSorted = true;
                    }
                }
                catch (Exception ex)
                {
                    Logging.Instance.Error("BuildAndRepairSystemBlock {0}: AsyncClusterScan exception: {1}", Logging.BlockName(_Welder, Logging.BlockNameOptions.None), ex);
                }

                // Clean expired entries from empty grid cache
                CleanupEmptyGridCache();

                // Publish shared result (atomic reference swap)
                cluster.SetResult(result);

                // Coordinator is also a member — apply own range/distance filtering
                ApplyClusterResultToSelf(result, updateSource);

                MissedResultCycles = 0;
                _InitialScanCompleted = true;

                // FEAT-071: track empty scans using the coordinator's OWN filtered counts
                // (raw cluster candidates may include blocks no member can reach).
                var hasTargets = State.PossibleWeldTargets.CurrentCount > 0
                    || State.PossibleGrindTargets.CurrentCount > 0
                    || State.PossibleFloatingTargets.CurrentCount > 0;
                if (hasTargets)
                    _consecutiveEmptyScans = 0;
                else
                    _consecutiveEmptyScans++;

                // FEAT-075: record per-type counts so IsTargetListSaturated can tell
                // "type depleted" from "type never existed".
                _lastScanWeldCandidateCount = result.WeldCandidates.Count;
                _lastScanGrindCandidateCount = result.GrindCandidates.Count;

            }
            finally
            {
                // Release cluster member snapshot so stale member references don't leak
                // across scan cycles. Next multi-member scan will repopulate.
                if (_ClusterMemberAreaCenters != null)
                    _ClusterMemberAreaCenters.Clear();
                if (_ClusterMemberAreaBoxes != null)
                    _ClusterMemberAreaBoxes.Clear();

                _LastTargetsUpdate = MyAPIGateway.Session.ElapsedPlayTime;
                _lastFullScanTime = _LastTargetsUpdate;
                if (updateSource) _LastSourceUpdate = _LastTargetsUpdate;
                lock (_Welder)
                {
                    _AsyncUpdateSourcesAndTargetsRunning = false;
                }
                if (profilerTs != 0L)
                {
                    MethodProfiler.StopAndLog("AsyncClusterScan", profilerTs, () =>
                        string.Format("entityId={0};updateSource={1};clusterMembers={2}",
                            _Welder.EntityId, updateSource, cluster.Members.Count));
                }
            }
        }

        /// <summary>
        /// Pre-sorts cluster candidates using the coordinator's settings and position.
        /// Members reuse this sort order, avoiding redundant per-member sorts.
        /// For co-located BaRs the coordinator's distance order is a near-perfect
        /// approximation, so members can skip their expensive local sort.
        /// </summary>
        private void PreSortClusterCandidates(Vector3D coordCenter, List<ClusterTargetCandidate> grindCandidates, List<ClusterTargetCandidate> weldCandidates)
        {
            var profilerTs = MethodProfiler.Start();
            try
            {
                // BUG-058/110: reuse one instance-field distance dict across scans.
                var maxCap = Math.Max(
                    grindCandidates != null ? grindCandidates.Count : 0,
                    weldCandidates != null ? weldCandidates.Count : 0);
                Dictionary<IMySlimBlock, double> distances;
                if (maxCap > 0)
                {
                    if (_ScanPreSortDistances == null) _ScanPreSortDistances = new Dictionary<IMySlimBlock, double>(maxCap);
                    else _ScanPreSortDistances.Clear();
                    distances = _ScanPreSortDistances;
                }
                else
                {
                    distances = null;
                }

                if (grindCandidates != null && grindCandidates.Count > 1)
                {
                    var grindUsePriority = (Settings.Flags & SyncBlockSettings.Settings.GrindIgnorePriorityOrder) == 0;
                    var grindSmallestGridFirst = (Settings.Flags & SyncBlockSettings.Settings.GrindSmallestGridFirst) != 0;
                    var grindNearFirst = (Settings.Flags & SyncBlockSettings.Settings.GrindNearFirst) != 0;

                    distances.Clear();
                    foreach (var c in grindCandidates)
                    {
                        var blockPos = c.Block.CubeGrid.GridIntegerToWorld(c.Block.Position);
                        distances[c.Block] = MinSquaredDistanceToClusterMembers(ref blockPos, ref coordCenter);
                    }

                    // BUG-091: per-grid min-distance lookup so GrindSmallestGridFirst breaks
                    // ties by nearest-block proximity instead of arbitrary EntityId.
                    if (grindSmallestGridFirst)
                    {
                        _gridMinDistLookup.Clear();
                        foreach (var c in grindCandidates)
                        {
                            double bd;
                            distances.TryGetValue(c.Block, out bd);
                            var gid = c.Block.CubeGrid.EntityId;
                            double existing;
                            if (_gridMinDistLookup.TryGetValue(gid, out existing))
                            {
                                if (bd < existing) _gridMinDistLookup[gid] = bd;
                            }
                            else
                            {
                                _gridMinDistLookup[gid] = bd;
                            }
                        }
                    }

                    grindCandidates.Sort((a, b) =>
                    {
                        var cmp = CompareGrindNonDistance(a.Block, a.Attributes, b.Block, b.Attributes,
                            grindUsePriority, grindSmallestGridFirst,
                            grindSmallestGridFirst ? _gridMinDistLookup : null);
                        if (cmp != 0) return cmp;

                        // Distance compare (squared, min-to-any-member via pre-built cache).
                        double distA, distB;
                        distances.TryGetValue(a.Block, out distA);
                        distances.TryGetValue(b.Block, out distB);
                        return grindNearFirst ? distA.CompareTo(distB) : distB.CompareTo(distA);
                    });
                }

                if (weldCandidates != null && weldCandidates.Count > 1)
                {
                    distances.Clear();
                    foreach (var c in weldCandidates)
                    {
                        var blockPos = c.Block.CubeGrid.GridIntegerToWorld(c.Block.Position);
                        distances[c.Block] = MinSquaredDistanceToClusterMembers(ref blockPos, ref coordCenter);
                    }

                    weldCandidates.Sort((a, b) =>
                    {
                        var cmp = CompareWeldPriority(a.Block, b.Block);
                        if (cmp != 0) return cmp;

                        double distA, distB;
                        distances.TryGetValue(a.Block, out distA);
                        distances.TryGetValue(b.Block, out distB);
                        var distCmp = distA.CompareTo(distB);
                        if (distCmp != 0) return distCmp;

                        return CompareBlockStableTiebreak(a.Block, b.Block);
                    });
                }
            }
            catch (Exception ex)
            {
                Logging.Instance.Error("PreSortClusterCandidates error: {0}", ex);
            }
            finally
            {
                var _grindCount = grindCandidates != null ? grindCandidates.Count : 0;
                var _weldCount = weldCandidates != null ? weldCandidates.Count : 0;
                var _clusterMembers = _ClusterMemberAreaCenters != null ? _ClusterMemberAreaCenters.Count : 0;
                if (profilerTs != 0L)
                {
                    MethodProfiler.StopAndLog("PreSortClusterCandidates", profilerTs, () =>
                        string.Format("entityId={0};grindCandidates={1};weldCandidates={2};clusterMembers={3}",
                            _Welder.EntityId, _grindCount, _weldCount, _clusterMembers));
                }
            }
        }

        /// <summary>
        /// Starts the async cluster result application for a non-coordinator member.
        /// </summary>
        private void StartAsyncApplyClusterResults(ScanCluster cluster, bool updateSource)
        {
            if (!_Welder.UseConveyorSystem)
            {
                lock (_PossibleSources) { _PossibleSources.Clear(); }
                lock (_PossiblePushTargets) { _PossiblePushTargets.Clear(); }
            }

            if (!_Welder.Enabled || !_Welder.IsFunctional || State.Ready == false)
            {
                // BUG-095: defer cleanup while a prior scan is still running.
                lock (_Welder)
                {
                    if (_AsyncUpdateSourcesAndTargetsRunning) return;
                }
                lock (State.PossibleWeldTargets) { State.PossibleWeldTargets.Clear(); State.PossibleWeldTargets.RebuildHash(); }
                lock (State.PossibleGrindTargets) { State.PossibleGrindTargets.Clear(); State.PossibleGrindTargets.RebuildHash(); }
                lock (State.PossibleFloatingTargets) { State.PossibleFloatingTargets.Clear(); State.PossibleFloatingTargets.RebuildHash(); }
                _InitialScanCompleted = false;
                _LastTargetsUpdate = MyAPIGateway.Session.ElapsedPlayTime;
                _LastSourceUpdate = _LastTargetsUpdate;
                return;
            }

            lock (_Welder)
            {
                if (_AsyncUpdateSourcesAndTargetsRunning) return;
                _AsyncUpdateSourcesAndTargetsRunning = true;
                Mod.AddAsyncAction(() => AsyncApplyClusterResults(cluster, updateSource));
            }
        }

        /// <summary>
        /// Member (non-coordinator) applies shared cluster results with own range/distance filtering.
        /// Much cheaper than a full scan — just range checks + distance sort on pre-filtered candidates.
        /// Falls back to emergency coordinator scan after 3 consecutive missed results.
        /// </summary>
        private void AsyncApplyClusterResults(ScanCluster cluster, bool updateSource)
        {
            var profilerTs = MethodProfiler.Start();
            try
            {
                if (!State.Ready) return;

                var result = cluster.GetResult();
                if (result == null)
                {
                    MissedResultCycles++;
                    if (MissedResultCycles > 3)
                    {
                        // Fallback: act as emergency coordinator — scan and publish results
                        AsyncClusterScan(cluster, updateSource);
                    }
                    return;
                }

                MissedResultCycles = 0;
                ApplyClusterResultToSelf(result, updateSource);
                _InitialScanCompleted = true;

            }
            finally
            {
                _LastTargetsUpdate = MyAPIGateway.Session.ElapsedPlayTime;
                if (updateSource) _LastSourceUpdate = _LastTargetsUpdate;
                lock (_Welder)
                {
                    _AsyncUpdateSourcesAndTargetsRunning = false;
                }
                if (profilerTs != 0L)
                {
                    MethodProfiler.StopAndLog("AsyncApplyClusterResults", profilerTs, () =>
                        string.Format("entityId={0};updateSource={1};missed={2}",
                            _Welder.EntityId, updateSource, MissedResultCycles));
                }
            }
        }

        /// <summary>
        /// Applies a ScanClusterResult to this BaR by filtering candidates through own IsInRange,
        /// computing distances, sorting, and swapping into State. Used by both coordinator and members.
        /// </summary>
        /// <summary>
        /// Truncates a sorted target list to maxCount while preserving representation
        /// from multiple grids. Each grid gets a guaranteed minimum number of slots
        /// so that one dominant grid cannot crowd out all others after sorting.
        /// </summary>
        private void TruncateGridAware(List<TargetBlockData> list, int maxCount)
        {
            if (list.Count <= maxCount) return;

            // Count distinct grids (reuse pooled HashSet)
            _truncateGridIds.Clear();
            for (int i = 0; i < list.Count; i++)
            {
                _truncateGridIds.Add(list[i].Block.CubeGrid.EntityId);
            }

            // Single grid — simple truncation, no fairness needed
            if (_truncateGridIds.Count <= 1)
            {
                list.RemoveRange(maxCount, list.Count - maxCount);
                return;
            }

            int numGrids = _truncateGridIds.Count;
            int minPerGrid = Math.Max(maxCount / numGrids, 4);

            // Track how many blocks we've kept per grid (reuse pooled Dictionary)
            _truncateKeptPerGrid.Clear();
            foreach (var id in _truncateGridIds)
            {
                _truncateKeptPerGrid[id] = 0;
            }

            // First pass: walk sorted list, keep items until each grid hits minPerGrid
            // or we fill up. Items beyond minPerGrid go to overflow for second pass.
            _truncateKept.Clear();
            _truncateOverflow.Clear();

            for (int i = 0; i < list.Count; i++)
            {
                var item = list[i];
                var gid = item.Block.CubeGrid.EntityId;
                int count;
                _truncateKeptPerGrid.TryGetValue(gid, out count);

                if (count < minPerGrid)
                {
                    _truncateKept.Add(item);
                    _truncateKeptPerGrid[gid] = count + 1;
                    if (_truncateKept.Count >= maxCount) break;
                }
                else
                {
                    _truncateOverflow.Add(item);
                }
            }

            // Second pass: fill remaining slots from overflow (already in sort order)
            if (_truncateKept.Count < maxCount)
            {
                int remaining = maxCount - _truncateKept.Count;
                for (int i = 0; i < _truncateOverflow.Count && remaining > 0; i++)
                {
                    _truncateKept.Add(_truncateOverflow[i]);
                    remaining--;
                }
            }

            list.Clear();
            list.AddRange(_truncateKept);
        }


        private void ApplyClusterResultToSelf(ScanClusterResult result, bool updateSource)
        {
            var profilerTs = MethodProfiler.Start();
            var preTruncateWeld = 0;
            var preTruncateGrind = 0;
            try
            {
                var emitterMatrix = _Welder.WorldMatrix;
                emitterMatrix.Translation = Vector3D.Transform(Settings.CorrectedAreaOffset, emitterMatrix);
                var areaOrientedBox = new MyOrientedBoundingBoxD(Settings.CorrectedAreaBoundingBox, emitterMatrix);

                _TempPossibleWeldTargets.Clear();
                _TempPossibleGrindTargets.Clear();
                _TempPossibleFloatingTargets.Clear();

                // Filter weld candidates by own range (no pre-sort cap; truncated after sort)
                if (result.WeldCandidates != null)
                {
                    for (int i = 0; i < result.WeldCandidates.Count; i++)
                    {
                        var candidate = result.WeldCandidates[i];
                        double distance;
                        if (candidate.Block.IsInRange(ref areaOrientedBox, out distance))
                        {
                            _TempPossibleWeldTargets.Add(new TargetBlockData(candidate.Block, distance, candidate.Attributes));
                        }
                    }
                }

                // Filter grind candidates by own range (no pre-sort cap; truncated after sort)
                if (result.GrindCandidates != null)
                {
                    for (int i = 0; i < result.GrindCandidates.Count; i++)
                    {
                        var candidate = result.GrindCandidates[i];
                        double distance;
                        if (candidate.Block.IsInRange(ref areaOrientedBox, out distance))
                        {
                            _TempPossibleGrindTargets.Add(new TargetBlockData(candidate.Block, distance, candidate.Attributes));
                        }
                    }
                }

                // Filter floating candidates by own working area (OBB containment check)
                if (result.FloatingCandidates != null)
                {
                    var invRotation = MatrixD.Transpose(MatrixD.CreateFromQuaternion(areaOrientedBox.Orientation));
                    var he = areaOrientedBox.HalfExtent;
                    for (int i = 0; i < result.FloatingCandidates.Count; i++)
                    {
                        if (_TempPossibleFloatingTargets.Count >= MaxPossibleFloatingTargets) break;
                        var candidate = result.FloatingCandidates[i];
                        var localPos = Vector3D.TransformNormal(candidate.WorldPosition - areaOrientedBox.Center, invRotation);
                        if (Math.Abs(localPos.X) > he.X || Math.Abs(localPos.Y) > he.Y || Math.Abs(localPos.Z) > he.Z)
                            continue;
                        var distance = (areaOrientedBox.Center - candidate.WorldPosition).Length();
                        _TempPossibleFloatingTargets.Add(new TargetEntityData(candidate.Entity, distance));
                    }
                }

                // Pre-sort weld targets (skip if coordinator pre-sorted — truncation
                // only needs approximate order to select good candidates per grid).
                if (!result.PreSorted)
                {
                    try
                    {
                        _TempPossibleWeldTargets.Sort((a, b) =>
                        {
                            var cmp = CompareWeldPriority(a.Block, b.Block);
                            if (cmp != 0) return cmp;

                            var distCmp = UtilsMath.CompareDistance(a.Distance, b.Distance);
                            if (distCmp != 0) return distCmp;

                            return CompareBlockStableTiebreak(a.Block, b.Block);
                        });
                    }
                    catch (Exception ex)
                    {
                        Logging.Instance.Error("Error on .Sort for cluster _TempPossibleWeldTargets. Exception: {0}", ex);
                    }
                }

                // Truncate after sorting, preserving blocks from multiple grids.
                preTruncateWeld = _TempPossibleWeldTargets.Count;
                TruncateGridAware(_TempPossibleWeldTargets, MaxPossibleWeldTargets);

                // BUG-086: re-sort after truncation; TruncateGridAware disrupts order via overflow list.
                if (preTruncateWeld > MaxPossibleWeldTargets || result.PreSorted)
                {
                    try
                    {
                        _TempPossibleWeldTargets.Sort((a, b) =>
                        {
                            var cmp = CompareWeldPriority(a.Block, b.Block);
                            if (cmp != 0) return cmp;

                            var distCmp = UtilsMath.CompareDistance(a.Distance, b.Distance);
                            if (distCmp != 0) return distCmp;

                            return CompareBlockStableTiebreak(a.Block, b.Block);
                        });
                    }
                    catch (Exception ex)
                    {
                        Logging.Instance.Error("Error on post-truncate .Sort for _TempPossibleWeldTargets. Exception: {0}", ex);
                    }
                }

                // Pre-sort grind targets (skip if coordinator pre-sorted).
                if (!result.PreSorted)
                {
                    try
                    {
                        var grindUsePriority = (Settings.Flags & SyncBlockSettings.Settings.GrindIgnorePriorityOrder) == 0;
                        var grindSmallestGridFirst = (Settings.Flags & SyncBlockSettings.Settings.GrindSmallestGridFirst) != 0;
                        var grindNearFirst = (Settings.Flags & SyncBlockSettings.Settings.GrindNearFirst) != 0;

                        // BUG-091: per-grid min-distance lookup for GrindSmallestGridFirst tiebreak.
                        if (grindSmallestGridFirst)
                        {
                            _gridMinDistLookup.Clear();
                            for (int i = 0; i < _TempPossibleGrindTargets.Count; i++)
                            {
                                var c = _TempPossibleGrindTargets[i];
                                var gid = c.Block.CubeGrid.EntityId;
                                double existing;
                                if (_gridMinDistLookup.TryGetValue(gid, out existing))
                                {
                                    if (c.Distance < existing) _gridMinDistLookup[gid] = c.Distance;
                                }
                                else
                                {
                                    _gridMinDistLookup[gid] = c.Distance;
                                }
                            }
                        }

                        _TempPossibleGrindTargets.Sort((a, b) =>
                        {
                            var cmp = CompareGrindNonDistance(a.Block, a.Attributes, b.Block, b.Attributes,
                                grindUsePriority, grindSmallestGridFirst,
                                grindSmallestGridFirst ? _gridMinDistLookup : null);
                            if (cmp != 0) return cmp;

                            // FEAT-070: honor grindNearFirst (was always nearest-first prior).
                            return grindNearFirst
                                ? UtilsMath.CompareDistance(a.Distance, b.Distance)
                                : UtilsMath.CompareDistance(b.Distance, a.Distance);
                        });
                    }
                    catch (Exception ex)
                    {
                        Logging.Instance.Error("Error on .Sort for cluster _TempPossibleGrindTargets. Exception: {0}", ex);
                    }
                }

                // Truncate after sorting, preserving blocks from multiple grids.
                preTruncateGrind = _TempPossibleGrindTargets.Count;
                TruncateGridAware(_TempPossibleGrindTargets, MaxPossibleGrindTargets);

                // BUG-086: Re-sort after truncation (see weld comment above).
                if (preTruncateGrind > MaxPossibleGrindTargets || result.PreSorted)
                {
                    try
                    {
                        var grindUsePriority = (Settings.Flags & SyncBlockSettings.Settings.GrindIgnorePriorityOrder) == 0;
                        var grindSmallestGridFirst = (Settings.Flags & SyncBlockSettings.Settings.GrindSmallestGridFirst) != 0;
                        var grindNearFirst = (Settings.Flags & SyncBlockSettings.Settings.GrindNearFirst) != 0;

                        // BUG-091: rebuild per-grid min-distance after truncation.
                        if (grindSmallestGridFirst)
                        {
                            _gridMinDistLookup.Clear();
                            for (int i = 0; i < _TempPossibleGrindTargets.Count; i++)
                            {
                                var c = _TempPossibleGrindTargets[i];
                                var gid = c.Block.CubeGrid.EntityId;
                                double existing;
                                if (_gridMinDistLookup.TryGetValue(gid, out existing))
                                {
                                    if (c.Distance < existing) _gridMinDistLookup[gid] = c.Distance;
                                }
                                else
                                {
                                    _gridMinDistLookup[gid] = c.Distance;
                                }
                            }
                        }

                        _TempPossibleGrindTargets.Sort((a, b) =>
                        {
                            var cmp = CompareGrindNonDistance(a.Block, a.Attributes, b.Block, b.Attributes,
                                grindUsePriority, grindSmallestGridFirst,
                                grindSmallestGridFirst ? _gridMinDistLookup : null);
                            if (cmp != 0) return cmp;

                            return grindNearFirst
                                ? UtilsMath.CompareDistance(a.Distance, b.Distance)
                                : UtilsMath.CompareDistance(b.Distance, a.Distance);
                        });
                    }
                    catch (Exception ex)
                    {
                        Logging.Instance.Error("Error on post-truncate .Sort for _TempPossibleGrindTargets. Exception: {0}", ex);
                    }
                }

                // Locality refinement: within distance bands, prefer blocks near the last
                // grind position. This keeps the primary nearest/farthest ordering intact
                // while adding spatial locality as a tiebreaker so the BaR grinds nearby
                // blocks in sequence instead of jumping across the grid.
                if (_HasLastGrindPosition && _TempPossibleGrindTargets.Count > 1)
                {
                    var lastPos = _LastGrindWorldPosition;
                    var bandSize = 2.0; // meters — blocks within this BaR-distance range are re-sorted by locality
                    int bandStart = 0;
                    while (bandStart < _TempPossibleGrindTargets.Count)
                    {
                        var baseDist = _TempPossibleGrindTargets[bandStart].Distance;
                        int bandEnd = bandStart + 1;
                        while (bandEnd < _TempPossibleGrindTargets.Count &&
                               Math.Abs(_TempPossibleGrindTargets[bandEnd].Distance - baseDist) < bandSize)
                        {
                            bandEnd++;
                        }
                        if (bandEnd - bandStart > 1)
                        {
                            var localLastPos = lastPos;
                            _TempPossibleGrindTargets.Sort(bandStart, bandEnd - bandStart,
                                Comparer<TargetBlockData>.Create((a, b) =>
                                {
                                    // Preserve autogrind-first ordering within the band.
                                    var autoA = (a.Attributes & TargetBlockData.AttributeFlags.Autogrind) != 0;
                                    var autoB = (b.Attributes & TargetBlockData.AttributeFlags.Autogrind) != 0;
                                    if (autoA != autoB) return autoA ? -1 : 1;

                                    var posA = a.Block.CubeGrid.GridIntegerToWorld(a.Block.Position);
                                    var posB = b.Block.CubeGrid.GridIntegerToWorld(b.Block.Position);
                                    var dA = (localLastPos - posA).LengthSquared();
                                    var dB = (localLastPos - posB).LengthSquared();
                                    return dA.CompareTo(dB);
                                }));
                        }
                        bandStart = bandEnd;
                    }
                }

                // Sort floating targets
                try
                {
                    _TempPossibleFloatingTargets.Sort((a, b) =>
                    {
                        var itemA = a.Entity;
                        var itemB = b.Entity;
                        var itemAFloating = itemA as MyFloatingObject;
                        var itemBFloating = itemB as MyFloatingObject;
                        if (itemAFloating != null && itemBFloating != null)
                        {
                            var priorityA = ComponentCollectPriority.GetPriority(itemAFloating.Item.Content.GetObjectId());
                            var priorityB = ComponentCollectPriority.GetPriority(itemBFloating.Item.Content.GetObjectId());
                            if (priorityA == priorityB)
                            {
                                return UtilsMath.CompareDistance(a.Distance, b.Distance);
                            }
                            else return priorityA - priorityB;
                        }
                        else if (itemAFloating == null) return -1;
                        else if (itemBFloating == null) return 1;
                        return UtilsMath.CompareDistance(a.Distance, b.Distance);
                    });
                }
                catch (Exception ex)
                {
                    Logging.Instance.Error("Error on .Sort for cluster _TempPossibleFloatingTargets. Exception: {0}", ex);
                }

                // Swap into State (same pattern as existing code)
                lock (State.PossibleWeldTargets)
                {
                    HashSet<IMySlimBlock> ignoredBlocks = null;
                    foreach (var old in State.PossibleWeldTargets)
                    {
                        if (old.Ignore)
                        {
                            if (ignoredBlocks == null)
                                ignoredBlocks = new HashSet<IMySlimBlock>();
                            ignoredBlocks.Add(old.Block);
                        }
                    }

                    State.PossibleWeldTargets.Clear();
                    State.PossibleWeldTargets.AddRange(_TempPossibleWeldTargets);

                    if (ignoredBlocks != null)
                    {
                        foreach (var target in State.PossibleWeldTargets)
                        {
                            if (ignoredBlocks.Contains(target.Block) && target.Block.IsFullIntegrity)
                                target.Ignore = true;
                        }
                    }

                    State.PossibleWeldTargets.RebuildHash();
                }
                _TempPossibleWeldTargets.Clear();

                lock (State.PossibleGrindTargets)
                {
                    State.PossibleGrindTargets.Clear();
                    State.PossibleGrindTargets.AddRange(_TempPossibleGrindTargets);
                    State.PossibleGrindTargets.RebuildHash();
                }
                _TempPossibleGrindTargets.Clear();

                lock (State.PossibleFloatingTargets)
                {
                    State.PossibleFloatingTargets.Clear();
                    State.PossibleFloatingTargets.AddRange(_TempPossibleFloatingTargets);
                    State.PossibleFloatingTargets.RebuildHash();
                }
                _TempPossibleFloatingTargets.Clear();

                // Apply sources whenever the cluster result contains them, regardless of this
                // BaR's own source timer. The coordinator's timer decides when to scan; members
                // should always consume available source data. Otherwise timer drift between
                // coordinator and members can cause members to miss source updates for minutes.
                if (result.SourcesUpdated)
                {
                    _TempPossibleSources.Clear();
                    _TempPossibleSources.AddRange(result.Sources);
                    _TempPossiblePushTargets.Clear();
                    _TempPossiblePushTargets.AddRange(result.PushTargets);

                    lock (_PossibleSources)
                    {
                        _PossibleSources.Clear();
                        _PossibleSources.AddRange(_TempPossibleSources);
                    }
                    lock (_PossiblePushTargets)
                    {
                        _PossiblePushTargets.Clear();
                        _PossiblePushTargets.AddRange(_TempPossiblePushTargets);
                    }
                    _TempPossibleSources.Clear();
                    _TempPossiblePushTargets.Clear();

                    // Only reset _PushTargetsFull when push targets actually changed
                    // (container added/removed) or after a 60s safety backoff.
                    // This prevents push retry storms where all BaRs simultaneously
                    // attempt expensive pushes into the same full containers on every
                    // 30s source rescan.
                    if (_PushTargetsFull)
                    {
                        var pushTargetsChanged = ComputePushTargetsSignature() != _PushTargetsFullSignature;
                        var backoffExpired = MyAPIGateway.Session.ElapsedPlayTime.Subtract(_PushTargetsFullSince).TotalSeconds >= 60;
                        if (pushTargetsChanged || backoffExpired)
                        {
                            _PushTargetsFull = false;
                        }
                    }
                }
            }
            finally
            {
                if (profilerTs != 0L)
                {
                    // Count unique grids for the profiler line. Iterate under the same locks
                    // as the swap above: StartAsyncClusterScan's early-exit path (BaR disabled /
                    // unpowered mid-scan) Clears these lists from the main thread, and this
                    // finally runs on the background thread without holding the swap lock.
                    _truncateGridIds.Clear();
                    lock (State.PossibleWeldTargets)
                    {
                        foreach (var t in State.PossibleWeldTargets) _truncateGridIds.Add(t.Block.CubeGrid.EntityId);
                    }
                    var weldGridCount = _truncateGridIds.Count;

                    _truncateGridIds.Clear();
                    lock (State.PossibleGrindTargets)
                    {
                        foreach (var t in State.PossibleGrindTargets) _truncateGridIds.Add(t.Block.CubeGrid.EntityId);
                    }
                    var grindGridCount = _truncateGridIds.Count;

                    MethodProfiler.StopAndLog("ApplyClusterResultToSelf", profilerTs, () =>
                        string.Format("entityId={0};weldTargets={1}(pre={2},grids={3});grindTargets={4}(pre={5},grids={6});floatingTargets={7}",
                            _Welder.EntityId,
                            State.PossibleWeldTargets.CurrentCount, preTruncateWeld, weldGridCount,
                            State.PossibleGrindTargets.CurrentCount, preTruncateGrind, grindGridCount,
                            State.PossibleFloatingTargets.CurrentCount));
                }
            }
        }
    }
}
