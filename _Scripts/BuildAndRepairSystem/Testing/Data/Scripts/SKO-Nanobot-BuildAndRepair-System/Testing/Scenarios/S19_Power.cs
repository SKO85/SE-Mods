namespace SKONanobotBuildAndRepairSystem.Testing.Scenarios
{
    /// <summary>
    /// SCN-CORE-003: S19 — rig battery off: the BaR is unpowered and welds nothing; battery switched on
    /// at 10 s: every block repaired. S20 — welding power demand (100 MW) above what the battery can give
    /// (12 MW): the BaR reports unpowered instead of welding as if powered; no exceptions.
    /// </summary>
    internal sealed class S19_Power : TestScenarioBase
    {
        private const double PowerOnAt = 10.0;
        private const double InsufficientWindow = 30.0;
        private const float HugeWeldingPowerMw = 100f;

        private readonly string _id;
        private readonly bool _insufficient;
        private TestRig _rig;
        private double _start = -1;
        private bool _sawUnpowered;
        private int _unpoweredSamples;
        private bool _progressWhileOff;
        private bool _poweredOn;

        public S19_Power(string id, bool insufficient)
        {
            _id = id;
            _insufficient = insufficient;
        }

        public override string Id { get { return _id; } }
        public override string Description
        {
            get
            {
                return _insufficient
                    ? "Power: welding needs more than the battery gives, the BaR reports unpowered"
                    : "Power: battery off, nothing welded; battery on at 10 s, everything repaired";
            }
        }
        public override int TimeoutSeconds { get { return _insufficient ? 40 : 120; } }

        public override void Setup(TestContext ctx)
        {
            _start = -1;
            _sawUnpowered = false;
            _unpoweredSamples = 0;
            _progressWhileOff = false;
            _poweredOn = false;
            // Power demand is registered when the BaR initialises, so set it before the rig spawns.
            ctx.SetModSettings(s =>
            {
                // S20 at normal speed: at WorkSpeed 10 a block is finished in one call, before the power
                // system reacts to the welding demand (first run: all welded, 3 unpowered samples).
                s.Welder.WorkSpeed = _insufficient ? 1 : 10;
                if (_insufficient) s.Welder.MaximumRequiredElectricPowerWelding = HugeWeldingPowerMw;
            });
            _rig = ctx.SpawnRig(new RigSpec { Name = _id, Bars = 1, DamagedArmor = 5, BatteryEnabled = _insufficient });
        }

        protected override void Configure(TestContext ctx)
        {
            ctx.StockCargo(_rig, "SteelPlate", 300);
            ctx.ConfigureBars(_rig, s =>
            {
                s.SearchMode = SearchModes.Grids;
                s.WorkMode = WorkModes.WeldBeforeGrind;
                s.WeldOptions = AutoWeldOptions.WeldFull;
            });
        }

        public override void Sample(TestContext ctx)
        {
            if (_start < 0) _start = ctx.IntegritySum(_rig.Targets);
            var welder = _rig.Bars.Count > 0 ? _rig.Bars[0].Welder : null;
            var unpowered = welder != null && welder.Enabled && !welder.IsWorking;
            if (unpowered) { _sawUnpowered = true; _unpoweredSamples++; }

            if (_insufficient || _poweredOn) return;
            if (ctx.RunSeconds < PowerOnAt)
            {
                if (ctx.IntegritySum(_rig.Targets) > _start + 0.01) _progressWhileOff = true;
                return;
            }
            if (_rig.Battery != null) _rig.Battery.Enabled = true;
            _poweredOn = true;
        }

        public override bool IsComplete(TestContext ctx)
        {
            if (_insufficient) return ctx.RunSeconds >= InsufficientWindow;
            return _poweredOn && ctx.CountFullIntegrity(_rig.Targets) == _rig.Targets.Count;
        }

        public override void Assert(TestContext ctx, TestResult result)
        {
            var welded = ctx.CountFullIntegrity(_rig.Targets);
            ctx.Metric("welded", (long)welded);
            ctx.Metric("unpoweredSamples", (long)_unpoweredSamples);
            ctx.Metric("integrityGain", ctx.IntegritySum(_rig.Targets) - _start);
            ctx.Check(_sawUnpowered, "the BaR never reported unpowered");

            if (_insufficient) return;
            ctx.Check(_rig.Battery != null, "rig battery not resolved");
            ctx.Check(!_progressWhileOff, "blocks were welded while the battery was off");
            ctx.Check(welded == _rig.Targets.Count, "welded=" + welded + "/" + _rig.Targets.Count + " after power came back");
        }
    }
}
