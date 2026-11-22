using SKONanobotBuildAndRepairSystem.Handlers;
using VRageMath;

namespace SKONanobotBuildAndRepairSystem.Testing.Scenarios
{
    /// <summary>
    /// SCN-PRIO-001 (grind list): a derelict grid of 4 armor + 4 conveyors; the conveyor class is disabled
    /// in the grind priority list — the janitor grinds the armor and leaves every conveyor intact.
    /// </summary>
    internal sealed class S16_GrindClassDisabled : TestScenarioBase
    {
        private TestRig _rig;
        private TestRig _derelict;
        private double _conveyorStart = -1;

        public override string Id { get { return "S16"; } }
        public override string Description { get { return "Grind priority: conveyor class disabled, janitor grinds the armor only"; } }

        public override void Setup(TestContext ctx)
        {
            _conveyorStart = -1;
            ctx.SetModSettings(s =>
            {
                s.Welder.WorkSpeed = 10;
                s.Welder.AllowedGrindJanitorRelations |= AutoGrindRelation.NoOwnership;
            });
            _rig = ctx.SpawnRig(new RigSpec { Name = "S16", Bars = 1 });
            var spec = new RigSpec { Name = "S16-derelict", Bars = 0, DamagedArmor = 4, DamagedBuildPercent = 1f, OwnerId = 0, Offset = new Vector3D(0, 0, -30) };
            ScenarioUtil.AddRow(spec, RigBlockKind.Conveyor, 4, 0, 0, 1, 1f, "conveyor");
            _derelict = ctx.SpawnRig(spec);
        }

        protected override void Configure(TestContext ctx)
        {
            ctx.ConfigureBars(_rig, s =>
            {
                s.SearchMode = SearchModes.BoundingBox;
                s.WorkMode = WorkModes.GrindOnly;
                s.UseGrindJanitorOn = AutoGrindRelation.NoOwnership;
                s.GrindJanitorOptions = 0;
            });
            for (int i = 0; i < _rig.Bars.Count; i++)
            {
                _rig.Bars[i].ScriptSetGrindEnabled((int)BlockClass.Conveyor, false);
                _rig.Bars[i].SettingsChanged();
            }
        }

        public override void Sample(TestContext ctx)
        {
            if (_conveyorStart < 0) _conveyorStart = ctx.IntegritySum(_derelict.Tag("conveyor"));
        }

        public override bool IsComplete(TestContext ctx)
        {
            return ctx.CountExisting(_derelict.Targets) == 0;
        }

        public override void Assert(TestContext ctx, TestResult result)
        {
            var conveyors = _derelict.Tag("conveyor");
            var armorLeft = ctx.CountExisting(_derelict.Targets);
            var conveyorsLeft = ctx.CountExisting(conveyors);
            ctx.Metric("armorLeft", (long)armorLeft);
            ctx.Metric("conveyorsLeft", (long)conveyorsLeft);
            ctx.Check(conveyors.Count == 4, "conveyors resolved=" + conveyors.Count + "/4");
            ctx.Check(armorLeft == 0, "armor left=" + armorLeft + " (janitor did not grind the enabled class)");
            ctx.Check(conveyorsLeft == conveyors.Count, "disabled conveyors ground: " + (conveyors.Count - conveyorsLeft));
            ctx.Check(ctx.IntegritySum(conveyors) >= _conveyorStart - 0.01, "disabled conveyors lost integrity");
        }
    }
}
