using SKONanobotBuildAndRepairSystem.Models;
using VRageMath;

namespace SKONanobotBuildAndRepairSystem.Testing.Scenarios
{
    /// <summary>
    /// Not a test: queued first in every session so no real scenario pays the one-time JIT cost
    /// of the weld, grind, scan and projector-build code after a world load (S01 measured 38 ms, then 1 ms;
    /// the first projected build of a session cost 76 ms in S03). No checks; it only has to run the paths once.
    /// </summary>
    internal sealed class S00_WarmUp : TestScenarioBase
    {
        private const double ProjectionWait = 10.0;
        // Two armor cells above the projector (rig projector at (2, 0, 1) for one BaR).
        private static readonly Vector3I[] ProjectedCells = { new Vector3I(2, 1, 1), new Vector3I(3, 1, 1) };

        private TestRig _rig;
        private TestRig _derelict;
        private bool _blueprintSet;

        public override string Id { get { return "S00"; } }
        public override string Description { get { return "Warm-up, not a test: weld, grind, scan and build a projection once"; } }
        public override int TimeoutSeconds { get { return 60; } }

        public override void Setup(TestContext ctx)
        {
            _blueprintSet = false;
            ctx.SetModSettings(s =>
            {
                s.Welder.WorkSpeed = 10;
                s.Welder.AllowedGrindJanitorRelations |= AutoGrindRelation.NoOwnership;
            });
            _rig = ctx.SpawnRig(new RigSpec { Name = "S00", Bars = 1, DamagedArmor = 5, Projector = true });
            _derelict = ctx.SpawnRig(new RigSpec { Name = "S00-derelict", Bars = 0, DamagedArmor = 3, DamagedBuildPercent = 1f, OwnerId = 0, Offset = new Vector3D(0, 0, -30) });
        }

        protected override void Configure(TestContext ctx)
        {
            ctx.StockCargo(_rig, "SteelPlate", 500);
            ctx.ConfigureBars(_rig, s =>
            {
                s.SearchMode = SearchModes.BoundingBox;
                s.WorkMode = WorkModes.WeldBeforeGrind;
                s.WeldOptions = AutoWeldOptions.WeldFull;
                s.UseGrindJanitorOn = AutoGrindRelation.NoOwnership;
                s.GrindJanitorOptions = 0;
                s.Flags = s.Flags | SyncBlockSettings.Settings.AllowBuild;
            });
        }

        public override bool IsReady(TestContext ctx)
        {
            if (!base.IsReady(ctx)) return false;
            var projector = _rig.Projector;
            if (projector == null || !_rig.ProjectorPosition.HasValue) return true; // warm-up: carry on without
            if (!_blueprintSet)
            {
                projector.SetProjectedGrid(S03_ProjectedBuild.BuildBlueprint(_rig.ProjectorPosition.Value, ProjectedCells));
                _blueprintSet = true;
                return false;
            }
            // Ready once it projects; never block the session on it.
            return (projector.IsProjecting && projector.BuildableBlocksCount > 0) || ctx.RunSecondsSinceSetup > ProjectionWait;
        }

        public override bool IsComplete(TestContext ctx)
        {
            var projector = _rig.Projector;
            var projectionDone = projector == null || !projector.IsProjecting || projector.RemainingBlocks == 0;
            return (ctx.CountFullIntegrity(_rig.Targets) == _rig.Targets.Count && ScenarioUtil.AllGone(ctx, _derelict, _derelict.Targets) && projectionDone)
                || ctx.RunSeconds >= 30;
        }

        public override void Assert(TestContext ctx, TestResult result)
        {
            var projector = _rig.Projector;
            ctx.Metric("welded", (long)ctx.CountFullIntegrity(_rig.Targets));
            ctx.Metric("projectionRemaining", projector != null ? (long)projector.RemainingBlocks : -1);
        }
    }
}
