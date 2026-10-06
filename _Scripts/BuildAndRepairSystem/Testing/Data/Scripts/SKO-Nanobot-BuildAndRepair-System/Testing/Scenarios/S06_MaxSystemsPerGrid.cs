using System.Collections.Generic;
using VRageMath;

namespace SKONanobotBuildAndRepairSystem.Testing.Scenarios
{
    /// <summary>SCN-CLUSTER-002: MaxSystemsPerTargetGrid=2 with four BaRs around one target grid — never exceeded, never deadlocked.</summary>
    internal sealed class S06_MaxSystemsPerGrid : TestScenarioBase
    {
        private const int Limit = 2;
        private readonly List<TestRig> _rigs = new List<TestRig>();
        private TestRig _target;
        private int _violations;
        private int _peakOnGrid;
        private bool _sawLimitsExceeded;

        public override string Id { get { return "S06"; } }
        public override string Description { get { return "MaxSystemsPerTargetGrid=2, four BaRs, one 40-block target: limit held, grid still welded"; } }
        public override int TimeoutSeconds { get { return 240; } }

        public override void Setup(TestContext ctx)
        {
            _rigs.Clear();
            _violations = 0;
            _peakOnGrid = 0;
            _sawLimitsExceeded = false;

            ctx.SetModSettings(s =>
            {
                s.Welder.WorkSpeed = 10;
                s.MaxSystemsPerTargetGrid = Limit;
                s.DisableLimitSystemsPerTargetGrid = false;
            });

            _target = ctx.SpawnRig(new RigSpec { Name = "S06-target", Bars = 0, DamagedArmor = 40 });
            var offsets = new[]
            {
                new Vector3D(60, 0, 60), new Vector3D(-60, 0, 60),
                new Vector3D(60, 0, -60), new Vector3D(-60, 0, -60)
            };
            for (int i = 0; i < offsets.Length; i++)
                _rigs.Add(ctx.SpawnRig(new RigSpec { Name = "S06-" + i, Bars = 1, Offset = offsets[i] }));
        }

        protected override void Configure(TestContext ctx)
        {
            for (int i = 0; i < _rigs.Count; i++)
            {
                ctx.StockCargo(_rigs[i], "SteelPlate", 1000);
                ctx.ConfigureBars(_rigs[i], s =>
                {
                    s.SearchMode = SearchModes.BoundingBox;
                    s.WorkMode = WorkModes.WeldBeforeGrind;
                    s.WeldOptions = AutoWeldOptions.WeldFull;
                });
            }
        }

        public override void Sample(TestContext ctx)
        {
            if (_target.Grid == null) return;
            int onGrid;
            if (Mod.GridSystemCount.TryGetValue(_target.Grid.EntityId, out onGrid))
            {
                if (onGrid > _peakOnGrid) _peakOnGrid = onGrid;
                if (onGrid > Limit) _violations++;
            }
            if (!_sawLimitsExceeded)
            {
                for (int i = 0; i < _rigs.Count; i++)
                {
                    var bars = _rigs[i].Bars;
                    for (int b = 0; b < bars.Count; b++)
                        if (bars[b].State.LimitsExceeded) { _sawLimitsExceeded = true; break; }
                    if (_sawLimitsExceeded) break;
                }
            }
        }

        public override bool IsComplete(TestContext ctx)
        {
            return ctx.CountFullIntegrity(_target.Targets) == _target.Targets.Count;
        }

        public override void Assert(TestContext ctx, TestResult result)
        {
            ctx.Metric("violations", (long)_violations);
            ctx.Metric("peakOnGrid", (long)_peakOnGrid);
            ctx.Metric("sawLimitsExceeded", _sawLimitsExceeded);
            ctx.Check(_violations == 0, "GridSystemCount exceeded " + Limit + " in " + _violations + " ticks (peak " + _peakOnGrid + ")");
            ctx.Check(_sawLimitsExceeded, "no BaR ever reported LimitsExceeded");

            var welded = ctx.CountFullIntegrity(_target.Targets);
            ctx.Metric("welded", (long)welded);
            ctx.Check(welded == _target.Targets.Count, "welded=" + welded + "/" + _target.Targets.Count + " (limit deadlock?)");

            ctx.Check(ctx.TickPeakMs <= 10.0, "tickPeakMs>10");
        }
    }
}
