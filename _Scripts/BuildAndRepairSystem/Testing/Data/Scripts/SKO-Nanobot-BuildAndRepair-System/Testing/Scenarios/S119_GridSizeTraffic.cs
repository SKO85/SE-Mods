using SKONanobotBuildAndRepairSystem.Models;
using System.Collections.Generic;
using VRageMath;

namespace SKONanobotBuildAndRepairSystem.Testing.Scenarios
{
    /// <summary>
    /// FEAT-261123.7 / SCN-BUDGET-010: CPU and network spikes by target grid size. 10 BaRs (BoundingBox, 100 m area,
    /// WeldOnly, WorkSpeed 3, 3,000 SteelPlate) above damaged armor fields: S119 small (100 blocks), S120 medium (1,000),
    /// S121 large (3,000), S122 very large (8,000), S123 all four at once (stacked 6 m apart). 40 s each. Records tick
    /// peak / avg, sim, scan cost and the net.* traffic incl. per-second peaks; checks are loose envelopes (data first).
    /// </summary>
    internal sealed class S119_GridSizeTraffic : TestScenarioBase
    {
        private const double Window = 40.0;
        private readonly string _id;
        private readonly int[] _sizes;
        private TestRig _rig;
        private readonly List<TestRig> _targets = new List<TestRig>();

        public S119_GridSizeTraffic(string id, params int[] sizes)
        {
            _id = id;
            _sizes = sizes;
        }

        public override string Id { get { return _id; } }
        public override string Description
        {
            get
            {
                return _sizes.Length == 1
                    ? "Traffic / spikes: 10 BaRs welding a " + _sizes[0] + "-block grid"
                    : "Traffic / spikes: 10 BaRs welding 100 + 1,000 + 3,000 + 8,000-block grids at once";
            }
        }
        public override int TimeoutSeconds { get { return 120; } }
        public override int ReadyTimeoutSeconds { get { return 120; } }

        public override void Setup(TestContext ctx)
        {
            ctx.SetModSettings(s => { s.Welder.WorkSpeed = 3; });
            _rig = ctx.SpawnRig(new RigSpec { Name = _id, Bars = 10 });
            _targets.Clear();
            for (int i = 0; i < _sizes.Length; i++)
            {
                _targets.Add(ctx.SpawnRig(new RigSpec
                {
                    Name = _id + "-grid" + _sizes[i],
                    Bars = 0,
                    DamagedArmor = _sizes[i],
                    RowWidth = 40,
                    Offset = new Vector3D(-50, -6 - 6 * i, -50)
                }));
            }
        }

        protected override void Configure(TestContext ctx)
        {
            ctx.StockCargo(_rig, "SteelPlate", 3000);
            ctx.ConfigureBars(_rig, s =>
            {
                s.SearchMode = SearchModes.BoundingBox;
                s.WorkMode = WorkModes.WeldOnly;
                s.AreaSize = new Vector3(100f, 100f, 100f);
            });
        }

        public override bool IsComplete(TestContext ctx)
        {
            return ctx.RunSeconds >= Window;
        }

        public override void Assert(TestContext ctx, TestResult result)
        {
            var blocks = 0;
            for (int i = 0; i < _sizes.Length; i++) blocks += _sizes[i];
            ctx.Metric("targetBlocks", (long)blocks);
            var welded = 0;
            for (int t = 0; t < _targets.Count; t++) welded += ctx.CountFullIntegrity(_targets[t].Targets);
            ctx.Metric("welded", (long)welded);
            long listed = 0, maxListed = 0;
            for (int b = 0; b < _rig.Bars.Count; b++)
            {
                var n = _rig.Bars[b].State.PossibleWeldTargets.CurrentCount;
                listed += n;
                if (n > maxListed) maxListed = n;
            }
            ctx.Metric("weldTargetsListedTotal", listed);
            ctx.Metric("weldTargetsListedMax", maxListed);
            ctx.Metric("weldedPerSecond", welded / Window);

            ctx.Check(ctx.SimMin >= 0.5, "sim speed dropped to " + ctx.SimMin.ToString("F2"));
            ctx.Check(ctx.TickAvgMs <= 2.0, "tick avg " + ctx.TickAvgMs.ToString("F2") + " ms > 2 ms");
            ctx.Check(welded > 0, "nothing welded in " + Window + " s");
            if (ctx.RemotePlayers() > 0)
            {
                long m, b, w; int mx;
                ctx.NetSent(40104, out m, out b, out w, out mx);
                ctx.Check(mx <= 64 * 1024, "largest state message " + mx + " bytes > 64 KB");
            }
        }
    }
}
