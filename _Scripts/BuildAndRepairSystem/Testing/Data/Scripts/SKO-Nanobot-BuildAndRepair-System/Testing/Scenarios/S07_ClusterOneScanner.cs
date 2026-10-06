using SKONanobotBuildAndRepairSystem.Cluster;
using SKONanobotBuildAndRepairSystem.Profiling;

namespace SKONanobotBuildAndRepairSystem.Testing.Scenarios
{
    /// <summary>SCN-CLUSTER-001: five identically configured BaRs on one rig form one cluster with a single scanner.</summary>
    internal sealed class S07_ClusterOneScanner : TestScenarioBase
    {
        private const int BarCount = 5;
        private TestRig _rig;
        private bool _clusterChecked;
        private bool _clusterOk;
        private int _members;
        private bool _hasCoordinator;

        public override string Id { get { return "S07"; } }
        public override string Description { get { return "Five co-located BaRs: one cluster, one scanner, shared results"; } }
        public override int TimeoutSeconds { get { return 180; } }

        public override void Setup(TestContext ctx)
        {
            _clusterChecked = false;
            _clusterOk = false;
            _members = 0;
            _hasCoordinator = false;
            _rig = ctx.SpawnRig(new RigSpec { Name = "S07", Bars = BarCount, DamagedArmor = 50 });
            ctx.SetModSettings(s => { s.Welder.WorkSpeed = 10; });
        }

        protected override void Configure(TestContext ctx)
        {
            ctx.StockCargo(_rig, "SteelPlate", 2500);
            ctx.ConfigureBars(_rig, s =>
            {
                s.SearchMode = SearchModes.Grids;
                s.WorkMode = WorkModes.WeldBeforeGrind;
                s.WeldOptions = AutoWeldOptions.WeldFull;
            });
        }

        public override void Sample(TestContext ctx)
        {
            if (_clusterChecked) return;
            _clusterChecked = true;

            var bars = _rig.Bars;
            var first = bars.Count > 0 ? bars[0].AssignedCluster : null;
            _clusterOk = first != null;
            for (int i = 1; i < bars.Count && _clusterOk; i++)
                if (!ReferenceEquals(bars[i].AssignedCluster, first)) _clusterOk = false;
            if (first != null)
            {
                _members = first.Members.Count;
                _hasCoordinator = first.Coordinator != null;
            }
        }

        public override bool IsComplete(TestContext ctx)
        {
            return ctx.CountFullIntegrity(_rig.Targets) == _rig.Targets.Count;
        }

        public override void Assert(TestContext ctx, TestResult result)
        {
            ctx.Metric("clusterShared", _clusterOk);
            ctx.Metric("clusterMembers", (long)_members);
            ctx.Metric("clusterCount", (long)ScanClusterCoordinator.ClusterCount);
            ctx.Check(_clusterOk, "BaRs do not share one cluster");
            ctx.Check(_members == BarCount, "cluster members=" + _members + " expected " + BarCount);
            ctx.Check(_hasCoordinator, "cluster has no coordinator");

            // Only the coordinator scans: calls bounded by run length / scan interval (+ slack for forced rescans).
            var interval = Mod.Settings.TargetsUpdateInterval.TotalSeconds;
            if (interval <= 0) interval = 2.0;
            var maxScans = (long)(ctx.RunSeconds / interval) + 3;
            MethodProfiler.MethodStatsSnapshot scans;
            var scanCalls = ctx.TryGetMethodStats("AsyncClusterScan", out scans) ? scans.Calls : 0L;
            ctx.Metric("maxScansAllowed", maxScans);
            ctx.Check(scanCalls <= maxScans, "AsyncClusterScan.calls=" + scanCalls + ">" + maxScans + " (more than one scanner?)");

            var welded = ctx.CountFullIntegrity(_rig.Targets);
            ctx.Metric("welded", (long)welded);
            ctx.Check(welded == _rig.Targets.Count, "welded=" + welded + "/" + _rig.Targets.Count);

            ctx.CheckMethodMaxMax("AsyncClusterScan", 20.0);
            ctx.CheckMethodAvgMax("ApplyClusterResultToSelf", 1.0);
            ctx.Check(ctx.SimMin >= 0.9, "simMin<0.9");
        }
    }
}
