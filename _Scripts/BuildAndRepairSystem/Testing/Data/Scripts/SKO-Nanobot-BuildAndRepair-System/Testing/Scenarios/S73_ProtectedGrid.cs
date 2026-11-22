using Sandbox.ModAPI;
using System.Collections.Generic;
using VRage.Game;
using VRage.Game.ModAPI;
using VRageMath;

namespace SKONanobotBuildAndRepairSystem.Testing.Scenarios
{
    internal enum ProtectionMode
    {
        /// <summary>Grid with DestructibleBlocks = false (admin toggle; some plugins set it).</summary>
        Indestructible,
        /// <summary>A BeforeDamage handler zeroes grind damage on the grid (how protection plugins usually work).</summary>
        DamageHandler
    }

    /// <summary>
    /// SCN-GRIND-006 (BUG-261122.15): protected grids are not ground by the janitor. Derelict 10-block row 25 m away,
    /// GrindOnly, janitor NoOwnership, 20 s. Indestructible: the scan drops the grid, no grind call at all.
    /// DamageHandler: the harness registers a BeforeDamage handler that zeroes grind damage on the grid (once per
    /// session, gated by ProtectedGrids); the BaR must respect it and not retry every tick (grid skipped 30 s).
    /// </summary>
    internal sealed class S73_ProtectedGrid : TestScenarioBase
    {
        private const double Window = 20.0;
        private const int MaxGrindCallsWhenRefused = 3; // one refusal per BaR per 30 s, plus timing slack

        /// <summary>Grids the harness damage handler protects (main thread).</summary>
        internal static readonly HashSet<long> ProtectedGrids = new HashSet<long>();
        private static bool _handlerRegistered;
        private static int _refusedHits;

        private readonly string _id;
        private readonly ProtectionMode _mode;
        private TestRig _bar;
        private TestRig _target;
        private double _integrityStart = -1;

        public S73_ProtectedGrid(string id, ProtectionMode mode)
        {
            _id = id;
            _mode = mode;
        }

        public override string Id { get { return _id; } }
        public override string Description
        {
            get
            {
                return _mode == ProtectionMode.Indestructible
                    ? "Protected grid: indestructible (DestructibleBlocks off) derelict is not ground"
                    : "Protected grid: a damage-handler protection (plugin style) stops the janitor, no retry storm";
            }
        }
        public override IList<string> PerfMethods { get { return GrindPerfMethods; } }
        private static readonly string[] GrindPerfMethods = { "ServerDoGrind", "ServerTryGrinding", "ServerTryWeldingGrindingCollecting", "UpdateBeforeSimulation10_100" };

        private static void OnBeforeDamage(object target, ref MyDamageInformation info)
        {
            if (ProtectedGrids.Count == 0 || info.Type != MyDamageType.Grind) return;
            var block = target as IMySlimBlock;
            if (block == null || block.CubeGrid == null || !ProtectedGrids.Contains(block.CubeGrid.EntityId)) return;
            info.Amount = 0f;
            _refusedHits++;
        }

        public override void Setup(TestContext ctx)
        {
            _integrityStart = -1;
            _refusedHits = 0;
            ProtectedGrids.Clear();
            if (_mode == ProtectionMode.DamageHandler && !_handlerRegistered)
            {
                // Permanent for the session (no unregister in the API); inert while ProtectedGrids is empty.
                MyAPIGateway.Session.DamageSystem.RegisterBeforeDamageHandler(0, OnBeforeDamage);
                _handlerRegistered = true;
            }
            ctx.SetModSettings(s =>
            {
                s.Welder.WorkSpeed = 10;
                s.Welder.AllowedGrindJanitorRelations |= AutoGrindRelation.NoOwnership;
            });
            _bar = ctx.SpawnRig(new RigSpec { Name = _id, Bars = 1 });
            _target = ctx.SpawnRig(new RigSpec
            {
                Name = _id + "-protected", Bars = 0, OwnerId = 0,
                DamagedArmor = 10, DamagedBuildPercent = 1f, RowWidth = 10,
                Indestructible = _mode == ProtectionMode.Indestructible,
                Offset = new Vector3D(0, 0, 25)
            });
        }

        protected override void Configure(TestContext ctx)
        {
            if (_mode == ProtectionMode.DamageHandler && _target.Grid != null) ProtectedGrids.Add(_target.Grid.EntityId);
            ctx.ConfigureBars(_bar, s =>
            {
                s.SearchMode = SearchModes.BoundingBox;
                s.WorkMode = WorkModes.GrindOnly;
                s.UseGrindJanitorOn = AutoGrindRelation.NoOwnership;
                s.GrindJanitorOptions = 0;
            });
        }

        public override void Sample(TestContext ctx)
        {
            if (_integrityStart < 0) _integrityStart = ctx.IntegritySum(_target.Targets);
        }

        public override bool IsComplete(TestContext ctx)
        {
            return ctx.RunSeconds >= Window;
        }

        public override void Assert(TestContext ctx, TestResult result)
        {
            var left = ScenarioUtil.AllGone(ctx, _target, _target.Targets) ? 0 : ctx.CountExisting(_target.Targets);
            var integrity = left > 0 ? ctx.IntegritySum(_target.Targets) : 0;
            long grindCalls = 0;
            Profiling.MethodProfiler.MethodStatsSnapshot stats;
            if (ctx.TryGetMethodStats("ServerDoGrind", out stats)) grindCalls = stats.Calls;
            ctx.Metric("left", (long)left);
            ctx.Metric("grindCalls", grindCalls);
            ctx.Metric("handlerRefusals", (long)_refusedHits);

            ctx.Check(left == _target.Targets.Count && integrity >= _integrityStart - 0.01,
                "protected grid was ground: " + (_target.Targets.Count - left) + " blocks gone, integrity " + _integrityStart + " -> " + integrity);
            if (_mode == ProtectionMode.Indestructible)
                ctx.Check(grindCalls == 0, "indestructible grid still reached ServerDoGrind " + grindCalls + " times (scan should drop it)");
            else
                ctx.Check(grindCalls <= MaxGrindCallsWhenRefused, "refused grid retried " + grindCalls + " times in " + Window + " s (expected <= " + MaxGrindCallsWhenRefused + ")");
        }

        public override void Teardown(TestContext ctx)
        {
            ProtectedGrids.Clear();
            base.Teardown(ctx);
        }
    }
}
