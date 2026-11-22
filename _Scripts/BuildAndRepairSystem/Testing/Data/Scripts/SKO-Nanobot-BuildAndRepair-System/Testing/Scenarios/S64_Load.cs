using System.Collections.Generic;
using VRageMath;

namespace SKONanobotBuildAndRepairSystem.Testing.Scenarios
{
    internal enum LoadWork { Weld, Grind, Mixed }

    /// <summary>
    /// SCN-BUDGET-008: many BaRs on several target ships of different sizes. Targets are dynamic grids in a row
    /// along X — small 25 / medium 150 / large 600 blocks (own, damaged, at y 0; derelicts, intact, at y 0 for
    /// grind or y +20 for mixed). BaR rigs (stations) stand on two lines in front of and behind the row (z -30 /
    /// +80, y 0 / -20), placed so every BaR's 200 m area covers every target. Large clusters = few rigs with many
    /// BaRs; small clusters = many rigs with 2 BaRs. Runs 75 s (or until all work is done) and records throughput
    /// per size, BaR utilisation, cluster count and the per-tick cost.
    /// </summary>
    internal sealed class S64_Load : TestScenarioBase
    {
        private const double Window = 75.0;
        private const double DerelictY = 20.0;
        private static readonly string[] SizeName = { "S", "M", "L" };
        private static readonly int[] SizeBlocks = { 25, 150, 600 };
        private static readonly int[] SizeRow = { 5, 15, 30 };
        private static readonly double[] SizeX = { 57.5, 10.0, -75.0 };

        // Rig slots: x0 keeps every BaR of the rig within x -30..25 (reach -75..70 with a 200 m area).
        private static readonly Vector3D[] LargeSlots =
        {
            new Vector3D(-27.5, 0, -30), new Vector3D(-27.5, 0, 80), new Vector3D(-27.5, -20, -30), new Vector3D(-27.5, -20, 80)
        };
        private static readonly double[] SmallX = { -27.5, -2.5, 20.0 };

        private static readonly string[] LoadPerfMethods =
        {
            "ServerDoWeld", "ServerTryWelding", "ServerDoGrind", "ServerTryGrinding",
            "ServerTryWeldingGrindingCollecting", "UpdateBeforeSimulation10_100",
            "AsyncClusterScan", "ApplyClusterResultToSelf", "RebuildMissingComponentsAggregate"
        };

        private readonly string _id;
        private readonly string _label;
        private readonly LoadWork _work;
        private readonly int _rigCount;
        private readonly int _barsPerRig;

        private readonly List<TestRig> _rigs = new List<TestRig>();
        private readonly TestRig[] _own = new TestRig[3];
        private readonly TestRig[] _derelict = new TestRig[3];
        private readonly double[] _ownStart = new double[3];
        private bool[] _worked;
        private bool _started;
        private int _activePeak;

        public S64_Load(string id, string label, LoadWork work, int rigs, int barsPerRig)
        {
            _id = id;
            _label = label;
            _work = work;
            _rigCount = rigs;
            _barsPerRig = barsPerRig;
        }

        public override string Id { get { return _id; } }
        public override string Description
        {
            get
            {
                return string.Format("Load {0}: {1} BaRs ({2} x {3}, {4}) on ships of 25/150/600 blocks", _work,
                    _rigCount * _barsPerRig, _rigCount, _barsPerRig, _label);
            }
        }
        public override int TimeoutSeconds { get { return 150; } }
        public override int ReadyTimeoutSeconds { get { return 90; } }
        public override IList<string> PerfMethods { get { return LoadPerfMethods; } }

        private bool HasWeld { get { return _work != LoadWork.Grind; } }
        private bool HasGrind { get { return _work != LoadWork.Weld; } }

        private Vector3D Slot(int i)
        {
            if (_barsPerRig > 2) return LargeSlots[i % LargeSlots.Length];
            // 2 z-lines x 2 levels x 3 x-positions = 12 small slots
            var x = SmallX[i % SmallX.Length];
            var z = (i / SmallX.Length) % 2 == 0 ? -30.0 : 80.0;
            var y = (i / (SmallX.Length * 2)) % 2 == 0 ? 0.0 : -20.0;
            return new Vector3D(x, y, z);
        }

        public override void Setup(TestContext ctx)
        {
            _rigs.Clear();
            _started = false;
            _activePeak = 0;
            for (int i = 0; i < 3; i++) { _own[i] = null; _derelict[i] = null; }
            ctx.SetModSettings(s =>
            {
                s.Welder.WorkSpeed = 10;
                s.Welder.AllowedGrindJanitorRelations |= AutoGrindRelation.NoOwnership;
            });

            for (int i = 0; i < 3; i++)
            {
                if (HasWeld)
                    _own[i] = ctx.SpawnRig(new RigSpec
                    {
                        Name = _id + "-own-" + SizeName[i], Bars = 0, Dynamic = true,
                        DamagedArmor = SizeBlocks[i], RowWidth = SizeRow[i],
                        Offset = new Vector3D(SizeX[i], 0, 0)
                    });
                if (HasGrind)
                    _derelict[i] = ctx.SpawnRig(new RigSpec
                    {
                        Name = _id + "-derelict-" + SizeName[i], Bars = 0, Dynamic = true, OwnerId = 0,
                        DamagedArmor = SizeBlocks[i], DamagedBuildPercent = 1f, RowWidth = SizeRow[i],
                        Offset = new Vector3D(SizeX[i], _work == LoadWork.Mixed ? DerelictY : 0, 0)
                    });
            }
            for (int r = 0; r < _rigCount; r++)
                _rigs.Add(ctx.SpawnRig(new RigSpec { Name = _id + "-bars-" + r, Bars = _barsPerRig, Offset = Slot(r) }));
            _worked = new bool[_rigCount * _barsPerRig];
        }

        protected override void Configure(TestContext ctx)
        {
            for (int r = 0; r < _rigs.Count; r++)
            {
                if (HasWeld) ctx.StockAllCargo(_rigs[r], "SteelPlate", 1000);
                ctx.ConfigureBars(_rigs[r], s =>
                {
                    s.SearchMode = SearchModes.BoundingBox;
                    s.WorkMode = _work == LoadWork.Grind ? WorkModes.GrindOnly : WorkModes.WeldBeforeGrind;
                    s.WeldOptions = AutoWeldOptions.WeldFull;
                    s.UseGrindJanitorOn = HasGrind ? AutoGrindRelation.NoOwnership : 0;
                    s.GrindJanitorOptions = 0;
                    s.AreaOffset = Vector3.Zero;
                    s.AreaSize = new Vector3(200f, 200f, 200f);
                });
            }
        }

        public override void Sample(TestContext ctx)
        {
            if (!_started)
            {
                _started = true;
                for (int i = 0; i < 3; i++) _ownStart[i] = _own[i] != null ? ctx.IntegritySum(_own[i].Targets) : 0;
            }
            var active = 0;
            var n = 0;
            for (int r = 0; r < _rigs.Count; r++)
            {
                var bars = _rigs[r].Bars;
                for (int b = 0; b < bars.Count; b++, n++)
                {
                    var st = bars[b].State;
                    if (st.CurrentWeldingBlock == null && st.CurrentGrindingBlock == null) continue;
                    active++;
                    if (n < _worked.Length) _worked[n] = true;
                }
            }
            if (active > _activePeak) _activePeak = active;
        }

        private bool OwnDone(TestContext ctx, int i)
        {
            return _own[i] == null || ctx.CountFullIntegrity(_own[i].Targets) == _own[i].Targets.Count;
        }

        private bool DerelictGone(TestContext ctx, int i)
        {
            return _derelict[i] == null || ScenarioUtil.AllGone(ctx, _derelict[i], _derelict[i].Targets);
        }

        public override bool IsComplete(TestContext ctx)
        {
            if (ctx.RunSeconds >= Window) return true;
            for (int i = 0; i < 3; i++) if (!OwnDone(ctx, i) || !DerelictGone(ctx, i)) return false;
            return true;
        }

        public override void Assert(TestContext ctx, TestResult result)
        {
            var seconds = ctx.RunSeconds > 1 ? ctx.RunSeconds : 1.0;
            var barCount = _worked.Length;
            var worked = 0;
            for (int i = 0; i < _worked.Length; i++) if (_worked[i]) worked++;
            var clusters = new HashSet<object>();
            for (int r = 0; r < _rigs.Count; r++)
                for (int b = 0; b < _rigs[r].Bars.Count; b++)
                {
                    var c = _rigs[r].Bars[b].AssignedCluster;
                    if (c != null) clusters.Add(c);
                }

            ctx.Metric("work", _work.ToString());
            ctx.Metric("bars", (long)barCount);
            ctx.Metric("rigs", (long)_rigs.Count);
            ctx.Metric("clusters", (long)clusters.Count);
            ctx.Metric("barsWorked", (long)worked);
            ctx.Metric("barsWorkedPct", barCount > 0 ? (double)worked / barCount : 0);
            ctx.Metric("activePeak", (long)_activePeak);

            var weldedTotal = 0;
            var groundTotal = 0;
            for (int i = 0; i < 3; i++)
            {
                if (_own[i] != null)
                {
                    var t = _own[i].Targets;
                    var welded = ctx.CountFullIntegrity(t);
                    var gain = ctx.IntegritySum(t) - _ownStart[i];
                    weldedTotal += welded;
                    ctx.Metric("welded" + SizeName[i], (long)welded);
                    ctx.Check(gain > 0.01, "own ship " + SizeName[i] + " (" + t.Count + " blocks) got no welding");
                }
                if (_derelict[i] != null)
                {
                    var t = _derelict[i].Targets;
                    var left = ScenarioUtil.AllGone(ctx, _derelict[i], t) ? 0 : ctx.CountExisting(t);
                    var ground = t.Count - left;
                    groundTotal += ground;
                    ctx.Metric("ground" + SizeName[i], (long)ground);
                    ctx.Check(ground > 0, "derelict ship " + SizeName[i] + " (" + t.Count + " blocks) got no grinding");
                }
            }
            if (HasWeld) { ctx.Metric("weldedTotal", (long)weldedTotal); ctx.Metric("weldedPerSecond", weldedTotal / seconds); }
            if (HasGrind) { ctx.Metric("groundTotal", (long)groundTotal); ctx.Metric("groundPerSecond", groundTotal / seconds); }

            ctx.Check(barCount == 0 || (double)worked / barCount >= 0.75, "only " + worked + "/" + barCount + " BaRs ever worked (targets in range for all)");
            ctx.Check(ctx.SimMin >= 0.5, "simMin<0.5");
            ctx.Check(ctx.TickAvgMs <= 2.0, "tickAvgMs>2");
            ctx.Check(ctx.TickPeakMs <= 50.0, "tickPeakMs>50");
        }
    }
}
