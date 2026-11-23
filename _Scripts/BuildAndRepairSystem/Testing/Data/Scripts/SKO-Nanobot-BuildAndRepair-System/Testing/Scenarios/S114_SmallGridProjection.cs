using Sandbox.Common.ObjectBuilders;
using SKONanobotBuildAndRepairSystem.Models;
using System;
using System.Collections.Generic;
using VRage;
using VRage.Game;
using VRage.Game.ModAPI;
using VRage.ObjectBuilders;
using VRageMath;

namespace SKONanobotBuildAndRepairSystem.Testing.Scenarios
{
    /// <summary>
    /// SCN-WELD-002 (Steam report "will not build small grid projections"): a static small-grid ship with a small
    /// projector projects 10 small armor blocks next to it. S114: a large BaR on the rig 12 m away builds them (BoundingBox,
    /// plates in the rig cargo). S115: a small BaR on the small ship itself builds them (Grids, plates in its own
    /// inventory). Pass: all 10 built on the expected cells, welded, plates charged exactly once.
    /// </summary>
    internal sealed class S114_SmallGridProjection : TestScenarioBase
    {
        private const int Stock = 50;
        private const double StallSeconds = 15.0;
        private static readonly Vector3I[] Cells =
        {
            new Vector3I(6, 0, 1), new Vector3I(6, 0, 2), new Vector3I(6, 0, 3), new Vector3I(6, 0, 4), new Vector3I(6, 0, 5),
            new Vector3I(7, 0, 1), new Vector3I(7, 0, 2), new Vector3I(7, 0, 3), new Vector3I(7, 0, 4), new Vector3I(7, 0, 5),
        };

        private readonly string _id;
        private readonly bool _smallBar;
        private TestRig _barRig, _ship;
        private bool _blueprintSet, _stocked;
        private readonly HashSet<Vector3I> _before = new HashSet<Vector3I>();
        private readonly List<IMySlimBlock> _scratch = new List<IMySlimBlock>();
        private int _lastNew;
        private double _lastChangeAt;

        public S114_SmallGridProjection(string id, bool smallBar)
        {
            _id = id;
            _smallBar = smallBar;
        }

        public override string Id { get { return _id; } }
        public override string Description
        {
            get
            {
                return _smallBar
                    ? "Small-grid projection built by a small BaR on the same small ship"
                    : "Small-grid projection on a small ship built by a large BaR nearby";
            }
        }
        public override int TimeoutSeconds { get { return 120; } }

        public override void Setup(TestContext ctx)
        {
            _blueprintSet = _stocked = false;
            _before.Clear();
            _lastNew = 0;
            _lastChangeAt = 0;
            ctx.SetModSettings(s => { s.Welder.WorkSpeed = 10; });
            if (_smallBar)
            {
                _ship = ctx.SpawnRig(new RigSpec { Name = _id + "-ship", SmallGrid = true, Bars = 1, Projector = true });
                _barRig = _ship;
            }
            else
            {
                _barRig = ctx.SpawnRig(new RigSpec { Name = _id, Bars = 1 });
                _ship = ctx.SpawnRig(new RigSpec { Name = _id + "-ship", SmallGrid = true, Bars = 0, Projector = true, Offset = new Vector3D(0, 0, -12) });
            }
        }

        protected override void Configure(TestContext ctx)
        {
            if (!_smallBar) ctx.StockCargo(_barRig, "SteelPlate", Stock);
            ctx.ConfigureBars(_barRig, s =>
            {
                s.SearchMode = _smallBar ? SearchModes.Grids : SearchModes.BoundingBox;
                s.WorkMode = WorkModes.WeldBeforeGrind;
                s.WeldOptions = AutoWeldOptions.WeldFull;
                s.Flags = s.Flags | SyncBlockSettings.Settings.AllowBuild;
            });
        }

        public override bool IsReady(TestContext ctx)
        {
            if (!base.IsReady(ctx)) return false;
            if (_smallBar && !_stocked)
            {
                var bar = _barRig.Bars.Count > 0 ? _barRig.Bars[0] : null;
                var inv = bar != null && bar.Welder != null ? bar.Welder.GetInventory(0) : null;
                if (inv == null) return false;
                inv.AddItems((MyFixedPoint)Stock, new MyObjectBuilder_Component { SubtypeName = "SteelPlate" });
                _stocked = true;
            }
            var projector = _ship.Projector;
            if (projector == null) return false;
            if (!_blueprintSet)
            {
                projector.SetProjectedGrid(BuildBlueprint());
                _blueprintSet = true;
                return false;
            }
            if (!projector.IsProjecting || projector.BuildableBlocksCount <= 0) return false;
            _before.Clear();
            _scratch.Clear();
            _ship.Grid.GetBlocks(_scratch);
            for (int i = 0; i < _scratch.Count; i++) _before.Add(_scratch[i].Position);
            _scratch.Clear();
            return true;
        }

        /// <summary>Small-grid blueprint: the projector at the origin plus small armor at Cells (ship coordinates).</summary>
        private static MyObjectBuilder_CubeGrid BuildBlueprint()
        {
            var grid = MyObjectBuilderSerializer.CreateNewObject<MyObjectBuilder_CubeGrid>();
            grid.GridSizeEnum = MyCubeSize.Small;
            grid.PersistentFlags = MyPersistentEntityFlags2.InScene;
            grid.PositionAndOrientation = new MyPositionAndOrientation(Vector3D.Zero, Vector3.Forward, Vector3.Up);
            grid.DisplayName = "NanobotTest-small-blueprint";
            grid.CubeBlocks = new List<MyObjectBuilder_CubeBlock>();
            var projector = MyObjectBuilderSerializer.CreateNewObject<MyObjectBuilder_Projector>("SmallProjector");
            projector.Min = Vector3I.Zero;
            projector.BlockOrientation = new SerializableBlockOrientation(Base6Directions.Direction.Forward, Base6Directions.Direction.Up);
            grid.CubeBlocks.Add(projector);
            for (int i = 0; i < Cells.Length; i++)
            {
                var block = MyObjectBuilderSerializer.CreateNewObject<MyObjectBuilder_CubeBlock>("SmallBlockArmorBlock");
                block.Min = Cells[i] - RigBuilder.SmallProjectorPos;
                block.BlockOrientation = new SerializableBlockOrientation(Base6Directions.Direction.Forward, Base6Directions.Direction.Up);
                block.ColorMaskHSV = new SerializableVector3(0f, -1f, 0f);
                grid.CubeBlocks.Add(block);
            }
            return grid;
        }

        private List<IMySlimBlock> NewBlocks()
        {
            var result = new List<IMySlimBlock>();
            if (_ship.Grid == null) return result;
            _scratch.Clear();
            _ship.Grid.GetBlocks(_scratch);
            for (int i = 0; i < _scratch.Count; i++)
                if (!_before.Contains(_scratch[i].Position)) result.Add(_scratch[i]);
            _scratch.Clear();
            return result;
        }

        public override void Sample(TestContext ctx)
        {
            if ((TestContext.Frame % 30) != 0) return;
            var n = NewBlocks().Count;
            if (n != _lastNew) { _lastNew = n; _lastChangeAt = ctx.RunSeconds; }
        }

        public override bool IsComplete(TestContext ctx)
        {
            var created = NewBlocks();
            if (created.Count == Cells.Length && ctx.CountFullIntegrity(created) == created.Count) return true;
            return ctx.RunSeconds - _lastChangeAt >= StallSeconds && ctx.RunSeconds >= 30.0;
        }

        public override void Assert(TestContext ctx, TestResult result)
        {
            var created = NewBlocks();
            var welded = ctx.CountFullIntegrity(created);
            ctx.Metric("built", (long)created.Count);
            ctx.Metric("welded", (long)welded);
            if (_ship.Projector != null)
            {
                ctx.Metric("projectorTotal", (long)_ship.Projector.TotalBlocks);
                ctx.Metric("projectorRemaining", (long)_ship.Projector.RemainingBlocks);
                ctx.Metric("projectorBuildable", (long)_ship.Projector.BuildableBlocksCount);
            }
            var onTarget = 0;
            for (int i = 0; i < created.Count; i++) if (Array.IndexOf(Cells, created[i].Position) >= 0) onTarget++;
            ctx.Metric("builtOnExpectedCells", (long)onTarget);
            var bar = _barRig.Bars.Count > 0 ? _barRig.Bars[0] : null;
            if (bar != null)
            {
                ctx.Metric("barWeldTargets", (long)bar.State.PossibleWeldTargets.CurrentCount);
                ctx.Metric("barProjectedSkipped", (long)bar.State.ProjectedBlocksSkipped);
                ctx.Metric("barLimitsExceeded", bar.State.LimitsExceeded);
            }

            ctx.Check(created.Count == Cells.Length, "built=" + created.Count + "/" + Cells.Length + " small projected blocks");
            ctx.Check(welded == created.Count, "welded=" + welded + "/" + created.Count);
            ctx.Check(onTarget == created.Count, "projection offset: " + (created.Count - onTarget) + " block(s) built off the expected cells");

            var perBlock = ScenarioUtil.ComponentsPerBlock("MyObjectBuilder_CubeBlock", "SmallBlockArmorBlock", "SteelPlate");
            var expected = Stock - welded * perBlock;
            var actual = ctx.CountComponentOnRig(_barRig, "SteelPlate");
            ctx.Metric("steelExpected", (long)expected);
            ctx.Metric("steelOnRig", actual);
            ctx.Check(Math.Abs(actual - expected) < 0.5, "SteelPlate " + actual + ", expected " + expected);
        }
    }
}
