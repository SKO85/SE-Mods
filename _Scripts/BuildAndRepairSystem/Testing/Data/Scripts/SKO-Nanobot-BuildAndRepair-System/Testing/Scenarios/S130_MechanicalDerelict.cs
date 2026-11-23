using Sandbox.ModAPI;
using System.Collections.Generic;
using VRage;
using VRage.Game;
using VRage.Game.ModAPI;
using VRage.ObjectBuilders;
using VRageMath;

namespace SKONanobotBuildAndRepairSystem.Testing.Scenarios
{
    /// <summary>
    /// SCN-GRIND (mechanical derelicts, coverage follow-up): an unowned derelict with a rotor and a piston, each with its
    /// head grid (the game's "add top part" action) carrying 2 unowned armor blocks. One BaR (GrindOnly, BoundingBox,
    /// janitor NoOwnership) must grind the base grid and both head grids away completely: mechanical blocks one per tick
    /// (dismount budget), heads that come loose when their base block goes, the raze queue draining.
    /// </summary>
    internal sealed class S130_MechanicalDerelict : TestScenarioBase
    {
        private const double Window = 150.0;
        private TestRig _rig, _derelict;
        private readonly List<IMyCubeGrid> _heads = new List<IMyCubeGrid>();
        private readonly string[] _tags = { "rotor", "piston" };
        private readonly string[] _actions = { "AddRotorTopPart", "Add Top Part" };
        private readonly bool[] _requested = new bool[2];
        private double _requestedAt = -1, _doneAt = -1;

        public override string Id { get { return "S130"; } }
        public override string Description { get { return "Mechanical derelict: base grid, rotor head and piston head all ground away"; } }
        public override int TimeoutSeconds { get { return 180; } }
        public override int ReadyTimeoutSeconds { get { return 60; } }

        public override void Setup(TestContext ctx)
        {
            _heads.Clear();
            _requested[0] = _requested[1] = false;
            _requestedAt = _doneAt = -1;
            ctx.SetModSettings(s =>
            {
                s.Welder.WorkSpeed = 5;
                s.Welder.AllowedGrindJanitorRelations |= AutoGrindRelation.NoOwnership;
            });
            _rig = ctx.SpawnRig(new RigSpec { Name = "S130", Bars = 1 });
            var derelict = new RigSpec { Name = "S130-derelict", Bars = 0, Armor = 4, OwnerId = 0, Offset = new Vector3D(0, 0, -30) };
            derelict.Extra.Add(new RigBlock { Kind = RigBlockKind.Stator, Position = new Vector3I(0, 1, 0), Tag = "rotor" });
            derelict.Extra.Add(new RigBlock { Kind = RigBlockKind.Piston, Position = new Vector3I(2, 1, 0), Tag = "piston" });
            _derelict = ctx.SpawnRig(derelict);
        }

        protected override void Configure(TestContext ctx)
        {
            ctx.ConfigureBars(_rig, s =>
            {
                s.SearchMode = SearchModes.BoundingBox;
                s.WorkMode = WorkModes.GrindOnly;
                s.UseGrindJanitorOn = AutoGrindRelation.NoOwnership;
                s.GrindJanitorOptions = 0;
                s.AreaSize = new Vector3(80f, 80f, 80f);
            });
        }

        private IMyMechanicalConnectionBlock Mech(int i)
        {
            var t = _derelict.Tag(_tags[i]);
            return t.Count > 0 && t[0] != null ? t[0].FatBlock as IMyMechanicalConnectionBlock : null;
        }

        public override bool IsReady(TestContext ctx)
        {
            if (!base.IsReady(ctx)) return false;
            if (_heads.Count == 2) return true;
            var ready = true;
            for (int i = 0; i < 2; i++)
            {
                var mech = Mech(i);
                if (mech == null) return false;
                if (mech.TopGrid != null && mech.Top != null) continue;
                ready = false;
                if (_requested[i]) continue;
                var actions = new List<Sandbox.ModAPI.Interfaces.ITerminalAction>();
                mech.GetActions(actions);
                for (int a = 0; a < actions.Count; a++)
                    if (actions[a].Id == _actions[i]) { actions[a].Apply(mech); break; }
                _requested[i] = true;
                _requestedAt = ctx.RunSecondsSinceSetup;
            }
            if (!ready)
            {
                if (_requestedAt >= 0 && ctx.RunSecondsSinceSetup - _requestedAt > 15.0)
                    ctx.Check(false, "head parts did not appear after the add-top-part actions");
                return false;
            }
            // Heads are there: 2 unowned armor blocks on each, along the head's away direction.
            for (int i = 0; i < 2; i++)
            {
                var mech = Mech(i);
                var head = mech.Top;
                var grid = mech.TopGrid;
                ctx.TrackSpawned(grid);
                _heads.Add(grid);
                var away = Base6Directions.GetIntVector(head.Orientation.Up);
                for (int k = 1; k <= 2; k++)
                {
                    var ob = MyObjectBuilderSerializer.CreateNewObject<MyObjectBuilder_CubeBlock>("LargeBlockArmorBlock");
                    ob.Min = head.Position + away * k;
                    ob.BlockOrientation = new SerializableBlockOrientation(Base6Directions.Direction.Forward, Base6Directions.Direction.Up);
                    ob.Owner = 0;
                    ob.BuiltBy = 0;
                    ob.ColorMaskHSV = new SerializableVector3(0f, -1f, 0f);
                    grid.AddBlock(ob, false);
                }
            }
            return true;
        }

        private static bool Gone(IMyCubeGrid g)
        {
            return g == null || g.Closed || g.MarkedForClose;
        }

        private int Remaining()
        {
            var n = Gone(_derelict.Grid) ? 0 : 1;
            for (int i = 0; i < _heads.Count; i++) if (!Gone(_heads[i])) n++;
            return n;
        }

        public override void Sample(TestContext ctx)
        {
            if (_doneAt < 0 && Remaining() == 0) _doneAt = ctx.RunSeconds;
        }

        public override bool IsComplete(TestContext ctx)
        {
            return _doneAt >= 0 || ctx.RunSeconds >= Window;
        }

        public override void Assert(TestContext ctx, TestResult result)
        {
            ctx.Metric("gridsLeft", (long)Remaining());
            ctx.Metric("headGrids", (long)_heads.Count);
            ctx.Metric("doneS", _doneAt);
            ctx.Check(_heads.Count == 2, "layout: " + _heads.Count + "/2 head grids");
            ctx.Check(Remaining() == 0, Remaining() + " of 3 derelict grids (base, rotor head, piston head) still there after " + Window + " s");
            ctx.Check(_rig.Grid != null && !_rig.Grid.Closed, "own rig was ground");
        }
    }
}
