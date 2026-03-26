using Draygo.API;
using Sandbox.ModAPI;
using SKONanobotBuildAndRepairSystem.Cluster;
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
    /// Only visible when DebugMode is enabled or a profiling session is active.
    /// Soft dependency on TextHudAPI (BuildInfo mod) — if not installed, nothing shows.
    /// Uses two side-by-side HUDMessages (labels + values) for clean column alignment.
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

        public static void Update(TimeSpan now)
        {
            if (!_registered || _hudApi == null || !_hudApi.Heartbeat) return;
            if (_labelMessage == null || _valueMessage == null) return;

            var debugMode = Mod.Settings.DebugMode;
            var profiling = MethodProfiler.IsRunning;

            if (!debugMode && !profiling)
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

            var totalSystems = Mod.NanobotSystems.Count;
            if (totalSystems == 0)
            {
                SetVisible(false);
                return;
            }

            _labelText.Clear();
            _valueText.Clear();

            if (debugMode)
                BuildDebugRows(totalSystems, now);

            if (profiling)
                BuildProfilingRows(debugMode);

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

        private static void BuildDebugRows(int totalSystems, TimeSpan now)
        {
            // --- Gather per-system stats in a single pass ---
            var active = 0;
            var welding = 0;
            var grinding = 0;
            var collecting = 0;
            var transporting = 0;
            var inventoryFull = 0;
            var componentStarved = 0;
            var safeZoneBlocked = 0;
            var totalWeldTargets = 0;
            var totalGrindTargets = 0;
            var totalFloatTargets = 0;
            var emptyGridCacheTotal = 0;
            double oldestScanAgeSec = 0;
            var modeWeldBefore = 0;
            var modeGrindBefore = 0;
            var modeStuck = 0;
            var modeWeldOnly = 0;
            var modeGrindOnly = 0;
            var searchGrids = 0;
            var searchBBox = 0;

            foreach (var sys in Mod.NanobotSystems.Values)
            {
                if (sys.Welder == null || !sys.Welder.IsWorking) continue;
                active++;
                if (sys.State.Welding) welding++;
                else if (sys.State.Grinding) grinding++;
                else if (sys.State.NeedCollecting) collecting++;

                switch (sys.Settings.WorkMode)
                {
                    case WorkModes.WeldBeforeGrind: modeWeldBefore++; break;
                    case WorkModes.GrindBeforeWeld: modeGrindBefore++; break;
                    case WorkModes.GrindIfWeldGetStuck: modeStuck++; break;
                    case WorkModes.WeldOnly: modeWeldOnly++; break;
                    case WorkModes.GrindOnly: modeGrindOnly++; break;
                }
                if (sys.Settings.SearchMode == SearchModes.Grids) searchGrids++;
                else if (sys.Settings.SearchMode == SearchModes.BoundingBox) searchBBox++;
                if (sys.State.Transporting) transporting++;
                if (sys.State.InventoryFull) inventoryFull++;
                if (sys.State.NeedWelding && !sys.State.Welding && !sys.State.InventoryFull)
                    componentStarved++;
                if (!sys.State.SafeZoneAllowsWelding || !sys.State.SafeZoneAllowsGrinding)
                    safeZoneBlocked++;
                // Count targets only from cluster coordinators to avoid double-counting
                // (members in the same cluster share overlapping target lists).
                var cluster = sys.AssignedCluster;
                if (cluster == null || cluster.IsCoordinator(sys))
                {
                    totalWeldTargets += sys.State.PossibleWeldTargets.CurrentCount;
                    totalGrindTargets += sys.State.PossibleGrindTargets.CurrentCount;
                    totalFloatTargets += sys.State.PossibleFloatingTargets.CurrentCount;
                }
                emptyGridCacheTotal += sys.EmptyGridCacheCount;

                var scanAge = now.Subtract(sys.LastTargetsUpdate).TotalSeconds;
                if (scanAge > oldestScanAgeSec) oldestScanAgeSec = scanAge;
            }
            var idle = active - welding - grinding - collecting;
            var off = totalSystems - active;

            var clusters = ScanClusterCoordinator.ClusterCount;
            var stagger = Mod.GetEffectiveStaggerGroupCount();
            var grindBudget = Mod.GetEffectiveMaxGrindsPerTick();
            var simSpeed = Mod.GetEffectiveSimSpeed();
            var simColor = simSpeed < 0.99f ? "<color=255,180,100>" : "<color=200,255,200>";
            var scanAgeColor = oldestScanAgeSec > 15 ? "<color=255,180,100>" : "<color=200,255,200>";

            // --- Server ---
            var playerList = new List<IMyPlayer>();
            MyAPIGateway.Players.GetPlayers(playerList);
            var playerCount = playerList.Count;

            // --- Systems ---
            AddRow("<color=130,180,230>--- BaR Systems ---", string.Format("<color=130,180,230>---  <color=160,160,160>{0} players online", playerCount));
            var working = welding + grinding + collecting;
            var workPct = active > 0 ? (int)(100.0 * working / active) : 0;
            AddRow("<color=white>Systems", string.Format("<color=200,255,200>{0}<color=white> / {1}  (<color=200,255,200>{2}%<color=white>  working)", active, totalSystems, workPct));
            AddRow("<color=white>Activity",
                string.Format("<color=100,220,100>{0}<color=white> weld  <color=255,160,80>{1}<color=white> grind  <color=100,180,255>{2}<color=white> collect  <color=160,160,160>{3}<color=white> idle  <color=255,80,80>{4}<color=white> off",
                    welding, grinding, collecting, idle, off));
            AddRow("<color=white>Transporting", string.Format("<color=200,255,200>{0}", transporting));
            AddRow("<color=white>Inventory Full",
                inventoryFull > 0
                    ? string.Format("<color=255,180,100>{0}", inventoryFull)
                    : string.Format("<color=200,255,200>{0}", inventoryFull));
            AddRow("<color=white>Comp. Starved",
                componentStarved > 0
                    ? string.Format("<color=255,180,100>{0}", componentStarved)
                    : string.Format("<color=200,255,200>{0}", componentStarved));
            if (safeZoneBlocked > 0)
                AddRow("<color=white>SafeZone Block", string.Format("<color=255,100,100>{0}", safeZoneBlocked));
            // Work mode breakdown — separate row per mode
            AddSpacer();
            AddRow("<color=130,180,230>--- Work Modes ---", "<color=130,180,230>---");
            AddRow("<color=white>Weld > Grind", string.Format("<color=200,255,200>{0}<color=white> ({1}%)", modeWeldBefore, active > 0 ? modeWeldBefore * 100 / active : 0));
            AddRow("<color=white>Grind > Weld", string.Format("<color=200,255,200>{0}<color=white> ({1}%)", modeGrindBefore, active > 0 ? modeGrindBefore * 100 / active : 0));
            AddRow("<color=white>Grind If Stuck", string.Format("<color=200,255,200>{0}<color=white> ({1}%)", modeStuck, active > 0 ? modeStuck * 100 / active : 0));
            AddRow("<color=white>Weld Only", string.Format("<color=200,255,200>{0}<color=white> ({1}%)", modeWeldOnly, active > 0 ? modeWeldOnly * 100 / active : 0));
            AddRow("<color=white>Grind Only", string.Format("<color=200,255,200>{0}<color=white> ({1}%)", modeGrindOnly, active > 0 ? modeGrindOnly * 100 / active : 0));

            // Search mode breakdown
            AddRow("<color=white>Search Grids", string.Format("<color=200,255,200>{0}<color=white> ({1}%)", searchGrids, active > 0 ? searchGrids * 100 / active : 0));
            AddRow("<color=white>Search BBox", string.Format("<color=200,255,200>{0}<color=white> ({1}%)", searchBBox, active > 0 ? searchBBox * 100 / active : 0));

            // --- Targets ---
            AddSpacer();
            AddRow("<color=130,180,230>--- Targets ---", "<color=130,180,230>---");
            AddRow("<color=white>Weld Targets", string.Format("<color=200,255,200>{0}", totalWeldTargets));
            AddRow("<color=white>Grind Targets", string.Format("<color=200,255,200>{0}", totalGrindTargets));
            AddRow("<color=white>Float Targets", string.Format("<color=200,255,200>{0}", totalFloatTargets));

            // --- Performance ---
            AddSpacer();
            AddRow("<color=130,180,230>--- Performance ---", "<color=130,180,230>---");
            AddRow("<color=white>Clusters", string.Format("<color=200,255,200>{0}", clusters));
            AddRow("<color=white>Stagger", string.Format("<color=200,255,200>{0}<color=white> groups", stagger));
            var grindUsed = Mod.GrindBudgetPeakUsed;
            var grindSaturated = grindUsed >= grindBudget;
            Mod.ResetGrindBudgetStats();
            AddRow("<color=white>Grind Budget",
                string.Format("{0}{1}<color=white> / {2} per tick{3}",
                    grindSaturated ? "<color=255,180,100>" : "<color=200,255,200>",
                    grindUsed, grindBudget,
                    grindSaturated ? "  <color=255,180,100>CAPPED" : ""));
            AddRow("<color=white>Sim Speed", string.Format("{0}{1:0.00}", simColor, simSpeed));
            AddRow("<color=white>Bg Tasks",
                string.Format("<color=200,255,200>{0}<color=white> enq  <color=200,255,200>{1}<color=white> peak",
                    Mod.BackgroundTasksEnqueued, Mod.BackgroundPeakRunning));
            Mod.ResetBackgroundTaskStats();
            AddRow("<color=white>Scan Age", string.Format("{0}{1:F1}s<color=white> (oldest)", scanAgeColor, oldestScanAgeSec));
            AddRow("<color=white>Empty Grid Skip", string.Format("<color=200,255,200>{0}<color=white> grids", emptyGridCacheTotal));

            // --- Assignments & Limits ---
            AddSpacer();
            AddRow("<color=130,180,230>--- Assignments ---", "<color=130,180,230>---");
            AddRow("<color=white>Block Assigns", string.Format("<color=200,255,200>{0}", BlockSystemAssigningHandler.AssignmentCount));
            if (Mod.Settings.MaxSystemsPerTargetGrid > 0)
                AddRow("<color=white>Max Sys/Grid", string.Format("<color=200,255,200>{0}", Mod.Settings.MaxSystemsPerTargetGrid));

            // --- Caches ---
            AddSpacer();
            AddRow("<color=130,180,230>--- Caches ---", "<color=130,180,230>---");
            AddRow("<color=white>SafeZones", string.Format("<color=200,255,200>{0}<color=white> zones  <color=200,255,200>{1}<color=white> grid  <color=200,255,200>{2}<color=white> block  <color=200,255,200>{3}<color=white> grind",
                SafeZoneHandler.Zones.Count, SafeZoneHandler.GridCacheCount, SafeZoneHandler.BlockCacheCount, SafeZoneHandler.GrindCacheCount));
            AddRow("<color=white>Ownership", string.Format("<color=200,255,200>{0}<color=white> entries", GridOwnershipCacheHandler.CacheCount));
            AddRow("<color=white>Block Priority", string.Format("<color=200,255,200>{0}<color=white> entries", BlockPriorityHandling.GetItemKeyCache.Count));
        }

        private static void BuildProfilingRows(bool debugAlreadyShown)
        {
            if (debugAlreadyShown)
                AddSpacer();

            var elapsed = MethodProfiler.ElapsedSeconds;
            var total = MethodProfiler.TotalSessionSeconds;

            AddRow("<color=255,200,100>--- Profiling ---", "<color=255,200,100>---");
            AddRow("<color=white>Status", "<color=255,200,100>RECORDING");

            if (total > 0)
                AddRow("<color=white>Elapsed", string.Format("<color=255,200,100>{0:F0}s<color=white> / {1:F0}s", elapsed, total));
            else
                AddRow("<color=white>Elapsed", string.Format("<color=255,200,100>{0:F0}s <color=white>(no auto-stop)", elapsed));

            AddRow("<color=white>Min Duration", string.Format("<color=200,255,200>{0}<color=white> ms", MethodProfiler.MinDurationMs));
        }
    }
}
