using SKONanobotBuildAndRepairSystem.Models;

namespace SKONanobotBuildAndRepairSystem.Testing.Scenarios
{
    /// <summary>
    /// SCN-WELD-004 / colour rules on the own grid. S23 ignore colour — 3 painted damaged blocks are
    /// skipped, 3 unpainted ones welded. S24 grind colour — 3 painted intact blocks are ground away,
    /// 3 unpainted damaged ones welded.
    /// </summary>
    internal sealed class S23_Colors : TestScenarioBase
    {
        private const double SettleSeconds = 5.0;

        private readonly string _id;
        private readonly bool _grindColor;
        private TestRig _rig;
        private double _paintedStart = -1;
        private double _doneAt = -1;

        public S23_Colors(string id, bool grindColor)
        {
            _id = id;
            _grindColor = grindColor;
        }

        public override string Id { get { return _id; } }
        public override string Description
        {
            get
            {
                return _grindColor
                    ? "Grind colour: painted blocks ground away, unpainted damaged blocks welded"
                    : "Ignore colour: painted damaged blocks skipped, unpainted ones welded";
            }
        }

        public override void Setup(TestContext ctx)
        {
            _paintedStart = -1;
            _doneAt = -1;
            ctx.SetModSettings(s => { s.Welder.WorkSpeed = 10; });
            var spec = new RigSpec { Name = _id, Bars = 1, DamagedArmor = 3 };
            for (int i = 0; i < 3; i++)
                spec.Extra.Add(new RigBlock
                {
                    Kind = RigBlockKind.Armor,
                    Position = new VRageMath.Vector3I(i, 1, 1),
                    BuildPercent = _grindColor ? 1f : 0.2f,
                    Color = ScenarioUtil.Paint,
                    Tag = "painted"
                });
            _rig = ctx.SpawnRig(spec);
        }

        protected override void Configure(TestContext ctx)
        {
            ctx.StockCargo(_rig, "SteelPlate", 300);
            ctx.ConfigureBars(_rig, s =>
            {
                s.SearchMode = SearchModes.Grids;
                s.WorkMode = WorkModes.WeldBeforeGrind;
                s.WeldOptions = AutoWeldOptions.WeldFull;
                if (_grindColor)
                {
                    s.GrindColor = ScenarioUtil.Paint;
                    s.Flags = (s.Flags | SyncBlockSettings.Settings.UseGrindColor) & ~SyncBlockSettings.Settings.UseIgnoreColor;
                }
                else
                {
                    s.IgnoreColor = ScenarioUtil.Paint;
                    s.Flags = (s.Flags | SyncBlockSettings.Settings.UseIgnoreColor) & ~SyncBlockSettings.Settings.UseGrindColor;
                }
            });
        }

        public override void Sample(TestContext ctx)
        {
            if (_paintedStart < 0) _paintedStart = ctx.IntegritySum(_rig.Tag("painted"));
            if (_doneAt < 0 && ctx.CountFullIntegrity(_rig.Targets) == _rig.Targets.Count) _doneAt = ctx.RunSeconds;
        }

        public override bool IsComplete(TestContext ctx)
        {
            if (_grindColor) return _doneAt >= 0 && ctx.CountExisting(_rig.Tag("painted")) == 0;
            return _doneAt >= 0 && ctx.RunSeconds >= _doneAt + SettleSeconds;
        }

        public override void Assert(TestContext ctx, TestResult result)
        {
            var painted = _rig.Tag("painted");
            var welded = ctx.CountFullIntegrity(_rig.Targets);
            var paintedLeft = ctx.CountExisting(painted);
            ctx.Metric("welded", (long)welded);
            ctx.Metric("paintedLeft", (long)paintedLeft);
            ctx.Check(painted.Count == 3, "painted blocks resolved=" + painted.Count + "/3");
            ctx.Check(welded == _rig.Targets.Count, "unpainted welded=" + welded + "/" + _rig.Targets.Count);
            if (_grindColor)
                ctx.Check(paintedLeft == 0, "grind colour: " + paintedLeft + " painted block(s) left");
            else
                ctx.Check(paintedLeft == painted.Count && ctx.IntegritySum(painted) <= _paintedStart + 0.01, "ignore colour: painted blocks were welded");
        }
    }
}
