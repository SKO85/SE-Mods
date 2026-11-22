using VRageMath;

namespace SKONanobotBuildAndRepairSystem.Testing.Scenarios
{
    /// <summary>
    /// SCN-WELD-001: WorkMode WeldOnly with the janitor (NoOwnership) configured: the own damaged blocks
    /// are welded and a derelict grid in range is never ground.
    /// </summary>
    internal sealed class S26_WeldOnlyNoGrind : TestScenarioBase
    {
        private const double SettleSeconds = 10.0;

        private TestRig _rig;
        private TestRig _derelict;
        private double _derelictStart = -1;
        private double _doneAt = -1;

        public override string Id { get { return "S26"; } }
        public override string Description { get { return "WeldOnly: own blocks welded, derelict grid in range never ground"; } }

        public override void Setup(TestContext ctx)
        {
            _derelictStart = -1;
            _doneAt = -1;
            ctx.SetModSettings(s =>
            {
                s.Welder.WorkSpeed = 10;
                s.Welder.AllowedGrindJanitorRelations |= AutoGrindRelation.NoOwnership;
            });
            _rig = ctx.SpawnRig(new RigSpec { Name = "S26", Bars = 1, DamagedArmor = 3 });
            _derelict = ctx.SpawnRig(new RigSpec { Name = "S26-derelict", Bars = 0, DamagedArmor = 3, DamagedBuildPercent = 1f, OwnerId = 0, Offset = new Vector3D(0, 0, -25) });
        }

        protected override void Configure(TestContext ctx)
        {
            ctx.StockCargo(_rig, "SteelPlate", 300);
            ctx.ConfigureBars(_rig, s =>
            {
                s.SearchMode = SearchModes.BoundingBox;
                s.WorkMode = WorkModes.WeldOnly;
                s.WeldOptions = AutoWeldOptions.WeldFull;
                s.UseGrindJanitorOn = AutoGrindRelation.NoOwnership;
                s.GrindJanitorOptions = 0;
            });
        }

        public override void Sample(TestContext ctx)
        {
            if (_derelictStart < 0) _derelictStart = ctx.IntegritySum(_derelict.Targets);
            if (_doneAt < 0 && ctx.CountFullIntegrity(_rig.Targets) == _rig.Targets.Count) _doneAt = ctx.RunSeconds;
        }

        public override bool IsComplete(TestContext ctx)
        {
            return _doneAt >= 0 && ctx.RunSeconds >= _doneAt + SettleSeconds;
        }

        public override void Assert(TestContext ctx, TestResult result)
        {
            var welded = ctx.CountFullIntegrity(_rig.Targets);
            var left = ScenarioUtil.AllGone(ctx, _derelict, _derelict.Targets) ? 0 : ctx.CountExisting(_derelict.Targets);
            ctx.Metric("welded", (long)welded);
            ctx.Metric("derelictLeft", (long)left);
            ctx.Check(welded == _rig.Targets.Count, "own welded=" + welded + "/" + _rig.Targets.Count);
            ctx.Check(left == _derelict.Targets.Count && ctx.IntegritySum(_derelict.Targets) >= _derelictStart - 0.01, "WeldOnly ground the derelict grid");
        }
    }
}
