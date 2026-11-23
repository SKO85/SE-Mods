using System.Collections.Generic;

namespace SKONanobotBuildAndRepairSystem.Testing.Scenarios
{
    /// <summary>
    /// SCN-BUDGET-006 (ship): a 4000-block ship (dynamic grid, 64 x 63 damaged armor layer) with 5 BaRs on
    /// board repairs itself for a fixed 120 s window. Throughput and cost on a very large ship (S08 is a station).
    /// </summary>
    internal sealed class S43_VeryLargeShip : TestScenarioBase
    {
        private const int Blocks = 4000;
        private const int RowWidth = 64;
        private const double WindowSeconds = 120.0;

        private static readonly string[] Methods =
        {
            "UpdateBeforeSimulation10_100",
            "ServerTryWeldingGrindingCollecting",
            "ServerTryWelding",
            "ServerDoWeld",
            "ServerFindMissingComponents",
            "RebuildMissingComponentsAggregate",
            "AsyncClusterScan",
            "AsyncAddBlocksOfBox",
            "ApplyClusterResultToSelf"
        };

        private TestRig _ship;

        public override string Id { get { return "S43"; } }
        public override string Description { get { return "Very large ship: 4000 damaged blocks, 5 BaRs on board, 120 s"; } }
        public override int TimeoutSeconds { get { return 150; } }
        public override IList<string> PerfMethods { get { return Methods; } }

        public override void Setup(TestContext ctx)
        {
            ctx.SetModSettings(s => { s.Welder.WorkSpeed = 10; });
            _ship = ctx.SpawnRig(new RigSpec { Name = "S43", Bars = 5, DamagedArmor = Blocks, RowWidth = RowWidth, Dynamic = true });
        }

        protected override void Configure(TestContext ctx)
        {
            ctx.StockAllCargo(_ship, "SteelPlate", 3000);
            ctx.ConfigureBars(_ship, s =>
            {
                s.SearchMode = SearchModes.Grids;
                s.WorkMode = WorkModes.WeldBeforeGrind;
                s.WeldOptions = AutoWeldOptions.WeldFull;
            });
        }

        public override bool IsComplete(TestContext ctx)
        {
            return ctx.RunSeconds >= WindowSeconds || ctx.CountFullIntegrity(_ship.Targets) == _ship.Targets.Count;
        }

        public override void Assert(TestContext ctx, TestResult result)
        {
            var welded = ctx.CountFullIntegrity(_ship.Targets);
            var seconds = ctx.RunSeconds > 0 ? ctx.RunSeconds : 1.0;
            ctx.Metric("shipBlocks", (long)_ship.Targets.Count);
            ctx.Metric("welded", (long)welded);
            ctx.Metric("weldedPerSecond", welded / seconds);
            ctx.Check(welded > 0, "no welding progress on the large ship");
            // Speed floor: ~70 % of the 2026-11-22 baseline (14.2 welds/s, dev PC).
            ctx.Check(welded / seconds >= 10.0, "weldedPerSecond " + (welded / seconds).ToString("F1") + " below the floor of 10");

            ctx.CheckMethodAvgMax("UpdateBeforeSimulation10_100", 1.5);
            ctx.CheckMethodMaxMax("ServerDoWeld", 40.0);
            ctx.CheckMethodMaxMax("RebuildMissingComponentsAggregate", 5.0);
            ctx.Check(ctx.TickAvgMs <= 0.5, "tickAvgMs>0.5");
            ctx.CheckTickPeak(15.0);
            ctx.Check(ctx.SimMin >= 0.8, "simMin<0.8");
        }
    }
}
