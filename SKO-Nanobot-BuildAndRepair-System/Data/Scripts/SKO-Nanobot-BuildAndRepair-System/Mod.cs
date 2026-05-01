namespace SKONanobotBuildAndRepairSystem
{
    using DefenseShields;
    using Sandbox.Game.EntityComponents;
    using Sandbox.ModAPI;
    using SKONanobotBuildAndRepairSystem.Cluster;
    using SKONanobotBuildAndRepairSystem.Chat;
    using SKONanobotBuildAndRepairSystem.Handlers;
    using SKONanobotBuildAndRepairSystem.Helpers;
    using SKONanobotBuildAndRepairSystem.Managers;
    using SKONanobotBuildAndRepairSystem.Caches;
    using SKONanobotBuildAndRepairSystem.Models;
    using SKONanobotBuildAndRepairSystem.Profiling;
    using SKONanobotBuildAndRepairSystem.Utils;
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
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

        // Tick-cached MyAPIGateway.Session.ElapsedPlayTime. Refreshed once per
        // UpdateBeforeSimulation; consumed by TtlCache and any other hot-path
        // code that would otherwise pay for a Session+ElapsedPlayTime accessor
        // chain on every call. TimeSpan reads/writes are atomic on x64 so
        // background-thread readers (TTL cleanup) see a stable value.
        public static TimeSpan NowPlayTime;
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
            // Both Inc and Dec are only called from SyncBlockState property setters,
            // which fire from server-side weld/grind logic and from network-state-received
            // handlers — both run on the main thread. The dictionary stays concurrent so
            // background readers (RebuildSaturatedGrids / IsGridOverSystemLimit are main
            // thread today, but the type leaves the door open) are safe, but writers don't
            // need a CAS retry loop.
            int current;
            if (!GridSystemCount.TryGetValue(gridId, out current)) return;
            if (current <= 1)
            {
                int removed;
                GridSystemCount.TryRemove(gridId, out removed);
            }
            else
            {
                GridSystemCount[gridId] = current - 1;
            }
        }

        // OPT 1: Mechanical block grind throttle. Caps mechanical connection block
        // destructions to 1 per tick globally, preventing 100-380ms spikes when multiple
        // BaRs destroy pistons/rotors/hinges simultaneously.
        private static readonly PerTickBudget _mechanicalGrindBudget = new PerTickBudget(1);

        // BUG-106: full-dismount throttle. SE engine cascade for grid integrity recalc,
        // conveyor refresh, block events causes 5-12ms decreaseMs spikes per dismount;
        // spreading these across ticks prevents the spikes from compounding when many BaRs
        // dismount simultaneously. Mechanical-block cap (1/tick) is a stricter sub-budget
        // applied first; this cap (3/tick) covers all dismounts that reach the raze path.
        public const int MaxDismountsPerTickDefault = 3;
        private static readonly PerTickBudget _dismountBudget = new PerTickBudget(MaxDismountsPerTickDefault);

        // BUG-107: proj.Build throttle. SE engine materialization + grid topology update
        // produces 7-9ms buildMs spikes on projected armor/conveyor blocks; cap=3/tick
        // spreads the load when many BaRs materialize simultaneously.
        public const int MaxProjBuildsPerTickDefault = 3;
        private static readonly PerTickBudget _projBuildBudget = new PerTickBudget(MaxProjBuildsPerTickDefault);

        public static bool TryClaimMechanicalGrindSlot() { return _mechanicalGrindBudget.TryClaim(); }
        public static bool TryClaimDismountSlot() { return _dismountBudget.TryClaim(); }
        public static bool TryClaimProjBuildSlot() { return _projBuildBudget.TryClaim(); }

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

        // --- BUG-154: Global weld budget per tick ---
        // Caps total ServerDoWeld calls per tick across all BaRs. Mirrors the grind
        // budget. Reason: profiling on a 60-BaR server (transmit disabled to isolate)
        // showed Mod.UpdateBeforeSimulation peaking at 36 ms with ServerDoWeld up to
        // 11 ms per call (engine welder.Weld() cost is opaque). When 3+ BaRs land on
        // ServerDoWeld in the same tick the costs stack into a visible server spike.
        // Two budgets: count (MaxWeldsPerTick) and time (MaxWeldMsPerTick) — the time
        // budget caps cumulative damage when single welds spike.
        public const int MaxWeldsPerTickDefault = 10;
        public const double MaxWeldMsPerTickDefault = 8.0;
        private static int _weldsThisTick;
        private static double _weldMsThisTick;
        private static int _lastWeldBudgetTick = -1;

        // Peak weld usage tracking for HUD debug
        private static int _weldPeakUsed;
        public static int WeldBudgetPeakUsed { get { return _weldPeakUsed; } }
        public static void ResetWeldBudgetStats() { _weldPeakUsed = 0; }

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

        public static int GetEffectiveMaxWeldsPerTick()
        {
            var configured = Settings.MaxWeldsPerTick;
            if (configured > 0) return configured;
            // Auto: scale with BaR count, minimum 5.
            var total = NanobotSystems.Count;
            return Math.Max(5, Math.Min(MaxWeldsPerTickDefault, total));
        }

        public static bool TryClaimWeldSlot()
        {
            var tick = MyAPIGateway.Session.GameplayFrameCounter;
            if (tick != _lastWeldBudgetTick)
            {
                _lastWeldBudgetTick = tick;
                _weldsThisTick = 0;
                _weldMsThisTick = 0.0;
            }
            if (_weldsThisTick >= GetEffectiveMaxWeldsPerTick()) return false;
            if (_weldMsThisTick >= MaxWeldMsPerTickDefault) return false;
            _weldsThisTick++;
            if (_weldsThisTick > _weldPeakUsed) _weldPeakUsed = _weldsThisTick;
            return true;
        }

        /// <summary>
        /// Called after each ServerDoWeld to accumulate time spent welding this tick.
        /// </summary>
        public static void ReportWeldTime(double ms)
        {
            _weldMsThisTick += ms;
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

        // Background task queue forwarders — implementation lives in
        // Managers/BackgroundTaskQueue.cs. Kept on Mod for source compatibility
        // with existing call sites (HudHandler, NanobotSystem.Scanning, SyncModSettings).
        public const int MaxBackgroundTasks_Default = BackgroundTaskQueue.MaxBackgroundTasks_Default;
        public const int MaxBackgroundTasks_Max = BackgroundTaskQueue.MaxBackgroundTasks_Max;
        public const int MaxBackgroundTasks_Min = BackgroundTaskQueue.MaxBackgroundTasks_Min;

        public static int BackgroundTasksEnqueued { get { return BackgroundTaskQueue.Enqueued; } }
        public static int BackgroundTasksCompleted { get { return BackgroundTaskQueue.Completed; } }
        public static int BackgroundPeakRunning { get { return BackgroundTaskQueue.PeakRunning; } }

        public static void ResetBackgroundTaskStats()
        {
            BackgroundTaskQueue.ResetStats();
        }

        // FriendlyRelationsHandler forwarders — implementation lives in
        // Handlers/FriendlyRelationsHandler.cs. Kept on Mod for source compatibility
        // with existing call sites (Grinding, Welding, DamageHandler, State).
        public static bool TryGetFriendlyBaRsForOwner(long ownerId, out List<NanobotSystem> friendlies)
        {
            return FriendlyRelationsHandler.TryGetBaRsForOwner(ownerId, out friendlies);
        }

        public static bool TryGetFriendlyOwnersForOwner(long ownerId, out List<long> owners)
        {
            return FriendlyRelationsHandler.TryGetOwnersForOwner(ownerId, out owners);
        }

        public static void MarkFriendlyDamage(long welderOwnerId, IMySlimBlock block, TimeSpan deadline)
        {
            FriendlyRelationsHandler.MarkDamage(welderOwnerId, block, deadline);
        }

        public static bool IsFriendlyDamage(long welderOwnerId, IMySlimBlock block)
        {
            return FriendlyRelationsHandler.IsDamage(welderOwnerId, block);
        }

        public static void CleanupFriendlyDamage()
        {
            FriendlyRelationsHandler.CleanupDamage();
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
            // Stopwatch-based ~1 ms spin between checks: System.Threading.Sleep is
            // prohibited by the SE sandbox, but the previous lock+poll loop ran with no
            // delay and pegged a main-thread core during the wait. The 1 s ceiling is
            // a safety net — scan workers normally drain in well under 100 ms.
            var deadline = DateTime.UtcNow.AddSeconds(1);
            var pollSpacingTicks = System.Diagnostics.Stopwatch.Frequency / 1000;
            var spin = new System.Diagnostics.Stopwatch();
            while (DateTime.UtcNow < deadline)
            {
                if (BackgroundTaskQueue.RunningWorkers <= 0) break;
                spin.Restart();
                while (spin.ElapsedTicks < pollSpacingTicks) { }
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

            // Clear block failure cooldown handler.
            try { BlockFailureCooldownHandler.Clear(); } catch { }

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

                    NowPlayTime = MyAPIGateway.Session.ElapsedPlayTime;
                    Init();
                }

                // If initialized, process calls.
                else
                {
                    var now = MyAPIGateway.Session.ElapsedPlayTime;
                    NowPlayTime = now;

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

                        PeriodicMaintenanceScheduler.Tick(now);

                        RebuildSourcesAndTargetsTimer();

                        // BUG-127: tick the deferred raze handler. Internally throttles to
                        // one drain every RazeQueueHandler.ProcessIntervalTicks ticks and
                        // batches per-grid via IMyCubeGrid.RazeBlocks, collapsing N physics
                        // + integrity recalcs into 1 per grid.
                        RazeQueueHandler.Process();

                        // BUG-130: shared friendly-damage map cleanup. Internally throttles to
                        // Settings.FriendlyDamageCleanup (default 10 s). Replaces the per-BaR
                        // CleanupFriendlyDamage that previously ran on every BaR's Update10.
                        CleanupFriendlyDamage();
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

        public static void AddAsyncAction(Action newAction)
        {
            BackgroundTaskQueue.Enqueue(newAction);
        }
    }
}