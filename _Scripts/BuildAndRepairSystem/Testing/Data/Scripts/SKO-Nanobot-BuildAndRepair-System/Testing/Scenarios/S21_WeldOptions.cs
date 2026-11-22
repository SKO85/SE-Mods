using SKONanobotBuildAndRepairSystem.Extensions;

namespace SKONanobotBuildAndRepairSystem.Testing.Scenarios
{
    /// <summary>
    /// SCN-WELD-003: 5 armor blocks at 20 %. S21 WeldFunctional — welding stops at the functional
    /// integrity, below full. S22 WeldSkeleton — damaged blocks are not repaired at all.
    /// </summary>
    internal sealed class S21_WeldOptions : TestScenarioBase
    {
        private const double SettleSeconds = 5.0;
        private const double SkeletonWindow = 15.0;

        private readonly string _id;
        private readonly AutoWeldOptions _mode;
        private TestRig _rig;
        private double _start = -1;
        private double _reachedAt = -1;

        public S21_WeldOptions(string id, AutoWeldOptions mode)
        {
            _id = id;
            _mode = mode;
        }

        public override string Id { get { return _id; } }
        public override string Description
        {
            get
            {
                return _mode == AutoWeldOptions.WeldSkeleton
                    ? "Weld option Skeleton: damaged blocks are left as they are"
                    : "Weld option Functional: welding stops at functional integrity, below full";
            }
        }

        public override void Setup(TestContext ctx)
        {
            _start = -1;
            _reachedAt = -1;
            ctx.SetModSettings(s => { s.Welder.WorkSpeed = 10; });
            _rig = ctx.SpawnRig(new RigSpec { Name = _id, Bars = 1, DamagedArmor = 5 });
        }

        protected override void Configure(TestContext ctx)
        {
            ctx.StockCargo(_rig, "SteelPlate", 300);
            ctx.ConfigureBars(_rig, s =>
            {
                s.SearchMode = SearchModes.Grids;
                s.WorkMode = WorkModes.WeldBeforeGrind;
                s.WeldOptions = _mode;
            });
        }

        private int CountAtFunctional()
        {
            var n = 0;
            for (int i = 0; i < _rig.Targets.Count; i++)
            {
                var b = _rig.Targets[i];
                if (b != null && b.Integrity >= b.GetRequiredIntegrity(AutoWeldOptions.WeldFunctional)) n++;
            }
            return n;
        }

        public override void Sample(TestContext ctx)
        {
            if (_start < 0) _start = ctx.IntegritySum(_rig.Targets);
            if (_reachedAt < 0 && _mode == AutoWeldOptions.WeldFunctional && CountAtFunctional() == _rig.Targets.Count) _reachedAt = ctx.RunSeconds;
        }

        public override bool IsComplete(TestContext ctx)
        {
            if (_mode == AutoWeldOptions.WeldSkeleton) return ctx.RunSeconds >= SkeletonWindow;
            return _reachedAt >= 0 && ctx.RunSeconds >= _reachedAt + SettleSeconds;
        }

        public override void Assert(TestContext ctx, TestResult result)
        {
            var full = ctx.CountFullIntegrity(_rig.Targets);
            ctx.Metric("fullIntegrity", (long)full);
            ctx.Metric("integrityGain", ctx.IntegritySum(_rig.Targets) - _start);

            if (_mode == AutoWeldOptions.WeldSkeleton)
            {
                ctx.Check(ctx.IntegritySum(_rig.Targets) <= _start + 0.01, "skeleton mode repaired damaged blocks");
                return;
            }
            var functional = CountAtFunctional();
            ctx.Metric("atFunctional", (long)functional);
            ctx.Check(functional == _rig.Targets.Count, "at functional integrity=" + functional + "/" + _rig.Targets.Count);
            ctx.Check(full == 0, "functional mode welded " + full + " block(s) to full integrity");
        }
    }
}
