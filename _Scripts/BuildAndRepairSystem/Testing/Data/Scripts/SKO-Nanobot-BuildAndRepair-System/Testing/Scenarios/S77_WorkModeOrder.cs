using VRageMath;

namespace SKONanobotBuildAndRepairSystem.Testing.Scenarios
{
    /// <summary>
    /// SCN-CORE-004: work-mode order with weld and grind work at the same time. 1 BaR (BoundingBox, janitor
    /// NoOwnership) with 6 damaged own blocks on its grid and a derelict 6-block grid 20 m away.
    /// S77 WeldBeforeGrind: the derelict is not ground while own blocks still need welding, then it is ground.
    /// S78 GrindBeforeWeld: own blocks are not welded while the derelict still has blocks, then they are welded.
    /// The loop prefers one activity and falls back to the other only in a cycle without it, so "out of order"
    /// progress is counted per sample and at most one hand-over sample is allowed.
    /// </summary>
    internal sealed class S77_WorkModeOrder : TestScenarioBase
    {
        private const int Blocks = 6;
        private const int MaxOutOfOrderSamples = 1;

        private readonly string _id;
        private readonly WorkModes _mode;
        private TestRig _rig;
        private TestRig _derelict;

        private double _ownLast = -1;
        private double _derelictLast = -1;
        private double _weldDoneAt = -1;
        private double _grindDoneAt = -1;
        private double _firstWeldAt = -1;
        private double _firstGrindAt = -1;
        private int _outOfOrder;

        public S77_WorkModeOrder(string id, WorkModes mode)
        {
            _id = id;
            _mode = mode;
        }

        public override string Id { get { return _id; } }
        public override string Description
        {
            get
            {
                return _mode == WorkModes.WeldBeforeGrind
                    ? "Work mode WeldBeforeGrind: own blocks are welded before the derelict is ground"
                    : "Work mode GrindBeforeWeld: the derelict is ground before own blocks are welded";
            }
        }
        public override int TimeoutSeconds { get { return 90; } }

        private bool WeldFirst { get { return _mode == WorkModes.WeldBeforeGrind; } }

        public override void Setup(TestContext ctx)
        {
            _ownLast = _derelictLast = _weldDoneAt = _grindDoneAt = _firstWeldAt = _firstGrindAt = -1;
            _outOfOrder = 0;
            ctx.SetModSettings(s =>
            {
                s.Welder.WorkSpeed = 10;
                s.Welder.AllowedGrindJanitorRelations |= AutoGrindRelation.NoOwnership;
            });
            _rig = ctx.SpawnRig(new RigSpec { Name = _id, Bars = 1, DamagedArmor = Blocks });
            _derelict = ctx.SpawnRig(new RigSpec
            {
                Name = _id + "-derelict", Bars = 0, OwnerId = 0,
                DamagedArmor = Blocks, DamagedBuildPercent = 1f, RowWidth = Blocks,
                Offset = new Vector3D(0, 0, 20)
            });
        }

        protected override void Configure(TestContext ctx)
        {
            ctx.StockAllCargo(_rig, "SteelPlate", 300);
            ctx.ConfigureBars(_rig, s =>
            {
                s.SearchMode = SearchModes.BoundingBox;
                s.WorkMode = _mode;
                s.WeldOptions = AutoWeldOptions.WeldFull;
                s.UseGrindJanitorOn = AutoGrindRelation.NoOwnership;
                s.GrindJanitorOptions = 0;
                s.AreaOffset = Vector3.Zero;
                s.AreaSize = new Vector3(100f, 100f, 100f);
            });
        }

        private bool DerelictGone(TestContext ctx)
        {
            return ScenarioUtil.AllGone(ctx, _derelict, _derelict.Targets);
        }

        public override void Sample(TestContext ctx)
        {
            var own = ctx.IntegritySum(_rig.Targets);
            var derelict = DerelictGone(ctx) ? 0 : ctx.IntegritySum(_derelict.Targets);
            if (_ownLast < 0) { _ownLast = own; _derelictLast = derelict; return; }

            var welded = own > _ownLast + 0.01;
            var ground = derelict < _derelictLast - 0.01;
            var now = ctx.RunSeconds;
            if (welded && _firstWeldAt < 0) _firstWeldAt = now;
            if (ground && _firstGrindAt < 0) _firstGrindAt = now;

            var weldPending = ctx.CountFullIntegrity(_rig.Targets) < _rig.Targets.Count;
            var grindPending = !DerelictGone(ctx);
            if (WeldFirst ? (ground && weldPending) : (welded && grindPending)) _outOfOrder++;

            if (_weldDoneAt < 0 && !weldPending) _weldDoneAt = now;
            if (_grindDoneAt < 0 && !grindPending) _grindDoneAt = now;
            _ownLast = own;
            _derelictLast = derelict;
        }

        public override bool IsComplete(TestContext ctx)
        {
            return (_weldDoneAt >= 0 && _grindDoneAt >= 0) || ctx.RunSeconds >= 75;
        }

        public override void Assert(TestContext ctx, TestResult result)
        {
            ctx.Metric("workMode", _mode.ToString());
            ctx.Metric("firstWeldS", _firstWeldAt);
            ctx.Metric("weldDoneS", _weldDoneAt);
            ctx.Metric("firstGrindS", _firstGrindAt);
            ctx.Metric("grindDoneS", _grindDoneAt);
            ctx.Metric("outOfOrderSamples", (long)_outOfOrder);
            ctx.Check(_weldDoneAt >= 0, "own blocks not all welded (" + ctx.CountFullIntegrity(_rig.Targets) + "/" + _rig.Targets.Count + ")");
            ctx.Check(_grindDoneAt >= 0, "derelict not ground away");
            ctx.Check(_outOfOrder <= MaxOutOfOrderSamples,
                (WeldFirst ? "derelict ground while own blocks still needed welding" : "own blocks welded while the derelict still had blocks") +
                " (" + _outOfOrder + " samples)");
            if (WeldFirst) ctx.Check(_firstGrindAt < 0 || _weldDoneAt < 0 || _firstGrindAt >= _weldDoneAt - 0.5, "grinding started " + (_weldDoneAt - _firstGrindAt).ToString("F1") + " s before welding finished");
            else ctx.Check(_firstWeldAt < 0 || _grindDoneAt < 0 || _firstWeldAt >= _grindDoneAt - 0.5, "welding started " + (_grindDoneAt - _firstWeldAt).ToString("F1") + " s before grinding finished");
        }
    }
}
