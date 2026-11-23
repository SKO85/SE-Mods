using SKONanobotBuildAndRepairSystem.Handlers;
using SKONanobotBuildAndRepairSystem.Models;
using System.Collections.Generic;
using VRageMath;

namespace SKONanobotBuildAndRepairSystem.Testing.Scenarios
{
    internal enum RaceMode { GridChurn, SettingsFlip, OnOff }

    /// <summary>
    /// SCN-BUDGET-011, async / timing robustness: things change while background scans and welds are in flight.
    /// S127 GridChurn: 5 BaRs (BoundingBox) repair 3 small target grids; every 3 s for 21 s the oldest grid is deleted and
    /// a new damaged one spawned. S128 SettingsFlip: 10 BaRs, 60 damaged blocks; SearchMode and WorkMode flip every 1 s for
    /// 20 s. S129 OnOff: the same rig, all BaRs switched off / on every 1.5 s for 21 s.
    /// Pass: no mod errors logged, the work still finishes (S127: the grids alive at the end; S128 / S129: all 60),
    /// tick avg <= 1 ms, no block claims left behind.
    /// </summary>
    internal sealed class S127_Races : TestScenarioBase
    {
        private const double ChurnFor = 21.0;
        private const double Window = 120.0;
        private readonly string _id;
        private readonly RaceMode _mode;
        private TestRig _rig;
        private readonly List<TestRig> _targets = new List<TestRig>();
        private double _next;
        private int _flips, _spawned, _closed;
        private double _doneAt = -1;

        public S127_Races(string id, RaceMode mode)
        {
            _id = id;
            _mode = mode;
        }

        public override string Id { get { return _id; } }
        public override string Description
        {
            get
            {
                switch (_mode)
                {
                    case RaceMode.GridChurn: return "Races: target grids deleted and spawned every 3 s while BaRs scan and weld";
                    case RaceMode.SettingsFlip: return "Races: SearchMode / WorkMode flipped every 1 s while 10 BaRs weld";
                    default: return "Races: 10 BaRs switched off / on every 1.5 s while welding";
                }
            }
        }
        public override int TimeoutSeconds { get { return 150; } }

        private TestRig SpawnTarget(TestContext ctx, int n)
        {
            var angle = n * 1.3;
            var offset = new Vector3D(System.Math.Cos(angle) * 30, 0, System.Math.Sin(angle) * 30 - 10);
            var t = ctx.SpawnRig(new RigSpec { Name = _id + "-t" + n, Bars = 0, DamagedArmor = 4, RowWidth = 2, Offset = offset });
            _targets.Add(t);
            _spawned++;
            return t;
        }

        public override void Setup(TestContext ctx)
        {
            _next = 0;
            _flips = _spawned = _closed = 0;
            _doneAt = -1;
            _targets.Clear();
            ctx.SetModSettings(s => { s.Welder.WorkSpeed = 2; });
            if (_mode == RaceMode.GridChurn)
            {
                _rig = ctx.SpawnRig(new RigSpec { Name = _id, Bars = 5 });
                for (int i = 0; i < 3; i++) SpawnTarget(ctx, i);
            }
            else
            {
                _rig = ctx.SpawnRig(new RigSpec { Name = _id, Bars = 10, DamagedArmor = 60, RowWidth = 20 });
            }
        }

        protected override void Configure(TestContext ctx)
        {
            ctx.StockCargo(_rig, "SteelPlate", 3000);
            ctx.ConfigureBars(_rig, s =>
            {
                s.SearchMode = _mode == RaceMode.GridChurn ? SearchModes.BoundingBox : SearchModes.Grids;
                s.WorkMode = WorkModes.WeldBeforeGrind;
                s.AreaSize = new Vector3(100f, 100f, 100f);
            });
        }

        public override void Sample(TestContext ctx)
        {
            var now = ctx.RunSeconds;
            if (now < ChurnFor && now >= _next)
            {
                switch (_mode)
                {
                    case RaceMode.GridChurn:
                        _next = now + 3.0;
                        if (now > 0.5)
                        {
                            // Delete the oldest live target grid, spawn a new one.
                            for (int i = 0; i < _targets.Count; i++)
                            {
                                var g = _targets[i].Grid;
                                if (g != null && !g.Closed && !g.MarkedForClose) { g.Close(); _closed++; break; }
                            }
                            SpawnTarget(ctx, _spawned);
                            ctx.TryResolveRigs();
                        }
                        break;
                    case RaceMode.SettingsFlip:
                        _next = now + 1.0;
                        _flips++;
                        var flip = (_flips % 2) == 1;
                        ctx.ConfigureBars(_rig, s =>
                        {
                            s.SearchMode = flip ? SearchModes.BoundingBox : SearchModes.Grids;
                            s.WorkMode = flip ? WorkModes.WeldOnly : WorkModes.WeldBeforeGrind;
                        });
                        break;
                    default:
                        _next = now + 1.5;
                        _flips++;
                        var on = (_flips % 2) == 0;
                        for (int i = 0; i < _rig.Bars.Count; i++)
                        {
                            var w = _rig.Bars[i] != null ? _rig.Bars[i].Welder as Sandbox.ModAPI.IMyFunctionalBlock : null;
                            if (w != null) w.Enabled = on;
                        }
                        break;
                }
            }
            if (_mode == RaceMode.OnOff && now >= ChurnFor && _flips > 0)
            {
                // Leave them on after the churn.
                for (int i = 0; i < _rig.Bars.Count; i++)
                {
                    var w = _rig.Bars[i] != null ? _rig.Bars[i].Welder as Sandbox.ModAPI.IMyFunctionalBlock : null;
                    if (w != null && !w.Enabled) w.Enabled = true;
                }
            }
            if (_doneAt < 0 && now >= ChurnFor && ctx.TryResolveRigs() && Done(ctx)) _doneAt = now;
        }

        private int Remaining(TestContext ctx)
        {
            if (_mode != RaceMode.GridChurn) return _rig.Targets.Count - ctx.CountFullIntegrity(_rig.Targets);
            var left = 0;
            for (int i = 0; i < _targets.Count; i++)
            {
                var t = _targets[i];
                if (t.Grid == null || t.Grid.Closed || t.Grid.MarkedForClose) continue;
                left += t.Targets.Count - ctx.CountFullIntegrity(t.Targets);
            }
            return left;
        }

        private bool Done(TestContext ctx)
        {
            return Remaining(ctx) == 0;
        }

        public override bool IsComplete(TestContext ctx)
        {
            return (_doneAt >= 0 && ctx.RunSeconds >= _doneAt + 3.0) || ctx.RunSeconds >= Window;
        }

        public override void Assert(TestContext ctx, TestResult result)
        {
            var remaining = Remaining(ctx);
            ctx.Metric("flips", (long)_flips);
            ctx.Metric("gridsSpawned", (long)_spawned);
            ctx.Metric("gridsDeleted", (long)_closed);
            ctx.Metric("remaining", (long)remaining);
            ctx.Metric("doneS", _doneAt);
            ctx.Metric("claimsLeft", (long)BlockSystemAssigningHandler.AssignmentCount);
            ctx.Check(ctx.ErrorsSinceStart == 0, ctx.ErrorsSinceStart + " mod error(s) logged during the race (see the mod log)");
            ctx.Check(remaining == 0, remaining + " block(s) not welded " + (Window - ChurnFor) + " s after the churn stopped");
            ctx.Check(ctx.TickAvgMs <= 1.0, "tick avg " + ctx.TickAvgMs.ToString("F2") + " ms > 1 ms");
            ctx.Check(BlockSystemAssigningHandler.AssignmentCount == 0, BlockSystemAssigningHandler.AssignmentCount + " block claim(s) left 3 s after the work finished");
            if (_mode == RaceMode.GridChurn) ctx.Check(_closed >= 5, "only " + _closed + " grids deleted during the churn");
            else ctx.Check(_flips >= 10, "only " + _flips + " flips");
        }
    }
}
