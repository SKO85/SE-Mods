using VRage;
using VRage.Game;
using VRage.Game.ModAPI;
using VRage.ObjectBuilders;
using VRageMath;

namespace SKONanobotBuildAndRepairSystem.Testing.Scenarios
{
    /// <summary>
    /// SCN-CORE-001 (BUG-261122.11): a block frame added to a fully repaired grid (as a hand-placed block arrives:
    /// grid.AddBlock, low integrity) is noticed and welded. Two frames: 3 s after the repair, and after a 30 s idle
    /// (empty-grid skip + idle scan backoff). Each must start welding within TargetsUpdateInterval + 3 s.
    /// </summary>
    internal sealed class S72_PlacedFrame : TestScenarioBase
    {
        private static readonly double[] IdleBefore = { 3.0, 30.0 };
        private const double ScanSlack = 3.0;
        private const float FramePercent = 0.05f;

        private TestRig _rig;
        private int _cycle;
        private bool _waiting;      // a placed frame is not yet full
        private double _doneAt;
        private double _placedAt;
        private IMySlimBlock _frame;
        private float _frameStart;
        private readonly double[] _detect = { -1, -1 };
        private readonly double[] _repair = { -1, -1 };
        private string _error;

        public override string Id { get { return "S72"; } }
        public override string Description { get { return "Placed frame: a block added to a repaired grid (after 3 s and 30 s idle) is welded"; } }
        public override int TimeoutSeconds { get { return 150; } }

        private static double DetectLimit { get { return Mod.Settings.TargetsUpdateInterval.TotalSeconds + ScanSlack; } }

        public override void Setup(TestContext ctx)
        {
            _cycle = 0;
            _waiting = false;
            _doneAt = -1;
            _frame = null;
            _error = null;
            _detect[0] = _detect[1] = _repair[0] = _repair[1] = -1;
            ctx.SetModSettings(s => { s.Welder.WorkSpeed = 10; });
            _rig = ctx.SpawnRig(new RigSpec { Name = "S72", Bars = 1, DamagedArmor = 3 });
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

        private IMySlimBlock PlaceFrame(TestContext ctx, Vector3I pos)
        {
            var ob = MyObjectBuilderSerializer.CreateNewObject<MyObjectBuilder_CubeBlock>("LargeBlockArmorBlock");
            ob.Min = pos;
            ob.BlockOrientation = new SerializableBlockOrientation(Base6Directions.Direction.Forward, Base6Directions.Direction.Up);
            ob.Owner = ctx.OwnerId;
            ob.BuiltBy = ctx.OwnerId;
            ob.ShareMode = MyOwnershipShareModeEnum.Faction;
            ob.ColorMaskHSV = new SerializableVector3(0f, -1f, 0f);
            ob.BuildPercent = FramePercent;
            ob.IntegrityPercent = FramePercent;
            return _rig.Grid.AddBlock(ob, false);
        }

        public override void Sample(TestContext ctx)
        {
            if (_error != null) return;
            if (_cycle == 0 && _doneAt < 0)
            {
                if (ctx.CountFullIntegrity(_rig.Targets) == _rig.Targets.Count) _doneAt = ctx.RunSeconds;
                return;
            }
            if (_waiting)
            {
                var c = _cycle - 1;
                if (_frame == null || _frame.IsDestroyed) { _error = "placed frame " + _cycle + " disappeared"; return; }
                if (_detect[c] < 0 && _frame.Integrity > _frameStart + 0.01f) _detect[c] = ctx.RunSeconds - _placedAt;
                if (!_frame.IsFullIntegrity) return;
                _repair[c] = ctx.RunSeconds - _placedAt;
                _waiting = false;
                _doneAt = ctx.RunSeconds;
                return;
            }
            if (_cycle >= IdleBefore.Length || ctx.RunSeconds < _doneAt + IdleBefore[_cycle]) return;

            // Next to the damaged row (armor at y 1, x 0..2): x 3 + cycle.
            _frame = PlaceFrame(ctx, new Vector3I(3 + _cycle, 1, 0));
            if (_frame == null) { _error = "grid.AddBlock refused the frame"; return; }
            _frameStart = _frame.Integrity;
            _placedAt = ctx.RunSeconds;
            _cycle++;
            _waiting = true;
        }

        public override bool IsComplete(TestContext ctx)
        {
            return _error != null || (_cycle >= IdleBefore.Length && !_waiting);
        }

        public override void Assert(TestContext ctx, TestResult result)
        {
            ctx.Check(_error == null, _error);
            ctx.Metric("detectLimitS", DetectLimit);
            for (int i = 0; i < IdleBefore.Length; i++)
            {
                ctx.Metric("frame" + (i + 1) + "IdleS", IdleBefore[i]);
                ctx.Metric("frame" + (i + 1) + "DetectS", _detect[i]);
                ctx.Metric("frame" + (i + 1) + "WeldedS", _repair[i]);
                ctx.Check(_detect[i] >= 0 && _detect[i] <= DetectLimit, "frame " + (i + 1) + ": not welded within " + DetectLimit + " s of placing");
                ctx.Check(_repair[i] >= 0, "frame " + (i + 1) + ": not welded to full");
            }
        }
    }
}
