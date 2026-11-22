using SKONanobotBuildAndRepairSystem.Models;
using VRageMath;

namespace SKONanobotBuildAndRepairSystem.Testing.Scenarios
{
    internal enum GrindOrderMode { FarthestFirst, NearestFirst, SmallestGridFirst }

    /// <summary>
    /// SCN-GRIND-003: three derelict grids at 20 / 45 / 70 m (4, 1 and 4 blocks). The grid the janitor
    /// touches first must match the grind order: farthest first (default), GrindNearFirst, or
    /// GrindSmallestGridFirst (the 1-block grid in the middle).
    /// </summary>
    internal sealed class S11_GrindOrder : TestScenarioBase
    {
        private static readonly string[] Names = { "near", "middle", "far" };
        private static readonly double[] Distances = { 20, 45, 70 };
        private static readonly int[] Sizes = { 4, 1, 4 };

        private readonly string _id;
        private readonly GrindOrderMode _mode;
        private TestRig _rig;
        private readonly TestRig[] _targets = new TestRig[3];
        private readonly double[] _initial = new double[3];
        private bool _sampled;
        private int _first;

        public S11_GrindOrder(string id, GrindOrderMode mode)
        {
            _id = id;
            _mode = mode;
        }

        public override string Id { get { return _id; } }
        public override string Description { get { return "Grind order " + _mode + ": the " + Names[Expected] + " derelict grid is ground first"; } }
        public override int TimeoutSeconds { get { return 60; } }

        private int Expected
        {
            get
            {
                switch (_mode)
                {
                    case GrindOrderMode.NearestFirst: return 0;
                    case GrindOrderMode.SmallestGridFirst: return 1;
                    default: return 2;
                }
            }
        }

        public override void Setup(TestContext ctx)
        {
            _sampled = false;
            _first = -1;
            ctx.SetModSettings(s =>
            {
                s.Welder.WorkSpeed = 10;
                s.Welder.AllowedGrindJanitorRelations |= AutoGrindRelation.NoOwnership;
            });
            _rig = ctx.SpawnRig(new RigSpec { Name = _id, Bars = 1 });
            for (int i = 0; i < 3; i++)
            {
                _targets[i] = ctx.SpawnRig(new RigSpec
                {
                    Name = _id + "-" + Names[i],
                    Bars = 0,
                    DamagedArmor = Sizes[i],
                    DamagedBuildPercent = 1f, // intact, but tracked as targets
                    OwnerId = 0,
                    Offset = new Vector3D(0, 0, -Distances[i])
                });
            }
        }

        protected override void Configure(TestContext ctx)
        {
            ctx.ConfigureBars(_rig, s =>
            {
                s.SearchMode = SearchModes.BoundingBox;
                s.WorkMode = WorkModes.GrindOnly;
                s.UseGrindJanitorOn = AutoGrindRelation.NoOwnership;
                s.GrindJanitorOptions = 0;
                var flags = s.Flags & ~(SyncBlockSettings.Settings.GrindNearFirst | SyncBlockSettings.Settings.GrindSmallestGridFirst);
                if (_mode == GrindOrderMode.NearestFirst) flags |= SyncBlockSettings.Settings.GrindNearFirst;
                if (_mode == GrindOrderMode.SmallestGridFirst) flags |= SyncBlockSettings.Settings.GrindSmallestGridFirst;
                s.Flags = flags;
            });
        }

        public override void Sample(TestContext ctx)
        {
            if (!_sampled)
            {
                for (int i = 0; i < 3; i++) _initial[i] = ctx.IntegritySum(_targets[i].Targets);
                _sampled = true;
                return;
            }
            if (_first >= 0) return;
            for (int i = 0; i < 3; i++)
            {
                var gone = ScenarioUtil.AllGone(ctx, _targets[i], _targets[i].Targets);
                if (gone || ctx.IntegritySum(_targets[i].Targets) < _initial[i] - 0.01)
                {
                    _first = i;
                    return;
                }
            }
        }

        public override bool IsComplete(TestContext ctx)
        {
            return _first >= 0;
        }

        public override void Assert(TestContext ctx, TestResult result)
        {
            ctx.Metric("firstGround", _first >= 0 ? Names[_first] : "none");
            ctx.Check(_first >= 0, "nothing was ground");
            ctx.Check(_first == Expected, "first ground: " + (_first >= 0 ? Names[_first] : "none") + ", expected " + Names[Expected]);
        }
    }
}
