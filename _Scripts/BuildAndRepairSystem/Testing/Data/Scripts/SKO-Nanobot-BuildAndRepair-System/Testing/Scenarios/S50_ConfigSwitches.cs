using SKONanobotBuildAndRepairSystem.Handlers;
using System.Collections.Generic;
using VRageMath;

namespace SKONanobotBuildAndRepairSystem.Testing.Scenarios
{
    internal enum ConfigSwitch { LimitDisabled, AssignmentDisabled, WeldBudgetOne }

    /// <summary>
    /// SCN-CFG-003 / SCN-CLUSTER-002 / SCN-BUDGET-001/002: mod switches turned off or down.
    /// S50 DisableLimitSystemsPerTargetGrid with MaxSystemsPerTargetGrid=2 — more than 2 of 4 BaRs work the
    /// target grid at once. S51 AssignToSystemEnabled=false — 2 BaRs still finish 30 blocks, no claims are
    /// made. S52 MaxWeldsPerTick=1 — 5 BaRs never weld more than once per tick and still finish.
    /// </summary>
    internal sealed class S50_ConfigSwitches : TestScenarioBase
    {
        private readonly string _id;
        private readonly ConfigSwitch _switch;
        private readonly List<TestRig> _rigs = new List<TestRig>();
        private TestRig _target;
        private int _peakOnTarget;

        public S50_ConfigSwitches(string id, ConfigSwitch configSwitch)
        {
            _id = id;
            _switch = configSwitch;
        }

        public override string Id { get { return _id; } }
        public override string Description
        {
            get
            {
                switch (_switch)
                {
                    case ConfigSwitch.LimitDisabled: return "DisableLimitSystemsPerTargetGrid: more than 2 BaRs work one grid (limit 2 ignored)";
                    case ConfigSwitch.AssignmentDisabled: return "AssignToSystemEnabled=false: 2 BaRs finish 30 blocks without claims";
                    default: return "MaxWeldsPerTick=1: 5 BaRs weld at most once per tick and finish";
                }
            }
        }
        public override int TimeoutSeconds { get { return 180; } }

        public override void Setup(TestContext ctx)
        {
            _rigs.Clear();
            _peakOnTarget = 0;
            ctx.SetModSettings(s =>
            {
                s.Welder.WorkSpeed = 10;
                if (_switch == ConfigSwitch.LimitDisabled)
                {
                    s.MaxSystemsPerTargetGrid = 2;
                    s.DisableLimitSystemsPerTargetGrid = true;
                }
                if (_switch == ConfigSwitch.AssignmentDisabled) s.AssignToSystemEnabled = false;
                if (_switch == ConfigSwitch.WeldBudgetOne) s.MaxWeldsPerTick = 1;
            });

            if (_switch == ConfigSwitch.LimitDisabled)
            {
                _target = ctx.SpawnRig(new RigSpec { Name = _id + "-target", Bars = 0, DamagedArmor = 40 });
                var offsets = new[] { new Vector3D(30, 0, 30), new Vector3D(-30, 0, 30), new Vector3D(30, 0, -30), new Vector3D(-30, 0, -30) };
                for (int i = 0; i < offsets.Length; i++) _rigs.Add(ctx.SpawnRig(new RigSpec { Name = _id + "-" + i, Bars = 1, Offset = offsets[i] }));
                return;
            }
            var bars = _switch == ConfigSwitch.AssignmentDisabled ? 2 : 5;
            var blocks = _switch == ConfigSwitch.AssignmentDisabled ? 30 : 20;
            _target = ctx.SpawnRig(new RigSpec { Name = _id, Bars = bars, DamagedArmor = blocks });
            _rigs.Add(_target);
        }

        protected override void Configure(TestContext ctx)
        {
            for (int i = 0; i < _rigs.Count; i++)
            {
                ctx.StockAllCargo(_rigs[i], "SteelPlate", 500);
                ctx.ConfigureBars(_rigs[i], s =>
                {
                    s.SearchMode = _switch == ConfigSwitch.LimitDisabled ? SearchModes.BoundingBox : SearchModes.Grids;
                    s.WorkMode = WorkModes.WeldBeforeGrind;
                    s.WeldOptions = AutoWeldOptions.WeldFull;
                });
            }
        }

        public override void Sample(TestContext ctx)
        {
            if (_switch != ConfigSwitch.LimitDisabled || _target.Grid == null) return;
            var n = 0;
            for (int r = 0; r < _rigs.Count; r++)
            {
                for (int b = 0; b < _rigs[r].Bars.Count; b++)
                {
                    var block = _rigs[r].Bars[b].State.CurrentWeldingBlock;
                    if (block != null && block.CubeGrid == _target.Grid) n++;
                }
            }
            if (n > _peakOnTarget) _peakOnTarget = n;
        }

        public override bool IsComplete(TestContext ctx)
        {
            return ctx.CountFullIntegrity(_target.Targets) == _target.Targets.Count;
        }

        public override void Assert(TestContext ctx, TestResult result)
        {
            var welded = ctx.CountFullIntegrity(_target.Targets);
            ctx.Metric("welded", (long)welded);
            ctx.Check(welded == _target.Targets.Count, "welded=" + welded + "/" + _target.Targets.Count);
            switch (_switch)
            {
                case ConfigSwitch.LimitDisabled:
                    ctx.Metric("peakBarsOnTarget", (long)_peakOnTarget);
                    ctx.Check(_peakOnTarget > 2, "limit disabled but at most " + _peakOnTarget + " BaRs worked the grid together");
                    break;
                case ConfigSwitch.AssignmentDisabled:
                    ctx.Check(ctx.AssignmentsPeak == 0, "claims made although AssignToSystemEnabled=false (peak " + ctx.AssignmentsPeak + ")");
                    break;
                default:
                    ctx.Check(ctx.WeldBudgetPeak <= 1, "weldBudgetPeak=" + ctx.WeldBudgetPeak + " with MaxWeldsPerTick=1");
                    break;
            }
        }
    }
}
