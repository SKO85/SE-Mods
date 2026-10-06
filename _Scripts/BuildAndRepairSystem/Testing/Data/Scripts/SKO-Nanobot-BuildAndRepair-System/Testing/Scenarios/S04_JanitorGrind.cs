using System.Collections.Generic;
using VRageMath;

namespace SKONanobotBuildAndRepairSystem.Testing.Scenarios
{
    /// <summary>SCN-GRIND-001: GrindOnly + NoOwnership razes an unowned 30-block grid and nothing else.</summary>
    internal sealed class S04_JanitorGrind : TestScenarioBase
    {
        private static readonly string[] GrindPerfMethods =
        {
            "ServerDoGrind",
            "ServerTryGrinding",
            "ServerTryWeldingGrindingCollecting",
            "UpdateBeforeSimulation10_100",
            "AsyncClusterScan",
            "ApplyClusterResultToSelf"
        };

        private TestRig _rig;
        private TestRig _target;

        public override string Id { get { return "S04"; } }
        public override string Description { get { return "GrindOnly/NoOwnership razes an unowned 30-block grid 40 m away"; } }
        public override IList<string> PerfMethods { get { return GrindPerfMethods; } }

        public override void Setup(TestContext ctx)
        {
            _rig = ctx.SpawnRig(new RigSpec { Name = "S04", Bars = 1 });
            _target = ctx.SpawnRig(new RigSpec { Name = "S04-target", Bars = 0, Armor = 30, OwnerId = 0, Offset = new Vector3D(0, 0, -40) });
            ctx.SetModSettings(s =>
            {
                s.Welder.WorkSpeed = 10;
                s.Welder.AllowedGrindJanitorRelations |= AutoGrindRelation.NoOwnership;
            });
        }

        protected override void Configure(TestContext ctx)
        {
            ctx.ConfigureBars(_rig, s =>
            {
                s.SearchMode = SearchModes.BoundingBox;
                s.WorkMode = WorkModes.GrindOnly;
                s.UseGrindJanitorOn = AutoGrindRelation.NoOwnership;
                s.GrindJanitorOptions = 0;
            });
        }

        public override bool IsComplete(TestContext ctx)
        {
            return TargetGone();
        }

        private bool TargetGone()
        {
            var grid = _target.Grid;
            return grid == null || grid.Closed || grid.MarkedForClose;
        }

        public override void Assert(TestContext ctx, TestResult result)
        {
            ctx.Metric("targetGone", TargetGone());
            ctx.Check(TargetGone(), "target grid still exists");

            var cap = Mod.GetEffectiveMaxGrindsPerTick();
            ctx.Metric("grindBudgetCap", (long)cap);
            ctx.Check(ctx.GrindBudgetPeak <= cap, "grindBudgetPeak=" + ctx.GrindBudgetPeak + ">" + cap);

            // Own (owned) rig must be untouched.
            ctx.Check(_rig.Grid != null && !_rig.Grid.Closed, "own rig grid closed");

            ctx.CheckMethodAvgMax("ServerDoGrind", 2.0);
            ctx.CheckMethodMaxMax("ServerDoGrind", 30.0);
            ctx.Check(ctx.SimMin >= 0.9, "simMin<0.9");
        }
    }
}
