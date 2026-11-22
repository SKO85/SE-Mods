namespace SKONanobotBuildAndRepairSystem.Testing.Scenarios
{
    /// <summary>SCN-CORE-001 / SCN-WELD-001: one BaR repairs 10 damaged armor blocks from stocked cargo.</summary>
    internal sealed class S01_RepairStocked : TestScenarioBase
    {
        private TestRig _rig;

        public override string Id { get { return "S01"; } }
        public override string Description { get { return "One BaR repairs 10 damaged armor blocks from stocked cargo"; } }

        public override void Setup(TestContext ctx)
        {
            _rig = ctx.SpawnRig(new RigSpec { Name = "S01", Bars = 1, DamagedArmor = 10 });
            ctx.SetModSettings(s => { s.Welder.WorkSpeed = 10; });
        }

        protected override void Configure(TestContext ctx)
        {
            ctx.StockCargo(_rig, "SteelPlate", 500);
            ctx.ConfigureBars(_rig, s =>
            {
                s.SearchMode = SearchModes.Grids;
                s.WorkMode = WorkModes.WeldBeforeGrind;
                s.WeldOptions = AutoWeldOptions.WeldFull;
            });
        }

        public override bool IsComplete(TestContext ctx)
        {
            return ctx.CountFullIntegrity(_rig.Targets) == _rig.Targets.Count;
        }

        public override void Assert(TestContext ctx, TestResult result)
        {
            var welded = ctx.CountFullIntegrity(_rig.Targets);
            ctx.Metric("welded", (long)welded);
            ctx.Check(welded == _rig.Targets.Count, "welded=" + welded + "/" + _rig.Targets.Count);

            var bar = _rig.Bars[0];
            ctx.Metric("state", (long)bar.GetWorkingState());
            ctx.Check(bar.State.MissingComponents.Count == 0, "missingComponents=" + bar.State.MissingComponents.Count + " expected 0");

            ctx.CheckMethodAvgMax("ServerDoWeld", 2.0);
            ctx.CheckMethodMaxMax("ServerDoWeld", 30.0);
            ctx.CheckMethodMaxMax("ServerTryWelding", 10.0);
            ctx.Check(ctx.TickPeakMs <= 10.0, "tickPeakMs>10");
            ctx.Check(ctx.SimMin >= 0.9, "simMin<0.9");
        }
    }
}
