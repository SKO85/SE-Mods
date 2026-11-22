using SKONanobotBuildAndRepairSystem.Handlers;
using VRage.Game.ModAPI;

namespace SKONanobotBuildAndRepairSystem.Testing.Scenarios
{
    /// <summary>SCN-BUDGET-001: two BaRs on one grid never lock on to the same block or a block claimed by the other.</summary>
    internal sealed class S05_NoDoubleAssign : TestScenarioBase
    {
        private TestRig _rig;
        private int _violations;
        private int _samples;

        public override string Id { get { return "S05"; } }
        public override string Description { get { return "Two BaRs, 30 damaged blocks: no double lock-on / foreign assignment"; } }
        public override int TimeoutSeconds { get { return 180; } }

        public override void Setup(TestContext ctx)
        {
            _violations = 0;
            _samples = 0;
            _rig = ctx.SpawnRig(new RigSpec { Name = "S05", Bars = 2, DamagedArmor = 30 });
            ctx.SetModSettings(s =>
            {
                s.Welder.WorkSpeed = 10;
                s.AssignToSystemEnabled = true;
            });
        }

        protected override void Configure(TestContext ctx)
        {
            ctx.StockCargo(_rig, "SteelPlate", 1500);
            ctx.ConfigureBars(_rig, s =>
            {
                s.SearchMode = SearchModes.Grids;
                s.WorkMode = WorkModes.WeldBeforeGrind;
                s.WeldOptions = AutoWeldOptions.WeldFull;
            });
        }

        public override void Sample(TestContext ctx)
        {
            _samples++;
            var bars = _rig.Bars;
            for (int i = 0; i < bars.Count; i++)
            {
                var a = bars[i].State.CurrentWeldingBlock;
                if (a == null) continue;

                long assigned;
                if (a.TryGetAssignedSystem(out assigned) && assigned != bars[i].Welder.EntityId) _violations++;

                for (int j = i + 1; j < bars.Count; j++)
                {
                    var b = bars[j].State.CurrentWeldingBlock;
                    if (b != null && SameBlock(a, b)) _violations++;
                }
            }
        }

        private static bool SameBlock(IMySlimBlock a, IMySlimBlock b)
        {
            if (ReferenceEquals(a, b)) return true;
            return a.CubeGrid != null && b.CubeGrid != null
                && a.CubeGrid.EntityId == b.CubeGrid.EntityId
                && a.Position == b.Position;
        }

        public override bool IsComplete(TestContext ctx)
        {
            return ctx.CountFullIntegrity(_rig.Targets) == _rig.Targets.Count;
        }

        public override void Assert(TestContext ctx, TestResult result)
        {
            ctx.Metric("samples", (long)_samples);
            ctx.Metric("violations", (long)_violations);
            ctx.Check(_violations == 0, "assignment violations=" + _violations);

            var welded = ctx.CountFullIntegrity(_rig.Targets);
            ctx.Metric("welded", (long)welded);
            ctx.Check(welded == _rig.Targets.Count, "welded=" + welded + "/" + _rig.Targets.Count);

            ctx.CheckMethodAvgMax("ServerDoWeld", 2.0);
            ctx.CheckMethodMaxMax("ServerDoWeld", 30.0);
            ctx.Check(ctx.SimMin >= 0.9, "simMin<0.9");
        }
    }
}
