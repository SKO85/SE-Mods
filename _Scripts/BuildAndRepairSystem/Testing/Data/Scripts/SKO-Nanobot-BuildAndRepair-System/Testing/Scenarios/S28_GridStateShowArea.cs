using Sandbox.ModAPI;
using SKONanobotBuildAndRepairSystem.Models;
using SKONanobotBuildAndRepairSystem.Profiling;
using System.Collections.Generic;
using VRageMath;

namespace SKONanobotBuildAndRepairSystem.Testing.Scenarios
{
    internal enum RigGridState { Station, ShipStill, ShipMoving }

    /// <summary>
    /// SCN-BUDGET-007: 5 BaRs (BoundingBox) repair 20 damaged armor blocks on their own grid for a fixed
    /// 60 s window — on a station, a ship standing still and a ship drifting at 20 m/s with a slow yaw —
    /// with ShowArea off and on. Measurement scenario (compare the six results): ShowAreaDraw cost per
    /// frame, tick cost, scans, cluster rebuilds, entity queries and sim speed. Checks only that the
    /// work is done in every grid state and that the area box is drawn when ShowArea is on.
    /// </summary>
    internal sealed class S28_GridStateShowArea : TestScenarioBase
    {
        private const int BarCount = 5;
        private const int Blocks = 20;
        private const double WindowSeconds = 60.0;

        private static readonly string[] Methods =
        {
            "ShowAreaDraw",
            "UpdateBeforeSimulation10_100",
            "ServerTryWeldingGrindingCollecting",
            "ServerDoWeld",
            "AsyncClusterScan",
            "AsyncAddBlocksOfBox",
            "ApplyClusterResultToSelf",
            "ScanClusterCoordinator.RebuildClusters",
            "SharedEntityCache.GetEntitiesInBox"
        };

        private readonly string _id;
        private readonly RigGridState _state;
        private readonly bool _showArea;
        private TestRig _rig;
        private Vector3D _startPos;
        private bool _started;

        public S28_GridStateShowArea(string id, RigGridState state, bool showArea)
        {
            _id = id;
            _state = state;
            _showArea = showArea;
        }

        public override string Id { get { return _id; } }
        public override string Description { get { return "Measure: " + _state + ", ShowArea " + (_showArea ? "on" : "off") + ", 5 BaRs, 60 s"; } }
        public override int TimeoutSeconds { get { return 90; } }
        public override IList<string> PerfMethods { get { return Methods; } }

        public override void Setup(TestContext ctx)
        {
            _started = false;
            ctx.SetModSettings(s => { s.Welder.WorkSpeed = 10; });
            var spec = new RigSpec { Name = _id, Bars = BarCount, DamagedArmor = Blocks };
            if (_state != RigGridState.Station)
            {
                spec.Dynamic = true;
                if (_state == RigGridState.ShipMoving)
                {
                    spec.LinearVelocity = new Vector3(20f, 0f, 0f);
                    spec.AngularVelocity = new Vector3(0f, 0.05f, 0f);
                }
            }
            _rig = ctx.SpawnRig(spec);
        }

        protected override void Configure(TestContext ctx)
        {
            ctx.StockAllCargo(_rig, "SteelPlate", 200);
            ctx.ConfigureBars(_rig, s =>
            {
                s.SearchMode = SearchModes.BoundingBox;
                s.WorkMode = WorkModes.WeldBeforeGrind;
                s.WeldOptions = AutoWeldOptions.WeldFull;
                s.Flags = _showArea ? s.Flags | SyncBlockSettings.Settings.ShowArea : s.Flags & ~SyncBlockSettings.Settings.ShowArea;
            });
        }

        public override void Sample(TestContext ctx)
        {
            if (_started || _rig.Grid == null) return;
            _startPos = _rig.Grid.WorldMatrix.Translation;
            _started = true;
        }

        public override bool IsComplete(TestContext ctx)
        {
            return ctx.RunSeconds >= WindowSeconds;
        }

        public override void Assert(TestContext ctx, TestResult result)
        {
            var welded = ctx.CountFullIntegrity(_rig.Targets);
            var moved = _rig.Grid != null && _started ? Vector3D.Distance(_startPos, _rig.Grid.WorldMatrix.Translation) : 0.0;
            ctx.Metric("gridState", _state.ToString());
            ctx.Metric("showArea", _showArea);
            ctx.Metric("welded", (long)welded);
            ctx.Metric("movedMeters", moved);
            ctx.Check(welded == _rig.Targets.Count, "welded=" + welded + "/" + _rig.Targets.Count + " on a " + _state);

            if (_state == RigGridState.ShipMoving) ctx.Check(moved > 100.0, "moving rig only moved " + moved.ToString("F0") + " m");

            MethodProfiler.MethodStatsSnapshot draw;
            var drawCalls = ctx.TryGetMethodStats("ShowAreaDraw", out draw) ? draw.Calls : 0L;
            if (!MyAPIGateway.Utilities.IsDedicated)
                ctx.Check(_showArea ? drawCalls > 0 : drawCalls == 0, "ShowAreaDraw calls=" + drawCalls + " with ShowArea " + (_showArea ? "on" : "off"));

            ctx.Check(ctx.SimMin >= 0.5, "simMin<0.5");
        }
    }
}
