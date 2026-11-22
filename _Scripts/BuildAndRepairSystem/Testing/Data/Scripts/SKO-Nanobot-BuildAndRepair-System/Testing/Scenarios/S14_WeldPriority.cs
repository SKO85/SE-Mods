using SKONanobotBuildAndRepairSystem.Handlers;
using System.Collections.Generic;
using VRage.Game.ModAPI;

namespace SKONanobotBuildAndRepairSystem.Testing.Scenarios
{
    /// <summary>
    /// SCN-PRIO-001: 4 damaged armor + 4 damaged conveyors on the rig. S14: armor moved to the top of
    /// the weld priority list — every armor block is finished before the first conveyor. S15: the
    /// conveyor class disabled — armor welded, conveyors untouched.
    /// </summary>
    internal sealed class S14_WeldPriority : TestScenarioBase
    {
        private readonly string _id;
        private readonly bool _disableConveyors;
        private TestRig _rig;
        private readonly Dictionary<IMySlimBlock, double> _doneAt = new Dictionary<IMySlimBlock, double>();
        private double _conveyorStart;
        private double _armorDoneAt;

        public S14_WeldPriority(string id, bool disableConveyors)
        {
            _id = id;
            _disableConveyors = disableConveyors;
        }

        public override string Id { get { return _id; } }
        public override string Description
        {
            get
            {
                return _disableConveyors
                    ? "Weld priority: conveyor class disabled, armor welded, conveyors untouched"
                    : "Weld priority: armor first, all armor finished before the first conveyor";
            }
        }

        public override void Setup(TestContext ctx)
        {
            _doneAt.Clear();
            _conveyorStart = -1;
            _armorDoneAt = -1;
            ctx.SetModSettings(s => { s.Welder.WorkSpeed = 10; });
            var spec = new RigSpec { Name = _id, Bars = 1, DamagedArmor = 4 };
            ScenarioUtil.AddRow(spec, RigBlockKind.Conveyor, 4, 0, 1, 1, 0.2f, "conveyor");
            _rig = ctx.SpawnRig(spec);
        }

        protected override void Configure(TestContext ctx)
        {
            ScenarioUtil.StockEverything(ctx, _rig, 100);
            ctx.ConfigureBars(_rig, s =>
            {
                s.SearchMode = SearchModes.Grids;
                s.WorkMode = WorkModes.WeldBeforeGrind;
                s.WeldOptions = AutoWeldOptions.WeldFull;
            });
            for (int i = 0; i < _rig.Bars.Count; i++)
            {
                var bar = _rig.Bars[i];
                bar.ScriptSetWeldPriority((int)BlockClass.ArmorBlock, 0);
                if (_disableConveyors) bar.ScriptSetWeldEnabled((int)BlockClass.Conveyor, false);
                bar.SettingsChanged();
            }
        }

        public override void Sample(TestContext ctx)
        {
            var conveyors = _rig.Tag("conveyor");
            if (_conveyorStart < 0) _conveyorStart = ctx.IntegritySum(conveyors);
            Record(ctx, _rig.Targets);
            Record(ctx, conveyors);
            if (_armorDoneAt < 0 && ctx.CountFullIntegrity(_rig.Targets) == _rig.Targets.Count) _armorDoneAt = ctx.RunSeconds;
        }

        private void Record(TestContext ctx, List<IMySlimBlock> blocks)
        {
            for (int i = 0; i < blocks.Count; i++)
            {
                var b = blocks[i];
                if (b != null && b.IsFullIntegrity && !_doneAt.ContainsKey(b)) _doneAt[b] = ctx.RunSeconds;
            }
        }

        public override bool IsComplete(TestContext ctx)
        {
            if (_disableConveyors) return _armorDoneAt >= 0 && ctx.RunSeconds >= _armorDoneAt + 5.0;
            return ctx.CountFullIntegrity(_rig.Targets) == _rig.Targets.Count && ctx.CountFullIntegrity(_rig.Tag("conveyor")) == _rig.Tag("conveyor").Count;
        }

        public override void Assert(TestContext ctx, TestResult result)
        {
            var conveyors = _rig.Tag("conveyor");
            var armorWelded = ctx.CountFullIntegrity(_rig.Targets);
            var conveyorWelded = ctx.CountFullIntegrity(conveyors);
            ctx.Metric("armorWelded", (long)armorWelded);
            ctx.Metric("conveyorWelded", (long)conveyorWelded);
            ctx.Check(conveyors.Count == 4, "conveyors resolved=" + conveyors.Count + "/4");
            ctx.Check(armorWelded == _rig.Targets.Count, "armor welded=" + armorWelded + "/" + _rig.Targets.Count);

            if (_disableConveyors)
            {
                var now = ctx.IntegritySum(conveyors);
                ctx.Metric("conveyorIntegrityGain", now - _conveyorStart);
                ctx.Check(now <= _conveyorStart + 0.01, "disabled conveyors were welded (+" + (now - _conveyorStart) + " integrity)");
                return;
            }

            ctx.Check(conveyorWelded == conveyors.Count, "conveyors welded=" + conveyorWelded + "/" + conveyors.Count);
            var lastArmor = Latest(_rig.Targets);
            var firstConveyor = Earliest(conveyors);
            ctx.Metric("lastArmorDoneS", lastArmor);
            ctx.Metric("firstConveyorDoneS", firstConveyor);
            ctx.Check(lastArmor <= firstConveyor, "a conveyor was finished before all armor (" + firstConveyor + " s < " + lastArmor + " s)");
        }

        private double Latest(List<IMySlimBlock> blocks)
        {
            var t = 0.0;
            double v;
            for (int i = 0; i < blocks.Count; i++) if (_doneAt.TryGetValue(blocks[i], out v) && v > t) t = v;
            return t;
        }

        private double Earliest(List<IMySlimBlock> blocks)
        {
            var t = double.MaxValue;
            double v;
            for (int i = 0; i < blocks.Count; i++) if (_doneAt.TryGetValue(blocks[i], out v) && v < t) t = v;
            return t;
        }
    }
}
