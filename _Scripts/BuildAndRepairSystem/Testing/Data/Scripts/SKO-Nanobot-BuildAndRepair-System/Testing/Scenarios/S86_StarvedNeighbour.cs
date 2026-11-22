using VRageMath;

namespace SKONanobotBuildAndRepairSystem.Testing.Scenarios
{
    /// <summary>
    /// SCN-WELD-008 (BUG-261123.1): two ships, not connected. Ship A: BaR, 10 damaged blocks, empty cargo (A's BaR
    /// starves). Ship B, 25 m away: BaR (Fly) with stocked cargo, no damage. B must repair A — a starved BaR's
    /// "no components" must not make every other BaR skip those blocks.
    /// </summary>
    internal sealed class S86_StarvedNeighbour : TestScenarioBase
    {
        private const double Window = 45.0;

        private TestRig _shipA;
        private TestRig _shipB;
        private double _doneAt = -1;
        private double _firstWeldAt = -1;
        private double _start = -1;

        public override string Id { get { return "S86"; } }
        public override string Description { get { return "A starved BaR does not stop a stocked BaR on another ship from repairing its grid"; } }
        public override int TimeoutSeconds { get { return 90; } }

        public override void Setup(TestContext ctx)
        {
            _doneAt = _firstWeldAt = _start = -1;
            ctx.SetModSettings(s => { s.Welder.WorkSpeed = 10; });
            _shipA = ctx.SpawnRig(new RigSpec { Name = "S86-A", Bars = 1, DamagedArmor = 10 });
            _shipB = ctx.SpawnRig(new RigSpec { Name = "S86-B", Bars = 1, Offset = new Vector3D(0, 0, -25) });
        }

        protected override void Configure(TestContext ctx)
        {
            ctx.StockCargo(_shipB, "SteelPlate", 500);
            ctx.ConfigureBars(_shipA, s =>
            {
                s.SearchMode = SearchModes.Grids;
                s.WorkMode = WorkModes.WeldOnly;
                s.WeldOptions = AutoWeldOptions.WeldFull;
            });
            ctx.ConfigureBars(_shipB, s =>
            {
                s.SearchMode = SearchModes.BoundingBox;
                s.WorkMode = WorkModes.WeldOnly;
                s.WeldOptions = AutoWeldOptions.WeldFull;
            });
        }

        public override void Sample(TestContext ctx)
        {
            var sum = ctx.IntegritySum(_shipA.Targets);
            if (_start < 0) _start = sum;
            if (_firstWeldAt < 0 && sum > _start + 0.01) _firstWeldAt = ctx.RunSeconds;
            if (_doneAt < 0 && ctx.CountFullIntegrity(_shipA.Targets) == _shipA.Targets.Count) _doneAt = ctx.RunSeconds;
        }

        public override bool IsComplete(TestContext ctx)
        {
            return _doneAt >= 0 || ctx.RunSeconds >= Window;
        }

        public override void Assert(TestContext ctx, TestResult result)
        {
            var welded = ctx.CountFullIntegrity(_shipA.Targets);
            ctx.Metric("welded", (long)welded);
            ctx.Metric("firstWeldS", _firstWeldAt);
            ctx.Metric("doneS", _doneAt);
            var barA = _shipA.Bars.Count > 0 ? _shipA.Bars[0] : null;
            if (barA != null) ctx.Metric("shipABarState", barA.GetWorkingState().ToString());
            ctx.Check(welded == _shipA.Targets.Count, "ship B repaired " + welded + "/" + _shipA.Targets.Count + " of ship A's blocks in " + Window + " s");
        }
    }
}
