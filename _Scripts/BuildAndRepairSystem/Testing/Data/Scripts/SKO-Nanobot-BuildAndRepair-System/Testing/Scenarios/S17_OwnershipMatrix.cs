using Sandbox.ModAPI;
using VRageMath;

namespace SKONanobotBuildAndRepairSystem.Testing.Scenarios
{
    /// <summary>
    /// SCN-GRIND-002 / SCN-GRIND-001: default janitor (Enemies + NoOwnership), WeldBeforeGrind, four
    /// damaged grids in range: owned by the caller (battery) → welded; armor-only built by the caller
    /// (no owner, BUG-261122.7) → welded; derelict (built by nobody) → ground; pirate-owned (SPRT,
    /// skipped when the world has no pirates) → ground, never welded.
    /// </summary>
    internal sealed class S17_OwnershipMatrix : TestScenarioBase
    {
        private TestRig _rig;
        private TestRig _own;
        private TestRig _ownBuilt;
        private TestRig _derelict;
        private TestRig _enemy;

        public override string Id { get { return "S17"; } }
        public override string Description { get { return "Ownership: own and own-built grids welded, derelict and pirate grids ground"; } }
        public override int TimeoutSeconds { get { return 180; } }

        public override void Setup(TestContext ctx)
        {
            _enemy = null;
            ctx.SetModSettings(s =>
            {
                s.Welder.WorkSpeed = 10;
                s.Welder.AllowedGrindJanitorRelations |= AutoGrindRelation.NoOwnership | AutoGrindRelation.Enemies;
            });
            _rig = ctx.SpawnRig(new RigSpec { Name = "S17", Bars = 1 });

            var own = new RigSpec { Name = "S17-own", Bars = 0, DamagedArmor = 3, Offset = new Vector3D(30, 0, 0) };
            own.Extra.Add(new RigBlock { Kind = RigBlockKind.Battery, Position = new Vector3I(0, 0, 1), Tag = "battery" });
            _own = ctx.SpawnRig(own);

            _ownBuilt = ctx.SpawnRig(new RigSpec { Name = "S17-ownBuilt", Bars = 0, DamagedArmor = 3, Offset = new Vector3D(-30, 0, 0) });
            _derelict = ctx.SpawnRig(new RigSpec { Name = "S17-derelict", Bars = 0, DamagedArmor = 3, OwnerId = 0, Offset = new Vector3D(0, 0, -30) });

            var pirates = MyAPIGateway.Session.Factions != null ? MyAPIGateway.Session.Factions.TryGetFactionByTag("SPRT") : null;
            if (pirates != null && pirates.FounderId != 0)
            {
                var enemy = new RigSpec { Name = "S17-enemy", Bars = 0, DamagedArmor = 3, OwnerId = pirates.FounderId, Offset = new Vector3D(0, 0, 30) };
                enemy.Extra.Add(new RigBlock { Kind = RigBlockKind.Battery, Position = new Vector3I(0, 0, 1), Tag = "battery" });
                _enemy = ctx.SpawnRig(enemy);
            }
        }

        protected override void Configure(TestContext ctx)
        {
            ctx.StockAllCargo(_rig, "SteelPlate", 200);
            ctx.ConfigureBars(_rig, s =>
            {
                s.SearchMode = SearchModes.BoundingBox;
                s.WorkMode = WorkModes.WeldBeforeGrind;
                s.WeldOptions = AutoWeldOptions.WeldFull;
                s.UseGrindJanitorOn = AutoGrindRelation.Enemies | AutoGrindRelation.NoOwnership;
                s.GrindJanitorOptions = 0;
            });
        }

        public override bool IsComplete(TestContext ctx)
        {
            return ctx.CountFullIntegrity(_own.Targets) == _own.Targets.Count
                && ctx.CountFullIntegrity(_ownBuilt.Targets) == _ownBuilt.Targets.Count
                && ScenarioUtil.AllGone(ctx, _derelict, _derelict.Targets)
                && (_enemy == null || ScenarioUtil.AllGone(ctx, _enemy, _enemy.Targets));
        }

        public override void Assert(TestContext ctx, TestResult result)
        {
            var own = ctx.CountFullIntegrity(_own.Targets);
            var ownBuilt = ctx.CountFullIntegrity(_ownBuilt.Targets);
            var derelictLeft = ScenarioUtil.AllGone(ctx, _derelict, _derelict.Targets) ? 0 : ctx.CountExisting(_derelict.Targets);
            ctx.Metric("ownWelded", (long)own);
            ctx.Metric("ownBuiltWelded", (long)ownBuilt);
            ctx.Metric("derelictLeft", (long)derelictLeft);
            ctx.Check(own == _own.Targets.Count && ctx.CountExisting(_own.Targets) == _own.Targets.Count, "own grid: welded=" + own + "/" + _own.Targets.Count);
            ctx.Check(ownBuilt == _ownBuilt.Targets.Count && ctx.CountExisting(_ownBuilt.Targets) == _ownBuilt.Targets.Count,
                "own-built unowned grid: welded=" + ownBuilt + "/" + _ownBuilt.Targets.Count + ", left=" + ctx.CountExisting(_ownBuilt.Targets) + " (ground by the janitor?)");
            ctx.Check(derelictLeft == 0, "derelict grid: " + derelictLeft + " blocks left");

            if (_enemy == null)
            {
                ctx.Metric("enemy", "skipped (no SPRT faction)");
                return;
            }
            var enemyLeft = ScenarioUtil.AllGone(ctx, _enemy, _enemy.Targets) ? 0 : ctx.CountExisting(_enemy.Targets);
            ctx.Metric("enemyLeft", (long)enemyLeft);
            ctx.Check(enemyLeft == 0, "pirate grid: " + enemyLeft + " blocks left");
        }
    }
}
