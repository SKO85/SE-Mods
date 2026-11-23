using Sandbox.ModAPI;
using VRage;
using VRage.Game;
using VRageMath;
using IMyInventory = VRage.Game.ModAPI.IMyInventory;
using MyItemType = VRage.Game.ModAPI.Ingame.MyItemType;

namespace SKONanobotBuildAndRepairSystem.Testing.Scenarios
{
    internal enum SourceKind
    {
        /// <summary>Cargo on a ship docked by connector (S38 docking).</summary>
        DockedShipCargo,
        /// <summary>An assembler's output inventory on the rig.</summary>
        AssemblerOutput
    }

    /// <summary>
    /// SCN-INV-001: components only in a non-cargo source. The rig's own cargo stays empty; 500 SteelPlate sit only in
    /// the source; the BaR must weld 5 damaged blocks from it. Layout check first: the source must be conveyor-connected.
    /// </summary>
    internal sealed class S87_Sources : TestScenarioBase
    {
        private const double Cell = 2.5;
        private const int Stock = 500;
        private const double Window = 40.0;
        private static readonly MyItemType SteelPlate = MyItemType.MakeComponent("SteelPlate");

        private readonly string _id;
        private readonly SourceKind _kind;
        private TestRig _rig;
        private TestRig _ship;
        private bool _connectRequested, _stocked, _reported;
        private double _connectRequestedAt;
        private double _doneAt = -1;
        private IMyInventory _source;
        private int _sourceIndex = -1;

        public S87_Sources(string id, SourceKind kind)
        {
            _id = id;
            _kind = kind;
        }

        public override string Id { get { return _id; } }
        public override string Description
        {
            get
            {
                return _kind == SourceKind.DockedShipCargo
                    ? "Source: components only in a docked ship's cargo are used for welding"
                    : "Source: components only in an assembler's output are used for welding";
            }
        }
        public override int TimeoutSeconds { get { return 90; } }

        public override void Setup(TestContext ctx)
        {
            _connectRequested = _stocked = _reported = false;
            _doneAt = -1;
            _ship = null;
            _source = null;
            _sourceIndex = -1;
            ctx.SetModSettings(s => { s.Welder.WorkSpeed = 10; });

            var rig = new RigSpec { Name = _id, Bars = 1, DamagedArmor = 5 };
            if (_kind == SourceKind.DockedShipCargo)
            {
                rig.Extra.Add(new RigBlock { Kind = RigBlockKind.Connector, Position = new Vector3I(1, 0, -1), Tag = "connector" });
                _rig = ctx.SpawnRig(rig);
                var ship = new RigSpec { Name = _id + "-ship", Bars = 0, Dynamic = true, Forward = Vector3.Backward, Offset = new Vector3D(1 * Cell, 0, -2 * Cell) };
                ship.Extra.Add(new RigBlock { Kind = RigBlockKind.Connector, Position = Vector3I.Zero, Tag = "connector" });
                ship.Extra.Add(new RigBlock { Kind = RigBlockKind.Cargo, Position = new Vector3I(0, 0, 1), Tag = "source" });
                _ship = ctx.SpawnRig(ship);
            }
            else
            {
                // Conveyor ports of the large assembler are only on some faces: three candidates (beside the rig, behind
                // the back cargo row like S84); the first one connected to the BaR is the source.
                rig.Extra.Add(new RigBlock { Kind = RigBlockKind.Assembler, Position = new Vector3I(2, 0, 0), Enabled = false, Tag = "source" });
                rig.Extra.Add(new RigBlock { Kind = RigBlockKind.Assembler, Position = new Vector3I(0, 0, 3), Enabled = false, Tag = "source" });
                rig.Extra.Add(new RigBlock { Kind = RigBlockKind.Assembler, Position = new Vector3I(1, 0, 3), Enabled = false, Tag = "source" });
                _rig = ctx.SpawnRig(rig);
            }
        }

        /// <summary>The source inventory connected to the BaR (assembler: output inventory); null until resolved.</summary>
        private IMyInventory SourceInventory()
        {
            if (_source != null) return _source;
            var bar = _rig.Bars.Count > 0 ? _rig.Bars[0] : null;
            var barInventory = bar != null && bar.Welder != null ? bar.Welder.GetInventory(0) : null;
            if (barInventory == null) return null;
            var t = (_ship ?? _rig).Tag("source");
            for (int i = 0; i < t.Count; i++)
            {
                var block = t[i] != null ? t[i].FatBlock : null;
                if (block == null) continue;
                var inv = _kind == SourceKind.AssemblerOutput ? block.GetInventory(1) : block.GetInventory(0);
                if (inv != null && inv.IsConnectedTo(barInventory))
                {
                    _source = inv;
                    _sourceIndex = i;
                    return inv;
                }
            }
            return null;
        }

        protected override void Configure(TestContext ctx)
        {
            ctx.ConfigureBars(_rig, s =>
            {
                s.SearchMode = SearchModes.Grids;
                s.WorkMode = WorkModes.WeldOnly;
                s.WeldOptions = AutoWeldOptions.WeldFull;
            });
        }

        public override bool IsReady(TestContext ctx)
        {
            if (!base.IsReady(ctx)) return false;
            if (_kind == SourceKind.DockedShipCargo)
            {
                var slim = _ship.Tag("connector").Count > 0 ? _ship.Tag("connector")[0] : null;
                var connector = slim != null ? slim.FatBlock as IMyShipConnector : null;
                if (connector == null) return false;
                if (connector.Status != Sandbox.ModAPI.Ingame.MyShipConnectorStatus.Connected)
                {
                    if (connector.Status == Sandbox.ModAPI.Ingame.MyShipConnectorStatus.Connectable && !_connectRequested)
                    {
                        connector.Connect();
                        _connectRequested = true;
                        _connectRequestedAt = ctx.RunSecondsSinceSetup;
                    }
                    else if (!_reported && (ctx.RunSecondsSinceSetup > 15.0 || (_connectRequested && ctx.RunSecondsSinceSetup - _connectRequestedAt > 10.0)))
                    {
                        ctx.Check(false, "layout: connectors did not lock (status " + connector.Status + ")");
                        _reported = true;
                    }
                    return false;
                }
            }
            var source = SourceInventory();
            if (source == null)
            {
                if (!_reported && ctx.RunSecondsSinceSetup > 20.0)
                {
                    ctx.Check(false, "layout: no " + _kind + " candidate is conveyor-connected to the BaR");
                    _reported = true;
                }
                return false;
            }
            if (!_stocked)
            {
                source.AddItems((MyFixedPoint)Stock, new MyObjectBuilder_Component { SubtypeName = "SteelPlate" });
                _stocked = true;
            }
            return true;
        }

        public override void Sample(TestContext ctx)
        {
            if (_doneAt < 0 && ctx.CountFullIntegrity(_rig.Targets) == _rig.Targets.Count) _doneAt = ctx.RunSeconds;
        }

        public override bool IsComplete(TestContext ctx)
        {
            return _doneAt >= 0 || ctx.RunSeconds >= Window;
        }

        public override void Assert(TestContext ctx, TestResult result)
        {
            var bar = _rig.Bars.Count > 0 ? _rig.Bars[0] : null;
            var barInventory = bar != null && bar.Welder != null ? bar.Welder.GetInventory(0) : null;
            var source = SourceInventory();
            ctx.Check(barInventory != null && source != null, "layout: BaR or source inventory missing");
            if (barInventory == null || source == null) return;

            var connected = source.IsConnectedTo(barInventory);
            var left = (double)source.GetItemAmount(SteelPlate);
            var welded = ctx.CountFullIntegrity(_rig.Targets);
            ctx.Metric("connected", connected ? 1L : 0L);
            ctx.Metric("sourceCandidate", (long)_sourceIndex);
            ctx.Metric("sourceLeft", left);
            ctx.Metric("welded", (long)welded);
            ctx.Metric("doneS", _doneAt);
            ctx.Check(connected, "layout: source not conveyor-connected to the BaR (scenario proves nothing)");
            ctx.Check(welded == _rig.Targets.Count, "welded " + welded + "/" + _rig.Targets.Count + " in " + Window + " s");
            ctx.Check(left < Stock, "no SteelPlate taken from the " + _kind + " source");
        }
    }
}
