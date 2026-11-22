using System.Collections.Generic;
using VRageMath;

namespace SKONanobotBuildAndRepairSystem.Testing.Scenarios
{
    /// <summary>
    /// SCN-CLUSTER-002 (BUG-261006.1): 4 BaRs around one 80-block target with MaxSystemsPerTargetGrid=4;
    /// at 15 s the limit is lowered to 2 — surplus lock-ons are released, from 5 s later never more than 2
    /// BaRs count on the grid, and the work goes on.
    /// </summary>
    internal sealed class S54_LimitLowered : TestScenarioBase
    {
        private const double LowerAt = 15.0;
        private const double Grace = 5.0;
        private const double After = 20.0;

        private readonly List<TestRig> _rigs = new List<TestRig>();
        private TestRig _target;
        private bool _lowered;
        private double _loweredAt;
        private int _peakBefore;
        private int _peakAfter;
        private int _violationsAfter;
        private double _integrityAtLower;

        public override string Id { get { return "S54"; } }
        public override string Description { get { return "Per-grid limit lowered 4 -> 2 while working: surplus BaRs released, limit held"; } }

        public override void Setup(TestContext ctx)
        {
            _rigs.Clear();
            _lowered = false;
            _peakBefore = _peakAfter = _violationsAfter = 0;
            ctx.SetModSettings(s =>
            {
                s.Welder.WorkSpeed = 1; // slow, so all four are still busy when the limit drops
                s.MaxSystemsPerTargetGrid = 4;
                s.DisableLimitSystemsPerTargetGrid = false;
            });
            _target = ctx.SpawnRig(new RigSpec { Name = "S54-target", Bars = 0, DamagedArmor = 80 });
            var offsets = new[] { new Vector3D(30, 0, 30), new Vector3D(-30, 0, 30), new Vector3D(30, 0, -30), new Vector3D(-30, 0, -30) };
            for (int i = 0; i < offsets.Length; i++) _rigs.Add(ctx.SpawnRig(new RigSpec { Name = "S54-" + i, Bars = 1, Offset = offsets[i] }));
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
            Mod.GridSystemCount.TryGetValue(_target.Grid.EntityId, out onGrid);

            if (!_lowered)
            {
                if (onGrid > _peakBefore) _peakBefore = onGrid;
                if (ctx.RunSeconds >= LowerAt)
                {
                    _integrityAtLower = ctx.IntegritySum(_target.Targets);
                    ctx.SetModSettings(s => { s.MaxSystemsPerTargetGrid = 2; });
                    _lowered = true;
                    _loweredAt = ctx.RunSeconds;
                }
                return;
            }
            if (ctx.RunSeconds < _loweredAt + Grace) return;
            if (onGrid > _peakAfter) _peakAfter = onGrid;
            if (onGrid > 2) _violationsAfter++;
        }

        public override bool IsComplete(TestContext ctx)
        {
            return _lowered && ctx.RunSeconds >= _loweredAt + After;
        }

        public override void Assert(TestContext ctx, TestResult result)
        {
            var gainAfter = ctx.IntegritySum(_target.Targets) - _integrityAtLower;
            ctx.Metric("peakBefore", (long)_peakBefore);
            ctx.Metric("peakAfter", (long)_peakAfter);
            ctx.Metric("violationsAfter", (long)_violationsAfter);
            ctx.Metric("integrityGainAfter", gainAfter);
            ctx.Check(_peakBefore > 2, "only " + _peakBefore + " BaR(s) on the grid before lowering — nothing to release");
            ctx.Check(_violationsAfter == 0, "more than 2 BaRs on the grid after lowering (" + _violationsAfter + " samples, peak " + _peakAfter + ")");
            ctx.Check(gainAfter > 0.01, "no welding progress after the limit was lowered");
        }
    }
}
