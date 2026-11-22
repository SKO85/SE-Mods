using VRage.Game;

namespace SKONanobotBuildAndRepairSystem.Testing.Scenarios
{
    /// <summary>
    /// SCN-CORE-001: damage detection. 5 blocks repaired, then damaged again (60 % of max integrity, bullet
    /// damage) twice — 3 s after the repair, and after a 30 s idle. Each time the BaR must notice and repair them
    /// again. The game applies damage on the grid's next update, so the lowest integrity seen after the hit is the
    /// baseline; "detected" = first rise above it. New damage ends the empty-grid skip and the idle scan backoff
    /// (BUG-261122.10), so detection must happen within one normal scan interval plus slack.
    /// </summary>
    internal sealed class S57_Redamage : TestScenarioBase
    {
        private static readonly double[] IdleBefore = { 3.0, 30.0 };
        private const double ScanSlack = 3.0;
        private const float DamageRatio = 0.6f;

        private TestRig _rig;
        private int _cycle;           // damage cycles done so far
        private bool _waitingRepair;  // true until the current repair is complete
        private bool _damageSeen;     // integrity dropped after the current hit
        private double _doneAt;
        private double _damagedAt;
        private double _fullSum;
        private double _minAfterDamage;
        private readonly double[] _detect = { -1, -1 };
        private readonly double[] _repair = { -1, -1 };

        public override string Id { get { return "S57"; } }
        public override string Description { get { return "Damage detection: repaired blocks damaged again (after 3 s and after 30 s idle) are repaired again"; } }
        public override int TimeoutSeconds { get { return 240; } }

        private static double DetectLimit { get { return Mod.Settings.TargetsUpdateInterval.TotalSeconds + ScanSlack; } }

        public override void Setup(TestContext ctx)
        {
            _cycle = 0;
            _waitingRepair = true;
            _damageSeen = false;
            _doneAt = -1;
            _detect[0] = _detect[1] = _repair[0] = _repair[1] = -1;
            ctx.SetModSettings(s => { s.Welder.WorkSpeed = 10; });
            _rig = ctx.SpawnRig(new RigSpec { Name = "S57", Bars = 1, DamagedArmor = 5 });
        }

        protected override void Configure(TestContext ctx)
        {
            ctx.StockAllCargo(_rig, "SteelPlate", 500);
            ctx.ConfigureBars(_rig, s =>
            {
                s.SearchMode = SearchModes.Grids;
                s.WorkMode = WorkModes.WeldBeforeGrind;
                s.WeldOptions = AutoWeldOptions.WeldFull;
            });
        }

        public override void Sample(TestContext ctx)
        {
            var sum = ctx.IntegritySum(_rig.Targets);
            var full = ctx.CountFullIntegrity(_rig.Targets) == _rig.Targets.Count;
            if (_waitingRepair)
            {
                if (_cycle > 0)
                {
                    var c = _cycle - 1;
                    if (!_damageSeen)
                    {
                        if (sum >= _fullSum - 0.01) return; // damage not applied yet
                        _damageSeen = true;
                        _minAfterDamage = sum;
                    }
                    if (sum < _minAfterDamage) _minAfterDamage = sum;
                    else if (_detect[c] < 0 && sum > _minAfterDamage + 0.01) _detect[c] = ctx.RunSeconds - _damagedAt;
                    if (!full) return;
                    _repair[c] = ctx.RunSeconds - _damagedAt;
                }
                else if (!full) return;
                _waitingRepair = false;
                _doneAt = ctx.RunSeconds;
                _fullSum = sum;
                return;
            }
            if (_cycle >= IdleBefore.Length || ctx.RunSeconds < _doneAt + IdleBefore[_cycle]) return;

            // Damage every target again, below destruction (applied by the game on the grid's next update).
            for (int i = 0; i < _rig.Targets.Count; i++)
            {
                var b = _rig.Targets[i];
                if (b != null && !b.IsDestroyed) b.DoDamage(b.MaxIntegrity * DamageRatio, MyDamageType.Bullet, true);
            }
            _damagedAt = ctx.RunSeconds;
            _damageSeen = false;
            _cycle++;
            _waitingRepair = true;
        }

        public override bool IsComplete(TestContext ctx)
        {
            return _cycle >= IdleBefore.Length && !_waitingRepair;
        }

        public override void Assert(TestContext ctx, TestResult result)
        {
            ctx.Metric("emptyGridRescanDelayS", (long)Mod.Settings.EmptyGridRescanDelaySeconds);
            ctx.Metric("detectLimitS", DetectLimit);
            for (int i = 0; i < IdleBefore.Length; i++)
            {
                ctx.Metric("cycle" + (i + 1) + "IdleS", IdleBefore[i]);
                ctx.Metric("cycle" + (i + 1) + "DetectS", _detect[i]);
                ctx.Metric("cycle" + (i + 1) + "RepairS", _repair[i]);
                ctx.Check(i < _cycle, "cycle " + (i + 1) + ": not reached");
                ctx.Check(_detect[i] >= 0 && _detect[i] <= DetectLimit, "cycle " + (i + 1) + ": re-damage not detected within " + DetectLimit + " s");
                ctx.Check(_repair[i] >= 0, "cycle " + (i + 1) + ": not repaired again");
            }
            ctx.Check(ctx.CountExisting(_rig.Targets) == _rig.Targets.Count, "a block was destroyed by the test damage");
        }
    }
}
