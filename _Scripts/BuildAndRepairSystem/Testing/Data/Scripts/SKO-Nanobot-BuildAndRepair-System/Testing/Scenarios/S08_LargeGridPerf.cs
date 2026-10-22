using System.Collections.Generic;
using VRageMath;

namespace SKONanobotBuildAndRepairSystem.Testing.Scenarios
{
    /// <summary>
    /// SCN-BUDGET-006 / BUG-260909.1 (GitHub #143): large-grid perf envelope. Five BaRs (BoundingBox)
    /// weld a 1000-block damaged grid for a fixed window. Functional pass = progress; the point is the
    /// perf numbers, especially RebuildMissingComponentsAggregate (walks every weld target per BaR every
    /// 3 s). Uses a code-built grid until the NanobotTest_BigShip prefab exists.
    /// </summary>
    internal sealed class S08_LargeGridPerf : TestScenarioBase
    {
        private const int BarCount = 5;
        private const int TargetBlocks = 1000;
        private const int TargetRowWidth = 32;     // 32 x 32 blocks = 80 m x 80 m
        private const double WindowSeconds = 180.0;
        private const int SteelPerContainer = 4000; // fits a large-grid small container; 22 containers on the rig

        private static readonly string[] LargeGridPerfMethods =
        {
            "ServerDoWeld",
            "ServerTryWelding",
            "ServerTryWeldingGrindingCollecting",
            "UpdateBeforeSimulation10_100",
            "RebuildMissingComponentsAggregate",
            "ServerFindMissingComponents",
            "PullFromSourcesOnePass",
            "AsyncClusterScan",
            "AsyncAddBlocksOfBox",
            "ApplyClusterResultToSelf"
        };

        private TestRig _rig;
        private TestRig _target;
        private int _weldTargetsPeak;

        public override string Id { get { return "S08"; } }
        public override string Description { get { return "Perf: 5 BaRs weld a 1000-block grid for 180 s (#143 envelope)"; } }
        public override int TimeoutSeconds { get { return 300; } }
        public override IList<string> PerfMethods { get { return LargeGridPerfMethods; } }

        public override void Setup(TestContext ctx)
        {
            _weldTargetsPeak = 0;
            ctx.SetModSettings(s => { s.Welder.WorkSpeed = 10; });
            _rig = ctx.SpawnRig(new RigSpec { Name = "S08", Bars = BarCount });
            // Rig sits at the origin; the target spans x -40..37.5, z -90..-12.5 — inside a 200 m area box.
            _target = ctx.SpawnRig(new RigSpec
            {
                Name = "S08-target",
                Bars = 0,
                DamagedArmor = TargetBlocks,
                RowWidth = TargetRowWidth,
                Offset = new Vector3D(-40, 0, -90)
            });
        }

        protected override void Configure(TestContext ctx)
        {
            ctx.StockAllCargo(_rig, "SteelPlate", SteelPerContainer);
            ctx.ConfigureBars(_rig, s =>
            {
                s.SearchMode = SearchModes.BoundingBox;
                s.WorkMode = WorkModes.WeldBeforeGrind;
                s.WeldOptions = AutoWeldOptions.WeldFull;
                s.AreaOffset = Vector3.Zero;
                s.AreaSize = new Vector3(200f, 200f, 200f);
            });
        }

        public override void Sample(TestContext ctx)
        {
            var bars = _rig.Bars;
            for (int i = 0; i < bars.Count; i++)
            {
                var n = bars[i].State.PossibleWeldTargets.CurrentCount;
                if (n > _weldTargetsPeak) _weldTargetsPeak = n;
            }
        }

        public override bool IsComplete(TestContext ctx)
        {
            if (ctx.RunSeconds >= WindowSeconds) return true;
            return ctx.CountFullIntegrity(_target.Targets) == _target.Targets.Count;
        }

        public override void Assert(TestContext ctx, TestResult result)
        {
            var welded = ctx.CountFullIntegrity(_target.Targets);
            var seconds = ctx.RunSeconds > 0 ? ctx.RunSeconds : 1.0;
            ctx.Metric("targetBlocks", (long)_target.Targets.Count);
            ctx.Metric("welded", (long)welded);
            ctx.Metric("weldedPerSecond", welded / seconds);
            ctx.Metric("weldTargetsPeak", (long)_weldTargetsPeak);
            ctx.Check(welded > 0, "no welding progress in " + WindowSeconds + " s");
            ctx.Check(_weldTargetsPeak > 0, "BaRs never saw the target grid (area / search mode?)");

            // Envelope from SCN-BUDGET-006 — starting points, tune from the first agg= lines.
            ctx.CheckMethodAvgMax("UpdateBeforeSimulation10_100", 1.5);
            ctx.CheckMethodAvgMax("ServerDoWeld", 3.0);
            ctx.CheckMethodMaxMax("ServerDoWeld", 40.0);
            ctx.CheckMethodMaxMax("RebuildMissingComponentsAggregate", 5.0);
            ctx.Check(ctx.TickAvgMs <= 0.5, "tickAvgMs>0.5");
            ctx.Check(ctx.TickPeakMs <= 15.0, "tickPeakMs>15");
            ctx.Check(ctx.SimMin >= 0.8, "simMin<0.8");

            var weldCap = Mod.GetEffectiveMaxWeldsPerTick();
            ctx.Metric("weldBudgetCap", (long)weldCap);
            ctx.Check(ctx.WeldBudgetPeak <= weldCap, "weldBudgetPeak=" + ctx.WeldBudgetPeak + ">" + weldCap);
        }
    }
}
