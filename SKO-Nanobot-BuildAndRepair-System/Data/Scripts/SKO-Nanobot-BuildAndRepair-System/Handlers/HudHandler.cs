using Draygo.API;
using Sandbox.ModAPI;
using SKONanobotBuildAndRepairSystem.Cluster;
using SKONanobotBuildAndRepairSystem.Models;
using SKONanobotBuildAndRepairSystem.Profiling;
using System;
using System.Collections.Generic;
using System.Text;
using VRage.Game.ModAPI;
using VRage.Utils;
using VRageMath;
using BlendTypeEnum = VRageRender.MyBillboard.BlendTypeEnum;

namespace SKONanobotBuildAndRepairSystem.Handlers
{
    /// <summary>
    /// Client-side HUD overlay showing BaR debug/profiling stats.
    /// On listen servers, reads data directly. On DS clients, reads from synced MsgDebugStats.
    /// Soft dependency on TextHudAPI (BuildInfo mod).
    /// </summary>
    static class HudHandler
    {
        private static HudAPIv2 _hudApi;
        private static HudAPIv2.HUDMessage _labelMessage;
        private static HudAPIv2.HUDMessage _valueMessage;
        private static HudAPIv2.BillBoardHUDMessage _background;
        private static StringBuilder _labelText = new StringBuilder(512);
        private static StringBuilder _valueText = new StringBuilder(512);
        private static bool _registered;
        private static TimeSpan _lastUpdate;
        private static readonly TimeSpan UpdateInterval = TimeSpan.FromSeconds(2);

        public static bool IsApiReady { get { return _registered && _hudApi != null && _hudApi.Heartbeat; } }

        /// <summary>
        /// Local per-client visibility toggle for the debug HUD.
        /// Debug data is only rendered when both DebugMode (server) and this flag (local) are true.
        /// Default false so admins aren't disturbed until they explicitly /nanobars debug show.
        /// </summary>
        private static bool _localDebugVisible;
        public static bool LocalDebugVisible { get { return _localDebugVisible; } }

        public static void SetLocalDebugVisible(bool visible)
        {
            _localDebugVisible = visible;
            if (!visible) SetVisible(false);
        }

        // TextHudAPI coords: X [-1..1] left-right, Y [1..-1] top-bottom.
        private static readonly Vector2D OriginLeft = new Vector2D(-0.98, 0.98);
        private static readonly Vector2D OriginRight = new Vector2D(0.48, 0.98);
        private static bool _rightAligned;
        private static Vector2D Origin { get { return _rightAligned ? OriginRight : OriginLeft; } }
        private const double ValueColumnOffset = 0.14;
        private const double Scale = 0.6;
        private const double BgPadding = 0.015;
        private static readonly Color BgColor = new Color(0, 0, 0, 160);

        // --- Profile Summary panel (always top-right) ---
        private static HudAPIv2.HUDMessage _profLabelMessage;
        private static HudAPIv2.HUDMessage _profValueMessage;
        private static HudAPIv2.BillBoardHUDMessage _profBackground;
        private static StringBuilder _profLabelText = new StringBuilder(1024);
        private static StringBuilder _profValueText = new StringBuilder(1024);
        private static int _profRowCount;
        private static bool _profSummaryEnabled;
        private static TimeSpan _profLastUpdate;
        private static readonly Vector2D ProfOrigin = new Vector2D(0.48, 0.98);
        private const double ProfValueColumnOffset = 0.20;

        /// <summary>
        /// Latest debug stats received from server (DS client path).
        /// </summary>
        public static MsgDebugStats ReceivedStats;

        /// <summary>
        /// Latest profile summary. On server: built locally. On client: received via network.
        /// </summary>
        public static MsgProfileSummary ReceivedProfileSummary;

        public static void SetPosition(bool right)
        {
            _rightAligned = right;
            if (_labelMessage != null)
                _labelMessage.Origin = Origin;
            if (_valueMessage != null)
                _valueMessage.Origin = new Vector2D(Origin.X + ValueColumnOffset, Origin.Y);
        }

        /// <summary>
        /// Toggle the profile summary HUD panel on/off.
        /// </summary>
        public static bool ToggleProfileSummary()
        {
            _profSummaryEnabled = !_profSummaryEnabled;
            if (!_profSummaryEnabled)
                SetProfVisible(false);
            return _profSummaryEnabled;
        }

        public static bool IsProfileSummaryEnabled { get { return _profSummaryEnabled; } }

        public static void Register()
        {
            if (MyAPIGateway.Utilities.IsDedicated) return;

            try
            {
                _hudApi = new HudAPIv2(OnRegistered);
            }
            catch (Exception ex)
            {
                Utils.Logging.Instance.Write("HudHandler.Register failed: {0}", ex.Message);
            }
        }

        public static void Unregister()
        {
            try
            {
                if (_background != null) { _background.Visible = false; _background.DeleteMessage(); _background = null; }
                if (_labelMessage != null) { _labelMessage.Visible = false; _labelMessage.DeleteMessage(); _labelMessage = null; }
                if (_valueMessage != null) { _valueMessage.Visible = false; _valueMessage.DeleteMessage(); _valueMessage = null; }
                if (_profBackground != null) { _profBackground.Visible = false; _profBackground.DeleteMessage(); _profBackground = null; }
                if (_profLabelMessage != null) { _profLabelMessage.Visible = false; _profLabelMessage.DeleteMessage(); _profLabelMessage = null; }
                if (_profValueMessage != null) { _profValueMessage.Visible = false; _profValueMessage.DeleteMessage(); _profValueMessage = null; }
                if (_hudApi != null) { _hudApi.Unload(); _hudApi = null; }
            }
            catch { }

            _registered = false;
            ReceivedStats = null;
        }

        private static void OnRegistered()
        {
            _registered = true;

            // Background billboard — created first and on the shadow layer so it renders behind text.
            _background = new HudAPIv2.BillBoardHUDMessage(
                Material: MyStringId.GetOrCompute("Square"),
                Origin: Origin,
                BillBoardColor: BgColor,
                Scale: 1d,
                Width: 0.01f,
                Height: 0.01f,
                HideHud: false,
                Shadowing: true,
                Blend: BlendTypeEnum.PostPP
            );
            _background.Visible = false;

            _labelMessage = new HudAPIv2.HUDMessage(
                Message: _labelText,
                Origin: Origin,
                Scale: Scale,
                HideHud: false,
                Shadowing: true,
                ShadowColor: Color.Black,
                Blend: BlendTypeEnum.PostPP
            );
            _labelMessage.InitialColor = Color.White;
            _labelMessage.Visible = false;

            _valueMessage = new HudAPIv2.HUDMessage(
                Message: _valueText,
                Origin: new Vector2D(Origin.X + ValueColumnOffset, Origin.Y),
                Scale: Scale,
                HideHud: false,
                Shadowing: true,
                ShadowColor: Color.Black,
                Blend: BlendTypeEnum.PostPP
            );
            _valueMessage.InitialColor = Color.White;
            _valueMessage.Visible = false;

            // Profile summary panel (always top-right)
            _profBackground = new HudAPIv2.BillBoardHUDMessage(
                Material: MyStringId.GetOrCompute("Square"),
                Origin: ProfOrigin,
                BillBoardColor: BgColor,
                Scale: 1d,
                Width: 0.01f,
                Height: 0.01f,
                HideHud: false,
                Shadowing: true,
                Blend: BlendTypeEnum.PostPP
            );
            _profBackground.Visible = false;

            _profLabelMessage = new HudAPIv2.HUDMessage(
                Message: _profLabelText,
                Origin: ProfOrigin,
                Scale: Scale,
                HideHud: false,
                Shadowing: true,
                ShadowColor: Color.Black,
                Blend: BlendTypeEnum.PostPP
            );
            _profLabelMessage.InitialColor = Color.White;
            _profLabelMessage.Visible = false;

            _profValueMessage = new HudAPIv2.HUDMessage(
                Message: _profValueText,
                Origin: new Vector2D(ProfOrigin.X + ProfValueColumnOffset, ProfOrigin.Y),
                Scale: Scale,
                HideHud: false,
                Shadowing: true,
                ShadowColor: Color.Black,
                Blend: BlendTypeEnum.PostPP
            );
            _profValueMessage.InitialColor = Color.White;
            _profValueMessage.Visible = false;
        }

        /// <summary>
        /// Called from Mod.UpdateBeforeSimulation() every frame.
        /// On server: builds stats and broadcasts to clients.
        /// On client: renders HUD from received stats.
        /// </summary>
        public static void Update(TimeSpan now)
        {
            var debugMode = Mod.Settings.DebugMode;
            var profiling = MethodProfiler.IsRunning;

            // Server: build and broadcast stats to admin clients when debug/profiling active
            if (MyAPIGateway.Session.IsServer && (debugMode || profiling))
            {
                if (now.Subtract(_lastUpdate) >= UpdateInterval)
                {
                    _lastUpdate = now;
                    var stats = BuildStats(now);
                    NetworkMessagingHandler.BroadcastDebugStatsToAdmins(stats);

                    // Build and broadcast profile summary if data exists
                    BuildAndBroadcastProfileSummary();

                    // On listen server, also render locally if the admin opted in
                    if (!MyAPIGateway.Utilities.IsDedicated && _localDebugVisible)
                        RenderHud(stats);
                }

                // Profile summary panel rendering (works from ReceivedProfileSummary)
                UpdateProfileSummaryPanel(now);
                return;
            }

            // Client: render from received stats
            if (!MyAPIGateway.Session.IsServer)
            {
                if (!_registered || _hudApi == null || !_hudApi.Heartbeat) return;
                if (_labelMessage == null || _valueMessage == null) return;

                var stats = ReceivedStats;
                if (stats == null || (!stats.ProfilingActive && !debugMode))
                {
                    SetVisible(false);
                }
                else if (!_localDebugVisible || !IsLocalPlayerAdmin())
                {
                    SetVisible(false);
                }
                else
                {
                    if (now.Subtract(_lastUpdate) >= UpdateInterval)
                    {
                        _lastUpdate = now;
                        RenderHud(stats);
                    }
                }

                // Profile summary panel (renders from ReceivedProfileSummary set by network)
                UpdateProfileSummaryPanel(now);
                return;
            }

            // Server but neither debug nor profiling — hide main panel
            if (!MyAPIGateway.Utilities.IsDedicated)
                SetVisible(false);

            // Server: still build/broadcast profile summary if profiling data exists
            if (now.Subtract(_lastUpdate) >= UpdateInterval)
            {
                _lastUpdate = now;
                BuildAndBroadcastProfileSummary();
            }

            // Profile summary panel rendering
            UpdateProfileSummaryPanel(now);
        }

        /// <summary>
        /// Server-side: build profile summary from MethodProfiler and broadcast to admin clients.
        /// On DS: only broadcast once when profiling stops (not continuously).
        /// On listen server: broadcast continuously for live updates.
        /// </summary>
        private static bool _lastBroadcastWasRunning;
        private static void BuildAndBroadcastProfileSummary()
        {
            if (!MyAPIGateway.Session.IsServer) return;
            if (!MethodProfiler.HasSummaryData) return;

            var summary = MethodProfiler.BuildSummaryMessage(15, 10);
            if (summary == null) return;

            // Store locally for listen-server rendering
            ReceivedProfileSummary = summary;

            // On DS: only broadcast when profiling is running, or once when it just stopped
            if (MyAPIGateway.Utilities.IsDedicated)
            {
                if (summary.IsRunning)
                {
                    _lastBroadcastWasRunning = true;
                }
                else if (_lastBroadcastWasRunning)
                {
                    // Profiling just stopped — send final summary
                    _lastBroadcastWasRunning = false;
                }
                else
                {
                    // Already sent the final summary, don't keep broadcasting
                    return;
                }
            }

            NetworkMessagingHandler.BroadcastProfileSummaryToAdmins(summary);
        }

        private static void UpdateProfileSummaryPanel(TimeSpan now)
        {
            if (!_profSummaryEnabled) return;
            if (MyAPIGateway.Utilities.IsDedicated) return;
            if (!_registered || _hudApi == null || !_hudApi.Heartbeat) return;
            if (_profLabelMessage == null || _profValueMessage == null) return;

            if (!IsLocalPlayerAdmin())
            {
                SetProfVisible(false);
                return;
            }

            var summary = ReceivedProfileSummary;
            if (summary == null)
            {
                // Show a "no data" message so the user knows the panel is active
                if (now.Subtract(_profLastUpdate) < UpdateInterval) return;
                _profLastUpdate = now;

                _profLabelText.Clear();
                _profValueText.Clear();
                _profRowCount = 0;
                ProfAddRow("<color=130,180,230>--- PROFILE SUMMARY ---", "<color=130,180,230>---");
                ProfAddRow("<color=160,160,160>No profiling data.", "<color=160,160,160>Run: /nanobars profile start");
                SizeProfBackground();
                SetProfVisible(true);
                return;
            }

            if (now.Subtract(_profLastUpdate) < UpdateInterval) return;
            _profLastUpdate = now;

            _profLabelText.Clear();
            _profValueText.Clear();
            _profRowCount = 0;

            try
            {
                RenderProfileSummaryFromModel(summary);
            }
            catch (Exception ex)
            {
                _profRowCount = 0;
                _profLabelText.Clear();
                _profValueText.Clear();
                ProfAddRow("<color=255,100,100>Profile summary error", string.Format("<color=255,100,100>{0}", ex.Message));
            }

            SizeProfBackground();
            SetProfVisible(true);
        }

        private static void RenderProfileSummaryFromModel(MsgProfileSummary s)
        {
            // Header
            if (s.IsRunning)
                ProfAddRow("<color=255,200,100>--- PROFILE SUMMARY ---",
                    string.Format("<color=255,200,100>--- RECORDING {0:F0}s", s.ElapsedSeconds));
            else
                ProfAddRow("<color=130,180,230>--- PROFILE SUMMARY ---",
                    string.Format("<color=130,180,230>--- {0} methods tracked", s.MethodCount));

            // Session name
            if (!string.IsNullOrEmpty(s.SessionName))
                ProfAddRow("<color=white>Session",
                    string.Format("<color=200,255,200>{0}", s.SessionName));

            // Sim-speed
            if (s.SimSpeedMin > 0 || s.SimSpeedAvg > 0)
            {
                var minColor = s.SimSpeedMin < 0.8f ? "<color=255,180,100>" : "<color=200,255,200>";
                var avgColor = s.SimSpeedAvg < 0.9f ? "<color=255,180,100>" : "<color=200,255,200>";
                ProfAddRow("<color=white>Sim-Speed",
                    string.Format("{0}{1:F2}<color=white> min  {2}{3:F2}<color=white> avg",
                        minColor, s.SimSpeedMin, avgColor, s.SimSpeedAvg));
            }

            // Domain summary
            if (s.Domains != null && s.Domains.Count > 0)
            {
                ProfAddSpacer();
                ProfAddRow("<color=130,180,230>--- DOMAINS ---", "<color=130,180,230>---");
                foreach (var d in s.Domains)
                {
                    ProfAddRow(string.Format("<color=white>{0}", d.Name),
                        string.Format("<color=200,255,200>{0:F0}ms<color=white> total  <color=200,255,200>{1:F3}ms<color=white> avg  <color=200,255,200>{2:F1}ms<color=white> max  <color=160,160,160>({3})",
                            d.TotalMs, d.AvgMs, d.MaxMs, d.Calls));
                }
            }

            // Top methods
            if (s.TopMethods != null && s.TopMethods.Count > 0)
            {
                ProfAddSpacer();
                ProfAddRow(string.Format("<color=130,180,230>--- TOP {0} METHODS ---", s.TopMethods.Count), "<color=130,180,230>---");
                foreach (var m in s.TopMethods)
                {
                    var name = m.Name;
                    if (name.Length > 32) name = name.Substring(0, 29) + "...";
                    var totalColor = m.TotalMs > 1000 ? "<color=255,180,100>" : "<color=200,255,200>";
                    var maxColor = m.MaxMs > 10 ? "<color=255,180,100>" : "<color=200,255,200>";
                    ProfAddRow(string.Format("<color=white>{0}", name),
                        string.Format("{0}{1:F0}ms<color=white> total  <color=200,255,200>{2:F3}ms<color=white> avg  {3}{4:F1}ms<color=white> max  <color=160,160,160>({5})",
                            totalColor, m.TotalMs, m.AvgMs, maxColor, m.MaxMs, m.Calls));
                }
            }

            // Top grids
            if (s.TopGrids != null && s.TopGrids.Count > 0)
            {
                ProfAddSpacer();
                ProfAddRow(string.Format("<color=130,180,230>--- TOP {0} GRIDS ---", s.TopGrids.Count), "<color=130,180,230>---");
                foreach (var g in s.TopGrids)
                {
                    var gName = g.Name ?? "?";
                    if (gName.Length > 28) gName = gName.Substring(0, 25) + "...";
                    var totalColor = g.TotalMs > 1000 ? "<color=255,180,100>" : "<color=200,255,200>";
                    var ownerPart = string.IsNullOrEmpty(g.OwnerName) ? "" : string.Format("  <color=160,160,160>[{0}]", g.OwnerName);
                    ProfAddRow(string.Format("<color=white>{0}", gName),
                        string.Format("{0}{1:F0}ms<color=white> total  <color=160,160,160>({2} calls){3}", totalColor, g.TotalMs, g.Calls, ownerPart));
                }
            }

            ProfAddSpacer();
            ProfAddRow("<color=255,160,80>Powered by SKO85", "<color=255,160,80>sko85.github.io/SE-Mods");
        }

        private static void SizeProfBackground()
        {
            if (_profBackground != null && _profRowCount > 0)
            {
                var lineHeight = 0.032 * Scale;
                var totalHeight = lineHeight * _profRowCount;
                var totalWidth = ProfValueColumnOffset + 0.45;
                if (_profValueMessage != null)
                {
                    var valueLen = _profValueMessage.GetTextLength();
                    if (Math.Abs(valueLen.X) > 0.01)
                        totalWidth = ProfValueColumnOffset + Math.Abs(valueLen.X) + 0.02;
                }
                var bgW = totalWidth + BgPadding * 2;
                var bgH = totalHeight + BgPadding * 2;
                _profBackground.Origin = new Vector2D(ProfOrigin.X - BgPadding + bgW / 2, ProfOrigin.Y + BgPadding - bgH / 2);
                _profBackground.Width = (float)bgW;
                _profBackground.Height = (float)bgH;
            }
        }

        private static void ProfAddRow(string label, string value)
        {
            _profLabelText.Append(label);
            _profLabelText.Append('\n');
            _profValueText.Append(value);
            _profValueText.Append('\n');
            _profRowCount++;
        }

        private static void ProfAddSpacer()
        {
            _profLabelText.Append('\n');
            _profValueText.Append('\n');
            _profRowCount++;
        }

        private static void SetProfVisible(bool visible)
        {
            if (_profBackground != null && _profBackground.Visible != visible) _profBackground.Visible = visible;
            if (_profLabelMessage != null && _profLabelMessage.Visible != visible) _profLabelMessage.Visible = visible;
            if (_profValueMessage != null && _profValueMessage.Visible != visible) _profValueMessage.Visible = visible;
        }

        /// <summary>
        /// Build debug stats from server-side data structures.
        /// </summary>
        private static MsgDebugStats BuildStats(TimeSpan now)
        {
            var profilerTs = MethodProfiler.Start();
            var s = new MsgDebugStats();

            s.TotalSystems = Mod.NanobotSystems.Count;

            foreach (var sys in Mod.NanobotSystems.Values)
            {
                if (sys.Welder == null || !sys.Welder.IsWorking) continue;
                s.Active++;
                if (sys.State.Welding) s.Welding++;
                else if (sys.State.Grinding) s.Grinding++;
                else if (sys.State.NeedCollecting) s.Collecting++;

                switch (sys.Settings.WorkMode)
                {
                    case WorkModes.WeldBeforeGrind: s.ModeWeldBefore++; break;
                    case WorkModes.GrindBeforeWeld: s.ModeGrindBefore++; break;
                    case WorkModes.GrindIfWeldGetStuck: s.ModeStuck++; break;
                    case WorkModes.WeldOnly: s.ModeWeldOnly++; break;
                    case WorkModes.GrindOnly: s.ModeGrindOnly++; break;
                }
                if (sys.Settings.SearchMode == SearchModes.Grids) s.SearchGrids++;
                else if (sys.Settings.SearchMode == SearchModes.BoundingBox) s.SearchBBox++;

                if (sys.State.Transporting) s.Transporting++;
                if (sys.State.InventoryFull) s.InventoryFull++;
                if (sys.State.NeedWelding && !sys.State.Welding && !sys.State.InventoryFull)
                    s.ComponentStarved++;
                if (!sys.State.SafeZoneAllowsWelding || !sys.State.SafeZoneAllowsGrinding || !sys.State.SafeZoneAllowsBuildingProjections)
                    s.SafeZoneBlocked++;

                var cluster = sys.AssignedCluster;
                if (cluster == null || cluster.IsCoordinator(sys))
                {
                    s.WeldTargets += sys.State.PossibleWeldTargets.CurrentCount;
                    s.GrindTargets += sys.State.PossibleGrindTargets.CurrentCount;
                    s.FloatTargets += sys.State.PossibleFloatingTargets.CurrentCount;
                }
                s.EmptyGridSkip += sys.EmptyGridCacheCount;

                // Skip BaRs that have never scanned (TimeSpan.Zero = "no scan yet" sentinel).
                // Without this guard, disabled/off BaRs inflate the metric to session uptime.
                if (sys.LastTargetsUpdate > TimeSpan.Zero)
                {
                    var scanAge = (float)now.Subtract(sys.LastTargetsUpdate).TotalSeconds;
                    if (scanAge > s.OldestScanAgeSec) s.OldestScanAgeSec = scanAge;
                }
            }

            s.Clusters = ScanClusterCoordinator.ClusterCount;
            s.Stagger = Mod.GetEffectiveStaggerGroupCount();
            s.GrindBudgetMax = Mod.GetEffectiveMaxGrindsPerTick();
            s.GrindBudgetPeak = Mod.GrindBudgetPeakUsed;
            Mod.ResetGrindBudgetStats();
            s.WeldBudgetMax = Mod.GetEffectiveMaxWeldsPerTick();
            s.WeldBudgetPeak = Mod.WeldBudgetPeakUsed;
            Mod.ResetWeldBudgetStats();
            s.SimSpeed = Mod.GetEffectiveSimSpeed();
            s.BgTasksEnqueued = Mod.BackgroundTasksEnqueued;
            s.BgTasksPeakRunning = Mod.BackgroundPeakRunning;
            Mod.ResetBackgroundTaskStats();
            s.BlockAssignments = BlockSystemAssigningHandler.AssignmentCount;
            s.BlockFailCooldowns = BlockFailureCooldownHandler.CooldownCount;
            s.MaxSysPerGrid = Mod.Settings.MaxSystemsPerTargetGrid;

            s.SafeZoneCount = SafeZoneHandler.Zones.Count;
            s.SafeZoneGridCache = SafeZoneHandler.GridCacheCount;
            s.SafeZoneBlockCache = SafeZoneHandler.BlockCacheCount;
            s.SafeZoneGrindCache = SafeZoneHandler.GrindCacheCount;
            s.OwnershipCache = GridOwnershipCacheHandler.CacheCount;
            s.BlockPriorityCache = BlockPriorityHandling.GetItemKeyCache.Count;
            s.CustomSettingsLoaded = Mod.CustomSettingsLoaded;

            var playerList = new List<IMyPlayer>();
            MyAPIGateway.Players.GetPlayers(playerList);
            s.PlayerCount = playerList.Count;

            s.TickCostAvgMs = (float)Mod.TickCostAvgMs;
            s.TickCostPeakMs = (float)Mod.TickCostPeakMs;
            Mod.ResetTickCostStats();

            s.SyncSent = Mod.SyncSent;
            s.SyncSkipped = Mod.SyncSkipped;
            Mod.ResetSyncStats();

            s.ProfilingActive = MethodProfiler.IsRunning;
            if (s.ProfilingActive)
            {
                s.ProfilingElapsed = (float)MethodProfiler.ElapsedSeconds;
                s.ProfilingTotal = (float)MethodProfiler.TotalSessionSeconds;
                s.ProfilingMinDuration = MethodProfiler.MinDurationMs;
            }

            if (profilerTs != 0L)
            {
                MethodProfiler.StopAndLog("HudHandler.BuildStats", profilerTs, () =>
                    string.Format("systems={0};active={1}", s.TotalSystems, s.Active));
            }
            return s;
        }

        /// <summary>
        /// Render HUD from a stats snapshot (works on both server and client).
        /// </summary>
        private static void RenderHud(MsgDebugStats s)
        {
            if (!_registered || _hudApi == null || !_hudApi.Heartbeat) return;
            if (_labelMessage == null || _valueMessage == null) return;

            if (s.TotalSystems == 0)
            {
                SetVisible(false);
                return;
            }

            _labelText.Clear();
            _valueText.Clear();
            _rowCount = 0;

            if (Mod.Settings.DebugMode)
                RenderDebugRows(s);

            if (s.ProfilingActive)
                RenderProfilingRows(s, Mod.Settings.DebugMode);

            // Size background from row count.
            // TextHudAPI line height is ~0.07 screen-units per row at Scale=1.
            if (_background != null && _rowCount > 0)
            {
                var lineHeight = 0.032 * Scale;
                var totalHeight = lineHeight * _rowCount;

                // Width: use GetTextLength if available, otherwise estimate.
                var totalWidth = ValueColumnOffset + 0.35;
                if (_valueMessage != null)
                {
                    var valueLen = _valueMessage.GetTextLength();
                    if (Math.Abs(valueLen.X) > 0.01)
                        totalWidth = ValueColumnOffset + Math.Abs(valueLen.X) + 0.02;
                }

                // BillBoardHUDMessage Origin is the center of the quad, not top-left.
                // Offset by half width (right) and half height (down) from the text origin.
                var bgW = totalWidth + BgPadding * 2;
                var bgH = totalHeight + BgPadding * 2;
                _background.Origin = new Vector2D(Origin.X - BgPadding + bgW / 2, Origin.Y + BgPadding - bgH / 2);
                _background.Width = (float)bgW;
                _background.Height = (float)bgH;
            }

            SetVisible(true);
        }

        private static bool IsLocalPlayerAdmin()
        {
            var player = MyAPIGateway.Session.Player;
            if (player == null) return true;
            var level = player.PromoteLevel.ToString();
            return level == "Admin" || level == "SpaceMaster" || level == "Owner";
        }

        private static void SetVisible(bool visible)
        {
            if (_background != null && _background.Visible != visible) _background.Visible = visible;
            if (_labelMessage != null && _labelMessage.Visible != visible) _labelMessage.Visible = visible;
            if (_valueMessage != null && _valueMessage.Visible != visible) _valueMessage.Visible = visible;
        }

        private static int _rowCount;

        private static void AddRow(string label, string value)
        {
            _labelText.Append(label);
            _labelText.Append("\n");
            _valueText.Append(value);
            _valueText.Append("\n");
            _rowCount++;
        }

        private static void AddSpacer()
        {
            _labelText.Append("\n");
            _valueText.Append("\n");
            _rowCount++;
        }

        private static void RenderDebugRows(MsgDebugStats s)
        {
            var idle = s.Active - s.Welding - s.Grinding - s.Collecting;
            var off = s.TotalSystems - s.Active;
            var working = s.Welding + s.Grinding + s.Collecting;
            var workPct = s.Active > 0 ? (int)(100.0 * working / s.Active) : 0;
            var simColor = s.SimSpeed < 0.99f ? "<color=255,180,100>" : "<color=200,255,200>";
            var scanAgeColor = s.OldestScanAgeSec > 15 ? "<color=255,180,100>" : "<color=200,255,200>";

            // --- Systems ---
            AddRow("<color=130,180,230>--- BAR SYSTEMS ---", string.Format("<color=130,180,230>---  <color=160,160,160>v{0}  |  {1} players online", Constants.ModVersion, s.PlayerCount));
            AddRow("<color=white>Systems", string.Format("<color=200,255,200>{0}<color=white> / {1}  (<color=200,255,200>{2}%<color=white>  working)", s.Active, s.TotalSystems, workPct));
            AddRow("<color=white>Activity",
                string.Format("<color=100,220,100>{0}<color=white> weld  <color=255,160,80>{1}<color=white> grind  <color=100,180,255>{2}<color=white> collect  <color=160,160,160>{3}<color=white> idle  <color=255,80,80>{4}<color=white> off",
                    s.Welding, s.Grinding, s.Collecting, idle, off));
            AddRow("<color=white>Transporting", string.Format("<color=200,255,200>{0}", s.Transporting));
            AddRow("<color=white>Inventory Full",
                s.InventoryFull > 0
                    ? string.Format("<color=255,180,100>{0}", s.InventoryFull)
                    : string.Format("<color=200,255,200>{0}", s.InventoryFull));
            AddRow("<color=white>Comp. Starved",
                s.ComponentStarved > 0
                    ? string.Format("<color=255,180,100>{0}", s.ComponentStarved)
                    : string.Format("<color=200,255,200>{0}", s.ComponentStarved));
            if (s.SafeZoneBlocked > 0)
                AddRow("<color=white>SafeZone Block", string.Format("<color=255,100,100>{0}", s.SafeZoneBlocked));

            // --- Work Modes ---
            AddSpacer();
            AddRow("<color=130,180,230>--- WORK MODES ---", "<color=130,180,230>---");
            AddRow("<color=white>Weld > Grind", string.Format("<color=200,255,200>{0}<color=white> ({1}%)", s.ModeWeldBefore, s.Active > 0 ? s.ModeWeldBefore * 100 / s.Active : 0));
            AddRow("<color=white>Grind > Weld", string.Format("<color=200,255,200>{0}<color=white> ({1}%)", s.ModeGrindBefore, s.Active > 0 ? s.ModeGrindBefore * 100 / s.Active : 0));
            AddRow("<color=white>Grind If Stuck", string.Format("<color=200,255,200>{0}<color=white> ({1}%)", s.ModeStuck, s.Active > 0 ? s.ModeStuck * 100 / s.Active : 0));
            AddRow("<color=white>Weld Only", string.Format("<color=200,255,200>{0}<color=white> ({1}%)", s.ModeWeldOnly, s.Active > 0 ? s.ModeWeldOnly * 100 / s.Active : 0));
            AddRow("<color=white>Grind Only", string.Format("<color=200,255,200>{0}<color=white> ({1}%)", s.ModeGrindOnly, s.Active > 0 ? s.ModeGrindOnly * 100 / s.Active : 0));
            AddRow("<color=white>Search Grids", string.Format("<color=200,255,200>{0}<color=white> ({1}%)", s.SearchGrids, s.Active > 0 ? s.SearchGrids * 100 / s.Active : 0));
            AddRow("<color=white>Search BBox", string.Format("<color=200,255,200>{0}<color=white> ({1}%)", s.SearchBBox, s.Active > 0 ? s.SearchBBox * 100 / s.Active : 0));

            // --- Targets ---
            AddSpacer();
            AddRow("<color=130,180,230>--- TARGETS ---", "<color=130,180,230>---");
            AddRow("<color=white>Weld Targets", string.Format("<color=200,255,200>{0}", s.WeldTargets));
            AddRow("<color=white>Grind Targets", string.Format("<color=200,255,200>{0}", s.GrindTargets));
            AddRow("<color=white>Float Targets", string.Format("<color=200,255,200>{0}", s.FloatTargets));

            // --- Performance ---
            AddSpacer();
            AddRow("<color=130,180,230>--- PERFORMANCE ---", "<color=130,180,230>---");
            var tickColor = s.TickCostAvgMs > 4.0 ? "<color=255,180,100>" : "<color=200,255,200>";
            var peakColor = s.TickCostPeakMs > 8.0 ? "<color=255,180,100>" : "<color=200,255,200>";
            AddRow("<color=white>Mod Tick Cost",
                string.Format("{0}{1:0.00}ms<color=white> avg  {2}{3:0.00}ms<color=white> peak",
                    tickColor, s.TickCostAvgMs, peakColor, s.TickCostPeakMs));
            var syncTotal = s.SyncSent + s.SyncSkipped;
            var syncPct = syncTotal > 0 ? s.SyncSkipped * 100 / syncTotal : 0;
            AddRow("<color=white>Net Sync",
                string.Format("<color=200,255,200>{0}<color=white> sent  <color=200,255,200>{1}<color=white> skip ({2}%)",
                    s.SyncSent, s.SyncSkipped, syncPct));
            AddRow("<color=white>Clusters", string.Format("<color=200,255,200>{0}", s.Clusters));
            AddRow("<color=white>Stagger", string.Format("<color=200,255,200>{0}<color=white> groups", s.Stagger));
            var grindSaturated = s.GrindBudgetPeak >= s.GrindBudgetMax;
            AddRow("<color=white>Grind Budget",
                string.Format("{0}{1}<color=white> / {2} per tick{3}",
                    grindSaturated ? "<color=255,180,100>" : "<color=200,255,200>",
                    s.GrindBudgetPeak, s.GrindBudgetMax,
                    grindSaturated ? "  <color=255,180,100>CAPPED" : ""));
            var weldSaturated = s.WeldBudgetPeak >= s.WeldBudgetMax;
            AddRow("<color=white>Weld Budget",
                string.Format("{0}{1}<color=white> / {2} per tick{3}",
                    weldSaturated ? "<color=255,180,100>" : "<color=200,255,200>",
                    s.WeldBudgetPeak, s.WeldBudgetMax,
                    weldSaturated ? "  <color=255,180,100>CAPPED" : ""));
            AddRow("<color=white>Sim Speed", string.Format("{0}{1:0.00}", simColor, s.SimSpeed));
            AddRow("<color=white>Bg Tasks",
                string.Format("<color=200,255,200>{0}<color=white> enq  <color=200,255,200>{1}<color=white> peak",
                    s.BgTasksEnqueued, s.BgTasksPeakRunning));
            AddRow("<color=white>Scan Age", string.Format("{0}{1:F1}s<color=white> (oldest)", scanAgeColor, s.OldestScanAgeSec));
            AddRow("<color=white>Empty Grid Skip", string.Format("<color=200,255,200>{0}<color=white> grids", s.EmptyGridSkip));

            // --- Assignments & Limits ---
            AddSpacer();
            AddRow("<color=130,180,230>--- ASSIGNMENTS ---", "<color=130,180,230>---");
            AddRow("<color=white>Block Assigns", string.Format("<color=200,255,200>{0}", s.BlockAssignments));
            AddRow("<color=white>Fail Cooldowns", string.Format("<color=200,255,200>{0}", s.BlockFailCooldowns));
            if (s.MaxSysPerGrid > 0)
                AddRow("<color=white>Max Sys/Grid", string.Format("<color=200,255,200>{0}", s.MaxSysPerGrid));

            // --- Caches ---
            AddSpacer();
            AddRow("<color=130,180,230>--- CACHES ---", "<color=130,180,230>---");
            AddRow("<color=white>SafeZones", string.Format("<color=200,255,200>{0}<color=white> zones  <color=200,255,200>{1}<color=white> grid  <color=200,255,200>{2}<color=white> block  <color=200,255,200>{3}<color=white> grind",
                s.SafeZoneCount, s.SafeZoneGridCache, s.SafeZoneBlockCache, s.SafeZoneGrindCache));
            AddRow("<color=white>Ownership", string.Format("<color=200,255,200>{0}<color=white> entries", s.OwnershipCache));
            AddRow("<color=white>Block Priority", string.Format("<color=200,255,200>{0}<color=white> entries", s.BlockPriorityCache));

            // --- Settings & Footer ---
            AddSpacer();
            AddRow("<color=white>ModSettings.xml",
                s.CustomSettingsLoaded
                    ? "<color=200,255,200>Loaded (custom)"
                    : "<color=160,160,160>Not found (defaults)");
            AddSpacer();
            AddRow("<color=255,160,80>Powered by SKO85", "<color=255,160,80>sko85.github.io/SE-Mods");
        }

        private static void RenderProfilingRows(MsgDebugStats s, bool debugAlreadyShown)
        {
            if (debugAlreadyShown)
                AddSpacer();

            AddRow("<color=255,200,100>--- PROFILING ---", "<color=255,200,100>---");
            AddRow("<color=white>Status", "<color=255,200,100>RECORDING");

            if (s.ProfilingTotal > 0)
                AddRow("<color=white>Elapsed", string.Format("<color=255,200,100>{0:F0}s<color=white> / {1:F0}s", s.ProfilingElapsed, s.ProfilingTotal));
            else
                AddRow("<color=white>Elapsed", string.Format("<color=255,200,100>{0:F0}s <color=white>(no auto-stop)", s.ProfilingElapsed));

            AddRow("<color=white>Min Duration", string.Format("<color=200,255,200>{0}<color=white> ms", s.ProfilingMinDuration));
        }
    }
}
