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
            // Auto: scale with active (enabled + working) BaR count, not total placed blocks.
            var active = 0;
            foreach (var sys in NanobotSystems.Values)
            {
                if (sys.Welder != null && sys.Welder.IsWorking)
                    active++;
            }
            if (active <= 5) return 1;
            if (active <= 10) return 2;
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
        private static TimeSpan SourcesAndTargetsUpdateTimerInterval = TimeSpan.FromSeconds(1);
        private static TimeSpan _LastSyncModDataRequestSend;
        private static TimeSpan _LastGeneralPeriodicCheck;
        private static TimeSpan _LastTtlCacheCleanerCheck;
        private static TimeSpan _LastSafeZoneUpdateCheck;

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
                                try { DlcCheckHelper.CleanupOwnerCache(); } catch { }
                                try { SharedGridBlockCache.Cleanup(); } catch { }
                                try { SharedEntityCache.Cleanup(); } catch { }
                            });
                        }

                        RebuildSourcesAndTargetsTimer();
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