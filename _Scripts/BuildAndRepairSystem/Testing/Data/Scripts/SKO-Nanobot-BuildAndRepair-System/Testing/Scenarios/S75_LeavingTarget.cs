using VRageMath;

namespace SKONanobotBuildAndRepairSystem.Testing.Scenarios
{
    /// <summary>
    /// SCN-CORE-002 (BUG-261122.16): a target ship leaving the BaR's area mid-work is not worked from afar. BaR rig with
    /// a 40 m BoundingBox area; a ship (S75: own, damaged -> welding; S76: derelict, intact -> janitor grinding) 12 m
    /// away. Once work on it has started, the ship is pushed away at 20 m/s. From 1 s after its last block left the
    /// area, its blocks must not change any more (scan-time-only range check used to keep working it until the next scan).
    /// </summary>
    internal sealed class S75_LeavingTarget : TestScenarioBase
    {
        private const float PushSpeed = 20f;
        private const double Grace = 1.0;
        private const double Watch = 6.0;

        private readonly string _id;
        private readonly bool _grind;
        private TestRig _bar;
        private TestRig _ship;
        private double _start = -1;
        private double _pushedAt = -1;
        private double _outAt = -1;
        private double _atGraceEnd = -1;
        private double _workBeforePush;

        public S75_LeavingTarget(string id, bool grind)
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
                    ? "Leaving target: a derelict ship pushed out of the area is not ground from afar"
                    : "Leaving target: an own ship pushed out of the area is not welded from afar";
            }
        }
        public override int TimeoutSeconds { get { return 90; } }

        public override void Setup(TestContext ctx)
        {
            _start = _pushedAt = _outAt = _atGraceEnd = -1;
            _workBeforePush = 0;
            ctx.SetModSettings(s =>
            {
                s.Welder.WorkSpeed = 1; // slow: work is still going on when the ship leaves
                s.Welder.AllowedGrindJanitorRelations |= AutoGrindRelation.NoOwnership;
            });
            _bar = ctx.SpawnRig(new RigSpec { Name = _id, Bars = 1 });
            _ship = ctx.SpawnRig(new RigSpec
            {
                Name = _id + "-ship", Bars = 0, Dynamic = true,
                OwnerId = _grind ? 0 : -1,
                DamagedArmor = 10, DamagedBuildPercent = _grind ? 1f : 0.2f, RowWidth = 5,
                Offset = new Vector3D(0, 0, 12)
            });
        }

        protected override void Configure(TestContext ctx)
        {
            ctx.StockAllCargo(_bar, "SteelPlate", 300);
            ctx.ConfigureBars(_bar, s =>
            {
                s.SearchMode = SearchModes.BoundingBox;
                s.WorkMode = _grind ? WorkModes.GrindOnly : WorkModes.WeldBeforeGrind;
                s.WeldOptions = AutoWeldOptions.WeldFull;
                s.UseGrindJanitorOn = _grind ? AutoGrindRelation.NoOwnership : 0;
                s.GrindJanitorOptions = 0;
                s.AreaOffset = Vector3.Zero;
                s.AreaSize = new Vector3(40f, 40f, 40f);
            });
        }

        private double Integrity(TestContext ctx)
        {
            return ctx.IntegritySum(_ship.Targets);
        }

        private bool AllOutOfArea()
        {
            var bar = _bar.Bars.Count > 0 ? _bar.Bars[0] : null;
            if (bar == null) return false;
            foreach (var b in _ship.Targets)
                if (b != null && !b.IsDestroyed && bar.IsTargetInArea(b)) return false;
            return true;
        }

        public override void Sample(TestContext ctx)
        {
            var integrity = Integrity(ctx);
            if (_start < 0) { _start = integrity; return; }

            if (_pushedAt < 0)
            {
                // Push once work has clearly started.
                if (System.Math.Abs(integrity - _start) < 1.0) return;
                _workBeforePush = System.Math.Abs(integrity - _start);
                if (_ship.Grid != null && _ship.Grid.Physics != null)
                    _ship.Grid.Physics.LinearVelocity = new Vector3(0, 0, PushSpeed);
                _pushedAt = ctx.RunSeconds;
                return;
            }
            if (_outAt < 0)
            {
                if (AllOutOfArea()) _outAt = ctx.RunSeconds;
                return;
            }
            if (_atGraceEnd < 0 && ctx.RunSeconds >= _outAt + Grace) _atGraceEnd = integrity;
        }

        public override bool IsComplete(TestContext ctx)
        {
            if (_pushedAt < 0) return ctx.RunSeconds > 60;
            if (_outAt < 0) return ctx.RunSeconds > _pushedAt + 20;
            return ctx.RunSeconds >= _outAt + Grace + Watch;
        }

        public override void Assert(TestContext ctx, TestResult result)
        {
            var changed = _atGraceEnd >= 0 ? System.Math.Abs(Integrity(ctx) - _atGraceEnd) : -1;
            ctx.Metric("workBeforePush", _workBeforePush);
            ctx.Metric("pushedAtS", _pushedAt);
            ctx.Metric("outOfAreaS", _outAt >= 0 && _pushedAt >= 0 ? _outAt - _pushedAt : -1);
            ctx.Metric("integrityChangedAfterLeaving", changed);
            ctx.Check(_pushedAt >= 0, "no " + (_grind ? "grinding" : "welding") + " started on the ship within 60 s");
            ctx.Check(_outAt >= 0, "ship never left the area after the push");
            ctx.Check(changed >= 0 && changed < 0.01, "ship still " + (_grind ? "ground" : "welded") + " after leaving the area (integrity changed by " + changed + ")");
        }
    }
}
