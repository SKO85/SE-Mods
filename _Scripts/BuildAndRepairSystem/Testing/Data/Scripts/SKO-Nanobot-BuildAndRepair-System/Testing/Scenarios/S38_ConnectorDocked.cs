using Sandbox.ModAPI;
using VRageMath;

namespace SKONanobotBuildAndRepairSystem.Testing.Scenarios
{
    /// <summary>
    /// SCN-WELD-007 (docked): a small ship docks to the rig by connector (locked through the game's
    /// Connect()); its 3 damaged armor blocks are reached through the connector in Grids mode and welded.
    /// Rig connector at grid (1,0,-1) faces world -Z; the ship is turned around (its Forward = world +Z)
    /// with its connector one large cell further out, so both connector faces touch.
    /// </summary>
    internal sealed class S38_ConnectorDocked : TestScenarioBase
    {
        private const double Cell = 2.5;

        private TestRig _rig;
        private TestRig _ship;
        private bool _connectRequested;
        private double _connectRequestedAt;
        private bool _reported;

        public override string Id { get { return "S38"; } }
        public override string Description { get { return "Docked ship (connector): damaged armor on the ship is welded in Grids mode"; } }

        public override void Setup(TestContext ctx)
        {
            _connectRequested = false;
            _reported = false;
            ctx.SetModSettings(s => { s.Welder.WorkSpeed = 10; });

            var rig = new RigSpec { Name = "S38", Bars = 1 };
            rig.Extra.Add(new RigBlock { Kind = RigBlockKind.Connector, Position = new Vector3I(1, 0, -1), Tag = "connector" });
            _rig = ctx.SpawnRig(rig);

            var ship = new RigSpec
            {
                Name = "S38-ship",
                Bars = 0,
                Dynamic = true,
                Forward = Vector3.Backward,
                Offset = new Vector3D(1 * Cell, 0, -2 * Cell)
            };
            ship.Extra.Add(new RigBlock { Kind = RigBlockKind.Connector, Position = Vector3I.Zero, Tag = "connector" });
            for (int z = 1; z <= 3; z++)
                ship.Extra.Add(new RigBlock { Kind = RigBlockKind.Armor, Position = new Vector3I(0, 0, z), BuildPercent = 0.2f, Tag = "armor" });
            _ship = ctx.SpawnRig(ship);
        }

        protected override void Configure(TestContext ctx)
        {
            ctx.StockCargo(_rig, "SteelPlate", 300);
            ctx.ConfigureBars(_rig, s =>
            {
                s.SearchMode = SearchModes.Grids;
                s.WorkMode = WorkModes.WeldBeforeGrind;
                s.WeldOptions = AutoWeldOptions.WeldFull;
            });
        }

        public override bool IsReady(TestContext ctx)
        {
            if (!base.IsReady(ctx)) return false;
            var slim = _ship.Tag("connector").Count > 0 ? _ship.Tag("connector")[0] : null;
            var connector = slim != null ? slim.FatBlock as IMyShipConnector : null;
            if (connector == null) return false;
            if (connector.Status == Sandbox.ModAPI.Ingame.MyShipConnectorStatus.Connected) return true;

            if (connector.Status == Sandbox.ModAPI.Ingame.MyShipConnectorStatus.Connectable && !_connectRequested)
            {
                connector.Connect();
                _connectRequested = true;
                _connectRequestedAt = ctx.RunSecondsSinceSetup;
            }
            else if (!_reported && (ctx.RunSecondsSinceSetup > 15.0 || (_connectRequested && ctx.RunSecondsSinceSetup - _connectRequestedAt > 10.0)))
            {
                ctx.Check(false, "connectors did not lock (status " + connector.Status + ")");
                _reported = true;
            }
            return false;
        }

        public override bool IsComplete(TestContext ctx)
        {
            var armor = _ship.Tag("armor");
            return armor.Count > 0 && ctx.CountFullIntegrity(armor) == armor.Count;
        }

        public override void Assert(TestContext ctx, TestResult result)
        {
            var armor = _ship.Tag("armor");
            var welded = ctx.CountFullIntegrity(armor);
            ctx.Metric("shipArmor", (long)armor.Count);
            ctx.Metric("welded", (long)welded);
            ctx.Check(armor.Count == 3, "ship armor resolved=" + armor.Count + "/3");
            ctx.Check(welded == armor.Count, "docked ship welded=" + welded + "/" + armor.Count);
        }
    }
}
