using VRageMath;

namespace SKONanobotBuildAndRepairSystem.Testing.Scenarios
{
    /// <summary>
    /// Not a test: queued first in every session so no real scenario pays the one-time JIT cost
    /// of the weld, grind and scan code after a world load (S01 measured 38 ms, then 1 ms).
    /// No checks; it only has to run the paths once.
    /// </summary>
    internal sealed class S00_WarmUp : TestScenarioBase
    {
        private TestRig _rig;
        private TestRig _derelict;

        public override string Id { get { return "S00"; } }
        public override string Description { get { return "Warm-up, not a test: weld, grind and scan once"; } }
        public override int TimeoutSeconds { get { return 60; } }

        public override void Setup(TestContext ctx)
        {
            ctx.SetModSettings(s =>
            {
                s.Welder.WorkSpeed = 10;
                s.Welder.AllowedGrindJanitorRelations |= AutoGrindRelation.NoOwnership;
            });
            _rig = ctx.SpawnRig(new RigSpec { Name = "S00", Bars = 1, DamagedArmor = 5 });
            _derelict = ctx.SpawnRig(new RigSpec { Name = "S00-derelict", Bars = 0, DamagedArmor = 3, DamagedBuildPercent = 1f, OwnerId = 0, Offset = new Vector3D(0, 0, -30) });
        }

        protected override void Configure(TestContext ctx)
        {
            ctx.StockCargo(_rig, "SteelPlate", 500);
            ctx.ConfigureBars(_rig, s =>
            {
                s.SearchMode = SearchModes.BoundingBox;
                s.WorkMode = WorkModes.WeldBeforeGrind;
                s.WeldOptions = AutoWeldOptions.WeldFull;
                s.UseGrindJanitorOn = AutoGrindRelation.NoOwnership;
                s.GrindJanitorOptions = 0;
            });
        }

        public override bool IsComplete(TestContext ctx)
        {
            return ctx.CountFullIntegrity(_rig.Targets) == _rig.Targets.Count && ScenarioUtil.AllGone(ctx, _derelict, _derelict.Targets);
        }

        public override void Assert(TestContext ctx, TestResult result)
        {
            ctx.Metric("welded", (long)ctx.CountFullIntegrity(_rig.Targets));
        }
    }
}
