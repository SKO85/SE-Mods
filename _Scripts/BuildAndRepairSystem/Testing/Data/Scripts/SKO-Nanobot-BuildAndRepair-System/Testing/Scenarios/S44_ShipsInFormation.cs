using Sandbox.ModAPI;
using VRageMath;

namespace SKONanobotBuildAndRepairSystem.Testing.Scenarios
{
    /// <summary>
    /// SCN-GRIND-002 / SCN-WELD-007: two ships flying side by side (both 15 m/s, 30 m apart). S44 friendly
    /// target ship (owned by the caller) — its damaged armor is welded. S45 pirate target ship (SPRT) —
    /// the janitor grinds it in flight (current behaviour: there is no "enemy grinding in motion" switch any
    /// more; v1.6.1's AllowEnemyGrindingInMotion was removed in v2.0.0). Skipped when the world has no pirates.
    /// </summary>
    internal sealed class S44_ShipsInFormation : TestScenarioBase
    {
        private static readonly Vector3 Velocity = new Vector3(15f, 0f, 0f);

        private readonly string _id;
        private readonly bool _enemy;
        private TestRig _barShip;
        private TestRig _target;
        private bool _skipped;
        private double _startGap = -1;

        public S44_ShipsInFormation(string id, bool enemy)
        {
            _id = id;
            _enemy = enemy;
        }

        public override string Id { get { return _id; } }
        public override string Description
        {
            get
            {
                return _enemy
                    ? "Two ships in flight: the pirate ship alongside is ground by the janitor"
                    : "Two ships in flight: the friendly ship alongside is repaired";
            }
        }

        public override void Setup(TestContext ctx)
        {
            _skipped = false;
            _startGap = -1;
            _target = null;
            ctx.SetModSettings(s =>
            {
                s.Welder.WorkSpeed = 10;
                s.Welder.AllowedGrindJanitorRelations |= AutoGrindRelation.Enemies | AutoGrindRelation.NoOwnership;
            });
            _barShip = ctx.SpawnRig(new RigSpec { Name = _id, Bars = 1, Dynamic = true, LinearVelocity = Velocity });

            var owner = ctx.OwnerId;
            if (_enemy)
            {
                var pirates = MyAPIGateway.Session.Factions != null ? MyAPIGateway.Session.Factions.TryGetFactionByTag("SPRT") : null;
                if (pirates == null || pirates.FounderId == 0) { _skipped = true; return; }
                owner = pirates.FounderId;
            }
            var target = new RigSpec
            {
                Name = _id + (_enemy ? "-enemy" : "-friendly"),
                Bars = 0,
                DamagedArmor = 4,
                OwnerId = owner,
                Dynamic = true,
                LinearVelocity = Velocity,
                Offset = new Vector3D(0, 0, -30)
            };
            target.Extra.Add(new RigBlock { Kind = RigBlockKind.Battery, Position = new Vector3I(0, 0, 1), Tag = "battery" });
            _target = ctx.SpawnRig(target);
        }

        protected override void Configure(TestContext ctx)
        {
            ctx.StockCargo(_barShip, "SteelPlate", 300);
            ctx.ConfigureBars(_barShip, s =>
            {
                s.SearchMode = SearchModes.BoundingBox;
                s.WorkMode = WorkModes.WeldBeforeGrind;
                s.WeldOptions = AutoWeldOptions.WeldFull;
                s.UseGrindJanitorOn = AutoGrindRelation.Enemies | AutoGrindRelation.NoOwnership;
                s.GrindJanitorOptions = 0;
            });
        }

        private double Gap()
        {
            if (_barShip.Grid == null || _target == null || _target.Grid == null || _target.Grid.Closed) return -1;
            return Vector3D.Distance(_barShip.Grid.WorldMatrix.Translation, _target.Grid.WorldMatrix.Translation);
        }

        public override void Sample(TestContext ctx)
        {
            if (_startGap < 0) _startGap = Gap();
        }

        public override bool IsComplete(TestContext ctx)
        {
            if (_skipped) return true;
            if (_enemy) return ScenarioUtil.AllGone(ctx, _target, _target.Targets);
            return ctx.CountFullIntegrity(_target.Targets) == _target.Targets.Count;
        }

        public override void Assert(TestContext ctx, TestResult result)
        {
            if (_skipped)
            {
                ctx.Metric("enemy", "skipped (no SPRT faction)");
                return;
            }
            ctx.Metric("gapStart", _startGap);
            ctx.Metric("gapEnd", Gap());
            if (_enemy)
            {
                var left = ScenarioUtil.AllGone(ctx, _target, _target.Targets) ? 0 : ctx.CountExisting(_target.Targets);
                ctx.Metric("enemyLeft", (long)left);
                ctx.Check(left == 0, "pirate ship in flight: " + left + " blocks left");
                return;
            }
            var welded = ctx.CountFullIntegrity(_target.Targets);
            ctx.Metric("welded", (long)welded);
            ctx.Check(welded == _target.Targets.Count && ctx.CountExisting(_target.Targets) == _target.Targets.Count,
                "friendly ship in flight: welded=" + welded + "/" + _target.Targets.Count);
        }
    }
}
