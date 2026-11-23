using System.Collections.Generic;

namespace SKONanobotBuildAndRepairSystem.Testing.Scenarios
{
    /// <summary>
    /// SCN-BUDGET-006 (scan cost by grid size): one BaR in BoundingBox mode (area 200 m) next to a static grid of
    /// 1,000 / 2,000 / 5,000 / 10,000 blocks (20 slowly welded damaged blocks keep it scanning, the rest intact armor).
    /// Records the cost of the game lookups the scan uses (grids in the area, all blocks of a grid) and of the mod's
    /// own scan steps, for 45 s. BoundingBox exercises both lookups; the scan itself runs on a background thread.
    /// </summary>
    internal sealed class S99_ScanCost : TestScenarioBase
    {
        private const double Window = 45.0;
        private const int Damaged = 20;
        private const int RowWidth = 100;
        private static readonly string[] Methods =
        {
            "UpdateBeforeSimulation10_100",
            "ServerTryWeldingGrindingCollecting",
            "AsyncClusterScan",
            "AsyncAddBlocksOfBox",
            "SharedEntityCache.GetEntitiesInBox",
            "SharedGridBlockCache.GetBlocks",
            "SharedGridBlockCache.GetBlocksFiltered",
            "PreSortClusterCandidates",
            "ApplyClusterResultToSelf"
        };

        private readonly string _id;
        private readonly int _blocks;
        private TestRig _rig;

        public S99_ScanCost(string id, int blocks)
        {
            _id = id;
            _blocks = blocks;
        }

        public override string Id { get { return _id; } }
        public override string Description { get { return "Scan cost: 1 BaR scanning a " + _blocks + "-block grid (area mode), 45 s"; } }
        public override int TimeoutSeconds { get { return 120; } }
        public override int ReadyTimeoutSeconds { get { return 120; } }
        public override IList<string> PerfMethods { get { return Methods; } }

        public override void Setup(TestContext ctx)
        {
            ctx.SetModSettings(s =>
            {
                s.Welder.WorkSpeed = 1;
                s.Welder.WeldingMultiplier = 0.05f; // keep the damaged blocks around: the BaR keeps scanning
            });
            _rig = ctx.SpawnRig(new RigSpec { Name = _id, Bars = 1, DamagedArmor = Damaged, Armor = _blocks - Damaged, RowWidth = RowWidth });
        }

        protected override void Configure(TestContext ctx)
        {
            ctx.StockAllCargo(_rig, "SteelPlate", 1000);
            ctx.ConfigureBars(_rig, s =>
            {
                s.SearchMode = SearchModes.BoundingBox;
                s.WorkMode = WorkModes.WeldOnly;
                s.WeldOptions = AutoWeldOptions.WeldFull;
                s.AreaSize = new VRageMath.Vector3(200f, 200f, 200f);
            });
        }

        public override bool IsComplete(TestContext ctx)
        {
            return ctx.RunSeconds >= Window;
        }

        public override void Assert(TestContext ctx, TestResult result)
        {
            ctx.Metric("gridBlocks", (long)_blocks);
            // Main thread must stay cheap whatever the grid size; scan cost itself is recorded (background thread).
            ctx.Check(ctx.TickAvgMs <= 1.0, "tickAvgMs " + ctx.TickAvgMs.ToString("F3") + " > 1.0 on a " + _blocks + "-block grid");
            ctx.Check(ctx.SimMin >= 0.8, "simMin " + ctx.SimMin.ToString("F2") + " < 0.8");
        }
    }
}
