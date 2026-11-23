using Sandbox.ModAPI;
using Sandbox.ModAPI.Ingame;
using System.Collections.Generic;
using VRage;
using VRage.Game;
using VRage.Game.ModAPI;
using VRageMath;
using IMyInventory = VRage.Game.ModAPI.IMyInventory;
using IMyConveyorSorter = Sandbox.ModAPI.IMyConveyorSorter;
using IMyShipConnector = Sandbox.ModAPI.IMyShipConnector;
using MyItemType = VRage.Game.ModAPI.Ingame.MyItemType;

namespace SKONanobotBuildAndRepairSystem.Testing.Scenarios
{
    /// <summary>
    /// SCN-INV-001 / SCN-BUDGET-006: component logistics at scale. A station with N BaRs (10 / 20 / 50 / 100), its rig
    /// cargo plus ~2 extra containers per BaR; SteelPlate spread thin over every container (need x 1.2) and a few plates in
    /// each BaR's own inventory (BaRs are sources for each other). Static target ships (20 damaged armor blocks each, one
    /// per 5 BaRs, at least 2) in front of the station. Pass: all ship blocks welded, plates conserved (exactly the ships'
    /// missing plates left storage), sim speed floor. Records throughput and the pull / push cost.
    /// Busy variants (S107-S110) add a working base: plate stacks moved between containers every 2 s (assemblers,
    /// refineries, ...), a supply ship that undocks every 8 s (first at 4 s) and redocks ~4 s later, and a sorter-guarded container
    /// section whose sorter is switched off / on and its filter flipped every 6 s. WorkSpeed 3.
    /// </summary>
    internal sealed class S103_ComponentLogistics : TestScenarioBase
    {
        private const double Cell = 2.5;
        private const double Window = 180.0;
        private const int DamagedPerShip = 20;
        private const int PlatesPerBar = 10;
        private static MyItemType Plate { get { return MyItemType.MakeComponent("SteelPlate"); } }
        private static readonly string[] Methods =
        {
            "UpdateBeforeSimulation10_100",
            "ServerTryWeldingGrindingCollecting",
            "ServerTryWelding",
            "ServerDoWeld",
            "ServerFindMissingComponents",
            "PullFromSourcesOnePass",
            "ServerEmptyTransportInventory",
            "ServerTryPushInventory",
            "RebuildMissingComponentsAggregate",
            "AsyncScanForSources",
            "AsyncClusterScan",
            "ApplyClusterResultToSelf"
        };

        private readonly string _id;
        private readonly int _bars;
        private readonly bool _busy;
        private TestRig _supply;
        private bool _connectRequested, _reported;
        private double _connectRequestedAt, _nextMove, _nextDock, _nextSorter;
        private bool _undocked;
        private int _moves, _undocks, _redocks, _sorterFlips, _moveCursor;
        private TestRig _station;
        private readonly List<TestRig> _ships = new List<TestRig>();
        private readonly List<IMySlimBlock> _targets = new List<IMySlimBlock>();
        private bool _stocked;
        private double _need, _stock, _doneAt = -1;
        private int _containers;

        public S103_ComponentLogistics(string id, int bars, bool busy = false)
        {
            _id = id;
            _bars = bars;
            _busy = busy;
        }

        public override string Id { get { return _id; } }
        public override string Description
        {
            get
            {
                return (_busy ? "Logistics, busy base: " : "Logistics: ") + _bars + " BaRs, ~" + (_bars * 5) + " containers, plates spread thin, ships around"
                    + (_busy ? ", moving items, docking supply ship, switching sorter" : "");
            }
        }
        public override int TimeoutSeconds { get { return 240; } }
        public override int ReadyTimeoutSeconds { get { return 120; } }
        public override IList<string> PerfMethods { get { return Methods; } }

        private int ShipCount { get { return System.Math.Max(2, _bars / 5); } }

        public override void Setup(TestContext ctx)
        {
            _ships.Clear();
            _targets.Clear();
            _stocked = false;
            _need = _stock = 0;
            _doneAt = -1;
            _supply = null;
            _connectRequested = _reported = _undocked = false;
            _moves = _undocks = _redocks = _sorterFlips = _moveCursor = 0;
            _nextMove = 2;
            _nextDock = 4;
            _nextSorter = 6;
            // WorkSpeed 3: long enough runs to compare (10 BaRs finished 40 blocks in 3 s at WorkSpeed 10).
            ctx.SetModSettings(s => { s.Welder.WorkSpeed = 3; });

            var station = new RigSpec { Name = _id, Bars = _bars };
            var width = _bars * 2;
            var extra = _bars * 2;
            for (int i = 0; i < extra; i++)
                station.Extra.Add(new RigBlock { Kind = RigBlockKind.Cargo, Position = new Vector3I(i % width, 0, 3 + i / width), Tag = "extra" });
            if (_busy)
            {
                // Sorter-guarded section behind the first extra row, and the supply ship's connector (S38 docking).
                station.Extra.Add(new RigBlock { Kind = RigBlockKind.Sorter, Position = new Vector3I(0, 0, 4), Tag = "sorter" });
                station.Extra.Add(new RigBlock { Kind = RigBlockKind.Cargo, Position = new Vector3I(0, 0, 5), Tag = "extra" });
                station.Extra.Add(new RigBlock { Kind = RigBlockKind.Connector, Position = new Vector3I(1, 0, -1), Tag = "connector" });
            }
            _station = ctx.SpawnRig(station);
            if (_busy)
            {
                var supply = new RigSpec { Name = _id + "-supply", Bars = 0, Dynamic = true, Forward = Vector3.Backward, Offset = new Vector3D(1 * Cell, 0, -2 * Cell) };
                supply.Extra.Add(new RigBlock { Kind = RigBlockKind.Connector, Position = Vector3I.Zero, Tag = "connector" });
                supply.Extra.Add(new RigBlock { Kind = RigBlockKind.Cargo, Position = new Vector3I(0, 0, 1), Tag = "supplycargo" });
                _supply = ctx.SpawnRig(supply);
            }

            var length = width * Cell;
            for (int k = 0; k < ShipCount; k++)
            {
                var x = (k + 0.5) * length / ShipCount - 5 * Cell;
                _ships.Add(ctx.SpawnRig(new RigSpec { Name = _id + "-ship" + k, Bars = 0, DamagedArmor = DamagedPerShip, Offset = new Vector3D(x, 0, -25) }));
            }
        }

        protected override void Configure(TestContext ctx)
        {
            ctx.ConfigureBars(_station, s =>
            {
                s.SearchMode = SearchModes.BoundingBox;
                s.WorkMode = WorkModes.WeldOnly;
                s.WeldOptions = AutoWeldOptions.WeldFull;
                s.AreaSize = new Vector3(100f, 60f, 100f);
            });
        }

        private List<IMyInventory> Containers()
        {
            var list = new List<IMyInventory>();
            for (int i = 0; i < _station.Cargos.Count; i++) list.Add(_station.Cargos[i].GetInventory(0));
            var t = _station.Tag("extra");
            for (int i = 0; i < t.Count; i++)
                if (t[i] != null && t[i].FatBlock != null) list.Add(t[i].FatBlock.GetInventory(0));
            if (_supply != null)
            {
                var sc = _supply.Tag("supplycargo");
                if (sc.Count > 0 && sc[0] != null && sc[0].FatBlock != null) list.Add(sc[0].FatBlock.GetInventory(0));
            }
            return list;
        }

        /// <summary>Plates in every container, every BaR and every BaR transport.</summary>
        private double PlatesInStorage()
        {
            double total = 0;
            var containers = Containers();
            for (int i = 0; i < containers.Count; i++) if (containers[i] != null) total += (double)containers[i].GetItemAmount(Plate);
            for (int b = 0; b < _station.Bars.Count; b++)
            {
                var bar = _station.Bars[b];
                var inv = bar.Welder != null ? bar.Welder.GetInventory(0) : null;
                if (inv != null) total += (double)inv.GetItemAmount(Plate);
                if (bar._TransportInventory != null) total += (double)bar._TransportInventory.GetItemAmount(Plate);
            }
            return total;
        }

        public override bool IsReady(TestContext ctx)
        {
            if (!base.IsReady(ctx)) return false;
            if (_stocked) return true;
            if (_busy)
            {
                var connector = Connector(_supply);
                if (connector == null) return false;
                if (connector.Status != MyShipConnectorStatus.Connected)
                {
                    if (connector.Status == MyShipConnectorStatus.Connectable && !_connectRequested)
                    {
                        connector.Connect();
                        _connectRequested = true;
                        _connectRequestedAt = ctx.RunSecondsSinceSetup;
                    }
                    else if (!_reported && (ctx.RunSecondsSinceSetup > 20.0 || (_connectRequested && ctx.RunSecondsSinceSetup - _connectRequestedAt > 10.0)))
                    {
                        ctx.Check(false, "layout: supply ship connectors did not lock (status " + connector.Status + ")");
                        _reported = true;
                    }
                    return false;
                }
            }
            for (int k = 0; k < _ships.Count; k++) _targets.AddRange(_ships[k].Targets);
            if (_targets.Count == 0) return false;

            var missing = new Dictionary<string, int>();
            for (int i = 0; i < _targets.Count; i++) _targets[i].GetMissingComponents(missing);
            int plates;
            missing.TryGetValue("SteelPlate", out plates);
            _need = plates;

            var containers = Containers();
            _containers = containers.Count;
            var perContainer = (int)System.Math.Ceiling(_need * 1.2 / System.Math.Max(1, containers.Count));
            for (int i = 0; i < containers.Count; i++)
                if (containers[i] != null) containers[i].AddItems((MyFixedPoint)perContainer, new MyObjectBuilder_Component { SubtypeName = "SteelPlate" });
            for (int b = 0; b < _station.Bars.Count; b++)
            {
                var inv = _station.Bars[b].Welder != null ? _station.Bars[b].Welder.GetInventory(0) : null;
                if (inv != null) inv.AddItems((MyFixedPoint)PlatesPerBar, new MyObjectBuilder_Component { SubtypeName = "SteelPlate" });
            }
            _stock = PlatesInStorage();
            _stocked = true;
            return true;
        }

        private static IMyShipConnector Connector(TestRig rig)
        {
            if (rig == null) return null;
            var t = rig.Tag("connector");
            return t.Count > 0 && t[0] != null ? t[0].FatBlock as IMyShipConnector : null;
        }

        public override void Sample(TestContext ctx)
        {
            if (_doneAt >= 0 || (TestContext.Frame % 60) != 0) return;
            if (_busy) Churn(ctx);
            if (ctx.CountFullIntegrity(_targets) == _targets.Count) _doneAt = ctx.RunSeconds;
        }

        /// <summary>The "working base": item traffic, docking and sorter changes (conservation-neutral).</summary>
        private void Churn(TestContext ctx)
        {
            var now = ctx.RunSeconds;
            if (now >= _nextMove)
            {
                _nextMove = now + 2;
                var containers = Containers();
                var moves = System.Math.Max(2, _bars / 5);
                for (int m = 0; m < moves && containers.Count > 1; m++)
                {
                    var from = containers[_moveCursor % containers.Count];
                    var to = containers[(_moveCursor * 7 + 3) % containers.Count];
                    _moveCursor++;
                    if (from == null || to == null || from == to) continue;
                    if ((double)from.GetItemAmount(Plate) < 5) continue;
                    from.RemoveItemsOfType((MyFixedPoint)5, new MyObjectBuilder_Component { SubtypeName = "SteelPlate" });
                    to.AddItems((MyFixedPoint)5, new MyObjectBuilder_Component { SubtypeName = "SteelPlate" });
                    _moves++;
                }
            }
            var connector = Connector(_supply);
            if (connector != null && now >= _nextDock)
            {
                if (!_undocked && connector.Status == MyShipConnectorStatus.Connected)
                {
                    connector.Disconnect();
                    _undocked = true;
                    _undocks++;
                    _nextDock = now + 4;
                }
                else if (_undocked)
                {
                    if (connector.Status == MyShipConnectorStatus.Connectable)
                    {
                        connector.Connect();
                        _undocked = false;
                        _redocks++;
                        _nextDock = now + 8;
                    }
                    else _nextDock = now + 1;
                }
                else _nextDock = now + 1;
            }
            if (now >= _nextSorter)
            {
                _nextSorter = now + 6;
                var t = _station.Tag("sorter");
                var sorter = t.Count > 0 && t[0] != null ? t[0].FatBlock as IMyConveyorSorter : null;
                if (sorter != null)
                {
                    _sorterFlips++;
                    if ((_sorterFlips % 2) == 1) sorter.Enabled = !sorter.Enabled;
                    else sorter.SetFilter((_sorterFlips % 4) == 0 ? MyConveyorSorterMode.Whitelist : MyConveyorSorterMode.Blacklist,
                        new List<MyInventoryItemFilter> { new MyInventoryItemFilter(new MyDefinitionId(typeof(MyObjectBuilder_Component), "SteelPlate")) });
                }
            }
        }

        public override bool IsComplete(TestContext ctx)
        {
            return _doneAt >= 0 || ctx.RunSeconds >= Window;
        }

        public override void Assert(TestContext ctx, TestResult result)
        {
            var welded = ctx.CountFullIntegrity(_targets);
            var seconds = _doneAt >= 0 ? _doneAt : ctx.RunSeconds;
            var used = _stock - PlatesInStorage();
            ctx.Metric("bars", (long)_bars);
            ctx.Metric("containers", (long)_containers);
            ctx.Metric("shipBlocks", (long)_targets.Count);
            ctx.Metric("welded", (long)welded);
            ctx.Metric("doneS", _doneAt);
            ctx.Metric("weldedPerSecond", seconds > 0 ? welded / seconds : 0);
            ctx.Metric("platesNeeded", _need);
            ctx.Metric("platesStocked", _stock);
            ctx.Metric("platesUsed", used);
            if (_busy)
            {
                ctx.Metric("itemMoves", (long)_moves);
                ctx.Metric("undocks", (long)_undocks);
                ctx.Metric("redocks", (long)_redocks);
                ctx.Metric("sorterChanges", (long)_sorterFlips);
                ctx.Check(_moves > 0 && _undocks > 0 && _sorterFlips > 0, "busy base did not churn (moves " + _moves + ", undocks " + _undocks + ", sorter " + _sorterFlips + ")");
            }
            ctx.Check(_targets.Count > 0 && _need > 0, "layout: no damaged ship blocks / nothing missing");
            ctx.Check(welded == _targets.Count, "welded " + welded + "/" + _targets.Count + " ship blocks in " + Window + " s");
            if (welded == _targets.Count)
                ctx.Check(System.Math.Abs(used - _need) < 0.5, "plates not conserved: used " + used + ", the ships were missing " + _need);
            else
                ctx.Check(used <= _need + 0.5, "more plates left storage (" + used + ") than the ships were missing (" + _need + ")");
            var simFloor = _bars <= 20 ? 0.8 : 0.5;
            ctx.Check(ctx.SimMin >= simFloor, "simMin " + ctx.SimMin.ToString("F2") + " < " + simFloor + " with " + _bars + " BaRs");
        }
    }
}
