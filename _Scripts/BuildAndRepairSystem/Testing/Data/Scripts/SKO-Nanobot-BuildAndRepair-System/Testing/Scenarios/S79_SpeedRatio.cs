using VRageMath;

namespace SKONanobotBuildAndRepairSystem.Testing.Scenarios
{
    /// <summary>
    /// SCN-BUDGET-009: the speed settings do what they say. One BaR works 40 blocks (S79: own damaged -> welding,
    /// S80: derelict intact 15 m away -> janitor grinding) through three phases, switching mod settings live:
    /// A WorkSpeed 10 / multiplier 0.1 (8 s), B WorkSpeed 1 / 0.1 (15 s), C WorkSpeed 10 / 0.2 (8 s); 1 s settle
    /// after each switch. The low multiplier makes a block take many actions so the rate is measurable.
    /// Pass: A/B ~10 (WorkSpeed = actions per second) and C/A ~2 (multiplier = work per action), within tolerance.
    /// Records the absolute rates (integrity per second) as a speed baseline.
    /// </summary>
    internal sealed class S79_SpeedRatio : TestScenarioBase
    {
        private const int Blocks = 40;
        private const double Settle = 1.0;
        private static readonly int[] PhaseWorkSpeed = { 10, 1, 10 };
        private static readonly float[] PhaseMultiplier = { 0.1f, 0.1f, 0.2f };
        private static readonly double[] PhaseSeconds = { 8.0, 15.0, 8.0 };

        private readonly string _id;
        private readonly bool _grind;
        private TestRig _bar;
        private TestRig _target;
        private int _phase;          // 0..2, 3 = done
        private double _phaseStart;
        private double _measureFrom = -1;
        private double _integrityFrom;
        private readonly double[] _rate = { -1, -1, -1 };
        private bool _ranOut;
        private bool _workStarted;
        private double _startIntegrity = -1;

        public S79_SpeedRatio(string id, bool grind)
        {
            _id = id;
            _grind = grind;
        }

        public override string Id { get { return _id; } }
        public override string Description
        {
            get
            {
                return _grind
                    ? "Grind speed: WorkSpeed 10 vs 1 is ~10x, GrindingMultiplier 0.2 vs 0.1 is ~2x"
                    : "Weld speed: WorkSpeed 10 vs 1 is ~10x, WeldingMultiplier 0.2 vs 0.1 is ~2x";
            }
        }
        public override int TimeoutSeconds { get { return 90; } }

        private void Apply(TestContext ctx, int phase)
        {
            ctx.SetModSettings(s =>
            {
                s.Welder.WorkSpeed = PhaseWorkSpeed[phase];
                if (_grind) s.Welder.GrindingMultiplier = PhaseMultiplier[phase];
                else s.Welder.WeldingMultiplier = PhaseMultiplier[phase];
            });
        }

        public override void Setup(TestContext ctx)
        {
            _phase = 0;
            _measureFrom = -1;
            _ranOut = false;
            _workStarted = false;
            _startIntegrity = -1;
            _rate[0] = _rate[1] = _rate[2] = -1;
            Apply(ctx, 0);
            ctx.SetModSettings(s => { s.Welder.AllowedGrindJanitorRelations |= AutoGrindRelation.NoOwnership; });
            if (_grind)
            {
                _bar = ctx.SpawnRig(new RigSpec { Name = _id, Bars = 1 });
                _target = ctx.SpawnRig(new RigSpec
                {
                    Name = _id + "-derelict", Bars = 0, OwnerId = 0,
                    DamagedArmor = Blocks, DamagedBuildPercent = 1f, RowWidth = 10,
                    Offset = new Vector3D(0, 0, 15)
                });
            }
            else
            {
                _bar = ctx.SpawnRig(new RigSpec { Name = _id, Bars = 1, DamagedArmor = Blocks });
                _target = _bar;
            }
        }

        protected override void Configure(TestContext ctx)
        {
            ctx.StockAllCargo(_bar, "SteelPlate", 1000);
            ctx.ConfigureBars(_bar, s =>
            {
                s.SearchMode = _grind ? SearchModes.BoundingBox : SearchModes.Grids;
                s.WorkMode = _grind ? WorkModes.GrindOnly : WorkModes.WeldOnly;
                s.WeldOptions = AutoWeldOptions.WeldFull;
                s.UseGrindJanitorOn = _grind ? AutoGrindRelation.NoOwnership : 0;
                s.GrindJanitorOptions = 0;
                s.AreaOffset = Vector3.Zero;
                s.AreaSize = new Vector3(100f, 100f, 100f);
            });
        }

        private double Integrity(TestContext ctx)
        {
            return ScenarioUtil.AllGone(ctx, _target, _target.Targets) ? 0 : ctx.IntegritySum(_target.Targets);
        }

        private bool WorkLeft(TestContext ctx)
        {
            return _grind ? !ScenarioUtil.AllGone(ctx, _target, _target.Targets) : ctx.CountFullIntegrity(_target.Targets) < _target.Targets.Count;
        }

        public override void Sample(TestContext ctx)
        {
            if (_phase > 2) return;
            var now = ctx.RunSeconds;
            if (_measureFrom < 0)
            {
                // First phase: wait until work has started (transport, first action); then settle before measuring.
                if (_phase == 0 && !_workStarted)
                {
                    var integrity = Integrity(ctx);
                    if (_startIntegrity < 0) { _startIntegrity = integrity; return; }
                    if (System.Math.Abs(integrity - _startIntegrity) < 0.01) return;
                    _workStarted = true;
                    _phaseStart = now;
                    return;
                }
                if (now < _phaseStart + Settle) return;
                _measureFrom = now;
                _integrityFrom = Integrity(ctx);
                return;
            }
            if (!WorkLeft(ctx)) { _ranOut = true; _phase = 3; return; }
            if (now < _measureFrom + PhaseSeconds[_phase]) return;

            _rate[_phase] = System.Math.Abs(Integrity(ctx) - _integrityFrom) / (now - _measureFrom);
            _phase++;
            _measureFrom = -1;
            _phaseStart = now;
            if (_phase <= 2) Apply(ctx, _phase);
        }

        public override bool IsComplete(TestContext ctx)
        {
            return _phase > 2 || ctx.RunSeconds >= 80;
        }

        public override void Assert(TestContext ctx, TestResult result)
        {
            var ratioWorkSpeed = _rate[0] > 0 && _rate[1] > 0 ? _rate[0] / _rate[1] : -1;
            var ratioMultiplier = _rate[0] > 0 && _rate[2] > 0 ? _rate[2] / _rate[0] : -1;
            ctx.Metric("rateWs10M01", _rate[0]);
            ctx.Metric("rateWs1M01", _rate[1]);
            ctx.Metric("rateWs10M02", _rate[2]);
            ctx.Metric("ratioWorkSpeed", ratioWorkSpeed);
            ctx.Metric("ratioMultiplier", ratioMultiplier);
            ctx.Check(!_ranOut, "ran out of " + (_grind ? "grind" : "weld") + " work before all phases were measured (rates " + _rate[0].ToString("F0") + "/" + _rate[1].ToString("F0") + "/" + _rate[2].ToString("F0") + ")");
            ctx.Check(_rate[0] > 0 && _rate[1] > 0 && _rate[2] > 0, "a phase made no progress");
            ctx.Check(ratioWorkSpeed >= 6 && ratioWorkSpeed <= 14, "WorkSpeed 10 vs 1 gave x" + ratioWorkSpeed.ToString("F1") + " (expected ~10)");
            ctx.Check(ratioMultiplier >= 1.5 && ratioMultiplier <= 2.5, (_grind ? "GrindingMultiplier" : "WeldingMultiplier") + " 0.2 vs 0.1 gave x" + ratioMultiplier.ToString("F2") + " (expected ~2)");
        }
    }
}
