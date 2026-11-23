using Sandbox.Common.ObjectBuilders;
using Sandbox.ModAPI;
using System.Collections.Generic;
using VRage;
using VRage.Game;
using VRage.Game.ModAPI;
using VRage.ObjectBuilders;
using VRageMath;
using IMyInventory = VRage.Game.ModAPI.IMyInventory;
using MyItemType = VRage.Game.ModAPI.Ingame.MyItemType;

namespace SKONanobotBuildAndRepairSystem.Testing.Scenarios
{
    /// <summary>
    /// SCN-WELD-007 / SCN-BUDGET-006: BaRs on a moving arm. Static base: stocked containers, battery, piston; on the piston
    /// head an advanced rotor (passes conveyors), on its head a conveyor-junction hub with BaRs around it (front-face
    /// conveyor port against the hub), base containers beside a junction under the piston, so
    /// components travel base -> piston -> rotor -> hub -> BaR. The piston extends / retracts every 6 s, the rotor turns
    /// (2 RPM), ShowArea toggles every 4 s. A ring of 8 small static grids 15 m out: S111 one BaR welding damaged grids;
    /// S112 three BaRs, half the ring damaged own grids, half unowned derelicts to grind.
    /// </summary>
    internal sealed class S111_MovingArm : TestScenarioBase
    {
        private const double Window = 180.0;
        private const int Ring = 8;
        private const double Radius = 15.0;
        private static MyItemType Plate { get { return MyItemType.MakeComponent("SteelPlate"); } }
        private static readonly string[] Methods =
        {
            "UpdateBeforeSimulation10_100",
            "ServerTryWeldingGrindingCollecting",
            "ServerTryWelding",
            "ServerTryGrinding",
            "ServerDoWeld",
            "ServerDoGrind",
            "ServerFindMissingComponents",
            "PullFromSourcesOnePass",
            "AsyncClusterScan",
            "ApplyClusterResultToSelf",
            "ShowAreaDraw"
        };

        private readonly string _id;
        private readonly int _bars;
        private readonly bool _grind;
        private TestRig _base;
        private readonly List<TestRig> _ring = new List<TestRig>();
        private readonly List<IMySlimBlock> _weldTargets = new List<IMySlimBlock>();
        private readonly List<IMySlimBlock> _grindTargets = new List<IMySlimBlock>();
        private readonly List<long> _barIds = new List<long>();
        private readonly List<NanobotSystem> _arm = new List<NanobotSystem>();

        private int _stage;
        private double _stageAt;
        private IMyCubeGrid _pistonTop, _rotorTop;
        private Sandbox.ModAPI.IMyPistonBase _piston;
        private Sandbox.ModAPI.IMyMotorStator _rotor;
        private bool _reported, _configured, _started;
        private double _plateStart = -1, _nextFlip, _nextToggle, _doneAt = -1;
        private int _reversals, _areaToggles;

        public S111_MovingArm(string id, int bars, bool grind)
        {
            _id = id;
            _bars = bars;
            _grind = grind;
        }

        public override string Id { get { return _id; } }
        public override string Description
        {
            get
            {
                return _grind
                    ? "Moving arm: 3 BaRs on piston + rotor weld and grind a ring of grids, ShowArea toggling"
                    : "Moving arm: 1 BaR on piston + rotor welds a ring of grids via the conveyors, ShowArea toggling";
            }
        }
        public override int TimeoutSeconds { get { return 260; } }
        public override int ReadyTimeoutSeconds { get { return 90; } }
        public override IList<string> PerfMethods { get { return Methods; } }

        public override void Setup(TestContext ctx)
        {
            _ring.Clear();
            _weldTargets.Clear();
            _grindTargets.Clear();
            _barIds.Clear();
            _arm.Clear();
            _stage = 0;
            _pistonTop = _rotorTop = null;
            _piston = null;
            _rotor = null;
            _reported = _configured = _started = false;
            _plateStart = -1;
            _doneAt = -1;
            _reversals = _areaToggles = 0;
            ctx.SetModSettings(s =>
            {
                // WorkSpeed 3, 6 blocks per ring grid: ~20-30 s of work on the moving arm (at 10 / 3 blocks it was 3-5 s).
                s.Welder.WorkSpeed = 3;
                s.Welder.AllowedGrindJanitorRelations |= AutoGrindRelation.NoOwnership;
            });

            var spec = new RigSpec { Name = _id, Bars = 0 };
            // Conveyor junction (ports on all sides) under the piston; containers beside it along X (proven links).
            spec.Extra.Add(new RigBlock { Kind = RigBlockKind.Conveyor, Position = new Vector3I(0, 0, 0), Tag = "junction" });
            for (int x = 1; x <= 4; x++) spec.Extra.Add(new RigBlock { Kind = RigBlockKind.Cargo, Position = new Vector3I(x, 0, 0), Tag = "basecargo" });
            spec.Extra.Add(new RigBlock { Kind = RigBlockKind.Battery, Position = new Vector3I(5, 0, 0), Tag = "battery" });
            spec.Extra.Add(new RigBlock { Kind = RigBlockKind.Piston, Position = new Vector3I(0, 1, 0), Tag = "piston" });
            _base = ctx.SpawnRig(spec);

            // Ring of small grids around the arm axis (above cargo (0,0,0)), at arm height.
            for (int k = 0; k < Ring; k++)
            {
                var angle = k * MathHelper.TwoPi / Ring;
                var offset = new Vector3D(System.Math.Cos(angle) * Radius, 8.0, System.Math.Sin(angle) * Radius);
                var derelict = _grind && (k % 2) == 1;
                var rig = derelict
                    ? new RigSpec { Name = _id + "-ring" + k, Bars = 0, Armor = 6, OwnerId = 0, Offset = offset }
                    : new RigSpec { Name = _id + "-ring" + k, Bars = 0, DamagedArmor = 6, Offset = offset };
                _ring.Add(ctx.SpawnRig(rig));
            }
        }

        protected override void Configure(TestContext ctx)
        {
            var cargo = _base.Tag("basecargo");
            for (int i = 0; i < cargo.Count; i++)
                if (cargo[i] != null && cargo[i].FatBlock != null)
                    cargo[i].FatBlock.GetInventory(0).AddItems((MyFixedPoint)500, new MyObjectBuilder_Component { SubtypeName = "SteelPlate" });
        }

        private static IMySlimBlock AddBlock(TestContext ctx, IMyCubeGrid grid, MyObjectBuilder_CubeBlock ob, Vector3I min, Base6Directions.Direction forward, Base6Directions.Direction up)
        {
            ob.Min = min;
            ob.BlockOrientation = new SerializableBlockOrientation(forward, up);
            ob.Owner = ctx.OwnerId;
            ob.BuiltBy = ctx.OwnerId;
            ob.ColorMaskHSV = new SerializableVector3(0f, -1f, 0f);
            return grid.AddBlock(ob, false);
        }

        /// <summary>
        /// "Add Top Part" makes a partly built head in survival. Finish it like a welder would: its missing components
        /// into the construction stockpile, then weld to full integrity (no player inventory involved).
        /// </summary>
        private static void CompleteBlock(TestContext ctx, IMySlimBlock block)
        {
            if (block == null || block.IsFullIntegrity) return;
            var missing = new Dictionary<string, int>();
            block.GetMissingComponents(missing);
            var temp = new Sandbox.Game.MyInventory(float.MaxValue, float.MaxValue, Vector3.MaxValue, MyInventoryFlags.CanSend);
            foreach (var kv in missing)
                temp.AddItems((MyFixedPoint)kv.Value, new MyObjectBuilder_Component { SubtypeName = kv.Key });
            block.MoveItemsToConstructionStockpile(temp);
            for (int i = 0; i < 200 && !block.IsFullIntegrity; i++)
                block.IncreaseMountLevel(10f, ctx.OwnerId, temp, 1f, false);
        }

        /// <summary>Direction (in the grid's own axes) closest to the world vector from -> to.</summary>
        private static Base6Directions.Direction LocalDirection(IMyCubeGrid grid, Vector3D from, Vector3D to)
        {
            var local = Vector3D.TransformNormal(to - from, MatrixD.Transpose(grid.WorldMatrix));
            return Base6Directions.GetClosestDirection((Vector3)local);
        }

        /// <summary>Any direction perpendicular to d.</summary>
        private static Base6Directions.Direction Perpendicular(Base6Directions.Direction d)
        {
            var axis = Base6Directions.GetAxis(d);
            return axis == Base6Directions.Axis.UpDown ? Base6Directions.Direction.Forward : Base6Directions.Direction.Up;
        }

        private static Vector3I Cross(Vector3I a, Vector3I b)
        {
            return new Vector3I(a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);
        }

        private static Sandbox.ModAPI.Interfaces.ITerminalAction Action(IMyTerminalBlock block, string id)
        {
            var actions = new List<Sandbox.ModAPI.Interfaces.ITerminalAction>();
            block.GetActions(actions);
            for (int i = 0; i < actions.Count; i++) if (actions[i].Id == id) return actions[i];
            return null;
        }

        private bool Fail(TestContext ctx, string message)
        {
            if (!_reported) ctx.Check(false, "layout: " + message);
            _reported = true;
            return false;
        }

        /// <summary>Builds the arm step by step: piston head, rotor, rotor head, hub + BaRs, then waits for the BaR systems.</summary>
        public override bool IsReady(TestContext ctx)
        {
            if (!base.IsReady(ctx)) return false;
            if (_reported) return false;
            var since = ctx.RunSecondsSinceSetup;
            switch (_stage)
            {
                case 0:
                    var ps = _base.Tag("piston");
                    _piston = ps.Count > 0 && ps[0] != null ? ps[0].FatBlock as Sandbox.ModAPI.IMyPistonBase : null;
                    if (_piston == null) return false;
                    if (_piston.TopGrid == null)
                    {
                        var add = Action(_piston, "Add Top Part");
                        if (add == null) return Fail(ctx, "piston has no 'Add Top Part' action");
                        add.Apply(_piston);
                    }
                    _stage = 1;
                    _stageAt = since;
                    return false;
                case 1:
                    if (_piston.TopGrid == null || _piston.Top == null) return since - _stageAt > 10 ? Fail(ctx, "no piston head") : false;
                    _pistonTop = _piston.TopGrid;
                    ctx.TrackSpawned(_pistonTop);
                    var head = _piston.Top;
                    CompleteBlock(ctx, head.SlimBlock);
                    if (!head.SlimBlock.IsFullIntegrity) return Fail(ctx, "could not complete the piston head (integrity " + head.SlimBlock.Integrity + "/" + head.SlimBlock.MaxIntegrity + ")");
                    // "Away from the base" from the geometry: a head's own Up can point back at its base.
                    var away = LocalDirection(_pistonTop, _piston.GetPosition(), _pistonTop.GridIntegerToWorld(head.Position));
                    // Advanced rotor: only it passes conveyors (a regular rotor does not). 1x2x1 along "away".
                    var statorOb = MyObjectBuilderSerializer.CreateNewObject<MyObjectBuilder_MotorAdvancedStator>("LargeAdvancedStator");
                    statorOb.Enabled = true;
                    var awayVec = Base6Directions.GetIntVector(away);
                    var stator = AddBlock(ctx, _pistonTop, statorOb, Vector3I.Min(head.Position + awayVec, head.Position + awayVec * 2), Perpendicular(away), away);
                    _rotor = stator != null ? stator.FatBlock as Sandbox.ModAPI.IMyMotorStator : null;
                    if (_rotor == null) return Fail(ctx, "could not place the rotor on the piston head");
                    var addTop = Action(_rotor, "AddRotorTopPart");
                    if (addTop == null) return Fail(ctx, "rotor has no 'AddRotorTopPart' action");
                    addTop.Apply(_rotor);
                    _stage = 2;
                    _stageAt = since;
                    return false;
                case 2:
                    if (_rotor.TopGrid == null || _rotor.Top == null) return since - _stageAt > 10 ? Fail(ctx, "no rotor head") : false;
                    _rotorTop = _rotor.TopGrid;
                    ctx.TrackSpawned(_rotorTop);
                    var rtop = _rotor.Top;
                    CompleteBlock(ctx, rtop.SlimBlock);
                    if (!rtop.SlimBlock.IsFullIntegrity) return Fail(ctx, "could not complete the rotor head (integrity " + rtop.SlimBlock.Integrity + "/" + rtop.SlimBlock.MaxIntegrity + ")");
                    var rAway = LocalDirection(_rotorTop, _rotor.GetPosition(), _rotorTop.GridIntegerToWorld(rtop.Position));
                    var u = Base6Directions.GetIntVector(rAway);
                    // Spacer junction between rotor head and hub: keeps the turning BaRs one block clear of the rotor.
                    var spacer = AddBlock(ctx, _rotorTop, MyObjectBuilderSerializer.CreateNewObject<MyObjectBuilder_Conveyor>("LargeBlockConveyor"), rtop.Position + u, Perpendicular(rAway), rAway);
                    if (spacer == null) return Fail(ctx, "could not place the spacer above the rotor head");
                    var hubPos = rtop.Position + u * 2;
                    var hub = AddBlock(ctx, _rotorTop, MyObjectBuilderSerializer.CreateNewObject<MyObjectBuilder_Conveyor>("LargeBlockConveyor"), hubPos, Perpendicular(rAway), rAway);
                    if (hub == null) return Fail(ctx, "could not place the hub conveyor junction");
                    // BaR model: main (large) conveyor port on its FRONT face (dummy at z = -2.36), small ones on the sides
                    // of the front cell (the left one recessed). Front face against the hub, BaR pointing outward.
                    var perp = Base6Directions.GetIntVector(Perpendicular(rAway));
                    var sideVecs = new[] { perp, Cross(u, perp), -perp };
                    for (int i = 0; i < _bars && i < sideVecs.Length; i++)
                    {
                        var side = sideVecs[i];
                        var barOb = MyObjectBuilderSerializer.CreateNewObject<MyObjectBuilder_ShipWelder>("SELtdLargeNanobotBuildAndRepairSystem");
                        barOb.Enabled = false; // switched on when the run starts
                        var front = hubPos + side;
                        var back = hubPos + side * 2;
                        var bar = AddBlock(ctx, _rotorTop, barOb, Vector3I.Min(front, back), Base6Directions.GetDirection(-side), rAway);
                        if (bar == null || bar.FatBlock == null) return Fail(ctx, "could not place BaR " + i + " on the rotor head");
                        _barIds.Add(bar.FatBlock.EntityId);
                    }
                    _stage = 3;
                    _stageAt = since;
                    return false;
                case 3:
                    _arm.Clear();
                    for (int i = 0; i < _barIds.Count; i++)
                    {
                        NanobotSystem system;
                        // Registered by the mod is enough: "ready" needs the BaR switched on, which happens when the run starts.
                        if (!Mod.NanobotSystems.TryGetValue(_barIds[i], out system) || system == null)
                            return since - _stageAt > 20 ? Fail(ctx, "the mod did not pick up the BaRs on the arm") : false;
                        _arm.Add(system);
                    }
                    if (!_configured)
                    {
                        for (int i = 0; i < _arm.Count; i++)
                            ctx.ConfigureBar(_arm[i], s =>
                            {
                                s.SearchMode = SearchModes.BoundingBox;
                                s.WorkMode = WorkModes.WeldBeforeGrind;
                                s.WeldOptions = AutoWeldOptions.WeldFull;
                                s.UseGrindJanitorOn = AutoGrindRelation.NoOwnership;
                                s.GrindJanitorOptions = 0;
                                s.AreaSize = new Vector3(60f, 60f, 60f); // ring 15 m out + 6-block rows: covered from any arm angle
                            });
                        _configured = true;
                    }
                    for (int k = 0; k < _ring.Count; k++)
                    {
                        _weldTargets.AddRange(_ring[k].Targets);
                        if (_ring[k].Targets.Count == 0 && _ring[k].Grid != null) _ring[k].Grid.GetBlocks(_grindTargets);
                    }
                    _plateStart = BaseCargoPlates();
                    _stage = 4;
                    return true;
            }
            return _stage >= 4;
        }

        private double BaseCargoPlates()
        {
            double total = 0;
            var cargo = _base.Tag("basecargo");
            for (int i = 0; i < cargo.Count; i++)
                if (cargo[i] != null && cargo[i].FatBlock != null) total += (double)cargo[i].FatBlock.GetInventory(0).GetItemAmount(Plate);
            return total;
        }

        private int GrindLeft(TestContext ctx)
        {
            var n = 0;
            for (int i = 0; i < _grindTargets.Count; i++)
            {
                var b = _grindTargets[i];
                if (b != null && b.CubeGrid != null && !b.CubeGrid.Closed && b.CubeGrid.GetCubeBlock(b.Position) != null) n++;
            }
            return n;
        }

        public override void Sample(TestContext ctx)
        {
            if ((TestContext.Frame % 30) != 0) return;
            var now = ctx.RunSeconds;
            if (!_started)
            {
                for (int i = 0; i < _arm.Count; i++) _arm[i].Welder.Enabled = true;
                if (_rotor != null) _rotor.TargetVelocityRPM = 1f; // gentle start, 2 RPM after 5 s
                if (_piston != null) _piston.Velocity = 0.5f;
                _nextFlip = now + 6;
                _nextToggle = now + 4;
                _started = true;
            }
            if (_rotor != null && now >= 5 && _rotor.TargetVelocityRPM < 2f) _rotor.TargetVelocityRPM = 2f;
            if (_piston != null && now >= _nextFlip)
            {
                _piston.Velocity = -_piston.Velocity;
                _reversals++;
                _nextFlip = now + 6;
            }
            if (now >= _nextToggle)
            {
                for (int i = 0; i < _arm.Count; i++)
                    ctx.ConfigureBar(_arm[i], s => s.Flags ^= Models.SyncBlockSettings.Settings.ShowArea);
                _areaToggles++;
                _nextToggle = now + 4;
            }
            if (_doneAt < 0 && ctx.CountFullIntegrity(_weldTargets) == _weldTargets.Count && GrindLeft(ctx) == 0) _doneAt = now;
        }

        public override bool IsComplete(TestContext ctx)
        {
            return _doneAt >= 0 || ctx.RunSeconds >= Window;
        }

        public override void Teardown(TestContext ctx)
        {
            if (_rotor != null) _rotor.TargetVelocityRPM = 0f;
            if (_piston != null) _piston.Velocity = 0f;
            base.Teardown(ctx);
        }

        public override void Assert(TestContext ctx, TestResult result)
        {
            var welded = ctx.CountFullIntegrity(_weldTargets);
            var grindLeft = GrindLeft(ctx);
            var used = _plateStart >= 0 ? _plateStart - BaseCargoPlates() : -1;
            var connected = 0;
            var baseInv = _base.Tag("basecargo").Count > 0 && _base.Tag("basecargo")[0].FatBlock != null ? _base.Tag("basecargo")[0].FatBlock.GetInventory(0) : null;
            for (int i = 0; i < _arm.Count; i++)
            {
                var inv = _arm[i].Welder != null ? _arm[i].Welder.GetInventory(0) : null;
                if (inv != null && baseInv != null && inv.IsConnectedTo(baseInv)) connected++;
            }
            ctx.Metric("pistonHeadAttached", _piston != null && _piston.IsAttached);
            ctx.Metric("rotorHeadAttached", _rotor != null && _rotor.IsAttached);
            var powered = 0;
            for (int i = 0; i < _arm.Count; i++) if (_arm[i].Welder != null && _arm[i].Welder.IsWorking) powered++;
            ctx.Metric("armBarsWorking", (long)powered);
            ctx.Metric("armBars", (long)_arm.Count);
            ctx.Metric("armBarsConnectedToBase", (long)connected);
            ctx.Metric("weldTargets", (long)_weldTargets.Count);
            ctx.Metric("welded", (long)welded);
            ctx.Metric("grindTargets", (long)_grindTargets.Count);
            ctx.Metric("grindLeft", (long)grindLeft);
            ctx.Metric("doneS", _doneAt);
            ctx.Metric("basePlatesUsed", used);
            ctx.Metric("pistonReversals", (long)_reversals);
            ctx.Metric("showAreaToggles", (long)_areaToggles);
            ctx.Check(_arm.Count == _bars, "layout: " + _arm.Count + "/" + _bars + " BaRs on the arm");
            ctx.Check(powered == _arm.Count, "layout: only " + powered + "/" + _arm.Count + " arm BaRs are working (power through piston / rotor?)");
            ctx.Check(connected == _arm.Count, "layout: only " + connected + "/" + _arm.Count + " arm BaRs are conveyor-connected to the base (piston / rotor / hub path)");
            ctx.Check(welded == _weldTargets.Count, "welded " + welded + "/" + _weldTargets.Count + " in " + Window + " s");
            if (_grind) ctx.Check(grindLeft == 0, grindLeft + " derelict blocks left");
            ctx.Check(used > 0, "no plates left the base containers (components did not come through piston and rotor)");
            ctx.Check(_reversals > 0 && _areaToggles > 0, "the arm did not move / ShowArea did not toggle");
            ctx.Check(ctx.SimMin >= 0.8, "simMin " + ctx.SimMin.ToString("F2") + " < 0.8");
            ctx.Check(ctx.TickAvgMs <= 1.0, "tickAvgMs " + ctx.TickAvgMs.ToString("F3") + " > 1.0");
        }
    }
}
