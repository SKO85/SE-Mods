using VRage.Game;

namespace SKONanobotBuildAndRepairSystem.Testing.Scenarios
{
    /// <summary>SCN-WELD-005: with empty cargo the missing-components aggregate lists the total shortfall; no spikes while starved.</summary>
    internal sealed class S02_RepairEmptyCargo : TestScenarioBase
    {
        private const double ObserveSeconds = 20.0;
        private TestRig _rig;

        public override string Id { get { return "S02"; } }
        public override string Description { get { return "Empty cargo: MissingComponents aggregate populated, no welding, no spikes"; } }
        public override int TimeoutSeconds { get { return 60; } }

        public override void Setup(TestContext ctx)
        {
            _rig = ctx.SpawnRig(new RigSpec { Name = "S02", Bars = 1, DamagedArmor = 10 });
            ctx.SetModSettings(s => { s.Welder.WorkSpeed = 10; });
        }

        protected override void Configure(TestContext ctx)
        {
            ctx.ConfigureBars(_rig, s =>
            {
                s.SearchMode = SearchModes.Grids;
                s.WorkMode = WorkModes.WeldBeforeGrind;
                s.WeldOptions = AutoWeldOptions.WeldFull;
            });
        }

        public override bool IsComplete(TestContext ctx)
        {
            return ctx.RunSeconds >= ObserveSeconds;
        }

        public override void Assert(TestContext ctx, TestResult result)
        {
            var bar = _rig.Bars[0];
            int missingKinds;
            int steelPlates;
            var missing = bar.State.MissingComponents;
            lock (missing)
            {
                missingKinds = missing.Count;
                missing.TryGetValue(new MyDefinitionId(typeof(MyObjectBuilder_Component), "SteelPlate"), out steelPlates);
            }
            ctx.Metric("missingKinds", (long)missingKinds);
            ctx.Metric("missingSteelPlate", (long)steelPlates);
            ctx.Check(missingKinds > 0, "missingComponents empty");
            ctx.Check(steelPlates > 0, "SteelPlate not reported as missing");

            var state = bar.GetWorkingState();
            ctx.Metric("state", (long)state);
            ctx.Check(state == NanobotSystem.WorkingState.MissingComponents, "state=" + state + " expected MissingComponents");

            var welded = ctx.CountFullIntegrity(_rig.Targets);
            ctx.Metric("welded", (long)welded);
            ctx.Check(welded == 0, "welded=" + welded + " expected 0 (no stock)");

            ctx.CheckMethodMaxMax("ServerTryWelding", 5.0);
            ctx.CheckMethodMaxMax("RebuildMissingComponentsAggregate", 2.0);
            ctx.Check(ctx.TickPeakMs <= 5.0, "tickPeakMs>5 while starved");
        }
    }
}
