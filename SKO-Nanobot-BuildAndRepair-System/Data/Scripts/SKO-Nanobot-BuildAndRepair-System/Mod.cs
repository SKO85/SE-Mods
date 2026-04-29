namespace SKONanobotBuildAndRepairSystem
{
    using DefenseShields;
    using Sandbox.Game.EntityComponents;
    using Sandbox.ModAPI;
    using SKONanobotBuildAndRepairSystem.Cluster;
    using SKONanobotBuildAndRepairSystem.Chat;
    using SKONanobotBuildAndRepairSystem.Handlers;
    using SKONanobotBuildAndRepairSystem.Helpers;
    using SKONanobotBuildAndRepairSystem.Caches;
    using SKONanobotBuildAndRepairSystem.Models;
    using SKONanobotBuildAndRepairSystem.Profiling;
    using SKONanobotBuildAndRepairSystem.Utils;
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Text;
    using VRage.Game;
    using VRage.Game.Components;
    using VRage.Game.ModAPI;

    [MySessionComponentDescriptor(MyUpdateOrder.BeforeSimulation)]
    public class Mod : MySessionComponentBase
    {
        public static Guid ModGuid = new Guid("8B57046C-DA20-4DE1-8E35-513FD21E3B9F");
        public static bool DisableLocalization = false;
        public static bool SettingsValid = false;
        public static bool CustomSettingsLoaded = false;
        public static SyncModSettings Settings = new SyncModSettings();
        public static readonly ConcurrentDictionary<long, NanobotSystem> NanobotSystems = new ConcurrentDictionary<long, NanobotSystem>();
        public static ShieldApi Shield; // Centralized DefenseShields API instance

        /// <summary>
        /// Live counter: how many BaR systems are actively targeting each grid.
        /// Incremented/decremented in SyncBlockState.CurrentWeldingBlock/CurrentGrindingBlock setters.
        /// </summary>
        public static ConcurrentDictionary<long, int> GridSystemCount = new ConcurrentDictionary<long, int>();

        public static void IncrementGridCount(long gridId)
        {
            GridSystemCount.AddOrUpdate(gridId, 1, (k, v) => v + 1);
        }

        public static void DecrementGridCount(long gridId)
        {
            // CAS loop: retry if the value changed between read and update.
            int current;
            while (GridSystemCount.TryGetValue(gridId, out current))
            {
                var newVal = current - 1;
                if (newVal <= 0)
                {
                    // Try to remove; if it fails (value changed), loop retries.
                    int removed;
                    if (GridSystemCount.TryRemove(gridId, out removed))
                        break;
                }
                else
                {
                    if (GridSystemCount.TryUpdate(gridId, newVal, current))
                        break;
                }
                // TryUpdate/TryRemove failed — value was modified concurrently; retry.
            }
        }

        // --- OPT 1: Mechanical block grind throttle ---
        // Caps mechanical connection block destructions to 1 per tick globally,
        // preventing 100-380ms spikes when multiple BaRs destroy pistons/rotors/hinges simultaneously.
        private static int _mechanicalGrindsThisTick;
        private static int _lastMechanicalTick = -1;

        public static bool TryClaimMechanicalGrindSlot()
        {
            var tick = MyAPIGateway.Session.GameplayFrameCounter;
            if (tick != _lastMechanicalTick)
            {
                _lastMechanicalTick = tick;
                _mechanicalGrindsThisTick = 0;
            }
            if (_mechanicalGrindsThisTick >= 1) return false;
            _mechanicalGrindsThisTick++;
            return true;
        }

        // --- BUG-106: Global dismount budget ---
        // Caps the number of full-dismount grinds per tick globally. Profile (20260428202503)
        // showed `decreaseMs` spikes of 5-12ms on full dismount of armor blocks (SE engine
        // cascade for grid integrity recalc, conveyor refresh, block events). Spreading
        // dismounts across ticks prevents these spikes from compounding when many BaRs
        // dismount simultaneously. Mechanical-block cap (1/tick) is a stricter sub-budget
        // applied first; this cap (3/tick) covers all dismounts that reach the raze path.
        public const int MaxDismountsPerTickDefault = 3;
        private static int _dismountsThisTick;
        private static int _lastDismountTick = -1;

        public static bool TryClaimDismountSlot()
        {
            var tick = MyAPIGateway.Session.GameplayFrameCounter;
            if (tick != _lastDismountTick)
            {
                _lastDismountTick = tick;
                _dismountsThisTick = 0;
            }
            if (_dismountsThisTick >= MaxDismountsPerTickDefault) return false;
            _dismountsThisTick++;
            return true;
        }

        // --- BUG-107: Global proj.Build budget ---
        // Caps `proj.Build()` calls per tick. Profile (20260428202503) showed `buildMs`
        // spikes of 7-9ms on projected armor/conveyor blocks (SE engine materialization +
        // grid topology update). When many BaRs simultaneously materialize projected blocks
        // these compound into multi-tick stalls. Cap=3/tick spreads the load.
        public const int MaxProjBuildsPerTickDefault = 3;
        private static int _projBuildsThisTick;
        private static int _lastProjBuildTick = -1;

        public static bool TryClaimProjBuildSlot()
        {
            var tick = MyAPIGateway.Session.GameplayFrameCounter;
            if (tick != _lastProjBuildTick)
            {
                _lastProjBuildTick = tick;
                _projBuildsThisTick = 0;
            }
            if (_projBuildsThisTick >= MaxProjBuildsPerTickDefault) return false;
            _projBuildsThisTick++;
            return true;
        }

        // --- BUG-127: Deferred raze queue ---
        // Decouples "block hit 0 integrity" (cheap, ~µs to enqueue) from "engine fully
        // removes block" (5-8 ms on conveyor-connected blocks like batteries/containers).
        // Without the queue, BUG-106's 3-per-tick dismount cap still let 3 razes pile on the
        // same tick = 24 ms of engine cleanup over the 16.7 ms 60-Hz frame budget. With the
        // queue, razes are processed at most MaxRazesPerTickDefault per tick — independent of
        // which tick the underlying grind happened on, so the engine work is spread across
        // multiple ticks. Block reference safety: Mod.UpdateBeforeSimulation runs on main
        // thread, same as grid mutations — no cross-thread access to engine state.
        public const int MaxRazesPerTickDefault = 1;
        private static readonly Queue<VRage.Game.ModAPI.IMySlimBlock> _razeQueue = new Queue<VRage.Game.ModAPI.IMySlimBlock>();

        public static void EnqueueRaze(VRage.Game.ModAPI.IMySlimBlock block)
        {
            if (block == null) return;
            lock (_razeQueue) { _razeQueue.Enqueue(block); }
        }

        public static int GetRazeQueueDepth()
        {
            lock (_razeQueue) { return _razeQueue.Count; }
        }

        private void ProcessRazeQueue()
        {
            var processed = 0;
            while (processed < MaxRazesPerTickDefault)
            {
                VRage.Game.ModAPI.IMySlimBlock target;
                lock (_razeQueue)
                {
                    if (_razeQueue.Count == 0) break;
                    target = _razeQueue.Dequeue();
                }
                if (target == null) continue;
                try
                {
                    var grid = target.CubeGrid;
                    if (grid == null) continue;
                    // Block could have been welded back up or razed by another path before we
                    // drained it; in either case skip and keep budget for the next entry.
                    if (target.FatBlock != null && target.FatBlock.Closed) continue;
                    // Mirrors the call sequence the grind site previously did inline. Both
                    // operations are deferred together so the SE-engine cleanup work is paced
                    // by MaxRazesPerTickDefault rather than co-occurring with the grind tick.
                    target.SetToConstructionSite();
                    grid.RemoveBlock(target, true);
                    processed++;
                }
                catch (Exception ex)
                {
                    Logging.Instance.Error(ex);
                }
            }
        }

        // --- OPT 2: BaR update staggering ---
        // Distributes ServerTryWeldingGrindingCollecting() calls across StaggerGroupCount groups
        // so only ~N/StaggerGroupCount BaRs fire per tick instead of all N.
        // Settings.StaggerGroupCount: 0 = auto (based on BaR count), >0 = explicit override.
        public const int StaggerGroupCountDefault = 3;
        private static int _nextStaggerSlot;

        public static int GetEffectiveStaggerGroupCount()
        {
            var configured = Settings.StaggerGroupCount;
            if (configured > 0) return configured;
            // Auto: scale with placed BaR count. Even disabled BaRs run the per-tick orchestration
            // (CleanupFriendlyDamage, Settings.TrySave, TryTransmitState), so they generate per-tick
            // CPU load and need to be staggered. Counting only IsWorking BaRs collapsed the stagger
            // to 1 in worlds with many disabled BaRs, making BUG-102's isolated-BaR fix inert there.
            var total = NanobotSystems.Count;
            if (total <= 5) return 1;
            if (total <= 10) return 2;
            return StaggerGroupCountDefault;
        }

        public static int ClaimStaggerSlot()
        {
            var groups = GetEffectiveStaggerGroupCount();
            var slot = _nextStaggerSlot % groups;
            _nextStaggerSlot++;
            return slot;
        }

        // --- Sim-speed override for testing ---
        // When set (not null), overrides MyAPIGateway.Physics.ServerSimulationRatio.
        // Controlled via /nanobars sim <value|reset> command (admin-only).
        public static float? SimSpeedOverride = null;

        public static float GetEffectiveSimSpeed()
        {
            if (SimSpeedOverride.HasValue) return SimSpeedOverride.Value;
            return MyAPIGateway.Physics != null ? MyAPIGateway.Physics.ServerSimulationRatio : 1.0f;
        }

        // --- OPT 3: Global grind budget per tick ---
        // Caps total ServerDoGrind calls per tick across all BaRs.
        // Two budgets: count-based (MaxGrindsPerTick) and time-based (MaxGrindMsPerTick).
        // The time budget prevents multiple expensive grinds from stacking in one frame.
        public const int MaxGrindsPerTickDefault = 10;
        public const double MaxGrindMsPerTickDefault = 8.0;
        private static int _grindsThisTick;
        private static double _grindMsThisTick;
        private static int _lastGrindBudgetTick = -1;

        // Peak grind usage tracking for HUD debug
        private static int _grindPeakUsed;
        public static int GrindBudgetPeakUsed { get { return _grindPeakUsed; } }
        public static void ResetGrindBudgetStats() { _grindPeakUsed = 0; }

        public static int GetEffectiveMaxGrindsPerTick()
        {
            var configured = Settings.MaxGrindsPerTick;
            if (configured > 0) return configured;
            // Auto: scale with BaR count, minimum 5.
            var total = NanobotSystems.Count;
            return Math.Max(5, Math.Min(MaxGrindsPerTickDefault, total));
        }

        public static bool TryClaimGrindSlot()
        {
            var tick = MyAPIGateway.Session.GameplayFrameCounter;
            if (tick != _lastGrindBudgetTick)
            {
                _lastGrindBudgetTick = tick;
                _grindsThisTick = 0;
                _grindMsThisTick = 0.0;
            }
            if (_grindsThisTick >= GetEffectiveMaxGrindsPerTick()) return false;
            if (_grindMsThisTick >= MaxGrindMsPerTickDefault) return false;
            _grindsThisTick++;
            if (_grindsThisTick > _grindPeakUsed) _grindPeakUsed = _grindsThisTick;
            return true;
        }

        /// <summary>
        /// Called after each ServerDoGrind to accumulate time spent grinding this tick.
        /// </summary>
        public static void ReportGrindTime(double ms)
        {
            _grindMsThisTick += ms;
        }

        // --- Lightweight tick cost tracking (always-on, for debug HUD) ---
        private static double _tickCostAccumMs;
        private static int _tickCostCount;
        private static double _tickCostAvgMs;
        private static double _tickCostPeakMs;

        public static double TickCostAvgMs { get { return _tickCostAvgMs; } }
        public static double TickCostPeakMs { get { return _tickCostPeakMs; } }

        public static void ResetTickCostStats()
        {
            _tickCostAvgMs = _tickCostCount > 0 ? _tickCostAccumMs / _tickCostCount : 0;
            _tickCostAccumMs = 0;
            _tickCostCount = 0;
            _tickCostPeakMs = 0;
        }

        private void RecordTickCost(long startTimestamp)
        {
            var elapsed = (double)(System.Diagnostics.Stopwatch.GetTimestamp() - startTimestamp) / System.Diagnostics.Stopwatch.Frequency * 1000.0;
            _tickCostAccumMs += elapsed;
            _tickCostCount++;
            if (elapsed > _tickCostPeakMs) _tickCostPeakMs = elapsed;
        }

        // --- Network sync counters (for debug HUD) ---
        private static int _syncSent;
        private static int _syncSkipped;

        public static int SyncSent { get { return _syncSent; } }
        public static int SyncSkipped { get { return _syncSkipped; } }

        public static void ReportSyncSent() { _syncSent++; }
        public static void ReportSyncSkipped() { _syncSkipped++; }
        public static void ResetSyncStats() { _syncSent = 0; _syncSkipped = 0; }

        private bool _initialized = false;
        private bool _welcomeShown = false;
        private static TimeSpan _LastSourcesAndTargetsUpdateTimer;
        private static TimeSpan SourcesAndTargetsUpdateTimerInterval = TimeSpan.FromSeconds(2);
        private static TimeSpan _LastSyncModDataRequestSend;
        private static TimeSpan _LastGeneralPeriodicCheck;
        private static TimeSpan _LastTtlCacheCleanerCheck;
        private static TimeSpan _LastSafeZoneUpdateCheck;
        // BUG-123: friendly-BaR-by-owner cache. Rebuilt every 5 s in UpdateBeforeSimulation
        // to amortize the GetUserRelationToOwner cost across all grind ticks. The dictionary
        // itself is replaced wholesale (atomic reference swap), so reads from grind paths
        // always see a self-consistent snapshot. Initial value `TimeSpan.Zero` (NOT MinValue —
        // that overflows `now.Subtract(...)` and aborts UpdateBeforeSimulation, breaking
        // RebuildSourcesAndTargetsTimer and clustering): the first rebuild fires after ~5 s of
        // session time, which matches pre-BUG-123 behavior (the loop ran per-grind anyway).
        private static TimeSpan _LastFriendlyBaRsRebuild = TimeSpan.Zero;
        private static volatile Dictionary<long, List<NanobotSystem>> _FriendlyBaRsByOwner = new Dictionary<long, List<NanobotSystem>>();

        public const int MaxBackgroundTasks_Default = 4;
        public const int MaxBackgroundTasks_Max = 10;
        public const int MaxBackgroundTasks_Min = 1;
        public static Queue<Action> AsynActions = new Queue<Action>();
        private static int ActualBackgroundTaskCount = 0;

        // Cumulative stats for HUD — reset by ResetBackgroundTaskStats()
        private static int _bgTasksEnqueued;
        private static int _bgTasksCompleted;
        private static int _bgPeakRunning;

        public static int BackgroundTasksEnqueued { get { lock (AsynActions) { return _bgTasksEnqueued; } } }
        public static int BackgroundTasksCompleted { get { lock (AsynActions) { return _bgTasksCompleted; } } }
        public static int BackgroundPeakRunning { get { lock (AsynActions) { return _bgPeakRunning; } } }

        public static void ResetBackgroundTaskStats()
        {
            lock (AsynActions) { _bgTasksEnqueued = 0; _bgTasksCompleted = 0; _bgPeakRunning = ActualBackgroundTaskCount; }
        }

        // BUG-123: friendly-BaR cache accessor. Returns the snapshot list of BaRs whose
        // welder considers `ownerId` friendly. Reads the volatile dict reference once and
        // does a TryGetValue — both O(1). Returns false (and a null `friendlies`) if the
        // cache hasn't yet been built for this owner; callers may treat that as "no
        // friendlies for now" since the cache rebuilds every 5 s and friendly-damage
        // tagging is a UX courtesy, not a safety gate.
        public static bool TryGetFriendlyBaRsForOwner(long ownerId, out List<NanobotSystem> friendlies)
        {
            var snapshot = _FriendlyBaRsByOwner;
            return snapshot.TryGetValue(ownerId, out friendlies);
        }

        // BUG-123: rebuild method. Walks NanobotSystems twice in a nested loop, but only
        // for distinct source-owner IDs (`seenOwners`) so we never repeat work for two BaRs
        // sharing an owner. Engine GetUserRelationToOwner is called inside the inner loop
        // and is the dominant cost — but it now amortizes across 5 s of grind ticks instead
        // of running per-grind. Background-thread invocation matches GridOwnershipCacheHandler.
        // Builds a fresh dict and atomically swaps in via the volatile field — readers always
        // see a consistent snapshot.
        private static void RebuildFriendlyBaRsCache()
        {
            var newCache = new Dictionary<long, List<NanobotSystem>>();
            var seenOwners = new HashSet<long>();
            foreach (var sourceEntry in NanobotSystems)
            {
                var sourceWelder = sourceEntry.Value != null ? sourceEntry.Value.Welder : null;
                if (sourceWelder == null) continue;
                var sourceOwnerId = sourceWelder.OwnerId;
                if (sourceOwnerId == 0) continue;
                if (!seenOwners.Add(sourceOwnerId)) continue;

                List<NanobotSystem> list = null;
                foreach (var otherEntry in NanobotSystems)
                {
                    var otherSystem = otherEntry.Value;
                    if (otherSystem == null) continue;
                    var otherWelder = otherSystem.Welder;
                    if (otherWelder == null) continue;
                    var relation = otherWelder.GetUserRelationToOwner(sourceOwnerId);
                    if (MyRelationsBetweenPlayerAndBlockExtensions.IsFriendly(relation))
                    {
                        if (list == null) list = new List<NanobotSystem>();
                        list.Add(otherSystem);
                    }
                }
                if (list != null) newCache[sourceOwnerId] = list;
            }
            _FriendlyBaRsByOwner = newCache;
        }

        public void Init()
        {
            // Need to set this first to prevent multiple init calls.
            _initialized = true;

            // Load the settings.
            Settings = SyncModSettings.Load();
            SettingsValid = MyAPIGateway.Session.IsServer;

            // Set default log level from settings.
            Logging.Instance.LogLevel = Settings.LogLevel;

            // Trigger settings changed for the first time.
            SettingsChanged();

            // Initialize Shield API once per session
            if (Shield == null && Settings.ShieldCheckEnabled)
            {
                Shield = new ShieldApi();
                Shield.Load();
            }

            // Register Safe-Zone Handler.
            SafeZoneHandler.Register();

            // Register Damage Handler.
            DamageHandler.Register();

            // Register Network Messaging Handler.
            NetworkMessagingHandler.Register();

            // Register Chat Message Handler.
            ChatHandler.Register();

            // Register HUD overlay (client-side only, soft dependency on TextHudAPI).
            HudHandler.Register();

            Logging.Instance.Write("BuildAndRepairSystemMod: Initialized.");
        }

        public static void SettingsChanged()
        {
            if (SettingsValid)
            {
                // Trigger settings changed for all nanobots first.
                foreach (var entry in NanobotSystems)
                {
                    entry.Value.SettingsChanged();
                }

                // Init terminal controls after settings have changed for all nanobots.
                InitControls();
            }
        }

        public static void InitControls()
        {
            // Call also on dedicated else the properties for the scripting interface are not initialized
            if (SettingsValid && !NanobotTerminal.CustomControlsInit && NanobotSystems.Count > 0)
            {
                NanobotTerminal.InitializeControls();
            }
        }

        protected override void UnloadData()
        {
            // Wait until background tasks finish (with timeout to prevent game freeze).
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (DateTime.UtcNow < deadline)
            {
                int actualBackgroundTaskCount;
                lock (AsynActions)
                {
                    actualBackgroundTaskCount = ActualBackgroundTaskCount;
                }
                if (actualBackgroundTaskCount <= 0) break;
            }

            // Unregister terminal controls.
            try { NanobotTerminal.Cleanup(); } catch { }

            // Unregister Shield API message handler
            try { Shield?.Unload(); } catch { }
            Shield = null;

            // Unregister HUD overlay.
            try { HudHandler.Unregister(); } catch { }

            // Unregister Chat Handler.
            try { ChatHandler.Unregister(); } catch { }

            // Unregister Safe-Zone Handler.
            try { SafeZoneHandler.Unregister(); } catch { }

            // Unregister Network Messaging Handler.
            try { NetworkMessagingHandler.Unregister(); } catch { }

            // Unregister Datamage Handler.
            try { DamageHandler.Unregister(); } catch { }

            // Clear block assigned handler.
            try { BlockSystemAssigningHandler.Clear(); } catch { }

            // Clear cluster coordinator.
            try { ScanClusterCoordinator.Clear(); } catch { }

            // Clear shared caches.
            try { SharedGridBlockCache.Clear(); } catch { }
            try { SharedEntityCache.Clear(); } catch { }

            // Close the profiler to release any open log files.
            try { MethodProfiler.Close(); } catch { }

            // Close the logging instance to release the log file.
            try { Logging.Instance.Close(); } catch { }

            // Call base unload.
            base.UnloadData();

            _initialized = false;
        }

        public override void SaveData()
        {
            if (MyAPIGateway.Session.IsServer)
            {
                foreach (var entry in NanobotSystems)
                {
                    try { entry.Value.Settings.Save(entry.Value.Entity, ModGuid); } catch { }
                }
            }
            base.SaveData();
        }

        public override void UpdateBeforeSimulation()
        {
            var tickStart = System.Diagnostics.Stopwatch.GetTimestamp();
            var profilerTs = MethodProfiler.Start();
            try
            {
                // If not initialized, to that first.
                if (!_initialized)
                {
                    if (MyAPIGateway.Session == null)
                        return;

                    Init();
                }

                // If initialized, process calls.
                else
                {
                    var now = MyAPIGateway.Session.ElapsedPlayTime;

                    // Start processing.
                    if (MyAPIGateway.Session.IsServer)
                    {
                        string autoStopMsg;
                        ulong autoStopSteamId;
                        if (MethodProfiler.TickAutoStop(out autoStopMsg, out autoStopSteamId))
                        {
                            if (autoStopSteamId != 0 && MyAPIGateway.Multiplayer != null && MyAPIGateway.Multiplayer.MultiplayerActive)
                                NetworkMessagingHandler.SendCommandResponse(autoStopSteamId, autoStopMsg, false, false, null, null);
                            else if (MyAPIGateway.Utilities != null)
                                MyAPIGateway.Utilities.ShowMessage("Nanobars", autoStopMsg);
                        }

                        // Periodic ownership cache refresh
                        if (now.Subtract(_LastGeneralPeriodicCheck) >= TimeSpan.FromSeconds(10))
                        {
                            _LastGeneralPeriodicCheck = now;
                            MyAPIGateway.Parallel.StartBackground(() =>
                            {
                                try { GridOwnershipCacheHandler.Update(); } catch { }
                            });
                        }

                        if (now.Subtract(_LastSafeZoneUpdateCheck) >= TimeSpan.FromSeconds(6))
                        {
                            _LastSafeZoneUpdateCheck = now;
                            MyAPIGateway.Parallel.StartBackground(() =>
                            {
                                try { SafeZoneHandler.GetSafeZones(); } catch { }
                            });
                        }

                        if (now.Subtract(_LastTtlCacheCleanerCheck) >= TimeSpan.FromMinutes(2))
                        {
                            _LastTtlCacheCleanerCheck = now;
                            MyAPIGateway.Parallel.StartBackground(() =>
                            {
                                try { InventoryHelper.Cleanup(); } catch { }
                                try { BlockPriorityHandling.GetItemKeyCache.CleanupExpired(); } catch { }
                                try { BlockSystemAssigningHandler.Cleanup(); } catch { }
                                try { SharedGridBlockCache.Cleanup(); } catch { }
                                try { SharedEntityCache.Cleanup(); } catch { }
                            });
                        }

                        // BUG-123: rebuild the friendly-BaR cache every 5 s. Walks NanobotSystems
                        // and groups by source-owner → list of BaRs whose welder relation to that
                        // owner is friendly. Replaces 58 GetUserRelationToOwner engine calls per
                        // grind with a dict lookup + iterate. 5 s staleness is well inside the
                        // 30 s default FriendlyDamageTimeout envelope.
                        if (now.Subtract(_LastFriendlyBaRsRebuild) >= TimeSpan.FromSeconds(5))
                        {
                            _LastFriendlyBaRsRebuild = now;
                            MyAPIGateway.Parallel.StartBackground(() =>
                            {
                                try { RebuildFriendlyBaRsCache(); } catch { }
                            });
                            // BUG-125: piggy-back a defensive periodic flush of profiler buffers
                            // on the same 5 s cadence. Without per-line Flush in MethodProfiler.Write,
                            // a long-running profile session that doesn't call StopSession could leave
                            // data buffered indefinitely. This bounds loss to ~5 s on hard crash.
                            MyAPIGateway.Parallel.StartBackground(() =>
                            {
                                try { MethodProfiler.FlushAll(); } catch { }
                            });
                        }

                        RebuildSourcesAndTargetsTimer();

                        // BUG-127: drain at most MaxRazesPerTickDefault entries from the
                        // deferred raze queue. Each RemoveBlock costs 5-8 ms of SE engine
                        // cleanup on complex blocks; spreading across ticks keeps the per-tick
                        // peak inside the 16.7 ms frame budget.
                        ProcessRazeQueue();
                    }

                    // If the Settings is not yet valid, sync the settings between clients and server.
                    else if (!SettingsValid)
                    {
                        if (now.Subtract(_LastSyncModDataRequestSend) >= TimeSpan.FromSeconds(10))
                        {
                            NetworkMessagingHandler.MsgDataRequestSend();
                            _LastSyncModDataRequestSend = MyAPIGateway.Session.ElapsedPlayTime;
                        }
                    }

                    // Update HUD overlay (client-side only, self-throttled).
                    HudHandler.Update(now);

                    // FEAT-054: One-time admin welcome message on session join (multiplayer only).
                    if (!_welcomeShown && !MyAPIGateway.Utilities.IsDedicated
                        && MyAPIGateway.Multiplayer != null && MyAPIGateway.Multiplayer.MultiplayerActive)
                    {
                        var player = MyAPIGateway.Session.Player;
                        if (player != null)
                        {
                            _welcomeShown = true;
                            var level = player.PromoteLevel.ToString();
                            if (level == "Admin" || level == "SpaceMaster" || level == "Owner")
                            {
                                MyAPIGateway.Utilities.ShowMessage("Nanobars",
                                    string.Format("Hi admin! Build and Repair System v{0} loaded. Type /nanobars -help for available commands.", Constants.ModVersion));
                            }
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Logging.Instance.Error(e);
            }
            finally
            {
                RecordTickCost(tickStart);
                if (profilerTs != 0L)
                {
                    MethodProfiler.StopAndLog("Mod.UpdateBeforeSimulation", profilerTs, () =>
                        string.Format("bars={0}", NanobotSystems.Count));
                }
            }
        }

        /// <summary>
        /// Rebuild the list of targets and inventory sources
        /// </summary>
        protected void RebuildSourcesAndTargetsTimer()
        {
            if (MyAPIGateway.Session.ElapsedPlayTime.Subtract(_LastSourcesAndTargetsUpdateTimer) > SourcesAndTargetsUpdateTimerInterval)
            {
                var profilerTs = MethodProfiler.Start();
                try
                {
                    // BUG-053: Refresh safe zone state for all BaRs before cluster rebuild
                    // so cluster keys always reflect current safe zone permissions.
                    // This eliminates the timing gap where per-BaR timers (2s, unsynchronized)
                    // could leave stale state when RebuildClusters computes keys.
                    if (SafeZoneHandler.Zones.Count > 0)
                    {
                        foreach (var system in NanobotSystems.Values)
                        {
                            if (system.Welder != null && system.State.Ready)
                            {
                                try { system.SetSafeZoneAndShieldStates(); } catch { }
                            }
                        }
                    }

                    ScanClusterCoordinator.RebuildClusters();
                    foreach (var buildAndRepairSystem in NanobotSystems.Values)
                    {
                        buildAndRepairSystem.UpdateSourcesAndTargetsTimer();
                    }
                    _LastSourcesAndTargetsUpdateTimer = MyAPIGateway.Session.ElapsedPlayTime;
                }
                finally
                {
                    if (profilerTs != 0L)
                    {
                        MethodProfiler.StopAndLog("Mod.RebuildSourcesAndTargetsTimer", profilerTs, () =>
                            string.Format("systemCount={0}", NanobotSystems.Count));
                    }
                }
            }
        }

        private void BlockDebugInfo()
        {
            try
            {
                var tool = MyAPIGateway.Session.Player.Character.EquippedTool;
                if (tool != null)
                {
                    var target = tool.Components.Get<MyCasterComponent>()?.HitBlock as IMySlimBlock;
                    if (target != null)
                    {
                        var sb = new StringBuilder();
                        sb.AppendLine($"Target: {target.BlockName()}");
                        sb.AppendLine($"Integrity: {target.Integrity}/{target.MaxIntegrity}");
                        sb.AppendLine($"MaxDeformation: {target.MaxDeformation}");
                        sb.AppendLine($"HasDeformation: {target.HasDeformation}");
                        sb.AppendLine($"MinDeformation: {Utils.Utils.MinDeformation}");
                        MyAPIGateway.Utilities.ShowNotification(sb.ToString(), 5000);
                    }
                }
            }
            catch
            {
            }
        }

        public static void AddAsyncAction(Action newAction)
        {
            lock (AsynActions)
            {
                AsynActions.Enqueue(newAction);
                _bgTasksEnqueued++;
                if (ActualBackgroundTaskCount < Settings.MaxBackgroundTasks)
                {
                    ActualBackgroundTaskCount++;
                    if (ActualBackgroundTaskCount > _bgPeakRunning) _bgPeakRunning = ActualBackgroundTaskCount;
                    MyAPIGateway.Parallel.StartBackground(() =>
                    {
                        try
                        {
                            while (true)
                            {
                                Action pendingAction = null;
                                lock (AsynActions)
                                {
                                    if (AsynActions.Count > 0)
                                    {
                                        pendingAction = AsynActions.Dequeue();
                                    }
                                    if (pendingAction == null)
                                    {
                                        ActualBackgroundTaskCount--;
                                        break;
                                    }
                                }
                                if (pendingAction != null)
                                {
                                    try
                                    {
                                        pendingAction();
                                    }
                                    catch { }
                                    lock (AsynActions) { _bgTasksCompleted++; }
                                }
                            }
                        }
                        catch
                        {
                            lock (AsynActions)
                            {
                                ActualBackgroundTaskCount--;
                            }
                        }
                    });
                }
            }
        }
    }
}