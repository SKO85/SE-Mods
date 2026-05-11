namespace SKONanobotBuildAndRepairSystem
{
    using DefenseShields;
    using Sandbox.ModAPI;
    using SKONanobotBuildAndRepairSystem.Cluster;
    using SKONanobotBuildAndRepairSystem.Chat;
    using SKONanobotBuildAndRepairSystem.Handlers;
    using SKONanobotBuildAndRepairSystem.Managers;
    using SKONanobotBuildAndRepairSystem.Caches;
    using SKONanobotBuildAndRepairSystem.Models;
    using SKONanobotBuildAndRepairSystem.Profiling;
    using SKONanobotBuildAndRepairSystem.Utils;
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
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

        // BUG-106: full-dismount throttle (3/tick); mech-block cap (1/tick) applies first.
        public const int MaxDismountsPerTickDefault = 3;
        private static readonly PerTickBudget _dismountBudget = new PerTickBudget(MaxDismountsPerTickDefault);

        // BUG-107: proj.Build throttle for projected-block materialization spikes.
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

        // Cache for the enabled-BaR count → mod-wide stagger. Refreshed lazily every
        // ~60 frames (~1 s @ 60Hz) so the per-tick call from each BaR's Update10
        // doesn't re-enumerate NanobotSystems every fire.
        private const int StaggerAutoCacheFrames = 60;
        private static int _cachedAutoStagger = -1;
        private static int _cachedAutoStaggerFrame = int.MinValue;

        public static int GetEffectiveStaggerGroupCount()
        {
            var configured = Settings.StaggerGroupCount;
            if (configured > 0) return configured;

            var frame = MyAPIGateway.Session != null ? MyAPIGateway.Session.GameplayFrameCounter : 0;
            if (_cachedAutoStagger > 0 && frame - _cachedAutoStaggerFrame < StaggerAutoCacheFrames)
                return _cachedAutoStagger;

            // Auto-scale by ENABLED BaR count only. Disabled BaRs short-circuit before
            // ServerTryWeldingGrindingCollecting, so they shouldn't punish staggering math
            // for the active population.
            var enabled = 0;
            foreach (var kvp in NanobotSystems)
            {
                if (kvp.Value != null && kvp.Value.IsEnabled) enabled++;
            }

            int value;
            if (enabled <= 5) value = 1;
            else if (enabled <= 10) value = 2;
            else value = StaggerGroupCountDefault;

            _cachedAutoStagger = value;
            _cachedAutoStaggerFrame = frame;
            return value;
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

        // OPT 3 / BUG-154: global weld + grind budgets per tick (count + ms cap).
        public const int MaxGrindsPerTickDefault = 10;
        public const double MaxGrindMsPerTickDefault = 8.0;
        public const int MaxWeldsPerTickDefault = 10;
        public const double MaxWeldMsPerTickDefault = 8.0;

        private static readonly PerTickBudget _grindBudget =
            new PerTickBudget(GetEffectiveMaxGrindsPerTick, GetEffectiveMaxGrindMsPerTick);
        private static readonly PerTickBudget _weldBudget =
            new PerTickBudget(GetEffectiveMaxWeldsPerTick, GetEffectiveMaxWeldMsPerTick);

        public static int GetEffectiveMaxGrindsPerTick()
        {
            var configured = Settings.MaxGrindsPerTick;
            if (configured > 0) return configured;
            // Auto: scale with BaR count, minimum 5.
            var total = NanobotSystems.Count;
            return Math.Max(5, Math.Min(MaxGrindsPerTickDefault, total));
        }

        public static int GetEffectiveMaxWeldsPerTick()
        {
            var configured = Settings.MaxWeldsPerTick;
            if (configured > 0) return configured;
            // Auto: scale with BaR count, minimum 5.
            var total = NanobotSystems.Count;
            return Math.Max(5, Math.Min(MaxWeldsPerTickDefault, total));
        }

        public static double GetEffectiveMaxGrindMsPerTick()
        {
            var configured = Settings.MaxGrindMsPerTick;
            return configured > 0 ? configured : MaxGrindMsPerTickDefault;
        }

        public static double GetEffectiveMaxWeldMsPerTick()
        {
            var configured = Settings.MaxWeldMsPerTick;
            return configured > 0 ? configured : MaxWeldMsPerTickDefault;
        }

        public static bool TryClaimGrindSlot() { return _grindBudget.TryClaim(); }
        public static bool TryClaimWeldSlot() { return _weldBudget.TryClaim(); }

        /// <summary>Called after each ServerDoGrind to accumulate time spent grinding this tick.</summary>
        public static void ReportGrindTime(double ms) { _grindBudget.ReportTime(ms); }

        /// <summary>Called after each ServerDoWeld to accumulate time spent welding this tick.</summary>
        public static void ReportWeldTime(double ms) { _weldBudget.ReportTime(ms); }

        public static int GrindBudgetPeakUsed { get { return _grindBudget.PeakUsed; } }
        public static int WeldBudgetPeakUsed { get { return _weldBudget.PeakUsed; } }
        public static void ResetGrindBudgetStats() { _grindBudget.ResetStats(); }
        public static void ResetWeldBudgetStats() { _weldBudget.ResetStats(); }

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

        // BUG-260511.13: tracks the last applied MaxSystemsPerTargetGrid so a
        // lowered limit can pre-release surplus BaR lock-ons before the per-BaR
        // SettingsChanged fan-out. Without this, every grid stays over the new
        // limit (count is held by existing CurrentWelding/CurrentGrindingBlock
        // references) and no BaR can pick a new target — the fleet deadlocks
        // and sim drops.
        private static int _lastMaxSystemsPerTargetGrid = -1;

        public static void SettingsChanged()
        {
            if (SettingsValid)
            {
                // Detect a *lowered* MaxSystemsPerTargetGrid and force-release
                // surplus lock-ons so GridSystemCount can drop to the new ceiling.
                // Runs BEFORE per-BaR SettingsChanged so the immediate rescans
                // see a clean saturation state.
                var currentLimit = Settings.MaxSystemsPerTargetGrid;
                if (_lastMaxSystemsPerTargetGrid > 0 && currentLimit > 0 && currentLimit < _lastMaxSystemsPerTargetGrid)
                {
                    ReleaseSurplusLockOnsForLoweredLimit(currentLimit);
                }
                // BUG-260511.14: any change to the limit (up or down) must reset
                // the FEAT-076 loop-exhausted flags on every BaR. If we don't,
                // BaRs that went exhausted during a lowered-limit window stay
                // exhausted even after the limit goes back up — the saturated-
                // count guard matches (was 0 then, still 0 now) and the picker
                // fast-skip never lifts until some unrelated trigger changes
                // the target-list hash. Profile confirmed this: 6960 grind/weld
                // picker calls × ~1.5 µs each = the fast-skip was firing on
                // every call.
                if (_lastMaxSystemsPerTargetGrid > 0
                    && currentLimit > 0
                    && currentLimit != _lastMaxSystemsPerTargetGrid)
                {
                    foreach (var pair in NanobotSystems)
                    {
                        if (pair.Value != null) pair.Value.ResetLoopExhaustedFlags();
                    }
                }
                _lastMaxSystemsPerTargetGrid = currentLimit;

                // Trigger settings changed for all nanobots first.
                foreach (var entry in NanobotSystems)
                {
                    entry.Value.SettingsChanged();
                }

                // Init terminal controls after settings have changed for all nanobots.
                InitControls();
            }
        }

        /// <summary>
        /// BUG-260511.13: when the admin lowers MaxSystemsPerTargetGrid below the
        /// existing per-grid system count, release the lock-ons of every BaR
        /// currently working a now-over-limit grid. GridSystemCount drops as
        /// the setters fire, the BaRs' next-cycle pickers see a clean saturation
        /// state, and the first `newLimit` BaRs per grid re-acquire targets.
        /// </summary>
        private static void ReleaseSurplusLockOnsForLoweredLimit(int newLimit)
        {
            // Identify grids over the new limit BEFORE releasing anything — the
            // setters mutate GridSystemCount while we iterate.
            var overLimitGrids = new HashSet<long>();
            foreach (var kvp in GridSystemCount)
            {
                if (kvp.Value > newLimit) overLimitGrids.Add(kvp.Key);
            }
            if (overLimitGrids.Count == 0) return;

            var released = 0;
            foreach (var pair in NanobotSystems)
            {
                var bar = pair.Value;
                if (bar == null || bar.State == null) continue;

                var weldBlock = bar.State.CurrentWeldingBlock;
                var grindBlock = bar.State.CurrentGrindingBlock;

                var weldGridId = (weldBlock != null && weldBlock.CubeGrid != null) ? weldBlock.CubeGrid.EntityId : 0L;
                var grindGridId = (grindBlock != null && grindBlock.CubeGrid != null) ? grindBlock.CubeGrid.EntityId : 0L;

                var weldOver = weldGridId != 0L && overLimitGrids.Contains(weldGridId);
                var grindOver = grindGridId != 0L && overLimitGrids.Contains(grindGridId);
                if (!weldOver && !grindOver) continue;

                // Setters Dec GridSystemCount; subsequent ticks rebuild saturation
                // and only `newLimit` BaRs per grid re-acquire.
                if (weldOver) bar.State.CurrentWeldingBlock = null;
                if (grindOver) bar.State.CurrentGrindingBlock = null;

                if (Settings.AssignToSystemEnabled && bar.Welder != null)
                {
                    BlockSystemAssigningHandler.ReleaseAllForSystem(bar.Welder.EntityId);
                }
                released++;
            }

            if (released > 0)
            {
                Logging.Instance.Write(Logging.Level.Info,
                    "MaxSystemsPerTargetGrid lowered to {0}; released {1} BaR lock-on(s) across {2} over-limit grid(s).",
                    newLimit, released, overLimitGrids.Count);
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

                        // BUG-127: tick the deferred raze handler (internally throttled).
                        RazeQueueHandler.Process();

                        // BUG-130: shared friendly-damage map cleanup (internally throttled).
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
                            // REF-4: compare MyPromoteLevel enum directly. Avoids the
                            // .ToString() allocation and is rename-proof.
                            var level = player.PromoteLevel;
                            if (level == MyPromoteLevel.Admin
                                || level == MyPromoteLevel.SpaceMaster
                                || level == MyPromoteLevel.Owner)
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
                    // BUG-053: refresh safe zone state before cluster rebuild so keys are current.
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