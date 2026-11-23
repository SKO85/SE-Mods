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
    /// <summary>Build = normal; the others must build nothing.</summary>
    internal enum ProjectionMode { Build, AllowBuildOff, ZoneForbids, AdminForbids, ZoneForbidsWelding, WeldFunctional, WeldSkeleton }

    /// <summary>
    /// SCN-WELD-002 / SCN-ZONE-001 / SCN-CFG-003: the rig's projector shows 10 armor blocks on top of the rig (two layers over the
    /// cargo/battery row). S03: AllowBuild + PushComponentImmediately (empty block inventory, the
    /// SE 1.211.018 case of BUG-261122.1) — every block built and welded, SteelPlate used exactly
    /// 25 per block (no free and no double creation component). S27: AllowBuild off; S42: a safe zone
    /// without BuildingProjections over the rig; S46: admin AllowBuildFixed with AllowBuildDefault=false — nothing built.
    /// S85 (BUG-261122.19): a safe zone without Welding but with BuildingProjections — all placed, none welded further.
    /// S124 / S125 (SCN-WELD-003): weld option Functional / Skeleton on a projection — all 10 placed; Functional welds
    /// each to functional integrity and stops below full, Skeleton places them and welds nothing further.
    /// New blocks are counted wherever they appear (alignment-independent); their positions are logged.
    /// </summary>
    internal sealed class S03_ProjectedBuild : TestScenarioBase
    {
        private const int Stock = 1000;
        private const double StallSeconds = 15.0;

        // Real grid cells (projector sits at (2,0,1) for a 1-BaR rig): above (-1,0,z) and the back row.
        private static readonly Vector3I[] Cells =
        {
            new Vector3I(-1, 1, 0), new Vector3I(-1, 1, 1), new Vector3I(-1, 1, 2), new Vector3I(0, 1, 2), new Vector3I(1, 1, 2),
            new Vector3I(-1, 2, 0), new Vector3I(-1, 2, 1), new Vector3I(-1, 2, 2), new Vector3I(0, 2, 2), new Vector3I(1, 2, 2)
        };

        private readonly string _id;
        private readonly ProjectionMode _mode;
        private TestRig _rig;
        private bool _blueprintSet;
        private readonly HashSet<Vector3I> _before = new HashSet<Vector3I>();
        private readonly List<IMySlimBlock> _scratch = new List<IMySlimBlock>();
        private int _lastNew;
        private double _lastChangeAt;

        public S03_ProjectedBuild(string id, ProjectionMode mode)
        {
            _id = id;
            _mode = mode;
        }

        private bool Expect { get { return _mode == ProjectionMode.Build || _mode == ProjectionMode.ZoneForbidsWelding || _mode == ProjectionMode.WeldFunctional || _mode == ProjectionMode.WeldSkeleton; } }
        private bool PlaceOnly { get { return _mode == ProjectionMode.ZoneForbidsWelding || _mode == ProjectionMode.WeldSkeleton; } }
        private const double FunctionalSettle = 5.0;
        private double _functionalAt = -1;

        private int CountAtFunctional(List<IMySlimBlock> blocks)
        {
            var n = 0;
            for (int i = 0; i < blocks.Count; i++)
                if (blocks[i] != null && blocks[i].Integrity >= SKONanobotBuildAndRepairSystem.Extensions.SlimBlockExtensions.GetRequiredIntegrity(blocks[i], AutoWeldOptions.WeldFunctional)) n++;
            return n;
        }

        public override string Id { get { return _id; } }
        public override string Description
        {
            get
            {
                switch (_mode)
                {
                    case ProjectionMode.AllowBuildOff: return "Projector with 'build projected' off: nothing is built";
                    case ProjectionMode.ZoneForbids: return "Projector in a safe zone without BuildingProjections: nothing is built";
                    case ProjectionMode.AdminForbids: return "Admin AllowBuildFixed (default off): projections are not built even when enabled";
                    case ProjectionMode.WeldFunctional: return "Weld option Functional on a projection: 10 blocks placed and welded to functional, not full";
                    case ProjectionMode.WeldSkeleton: return "Weld option Skeleton on a projection: 10 blocks placed, not welded further";
                    case ProjectionMode.ZoneForbidsWelding: return "Safe zone without Welding, with BuildingProjections: projections placed, not welded further";
                    default: return "Projector: 10 projected blocks built from an empty block inventory, components charged exactly once";
                }
            }
        }
        public override int TimeoutSeconds { get { return Expect ? 120 : 25; } }

        public override void Setup(TestContext ctx)
        {
            _blueprintSet = false;
            _functionalAt = -1;
            _before.Clear();
            _lastNew = 0;
            _lastChangeAt = 0;
            ctx.SetModSettings(s =>
            {
                s.Welder.WorkSpeed = 10;
                if (_mode == ProjectionMode.AdminForbids)
                {
                    s.Welder.AllowBuildFixed = true;
                    s.Welder.AllowBuildDefault = false;
                }
            });
            _rig = ctx.SpawnRig(new RigSpec { Name = _id, Bars = 1, Projector = true });
            if (_mode == ProjectionMode.ZoneForbids)
                ctx.SpawnSafeZone(ctx.Origin, 60f, VRage.Game.ObjectBuilders.Components.MySafeZoneAction.All & ~VRage.Game.ObjectBuilders.Components.MySafeZoneAction.BuildingProjections);
            if (_mode == ProjectionMode.ZoneForbidsWelding)
                ctx.SpawnSafeZone(ctx.Origin, 60f, VRage.Game.ObjectBuilders.Components.MySafeZoneAction.All & ~VRage.Game.ObjectBuilders.Components.MySafeZoneAction.Welding);
        }

        protected override void Configure(TestContext ctx)
        {
            ctx.StockCargo(_rig, "SteelPlate", Stock);
            ctx.ConfigureBars(_rig, s =>
            {
                s.SearchMode = SearchModes.Grids;
                s.WorkMode = WorkModes.WeldBeforeGrind;
                s.WeldOptions = _mode == ProjectionMode.WeldFunctional ? AutoWeldOptions.WeldFunctional
                    : _mode == ProjectionMode.WeldSkeleton ? AutoWeldOptions.WeldSkeleton
                    : AutoWeldOptions.WeldFull;
                // AdminForbids tries to switch it on: CheckLimits (via SettingsChanged) must force it off.
                s.Flags = _mode == ProjectionMode.AllowBuildOff
                    ? s.Flags & ~SyncBlockSettings.Settings.AllowBuild
                    : s.Flags | SyncBlockSettings.Settings.AllowBuild | SyncBlockSettings.Settings.PushComponentImmediately;
            });
        }

        public override bool IsReady(TestContext ctx)
        {
            if (!base.IsReady(ctx)) return false;
            var projector = _rig.Projector;
            if (projector == null) return false;
            if (!_blueprintSet)
            {
                // Loaded on a background task by the game; ready once it projects.
                projector.SetProjectedGrid(BuildBlueprint(_rig.ProjectorPosition.Value, Cells));
                _blueprintSet = true;
                return false;
            }
            if (!projector.IsProjecting || (Expect && projector.BuildableBlocksCount <= 0)) return false;

            // Baseline for "new blocks": everything on the rig before the BaRs are switched on.
            _before.Clear();
            _scratch.Clear();
            _rig.Grid.GetBlocks(_scratch);
            for (int i = 0; i < _scratch.Count; i++) _before.Add(_scratch[i].Position);
            _scratch.Clear();
            return true;
        }

        /// <summary>Blueprint of the projector at the origin plus armor at the given rig cells (also used by the S00 warm-up).</summary>
        internal static MyObjectBuilder_CubeGrid BuildBlueprint(Vector3I projectorPos, Vector3I[] cells)
        {
            // The projector itself is block 0 at the origin (like a ship blueprint of its own grid), so the
            // projection lines up with the rig: grid cell p shows at blueprint position p - projectorPos.
            // First run without it: the first armor block took the projector's place (9 built, 1 blocked).
            var grid = MyObjectBuilderSerializer.CreateNewObject<MyObjectBuilder_CubeGrid>();
            grid.GridSizeEnum = MyCubeSize.Large;
            grid.PersistentFlags = MyPersistentEntityFlags2.InScene;
            grid.PositionAndOrientation = new MyPositionAndOrientation(Vector3D.Zero, Vector3.Forward, Vector3.Up);
            grid.DisplayName = "NanobotTest-blueprint";
            grid.CubeBlocks = new List<MyObjectBuilder_CubeBlock>();

            var projector = MyObjectBuilderSerializer.CreateNewObject<MyObjectBuilder_Projector>("LargeProjector");
            projector.Min = Vector3I.Zero;
            projector.BlockOrientation = new SerializableBlockOrientation(Base6Directions.Direction.Forward, Base6Directions.Direction.Up);
            grid.CubeBlocks.Add(projector);

            for (int i = 0; i < cells.Length; i++)
            {
                var block = MyObjectBuilderSerializer.CreateNewObject<MyObjectBuilder_CubeBlock>("LargeBlockArmorBlock");
                block.Min = cells[i] - projectorPos;
                block.BlockOrientation = new SerializableBlockOrientation(Base6Directions.Direction.Forward, Base6Directions.Direction.Up);
                block.ColorMaskHSV = new SerializableVector3(0f, -1f, 0f);
                grid.CubeBlocks.Add(block);
            }
            return grid;
        }

        /// <summary>Blocks on the rig that were not there when the BaRs were switched on.</summary>
        private List<IMySlimBlock> NewBlocks()
        {
            var result = new List<IMySlimBlock>();
            var grid = _rig.Grid;
            if (grid == null) return result;
            _scratch.Clear();
            grid.GetBlocks(_scratch);
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
            if (!Expect) return ctx.RunSeconds >= 20.0;
            var created = NewBlocks();
            if (_mode == ProjectionMode.WeldFunctional)
            {
                // All placed and at functional, then a few more seconds to catch welding on to full.
                if (_functionalAt < 0 && created.Count == Cells.Length && CountAtFunctional(created) == created.Count) _functionalAt = ctx.RunSeconds;
                if (_functionalAt >= 0) return ctx.RunSeconds >= _functionalAt + FunctionalSettle;
                return ctx.RunSeconds >= 60.0 || (created.Count > 0 && ctx.RunSeconds - _lastChangeAt >= StallSeconds && ctx.RunSeconds >= 30.0);
            }
            if (PlaceOnly) return created.Count == Cells.Length || (created.Count > 0 && ctx.RunSeconds - _lastChangeAt >= StallSeconds) || ctx.RunSeconds >= 40.0;
            if (created.Count == Cells.Length && ctx.CountFullIntegrity(created) == created.Count) return true;
            // Stalled: something was built, nothing new for a while and all of it welded.
            return created.Count > 0 && ctx.CountFullIntegrity(created) == created.Count && ctx.RunSeconds - _lastChangeAt >= StallSeconds;
        }

        public override void Assert(TestContext ctx, TestResult result)
        {
            var created = NewBlocks();
            var welded = ctx.CountFullIntegrity(created);
            ctx.Metric("built", (long)created.Count);
            ctx.Metric("welded", (long)welded);
            if (_rig.Projector != null)
            {
                ctx.Metric("projectorTotal", (long)_rig.Projector.TotalBlocks);
                ctx.Metric("projectorRemaining", (long)_rig.Projector.RemainingBlocks);
            }
            var at = "";
            for (int i = 0; i < created.Count && i < 4; i++) at += (i > 0 ? " " : "") + created[i].Position.X + "," + created[i].Position.Y + "," + created[i].Position.Z;
            ctx.Metric("builtAt", at);
            var onTarget = 0;
            for (int i = 0; i < created.Count; i++) if (Array.IndexOf(Cells, created[i].Position) >= 0) onTarget++;
            ctx.Metric("builtOnExpectedCells", (long)onTarget);
            // proj.Build requests an instant build; the game grants it in creative / with creative tools (BUG-261122.19).
            var session = Sandbox.ModAPI.MyAPIGateway.Session;
            ctx.Metric("creativeMode", session != null && session.CreativeMode);
            ctx.Metric("creativeRights", session != null && session.HasCreativeRights);

            if (!Expect)
            {
                var allowBuild = _rig.Bars.Count > 0 && (_rig.Bars[0].Settings.Flags & SyncBlockSettings.Settings.AllowBuild) != 0;
                ctx.Metric("allowBuildOnBar", allowBuild);
                ctx.Check(created.Count == 0, "built=" + created.Count + " (" + _mode + ")");
                if (_mode == ProjectionMode.AdminForbids) ctx.Check(!allowBuild, "AllowBuild stayed on despite AllowBuildFixed");
                return;
            }

            ctx.Check(created.Count == Cells.Length, "built=" + created.Count + "/" + Cells.Length);
            if (_mode == ProjectionMode.WeldFunctional)
            {
                var atFunctional = CountAtFunctional(created);
                var plates = ctx.CountComponentOnRig(_rig, "SteelPlate");
                var perFull = ScenarioUtil.ComponentsPerBlock("MyObjectBuilder_CubeBlock", "LargeBlockArmorBlock", "SteelPlate");
                ctx.Metric("atFunctional", (long)atFunctional);
                ctx.Metric("steelOnRig", plates);
                ctx.Metric("steelIfFull", (long)(Stock - created.Count * perFull));
                ctx.Check(atFunctional == created.Count, "at functional integrity=" + atFunctional + "/" + created.Count);
                ctx.Check(welded == 0, "functional mode welded " + welded + " projected block(s) to full integrity");
                ctx.Check(plates > Stock - created.Count * perFull, "functional mode used as many plates as full welding (" + plates + " left)");
                return;
            }
            if (PlaceOnly)
            {
                // Placed, not welded further. The build may move the BaR's ready components into the new block's
                // construction stockpile: plates are conserved (rig + stockpiles, plus the mounted creation plate).
                var steelLeft = ctx.CountComponentOnRig(_rig, "SteelPlate");
                var plateId = new MyDefinitionId(typeof(MyObjectBuilder_Component), "SteelPlate");
                var stockpiled = 0;
                for (int i = 0; i < created.Count; i++) stockpiled += created[i].GetConstructionStockpileItemAmount(plateId);
                ctx.Metric("steelOnRig", steelLeft);
                ctx.Metric("steelInStockpiles", (long)stockpiled);
                ctx.Check(welded == 0, "welded=" + welded + (_mode == ProjectionMode.WeldSkeleton ? " in skeleton mode" : " inside a no-welding safe zone"));
                var sum = steelLeft + stockpiled;
                ctx.Check(Math.Abs(sum - Stock) < 0.5 || Math.Abs(sum + created.Count - Stock) < 0.5,
                    "SteelPlate not conserved: rig " + steelLeft + " + stockpiles " + stockpiled + " (+" + created.Count + " mounted) vs stock " + Stock);
                return;
            }
            ctx.Check(welded == created.Count, "welded=" + welded + "/" + created.Count);
            ctx.Check(onTarget == created.Count, "projection offset: " + (created.Count - onTarget) + " block(s) built off the expected cells (" + at + ")");

            // Every built block holds its full SteelPlate count; the rest must still be on the rig.
            var perBlock = ScenarioUtil.ComponentsPerBlock("MyObjectBuilder_CubeBlock", "LargeBlockArmorBlock", "SteelPlate");
            var expected = Stock - welded * perBlock;
            var actual = ctx.CountComponentOnRig(_rig, "SteelPlate");
            ctx.Metric("steelExpected", (long)expected);
            ctx.Metric("steelOnRig", actual);
            var diff = actual - expected;
            ctx.Check(Math.Abs(diff) < 0.5, "SteelPlate on rig " + actual + ", expected " + expected +
                (diff > 0 ? " (blocks got components for free)" : " (components charged twice)"));

            ctx.CheckMethodMaxMax("ServerDoWeld", 40.0);
            ctx.CheckTickPeak(15.0);
        }
    }
}
