using VRageMath;

namespace SKONanobotBuildAndRepairSystem.Testing.Scenarios
{
    /// <summary>
    /// SCN-CORE-002 / SCN-CFG-001: mod setting Range = 20 m clamps the working area to ±20 m.
    /// A damaged grid 12 m away is repaired, one 60 m away is left alone.
    /// </summary>
    internal sealed class S25_RangeLimit : TestScenarioBase
    {
        private const int Range = 20;
        private const double SettleSeconds = 10.0;

        private TestRig _rig;
        private TestRig _near;
        private TestRig _far;
        private double _farStart = -1;
        private double _nearDoneAt = -1;

        public override string Id { get { return "S25"; } }
        public override string Description { get { return "Mod Range 20 m: grid at 12 m repaired, grid at 60 m untouched"; } }

        public override void Setup(TestContext ctx)
        {
            _farStart = -1;
            _nearDoneAt = -1;
            // Range is applied to the BaR area when the BaR initialises: set it before spawning.
            ctx.SetModSettings(s =>
            {
                s.Welder.WorkSpeed = 10;
                s.Range = Range;
            });
            _rig = ctx.SpawnRig(new RigSpec { Name = "S25", Bars = 1 });
            _near = ctx.SpawnRig(Owned("S25-near", -12));
            _far = ctx.SpawnRig(Owned("S25-far", -60));
        }

        private static RigSpec Owned(string name, double z)
        {
            var spec = new RigSpec { Name = name, Bars = 0, DamagedArmor = 3, Offset = new Vector3D(0, 0, z) };
            spec.Extra.Add(new RigBlock { Kind = RigBlockKind.Battery, Position = new Vector3I(0, 0, 1), Tag = "battery" });
            return spec;
        }

        protected override void Configure(TestContext ctx)
        {
            ctx.StockCargo(_rig, "SteelPlate", 300);
            ctx.ConfigureBars(_rig, s =>
            {
                s.SearchMode = SearchModes.BoundingBox;
                s.WorkMode = WorkModes.WeldBeforeGrind;
                s.WeldOptions = AutoWeldOptions.WeldFull;
            });
        }

        public override void Sample(TestContext ctx)
        {
            if (_farStart < 0) _farStart = ctx.IntegritySum(_far.Targets);
            if (_nearDoneAt < 0 && ctx.CountFullIntegrity(_near.Targets) == _near.Targets.Count) _nearDoneAt = ctx.RunSeconds;
        }

        public override bool IsComplete(TestContext ctx)
        {
            return _nearDoneAt >= 0 && ctx.RunSeconds >= _nearDoneAt + SettleSeconds;
        }

        public override void Assert(TestContext ctx, TestResult result)
        {
            var near = ctx.CountFullIntegrity(_near.Targets);
            var farGain = ctx.IntegritySum(_far.Targets) - _farStart;
            var area = _rig.Bars.Count > 0 ? _rig.Bars[0].Settings.AreaSize : Vector3.Zero;
            ctx.Metric("nearWelded", (long)near);
            ctx.Metric("farIntegrityGain", farGain);
            ctx.Metric("areaSizeX", area.X);
            ctx.Check(near == _near.Targets.Count, "near grid welded=" + near + "/" + _near.Targets.Count);
            ctx.Check(farGain <= 0.01, "grid outside the 20 m range was welded (+" + farGain + ")");
            ctx.Check(area.X <= Range * 2 + 0.5f, "area size " + area.X + " m not clamped to Range*2");
        }
    }
}
