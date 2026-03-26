using Draygo.API;
using Sandbox.ModAPI;
using SKONanobotBuildAndRepairSystem.Cluster;
using SKONanobotBuildAndRepairSystem.Models;
using SKONanobotBuildAndRepairSystem.Profiling;
using System;
using System.Collections.Generic;
using System.Text;
using VRage.Game.ModAPI;
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
        private static StringBuilder _labelText = new StringBuilder(512);
        private static StringBuilder _valueText = new StringBuilder(512);
        private static bool _registered;
        private static TimeSpan _lastUpdate;
        private static readonly TimeSpan UpdateInterval = TimeSpan.FromSeconds(2);

        private static readonly Vector2D Origin = new Vector2D(0.55, 0.98);
        private const double ValueColumnOffset = 0.14;
        private const double Scale = 0.7;

        /// <summary>
        /// Latest debug stats received from server (DS client path).
        /// </summary>
        public static MsgDebugStats ReceivedStats;

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
                if (_labelMessage != null) { _labelMessage.Visible = false; _labelMessage.DeleteMessage(); _labelMessage = null; }
                if (_valueMessage != null) { _valueMessage.Visible = false; _valueMessage.DeleteMessage(); _valueMessage = null; }
                if (_hudApi != null) { _hudApi.Unload(); _hudApi = null; }
            }
            catch { }

            _registered = false;
            ReceivedStats = null;
        }

        private static void OnRegistered()
        {
            _registered = true;

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

            // Server: build and broadcast stats when debug/profiling active
            if (MyAPIGateway.Session.IsServer && (debugMode || profiling))
            {
                if (now.Subtract(_lastUpdate) >= UpdateInterval)
                {
                    _lastUpdate = now;
                    var stats = BuildStats(now);
                    NetworkMessagingHandler.BroadcastDebugStats(stats);

                    // On listen server, also render locally
                    if (!MyAPIGateway.Utilities.IsDedicated)
                        RenderHud(stats);
                }
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
                    return;
                }

                if (!IsLocalPlayerAdmin())
                {
                    SetVisible(false);
                    return;
                }

                if (now.Subtract(_lastUpdate) < UpdateInterval) return;
                _lastUpdate = now;

                RenderHud(stats);
                return;
            }

            // Server but neither debug nor profiling — hide
            if (!MyAPIGateway.Utilities.IsDedicated)
                SetVisible(false);
        }

        /// <summary>
        /// Build debug stats from server-side data structures.
        /// </summary>
        private static MsgDebugStats BuildStats(TimeSpan now)
        {
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
                if (!sys.State.SafeZoneAllowsWelding || !sys.State.SafeZoneAllowsGrinding)
                    s.SafeZoneBlocked++;

                var cluster = sys.AssignedCluster;
                if (cluster == null || cluster.IsCoordinator(sys))
                {
                    s.WeldTargets += sys.State.PossibleWeldTargets.CurrentCount;
                    s.GrindTargets += sys.State.PossibleGrindTargets.CurrentCount;
                    s.FloatTargets += sys.State.PossibleFloatingTargets.CurrentCount;
                }
                s.EmptyGridSkip += sys.EmptyGridCacheCount;

                var scanAge = (float)now.Subtract(sys.LastTargetsUpdate).TotalSeconds;
                if (scanAge > s.OldestScanAgeSec) s.OldestScanAgeSec = scanAge;
            }

            s.Clusters = ScanClusterCoordinator.ClusterCount;
            s.Stagger = Mod.GetEffectiveStaggerGroupCount();
            s.GrindBudgetMax = Mod.GetEffectiveMaxGrindsPerTick();
            s.GrindBudgetPeak = Mod.GrindBudgetPeakUsed;
            Mod.ResetGrindBudgetStats();
            s.SimSpeed = Mod.GetEffectiveSimSpeed();
            s.BgTasksEnqueued = Mod.BackgroundTasksEnqueued;
            s.BgTasksPeakRunning = Mod.BackgroundPeakRunning;
            Mod.ResetBackgroundTaskStats();
            s.BlockAssignments = BlockSystemAssigningHandler.AssignmentCount;
            s.MaxSysPerGrid = Mod.Settings.MaxSystemsPerTargetGrid;

            s.SafeZoneCount = SafeZoneHandler.Zones.Count;
            s.SafeZoneGridCache = SafeZoneHandler.GridCacheCount;
            s.SafeZoneBlockCache = SafeZoneHandler.BlockCacheCount;
            s.SafeZoneGrindCache = SafeZoneHandler.GrindCacheCount;
            s.OwnershipCache = GridOwnershipCacheHandler.CacheCount;
            s.BlockPriorityCache = BlockPriorityHandling.GetItemKeyCache.Count;

            var playerList = new List<IMyPlayer>();
            MyAPIGateway.Players.GetPlayers(playerList);
            s.PlayerCount = playerList.Count;

            s.ProfilingActive = MethodProfiler.IsRunning;
            if (s.ProfilingActive)
            {
                s.ProfilingElapsed = (float)MethodProfiler.ElapsedSeconds;
                s.ProfilingTotal = (float)MethodProfiler.TotalSessionSeconds;
                s.ProfilingMinDuration = MethodProfiler.MinDurationMs;
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

            if (Mod.Settings.DebugMode)
                RenderDebugRows(s);

            if (s.ProfilingActive)
                RenderProfilingRows(s, Mod.Settings.DebugMode);

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
            if (_labelMessage != null && _labelMessage.Visible != visible) _labelMessage.Visible = visible;
            if (_valueMessage != null && _valueMessage.Visible != visible) _valueMessage.Visible = visible;
        }

        private static void AddRow(string label, string value)
        {
            _labelText.Append(label);
            _labelText.Append("\n");
            _valueText.Append(value);
            _valueText.Append("\n");
        }

        private static void AddSpacer()
        {
            _labelText.Append("\n");
            _valueText.Append("\n");
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
            AddRow("<color=130,180,230>--- BaR Systems ---", string.Format("<color=130,180,230>---  <color=160,160,160>{0} players online", s.PlayerCount));
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
            AddRow("<color=130,180,230>--- Work Modes ---", "<color=130,180,230>---");
            AddRow("<color=white>Weld > Grind", string.Format("<color=200,255,200>{0}<color=white> ({1}%)", s.ModeWeldBefore, s.Active > 0 ? s.ModeWeldBefore * 100 / s.Active : 0));
            AddRow("<color=white>Grind > Weld", string.Format("<color=200,255,200>{0}<color=white> ({1}%)", s.ModeGrindBefore, s.Active > 0 ? s.ModeGrindBefore * 100 / s.Active : 0));
            AddRow("<color=white>Grind If Stuck", string.Format("<color=200,255,200>{0}<color=white> ({1}%)", s.ModeStuck, s.Active > 0 ? s.ModeStuck * 100 / s.Active : 0));
            AddRow("<color=white>Weld Only", string.Format("<color=200,255,200>{0}<color=white> ({1}%)", s.ModeWeldOnly, s.Active > 0 ? s.ModeWeldOnly * 100 / s.Active : 0));
            AddRow("<color=white>Grind Only", string.Format("<color=200,255,200>{0}<color=white> ({1}%)", s.ModeGrindOnly, s.Active > 0 ? s.ModeGrindOnly * 100 / s.Active : 0));
            AddRow("<color=white>Search Grids", string.Format("<color=200,255,200>{0}<color=white> ({1}%)", s.SearchGrids, s.Active > 0 ? s.SearchGrids * 100 / s.Active : 0));
            AddRow("<color=white>Search BBox", string.Format("<color=200,255,200>{0}<color=white> ({1}%)", s.SearchBBox, s.Active > 0 ? s.SearchBBox * 100 / s.Active : 0));

            // --- Targets ---
            AddSpacer();
            AddRow("<color=130,180,230>--- Targets ---", "<color=130,180,230>---");
            AddRow("<color=white>Weld Targets", string.Format("<color=200,255,200>{0}", s.WeldTargets));
            AddRow("<color=white>Grind Targets", string.Format("<color=200,255,200>{0}", s.GrindTargets));
            AddRow("<color=white>Float Targets", string.Format("<color=200,255,200>{0}", s.FloatTargets));

            // --- Performance ---
            AddSpacer();
            AddRow("<color=130,180,230>--- Performance ---", "<color=130,180,230>---");
            AddRow("<color=white>Clusters", string.Format("<color=200,255,200>{0}", s.Clusters));
            AddRow("<color=white>Stagger", string.Format("<color=200,255,200>{0}<color=white> groups", s.Stagger));
            var grindSaturated = s.GrindBudgetPeak >= s.GrindBudgetMax;
            AddRow("<color=white>Grind Budget",
                string.Format("{0}{1}<color=white> / {2} per tick{3}",
                    grindSaturated ? "<color=255,180,100>" : "<color=200,255,200>",
                    s.GrindBudgetPeak, s.GrindBudgetMax,
                    grindSaturated ? "  <color=255,180,100>CAPPED" : ""));
            AddRow("<color=white>Sim Speed", string.Format("{0}{1:0.00}", simColor, s.SimSpeed));
            AddRow("<color=white>Bg Tasks",
                string.Format("<color=200,255,200>{0}<color=white> enq  <color=200,255,200>{1}<color=white> peak",
                    s.BgTasksEnqueued, s.BgTasksPeakRunning));
            AddRow("<color=white>Scan Age", string.Format("{0}{1:F1}s<color=white> (oldest)", scanAgeColor, s.OldestScanAgeSec));
            AddRow("<color=white>Empty Grid Skip", string.Format("<color=200,255,200>{0}<color=white> grids", s.EmptyGridSkip));

            // --- Assignments & Limits ---
            AddSpacer();
            AddRow("<color=130,180,230>--- Assignments ---", "<color=130,180,230>---");
            AddRow("<color=white>Block Assigns", string.Format("<color=200,255,200>{0}", s.BlockAssignments));
            if (s.MaxSysPerGrid > 0)
                AddRow("<color=white>Max Sys/Grid", string.Format("<color=200,255,200>{0}", s.MaxSysPerGrid));

            // --- Caches ---
            AddSpacer();
            AddRow("<color=130,180,230>--- Caches ---", "<color=130,180,230>---");
            AddRow("<color=white>SafeZones", string.Format("<color=200,255,200>{0}<color=white> zones  <color=200,255,200>{1}<color=white> grid  <color=200,255,200>{2}<color=white> block  <color=200,255,200>{3}<color=white> grind",
                s.SafeZoneCount, s.SafeZoneGridCache, s.SafeZoneBlockCache, s.SafeZoneGrindCache));
            AddRow("<color=white>Ownership", string.Format("<color=200,255,200>{0}<color=white> entries", s.OwnershipCache));
            AddRow("<color=white>Block Priority", string.Format("<color=200,255,200>{0}<color=white> entries", s.BlockPriorityCache));
        }

        private static void RenderProfilingRows(MsgDebugStats s, bool debugAlreadyShown)
        {
            if (debugAlreadyShown)
                AddSpacer();

            AddRow("<color=255,200,100>--- Profiling ---", "<color=255,200,100>---");
            AddRow("<color=white>Status", "<color=255,200,100>RECORDING");

            if (s.ProfilingTotal > 0)
                AddRow("<color=white>Elapsed", string.Format("<color=255,200,100>{0:F0}s<color=white> / {1:F0}s", s.ProfilingElapsed, s.ProfilingTotal));
            else
                AddRow("<color=white>Elapsed", string.Format("<color=255,200,100>{0:F0}s <color=white>(no auto-stop)", s.ProfilingElapsed));

            AddRow("<color=white>Min Duration", string.Format("<color=200,255,200>{0}<color=white> ms", s.ProfilingMinDuration));
        }
    }
}
