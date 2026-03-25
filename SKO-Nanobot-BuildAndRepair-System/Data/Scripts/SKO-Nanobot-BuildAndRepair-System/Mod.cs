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
        public static SyncModSettings Settings = new SyncModSettings();
        public static readonly ConcurrentDictionary<long, NanobotSystem> NanobotSystems = new ConcurrentDictionary<long, NanobotSystem>();
        public static ShieldApi Shield; // Centralized DefenseShields API instance

        /// <summary>
        /// Centralized per-tick cache: how many BaR systems are actively targeting each grid.
        /// Built once per tick by BuildGridSystemCountCache(), read by all NanobotSystem instances.
        /// </summary>
        public static Dictionary<long, int> GridSystemCountCache = new Dictionary<long, int>();
        private static int _lastGridCountCacheTick = -1;

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
            if (active <= 4) return 1;
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
            return true;
        }

        /// <summary>
        /// Called after each ServerDoGrind to accumulate time spent grinding this tick.
        /// </summary>
        public static void ReportGrindTime(double ms)
        {
            _grindMsThisTick += ms;
        }

        private bool _initialized = false;
        private static TimeSpan _LastSourcesAndTargetsUpdateTimer;
        private static TimeSpan SourcesAndTargetsUpdateTimerInterval = TimeSpan.FromSeconds(2);
        private static TimeSpan _LastSyncModDataRequestSend;
        private static TimeSpan _LastGeneralPeriodicCheck;
        private static TimeSpan _LastTtlCacheCleanerCheck;
        private static TimeSpan _LastSafeZoneUpdateCheck;

        public const int MaxBackgroundTasks_Default = 4;
        public const int MaxBackgroundTasks_Max = 10;
        public const int MaxBackgroundTasks_Min = 1;
        public static Queue<Action> AsynActions = new Queue<Action>();
        private static int ActualBackgroundTaskCount = 0;

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

                    // Show some debug info on blocks we point to:
                    // BlockDebugInfo();
                }
            }
            catch (Exception e)
            {
                Logging.Instance.Error(e);
            }
            finally
            {
                MethodProfiler.StopAndLog("Mod.UpdateBeforeSimulation", profilerTs, () =>
                    string.Format("bars={0}", NanobotSystems.Count));
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
                ScanClusterCoordinator.RebuildClusters();
                foreach (var buildAndRepairSystem in NanobotSystems.Values)
                {
                    buildAndRepairSystem.UpdateSourcesAndTargetsTimer();
                }
                _LastSourcesAndTargetsUpdateTimer = MyAPIGateway.Session.ElapsedPlayTime;
                }
                finally
                {
                    MethodProfiler.StopAndLog("Mod.RebuildSourcesAndTargetsTimer", profilerTs, () =>
                        string.Format("systemCount={0}", NanobotSystems.Count));
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

        /// <summary>
        /// Builds the centralized grid system count cache once per tick.
        /// Counts how many BaR systems are actively welding/grinding on each grid.
        /// Called by NanobotSystem.ServerTryWeldingGrindingCollecting() — runs at most once per tick.
        /// </summary>
        public static void BuildGridSystemCountCache()
        {
            var tick = MyAPIGateway.Session.GameplayFrameCounter;
            if (tick == _lastGridCountCacheTick) return;
            _lastGridCountCacheTick = tick;

            var profilerTs = MethodProfiler.Start();
            GridSystemCountCache.Clear();
            try
            {
                foreach (var system in NanobotSystems.Values)
                {
                    long weldGridId = 0;
                    long grindGridId = 0;

                    var weldBlock = system.State.CurrentWeldingBlock;
                    if (weldBlock != null && weldBlock.CubeGrid != null)
                        weldGridId = weldBlock.CubeGrid.EntityId;

                    var grindBlock = system.State.CurrentGrindingBlock;
                    if (grindBlock != null && grindBlock.CubeGrid != null)
                        grindGridId = grindBlock.CubeGrid.EntityId;

                    if (weldGridId != 0)
                    {
                        int existing;
                        if (GridSystemCountCache.TryGetValue(weldGridId, out existing))
                            GridSystemCountCache[weldGridId] = existing + 1;
                        else
                            GridSystemCountCache[weldGridId] = 1;
                    }

                    if (grindGridId != 0 && grindGridId != weldGridId)
                    {
                        int existing;
                        if (GridSystemCountCache.TryGetValue(grindGridId, out existing))
                            GridSystemCountCache[grindGridId] = existing + 1;
                        else
                            GridSystemCountCache[grindGridId] = 1;
                    }
                }
            }
            finally
            {
                var _cacheSize = GridSystemCountCache.Count;
                MethodProfiler.StopAndLog("Mod.BuildGridSystemCountCache", profilerTs, () =>
                    string.Format("cachedGrids={0};totalSystems={1}", _cacheSize, NanobotSystems.Count));
            }
        }

        public static void AddAsyncAction(Action newAction)
        {
            lock (AsynActions)
            {
                AsynActions.Enqueue(newAction);
                if (ActualBackgroundTaskCount < Settings.MaxBackgroundTasks)
                {
                    ActualBackgroundTaskCount++;
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
                                    ;
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