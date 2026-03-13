namespace SKONanobotBuildAndRepairSystem
{
    using DefenseShields;
    using Sandbox.Game.EntityComponents;
    using Sandbox.ModAPI;
    using SKONanobotBuildAndRepairSystem.Cluster;
    using SKONanobotBuildAndRepairSystem.Handlers;
    using SKONanobotBuildAndRepairSystem.Helpers;
    using SKONanobotBuildAndRepairSystem.Models;
    using SKONanobotBuildAndRepairSystem.Profiling;
    using SKONanobotBuildAndRepairSystem.Utils;
    using System;
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
        public static readonly Dictionary<long, NanobotSystem> NanobotSystems = new Dictionary<long, NanobotSystem>();
        public static ShieldApi Shield; // Centralized DefenseShields API instance

        /// <summary>
        /// Centralized per-tick cache: how many BaR systems are actively targeting each grid.
        /// Built once per tick by BuildGridSystemCountCache(), read by all NanobotSystem instances.
        /// </summary>
        public static Dictionary<long, int> GridSystemCountCache = new Dictionary<long, int>();
        private static int _lastGridCountCacheTick = -1;

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
            // Wait until tasks finish.
            while (true)
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
            try { Utils.SharedGridBlockCache.Clear(); } catch { }
            try { Utils.SharedEntityCache.Clear(); } catch { }

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
                lock (NanobotSystems)
                {
                    foreach (var entry in NanobotSystems)
                    {
                        try { entry.Value.Settings.Save(entry.Value.Entity, ModGuid); } catch { }
                    }
                }
            }
            base.SaveData();
        }

        public override void UpdateBeforeSimulation()
        {
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
                        MethodProfiler.TickAutoStop();

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
                                try { Utils.SharedGridBlockCache.Cleanup(); } catch { }
                                try { Utils.SharedEntityCache.Cleanup(); } catch { }
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
                lock (NanobotSystems)
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